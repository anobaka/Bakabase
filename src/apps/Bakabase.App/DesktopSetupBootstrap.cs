using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Bakabase.Abstractions.Components.App;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Profiles;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Bakabase.Shell.Windows;
using Bakabase.Shell.Components;

namespace Bakabase.App;

/// <summary>
/// The desktop coordinator owns only a setup window and process supervision. All database
/// work and the main application run in separate authenticated child processes.
/// </summary>
internal sealed class DesktopSetupBootstrap : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private SetupWindow? _window;
    private Task<int>? _running;
    private bool _businessWasReady;
    private volatile bool _businessReady;
    private string? _pendingToolLink;
    private readonly SemaphoreSlim _toolForwarding = new(1);

    private DesktopWorkspaceRunner? _workspaces;
    private CancellationTokenSource? _currentStartup;
    private string CurrentAnchor => _workspaces?.Current.AnchorDirectory ?? AppDataLocator.ResolveAnchor();

    public async Task<bool> RunAsync(string[] args)
    {
        var initialLink = DesktopToolLink.FromArguments(args);
        if (initialLink != null) Interlocked.CompareExchange(ref _pendingToolLink, initialLink, null);
        try { if (await TryActivateExistingAsync(args, activationMessage: _pendingToolLink)) return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        var initial = new DesktopWorkspaceContext(DesktopProfileSelection.CurrentId, AppDataLocator.ResolveAnchor(),
            AppDataLocator.IsEnvironmentOverride, args.Where(arg => DesktopToolLink.GetRoute(arg) == null).ToArray());
        _workspaces = new DesktopWorkspaceRunner(initial);
        _workspaces.RestoredAfterFailure += error => Dispatcher.UIThread.Post(() => EnsureWindow().ShowError(
            "The selected workspace could not be opened. Your previous workspace has been reopened.\n\n" + error.Message));
        using var activation = ActivationServer.Start(DesktopWorkspaceSession.Channel(DesktopProfileRegistry.ForCurrentUser().RootDirectory),
            message => Dispatcher.UIThread.Post(() => HandleDesktopActivation(message)));
        _running = Task.Run(() => _workspaces.RunAsync(RunWorkspaceAsync, _stopping.Token));
        try { Environment.ExitCode = await _running; }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            Console.Error.WriteLine($"Desktop setup coordinator failed: {error}");
            EnsureWindow().ShowError(error.Message);
            try { await _window!.ClosedTask.WaitAsync(_stopping.Token); }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        }
        return false;
    }

    private async Task<int> RunWorkspaceAsync(DesktopWorkspaceContext workspace, CancellationToken token)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        _currentStartup = startup;
        _businessWasReady = false;
        _businessReady = false;
        var options = new SetupCoordinatorOptions
        {
            Addresses = ["http://127.0.0.1:0"], Arguments = workspace.Arguments, IsDesktop = true,
            AnchorDirectory = workspace.AnchorDirectory, EnvironmentOverride = workspace.EnvironmentOverride,
            RecoverStartupFailureExternally = true,
            PrepareWorkspaceSwitch = PrepareWorkspaceSwitch,
            ReserveRestart = release => _workspaces!.ReserveRestart(release),
            BeforeSetup = () => _workspaces!.BeginSetup(),
            SetupRejected = () => { if (_businessReady) _workspaces!.Ready(); },
            OnControlAcquired = () => ActivationServer.Start(SetupActivationChannel(workspace.AnchorDirectory),
                message => Dispatcher.UIThread.Post(() => HandleDesktopActivation(message))),
            OnSetupUrl = ShowSetupAsync,
            OnBusinessUnavailable = () => { _businessReady = false; _workspaces!.Unavailable(); },
            DirectoryPicker = () => _window?.PickDirectoryAsync() ?? Task.FromResult<string?>(null),
            OnBusinessReady = address =>
            {
                _businessReady = true;
                Dispatcher.UIThread.Post(() =>
                {
                    if (startup.IsCancellationRequested || !ReferenceEquals(_currentStartup, startup)) return;
                    _businessWasReady = true;
                    _window?.ContinueStartup(); _window = null;
                    _workspaces!.Ready();
                    _ = ForwardPendingToolLinkAsync();
                });
            }
        };
        try { return await SetupProcessCoordinator.RunAsync(options, startup.Token); }
        finally { _currentStartup = null; }
    }

    private Action? PrepareWorkspaceSwitch(string id, bool commit)
    {
        var runner = _workspaces ?? throw new InvalidOperationException("The desktop workspace coordinator is not ready.");
        if (runner.Current.ProfileId == id) { Dispatcher.UIThread.Post(Reopen); return null; }
        if (commit) return runner.PrepareCommit(id);
        var profile = DesktopProfileRegistry.ForCurrentUser().Get(id);
        var target = AppDataLocator.ResolveEffectiveDataDirectory(profile.AnchorDirectory);
        var args = runner.Current.Arguments.Where(arg => !arg.StartsWith(DesktopProfileSelection.ArgumentPrefix, StringComparison.Ordinal) &&
            !arg.StartsWith(RestartHandoff.PredecessorPidSwitch + "=", StringComparison.Ordinal)).Append(DesktopProfileSelection.ArgumentPrefix + id).ToArray();
        runner.PrepareSwitch(new(id, profile.AnchorDirectory, false, args), () =>
        {
            if (_window?.ClosedTask.IsCompleted == false || ServerSetupSession.Current?.Submitted == true ||
                ServerAppDataImport.ReadPending(AppDataLocator.ResolveEffectiveDataDirectory(CurrentAnchor)) != null ||
                ServerAppDataRelocation.ReadPending(CurrentAnchor) != null)
                throw new InvalidOperationException("Finish or close Setup before switching workspaces.");
            if (!Directory.Exists(target) && !profile.RequiresSetup)
                throw new DirectoryNotFoundException("Reconnect this workspace's storage before opening it.");
            // Permission/lock failures are detected while the original host is still open.
            var claim = DataDirectoryLock.TryAcquire(target);
            using var data = claim.Lock ?? throw new IOException("The selected workspace cannot be opened: " + claim.Status, claim.Error);
            if (IsLocked(Path.Combine(profile.AnchorDirectory, SetupProcessCoordinator.LockFileName)))
                throw new IOException("The selected workspace is already in use. Close its older instance before switching.");
        });
        return null;
    }

    private static bool IsLocked(string path)
    {
        try { using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
    }

    private void HandleDesktopActivation(string message)
    {
        if (DesktopToolLink.GetRoute(message) != null) { AcceptToolLink(message); return; }
        if (message.StartsWith(DesktopWorkspaceSession.SwitchPrefix, StringComparison.Ordinal))
        {
            var id = message[DesktopWorkspaceSession.SwitchPrefix.Length..];
            try { PrepareWorkspaceSwitch(id, false); PrepareWorkspaceSwitch(id, true)?.Invoke(); }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Could not switch workspace: {error.Message}");
                if (_window?.ClosedTask.IsCompleted == false) Reopen();
                else EnsureWindow().ShowError(error.Message);
            }
        }
        else if (message == ActivationChannel.ShowMessage) Reopen();
    }

    internal static async Task<bool> TryActivateExistingAsync(string[] args, string? anchor = null,
        string? activationMessage = null)
    {
        // A replacement is waiting for its predecessor, not asking that predecessor to
        // show a window. Do not consume a requested restart as a duplicate launch.
        if (RestartHandoff.TryReadPredecessorPid(args) != null) return false;
        anchor ??= AppDataLocator.ResolveAnchor();
        var link = DesktopToolLink.GetRoute(activationMessage) != null ? activationMessage : DesktopToolLink.FromArguments(args);
        var message = link ?? ActivationChannel.ShowMessage;
        // The coordinator stays available while setup/maintenance has no business child.
        if (await TryActivateLockOwnerAsync(Path.Combine(anchor, SetupProcessCoordinator.LockFileName),
                SetupActivationChannel(anchor), message)) return true;
        var data = AppDataLocator.ResolveEffectiveDataDirectory(anchor);
        return await TryActivateLockOwnerAsync(Path.Combine(data, DataDirectoryLock.FileName),
            ActivationChannel.GetName(DataDirectoryIdentity.Normalize(data)), message);
    }

    internal static string SetupActivationChannel(string anchor) =>
        ActivationChannel.GetName(DataDirectoryIdentity.Normalize(anchor) + "|setup");

    private static async Task<bool> TryActivateLockOwnerAsync(string lockPath, string channel, string message)
    {
        if (!File.Exists(lockPath)) return false;
        try
        {
            // The marker survives normal exits. A shared, read-only probe avoids waiting
            // for a nonexistent pipe on every cold start without creating or rewriting it.
            using var unowned = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { /* An exclusive owner may have its activation channel ready. */ }
        catch (UnauthorizedAccessException) { return false; }
        return await ActivationChannel.TrySendAsync(channel, message, SingleInstanceGuard.HandOffTimeout);
    }

    private async Task ShowSetupAsync(string url)
    {
        _stopping.Token.ThrowIfCancellationRequested();
        _workspaces?.Busy();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_stopping.IsCancellationRequested) return;
            var window = EnsureWindow();
            window.Navigate(WithLanguage(url));
            window.BringToFront();
        }, DispatcherPriority.Normal, _stopping.Token);
    }

    private SetupWindow EnsureWindow()
    {
        if (_window == null || _window.ClosedTask.IsCompleted)
        {
            var window = new SetupWindow();
            window.Closed += (_, _) =>
            {
                // An established instance may close the setup window while its supervised
                // operation continues. Before first readiness, closing means cancel launch.
                if (!_businessWasReady && window.Cancelled.IsCancellationRequested) _currentStartup?.Cancel();
                else if (_businessReady) _workspaces?.Ready();
            };
            _window = window;
        }
        return _window;
    }

    private static string WithLanguage(string url)
    {
        var builder = new UriBuilder(url);
        var fragment = builder.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(item => !item.StartsWith("lang=", StringComparison.Ordinal));
        builder.Fragment = string.Join('&', fragment.Append("lang=" + SetupWindow.Language));
        return builder.Uri.AbsoluteUri;
    }

    internal void OnActivated(ActivatedEventArgs e)
    {
        // Avalonia uses Background for native application activation (including an
        // accessory app being promoted by Launch Services), separately from Reopen.
        if (e.Kind == ActivationKind.Background)
        {
            MacOSDockVisibility.SetVisible(_window?.ClosedTask.IsCompleted == false);
            return;
        }
        if (e is ProtocolActivatedEventArgs protocol)
        {
            var link = protocol.Uri.OriginalString;
            if (DesktopToolLink.GetRoute(link) == null) return;
            // Tool links always belong to the currently active workspace. They cannot
            // create another desktop or unexpectedly replace the user's current context.
            AcceptToolLink(link);
            return;
        }
        if (e.Kind != ActivationKind.Reopen) return;
        Reopen();
    }

    private void AcceptToolLink(string link)
    {
        if (DesktopToolLink.GetRoute(link) == null) return;
        Interlocked.Exchange(ref _pendingToolLink, link);
        if (_businessReady) _ = ForwardPendingToolLinkAsync();
        if (_window?.ClosedTask.IsCompleted == false) _window.BringToFront();
    }

    private void Reopen()
    {
        if (_window?.ClosedTask.IsCompleted == false)
        {
            _window.BringToFront();
            return;
        }
        // Reopening the app bundle can promote this process to a regular app again.
        // With no Setup window, only the business child should retain a Dock entry.
        MacOSDockVisibility.SetVisible(false);
        // Finder / Dock may reopen the coordinator rather than the UI child. Contact the
        // business owner's existing activation pipe without taking its data-directory lock.
        _ = Task.Run(async () =>
        {
            try
            {
                var data = AppDataLocator.ResolveEffectiveDataDirectory(CurrentAnchor);
                var channel = ActivationChannel.GetName(DataDirectoryIdentity.Normalize(data));
                await ActivationChannel.TrySendAsync(channel, ActivationChannel.ShowMessage, SingleInstanceGuard.HandOffTimeout);
            }
            catch (Exception error) { Console.Error.WriteLine($"Could not activate Bakabase: {error.Message}"); }
        });
    }

    private async Task ForwardPendingToolLinkAsync()
    {
        await _toolForwarding.WaitAsync();
        try
        {
            while (!_stopping.IsCancellationRequested && _businessReady && Volatile.Read(ref _pendingToolLink) is { } link)
            {
                var data = AppDataLocator.ResolveEffectiveDataDirectory(CurrentAnchor);
                var channel = ActivationChannel.GetName(DataDirectoryIdentity.Normalize(data));
                if (!await ActivationChannel.TrySendAsync(channel, link, SingleInstanceGuard.HandOffTimeout))
                    return; // Maintenance may have stopped the child; next readiness retries.
                // A stop may have started while the pipe write was in flight. Keep the
                // intent for the replacement child rather than consuming it in the old UI.
                if (_businessReady) Interlocked.CompareExchange(ref _pendingToolLink, null, link);
            }
        }
        catch (Exception error) { Console.Error.WriteLine($"Could not open a desktop tool: {error.Message}"); }
        finally { _toolForwarding.Release(); }
    }

    public void RequestStop() => _stopping.Cancel();

    public void Dispose()
    {
        _stopping.Cancel();
        // RunAsync's coordinator task itself runs on the pool, so draining it does not
        // depend on the already-stopped Avalonia synchronization context.
        if (_running != null)
        {
            try { _running.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
            catch (Exception error) { Console.Error.WriteLine($"Could not finish setup shutdown: {error.Message}"); }
        }
        _stopping.Dispose();
    }
}

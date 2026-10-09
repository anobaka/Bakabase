using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Bakabase.Abstractions.Components.App;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Bakabase.Shell.Windows;

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

    public async Task<bool> RunAsync(string[] args)
    {
        var initialLink = DesktopToolLink.FromArguments(args);
        if (initialLink != null) Interlocked.CompareExchange(ref _pendingToolLink, initialLink, null);
        try { if (await TryActivateExistingAsync(args, activationMessage: _pendingToolLink)) return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Directory resolution failures belong to the coordinator's normal error
            // window below; the best-effort duplicate check must not bypass that UI.
        }
        var options = new SetupCoordinatorOptions
        {
            Addresses = ["http://127.0.0.1:0"],
            // A navigation is consumed by this coordinator once, never replayed after a
            // maintenance operation or interpreted as host configuration in the child.
            Arguments = args.Where(arg => DesktopToolLink.GetRoute(arg) == null).ToArray(),
            IsDesktop = true,
            OnControlAcquired = () => ActivationServer.Start(SetupActivationChannel(AppDataLocator.ResolveAnchor()),
                message => Dispatcher.UIThread.Post(() =>
                {
                    if (DesktopToolLink.GetRoute(message) != null) AcceptToolLink(message);
                    else if (message == ActivationChannel.ShowMessage) Reopen();
                })),
            OnSetupUrl = ShowSetupAsync,
            OnBusinessUnavailable = () => _businessReady = false,
            DirectoryPicker = () => _window?.PickDirectoryAsync() ?? Task.FromResult<string?>(null),
            OnBusinessReady = address =>
            {
                _businessReady = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _businessWasReady = true;
                    _window?.ContinueStartup();
                    _window = null;
                    _ = ForwardPendingToolLinkAsync();
                });
            }
        };
        // The coordinator must never capture Avalonia's context: Main disposes us after
        // that dispatcher stops. Native callbacks explicitly marshal to the UI thread.
        _running = Task.Run(() => SetupProcessCoordinator.RunAsync(options, _stopping.Token));
        try
        {
            Environment.ExitCode = await _running;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Closing the first-run window cancels setup without creating a library.
        }
        catch (Exception e)
        {
            Environment.ExitCode = 1;
            Console.Error.WriteLine($"Desktop setup coordinator failed: {e}");
            EnsureWindow().ShowError(e.Message);
            try { await _window!.ClosedTask.WaitAsync(_stopping.Token); }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        }
        // This process must never proceed into the shell's host factory / AppService.
        return false;
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
                if (!_businessWasReady && window.Cancelled.IsCancellationRequested) _stopping.Cancel();
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
        if (e is ProtocolActivatedEventArgs protocol)
        {
            AcceptToolLink(protocol.Uri.OriginalString);
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
        // Finder / Dock may reopen the coordinator rather than the UI child. Contact the
        // business owner's existing activation pipe without taking its data-directory lock.
        _ = Task.Run(async () =>
        {
            try
            {
                var data = AppDataLocator.ResolveEffectiveDataDirectory(AppDataLocator.ResolveAnchor());
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
                var data = AppDataLocator.ResolveEffectiveDataDirectory(AppDataLocator.ResolveAnchor());
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

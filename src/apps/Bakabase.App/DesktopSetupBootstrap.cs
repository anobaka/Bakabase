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
    private IActivatableLifetime? _activation;

    public async Task<bool> RunAsync(string[] args)
    {
        try { if (await TryActivateExistingAsync(args)) return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Directory resolution failures belong to the coordinator's normal error
            // window below; the best-effort duplicate check must not bypass that UI.
        }
        _activation = Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
        if (_activation != null) _activation.Activated += OnActivated;
        var options = new SetupCoordinatorOptions
        {
            Addresses = ["http://127.0.0.1:0"],
            Arguments = args,
            IsDesktop = true,
            OnSetupUrl = ShowSetupAsync,
            DirectoryPicker = () => _window?.PickDirectoryAsync() ?? Task.FromResult<string?>(null),
            OnBusinessReady = _ => Dispatcher.UIThread.Post(() =>
            {
                _businessWasReady = true;
                _window?.ContinueStartup();
                _window = null;
            })
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

    internal static async Task<bool> TryActivateExistingAsync(string[] args, string? anchor = null)
    {
        // A replacement is waiting for its predecessor, not asking that predecessor to
        // show a window. Do not consume a requested restart as a duplicate launch.
        if (RestartHandoff.TryReadPredecessorPid(args) != null) return false;
        var data = AppDataLocator.ResolveEffectiveDataDirectory(anchor ?? AppDataLocator.ResolveAnchor());
        var lockPath = Path.Combine(data, DataDirectoryLock.FileName);
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
        var channel = ActivationChannel.GetName(DataDirectoryIdentity.Normalize(data));
        return await ActivationChannel.TrySendAsync(channel, ActivationChannel.ShowMessage,
            SingleInstanceGuard.HandOffTimeout);
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

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Reopen) return;
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

    public void RequestStop() => _stopping.Cancel();

    public void Dispose()
    {
        if (_activation != null) _activation.Activated -= OnActivated;
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

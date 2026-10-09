using Avalonia;
using Bakabase.Abstractions.Components.App;
using Bakabase.Shell.Components;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Service.Components.ServerData;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using Velopack;
// Aliased because this project's own namespace is Bakabase.App: an unqualified `App`
// binds to that namespace rather than to the shell's Avalonia application class.
using ShellApp = Bakabase.Shell.App;

namespace Bakabase.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must be the first thing to run in the app.
        // It handles install/uninstall/update lifecycle hooks.
        //
        // SetAutoApplyOnStartup(false): by default Velopack silently applies an
        // already-downloaded update on the next launch (before any UI shows).
        // We auto-download upgrade packages but want the install itself to be a
        // deliberate user action, so we disable that implicit apply. A staged
        // update then stays as PendingRestart until the user clicks "restart to
        // update", which explicitly schedules Velopack after the shell's graceful exit.
        //
        // SetLogger: this logger is handed to the process-wide VelopackLocator, so every
        // later UpdateManager picks it up and its diagnostics (feed URL, channel, the
        // versions a check compared) land in AppLog instead of only in Velopack's own log
        // file, which nobody opens when an update check is being questioned.
        var velopack = VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .SetLogger(new SerilogVelopackLogger());
        if (OperatingSystem.IsWindows())
            velopack.OnAfterInstallFastCallback(_ => DesktopProtocolRegistration.Register())
                .OnAfterUpdateFastCallback(_ => DesktopProtocolRegistration.Register())
                .OnBeforeUninstallFastCallback(_ => DesktopProtocolRegistration.Unregister());
        velopack.Run();

        // The protocol command starts with a fixed marker, so an untrusted URI can never
        // become Velopack's first argument. Validate it before any child/setup/config parsing.
        if (!DesktopToolLink.TryNormalizeArguments(args, out args)) return;

        // Everything from here on can be reported. Velopack's hook invocations never reach this
        // line (Run ends in Environment.Exit for them), so installing the handler after it costs
        // no coverage of a real launch.
        CrashHandler.Install();

        // Internal child roles are authenticated by the coordinator before any directory
        // guard, AppService or Avalonia initialization can run.
        var connection = SetupChildConnection.ConnectAsync(args).GetAwaiter().GetResult();
        if (connection?.Role == "worker")
        {
            try { Environment.ExitCode = SetupMaintenanceWorker.RunAsync(connection).GetAwaiter().GetResult(); }
            finally { DisposeConnection(connection); }
            return;
        }

        // A restart spawns its replacement before it has stopped itself, and the
        // single-instance guard refuses whoever finds the data directory still locked. Wait
        // here, ahead of everything that reaches that check, for the process that spawned us to
        // be gone. Does nothing on an ordinary launch.
        var restartHandoff = RestartHandoff.WaitForPredecessor(args);

        if (connection == null)
        {
            using var coordinator = new DesktopSetupBootstrap();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; coordinator.RequestStop(); };
            Console.CancelKeyPress += cancel;
            using var sigterm = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
                context => { context.Cancel = true; coordinator.RequestStop(); });
            try { BuildCoordinatorApp(coordinator, args).StartWithClassicDesktopLifetime(args); }
            finally { Console.CancelKeyPress -= cancel; }
            return;
        }

        // One instance per data directory, settled before anything touches the directory:
        // the static constructor below creates it and opens a log file in it, and the shell
        // would go on to run a pending relocation and show a tray icon. A second launch on
        // the same directory asks the running one to show its window and ends here, having
        // written nothing. A launch on a different directory (BAKABASE_DATA_DIR) is its own
        // instance and carries on.
        if (!SingleInstanceGuard.EnterOrHandOff())
        {
            DisposeConnection(connection);
            return;
        }

        try
        {
            BuildBusinessApp(connection, restartHandoff).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                ImportProgressStore.Current?.Dispose();
                ImportProgressStore.Current = null;
                SingleInstanceGuard.ReleaseAll();
            }
            finally { DisposeConnection(connection); }
        }
    }

    // Configure(Func<TApp>) rather than Configure<App>(): the shell takes the host
    // it should run behind as a constructor argument, and picking UnifiedHost here
    // is exactly what makes this build the all-in-one flavour — its own server, plus
    // the relays that let the same window manage other servers.
    public static AppBuilder BuildAvaloniaApp()
        => BuildCoordinatorApp(new DesktopSetupBootstrap(), []);

    private static AppBuilder BuildCoordinatorApp(DesktopSetupBootstrap bootstrap, string[] args)
        => AppBuilder.Configure(() => new ShellApp(
                (_, _) => throw new InvalidOperationException("The setup coordinator cannot start application services."),
                () => bootstrap.RunAsync(args), onActivation: bootstrap.OnActivated))
            .UsePlatformDetect()
            .LogToTrace();

    private static AppBuilder BuildBusinessApp(SetupChildConnection connection, string? restartHandoff)
        => AppBuilder.Configure(() =>
            {
                var app = new ShellApp((guiAdapter, systemService) =>
                    new BakabaseShellHost(new UnifiedHost(guiAdapter, systemService)),
                    () => PrepareBusinessAsync(connection, restartHandoff), connection.Ready, connection.Fatal);
                connection.StopRequested += () => Dispatcher.UIThread.Post(() => app.RequestMaintenanceStop());
                return app;
            })
            .UsePlatformDetect()
            .LogToTrace();

    private static Task<bool> PrepareBusinessAsync(SetupChildConnection connection, string? restartHandoff)
    {
        if (connection.Stopping.IsCancellationRequested) return Task.FromResult(false);
        // Only the business child initializes legacy layout / options. Hold the anchor as
        // well as the effective directory so neither spelling can start another owner.
        var anchor = AppDataLocator.ResolveAnchor();
        if (SingleInstanceGuard.GetHeldLock(anchor) == null) SingleInstanceGuard.AcquireAdditional(anchor);
        if (SingleInstanceGuard.GetHeldLock(anchor) == null)
            throw new IOException("Cannot own the application's data-directory anchor.");
        _ = AppService.DefaultAppDataDirectory;
        var ownsData = SingleInstanceGuard.EnterOrHandOff();
        if (restartHandoff != null) Serilog.Log.Information(restartHandoff);
        if (ownsData && !connection.Stopping.IsCancellationRequested)
            ImportProgressStore.Current = new ImportProgressStore(AppDataLocator.ResolveEffectiveDataDirectory(anchor));
        return Task.FromResult(ownsData && !connection.Stopping.IsCancellationRequested);
    }

    private static void DisposeConnection(SetupChildConnection connection) =>
        Task.Run(async () => await connection.DisposeAsync()).GetAwaiter().GetResult();
}

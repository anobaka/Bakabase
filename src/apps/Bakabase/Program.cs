using Avalonia;
using Bakabase.Components;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Velopack;

namespace Bakabase;

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
        // update", which explicitly calls UpdateManager.ApplyUpdatesAndRestart.
        //
        // SetLogger: this logger is handed to the process-wide VelopackLocator, so every
        // later UpdateManager picks it up and its diagnostics (feed URL, channel, the
        // versions a check compared) land in AppLog instead of only in Velopack's own log
        // file, which nobody opens when an update check is being questioned.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .SetLogger(new SerilogVelopackLogger())
            .Run();

        // Everything from here on can be reported. Velopack's hook invocations never reach this
        // line (Run ends in Environment.Exit for them), so installing the handler after it costs
        // no coverage of a real launch.
        CrashHandler.Install();

        // Touching AppService runs its static constructor, which is what builds the Serilog file
        // sink. That would otherwise happen a step later, inside OnFrameworkInitializationCompleted
        // — leaving Avalonia's XAML load and tray-icon resolution in a window where a throw is
        // recorded nowhere but the OS event log. Pulling it forward changes only when this work
        // runs, not what it does: OnFrameworkInitializationCompleted's first act is to read the
        // same property. Deliberately not guarded — the static ctor throws by design when the
        // AppData layout cannot be migrated, and the handler above is already armed to report it.
        _ = AppService.DefaultAppDataDirectory;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

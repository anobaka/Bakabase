using Avalonia;
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
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .Run();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

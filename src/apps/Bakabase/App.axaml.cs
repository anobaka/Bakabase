using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Bakabase.Components;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Relocation;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Service.Components;
using Bakabase.Windows;

namespace Bakabase;

public partial class App : Application
{
    private AvaloniaGuiAdapter _guiAdapter = null!;
    private ISystemService _systemService = null!;
    public BakabaseHost? Host { get; private set; }

    public TrayIcon AppTrayIcon { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Resolve XAML-declared tray icon and menu items
        var icons = TrayIcon.GetIcons(this);
        AppTrayIcon = icons![0];
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            // Run any pending data-path relocation BEFORE AppOptionsManager is read — the runner
            // mutates app.json (the same file AppOptionsManager loads) when committing the new
            // DataPath. Splash window provides progress for multi-GB copies.
            await RunPendingRelocationIfAnyAsync();

            _guiAdapter = GuiAdapterCreator.Create<AvaloniaGuiAdapter>(this);
            _systemService = new CrossPlatformSystemService();

            var options = AppOptionsManager.Default.Value;
            AppService.SetCulture(options.Language);

            Host = new BakabaseHost(_guiAdapter, _systemService);

            // Wire up tray events now that Host is available
            AppTrayIcon.Clicked += (_, _) => _guiAdapter.Show();

            var openItem = AppTrayIcon.Menu!.Items.OfType<NativeMenuItem>().First(i => i.Header == "Open");
            var exitItem = AppTrayIcon.Menu!.Items.OfType<NativeMenuItem>().First(i => i.Header == "Exit");
            openItem.Click += (_, _) => _guiAdapter.Show();
            exitItem.Click += async (_, _) => await Host.TryToExit(true);

            await Host.Start(desktop.Args ?? []);

            desktop.Exit += (_, _) =>
            {
                AppTrayIcon.IsVisible = false;
                Host?.Dispose();
            };
        }
    }

    /// <summary>
    /// If <c>.pending_relocate</c> exists, run the relocation with a splash window for
    /// progress feedback. Runs synchronously on the UI thread for the splash, with the
    /// actual filesystem work on a background task.
    /// </summary>
    private static async System.Threading.Tasks.Task RunPendingRelocationIfAnyAsync()
    {
        var anchor = AppService.DefaultAppDataDirectory;
        var currentDataDir = AppOptionsManager.Default.Value.DataPath ?? anchor;
        var marker = PendingRelocation.TryReadFrom(currentDataDir);
        if (marker == null) return;

        // Release the static Serilog file handle so the runner's source-dir cleanup isn't
        // blocked on Windows. We re-target after the runner finishes, regardless of outcome.
        Serilog.Log.CloseAndFlush();

        RelocationSplashWindow? splash = null;
        try
        {
            splash = new RelocationSplashWindow(marker.Language);
            splash.Show();
        }
        catch
        {
            // headless / no display → fall through, runner still works
            splash = null;
        }

        var splashRef = splash;
        var progress = new System.Progress<RelocationProgress>(p =>
        {
            if (splashRef == null) return;
            Dispatcher.UIThread.Post(() => splashRef.UpdateProgress(p));
        });

        var outcome = await PendingRelocationRunner.TryRunAsync(
            anchor,
            () => AppOptionsManager.Default.Value.DataPath ?? anchor,
            async (newPath, prevDataDir) =>
            {
                await AppOptionsManager.Default.SaveAsync(o =>
                {
                    if (prevDataDir != null) o.PrevDataPath = prevDataDir;
                    o.DataPath = newPath;
                });
            },
            progress);

        if (splash != null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => splash.Close());
        }

        // Re-open the log sink at whatever data dir is now effective: target on success,
        // original currentDataDir on failure (the runner preserves the marker for retry,
        // and the source dir has not been deleted in that case).
        var effectiveDataDir = outcome.Kind == RelocationOutcomeKind.Success
            ? marker.Target
            : currentDataDir;
        AppService.ReconfigureLogger(effectiveDataDir);

        if (outcome.Kind == RelocationOutcomeKind.Error)
        {
            // Marker is preserved on error so the next launch retries. Log only — Phase 2 keeps
            // failure handling minimal so a broken relocation can't strand the user with no app.
            System.Console.Error.WriteLine(
                $"Data path relocation failed: {outcome.ErrorMessage}\n{outcome.StackTrace}");
        }
    }
}

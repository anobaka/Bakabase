using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Bakabase.Shell.Components;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Relocation;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Shell.Windows;

namespace Bakabase.Shell;

public partial class App : Application
{
    private readonly Func<IGuiAdapter, ISystemService, IShellHost> _hostFactory;

    private AvaloniaGuiAdapter _guiAdapter = null!;
    private ISystemService _systemService = null!;
    public IShellHost? Host { get; private set; }

    /// <summary>
    /// The entry project decides which host sits behind the shell — see
    /// <see cref="IShellHost"/>. Avalonia builds the app through
    /// <c>AppBuilder.Configure(Func&lt;TApp&gt;)</c>, which is what lets this take a
    /// constructor argument at all.
    /// </summary>
    public App(Func<IGuiAdapter, ISystemService, IShellHost> hostFactory)
    {
        _hostFactory = hostFactory;
    }

    /// <summary>
    /// Single owner of every user-initiated exit. Assigned alongside <see cref="Host"/>, which
    /// happens before the main window (and therefore before anything can ask to close).
    /// </summary>
    public ExitCoordinator ExitCoordinator { get; private set; } = null!;

    public TrayIcon AppTrayIcon { get; private set; } = null!;

    /// <summary>
    /// Whether the desktop shows the tray icon right now, i.e. whether "minimize to tray" is a
    /// real option and the tray menu can be reached. See <see cref="TrayIconAvailability"/> — on
    /// Linux without a StatusNotifierWatcher the icon never appears and a hidden window cannot
    /// be recovered. Safe on any thread.
    /// </summary>
    public bool IsTrayIconAvailable => TrayIconAvailability.IsSupported(AppTrayIcon);

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

            Host = _hostFactory(_guiAdapter, _systemService);
            ExitCoordinator = new ExitCoordinator(this, _guiAdapter);

            // Wire up tray events now that Host is available
            AppTrayIcon.Clicked += (_, _) => _guiAdapter.Show();

            // macOS: clicking the Dock icon, or opening the app again from Finder or
            // Launchpad, does not start a second process — LaunchServices sends the running
            // one a "reopen" instead. With the window hidden (closed to the tray), that has to
            // bring it back, or the click does nothing at all. Windows has no such event; a
            // second launch there is a second process, which the single-instance guard turns
            // into the same Show() through its activation channel.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e.Kind == ActivationKind.Reopen)
                    {
                        _guiAdapter.Show();
                    }
                };
            }

            // desktop.Exit below covers the graceful exits only. Anything that ends the
            // process without unwinding Avalonia — Environment.Exit from Velopack's
            // ApplyUpdatesAndRestart, a fatal-error bail-out, Ctrl+C on a console run —
            // would otherwise leave the icon registered with Explorer until the user
            // happens to hover it. ProcessExit fires for all of those.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => SetTrayIconVisible(false);

            // A previous instance that was hard-killed (VS "Stop Debugging", Task Manager,
            // a crash) ran none of the above and left its icon behind. Nothing in *that*
            // process can fix it after the fact, so the next launch cleans up instead.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2));
                TrayIconGhostSweeper.Sweep();
            });

            // Found by their XAML headers, which TrayMenuController replaces with localised ones
            // below — so this lookup has to come first.
            var openItem = AppTrayIcon.Menu!.Items.OfType<NativeMenuItem>().First(i => i.Header == "Open");
            var exitItem = AppTrayIcon.Menu!.Items.OfType<NativeMenuItem>().First(i => i.Header == "Exit");
            openItem.Click += (_, _) => _guiAdapter.Show();

            // Not Host.TryToExit(true): that path calls Shutdown() and then falls through into
            // its own close-behaviour switch, so a tray exit with CloseBehavior.Prompt raises a
            // confirmation dialog while Avalonia is already tearing down.
            exitItem.Click += async (_, _) => await ExitCoordinator.RequestExitAsync(ExitTrigger.TrayMenu);

            // The host's container is read lazily: it does not exist until Start has built it,
            // and the all-in-one's Start does not return until the host has stopped again, so
            // there is no "after start" to wire this up in.
            var trayMenu = new TrayMenuController(AppTrayIcon.Menu!, openItem, exitItem, _guiAdapter,
                () => Host?.Host?.Services, () => IsTrayIconAvailable);
            desktop.Exit += (_, _) => trayMenu.Dispose();

            // Only now, with Open and Exit wired (App.axaml declares it hidden).
            SetTrayIconVisible(true);

            await Host.Start(desktop.Args ?? []);

            desktop.Exit += (_, _) =>
            {
                SetTrayIconVisible(false);
                Host?.Dispose();
            };
        }
    }

    /// <summary>
    /// Registers / unregisters the tray icon with the shell (Shell_NotifyIcon NIM_ADD /
    /// NIM_DELETE on Windows). Idempotent, never throws, and safe to call from any thread —
    /// most callers are exit paths, where an exception would be worse than a leftover icon.
    /// </summary>
    public void SetTrayIconVisible(bool visible)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                AppTrayIcon.IsVisible = visible;
            }
            else
            {
                // Bounded wait: on the ProcessExit path the UI thread is still pumping, but
                // if it ever isn't we must not stall the process on its way out.
                Dispatcher.UIThread.Invoke(() => AppTrayIcon.IsVisible = visible,
                    DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
            }
        }
        catch
        {
            // Already torn down / dispatcher gone — nothing left to clean up.
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
        // app.json + redirect have already been migrated into the new layout by
        // AppService's static ctor, so EffectiveAppDataResolver is the source of truth.
        var currentDataDir = EffectiveAppDataResolver.Resolve(anchor).DataDir;
        var marker = PendingRelocation.TryReadFrom(currentDataDir);
        if (marker == null) return;

        // This process already owns currentDataDir (the entry point took its lock). Own the
        // target too, before a byte is copied into it, and keep both until the move is over:
        // until the pointer flips a second launch resolves the source, after it the target,
        // and either way it must find this process holding it — not a half-copied database it
        // could open. A target some other instance owns (one started with BAKABASE_DATA_DIR
        // pointing there) means the move waits: the marker stays, and the next launch retries.
        var targetClaim = SingleInstanceGuard.AcquireAdditional(marker.Target);
        if (targetClaim == SingleInstanceEntry.Refused)
        {
            Serilog.Log.Warning(
                "Data path relocation to {Target} postponed: another running Bakabase owns that directory. " +
                "The request is kept and retried on the next launch.", marker.Target);
            return;
        }

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
            () => EffectiveAppDataResolver.Resolve(anchor).DataDir,
            async (newDataDir, prevDataDir) =>
            {
                // Step 1: flip the pointer at the anchor. After this line, AppOptionsManager
                // resolves to {newDataDir}/app.json (or to the anchor when the redirect was
                // deleted because newDataDir == anchor).
                if (string.Equals(
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(newDataDir)),
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(anchor)),
                        StringComparison.OrdinalIgnoreCase))
                {
                    AnchorRedirect.Delete(anchor);
                }
                else
                {
                    AnchorRedirect.Write(anchor, newDataDir);
                }

                // Step 2: persist PrevDataPath into the destination's app.json so
                // IAppDataPathRelocator can rebase stored absolute paths on next boot.
                if (prevDataDir != null)
                {
                    await AppOptionsManager.Default.SaveAsync(o => o.PrevDataPath = prevDataDir);
                }

                // Step 3: clean up any anchor-side app.json left behind by the legacy
                // layout — leaving it would only be confusing; nothing reads it once the
                // redirect is in place.
                if (AnchorRedirect.Exists(anchor))
                {
                    var legacyAnchorAppJson = Path.Combine(anchor, EffectiveAppDataResolver.AppOptionsFileName);
                    if (System.IO.File.Exists(legacyAnchorAppJson))
                    {
                        System.IO.File.Delete(legacyAnchorAppJson);
                    }
                }
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

        if (outcome.Kind == RelocationOutcomeKind.Success)
        {
            // The target is this process's data directory now. The source has been emptied by
            // the runner (all but its lock file), so letting go of it last means it was owned
            // right up to the moment it stopped mattering.
            SingleInstanceGuard.Promote(marker.Target);
            SingleInstanceGuard.Retire(currentDataDir);
        }
        else if (targetClaim == SingleInstanceEntry.Entered)
        {
            // Not moving after all: give the target back as it was.
            SingleInstanceGuard.Abandon(marker.Target);
        }

        if (outcome.Kind == RelocationOutcomeKind.Error)
        {
            // Marker is preserved on error so the next launch retries. Log only — Phase 2 keeps
            // failure handling minimal so a broken relocation can't strand the user with no app.
            System.Console.Error.WriteLine(
                $"Data path relocation failed: {outcome.ErrorMessage}\n{outcome.StackTrace}");
        }
    }
}

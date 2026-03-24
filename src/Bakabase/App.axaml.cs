using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bakabase.Components;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Service.Components;

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
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;
using Bakabase.Components;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Service.Components;

namespace Bakabase;

public partial class App : Application
{
    private IGuiAdapter _guiAdapter = null!;
    private ISystemService _systemService = null!;
    public BakabaseHost? Host { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
        AvaloniaWebViewBuilder.Initialize(default);
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
            await Host.Start(desktop.Args ?? []);

            desktop.Exit += (_, _) =>
            {
                _guiAdapter.HideTray();
                Host?.Dispose();
            };
        }
    }
}

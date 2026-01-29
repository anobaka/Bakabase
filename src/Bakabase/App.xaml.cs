using System.Windows;
using Bakabase.Components;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Service.Components;
using Application = System.Windows.Application;

namespace Bakabase
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IGuiAdapter _guiAdapter;
        private readonly ISystemService _systemService;
        public BakabaseHost Host { get; private set; }

        public App()
        {
            _guiAdapter = GuiAdapterCreator.Create<WpfGuiAdapter>(this);
            _systemService = new WindowsSystemService();

            var options = AppOptionsManager.Default.Value;
            AppService.SetCulture(options.Language);
        }

        private void App_OnExit(object sender, ExitEventArgs e)
        {
            _guiAdapter.HideTray();
            Host?.Dispose();
        }

        private async void App_OnStartup(object sender, StartupEventArgs e)
        {
            Host = new BakabaseHost(_guiAdapter, _systemService);
            await Host.Start(e.Args);
        }
    }
}

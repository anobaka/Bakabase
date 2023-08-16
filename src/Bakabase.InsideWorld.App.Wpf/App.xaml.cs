using System.Windows;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.App.Core;
using Bakabase.InsideWorld.App.Wpf.Components;
using Application = System.Windows.Application;

namespace Bakabase.InsideWorld.App.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IGuiAdapter _guiAdapter;
        public InsideWorldHost Host { get; private set; }

        public App()
        {
            _guiAdapter = GuiAdapterCreator.Create<WpfGuiAdapter>(this);

            var options = AppOptionsManager.Instance.Value;
            AppService.SetCulture(options.Language);
        }

        private void App_OnExit(object sender, ExitEventArgs e)
        {
            _guiAdapter.HideTray();
        }

        private async void App_OnStartup(object sender, StartupEventArgs e)
        {
            _guiAdapter.ChangeUiTheme(AppOptionsManager.Instance.CalculatedUiTheme);

            Host = new InsideWorldHost(_guiAdapter);
            Host.Start(new string[] { });
        }
    }
}
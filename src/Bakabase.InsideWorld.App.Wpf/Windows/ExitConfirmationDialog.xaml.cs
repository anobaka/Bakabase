using System.Windows;
using Bakabase.Infrastructures;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.App.Wpf.Windows
{
    /// <summary>
    /// Interaction logic for ExitConfirmationDialog.xaml
    /// </summary>
    public partial class ExitConfirmationDialog : Window
    {
        private readonly IStringLocalizer _localizer;
        public ExitConfirmationDialog(IStringLocalizer localizer)
        {
            _localizer = localizer;
            InitializeComponent();

            this.ExitBtn.Content = _localizer[AppSharedResource.App_Exit];
            this.MinimizeBtn.Content = _localizer[AppSharedResource.App_Minimize];
            this.RememberCheckBox.Content = _localizer[AppSharedResource.App_RememberMe];
            this.Tip.Text = _localizer[AppSharedResource.App_TipOnExit];
        }
    }
}

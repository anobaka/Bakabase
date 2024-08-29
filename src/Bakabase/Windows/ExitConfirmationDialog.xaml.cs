using System.Windows;
using Bakabase.Infrastructures.Resources;

namespace Bakabase.Windows
{
    /// <summary>
    /// Interaction logic for ExitConfirmationDialog.xaml
    /// </summary>
    public partial class ExitConfirmationDialog : Window
    {
        private readonly AppLocalizer _localizer;

        public ExitConfirmationDialog(AppLocalizer localizer)
        {
            _localizer = localizer;
            InitializeComponent();

            this.ExitBtn.Content = _localizer.App_Exit();
            this.MinimizeBtn.Content = _localizer.App_Minimize();
            this.RememberCheckBox.Content = _localizer.App_RememberMe();
            this.Tip.Text = _localizer.App_TipOnExit();
        }

        private void RememberCheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
}
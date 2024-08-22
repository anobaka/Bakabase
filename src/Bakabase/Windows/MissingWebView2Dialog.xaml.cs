using System.Diagnostics;
using System.Security.Policy;
using System.Windows;
using Bakabase.Infrastructures.Resources;

namespace Bakabase.Windows
{
    /// <summary>
    /// Interaction logic for ExitConfirmationDialog.xaml
    /// </summary>
    public partial class MissingWebView2Dialog : Window
    {
        public MissingWebView2Dialog(AppLocalizer localizer)
        {
            InitializeComponent();

            this.DownloadBtn.Content = localizer.App_GoToDownload();
            this.Tip.Text = localizer.App_MissingWebView2Tip();
        }

        private void DownloadBtn_OnClick(object sender, RoutedEventArgs e)
        {
            const string url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
            Process.Start(new ProcessStartInfo(url) {UseShellExecute = true});
            Close();
        }
    }
}
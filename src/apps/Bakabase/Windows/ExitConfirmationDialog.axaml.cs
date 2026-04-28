using Avalonia.Controls;
using Bakabase.Infrastructures.Resources;

namespace Bakabase.Windows;

public partial class ExitConfirmationDialog : Window
{
    public ExitConfirmationDialog(AppLocalizer localizer)
    {
        InitializeComponent();

        Title = localizer.App_Exiting();
        ExitBtn.Content = localizer.App_Exit();
        MinimizeBtn.Content = localizer.App_Minimize();
        RememberCheckBox.Content = localizer.App_RememberMe();
        Tip.Text = localizer.App_TipOnExit();
    }
}

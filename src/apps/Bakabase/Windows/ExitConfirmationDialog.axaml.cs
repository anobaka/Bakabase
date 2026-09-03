using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Bakabase.Resources;

namespace Bakabase.Windows;

/// <summary>What the user decided when asked to close the app.</summary>
public enum ExitChoice
{
    /// <summary>Stay running, window visible. Also the result of dismissing the dialog.</summary>
    Cancel,

    /// <summary>Hide the window, keep the process (and background tasks) alive.</summary>
    Minimize,

    /// <summary>Shut down for real.</summary>
    Exit
}

public enum ExitPromptKind
{
    /// <summary>
    /// The user closed the window and we are asking what that should mean. This prompt decides
    /// the default close behaviour, so it offers "remember my choice".
    /// </summary>
    Choice,

    /// <summary>
    /// Exiting is already decided (tray → Exit, or the configured behaviour) and we are only
    /// confirming because work would be lost. Warning-led, and never persisted as a preference.
    /// </summary>
    ConfirmBusy
}

/// <param name="AllowMinimize">
/// False when there is no notification area to minimize into — see
/// <c>TrayIconAvailability</c>. Ignored for <see cref="ExitPromptKind.ConfirmBusy"/>, which
/// never offers minimize: the user already asked to quit.
/// </param>
/// <param name="BusyTaskNames">Critical tasks that are still active, most relevant first.</param>
public sealed record ExitPromptOptions(
    ExitPromptKind Kind,
    bool AllowMinimize,
    IReadOnlyList<string> BusyTaskNames);

public sealed record ExitPromptResult(ExitChoice Choice, bool Remember);

public partial class ExitConfirmationDialog : Window
{
    /// <summary>Beyond this the list stops growing and we summarise the rest.</summary>
    private const int MaxListedBusyTasks = 4;

    private ExitChoice _choice = ExitChoice.Cancel;

    public ExitConfirmationDialog() : this(new ExitPromptOptions(ExitPromptKind.Choice, true, [])) { }

    public ExitConfirmationDialog(ExitPromptOptions options)
    {
        InitializeComponent();

        var confirming = options.Kind == ExitPromptKind.ConfirmBusy;
        var canMinimize = options.AllowMinimize && !confirming;

        Title = ExitStrings.DialogTitle;
        MinimizeBtn.Content = ExitStrings.Minimize;
        ExitBtn.Content = ExitStrings.Exit;
        CancelBtn.Content = ExitStrings.Cancel;
        RememberCheckBox.Content = ExitStrings.Remember;
        RememberHint.Text = ExitStrings.RememberHint;
        BusyHeading.Text = ExitStrings.BusyHeading;
        BusyBody.Text = ExitStrings.BusyBody;

        Heading.Text = confirming ? ExitStrings.BusyHeading : ExitStrings.Heading;
        Subheading.Text = confirming
            ? ExitStrings.BusyBody
            // Promising a tray that is not there would be a lie, and on Linux a hidden window
            // has no way back — so say what closing will actually do instead.
            : canMinimize
                ? ExitStrings.Subheading
                : ExitStrings.SubheadingNoTray;

        MinimizeBtn.IsVisible = canMinimize;
        RememberPanel.IsVisible = !confirming;

        if (!canMinimize)
        {
            // Nothing else can be the default action once "minimize" is gone, and leaving
            // Enter unbound in a modal dialog is worse than binding it to the only forward
            // action the user has left.
            ExitBtn.IsDefault = true;
            // Swap, don't add: both class styles set the same template properties, so leaving
            // exit-secondary on would make the winner depend on declaration order.
            ExitBtn.Classes.Remove("exit-secondary");
            ExitBtn.Classes.Add("exit-primary");
        }

        RenderBusyTasks(options.BusyTaskNames, showPanel: !confirming);

        MinimizeBtn.Click += (_, _) => Finish(ExitChoice.Minimize);
        ExitBtn.Click += (_, _) => Finish(ExitChoice.Exit);
        CancelBtn.Click += (_, _) => Finish(ExitChoice.Cancel);
    }

    /// <summary>
    /// The warning block repeats itself when the header is already the warning
    /// (<paramref name="showPanel"/> false) — there we only need the task list, which the
    /// header cannot carry.
    /// </summary>
    private void RenderBusyTasks(IReadOnlyList<string> names, bool showPanel)
    {
        if (names.Count == 0)
        {
            BusyPanel.IsVisible = false;
            return;
        }

        BusyPanel.IsVisible = true;
        BusyHeading.IsVisible = showPanel;
        BusyBody.IsVisible = showPanel;

        foreach (var name in names.Take(MaxListedBusyTasks))
        {
            BusyTaskList.Children.Add(BusyTaskLine("•  " + name, dimmed: false));
        }

        if (names.Count > MaxListedBusyTasks)
        {
            BusyTaskList.Children.Add(
                BusyTaskLine("•  " + ExitStrings.BusyMore(names.Count - MaxListedBusyTasks), dimmed: true));
        }
    }

    private static TextBlock BusyTaskLine(string text, bool dimmed) => new()
    {
        Text = text,
        FontSize = 12,
        TextTrimming = TextTrimming.CharacterEllipsis,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Opacity = dimmed ? 0.6 : 0.9
    };

    private void Finish(ExitChoice choice)
    {
        _choice = choice;
        Close();
    }

    /// <summary>
    /// Shows the dialog and resolves once it is gone. Dismissing it any other way — the title
    /// bar X, Alt+F4, the owner closing — leaves <see cref="_choice"/> at
    /// <see cref="ExitChoice.Cancel"/>, so "no answer" can never be read as consent to quit.
    /// </summary>
    public static async Task<ExitPromptResult> PromptAsync(Window? owner, ExitPromptOptions options)
    {
        var dialog = new ExitConfirmationDialog(options);

        if (owner is {IsVisible: true})
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            // No visible owner (window already hidden, or we are quitting during startup):
            // ShowDialog would throw, so fall back to a modeless window we await by hand.
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
            dialog.ShowInTaskbar = true;
            dialog.Show();
            dialog.Activate();
            await closed.Task;
        }

        return new ExitPromptResult(dialog._choice, dialog.RememberCheckBox.IsChecked == true);
    }
}

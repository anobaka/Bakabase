using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Bakabase.Resources;

namespace Bakabase.Windows;

/// <summary>
/// Shown while the app is actually shutting down: the host is stopping, background tasks are
/// being wound up and data is being flushed. Without it the window simply vanishes and the
/// process lingers, which reads as a hang.
/// </summary>
public partial class ExitProgressWindow : Window
{
    private const int MaxListedTasks = 5;

    private bool _closingAllowed;

    /// <summary>Raised when the user gives up waiting and asks to quit immediately.</summary>
    public event Action? ForceQuitRequested;

    public ExitProgressWindow()
    {
        InitializeComponent();

        Title = ExitStrings.ClosingTitle;
        Heading.Text = ExitStrings.ClosingHeading;
        PhaseText.Text = ExitStrings.ClosingStoppingTasks;
        ForceHint.Text = ExitStrings.ForceQuitHint;
        ForceQuitBtn.Content = ExitStrings.ForceQuit;

        ForceQuitBtn.Click += (_, _) =>
        {
            ForceQuitBtn.IsEnabled = false;
            ForceQuitRequested?.Invoke();
        };
    }

    /// <summary>
    /// Belt and braces: the window is undecorated so there is no X to click, and the only
    /// dismiss affordance is "Quit now". Closing it early would leave a shutdown running with
    /// nothing on screen to explain the wait.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closingAllowed)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>Lets the shutdown sequence — and only it — take the window down.</summary>
    public void AllowClose() => _closingAllowed = true;

    public void SetPhase(string phase) => PhaseText.Text = phase;

    public void ShowForceQuit() => ForcePanel.IsVisible = true;

    /// <summary>
    /// Names the work still in flight so a slow shutdown is legible rather than mysterious.
    /// Rebuilds the list wholesale — it is at most a handful of rows, updated once a second.
    /// </summary>
    public void SetRemainingTasks(IReadOnlyList<string> names)
    {
        TaskList.Children.Clear();
        TaskPanel.IsVisible = names.Count > 0;

        if (names.Count == 0)
        {
            return;
        }

        foreach (var name in names.Take(MaxListedTasks))
        {
            TaskList.Children.Add(Line("•  " + name, 0.85));
        }

        if (names.Count > MaxListedTasks)
        {
            TaskList.Children.Add(Line("•  " + ExitStrings.BusyMore(names.Count - MaxListedTasks), 0.6));
        }
    }

    private static TextBlock Line(string text, double opacity) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = opacity,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
}

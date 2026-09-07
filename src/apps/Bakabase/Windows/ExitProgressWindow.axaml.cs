using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Bakabase.Resources;

namespace Bakabase.Windows;

/// <summary>
/// One line in the shutdown window's "what is still running" list.
/// </summary>
/// <param name="Name">Task name, already suffixed with a percentage when it has one.</param>
/// <param name="IsCritical">
/// Whether interrupting it loses data. Critical tasks are what the shutdown actually waits for, so
/// they are listed first and shown at full strength; the rest are there to explain the wait.
/// </param>
public readonly record struct ExitTaskLine(string Name, bool IsCritical);

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

    public void SetPhase(string phase) => OnUiThread(() => PhaseText.Text = phase);

    public void ShowForceQuit() => OnUiThread(() => ForcePanel.IsVisible = true);

    /// <summary>
    /// Names the work still in flight so a slow shutdown is legible rather than mysterious.
    /// Rebuilds the list wholesale — it is at most a handful of rows, refreshed a few times a second.
    /// </summary>
    public void SetRemainingTasks(IReadOnlyList<ExitTaskLine> tasks) => OnUiThread(() =>
    {
        TaskList.Children.Clear();
        TaskPanel.IsVisible = tasks.Count > 0;

        if (tasks.Count == 0)
        {
            return;
        }

        // Header, so the count is visible even when the list is truncated.
        TaskList.Children.Add(Line(ExitStrings.ClosingRemaining(tasks.Count), 0.55));

        // Critical work first: it is the reason the shutdown is waiting at all.
        var ordered = tasks.OrderByDescending(t => t.IsCritical).ToArray();

        foreach (var task in ordered.Take(MaxListedTasks))
        {
            TaskList.Children.Add(Line("•  " + task.Name, task.IsCritical ? 0.9 : 0.65));
        }

        if (ordered.Length > MaxListedTasks)
        {
            TaskList.Children.Add(Line("•  " + ExitStrings.BusyMore(ordered.Length - MaxListedTasks), 0.5));
        }
    });

    /// <summary>
    /// Every mutation here touches Avalonia controls, and the shutdown sequence reports from
    /// wherever its awaits happen to resume — which stops being the UI thread the moment anything
    /// on the path completes on a thread-pool thread. Marshal rather than assume.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        // Post, not Invoke: the shutdown must never block waiting on a UI thread that may itself be
        // waiting on the shutdown.
        Dispatcher.UIThread.Post(action);
    }

    private static TextBlock Line(string text, double opacity) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = opacity,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
}

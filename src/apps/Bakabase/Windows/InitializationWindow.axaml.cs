using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Bakabase.Windows;

/// <summary>
/// Boot-time splash. Shown by <see cref="Components.AvaloniaGuiAdapter"/> while the host
/// boots, until the main WebView opens. Visually matches <see cref="RelocationSplashWindow"/>:
/// constant title at the top, current phase below, indeterminate progress bar, optional
/// fine-grained detail line. The progress bar can be promoted to determinate via
/// <see cref="SetPhase"/> when the caller actually has a fraction to report.
/// </summary>
public partial class InitializationWindow : Window
{
    private TextBlock _phaseText = null!;
    private TextBlock _detailText = null!;
    private ProgressBar _progress = null!;

    public InitializationWindow()
    {
        InitializeComponent();
        _phaseText = this.FindControl<TextBlock>("PhaseText")!;
        _detailText = this.FindControl<TextBlock>("DetailText")!;
        _progress = this.FindControl<ProgressBar>("Progress")!;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Update the phase line and (optionally) the detail line + determinate fraction.
    /// Pass <paramref name="fraction"/> as <c>null</c> to keep the bar indeterminate — the
    /// default for stage-level work that has no countable subtask.
    /// </summary>
    public void SetPhase(string phase, string? detail = null, double? fraction = null)
    {
        _phaseText.Text = phase ?? string.Empty;
        _detailText.Text = detail ?? string.Empty;

        if (fraction is { } f)
        {
            _progress.IsIndeterminate = false;
            _progress.Value = Math.Clamp(f * 100d, 0d, 100d);
        }
        else
        {
            _progress.IsIndeterminate = true;
        }
    }
}

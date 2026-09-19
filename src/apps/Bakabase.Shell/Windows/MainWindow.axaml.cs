using Avalonia;
using Avalonia.Controls;
using Bakabase.Shell.Controls;

namespace Bakabase.Shell.Windows;

public partial class MainWindow : Window
{
    private static readonly (int MinScreenWidth, int MinWindowWidth)[] MinWidths =
    [
        (2560, 1920),
        (1920, 1600),
        (1600, 1440),
        (0, 1280)
    ];

    private static readonly (int MinScreenHeight, int MinWindowHeight)[] MinHeights =
    [
        (1440, 1080),
        (1080, 900),
        (900, 810),
        (0, 720)
    ];

    private NativeWebViewHost? _webView;

    public MainWindow()
    {
        InitializeComponent();

        MinWidth = 1280;
        MinHeight = 720;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty || change.Property == WindowStateProperty)
        {
            SyncWebViewVisibility();
        }
    }

    /// <summary>
    /// Tells the embedded browser whether anyone can see it.
    /// </summary>
    /// <remarks>
    /// This window spends most of its life in the tray, and until it said so the WebView
    /// went on compositing the page behind it — an app doing nothing held a browser
    /// process at double-digit CPU. Both transitions matter and they are separate
    /// properties: closing to the tray flips <c>IsVisible</c>, minimising does not.
    /// </remarks>
    private void SyncWebViewVisibility()
    {
        // Property changes start arriving before InitializeComponent has built the tree,
        // so the lookup is retried until it finds something rather than cached as null.
        _webView ??= this.FindControl<NativeWebViewHost>("WebView");
        _webView?.SetRendererVisible(IsVisible && WindowState != WindowState.Minimized);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        SyncWebViewVisibility();

        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var scaling = screen.Scaling;
            var availableWidth = screen.Bounds.Width / scaling;
            var availableHeight = screen.Bounds.Height / scaling;

            foreach (var (sw, mw) in MinWidths)
            {
                if (availableWidth >= sw)
                {
                    Width = mw;
                    break;
                }
            }

            foreach (var (sh, mh) in MinHeights)
            {
                if (availableHeight >= sh)
                {
                    Height = mh;
                    break;
                }
            }
        }
    }
}

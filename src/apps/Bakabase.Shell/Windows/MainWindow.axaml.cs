using Avalonia;
using Avalonia.Controls;
using Bakabase.Shell.Controls;

namespace Bakabase.Shell.Windows;

/// <summary>The tray's "Switch to" list as the main window shows it; see <see cref="MainWindow.SetServerSwitchMenu"/>.</summary>
/// <param name="Header">The menu's title, already localised.</param>
/// <param name="Entries">This device and every managed server, in menu order.</param>
internal sealed record ServerSwitchMenu(string Header, IReadOnlyList<ServerSwitchMenuEntry> Entries);

/// <param name="Label">Already escaped for access keys ("_" doubled).</param>
/// <param name="Invoke">Runs on the UI thread when the entry is picked; must not block it.</param>
internal sealed record ServerSwitchMenuEntry(string Label, Action Invoke);

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

    /// <summary>
    /// Shows <paramref name="menu"/> as a menu bar above the page, or hides the bar when it is
    /// null or empty. UI thread only.
    /// </summary>
    /// <remarks>
    /// For desktops that show no tray, where this window would otherwise have no way back from
    /// a server whose UI predates the switcher. A menu bar rather than a keyboard shortcut: keys
    /// typed while the page has focus go to the browser's own native window and never reach
    /// Avalonia (on X11 the WebKitGTK window is simply reparented into ours, with no XEmbed
    /// focus forwarding), whereas a click on the bar lands in Avalonia's part of the window
    /// whatever has focus. The submenu opens as a popup window of its own, so it draws over the
    /// page instead of underneath it.
    /// </remarks>
    internal void SetServerSwitchMenu(ServerSwitchMenu? menu)
    {
        var bar = this.FindControl<Menu>("ServerSwitchBar");
        var root = this.FindControl<MenuItem>("ServerSwitchRoot");
        if (bar == null || root == null)
        {
            return;
        }

        if (menu == null || menu.Entries.Count == 0)
        {
            bar.Close();
            bar.IsVisible = false;
            root.Items.Clear();
            return;
        }

        root.Header = menu.Header;
        root.Items.Clear();
        foreach (var entry in menu.Entries)
        {
            var item = new MenuItem { Header = entry.Label };
            var invoke = entry.Invoke;
            item.Click += (_, _) => invoke();
            root.Items.Add(item);
        }

        bar.IsVisible = true;
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

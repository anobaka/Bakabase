using Avalonia;
using Avalonia.Controls;

namespace Bakabase.Windows;

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

    public MainWindow()
    {
        InitializeComponent();

        MinWidth = 1280;
        MinHeight = 720;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

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

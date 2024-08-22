using System.Windows;

namespace Bakabase.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly IEnumerable<(int MinScreenWidth, int MinWindowWidth)> MinWidths = new[]
        {
            (1920, 1600),
            (1600, 1440),
            (0, 1280)
        };

        private static readonly IEnumerable<(int MinScreenHeight, int MinWindowHeight)> MinHeights = new[]
        {
            (1080, 900),
            (900, 810),
            (0, 720)
        };

        public MainWindow()
        {
            var availableWidth = SystemParameters.PrimaryScreenWidth;
            var availableHeight = SystemParameters.PrimaryScreenHeight;

            var width = 0;
            var height = 0;

            foreach (var (sw, mw) in MinWidths)
            {
                if (availableWidth >= sw)
                {
                    width = mw;
                    break;
                }
            }

            foreach (var (sh, mh) in MinHeights)
            {
                if (availableHeight >= sh)
                {
                    height = mh;
                    break;
                }
            }

            this.Width = width;
            this.MinWidth = width;
            this.Height = height;
            this.MinHeight = height;

            InitializeComponent();
        }
    }
}
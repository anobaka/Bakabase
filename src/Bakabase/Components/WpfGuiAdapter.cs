using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Infrastructures.Resources;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Windows;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Bakabase.Components
{
    public class WpfGuiAdapter : GuiAdapter
    {
        private readonly App _app;
        private NotifyIcon? _tray;
        private InitializationWindow? _initializationWindow;
        private ErrorWindow? _errorWindow;
        private MainWindow? _mainWindow;
        private ExitConfirmationDialog? _exitConfirmationDialog;
        private MissingWebView2Dialog? _missingWebView2Dialog;
        private bool _isDarkMode = false;

        public WpfGuiAdapter(App app)
        {
            _app = app;
        }

        public override void InvokeInGuiContext(Action action) => _app.Dispatcher.Invoke(action);

        public override T InvokeInGuiContext<T>(Func<T> func) => _app.Dispatcher.Invoke(func);

        [GuiContextInterceptor]
        public override string[]? OpenFilesSelector(string? initialDirectory = null)
        {
            if (initialDirectory.IsNotEmpty())
            {
                initialDirectory = new DirectoryInfo(initialDirectory!).FullName;
            }

            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                InitialDirectory = initialDirectory
            };
            var result = dialog.ShowDialog();
            return result == true ? dialog.FileNames.Select(f => f.StandardizePath()).ToArray() : null;
        }

        [GuiContextInterceptor]
        public override string? OpenFileSelector(string? initialDirectory = null)
        {
            if (initialDirectory.IsNotEmpty())
            {
                initialDirectory = new DirectoryInfo(initialDirectory!).FullName;
            }

            var dialog = new OpenFileDialog()
            {
                InitialDirectory = initialDirectory
            };
            var result = dialog.ShowDialog();
            return result == true ? dialog.FileName.StandardizePath() : null;
        }

        [GuiContextInterceptor]
        public override string? OpenFolderSelector(string? initialDirectory = null)
        {
            if (initialDirectory.IsNotEmpty())
            {
                initialDirectory = new DirectoryInfo(initialDirectory!).FullName;
            }

            string? folder = null;
            using var dialog = new FolderBrowserDialog
            {
                InitialDirectory = initialDirectory,
            };
            var result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                folder = dialog.SelectedPath;
            }

            return folder.StandardizePath();
        }

        public override string GetDownloadsDirectory()
        {
            var v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders"
                , "{374DE290-123F-4565-9164-39C4925E467B}", null)!;
            return v.ToString();
        }

        [GuiContextInterceptor]
        public override void ShowTray(Func<Task> onExiting)
        {
            _tray = new NotifyIcon()
            {
                Icon = new Icon("Assets/favicon.ico"),
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true
            };

            _tray.MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)
                {
                    Show();
                }
            };

            _tray.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripMenuItem("Open", null, (o, eventArgs) => { Show(); }, "Open"),
                new ToolStripMenuItem("Exit", null,
                    async (o, eventArgs) => { await onExiting(); },
                    "Exit")
            });
        }

        [GuiContextInterceptor]
        public override void HideTray()
        {
            if (_tray != null)
            {
                _tray.Visible = false;
            }
        }

        [GuiContextInterceptor]
        public override void SetTrayText(string text)
        {
            if (_tray != null)
            {
                const int systemLimit = 127;
                _tray.Text = text.Substring(0, Math.Min(systemLimit, text.Length));
            }
        }

        [GuiContextInterceptor]
        public override void SetTrayIcon(Icon icon)
        {
            if (_tray != null)
            {
                _tray.Icon = icon;
            }
        }

        [GuiContextInterceptor]
        public override void ShowFatalErrorWindow(string message, string title = "Fatal Error")
        {
            _errorWindow ??= new ErrorWindow();
            RegisterApplyingUiThemeOnVisibilityChange(_errorWindow);

            _errorWindow.Title.Text = title;
            _errorWindow.StackTrace.Text = message;

            _errorWindow.Show();

            _mainWindow?.Close();
            _initializationWindow?.Close();
        }

        [GuiContextInterceptor]
        public override void ShowInitializationWindow(string processName)
        {
            _initializationWindow ??= new InitializationWindow();
            RegisterApplyingUiThemeOnVisibilityChange(_initializationWindow);
            _initializationWindow.ProcessName.Text = processName;
            _initializationWindow.Show();
        }

        [GuiContextInterceptor]
        public override void DestroyInitializationWindow()
        {
            _initializationWindow?.Close();
        }

        [GuiContextInterceptor]
        public override async void ShowMainWebView(string url, string title, Func<Task> onClosing)
        {
            _mainWindow ??= new MainWindow();
            RegisterApplyingUiThemeOnVisibilityChange(_mainWindow);

            try
            {
                _mainWindow.Show();


                _mainWindow.Title = title;
                await _mainWindow.WebView2.EnsureCoreWebView2Async();
                await _mainWindow.WebView2.CoreWebView2.Profile.ClearBrowsingDataAsync();
                _mainWindow.WebView2.CoreWebView2.Navigate(url);
                _mainWindow.Closing += async (sender, args) =>
                {
                    args.Cancel = true;
                    await onClosing();
                };
            }
            catch (Exception e)
            {
                _mainWindow.Close();
                _missingWebView2Dialog ??=
                    new MissingWebView2Dialog(_app.Host.Host.Services.GetRequiredService<AppLocalizer>());
                RegisterApplyingUiThemeOnVisibilityChange(_missingWebView2Dialog);
                _missingWebView2Dialog.Show();
            }
        }

        [GuiContextInterceptor]
        public override void SetMainWindowTitle(string title)
        {
            if (_mainWindow != null)
            {
                _mainWindow.Title = title;
            }
        }

        public override bool MainWebViewVisible => _mainWindow?.Visibility == Visibility.Visible;

        [GuiContextInterceptor]
        public override void Shutdown()
        {
            System.Windows.Application.Current.Shutdown();
        }

        [GuiContextInterceptor]
        public override void Hide()
        {
            _mainWindow?.Hide();
        }

        [GuiContextInterceptor]
        public override void Show()
        {
            _mainWindow?.Show();
            if (_mainWindow != null)
            {
                _mainWindow.Topmost = true;
                _mainWindow.Topmost = false;
            }
        }

        private async void _onExitConfirmationDialogClosing(CloseBehavior behavior,
            Func<CloseBehavior, bool, Task> onClosed)
        {
            var remember = _exitConfirmationDialog?.RememberCheckBox.IsChecked ?? false;
            if (behavior != CloseBehavior.Cancel)
            {
                _exitConfirmationDialog?.Close();
                _exitConfirmationDialog = null;
            }

            await onClosed(behavior, remember);
        }

        [GuiContextInterceptor]
        public override void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed)
        {
            if (_exitConfirmationDialog == null)
            {
                _exitConfirmationDialog =
                    new ExitConfirmationDialog(_app.Host.Host.Services.GetRequiredService<AppLocalizer>());
                _exitConfirmationDialog.ExitBtn.Click += (sender, args) =>
                {
                    _onExitConfirmationDialogClosing(CloseBehavior.Exit, onClosed);
                };
                _exitConfirmationDialog.MinimizeBtn.Click += (sender, args) =>
                {
                    _onExitConfirmationDialogClosing(CloseBehavior.Minimize, onClosed);
                };
                _exitConfirmationDialog.Closing += (sender, args) =>
                {
                    onClosed(CloseBehavior.Cancel, false);
                    _exitConfirmationDialog = null;
                };
                RegisterApplyingUiThemeOnVisibilityChange(_exitConfirmationDialog);
            }

            _exitConfirmationDialog.RememberCheckBox.IsChecked = false;
            _exitConfirmationDialog.Show();
        }

        [GuiContextInterceptor]
        public override bool ShowConfirmDialog(string message, string caption)
        {
            var result = MessageBox.Show(message, caption, MessageBoxButton.OKCancel);
            return result == MessageBoxResult.OK;
        }

        [GuiContextInterceptor]
        public override void ChangeUiTheme(UiTheme theme)
        {
            _isDarkMode = theme == UiTheme.Dark;

            var windows = new Window?[]
                {_mainWindow, _errorWindow, _exitConfirmationDialog, _initializationWindow, _missingWebView2Dialog};

            foreach (var window in windows)
            {
                ApplyUiTheme(window);
            }
        }

        [DllImport("shell32.dll")]
        public static extern int SHGetStockIconInfo(
            uint siid,
            uint uFlags,
            ref SHSTOCKICONINFO psii);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHSTOCKICONINFO
        {
            public uint cbSize;
            public IntPtr hIcon;
            public int iSysIconIndex;
            public int iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szPath;
        }

        const uint SIID_DOCNOASSOC = 0; // Default file icon
        const uint SHSIID_FOLDER = 0x3; // Folder icon
        const uint SHGSI_ICON = 0x100; // Retrieve icon

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        private static readonly ConcurrentDictionary<IconType, byte[]> SystemIconCache = [];
        private static readonly Lock SystemIconAcquireLock = new Lock();

        public override byte[]? GetIcon(IconType type, string? path)
        {
            Start:
            switch (type)
            {
                case IconType.UnknownFile:
                case IconType.Directory:
                {
                    if (!SystemIconCache.TryGetValue(type, out var d))
                    {
                        lock (SystemIconAcquireLock)
                        {
                            if (!SystemIconCache.TryGetValue(type, out d))
                            {
                                var stockIconInfo = new SHSTOCKICONINFO();
                                stockIconInfo.cbSize = (uint) Marshal.SizeOf(stockIconInfo);

                                var result = SHGetStockIconInfo(
                                    type == IconType.UnknownFile ? SIID_DOCNOASSOC : SHSIID_FOLDER, SHGSI_ICON,
                                    ref stockIconInfo);

                                using var icon = Icon.FromHandle(stockIconInfo.hIcon);

                                using var ms = new MemoryStream();
                                icon.ToBitmap().Save(ms, ImageFormat.Png);

                                DestroyIcon(stockIconInfo.hIcon);

                                SystemIconCache[type] = d = ms.ToArray();
                            }
                        }
                    }

                    return d;
                }
                case IconType.Dynamic:
                {
                    if (path.IsNullOrEmpty())
                    {
                        return null;
                    }

                    if (File.Exists(path))
                    {
                        using var icon = Icon.ExtractAssociatedIcon(path);
                        if (icon == null)
                        {
                            return null;
                        }

                        using var ms = new MemoryStream();
                        icon.ToBitmap().Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }

                    if (Directory.Exists(path))
                    {
                        type = IconType.Directory;
                        goto Start;
                    }

                    return null;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        [GuiContextInterceptor]
        private void ApplyUiTheme(Window? window)
        {
            if (window != null)
            {
                try
                {
                    UseImmersiveDarkMode(new WindowInteropHelper(window).Handle, _isDarkMode);
                    // Console.WriteLine(
                    //     $"Change color of title bar for {window.Title} to {(_isDarkMode ? "dark" : "light")}");
                }
                catch
                {
                    // ignored
                }
            }
        }

        private void RegisterApplyingUiThemeOnVisibilityChange(Window window)
        {
            window.IsVisibleChanged -= ApplyUiThemeOnWindowVisibilityChange;
            window.IsVisibleChanged += ApplyUiThemeOnWindowVisibilityChange;
        }

        private void ApplyUiThemeOnWindowVisibilityChange(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (args is {NewValue: true, OldValue: false} && sender is Window window)
            {
                ApplyUiTheme(window);
            }
        }

        #region WIN32 API to change title bar background color

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private static bool UseImmersiveDarkMode(IntPtr handle, bool enabled)
        {
            int attributeValue = enabled ? 1 : 0;
            var r = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref attributeValue, sizeof(int)) == 0;
            // if (r)
            // {
            //     InvalidateWindow(handle);
            // }
            return r;
        }

        private static void InvalidateWindow(IntPtr hwnd)
        {
            // Force the window to refresh
            var rect = new RECT();
            GetWindowRect(hwnd, ref rect);
            InvalidateRect(hwnd, ref rect, true);
        }

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, ref RECT lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion
    }
}
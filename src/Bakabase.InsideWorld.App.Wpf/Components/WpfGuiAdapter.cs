using System.IO;
using System.Reflection;
using System.Windows;
using Bakabase.Infrastructures;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.App.Wpf.Windows;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Bakabase.InsideWorld.App.Wpf.Components
{
    public class WpfGuiAdapter : GuiAdapter
    {
        private readonly App _app;
        private NotifyIcon? _tray;
        private InitializationWindow? _initializationWindow;
        private ErrorWindow? _errorWindow;
        private MainWindow? _mainWindow;
        private ExitConfirmationDialog? _exitConfirmationDialog;

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
                InitialDirectory = initialDirectory
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
                const int SystemLimit = 127;
                _tray.Text = text.Substring(0, Math.Min(SystemLimit, text.Length));
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
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closing += async (sender, args) =>
                {
                    args.Cancel = true;
                    await onClosing();
                };
                _mainWindow.Title = title;
                _mainWindow.Show();
#if DEBUG
                await _mainWindow.WebView2.EnsureCoreWebView2Async();
#else
                var webview2EnvPath =
                    Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "Microsoft.WebView2.FixedVersionRuntime.101.0.1210.53.x64");
                var env = await CoreWebView2Environment.CreateAsync(webview2EnvPath);
                await _mainWindow.WebView2.EnsureCoreWebView2Async(env);
#endif
            }

            _mainWindow.WebView2.CoreWebView2.Navigate(url);
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
                    new ExitConfirmationDialog(_app.Host.Host.Services
                        .GetRequiredService<IStringLocalizer<AppSharedResource>>());
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
            }

            _exitConfirmationDialog.RememberCheckBox.IsChecked = false;
            _exitConfirmationDialog.Show();
        }

        public override bool ShowConfirmDialog(string message, string caption)
        {
            var result = MessageBox.Show(message, caption, MessageBoxButton.OKCancel);
            return result == MessageBoxResult.OK;
        }

        public override void ChangeUiTheme(UiTheme theme)
        {
            return;
        }
    }
}
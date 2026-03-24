using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Bakabase.Abstractions.Components.Gui;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Infrastructures.Resources;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Controls;
using Bakabase.Windows;
using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Components;

public class AvaloniaGuiAdapter : GuiAdapter, ITrayIconController
{
    private readonly App _app;
    private InitializationWindow? _initializationWindow;
    private ErrorWindow? _errorWindow;
    private MainWindow? _mainWindow;
    private ExitConfirmationDialog? _exitConfirmationDialog;

    public AvaloniaGuiAdapter(App app)
    {
        _app = app;
    }

    public override void InvokeInGuiContext(Action action) =>
        Dispatcher.UIThread.Invoke(action);

    public override T InvokeInGuiContext<T>(Func<T> func) =>
        Dispatcher.UIThread.Invoke(func);

    public void SetTrayIcon(bool isRunning)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var assetName = isRunning ? "tray-running" : "favicon";
            _app.AppTrayIcon.Icon = new WindowIcon(
                AssetLoader.Open(new Uri($"avares://Bakabase/Assets/{assetName}.ico")));
        });
    }

    [GuiContextInterceptor]
    public override void ShowFatalErrorWindow(string message, string title = "Fatal Error")
    {
        _errorWindow ??= new ErrorWindow();
        _errorWindow.Title = title;
        _errorWindow.FindControl<TextBlock>("ErrorTitle")!.Text = title;
        _errorWindow.FindControl<TextBox>("StackTrace")!.Text = message;
        _errorWindow.Show();

        _mainWindow?.Close();
        _initializationWindow?.Close();
    }

    [GuiContextInterceptor]
    public override void ShowInitializationWindow(string processName)
    {
        _initializationWindow ??= new InitializationWindow();
        _initializationWindow.FindControl<TextBlock>("ProcessName")!.Text = processName;
        _initializationWindow.Show();
    }

    [GuiContextInterceptor]
    public override void DestroyInitializationWindow()
    {
        _initializationWindow?.Close();
    }

    [GuiContextInterceptor]
    public override void ShowMainWebView(string url, string title, Func<Task> onClosing)
    {
        try
        {
            _mainWindow ??= new MainWindow();
            _mainWindow.Show();
            _mainWindow.Title = title;

            var webView = _mainWindow.FindControl<NativeWebViewHost>("WebView")!;
            webView.Navigate(url);

            _mainWindow.Closing += async (_, args) =>
            {
                args.Cancel = true;
                await onClosing();
            };
        }
        catch (Exception ex)
        {
            try { _mainWindow?.Close(); } catch { /* ignore */ }
            _mainWindow = null;
            ShowFatalErrorWindow(
                $"Failed to initialize WebView: {ex.Message}\n\n{ex}",
                "WebView Error");
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

    public override bool MainWebViewVisible => _mainWindow?.IsVisible == true;

    [GuiContextInterceptor]
    public override void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [GuiContextInterceptor]
    public override void Hide()
    {
        _mainWindow?.Hide();
    }

    [GuiContextInterceptor]
    public override void Show()
    {
        if (_mainWindow != null)
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    private async void _onExitConfirmationDialogClosing(CloseBehavior behavior,
        Func<CloseBehavior, bool, Task> onClosed)
    {
        var remember = _exitConfirmationDialog?.FindControl<CheckBox>("RememberCheckBox")?.IsChecked ?? false;
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
                new ExitConfirmationDialog(_app.Host!.Host.Services.GetRequiredService<AppLocalizer>());
            _exitConfirmationDialog.FindControl<Button>("ExitBtn")!.Click += (_, _) =>
            {
                _onExitConfirmationDialogClosing(CloseBehavior.Exit, onClosed);
            };
            _exitConfirmationDialog.FindControl<Button>("MinimizeBtn")!.Click += (_, _) =>
            {
                _onExitConfirmationDialogClosing(CloseBehavior.Minimize, onClosed);
            };
            _exitConfirmationDialog.Closing += (_, _) =>
            {
                onClosed(CloseBehavior.Cancel, false);
                _exitConfirmationDialog = null;
            };
        }

        _exitConfirmationDialog.FindControl<CheckBox>("RememberCheckBox")!.IsChecked = false;
        _exitConfirmationDialog.Show();
    }

    [GuiContextInterceptor]
    public override bool ShowConfirmDialog(string message, string caption)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new Window
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                CanResize = false,
                Topmost = true
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            okButton.Click += (_, _) =>
            {
                tcs.TrySetResult(true);
                dialog.Close();
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            cancelButton.Click += (_, _) =>
            {
                tcs.TrySetResult(false);
                dialog.Close();
            };

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 14,
                        Margin = new Avalonia.Thickness(0, 0, 0, 20),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        MaxWidth = 400
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Spacing = 20,
                        Children = { okButton, cancelButton }
                    }
                }
            };

            dialog.Closing += (_, _) =>
            {
                tcs.TrySetResult(false);
            };

            if (_mainWindow != null)
            {
                await dialog.ShowDialog(_mainWindow);
            }
            else
            {
                dialog.Show();
            }
        });

        return tcs.Task.GetAwaiter().GetResult();
    }

    [GuiContextInterceptor]
    public override void ChangeUiTheme(UiTheme theme)
    {
        if (Application.Current == null) return;

        Application.Current.RequestedThemeVariant = theme switch
        {
            UiTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
            UiTheme.Light => Avalonia.Styling.ThemeVariant.Light,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
    }

    public override byte[]? GetIcon(IconType type, string? path)
    {
        // Cross-platform icon extraction is not available without platform-specific APIs.
        // Return null to let the caller handle the fallback.
        return null;
    }
}

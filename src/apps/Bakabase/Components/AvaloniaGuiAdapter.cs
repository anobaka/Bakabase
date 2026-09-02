using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Bakabase.Abstractions.Components.Gui;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Controls;
using Bakabase.Windows;
using Bootstrap.Extensions;

namespace Bakabase.Components;

public class AvaloniaGuiAdapter : GuiAdapter, ITrayIconController
{
    private readonly App _app;
    private InitializationWindow? _initializationWindow;
    private ErrorWindow? _errorWindow;
    private MainWindow? _mainWindow;

    /// <summary>
    /// Set when the app is exiting via <see cref="Shutdown"/> — i.e. programmatic exit
    /// (CloseBehavior.Exit, tray Exit, /restart endpoint, fatal error). Lets the main
    /// window's Closing handler distinguish "user clicked the X" from "we're already on
    /// our way out" and skip the exit prompt in the latter case. Showing the prompt
    /// while Avalonia is tearing down produces a frozen dialog.
    /// </summary>
    private bool _isShuttingDown;

    /// <summary>
    /// While set, <see cref="Shutdown"/> records the request instead of acting on it.
    ///
    /// <see cref="ExitCoordinator"/> stops the web host on its way out, and AppHost registers
    /// <c>ApplicationStopping -> IGuiAdapter.Shutdown</c>. Without this latch that callback
    /// would end the Avalonia lifetime — and with it the process — while the coordinator is
    /// still flushing data, which is precisely the work it stopped the host to allow.
    /// </summary>
    private bool _deferShutdown;

    /// <summary>Owner for modal dialogs. Null until the main window exists.</summary>
    internal Window? MainWindow => _mainWindow;

    public AvaloniaGuiAdapter(App app)
    {
        _app = app;
    }

    /// <summary>
    /// Hands control of the actual teardown to <see cref="ExitCoordinator"/>. Must be paired
    /// with <see cref="CompleteDeferredShutdown"/>.
    /// </summary>
    internal void BeginDeferredShutdown()
    {
        _isShuttingDown = true;
        _deferShutdown = true;
    }

    /// <summary>Ends the Avalonia lifetime for real, once the wind-down is done.</summary>
    internal void CompleteDeferredShutdown()
    {
        _deferShutdown = false;
        Shutdown();
    }

    public override void InvokeInGuiContext(Action action) =>
        Dispatcher.UIThread.Invoke(action);

    public override T InvokeInGuiContext<T>(Func<T> func) =>
        Dispatcher.UIThread.Invoke(func);

    /// <summary>
    /// Cached one per state. BTaskManager flips its running flag whenever any task starts or
    /// finishes, so this is called constantly; building a fresh <see cref="WindowIcon"/> each time
    /// re-decoded the .ico and — on Avalonia before 11.3.8, which had no finalizer on Win32Icon —
    /// leaked a GDI handle per call until the process ran out and tray interactions started
    /// failing. Two long-lived icons and an early-out on "no change" keep that churn at zero.
    /// </summary>
    private readonly Dictionary<bool, WindowIcon> _trayIcons = new();
    private bool? _trayIconIsRunning;

    public void SetTrayIcon(bool isRunning)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (_trayIconIsRunning == isRunning)
            {
                return;
            }

            try
            {
                if (!_trayIcons.TryGetValue(isRunning, out var icon))
                {
                    var assetName = isRunning ? "tray-running" : "favicon";
                    icon = new WindowIcon(
                        AssetLoader.Open(new Uri($"avares://Bakabase/Assets/{assetName}.ico")));
                    _trayIcons[isRunning] = icon;
                }

                _app.AppTrayIcon.Icon = icon;
                _trayIconIsRunning = isRunning;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Avalonia's Win32Icon.CreateIcon throws Win32Exception
                // "操作成功完成" (GetLastError() == 0) on some Windows
                // machines when the tray icon is updated under contention.
                // Updating the icon is purely cosmetic, so swallowing the
                // failure is fine and avoids spamming the error dashboard.
            }
        });
    }

    public void SetTrayIconVisible(bool visible) => _app.SetTrayIconVisible(visible);

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
    public override void ShowInitializationWindow(string processName, string? detail = null, double? fraction = null)
    {
        _initializationWindow ??= new InitializationWindow();
        _initializationWindow.SetPhase(processName, detail, fraction);
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
                // Programmatic shutdown (CloseBehavior.Exit, tray Exit, /restart) goes
                // through Shutdown() → desktop.Shutdown() → this Closing event. The user's
                // intent to leave is already established, and showing the exit prompt
                // during Avalonia teardown produces a frozen dialog.
                if (_isShuttingDown) return;

                args.Cancel = true;

                // Deliberately not `onClosing` (AppHost.TryToExit): that method hides the
                // window before the "tasks are still running" check and double-acts on the
                // tray path. ExitCoordinator owns the whole flow instead — see its remarks.
                await _app.ExitCoordinator.RequestExitAsync(ExitTrigger.WindowClose);
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
        _isShuttingDown = true;

        // The coordinator is mid-teardown and will call CompleteDeferredShutdown when the
        // data is safely on disk. Ending the lifetime here would cut that short.
        if (_deferShutdown)
        {
            return;
        }

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

    /// <summary>
    /// Kept because <see cref="GuiAdapter"/> declares it, but the app's own exit paths go
    /// through <see cref="ExitCoordinator"/> instead — only <c>AppHost.TryToExit</c> calls
    /// this, and nothing we control calls that any more.
    ///
    /// The previous implementation fired <paramref name="onClosed"/> twice per interaction:
    /// picking Exit or Minimize called <c>Close()</c>, whose Closing handler reported
    /// <see cref="CloseBehavior.Cancel"/>, and only then did the real choice arrive.
    /// </summary>
    [GuiContextInterceptor]
    public override void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed)
    {
        _ = PromptAndReportAsync(onClosed);
    }

    private async Task PromptAndReportAsync(Func<CloseBehavior, bool, Task> onClosed)
    {
        var result = await ExitConfirmationDialog.PromptAsync(
            _mainWindow,
            new ExitPromptOptions(AllowMinimize: true, ShowRemember: true, []));

        var behavior = result.Choice switch
        {
            ExitChoice.Minimize => CloseBehavior.Minimize,
            ExitChoice.Exit => CloseBehavior.Exit,
            _ => CloseBehavior.Cancel
        };

        await onClosed(behavior, result.Remember);
    }

    /// <summary>
    /// Synchronous by contract (<see cref="IGuiAdapter"/> returns <see cref="bool"/>), which
    /// makes it a deadlock trap: <see cref="GuiContextInterceptorAttribute"/> already puts us
    /// on the UI thread, so the old body's <c>Dispatcher.UIThread.InvokeAsync(...)</c> queued
    /// the dialog behind the very thread it then blocked with
    /// <c>tcs.Task.GetAwaiter().GetResult()</c> — the job could never run and the app froze.
    ///
    /// Building the dialog inline and pumping a nested <see cref="DispatcherFrame"/> keeps the
    /// blocking signature while letting the UI thread keep servicing input.
    /// </summary>
    [GuiContextInterceptor]
    public override bool ShowConfirmDialog(string message, string caption)
    {
        var confirmed = false;

        var dialog = new Window
        {
            Title = caption,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = _mainWindow is {IsVisible: true}
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
            CanResize = false,
            ShowInTaskbar = false
            // Background intentionally unset: Window picks up the theme's own brush, which
            // follows ChangeUiTheme. Hardcoding one is what made the old exit dialog unreadable
            // in dark mode.
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 88,
            IsDefault = true,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        okButton.Classes.Add("accent");
        okButton.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 88,
            IsCancel = true,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    FontSize = 14,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = {cancelButton, okButton}
                }
            }
        };

        var frame = new DispatcherFrame();
        dialog.Closed += (_, _) => frame.Continue = false;

        if (_mainWindow is {IsVisible: true})
        {
            _ = dialog.ShowDialog(_mainWindow);
        }
        else
        {
            dialog.Show();
        }

        Dispatcher.UIThread.PushFrame(frame);
        return confirmed;
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

    public override IWebViewSession CreateWebViewSession(WebViewSessionOptions options)
    {
        return Dispatcher.UIThread.Invoke(() =>
        {
            var window = new CookieCaptureWindow(
                options.Title,
                options.ConfirmButtonText,
                options.CancelButtonText,
                options.InitialStatusText);

            if (_mainWindow != null)
            {
                // ShowDialog blocks until close; Show() is non-blocking and lets the caller
                // drive the lifecycle via the returned IWebViewSession.
                window.Show(_mainWindow);
            }
            else
            {
                window.Show();
            }

            return (IWebViewSession)new AvaloniaWebViewSession(window);
        });
    }
}

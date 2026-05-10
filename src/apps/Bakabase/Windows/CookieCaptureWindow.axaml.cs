using System;
using Avalonia.Controls;
using Avalonia.Input;
using Bakabase.Controls;

namespace Bakabase.Windows;

/// <summary>
/// Plain UI shell: WebView surface, address bar, status line, Confirm/Cancel buttons.
/// All capture policy (chain rules, cookie mirrors, stale clears, completion detection)
/// lives in the Service-layer orchestrator that drives this window via
/// <see cref="Bakabase.Infrastructures.Components.Gui.IWebViewSession"/>.
/// </summary>
public partial class CookieCaptureWindow : Window
{
    public NativeWebViewHost WebView { get; }

    public event Action? ConfirmClicked;
    public event Action? CancelClicked;

    private readonly TextBlock _statusText;
    private readonly TextBox _addressBar;

    public CookieCaptureWindow() : this("Cookie Capture", "Confirm", "Cancel", "")
    {
    }

    public CookieCaptureWindow(string title, string confirmText, string cancelText, string initialStatus)
    {
        InitializeComponent();

        Title = title;
        WebView = this.FindControl<NativeWebViewHost>("CaptureWebView")!;

        var confirmBtn = this.FindControl<Button>("ConfirmBtn")!;
        var cancelBtn = this.FindControl<Button>("CancelBtn")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _addressBar = this.FindControl<TextBox>("AddressBar")!;
        var goBtn = this.FindControl<Button>("GoBtn")!;

        confirmBtn.Content = confirmText;
        cancelBtn.Content = cancelText;
        _statusText.Text = initialStatus;

        // Address bar mirrors current URL.
        WebView.Navigated += url => _addressBar.Text = url;

        // Address bar can drive the WebView (user-typed URLs).
        goBtn.Click += (_, _) => NavigateToAddressBarUrl();
        _addressBar.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) NavigateToAddressBarUrl();
        };

        confirmBtn.Click += (_, _) => ConfirmClicked?.Invoke();
        cancelBtn.Click += (_, _) =>
        {
            CancelClicked?.Invoke();
            Close();
        };
    }

    public void SetStatusText(string text) => _statusText.Text = text;

    private void NavigateToAddressBarUrl()
    {
        var url = _addressBar.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        if (!url.Contains("://")) url = "https://" + url;
        WebView.Navigate(url);
    }
}

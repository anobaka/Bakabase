using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Bakabase.Controls;

namespace Bakabase.Windows;

public partial class CookieCaptureWindow : Window
{
    private readonly string[] _cookieUrls;
    private readonly TaskCompletionSource<string?> _tcs;
    private readonly NativeWebViewHost _webView;
    private readonly Button _confirmBtn;
    private readonly Button _cancelBtn;
    private readonly TextBlock _statusText;
    private readonly TextBox _addressBar;
    private readonly Button _goBtn;
    private bool _completing;

    public CookieCaptureWindow()
    {
        InitializeComponent();
        _cookieUrls = [];
        _tcs = new TaskCompletionSource<string?>();
        _webView = this.FindControl<NativeWebViewHost>("CaptureWebView")!;
        _confirmBtn = this.FindControl<Button>("ConfirmBtn")!;
        _cancelBtn = this.FindControl<Button>("CancelBtn")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _addressBar = this.FindControl<TextBox>("AddressBar")!;
        _goBtn = this.FindControl<Button>("GoBtn")!;
    }

    public CookieCaptureWindow(
        string startUrl,
        string title,
        string[] cookieUrls,
        TaskCompletionSource<string?> tcs,
        CookieCaptureWindowLabels? labels = null)
    {
        InitializeComponent();
        _cookieUrls = cookieUrls;
        _tcs = tcs;

        var l = labels ?? CookieCaptureWindowLabels.Default;

        Title = title;
        _webView = this.FindControl<NativeWebViewHost>("CaptureWebView")!;
        _confirmBtn = this.FindControl<Button>("ConfirmBtn")!;
        _cancelBtn = this.FindControl<Button>("CancelBtn")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _addressBar = this.FindControl<TextBox>("AddressBar")!;
        _goBtn = this.FindControl<Button>("GoBtn")!;

        _confirmBtn.Content = l.Confirm;
        _cancelBtn.Content = l.Cancel;
        _statusText.Text = l.WaitingForLogin;
        _addressBar.Text = startUrl;

        _webView.Navigate(startUrl);

        // Sync address bar when WebView navigates
        _webView.Navigated += url => _addressBar.Text = url;

        // Address bar navigation
        _goBtn.Click += (_, _) => NavigateToAddressBarUrl();
        _addressBar.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) NavigateToAddressBarUrl();
        };

        _confirmBtn.Click += async (_, _) => await CompleteCapture(l);

        _cancelBtn.Click += (_, _) =>
        {
            _tcs.TrySetResult(null);
            Close();
        };

        Closing += (_, _) =>
        {
            _tcs.TrySetResult(null);
        };
    }

    private void NavigateToAddressBarUrl()
    {
        var url = _addressBar.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        if (!url.Contains("://")) url = "https://" + url;
        _webView.Navigate(url);
    }

    private async Task CompleteCapture(CookieCaptureWindowLabels labels)
    {
        if (_completing) return;
        _completing = true;

        _confirmBtn.IsEnabled = false;
        _cancelBtn.IsEnabled = false;
        _statusText.Text = labels.ExtractingCookies;

        try
        {
            var cookies = await _webView.GetCookiesAsync(_cookieUrls);
            _tcs.TrySetResult(cookies);
            Close();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"{labels.Error}: {ex.Message}";
            _confirmBtn.IsEnabled = true;
            _cancelBtn.IsEnabled = true;
            _completing = false;
        }
    }

    public Task<string?> WaitForResultAsync() => _tcs.Task;
}

public record CookieCaptureWindowLabels
{
    public string Confirm { get; init; } = "Confirm";
    public string Cancel { get; init; } = "Cancel";
    public string WaitingForLogin { get; init; } = "Please log in, then click Confirm";
    public string ExtractingCookies { get; init; } = "Extracting cookies...";
    public string Error { get; init; } = "Error";

    public static CookieCaptureWindowLabels Default { get; } = new();
}

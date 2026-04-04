using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Bakabase.Controls;

namespace Bakabase.Windows;

public partial class CookieCaptureWindow : Window
{
    private readonly string[] _cookieUrls;
    private readonly TaskCompletionSource<string?> _tcs;
    private readonly Func<string, (bool Done, string? NavigateToUrl)>? _onTick;
    private readonly NativeWebViewHost _webView;
    private readonly Button _confirmBtn;
    private readonly Button _cancelBtn;
    private readonly TextBlock _statusText;
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
    }

    public CookieCaptureWindow(
        string startUrl,
        string title,
        string[] cookieUrls,
        TaskCompletionSource<string?> tcs,
        Func<string, (bool Done, string? NavigateToUrl)>? onTick = null,
        CookieCaptureWindowLabels? labels = null)
    {
        InitializeComponent();
        _cookieUrls = cookieUrls;
        _tcs = tcs;
        _onTick = onTick;

        var l = labels ?? CookieCaptureWindowLabels.Default;

        Title = title;
        _webView = this.FindControl<NativeWebViewHost>("CaptureWebView")!;
        _confirmBtn = this.FindControl<Button>("ConfirmBtn")!;
        _cancelBtn = this.FindControl<Button>("CancelBtn")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;

        _confirmBtn.Content = l.Confirm;
        _cancelBtn.Content = l.Cancel;
        _statusText.Text = l.WaitingForLogin;

        _webView.Navigate(startUrl);

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

        if (_onTick != null)
        {
            StartUrlPolling(l);
        }
    }

    private void StartUrlPolling(CookieCaptureWindowLabels labels)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (_completing) return;

            var currentUrl = _webView.GetCurrentUrl();
            if (currentUrl == null) return;

            var result = _onTick!(currentUrl);

            if (result.Done)
            {
                timer.Stop();
                _statusText.Text = labels.LoginDetected;
                await CompleteCapture(labels);
            }
            else if (result.NavigateToUrl != null)
            {
                _statusText.Text = string.Format(labels.NavigatingTo, new Uri(result.NavigateToUrl).Host);
                _webView.Navigate(result.NavigateToUrl);
            }
        };
        timer.Start();

        Closing += (_, _) => timer.Stop();
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
    public string WaitingForLogin { get; init; } = "Please log in, or click Confirm if already logged in";
    public string LoginDetected { get; init; } = "Login detected, extracting cookies...";
    public string ExtractingCookies { get; init; } = "Extracting cookies...";
    public string NavigatingTo { get; init; } = "Navigating to {0}..."; // {0} = host
    public string Error { get; init; } = "Error";

    public static CookieCaptureWindowLabels Default { get; } = new();
}

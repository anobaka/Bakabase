using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Bakabase.Shell.Controls;
using Bakabase.Shell.Components;

namespace Bakabase.Shell.Windows;

/// <summary>The shared setup page, before any application options or data services exist.</summary>
public sealed class SetupWindow : Window
{
    private readonly NativeWebViewHost _webView;
    private readonly string _temporaryProfile = Path.Combine(Path.GetTempPath(), "bakabase-setup-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _cancelled = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _continuingStartup;
    private bool _pickerOpen;

    public CancellationToken Cancelled => _cancelled.Token;
    public Task ClosedTask => _closed.Task;

    public SetupWindow()
    {
        Title = "Bakabase";
        Width = 1000;
        Height = 800;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _webView = new NativeWebViewHost { WindowsUserDataDirectory = _temporaryProfile };
        Content = _webView;
        Closed += (_, _) =>
        {
            if (!_continuingStartup) _cancelled.Cancel();
            _closed.TrySetResult();
            // Native activation callbacks must already observe this window as closed.
            MacOSDockVisibility.SetVisible(false);
            // WebView2 may hold files until its process exits. Never delay exit for a cache.
            try { if (Directory.Exists(_temporaryProfile)) Directory.Delete(_temporaryProfile, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }

    public void Navigate(string url)
    {
        // The coordinator can navigate to progress before the page handles the apply
        // response. Keep the language chosen in this window across that handoff.
        var language = CurrentPageLanguage();
        var page = new Uri(url);
        if (language != null && HttpUtility.ParseQueryString(page.Fragment.TrimStart('#'))["lang"] == null)
        {
            var builder = new UriBuilder(page);
            var fragment = page.Fragment.TrimStart('#');
            builder.Fragment = (fragment.Length > 0 ? fragment + "&" : "") + "lang=" + language;
            url = builder.Uri.AbsoluteUri;
        }
        _webView.Navigate(url);
    }

    private string? CurrentPageLanguage()
    {
        if (!Uri.TryCreate(_webView.GetCurrentUrl(), UriKind.Absolute, out var page)) return null;
        var language = HttpUtility.ParseQueryString(page.Fragment.TrimStart('#'))["lang"];
        return language is "cn" or "en" ? language : null;
    }

    public void BringToFront()
    {
        if (_closed.Task.IsCompleted) return;
        MacOSDockVisibility.SetVisible(true);
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public async Task<string?> PickDirectoryAsync() => await Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (_closed.Task.IsCompleted || _pickerOpen) return null;
        _pickerOpen = true;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = (CurrentPageLanguage() ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) is "cn" or "zh"
                    ? "选择目录" : "Choose a directory",
                AllowMultiple = false
            });
            return folders.FirstOrDefault()?.TryGetLocalPath();
        }
        finally { _pickerOpen = false; }
    });

    public void ShowError(string error)
    {
        Content = new TextBox
        {
            Text = error, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(24)
        };
        BringToFront();
    }

    public void ContinueStartup()
    {
        _continuingStartup = true;
        Close();
    }
}

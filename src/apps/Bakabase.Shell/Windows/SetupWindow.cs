using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Bakabase.Shell.Controls;

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

    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "cn" : "en";
    public CancellationToken Cancelled => _cancelled.Token;
    public Task ClosedTask => _closed.Task;

    public SetupWindow()
    {
        Title = Language == "cn" ? "Bakabase · 初始化" : "Bakabase · Setup";
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
            // WebView2 may hold files until its process exits. Never delay exit for a cache.
            try { if (Directory.Exists(_temporaryProfile)) Directory.Delete(_temporaryProfile, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }

    public void Navigate(string url) => _webView.Navigate(url);

    public void BringToFront()
    {
        if (_closed.Task.IsCompleted) return;
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
                Title = Language == "cn" ? "选择目录" : "Choose a directory",
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

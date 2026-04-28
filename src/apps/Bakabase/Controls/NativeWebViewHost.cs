using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Bakabase.Controls;

/// <summary>
/// Cross-platform WebView host using Avalonia's NativeControlHost.
/// Uses platform-native WebView engines via P/Invoke:
/// - macOS: WKWebView (WebKit framework)
/// - Windows: WebView2 (Microsoft Edge)
/// - Linux: WebKitGTK
/// </summary>
public partial class NativeWebViewHost : NativeControlHost
{
    private string? _pendingUrl;
    private bool _initialized;

    /// <summary>
    /// Fired when the WebView navigates to a new URL.
    /// </summary>
    public event Action<string>? Navigated;

    public void Navigate(string url)
    {
        Console.WriteLine($"[NativeWebViewHost] Navigate called: url={url}, initialized={_initialized}");
        _pendingUrl = url;
        if (_initialized)
            PlatformNavigate(url);
    }

    /// <summary>
    /// Gets the current URL displayed in the WebView.
    /// </summary>
    public string? GetCurrentUrl()
    {
        if (!_initialized) return null;

        if (OperatingSystem.IsWindows())
            return GetCurrentUrlWindows();
        if (OperatingSystem.IsMacOS())
            return GetCurrentUrlMacOS();
        if (OperatingSystem.IsLinux())
            return GetCurrentUrlLinux();

        return null;
    }

    /// <summary>
    /// Extracts cookies from the WebView for the given URLs.
    /// Returns a cookie header string (e.g. "name1=value1; name2=value2").
    /// </summary>
    public async Task<string?> GetCookiesAsync(string[] cookieUrls)
    {
        if (!_initialized) return null;

        if (OperatingSystem.IsWindows())
            return await GetCookiesWindows(cookieUrls);
        if (OperatingSystem.IsMacOS())
            return await GetCookiesMacOS(cookieUrls);
        if (OperatingSystem.IsLinux())
            return await GetCookiesLinux(cookieUrls);

        return null;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        Console.WriteLine($"[NativeWebViewHost] CreateNativeControlCore called, pendingUrl={_pendingUrl}");

        if (OperatingSystem.IsMacOS())
            return CreateMacOS(parent);
        if (OperatingSystem.IsWindows())
            return CreateWindows(parent);
        if (OperatingSystem.IsLinux())
            return CreateLinux(parent);

        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (OperatingSystem.IsMacOS())
            DestroyMacOS();
        else if (OperatingSystem.IsWindows())
            DestroyWindows();
        else if (OperatingSystem.IsLinux())
            DestroyLinux();

        _initialized = false;
    }

    private void PlatformNavigate(string url)
    {
        if (OperatingSystem.IsMacOS())
            NavigateMacOS(url);
        else if (OperatingSystem.IsWindows())
            NavigateWindows(url);
        else if (OperatingSystem.IsLinux())
            NavigateLinux(url);
    }
}

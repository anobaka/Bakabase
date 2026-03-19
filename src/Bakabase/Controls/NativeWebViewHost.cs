using System;
using System.Runtime.InteropServices;
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

    public void Navigate(string url)
    {
        _pendingUrl = url;
        if (_initialized)
            PlatformNavigate(url);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
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

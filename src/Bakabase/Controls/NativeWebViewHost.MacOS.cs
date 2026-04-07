using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace Bakabase.Controls;

public partial class NativeWebViewHost
{
    private IntPtr _macWebView;
    private IntPtr _macConfig;

    private static bool _macIconSet;

    private IPlatformHandle CreateMacOS(IPlatformHandle parent)
    {
        // Ensure WebKit framework is loaded
        ObjC.dlopen("/System/Library/Frameworks/WebKit.framework/WebKit", ObjC.RTLD_NOW);

        // Set the Dock icon on first creation (macOS needs this for non-bundled apps)
        if (!_macIconSet)
        {
            _macIconSet = true;
            try
            {
                SetMacOSDockIcon();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NativeWebViewHost] Failed to set Dock icon: {ex.Message}");
            }
        }

        // Create WKWebViewConfiguration
        _macConfig = ObjC.SendIntPtr(
            ObjC.SendIntPtr(ObjC.objc_getClass("WKWebViewConfiguration"), ObjC.Sel("alloc")),
            ObjC.Sel("init"));

        // Enable developer extras on preferences (for Safari Web Inspector)
        var prefs = ObjC.SendIntPtr(_macConfig, ObjC.Sel("preferences"));
        var devExtrasKey = ObjC.CreateNSString("developerExtrasEnabled");
        var nsYes = ObjC.SendIntPtr_Bool(
            ObjC.objc_getClass("NSNumber"), ObjC.Sel("numberWithBool:"), true);
        ObjC.SendVoid_IntPtr_IntPtr(prefs, ObjC.Sel("setValue:forKey:"), nsYes, devExtrasKey);
        ObjC.SendVoid(devExtrasKey, ObjC.Sel("release"));

        // Create WKWebView with initWithFrame:CGRectZero configuration:config
        var wkClass = ObjC.objc_getClass("WKWebView");
        var alloc = ObjC.SendIntPtr(wkClass, ObjC.Sel("alloc"));
        _macWebView = ObjC.SendIntPtr_CGRect_IntPtr(
            alloc, ObjC.Sel("initWithFrame:configuration:"),
            0, 0, 0, 0, // CGRectZero - NativeControlHost manages sizing
            _macConfig);

        // Do NOT set autoresizingMask - NativeControlHost manages the frame directly.
        // Setting autoresizingMask causes double-sizing issues on Retina displays.

        // Set a modern User-Agent to avoid "browser version too low" errors
        var userAgent = ObjC.CreateNSString(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        ObjC.SendVoid_IntPtr(_macWebView, ObjC.Sel("setCustomUserAgent:"), userAgent);
        ObjC.SendVoid(userAgent, ObjC.Sel("release"));

        // Enable Web Inspector (macOS 13.3+ Ventura)
        // setInspectable: is only available on macOS 13.3+, use respondsToSelector: to check
        var inspectableSel = ObjC.Sel("setInspectable:");
        if (ObjC.SendBool(_macWebView, ObjC.Sel("respondsToSelector:"), inspectableSel))
        {
            ObjC.SendVoid_Bool(_macWebView, inspectableSel, true);
        }

        // Listen for size changes to manually sync WKWebView frame
        SizeChanged += OnSizeChangedMacOS;

        _initialized = true;

        if (_pendingUrl != null)
            NavigateMacOS(_pendingUrl);

        return new PlatformHandle(_macWebView, "NSView");
    }

    private void OnSizeChangedMacOS(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (_macWebView == IntPtr.Zero) return;

        var w = e.NewSize.Width;
        var h = e.NewSize.Height;
        Console.WriteLine($"[NativeWebViewHost] SizeChanged: Avalonia={w}x{h}");

        // Set the WKWebView frame to match Avalonia's layout size (in points)
        ObjC.SendVoid_CGRect(_macWebView, ObjC.Sel("setFrame:"), 0, 0, w, h);
    }

    private void NavigateMacOS(string url)
    {
        if (_macWebView == IntPtr.Zero) return;

        System.Diagnostics.Debug.WriteLine($"NativeWebViewHost: Navigating to {url}");

        // Create NSURL from string
        var nsUrlString = ObjC.CreateNSString(url);
        var nsUrl = ObjC.SendIntPtr_IntPtr(
            ObjC.objc_getClass("NSURL"), ObjC.Sel("URLWithString:"), nsUrlString);

        if (nsUrl == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine($"NativeWebViewHost: NSURL creation failed for '{url}'");
            ObjC.SendVoid(nsUrlString, ObjC.Sel("release"));
            return;
        }

        // Create NSURLRequest
        var request = ObjC.SendIntPtr_IntPtr(
            ObjC.objc_getClass("NSURLRequest"), ObjC.Sel("requestWithURL:"), nsUrl);

        if (request == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("NativeWebViewHost: NSURLRequest creation failed");
            ObjC.SendVoid(nsUrlString, ObjC.Sel("release"));
            return;
        }

        // Load request
        ObjC.SendVoid_IntPtr(_macWebView, ObjC.Sel("loadRequest:"), request);

        // Release NSString (NSURL and NSURLRequest are autoreleased by class methods)
        ObjC.SendVoid(nsUrlString, ObjC.Sel("release"));
    }

    private static void SetMacOSDockIcon()
    {
        // Try to find favicon.png relative to the executable
        var exeDir = AppContext.BaseDirectory;
        var iconPath = System.IO.Path.Combine(exeDir, "Assets", "dock-icon.png");
        if (!System.IO.File.Exists(iconPath))
            iconPath = System.IO.Path.Combine(exeDir, "dock-icon.png");
        if (!System.IO.File.Exists(iconPath))
        {
            // Try relative to working directory
            iconPath = System.IO.Path.Combine("Assets", "dock-icon.png");
        }

        if (!System.IO.File.Exists(iconPath))
        {
            Console.WriteLine($"[NativeWebViewHost] Icon file not found, tried Assets/dock-icon.png");
            return;
        }

        var nsStringPath = ObjC.CreateNSString(iconPath);
        var nsImage = ObjC.SendIntPtr_IntPtr(
            ObjC.SendIntPtr(ObjC.objc_getClass("NSImage"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithContentsOfFile:"), nsStringPath);

        if (nsImage != IntPtr.Zero)
        {
            var nsApp = ObjC.SendIntPtr(ObjC.objc_getClass("NSApplication"), ObjC.Sel("sharedApplication"));
            ObjC.SendVoid_IntPtr(nsApp, ObjC.Sel("setApplicationIconImage:"), nsImage);
            Console.WriteLine($"[NativeWebViewHost] Dock icon set from {iconPath}");
        }
        else
        {
            Console.WriteLine($"[NativeWebViewHost] Failed to create NSImage from {iconPath}");
        }

        ObjC.SendVoid(nsStringPath, ObjC.Sel("release"));
    }

    private string? GetCurrentUrlMacOS()
    {
        if (_macWebView == IntPtr.Zero) return null;
        try
        {
            var urlObj = ObjC.SendIntPtr(_macWebView, ObjC.Sel("URL"));
            if (urlObj == IntPtr.Zero) return null;
            var absStr = ObjC.SendIntPtr(urlObj, ObjC.Sel("absoluteString"));
            if (absStr == IntPtr.Zero) return null;
            var utf8Ptr = ObjC.SendIntPtr(absStr, ObjC.Sel("UTF8String"));
            return utf8Ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(utf8Ptr) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts cookies from WKWebView via NSHTTPCookieStorage.
    /// On macOS, the default WKWebsiteDataStore syncs cookies to the shared
    /// NSHTTPCookieStorage, so we can read them including HttpOnly cookies.
    /// Falls back to JS document.cookie if no cookies found.
    /// </summary>
    private async Task<string?> GetCookiesMacOS(string[] cookieUrls)
    {
        if (_macWebView == IntPtr.Zero) return null;

        try
        {
            var allCookies = new Dictionary<string, string>();
            var sharedStorage = ObjC.SendIntPtr(
                ObjC.objc_getClass("NSHTTPCookieStorage"), ObjC.Sel("sharedHTTPCookieStorage"));

            if (sharedStorage == IntPtr.Zero)
            {
                return await GetCookiesMacOSFallback(cookieUrls);
            }

            foreach (var url in cookieUrls)
            {
                var nsUrlString = ObjC.CreateNSString(url);
                var nsUrl = ObjC.SendIntPtr_IntPtr(
                    ObjC.objc_getClass("NSURL"), ObjC.Sel("URLWithString:"), nsUrlString);

                if (nsUrl != IntPtr.Zero)
                {
                    var cookies = ObjC.SendIntPtr_IntPtr(sharedStorage, ObjC.Sel("cookiesForURL:"), nsUrl);
                    if (cookies != IntPtr.Zero)
                    {
                        var count = (int)ObjC.SendNInt(cookies, ObjC.Sel("count"));
                        for (var i = 0; i < count; i++)
                        {
                            var cookie = ObjC.SendIntPtr_NInt(cookies, ObjC.Sel("objectAtIndex:"), i);
                            var namePtr = ObjC.SendIntPtr(cookie, ObjC.Sel("name"));
                            var valuePtr = ObjC.SendIntPtr(cookie, ObjC.Sel("value"));

                            var nameUtf8 = ObjC.SendIntPtr(namePtr, ObjC.Sel("UTF8String"));
                            var valueUtf8 = ObjC.SendIntPtr(valuePtr, ObjC.Sel("UTF8String"));

                            var name = nameUtf8 != IntPtr.Zero ? Marshal.PtrToStringUTF8(nameUtf8) : null;
                            var value = valueUtf8 != IntPtr.Zero ? Marshal.PtrToStringUTF8(valueUtf8) : null;

                            if (name != null && value != null)
                            {
                                allCookies.TryAdd(name, value);
                            }
                        }
                    }
                }

                ObjC.SendVoid(nsUrlString, ObjC.Sel("release"));
            }

            if (allCookies.Count > 0)
            {
                return string.Join("; ", allCookies.Select(kv => $"{kv.Key}={kv.Value}"));
            }

            // Shared storage might not have synced yet, fall back to JS
            return await GetCookiesMacOSFallback(cookieUrls);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetCookiesMacOS failed: {ex}");
            return await GetCookiesMacOSFallback(cookieUrls);
        }
    }

    /// <summary>
    /// Fallback: extract cookies via JS document.cookie (cannot get HttpOnly cookies).
    /// </summary>
    private async Task<string?> GetCookiesMacOSFallback(string[] cookieUrls)
    {
        var allCookies = new Dictionary<string, string>();

        foreach (var url in cookieUrls)
        {
            NavigateMacOS(url);
            await Task.Delay(2000);

            const string marker = "__BAKABASE_COOKIE__";
            var js = ObjC.CreateNSString($"document.title = '{marker}' + document.cookie");
            ObjC.SendVoid_IntPtr_IntPtr(_macWebView, ObjC.Sel("evaluateJavaScript:completionHandler:"), js, IntPtr.Zero);
            ObjC.SendVoid(js, ObjC.Sel("release"));

            await Task.Delay(500);

            var titlePtr = ObjC.SendIntPtr(_macWebView, ObjC.Sel("title"));
            if (titlePtr != IntPtr.Zero)
            {
                var utf8Ptr = ObjC.SendIntPtr(titlePtr, ObjC.Sel("UTF8String"));
                if (utf8Ptr != IntPtr.Zero)
                {
                    var title = Marshal.PtrToStringUTF8(utf8Ptr);
                    if (title != null && title.StartsWith(marker))
                    {
                        var cookieStr = title[marker.Length..];
                        foreach (var pair in cookieStr.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        {
                            var eqIdx = pair.IndexOf('=');
                            if (eqIdx > 0)
                                allCookies.TryAdd(pair[..eqIdx].Trim(), pair[(eqIdx + 1)..].Trim());
                        }
                    }
                }
            }
        }

        if (allCookies.Count == 0) return null;
        return string.Join("; ", allCookies.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private void DestroyMacOS()
    {
        SizeChanged -= OnSizeChangedMacOS;

        if (_macWebView != IntPtr.Zero)
        {
            ObjC.SendVoid(_macWebView, ObjC.Sel("release"));
            _macWebView = IntPtr.Zero;
        }

        if (_macConfig != IntPtr.Zero)
        {
            ObjC.SendVoid(_macConfig, ObjC.Sel("release"));
            _macConfig = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Low-level ObjC runtime P/Invoke declarations.
    /// Uses objc_msgSend directly to avoid the broken ObjCRuntime.Class.Register path
    /// that causes AmbiguousMatchException on .NET 9.
    /// </summary>
    private static class ObjC
    {
        public const int RTLD_NOW = 2;

        [DllImport("libdl.dylib")]
        public static extern IntPtr dlopen(string path, int mode);

        [DllImport("libobjc.dylib", EntryPoint = "objc_getClass")]
        public static extern IntPtr objc_getClass(string name);

        [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
        public static extern IntPtr sel_registerName(string name);

        public static IntPtr Sel(string name) => sel_registerName(name);

        // objc_msgSend: () -> IntPtr
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

        // objc_msgSend: (IntPtr) -> IntPtr
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendIntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        // objc_msgSend: (bool) -> IntPtr [numberWithBool:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendIntPtr_Bool(IntPtr receiver, IntPtr selector,
            [MarshalAs(UnmanagedType.I1)] bool arg1);

        // objc_msgSend: (CGRect, IntPtr) -> IntPtr [initWithFrame:configuration:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendIntPtr_CGRect_IntPtr(IntPtr receiver, IntPtr selector,
            double x, double y, double width, double height, IntPtr arg);

        // objc_msgSend: () -> void
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void SendVoid(IntPtr receiver, IntPtr selector);

        // objc_msgSend: (IntPtr) -> void
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void SendVoid_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        // objc_msgSend: (IntPtr, IntPtr) -> void [setValue:forKey:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void SendVoid_IntPtr_IntPtr(IntPtr receiver, IntPtr selector,
            IntPtr arg1, IntPtr arg2);

        // objc_msgSend: (ulong) -> void [setAutoresizingMask:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void SendVoid_ULong(IntPtr receiver, IntPtr selector, ulong arg1);

        // objc_msgSend: (CGRect) -> void [setFrame:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void SendVoid_CGRect(IntPtr receiver, IntPtr selector,
            double x, double y, double width, double height);

        // objc_msgSend: (bool) -> void [setInspectable:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void SendVoid_Bool(IntPtr receiver, IntPtr selector,
            [MarshalAs(UnmanagedType.I1)] bool arg1);

        // objc_msgSend: (IntPtr) -> bool [respondsToSelector:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr arg1);

        // objc_msgSend: () -> nint (for count, etc.)
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern nint SendNInt(IntPtr receiver, IntPtr selector);

        // objc_msgSend: (nint) -> IntPtr [objectAtIndex:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr SendIntPtr_NInt(IntPtr receiver, IntPtr selector, nint arg1);

        public static IntPtr CreateNSString(string str)
        {
            var nsStringClass = objc_getClass("NSString");
            var alloc = SendIntPtr(nsStringClass, Sel("alloc"));
            var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(str + '\0');
            var ptr = Marshal.AllocHGlobal(utf8Bytes.Length);
            Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);
            var result = SendIntPtr_IntPtr(alloc, Sel("initWithUTF8String:"), ptr);
            Marshal.FreeHGlobal(ptr);
            return result;
        }
    }

    /// <summary>
    /// Simple IPlatformHandle implementation for returning native view pointers.
    /// </summary>
    private class PlatformHandle : IPlatformHandle
    {
        public IntPtr Handle { get; }
        public string HandleDescriptor { get; }

        public PlatformHandle(IntPtr handle, string descriptor)
        {
            Handle = handle;
            HandleDescriptor = descriptor;
        }
    }
}

using System;
using System.Runtime.InteropServices;
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

        // Set autoresizing mask so it fills the host area when resized
        // NSViewWidthSizable (2) | NSViewHeightSizable (16) = 18
        ObjC.SendVoid_ULong(_macWebView, ObjC.Sel("setAutoresizingMask:"), 18);

        // Enable Web Inspector (macOS 13.3+ Ventura)
        // setInspectable: is only available on macOS 13.3+, use respondsToSelector: to check
        var inspectableSel = ObjC.Sel("setInspectable:");
        if (ObjC.SendBool(_macWebView, ObjC.Sel("respondsToSelector:"), inspectableSel))
        {
            ObjC.SendVoid_Bool(_macWebView, inspectableSel, true);
        }

        _initialized = true;

        if (_pendingUrl != null)
            NavigateMacOS(_pendingUrl);

        return new PlatformHandle(_macWebView, "NSView");
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
        var iconPath = System.IO.Path.Combine(exeDir, "Assets", "favicon.png");
        if (!System.IO.File.Exists(iconPath))
            iconPath = System.IO.Path.Combine(exeDir, "favicon.png");
        if (!System.IO.File.Exists(iconPath))
        {
            // Try relative to working directory
            iconPath = System.IO.Path.Combine("Assets", "favicon.png");
        }

        if (!System.IO.File.Exists(iconPath))
        {
            Console.WriteLine($"[NativeWebViewHost] Icon file not found, tried Assets/favicon.png");
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

    private void DestroyMacOS()
    {
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

        // objc_msgSend: (bool) -> void [setInspectable:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void SendVoid_Bool(IntPtr receiver, IntPtr selector,
            [MarshalAs(UnmanagedType.I1)] bool arg1);

        // objc_msgSend: (IntPtr) -> bool [respondsToSelector:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr arg1);

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

using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Bakabase.Controls;

public partial class NativeWebViewHost
{
    private IntPtr _macWebView;
    private IntPtr _macConfig;

    private IPlatformHandle CreateMacOS(IPlatformHandle parent)
    {
        // Ensure WebKit framework is loaded
        ObjC.dlopen("/System/Library/Frameworks/WebKit.framework/WebKit", ObjC.RTLD_NOW);

        // Create WKWebViewConfiguration
        _macConfig = ObjC.SendIntPtr(
            ObjC.SendIntPtr(ObjC.objc_getClass("WKWebViewConfiguration"), ObjC.Sel("alloc")),
            ObjC.Sel("init"));

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

        _initialized = true;

        if (_pendingUrl != null)
            NavigateMacOS(_pendingUrl);

        return new PlatformHandle(_macWebView, "NSView");
    }

    private void NavigateMacOS(string url)
    {
        if (_macWebView == IntPtr.Zero) return;

        // Create NSURL from string
        var nsUrlString = ObjC.CreateNSString(url);
        var nsUrl = ObjC.SendIntPtr_IntPtr(
            ObjC.objc_getClass("NSURL"), ObjC.Sel("URLWithString:"), nsUrlString);

        // Create NSURLRequest
        var request = ObjC.SendIntPtr_IntPtr(
            ObjC.objc_getClass("NSURLRequest"), ObjC.Sel("requestWithURL:"), nsUrl);

        // Load request
        ObjC.SendVoid_IntPtr(_macWebView, ObjC.Sel("loadRequest:"), request);

        // Release temporary objects (NSURL and NSURLRequest are autoreleased by class methods,
        // but NSString from alloc/init needs release)
        ObjC.SendVoid(nsUrlString, ObjC.Sel("release"));
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

        // objc_msgSend: (ulong) -> void [setAutoresizingMask:]
        [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
        public static extern void SendVoid_ULong(IntPtr receiver, IntPtr selector, ulong arg1);

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Bakabase.Controls;

public partial class NativeWebViewHost
{
    private IntPtr _winHwnd;
    private IntPtr _winWndProcDelegate; // prevent GC
    private object? _winController; // CoreWebView2Controller (typed via dynamic to avoid hard compile-time dep)
    private object? _winWebView; // CoreWebView2
    private bool _winWebView2Ready;

    private IPlatformHandle CreateWindows(IPlatformHandle parent)
    {
        // Register a simple window class for hosting WebView2
        var className = "BakabaseWebViewHost_" + GetHashCode();
        var wndClass = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = Win32.DefWindowProcW,
            hInstance = Win32.GetModuleHandleW(null),
            lpszClassName = className
        };
        Win32.RegisterClassExW(ref wndClass);

        // Create child window
        _winHwnd = Win32.CreateWindowExW(
            0, className, "",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN,
            0, 0, 1, 1,
            parent.Handle, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

        _initialized = true;

        // Listen for size changes to resize WebView2
        SizeChanged += OnSizeChangedWindows;

        // Initialize WebView2 asynchronously
        _ = InitWebView2Async();

        return new PlatformHandle(_winHwnd, "HWND");
    }

    private void OnSizeChangedWindows(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (_winHwnd == IntPtr.Zero) return;

        var w = (int)e.NewSize.Width;
        var h = (int)e.NewSize.Height;

        // Resize the host HWND
        Win32.SetWindowPos(_winHwnd, IntPtr.Zero, 0, 0, w, h,
            Win32.SWP_NOZORDER | Win32.SWP_NOMOVE | Win32.SWP_NOACTIVATE);

        // Resize WebView2 controller bounds
        ResizeWebView2();
    }

    private async Task InitWebView2Async()
    {
        try
        {
            // Use reflection to load Microsoft.Web.WebView2.Core types
            // This avoids a hard compile-time dependency on the Windows-only native loader
            var coreAssembly = System.Reflection.Assembly.Load("Microsoft.Web.WebView2.Core");
            var envType = coreAssembly.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment")!;

            // CreateAsync(string?, string?, CoreWebView2EnvironmentOptions?) - all nullable
            var createMethods = envType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var createMethod = createMethods.First(m => m.Name == "CreateAsync" && m.GetParameters().Length == 3);

            // CoreWebView2Environment.CreateAsync(null, null, null)
            var envTask = (Task)createMethod.Invoke(null, new object?[] { null, null, null })!;
            await envTask;
            var env = envTask.GetType().GetProperty("Result")!.GetValue(envTask);

            // env.CreateCoreWebView2ControllerAsync(hwnd)
            var createControllerMethod = env!.GetType().GetMethod("CreateCoreWebView2ControllerAsync",
                new[] { typeof(IntPtr) })!;
            var controllerTask = (Task)createControllerMethod.Invoke(env, new object[] { _winHwnd })!;
            await controllerTask;
            _winController = controllerTask.GetType().GetProperty("Result")!.GetValue(controllerTask);

            // Get CoreWebView2
            _winWebView = _winController!.GetType().GetProperty("CoreWebView2")!.GetValue(_winController);

            // Set a modern User-Agent to avoid "browser version too low" errors on sites like Bilibili
            try
            {
                var settings = _winWebView!.GetType().GetProperty("Settings")?.GetValue(_winWebView);
                settings?.GetType().GetProperty("UserAgent")?.SetValue(settings,
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set WebView2 UserAgent: {ex.Message}");
            }

            // Resize to fill parent
            ResizeWebView2();

            _winWebView2Ready = true;

            // Navigate if URL was set before WebView2 was ready
            if (_pendingUrl != null)
                NavigateWindows(_pendingUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 initialization failed: {ex}");
        }
    }

    private void ResizeWebView2()
    {
        if (_winController == null || _winHwnd == IntPtr.Zero) return;

        Win32.GetClientRect(_winHwnd, out var rect);

        // controller.Bounds = new Rectangle(0, 0, width, height)
        var boundsProperty = _winController.GetType().GetProperty("Bounds");
        if (boundsProperty != null)
        {
            // CoreWebView2Controller.Bounds uses System.Drawing.Rectangle
            var rectType = boundsProperty.PropertyType;
            var rectInstance = Activator.CreateInstance(rectType, 0, 0, rect.Right, rect.Bottom);
            boundsProperty.SetValue(_winController, rectInstance);
        }
    }

    private void NavigateWindows(string url)
    {
        if (!_winWebView2Ready || _winWebView == null) return;

        var navigateMethod = _winWebView.GetType().GetMethod("Navigate", new[] { typeof(string) });
        navigateMethod?.Invoke(_winWebView, new object[] { url });
    }

    private string? GetCurrentUrlWindows()
    {
        if (!_winWebView2Ready || _winWebView == null) return null;
        try
        {
            return _winWebView.GetType().GetProperty("Source")?.GetValue(_winWebView)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts cookies from WebView2 using CoreWebView2.CookieManager.GetCookiesAsync.
    /// This provides full access to all cookies including HttpOnly.
    /// CookieUrls are processed in priority order — first-write-wins for duplicate cookie names.
    /// </summary>
    private async Task<string?> GetCookiesWindows(string[] cookieUrls)
    {
        if (!_winWebView2Ready || _winWebView == null) return null;

        try
        {
            var cookieManager = _winWebView.GetType().GetProperty("CookieManager")?.GetValue(_winWebView);
            if (cookieManager == null) return null;

            var getCookiesMethod = cookieManager.GetType().GetMethod("GetCookiesAsync", new[] { typeof(string) });
            if (getCookiesMethod == null) return null;

            var allCookies = new Dictionary<string, string>();

            foreach (var url in cookieUrls)
            {
                var task = (Task)getCookiesMethod.Invoke(cookieManager, new object[] { url })!;
                await task;
                var cookieList = task.GetType().GetProperty("Result")?.GetValue(task);
                if (cookieList == null) continue;

                var enumerable = cookieList as System.Collections.IEnumerable;
                if (enumerable == null) continue;

                foreach (var cookie in enumerable)
                {
                    var name = cookie.GetType().GetProperty("Name")?.GetValue(cookie)?.ToString();
                    var value = cookie.GetType().GetProperty("Value")?.GetValue(cookie)?.ToString();
                    if (name != null && value != null)
                    {
                        // First-write-wins: higher-priority URL cookies take precedence
                        allCookies.TryAdd(name, value);
                    }
                }
            }

            if (allCookies.Count == 0) return null;
            return string.Join("; ", allCookies.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetCookiesWindows failed: {ex}");
            return null;
        }
    }

    private void DestroyWindows()
    {
        SizeChanged -= OnSizeChangedWindows;

        if (_winController != null)
        {
            var closeMethod = _winController.GetType().GetMethod("Close");
            closeMethod?.Invoke(_winController, null);
            _winController = null;
            _winWebView = null;
            _winWebView2Ready = false;
        }

        if (_winHwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_winHwnd);
            _winHwnd = IntPtr.Zero;
        }
    }

    private static class Win32
    {
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_CLIPCHILDREN = 0x02000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        public delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
    }
}

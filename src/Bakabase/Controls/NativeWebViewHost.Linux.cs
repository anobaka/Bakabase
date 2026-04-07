using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace Bakabase.Controls;

public partial class NativeWebViewHost
{
    private IntPtr _linuxWebView;
    private IntPtr _linuxScrolledWindow;

    private IPlatformHandle CreateLinux(IPlatformHandle parent)
    {
        // WebKitGTK creates a GtkWidget. On X11, we need to extract the X11 window ID
        // for NativeControlHost embedding.

        // Create a GtkScrolledWindow to host the WebView (WebKitGTK widgets need a scrollable parent)
        _linuxScrolledWindow = Gtk.gtk_scrolled_window_new(IntPtr.Zero, IntPtr.Zero);

        // Create WebKitGTK WebView
        _linuxWebView = WebKitGtk.webkit_web_view_new();
        Gtk.gtk_container_add(_linuxScrolledWindow, _linuxWebView);

        // Realize the widget hierarchy to create the underlying X11 window
        Gtk.gtk_widget_realize(_linuxScrolledWindow);
        Gtk.gtk_widget_show_all(_linuxScrolledWindow);

        // Get the X11 window ID
        var gdkWindow = Gtk.gtk_widget_get_window(_linuxScrolledWindow);
        if (gdkWindow == IntPtr.Zero)
        {
            // Fallback: try without scrolled window
            Gtk.gtk_widget_realize(_linuxWebView);
            gdkWindow = Gtk.gtk_widget_get_window(_linuxWebView);
        }

        var xid = IntPtr.Zero;
        if (gdkWindow != IntPtr.Zero)
        {
            xid = Gdk.gdk_x11_window_get_xid(gdkWindow);
        }

        if (xid == IntPtr.Zero)
        {
            // If we can't get an X11 window (e.g., Wayland), fall back to base implementation
            System.Diagnostics.Debug.WriteLine("NativeWebViewHost: Could not obtain X11 window for WebKitGTK. " +
                "Native WebView embedding requires X11. On Wayland, consider running with GDK_BACKEND=x11.");
            CleanupLinuxWidgets();
            return base.CreateNativeControlCore(parent);
        }

        // Set a modern User-Agent to avoid "browser version too low" errors
        var settings = WebKitGtk.webkit_web_view_get_settings(_linuxWebView);
        if (settings != IntPtr.Zero)
        {
            WebKitGtk.webkit_settings_set_user_agent(settings,
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        }

        _initialized = true;

        if (_pendingUrl != null)
            NavigateLinux(_pendingUrl);

        return new PlatformHandle(xid, "XID");
    }

    private void NavigateLinux(string url)
    {
        if (_linuxWebView == IntPtr.Zero) return;
        WebKitGtk.webkit_web_view_load_uri(_linuxWebView, url);

        // Process pending GTK events so the WebView starts loading
        while (Gtk.gtk_events_pending())
            Gtk.gtk_main_iteration();
    }

    private string? GetCurrentUrlLinux()
    {
        if (_linuxWebView == IntPtr.Zero) return null;
        try
        {
            var uriPtr = WebKitGtk.webkit_web_view_get_uri(_linuxWebView);
            return uriPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(uriPtr) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts cookies from WebKitGTK by navigating to each cookie URL
    /// and reading document.cookie via JavaScript (title trick).
    /// Note: HttpOnly cookies are not accessible via this method.
    /// </summary>
    private async Task<string?> GetCookiesLinux(string[] cookieUrls)
    {
        if (_linuxWebView == IntPtr.Zero) return null;

        try
        {
            var allCookies = new Dictionary<string, string>();

            foreach (var url in cookieUrls)
            {
                NavigateLinux(url);
                await Task.Delay(2000); // Wait for page load

                // Inject JS to copy document.cookie into the page title
                const string marker = "__BAKABASE_COOKIE__";
                var js = $"document.title = '{marker}' + document.cookie";
                WebKitGtk.webkit_web_view_run_javascript(_linuxWebView, js, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

                // Process pending GTK events
                while (Gtk.gtk_events_pending())
                    Gtk.gtk_main_iteration();

                await Task.Delay(500); // Wait for JS execution

                // Read the title
                var titlePtr = WebKitGtk.webkit_web_view_get_title(_linuxWebView);
                if (titlePtr != IntPtr.Zero)
                {
                    var title = Marshal.PtrToStringUTF8(titlePtr);
                    if (title != null && title.StartsWith(marker))
                    {
                        var cookieStr = title[marker.Length..];
                        ParseCookieStringLinux(cookieStr, allCookies);
                    }
                }
            }

            if (allCookies.Count == 0) return null;
            return string.Join("; ", allCookies.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetCookiesLinux failed: {ex}");
            return null;
        }
    }

    private static void ParseCookieStringLinux(string cookieStr, Dictionary<string, string> target)
    {
        if (string.IsNullOrWhiteSpace(cookieStr)) return;
        foreach (var pair in cookieStr.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0)
            {
                var name = pair[..eqIdx].Trim();
                var value = pair[(eqIdx + 1)..].Trim();
                target[name] = value;
            }
        }
    }

    private void DestroyLinux()
    {
        CleanupLinuxWidgets();
    }

    private void CleanupLinuxWidgets()
    {
        if (_linuxScrolledWindow != IntPtr.Zero)
        {
            Gtk.gtk_widget_destroy(_linuxScrolledWindow);
            _linuxScrolledWindow = IntPtr.Zero;
            _linuxWebView = IntPtr.Zero; // destroyed with parent
        }
        else if (_linuxWebView != IntPtr.Zero)
        {
            Gtk.gtk_widget_destroy(_linuxWebView);
            _linuxWebView = IntPtr.Zero;
        }
    }

    private static class Gtk
    {
        private const string GtkLib = "libgtk-3.so.0";

        [DllImport(GtkLib)]
        public static extern IntPtr gtk_scrolled_window_new(IntPtr hadjustment, IntPtr vadjustment);

        [DllImport(GtkLib)]
        public static extern void gtk_container_add(IntPtr container, IntPtr widget);

        [DllImport(GtkLib)]
        public static extern void gtk_widget_show_all(IntPtr widget);

        [DllImport(GtkLib)]
        public static extern void gtk_widget_realize(IntPtr widget);

        [DllImport(GtkLib)]
        public static extern IntPtr gtk_widget_get_window(IntPtr widget);

        [DllImport(GtkLib)]
        public static extern void gtk_widget_destroy(IntPtr widget);

        [DllImport(GtkLib)]
        public static extern bool gtk_events_pending();

        [DllImport(GtkLib)]
        public static extern bool gtk_main_iteration();
    }

    private static class Gdk
    {
        [DllImport("libgdk-3.so.0")]
        public static extern IntPtr gdk_x11_window_get_xid(IntPtr gdkWindow);
    }

    private static class WebKitGtk
    {
        // Try webkit2gtk-4.1 first (newer distros), fall back to 4.0
        private const string WebKitLib = "libwebkit2gtk-4.1.so.0";

        [DllImport(WebKitLib)]
        public static extern IntPtr webkit_web_view_new();

        [DllImport(WebKitLib, CharSet = CharSet.Ansi)]
        public static extern void webkit_web_view_load_uri(IntPtr webView, string uri);

        [DllImport(WebKitLib, CharSet = CharSet.Ansi)]
        public static extern void webkit_web_view_run_javascript(IntPtr webView, string script, IntPtr cancellable, IntPtr callback, IntPtr userData);

        [DllImport(WebKitLib)]
        public static extern IntPtr webkit_web_view_get_uri(IntPtr webView);

        [DllImport(WebKitLib)]
        public static extern IntPtr webkit_web_view_get_title(IntPtr webView);

        [DllImport(WebKitLib)]
        public static extern IntPtr webkit_web_view_get_settings(IntPtr webView);

        [DllImport(WebKitLib, CharSet = CharSet.Ansi)]
        public static extern void webkit_settings_set_user_agent(IntPtr settings, string userAgent);
    }
}

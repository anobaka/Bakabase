using System;
using System.Runtime.InteropServices;
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
    }
}

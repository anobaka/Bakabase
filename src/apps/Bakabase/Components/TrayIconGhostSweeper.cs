using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Bakabase.Components;

/// <summary>
/// Removes "ghost" tray icons — entries the Windows notification area still paints for a
/// process that is already gone.
///
/// A tray icon is registered with <c>Shell_NotifyIcon(NIM_ADD)</c> against an owner window
/// and is only unregistered when the owner calls <c>NIM_DELETE</c>. When a process dies
/// without getting the chance to do that — Visual Studio's "Stop Debugging" (a plain
/// TerminateProcess: no finalizers, no ProcessExit, no user code at all), Task Manager
/// "End task", a hard crash — Explorer keeps the stale entry until it next re-validates it.
/// It only re-validates the icons the mouse passes over, which is exactly why the leftover
/// icon disappears the moment you hover it.
///
/// Everything that runs inside our own process (see <c>App.HideTrayIcon</c>) cannot help
/// there, because nothing of ours runs. So we do the re-validation for the user instead:
/// once at startup we synthesize the mouse movement Explorer is waiting for, which makes it
/// drop every dead entry — ours from a previous run, and any other app's while we're at it.
/// </summary>
internal static class TrayIconGhostSweeper
{
    /// <summary>Top-level shell windows that host notification-area toolbars.</summary>
    private static readonly string[] TrayHostClasses =
    [
        // Taskbar (promoted icons).
        "Shell_TrayWnd",
        // Overflow flyout ("hidden icons"), Windows 10 / early Windows 11.
        "NotifyIconOverflowWindow",
        // Overflow flyout, Windows 11 22H2+. Hosts a XAML island; on builds that still keep a
        // classic toolbar inside it we can sweep it, otherwise the search simply finds nothing.
        "TopLevelWindowForOverflowXamlIsland"
    ];

    private const string ToolbarClassName = "ToolbarWindow32";

    /// <summary>Tray buttons are ~16-24px wide; 8px steps hit every one of them.</summary>
    private const int ProbeStep = 8;

    /// <summary>Bounds the work per toolbar so an absurd client rect can't spam Explorer.</summary>
    private const int MaxProbesPerToolbar = 512;

    /// <summary>
    /// Best-effort, no-op on non-Windows. Safe to call at any time: only entries whose owner
    /// window no longer exists are dropped, so live icons (ours included) are never touched.
    /// </summary>
    public static void Sweep()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            foreach (var toolbar in FindNotificationToolbars())
            {
                ProbeToolbar(toolbar);
            }
        }
        catch
        {
            // Purely cosmetic housekeeping against undocumented shell internals — a change in
            // Explorer's window layout must never be able to take the app down.
        }
    }

    private static List<IntPtr> FindNotificationToolbars()
    {
        var toolbars = new List<IntPtr>();
        var className = new StringBuilder(256);

        // EnumChildWindows already walks the whole descendant tree, so a flat scan per host
        // window is enough regardless of how deeply the toolbar is nested.
        var collect = new EnumWindowsProc((hwnd, _) =>
        {
            className.Clear();
            if (GetClassNameW(hwnd, className, className.Capacity) > 0 &&
                string.Equals(className.ToString(), ToolbarClassName, StringComparison.Ordinal))
            {
                toolbars.Add(hwnd);
            }

            return true;
        });

        foreach (var hostClass in TrayHostClasses)
        {
            var host = FindWindowW(hostClass, null);
            if (host != IntPtr.Zero)
            {
                EnumChildWindows(host, collect, IntPtr.Zero);
            }
        }

        GC.KeepAlive(collect);
        return toolbars;
    }

    private static void ProbeToolbar(IntPtr toolbar)
    {
        if (!GetClientRect(toolbar, out var rect))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var probes = 0;
        for (var y = ProbeStep / 2; y < height; y += ProbeStep)
        {
            for (var x = ProbeStep / 2; x < width; x += ProbeStep)
            {
                if (++probes > MaxProbesPerToolbar)
                {
                    return;
                }

                // SendMessageTimeout rather than SendMessage: Explorer is another process and
                // we must not block startup on it if it is busy or hung.
                SendMessageTimeoutW(toolbar, WM_MOUSEMOVE, IntPtr.Zero, MakeLParam(x, y),
                    SMTO_ABORTIFHUNG, ProbeTimeoutMs, out _);
            }
        }
    }

    private static IntPtr MakeLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint ProbeTimeoutMs = 20;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}

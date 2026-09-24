using System;
using System.Reflection;
using Avalonia.Controls;

namespace Bakabase.Shell.Components;

/// <summary>
/// Answers "is there actually a notification area showing our icon?".
///
/// This matters on Linux only, and it matters a lot there. Avalonia implements the tray via
/// the DBus StatusNotifierItem spec, so the icon silently does nothing on a desktop with no
/// StatusNotifierWatcher — GNOME without the AppIndicator extension being the common case.
/// Worse, the "a second launch shows the running instance" recovery in AppHost is gated on
/// RuntimeMode being WinForms or MacOS, so on Linux the tray is the only way back into the
/// app from outside its window: offering "minimize to tray" there hides the app forever, and
/// the tray's "Switch to" submenu is out of reach (the main window then carries it instead —
/// see <see cref="TrayMenuController"/>).
/// </summary>
/// <remarks>
/// Asked afresh every time rather than cached. The watcher can appear after we start (an app
/// launched at login can beat the shell extension that provides it) and can go away again,
/// and Avalonia follows both; so does this.
/// </remarks>
internal static class TrayIconAvailability
{
    private static readonly FieldInfo? ImplField =
        typeof(TrayIcon).GetField("_impl", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>Reflection failures are reported once; the answer is asked for every couple of seconds.</summary>
    private static volatile bool _reportedProbeFailure;

    /// <summary>Safe on any thread: it only reads two fields of the platform implementation.</summary>
    public static bool IsSupported(TrayIcon? icon)
    {
        // Windows' notification area and the macOS menu bar are always there, and both
        // platforms additionally have the single-instance "show the running instance" path
        // as a second way back to a hidden window.
        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        if (icon == null)
        {
            return false;
        }

        try
        {
            var impl = ImplField?.GetValue(icon);
            if (impl == null)
            {
                // No platform implementation at all — nothing will ever appear.
                return false;
            }

            var type = impl.GetType();
            switch (type.Name)
            {
                case "XEmbedTrayIconImpl":
                    // What Avalonia's X11 backend falls back to without a session bus: a
                    // placeholder that logs "not implemented" and shows nothing.
                    return false;
                case "DBusTrayIconImpl":
                {
                    // Not IsActive alone: Avalonia 11.3 sets it as soon as it has a session-bus
                    // connection, whether or not anything will display the icon, and clears it
                    // only on dispose. _serviceConnected is what tracks the watcher: set when
                    // org.kde.StatusNotifierWatcher has an owner, cleared when it loses it.
                    // Both members are internal to Avalonia, hence the reflection.
                    var isActive = type.GetProperty("IsActive", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(impl) as bool?;
                    var watcherPresent = type.GetField("_serviceConnected", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(impl) as bool?;
                    if (isActive == null || watcherPresent == null)
                    {
                        ReportProbeFailure(null);
                        return false;
                    }

                    return isActive.Value && watcherPresent.Value;
                }
                default:
                    // Some other implementation we do not recognise. If the platform bothered to
                    // provide one, assume it works.
                    return true;
            }
        }
        catch (Exception e)
        {
            ReportProbeFailure(e);
            return false;
        }
    }

    /// <summary>
    /// Avalonia moved something. Fail closed on Linux: losing the minimize option and showing
    /// the switch menu in the window are small annoyances, while hiding the window with no way
    /// to restore it is not.
    /// </summary>
    private static void ReportProbeFailure(Exception? e)
    {
        if (_reportedProbeFailure)
        {
            return;
        }

        _reportedProbeFailure = true;
        Serilog.Log.Warning(e, "Could not determine tray icon availability; assuming none");
    }
}

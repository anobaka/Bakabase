using System;
using System.Reflection;
using Avalonia.Controls;

namespace Bakabase.Components;

/// <summary>
/// Answers "is there actually a notification area to minimize into?".
///
/// This matters on Linux only, and it matters a lot there. Avalonia implements the tray via
/// the DBus StatusNotifierItem spec, so the icon silently does nothing on a desktop with no
/// StatusNotifierWatcher — GNOME without the AppIndicator extension being the common case.
/// Worse, the "a second launch shows the running instance" recovery in AppHost is gated on
/// RuntimeMode being WinForms or MacOS, so on Linux a hidden window has no way back at all:
/// offering "minimize to tray" there hides the app forever.
/// </summary>
internal static class TrayIconAvailability
{
    private static readonly FieldInfo? ImplField =
        typeof(TrayIcon).GetField("_impl", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool? _cached;

    /// <summary>
    /// Cached after the first call: the answer is settled long before the user closes the app
    /// (the icon is registered at startup) and cannot change without a session restart.
    /// </summary>
    public static bool IsSupported(TrayIcon? icon)
    {
        return _cached ??= Detect(icon);
    }

    private static bool Detect(TrayIcon? icon)
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

            // DBusTrayIconImpl.IsActive is set once the StatusNotifierWatcher accepts the
            // registration, and cleared when it goes away. Both types are internal to
            // Avalonia, hence the reflection.
            if (impl.GetType().GetProperty("IsActive", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(impl) is bool isActive)
            {
                return isActive;
            }

            // Some other implementation we do not recognise. If the platform bothered to
            // provide one, assume it works.
            return true;
        }
        catch (Exception e)
        {
            // Avalonia moved something. Fail closed on Linux: losing the minimize option is a
            // small annoyance, while hiding the window with no way to restore it is not.
            Serilog.Log.Warning(e, "Could not determine tray icon availability; assuming none");
            return false;
        }
    }
}

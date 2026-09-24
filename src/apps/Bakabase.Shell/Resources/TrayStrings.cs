using System.Globalization;
using System.Resources;

namespace Bakabase.Shell.Resources;

/// <summary>
/// Strings for the tray icon's menu.
///
/// A plain <see cref="ResourceManager"/> for the same reason as <see cref="ExitStrings"/>: the
/// tray exists before the DI container is built and outlives it on the way out, so its labels
/// must not depend on a resolvable service provider.
///
/// Unlike the exit strings, every accessor takes the culture explicitly. The menu is relabelled
/// from the thread pool as well as the UI thread, and the language can change while the app runs
/// (<c>AppService.SetCulture</c> from the options endpoint), which only moves the process-wide
/// default — the UI thread keeps the culture it was given at boot. <see cref="Culture"/> reads
/// the default first so the tray follows a language switch without a restart.
/// </summary>
internal static class TrayStrings
{
    private static readonly ResourceManager Manager =
        new("Bakabase.Shell.Resources.TrayResource", typeof(TrayStrings).Assembly);

    /// <summary>The language the rest of the app is currently in.</summary>
    public static CultureInfo Culture =>
        CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture;

    /// <param name="fallback">
    /// Used when the satellite assembly is missing from the package: an unlabelled tray item is
    /// worse than an English one.
    /// </param>
    private static string Get(string key, string fallback, CultureInfo culture)
    {
        try
        {
            return Manager.GetString(key, culture) ?? fallback;
        }
        catch (MissingManifestResourceException)
        {
            return fallback;
        }
    }

    public static string Open(CultureInfo culture) => Get("Tray_Open", "Open", culture);

    public static string Exit(CultureInfo culture) => Get("Tray_Exit", "Exit", culture);

    public static string SwitchTo(CultureInfo culture) => Get("Tray_SwitchTo", "Switch to", culture);

    /// <summary>This device's entry in the switch submenu, named when the name is known.</summary>
    public static string ThisDevice(CultureInfo culture, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? Get("Tray_ThisDevice", "This device", culture)
            : string.Format(culture, Get("Tray_ThisDeviceNamed", "This device ({0})", culture), name);
}

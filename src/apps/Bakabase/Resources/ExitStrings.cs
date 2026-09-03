using System.Globalization;
using System.Resources;

namespace Bakabase.Resources;

/// <summary>
/// Strings for the exit experience (confirmation dialog + shutdown progress window).
///
/// Deliberately a plain <see cref="ResourceManager"/> rather than an
/// <c>IStringLocalizer</c>: the exit flow has to render correctly at moments when the DI
/// container is on its way out — or was never built, if the user quits during startup — so
/// it must not depend on a resolvable service provider. Culture comes from
/// <see cref="CultureInfo.CurrentUICulture"/>, which <c>AppService.SetCulture</c> sets
/// process-wide during boot; <c>zh-CN</c> resolves to the <c>zh-Hans</c> satellite through
/// the normal parent-culture chain.
/// </summary>
internal static class ExitStrings
{
    private static readonly ResourceManager Manager =
        new("Bakabase.Resources.ExitResource", typeof(ExitStrings).Assembly);

    /// <param name="fallback">
    /// Used when the satellite assembly is missing from the package. Quitting the app must
    /// never fail on a packaging mishap, so every string has an English last resort.
    /// </param>
    private static string Get(string key, string fallback)
    {
        try
        {
            return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
        }
        catch (MissingManifestResourceException)
        {
            return fallback;
        }
    }

    public static string DialogTitle => Get("Exit_DialogTitle", "Close Bakabase");

    public static string Heading => Get("Exit_Heading", "Close Bakabase?");

    public static string Subheading => Get("Exit_Subheading",
        "Bakabase can keep running in the notification area so background tasks continue.");

    public static string SubheadingNoTray => Get("Exit_SubheadingNoTray",
        "This desktop has no system tray, so closing the window will exit Bakabase.");

    public static string Minimize => Get("Exit_Minimize", "Minimize to tray");

    public static string Exit => Get("Exit_Exit", "Exit");

    public static string Cancel => Get("Exit_Cancel", "Cancel");

    public static string Remember => Get("Exit_Remember", "Remember my choice");

    public static string RememberHint => Get("Exit_RememberHint",
        "You can change this later under Settings → Functional → Exit behavior.");

    public static string BusyHeading => Get("Exit_BusyHeading", "Background tasks are still running");

    public static string BusyBody => Get("Exit_BusyBody",
        "Exiting now interrupts them and unsaved changes may be lost.");

    public static string BusyMore(int count) =>
        string.Format(CultureInfo.CurrentUICulture, Get("Exit_BusyMore", "and {0} more"), count);

    public static string ClosingTitle => Get("Exit_ClosingTitle", "Closing Bakabase");

    public static string ClosingHeading => Get("Exit_ClosingHeading", "Closing Bakabase…");

    public static string ClosingStoppingTasks =>
        Get("Exit_ClosingStoppingTasks", "Finishing background tasks…");

    public static string ClosingSavingData => Get("Exit_ClosingSavingData", "Saving data…");

    public static string ClosingDone => Get("Exit_ClosingDone", "Done");

    public static string ClosingRemaining(int count) =>
        string.Format(CultureInfo.CurrentUICulture,
            Get("Exit_ClosingRemaining", "{0} task(s) still finishing"), count);

    public static string ForceQuit => Get("Exit_ForceQuit", "Quit now");

    public static string ForceQuitHint => Get("Exit_ForceQuitHint",
        "This is taking longer than usual. Quitting now may lose unsaved changes.");
}

using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

/// <summary>
/// The login window's strings, from this module's own resources.
/// </summary>
/// <remarks>
/// A second copy of six strings the all-in-one also has in its shared resource, and
/// deliberately so: a relay for a managed server shows this window from its own container,
/// without the legacy business layer that owns the other copy, and shipping the window in
/// English there would be a worse trade than duplicating six entries.
/// </remarks>
public class ThirdPartyCookieCaptureLocalizer : ICookieCaptureLocalizer
{
    private readonly IStringLocalizer _localizer;

    /// <remarks>
    /// Built through the factory rather than injecting <c>IStringLocalizer&lt;T&gt;</c>,
    /// because the resource marker type is internal to this module and this class is not.
    /// </remarks>
    public ThirdPartyCookieCaptureLocalizer(IStringLocalizerFactory factory) =>
        _localizer = factory.Create(typeof(ThirdPartyResource));

    public string LoginTo(string platformName) => _localizer["CookieCapture_LoginTo", platformName];
    public string Confirm => _localizer["CookieCapture_Confirm"];
    public string Cancel => _localizer["CookieCapture_Cancel"];
    public string WaitingForLogin => _localizer["CookieCapture_WaitingForLogin"];
    public string ExtractingCookies => _localizer["CookieCapture_ExtractingCookies"];
    public string Cancelled => _localizer["CookieCapture_Cancelled"];
}

using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Patreon;

/// <summary>
/// Patreon cookie capture flow.
///
/// Flow:
/// 1. Start at patreon.com.
/// 2. If not logged in, user navigates to patreon.com/login.
/// 3. After login, redirect back to patreon.com → auto Done.
/// 4. If already logged in, user clicks Confirm.
/// </summary>
public class PatreonCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Patreon;
    public string StartUrl => "https://www.patreon.com/";
    public string Title => "Login to Patreon";
    public string[] CookieUrls => ["https://www.patreon.com/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        if (currentUrl.Contains("patreon.com/login") || currentUrl.Contains("patreon.com/signup"))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        if (_sawLoginPage && currentUrl.Contains("patreon.com") &&
            !currentUrl.Contains("/login") && !currentUrl.Contains("/signup"))
        {
            return CookieCaptureStep.Done;
        }

        return CookieCaptureStep.Wait;
    }
}

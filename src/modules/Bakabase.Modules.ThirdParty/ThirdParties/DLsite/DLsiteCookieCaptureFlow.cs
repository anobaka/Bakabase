using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

/// <summary>
/// DLsite cookie capture flow.
///
/// Flow:
/// 1. Start at play.dlsite.com (the actual API target).
/// 2. play.dlsite.com's JS checks login status:
///    - If logged in: page stays. User clicks Confirm manually.
///    - If not logged in: JS redirects to login.dlsite.com.
/// 3. User logs in at login.dlsite.com.
/// 4. After login, redirected back (may go through www.dlsite.com) → auto Done.
/// </summary>
public class DLsiteCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.DLsite;
    public string StartUrl => "https://play.dlsite.com/";
    public string Title => "Login to DLsite";
    public string[] CookieUrls => ["https://play.dlsite.com/", "https://www.dlsite.com/", "https://login.dlsite.com/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        if (currentUrl.Contains("login.dlsite.com"))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        // After login, user may be redirected to www.dlsite.com or play.dlsite.com.
        // Navigate to play.dlsite.com to ensure Play API cookies are set.
        if (_sawLoginPage && currentUrl.Contains("dlsite.com") && !currentUrl.Contains("login.dlsite.com"))
        {
            if (!currentUrl.Contains("play.dlsite.com"))
            {
                return CookieCaptureStep.NavigateTo("https://play.dlsite.com/");
            }

            // Back on play.dlsite.com after login → done
            return CookieCaptureStep.Done;
        }

        // Not redirected to login yet, or still on play.dlsite.com.
        // User might already be logged in — they can click Confirm.
        return CookieCaptureStep.Wait;
    }
}

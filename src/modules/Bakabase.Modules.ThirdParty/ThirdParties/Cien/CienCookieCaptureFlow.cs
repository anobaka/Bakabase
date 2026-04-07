using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Cien;

/// <summary>
/// Cien (ci-en.dlsite.com) cookie capture flow.
///
/// Flow:
/// 1. Start at ci-en.dlsite.com.
/// 2. Cien uses DLsite auth. If not logged in, redirects to login.dlsite.com.
/// 3. After login, redirect back to ci-en.dlsite.com → auto Done.
/// 4. If already logged in, user clicks Confirm.
/// </summary>
public class CienCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Cien;
    public string StartUrl => "https://ci-en.dlsite.com/";
    public string Title => "Login to Cien";
    public string[] CookieUrls => ["https://ci-en.dlsite.com/", "https://login.dlsite.com/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        if (currentUrl.Contains("login.dlsite.com") || currentUrl.Contains("ci-en.dlsite.com/users/sign_in"))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        if (_sawLoginPage && currentUrl.Contains("ci-en.dlsite.com") && !currentUrl.Contains("/sign_in"))
        {
            return CookieCaptureStep.Done;
        }

        return CookieCaptureStep.Wait;
    }
}

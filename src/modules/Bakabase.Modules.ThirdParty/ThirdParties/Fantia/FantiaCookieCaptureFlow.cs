using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fantia;

/// <summary>
/// Fantia cookie capture flow.
///
/// Flow:
/// 1. Start at fantia.jp.
/// 2. If not logged in, user navigates to fantia.jp/sessions/signin.
/// 3. After login, redirect back to fantia.jp → auto Done.
/// 4. If already logged in, user clicks Confirm.
/// </summary>
public class FantiaCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Fantia;
    public string StartUrl => "https://fantia.jp/";
    public string Title => "Login to Fantia";
    public string[] CookieUrls => ["https://fantia.jp/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        if (currentUrl.Contains("fantia.jp/sessions"))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        if (_sawLoginPage && currentUrl.Contains("fantia.jp") && !currentUrl.Contains("/sessions"))
        {
            return CookieCaptureStep.Done;
        }

        return CookieCaptureStep.Wait;
    }
}

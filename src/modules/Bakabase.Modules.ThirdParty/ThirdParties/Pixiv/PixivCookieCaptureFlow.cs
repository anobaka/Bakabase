using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;

/// <summary>
/// Pixiv cookie capture flow.
///
/// Flow:
/// 1. Start at pixiv.net.
/// 2. If not logged in, user navigates to accounts.pixiv.net/login.
/// 3. After login, Pixiv redirects back to pixiv.net → auto Done.
/// 4. If already logged in, user clicks Confirm.
/// </summary>
public class PixivCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Pixiv;
    public string StartUrl => "https://www.pixiv.net/";
    public string Title => "Login to Pixiv";
    public string[] CookieUrls => ["https://www.pixiv.net/", "https://accounts.pixiv.net/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        if (currentUrl.Contains("accounts.pixiv.net"))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        if (_sawLoginPage && currentUrl.Contains("pixiv.net") && !currentUrl.Contains("accounts.pixiv.net"))
        {
            return CookieCaptureStep.Done;
        }

        return CookieCaptureStep.Wait;
    }
}

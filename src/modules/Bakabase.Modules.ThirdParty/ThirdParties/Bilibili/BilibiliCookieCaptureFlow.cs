using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;

/// <summary>
/// Bilibili cookie capture flow.
///
/// Flow:
/// 1. Start at bilibili.com.
/// 2. If not logged in, user navigates to login (passport.bilibili.com).
/// 3. After login, Bilibili redirects back to bilibili.com → auto Done.
/// 4. If already logged in, user clicks Confirm.
/// </summary>
public class BilibiliCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.BiliBili;
    public string StartUrl => "https://www.bilibili.com/";
    public string Title => "Login to Bilibili";
    public string[] CookieUrls => ["https://www.bilibili.com/", "https://api.bilibili.com/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        if (currentUrl.Contains("passport.bilibili.com"))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        if (_sawLoginPage && currentUrl.Contains("bilibili.com") && !currentUrl.Contains("passport.bilibili.com"))
        {
            return CookieCaptureStep.Done;
        }

        return CookieCaptureStep.Wait;
    }
}

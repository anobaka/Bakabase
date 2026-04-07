using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;

/// <summary>
/// Bangumi (bgm.tv) cookie capture: start at homepage, user may open /login, then return after login.
/// </summary>
public class BangumiCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Bangumi;
    public string StartUrl => "https://bgm.tv/";
    public string Title => "Login to Bangumi";
    public string[] CookieUrls => ["https://bgm.tv/", "https://api.bgm.tv/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        if (currentUrl.Contains("bgm.tv/login", StringComparison.OrdinalIgnoreCase))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        if (_sawLoginPage && currentUrl.Contains("bgm.tv", StringComparison.OrdinalIgnoreCase) &&
            !currentUrl.Contains("/login", StringComparison.OrdinalIgnoreCase))
        {
            return CookieCaptureStep.Done;
        }

        return CookieCaptureStep.Wait;
    }
}

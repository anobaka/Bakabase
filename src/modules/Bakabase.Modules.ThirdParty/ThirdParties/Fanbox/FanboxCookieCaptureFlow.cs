using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fanbox;

/// <summary>
/// Fanbox cookie capture flow.
///
/// Flow:
/// 1. Start at fanbox.cc.
/// 2. Fanbox uses Pixiv for auth. If not logged in, user navigates to
///    accounts.pixiv.net or fanbox.cc login.
/// 3. After login, redirect back to fanbox.cc → auto Done.
/// 4. If already logged in, user clicks Confirm.
/// </summary>
public class FanboxCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Fanbox;
    public string StartUrl => "https://www.fanbox.cc/";
    public string Title => "Login to Fanbox";
    public string[] CookieUrls => ["https://www.fanbox.cc/", "https://api.fanbox.cc/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        if (currentUrl.Contains("accounts.pixiv.net") || currentUrl.Contains("fanbox.cc/login"))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        if (_sawLoginPage && currentUrl.Contains("fanbox.cc") && !currentUrl.Contains("/login"))
        {
            return CookieCaptureStep.Done;
        }

        return CookieCaptureStep.Wait;
    }
}

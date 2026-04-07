using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

/// <summary>
/// SoulPlus / North Plus (Discuz) cookie capture at north-plus.net.
/// </summary>
public class SoulPlusCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.SoulPlus;
    public string StartUrl => "https://www.north-plus.net/";
    public string Title => "Login to North Plus";
    public string[] CookieUrls => ["https://www.north-plus.net/"];

    private bool _sawLoginPage;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        var lower = currentUrl.ToLowerInvariant();
        if (lower.Contains("member.php?mod=logging") || lower.Contains("action=login"))
        {
            _sawLoginPage = true;
            return CookieCaptureStep.Wait;
        }

        if (_sawLoginPage && lower.Contains("north-plus.net") && !lower.Contains("member.php?mod=logging"))
        {
            return CookieCaptureStep.Done;
        }

        return CookieCaptureStep.Wait;
    }
}

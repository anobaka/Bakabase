using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

public class DLsiteCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.DLsite;
    public string StartUrl => "https://play.dlsite.com/";
    public string PlatformName => "DLsite";
    public string[] CookieUrls => ["https://play.dlsite.com/", "https://www.dlsite.com/", "https://login.dlsite.com/"];
}

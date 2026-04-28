using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;

public class BangumiCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Bangumi;
    public string StartUrl => "https://bgm.tv/";
    public string PlatformName => "Bangumi";
    public string[] CookieUrls => ["https://bgm.tv/", "https://api.bgm.tv/"];
}

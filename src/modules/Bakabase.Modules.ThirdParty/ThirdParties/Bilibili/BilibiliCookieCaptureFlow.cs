using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;

public class BilibiliCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.BiliBili;
    public string StartUrl => "https://www.bilibili.com/";
    public string PlatformName => "Bilibili";
    public string[] CookieUrls => ["https://www.bilibili.com/", "https://api.bilibili.com/"];
}

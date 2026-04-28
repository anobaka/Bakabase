using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;

public class PixivCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Pixiv;
    public string StartUrl => "https://www.pixiv.net/";
    public string PlatformName => "Pixiv";
    public string[] CookieUrls => ["https://www.pixiv.net/", "https://accounts.pixiv.net/"];
}

using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public class ExHentaiCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.ExHentai;
    public string StartUrl => "https://exhentai.org/";
    public string PlatformName => "ExHentai";
    public string[] CookieUrls => ["https://exhentai.org/", "https://forums.e-hentai.org/", "https://e-hentai.org/"];
}

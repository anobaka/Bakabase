using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fantia;

public class FantiaCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Fantia;
    public string StartUrl => "https://fantia.jp/";
    public string PlatformName => "Fantia";
    public string[] CookieUrls => ["https://fantia.jp/"];
}

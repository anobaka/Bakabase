using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Cien;

public class CienCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Cien;
    public string StartUrl => "https://ci-en.dlsite.com/";
    public string PlatformName => "Cien";
    public string[] CookieUrls => ["https://ci-en.dlsite.com/", "https://login.dlsite.com/"];
}

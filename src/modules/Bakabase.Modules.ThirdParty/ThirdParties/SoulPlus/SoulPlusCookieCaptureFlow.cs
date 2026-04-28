using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public class SoulPlusCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.SoulPlus;
    public string StartUrl => "https://www.north-plus.net/";
    public string PlatformName => "North Plus";
    public string[] CookieUrls => ["https://www.north-plus.net/"];
}

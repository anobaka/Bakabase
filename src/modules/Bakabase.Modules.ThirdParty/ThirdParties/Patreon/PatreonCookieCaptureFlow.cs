using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Patreon;

public class PatreonCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Patreon;
    public string StartUrl => "https://www.patreon.com/";
    public string PlatformName => "Patreon";
    public string[] CookieUrls => ["https://www.patreon.com/"];
}

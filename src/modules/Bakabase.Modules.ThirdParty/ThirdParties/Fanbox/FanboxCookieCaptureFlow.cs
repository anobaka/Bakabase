using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fanbox;

public class FanboxCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.Fanbox;
    public string StartUrl => "https://www.fanbox.cc/";
    public string PlatformName => "Fanbox";
    public string[] CookieUrls => ["https://www.fanbox.cc/", "https://api.fanbox.cc/"];
}

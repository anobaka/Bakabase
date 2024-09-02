using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.ThirdParty.Components.Localization;

internal class ThirdPartyLocalizer(IStringLocalizer<ThirdPartyResource> localizer) : IThirdPartyLocalizer
{
    public string ThirdParty_Bilibili_CookieIsInvalid()
    {
        return localizer[nameof(ThirdParty_Bilibili_CookieIsInvalid)];
    }

    public string ThirdParty_Bilibili_FavoritesIsMissing()
    {
        return localizer[nameof(ThirdParty_Bilibili_FavoritesIsMissing)];
    }
}
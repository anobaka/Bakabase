using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

public class DLsiteHttpMessageHandler<TDLsiteOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TDLsiteOptions> optionsManager,
    BakabaseWebProxy webProxy,
    IThirdPartyCookieContainer cookieContainer)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TDLsiteOptions>(logger, ThirdPartyId.DLsite, optionsManager,
        webProxy, cookieContainer)
    where TDLsiteOptions : class, IThirdPartyHttpClientOptions, new()
{
    protected override void ConfigureHandler()
    {
        // Disable auto-redirect so DLsiteClient can follow redirects manually
        // and carry cookies across different DLsite subdomains.
        AllowAutoRedirect = false;
    }
}

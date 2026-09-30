using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public class ExHentaiHttpMessageHandler<TExHentaiOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TExHentaiOptions> optionsManager,
    BakabaseWebProxy webProxy,
    IThirdPartyCookieContainer cookieContainer)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TExHentaiOptions>(logger, ThirdPartyId.ExHentai, optionsManager,
        webProxy, cookieContainer)
    where TExHentaiOptions : class, IThirdPartyHttpClientOptions, new()
{
    protected override void ConfigureHandler()
    {
        // ExHentaiClient follows redirects explicitly so account credentials never cross
        // into an image/torrent CDN through HttpClientHandler's automatic redirect path.
        AllowAutoRedirect = false;
    }
}

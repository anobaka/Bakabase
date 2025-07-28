using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;

public class BangumiHttpMessageHandler<TBangumiOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TBangumiOptions> optionsManager,
    BakabaseWebProxy webProxy)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TBangumiOptions>(logger, ThirdPartyId.Bangumi, optionsManager,
        webProxy)
    where TBangumiOptions : class, IThirdPartyHttpClientOptions, new();
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;

public class BilibiliHttpMessageHandler<TBilibiliOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TBilibiliOptions> optionsManager,
    BakabaseWebProxy webProxy)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TBilibiliOptions>(logger, ThirdPartyId.Bilibili, optionsManager,
        webProxy)
    where TBilibiliOptions : class, IThirdPartyHttpClientOptions, new();
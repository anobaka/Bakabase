using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

public class DLsiteHttpMessageHandler<TDLsiteOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TDLsiteOptions> optionsManager,
    BakabaseWebProxy webProxy)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TDLsiteOptions>(logger, ThirdPartyId.DLsite, optionsManager,
        webProxy)
    where TDLsiteOptions : class, IThirdPartyHttpClientOptions, new();
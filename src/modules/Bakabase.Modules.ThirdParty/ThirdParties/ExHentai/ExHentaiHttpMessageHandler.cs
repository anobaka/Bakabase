using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public class ExHentaiHttpMessageHandler<TExHentaiOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TExHentaiOptions> optionsManager,
    BakabaseWebProxy webProxy)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TExHentaiOptions>(logger, ThirdPartyId.ExHentai, optionsManager,
        webProxy)
    where TExHentaiOptions : class, IThirdPartyHttpClientOptions, new();
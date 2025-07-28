using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;

public class PixivHttpMessageHandler<TPixivOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TPixivOptions> optionsManager,
    BakabaseWebProxy webProxy)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TPixivOptions>(logger, ThirdPartyId.Pixiv, optionsManager,
        webProxy)
    where TPixivOptions : class, IThirdPartyHttpClientOptions, new();
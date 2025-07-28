using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Tmdb;

public class TmdbHttpMessageHandler<TTmdbOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TTmdbOptions> optionsManager,
    BakabaseWebProxy webProxy)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TTmdbOptions>(logger, ThirdPartyId.Tmdb, optionsManager,
        webProxy)
    where TTmdbOptions : class, IThirdPartyHttpClientOptions, new();
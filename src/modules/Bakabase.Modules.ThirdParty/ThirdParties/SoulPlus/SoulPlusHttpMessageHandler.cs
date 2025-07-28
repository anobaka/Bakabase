using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public class SoulPlusHttpMessageHandler<TSoulPlusOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TSoulPlusOptions> optionsManager,
    BakabaseWebProxy webProxy)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TSoulPlusOptions>(logger, ThirdPartyId.SoulPlus, optionsManager,
        webProxy)
    where TSoulPlusOptions : class, ISoulPlusOptions, new();
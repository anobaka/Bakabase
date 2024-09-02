using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai
{
    public class
        ExHentaiThirdPartyHttpMessageHandler(
            ThirdPartyHttpRequestLogger logger,
            AspNetCoreOptionsManager<ExHentaiOptions> optionsManager,
            BakabaseWebProxy proxy)
        : BakabaseOptionsBasedThirdPartyHttpMessageHandler<ExHentaiOptions,
            ThirdPartyHttpClientOptions>(logger, ThirdPartyId.ExHentai,
            optionsManager, proxy)
    {
        protected override ThirdPartyHttpClientOptions ToThirdPartyHttpClientOptions(ExHentaiOptions options)
        {
            return new ThirdPartyHttpClientOptions
            {
                Cookie = options.Cookie,
                Interval = options.Downloader?.Interval ?? 500,
                MaxThreads = 1
            };
        }
    }
}
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili
{
    public class
        BilibiliThirdPartyHttpMessageHandler : BakabaseOptionsBasedThirdPartyHttpMessageHandler<BilibiliOptions,
            ThirdPartyHttpClientOptions>
    {
        public BilibiliThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger,
            AspNetCoreOptionsManager<BilibiliOptions> optionsManager, BakabaseWebProxy webProxy) : base(logger,
            ThirdPartyId.Bilibili, optionsManager, webProxy)
        {
        }

        protected override ThirdPartyHttpClientOptions ToThirdPartyHttpClientOptions(BilibiliOptions options)
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
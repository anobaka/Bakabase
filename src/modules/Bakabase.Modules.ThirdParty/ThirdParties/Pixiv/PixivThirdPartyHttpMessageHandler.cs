using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv
{
    public class
        PixivThirdPartyHttpMessageHandler : BakabaseOptionsBasedThirdPartyHttpMessageHandler<PixivOptions,
            ThirdPartyHttpClientOptions>
    {
        public PixivThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger,
            AspNetCoreOptionsManager<PixivOptions> optionsManager, BakabaseWebProxy proxy) : base(logger,
            ThirdPartyId.Pixiv, optionsManager, proxy)
        {
            Proxy = proxy;
        }

        protected override ThirdPartyHttpClientOptions ToThirdPartyHttpClientOptions(PixivOptions options)
        {
            return new ThirdPartyHttpClientOptions
            {
                Cookie = options.Cookie,
                Interval = options.Downloader?.Interval ?? 500,
                MaxThreads = 1,
                Referer = "https://www.pixiv.net/"
            };
        }
    }
}
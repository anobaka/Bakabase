using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus
{
    public class
        SoulPlusThirdPartyHttpMessageHandler : BakabaseOptionsBasedThirdPartyHttpMessageHandler<SoulPlusOptions,
            ThirdPartyHttpClientOptions>
    {
        public SoulPlusThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger,
            AspNetCoreOptionsManager<SoulPlusOptions> optionsManager, BakabaseWebProxy proxy) : base(logger,
            ThirdPartyId.Pixiv, optionsManager, proxy)
        {
            Proxy = proxy;
        }

        protected override ThirdPartyHttpClientOptions ToThirdPartyHttpClientOptions(SoulPlusOptions options)
        {
            return new ThirdPartyHttpClientOptions
            {
                Cookie = options.Cookie,
                Interval = 1000,
                MaxThreads = 1
            };
        }
    }
}
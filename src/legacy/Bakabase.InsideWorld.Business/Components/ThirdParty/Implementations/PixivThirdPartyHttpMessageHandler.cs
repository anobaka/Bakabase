using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Configs.Infrastructures;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Implementations
{
    public class
        PixivThirdPartyHttpMessageHandler : InsideWorldOptionsBasedThirdPartyHttpMessageHandler<PixivOptions,
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
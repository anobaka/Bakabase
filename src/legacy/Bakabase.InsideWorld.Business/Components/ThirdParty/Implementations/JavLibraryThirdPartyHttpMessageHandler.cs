using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bootstrap.Components.Configuration;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Implementations
{
    public class JavLibraryThirdPartyHttpMessageHandler : BakabaseOptionsBasedThirdPartyHttpMessageHandler<
        JavLibraryOptions, ThirdPartyHttpClientOptions>
    {
        public JavLibraryThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger, ThirdPartyId thirdPartyId,
            AspNetCoreOptionsManager<JavLibraryOptions> optionsManager, BakabaseWebProxy webProxy) : base(logger,
            thirdPartyId, optionsManager, webProxy)
        {
        }

        protected override ThirdPartyHttpClientOptions ToThirdPartyHttpClientOptions(JavLibraryOptions options)
        {
            return new ThirdPartyHttpClientOptions
            {
                Cookie = options.Cookie,
                Interval = 500,
                MaxThreads = 1,
            };
        }
    }
}
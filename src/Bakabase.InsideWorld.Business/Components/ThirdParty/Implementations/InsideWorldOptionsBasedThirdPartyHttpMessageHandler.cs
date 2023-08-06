using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Network;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Http;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Implementations
{
    public abstract class
        InsideWorldOptionsBasedThirdPartyHttpMessageHandler<TInsideWorldOptions, TThirdPartyHttpClientOptions> :
            AbstractThirdPartyHttpMessageHandler<TThirdPartyHttpClientOptions>
        where TThirdPartyHttpClientOptions : ThirdPartyHttpClientOptions where TInsideWorldOptions : class, new()
    {
        protected readonly AspNetCoreOptionsManager<TInsideWorldOptions> OptionsManager;
        private readonly IDisposable _optionsChangeHandlerDisposable;

        protected InsideWorldOptionsBasedThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger,
            ThirdPartyId thirdPartyId, AspNetCoreOptionsManager<TInsideWorldOptions> optionsManager,
            InsideWorldWebProxy webProxy) : base(logger, thirdPartyId, webProxy)
        {
            OptionsManager = optionsManager;
            _optionsChangeHandlerDisposable = OptionsManager.OnChange(OnOptionsChange);
        }

        protected abstract TThirdPartyHttpClientOptions ToThirdPartyHttpClientOptions(TInsideWorldOptions options);

        private void OnOptionsChange(TInsideWorldOptions newOptions)
        {
            Options = Microsoft.Extensions.Options.Options.Create(ToThirdPartyHttpClientOptions(newOptions));
        }

        public override IOptions<TThirdPartyHttpClientOptions> Options
        {
            protected get
            {
                if (base.Options == null)
                {
                    OnOptionsChange(OptionsManager.Value);
                }

                return base.Options;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _optionsChangeHandlerDisposable?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
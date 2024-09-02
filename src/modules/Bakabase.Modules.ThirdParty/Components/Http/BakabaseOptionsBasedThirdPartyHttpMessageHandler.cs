using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.ThirdParty.Components.Http
{
    public abstract class
        BakabaseOptionsBasedThirdPartyHttpMessageHandler<TBakabaseOptions, TThirdPartyHttpClientOptions> :
        AbstractThirdPartyHttpMessageHandler<TThirdPartyHttpClientOptions>
        where TThirdPartyHttpClientOptions : ThirdPartyHttpClientOptions where TBakabaseOptions : class, new()
    {
        protected readonly AspNetCoreOptionsManager<TBakabaseOptions> OptionsManager;
        private readonly IDisposable _optionsChangeHandlerDisposable;

        protected BakabaseOptionsBasedThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger,
            ThirdPartyId thirdPartyId, AspNetCoreOptionsManager<TBakabaseOptions> optionsManager,
            BakabaseWebProxy webProxy) : base(logger, thirdPartyId, webProxy)
        {
            OptionsManager = optionsManager;
            _optionsChangeHandlerDisposable = OptionsManager.OnChange(OnOptionsChange);
        }

        protected abstract TThirdPartyHttpClientOptions ToThirdPartyHttpClientOptions(TBakabaseOptions options);

        private void OnOptionsChange(TBakabaseOptions newOptions)
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
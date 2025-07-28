using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.ThirdParty.Components.Http
{
    public abstract class
        BakabaseOptionsBasedThirdPartyHttpMessageHandler<TBakabaseOptions> :
        AbstractThirdPartyHttpMessageHandler<TBakabaseOptions>
        where TBakabaseOptions : class, IThirdPartyHttpClientOptions, new()
    {
        private readonly IDisposable _optionsChangeHandlerDisposable;

        protected BakabaseOptionsBasedThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger,
            ThirdPartyId thirdPartyId, AspNetCoreOptionsManager<TBakabaseOptions> optionsManager,
            BakabaseWebProxy webProxy) : base(logger, thirdPartyId, webProxy, optionsManager.Value)
        {
            _optionsChangeHandlerDisposable = optionsManager.OnChange(options => Options = options);
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _optionsChangeHandlerDisposable.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
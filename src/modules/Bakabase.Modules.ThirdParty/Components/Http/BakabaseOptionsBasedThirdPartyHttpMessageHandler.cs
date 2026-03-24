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
        private readonly IThirdPartyCookieContainer? _cookieContainerProvider;

        protected BakabaseOptionsBasedThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger,
            ThirdPartyId thirdPartyId, AspNetCoreOptionsManager<TBakabaseOptions> optionsManager,
            BakabaseWebProxy webProxy, IThirdPartyCookieContainer? cookieContainer = null) : base(logger, thirdPartyId, webProxy, optionsManager.Value, cookieContainer)
        {
            _cookieContainerProvider = cookieContainer;
            _optionsChangeHandlerDisposable = optionsManager.OnChange(options =>
            {
                Options = options;
                // Reset all cookie containers for this source when options change
                // (user may have updated their cookies)
                _cookieContainerProvider?.ResetByPrefix(CookieContainerKeyPrefix);
            });
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

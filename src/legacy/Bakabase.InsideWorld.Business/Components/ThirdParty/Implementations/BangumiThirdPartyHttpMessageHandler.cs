using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Implementations;

public class BangumiThirdPartyHttpMessageHandler : AbstractThirdPartyHttpMessageHandler<ThirdPartyHttpClientOptions>
{
    public BangumiThirdPartyHttpMessageHandler(ThirdPartyHttpRequestLogger logger, BakabaseWebProxy webProxy) : base(
        logger, ThirdPartyId.Bangumi, webProxy)
    {
        Options = Microsoft.Extensions.Options.Options.Create(new ThirdPartyHttpClientOptions
        {
            MaxThreads = 1,
            Interval = 2000
        });
    }

    public sealed override IOptions<ThirdPartyHttpClientOptions> Options
    {
        protected get => base.Options;
        set => base.Options = value;
    }
}
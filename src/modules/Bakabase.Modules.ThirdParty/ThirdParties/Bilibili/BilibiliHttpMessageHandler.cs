using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;

/// <summary>
/// The Bilibili <b>API</b> client's handler: cookie, rate limit, request log. CDN downloads must not go through
/// it (the cookie would reach the CDN and every signed URL would be logged).
/// </summary>
public class BilibiliHttpMessageHandler<TBilibiliOptions>(
    ThirdPartyHttpRequestLogger logger,
    AspNetCoreOptionsManager<TBilibiliOptions> optionsManager,
    BakabaseWebProxy webProxy)
    : BakabaseOptionsBasedThirdPartyHttpMessageHandler<TBilibiliOptions>(logger, ThirdPartyId.Bilibili, optionsManager,
        webProxy)
    where TBilibiliOptions : class, IThirdPartyHttpClientOptions, new()
{
    protected override void BeforeRequesting(HttpRequestMessage request, CancellationToken ct)
    {
        base.BeforeRequesting(request, ct);
        BilibiliRequestDefaults.ApplyApiDefaults(request);
    }

    protected override async Task BeforeRequestingAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await base.BeforeRequestingAsync(request, ct);
        BilibiliRequestDefaults.ApplyApiDefaults(request);
    }
}

using System.Net;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;

/// <summary>
/// The primary handler of the <see cref="InternalOptions.HttpClientNames.BilibiliCdn"/> client: Bilibili's proxy
/// setting, no cookie, no rate limit, no request log, and the headers the CDN insists on
/// (<see cref="BilibiliCdnRequestHeaders"/>). Unlike the API handler it derives from nothing that attaches the
/// account cookie or records URLs — signed CDN URLs carry the user's IP (<c>oi</c>), <c>mid</c> and signatures.
/// </summary>
public sealed class BilibiliCdnHttpMessageHandler<TOptions> : HttpClientHandler
    where TOptions : class, IThirdPartyHttpClientOptions, new()
{
    private readonly AspNetCoreOptionsManager<TOptions> _options;

    public BilibiliCdnHttpMessageHandler(AspNetCoreOptionsManager<TOptions> options, BakabaseWebProxy webProxy)
    {
        _options = options;
        Proxy = webProxy.ForThirdParty(ThirdPartyId.Bilibili);
        UseProxy = true;
        UseCookies = false;
        AllowAutoRedirect = true;
        // Streams are ranged byte copies; small bodies (danmaku, subtitles) are decoded by their callers, which
        // need to see the raw Content-Encoding (danmaku is raw deflate that the automatic decoder rejects).
        AutomaticDecompression = DecompressionMethods.None;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        BilibiliCdnRequestHeaders.Apply(request, _options.Value.UserAgent);
        return base.SendAsync(request, ct);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
    {
        BilibiliCdnRequestHeaders.Apply(request, _options.Value.UserAgent);
        return base.Send(request, ct);
    }
}

/// <summary>
/// What Bilibili's CDN requires: a <c>bilibili.com</c> Referer and a "normal" User-Agent (none, curl or
/// python-requests are answered 403) — and what it must never get: the account cookie.
/// </summary>
public static class BilibiliCdnRequestHeaders
{
    public const string Referer = "https://www.bilibili.com/";

    /// <summary>
    /// Forces the User-Agent (<paramref name="userAgentOverride"/> when set, otherwise
    /// <see cref="InternalOptions.DefaultHttpUserAgent"/>), adds the Referer when absent and removes any Cookie.
    /// In the CDN handler this runs after <see cref="HttpClient"/> merged its default headers, so it is the final
    /// word: a library that builds its own requests (with its own UA) cannot override it.
    /// </summary>
    public static void Apply(HttpRequestMessage request, string? userAgentOverride)
    {
        var ua = string.IsNullOrWhiteSpace(userAgentOverride)
            ? InternalOptions.DefaultHttpUserAgent
            : userAgentOverride;
        request.Headers.UserAgent.Clear();
        request.Headers.Remove("User-Agent");
        request.Headers.TryAddWithoutValidation("User-Agent", ua);
        request.Headers.Referrer ??= new Uri(Referer);
        // Defence in depth: nothing sent through the CDN client may carry the account cookie.
        request.Headers.Remove("Cookie");
    }
}

using System.Net.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Http;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Av;

/// <summary>
/// Builds GET requests for AV clients that route a per-source cookie through the shared
/// HttpClient handler — so we don't need a dedicated HttpMessageHandler per source.
/// </summary>
public static class AvHttpRequestBuilder
{
    public static HttpRequestMessage BuildGet(string url, AvSourceResolvedConfig config)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(config.Cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", config.Cookie);
        }
        if (!string.IsNullOrWhiteSpace(config.UserAgent))
        {
            request.Headers.UserAgent.Clear();
            request.Headers.TryAddWithoutValidation("User-Agent", config.UserAgent);
        }
        return request;
    }
}

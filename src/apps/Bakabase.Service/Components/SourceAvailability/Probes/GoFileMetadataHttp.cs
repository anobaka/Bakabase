using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Service.Components.CloudDownloads.WebProviders;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

/// <summary>Only compile-time GoFile HTTPS endpoints. System DNS may be a local TUN's fake-IP;
/// TLS still authenticates the official hostname. This exception never handles user-selected
/// hosts, redirects, payloads, cookies or arbitrary API operations.</summary>
internal static class GoFileMetadataHttp
{
    internal static bool Allows(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Fragment.Length == 0 && (uri.IdnHost == "gofile.io" && uri.AbsolutePath == "/js/wt.obf.js" && uri.Query.Length == 0 ||
            uri.IdnHost == "api.gofile.io" && (uri.AbsolutePath == "/accounts" && uri.Query.Length == 0 ||
                Regex.IsMatch(uri.AbsolutePath, @"\A/contents/[A-Za-z0-9_-]{6,128}\z")));

    internal static HttpMessageHandler Create(IWebProxy proxy) =>
        new MethodBoundary(ProxyAwarePublicHttpHandler.ForFixedMetadataEndpoints(proxy, Allows));

    private sealed class MethodBoundary(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            var uri = request.RequestUri;
            if (uri == null || !Allows(uri) ||
                (uri.IdnHost == "api.gofile.io" && uri.AbsolutePath == "/accounts"
                    ? request.Method != HttpMethod.Post : request.Method != HttpMethod.Get))
                throw new HttpRequestException("The source metadata operation is not supported.");
            return base.SendAsync(request, ct);
        }
    }
}

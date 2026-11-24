using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

/// <summary>Public-target validation at every redirect; direct sockets are DNS-pinned.
/// An explicitly configured application proxy owns its own DNS resolution. It may be local,
/// but this never permits private target URLs, ambient credentials, cookies or arbitrary hosts.
/// </summary>
public sealed class ProxyAwarePublicHttpHandler : HttpMessageHandler
{
    private readonly IWebProxy _proxy;
    private readonly Func<Uri, bool> _allowed;
    private readonly Func<Uri, CancellationToken, Task> _check;
    private readonly HttpMessageInvoker _direct;
    private readonly ConcurrentDictionary<string, HttpMessageInvoker> _proxied = new();
    private readonly Func<Uri, ICredentials?, HttpMessageHandler> _proxyFactory;
    private readonly bool _followRedirects;
    private readonly Action<Uri, Uri>? _validateRoute;
    private readonly Func<Uri, CancellationToken, Task>? _checkProxyTarget;

    public ProxyAwarePublicHttpHandler(IWebProxy proxy, Func<Uri, bool>? allowed = null, bool followRedirects = true,
        Func<Uri, CancellationToken, Task>? checkProxyTarget = null, Action<Uri, Uri>? validateRoute = null)
        : this(proxy, allowed ?? (_ => true), PublicCloudHttp.EnsurePublicHostAsync,
            PublicCloudHttp.CreateHandler(), (uri, credentials) => new SocketsHttpHandler
            {
                Proxy = new WebProxy(uri) {Credentials = credentials}, UseProxy = true,
                UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            }, followRedirects, checkProxyTarget, validateRoute) { }

    // Fixed provider metadata endpoints may use the host's TUN/fake-IP DNS. Authenticate the
    // allowlisted hostname with TLS and never follow a redirect or forward ambient credentials.
    internal static HttpMessageHandler ForFixedMetadataEndpoints(IWebProxy proxy, Func<Uri, bool> allowed) =>
        new ProxyAwarePublicHttpHandler(proxy, allowed, (_, _) => Task.CompletedTask, Transport(null),
            (endpoint, credentials) => Transport(new WebProxy(endpoint) {Credentials = credentials}), followRedirects: false);

    private static HttpMessageHandler Transport(IWebProxy? proxy) => new SocketsHttpHandler
    {
        Proxy = proxy, UseProxy = proxy != null, UseCookies = false, AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        // No certificate callback: hostname/SNI and chain validation are mandatory.
    };

    internal ProxyAwarePublicHttpHandler(IWebProxy proxy, Func<Uri, bool> allowed,
        Func<Uri, CancellationToken, Task> check, HttpMessageHandler direct,
        Func<Uri, ICredentials?, HttpMessageHandler> proxyFactory, bool followRedirects = true,
        Func<Uri, CancellationToken, Task>? checkProxyTarget = null, Action<Uri, Uri>? validateRoute = null)
    {
        _proxy = proxy; _allowed = allowed; _check = check;
        _direct = new HttpMessageInvoker(direct); _proxyFactory = proxyFactory;
        _followRedirects = followRedirects; _checkProxyTarget = checkProxyTarget; _validateRoute = validateRoute;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var current = request;
        var ownsRequest = false;
        try
        {
            for (var hop = 0; hop <= 8; hop++)
            {
                var uri = current.RequestUri ?? throw new HttpRequestException("The public request URL is missing.");
                PublicCloudHttp.EnsureSafeUri(uri);
                _validateRoute?.Invoke(request.RequestUri!, uri);
                if (!_allowed(uri)) throw new HttpRequestException("This address or redirect is not supported by the public-share adapter; a login or manual download may be required.");
                var endpoint = _proxy.IsBypassed(uri) ? null : _proxy.GetProxy(uri);
                HttpResponseMessage response;
                try
                {
                    if (endpoint == null || endpoint == uri)
                    {
                        await _check(uri, ct);
                        response = await _direct.SendAsync(current, ct);
                    }
                    else
                    {
                        if (_checkProxyTarget != null) await _checkProxyTarget(uri, ct);
                        // Capture both endpoint and credentials for this request. Do not forward
                        // application headers or credentials as URL parameters or origin headers.
                        var credentials = _proxy.Credentials;
                        var credential = credentials?.GetCredential(endpoint, "Basic");
                        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.AbsoluteUri + "\0" +
                            credential?.Domain + "\0" + credential?.UserName + "\0" + credential?.Password)));
                        var viaProxy = _proxied.GetOrAdd(key, _ => new HttpMessageInvoker(_proxyFactory(endpoint, credentials)));
                        response = await viaProxy.SendAsync(current, ct);
                    }
                }
                catch (HttpRequestException error)
                {
                    if (error is CloudSourceNetworkException) throw;
                    if (error.Message.StartsWith("SOURCE_NETWORK_REQUIRES_PROXY:", StringComparison.Ordinal)) throw;
                    throw new HttpRequestException(error.HttpRequestError,
                        "PUBLIC_SHARE_NETWORK_ERROR: the network request failed (" + error.HttpRequestError + "). Check the source proxy and try again.",
                        null, error.StatusCode);
                }
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
                if (!_followRedirects) return response;
                var location = response.Headers.Location;
                if (location == null || hop == 8 || current.Method != HttpMethod.Get && current.Method != HttpMethod.Head)
                { response.Dispose(); throw new HttpRequestException("The public share returned an unsupported redirect."); }
                var destination = new Uri(uri, location);
                if (destination.Scheme != uri.Scheme)
                { response.Dispose(); throw new HttpRequestException("The public share returned a redirect to a different protocol."); }
                var next = new HttpRequestMessage(current.Method, destination);
                foreach (var header in current.Headers)
                    if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                        (uri.Authority == destination.Authority || header.Key is "Range" or "If-Range" or "If-Match" or "If-None-Match" or "Accept" or "Accept-Encoding" or "User-Agent"))
                        next.Headers.TryAddWithoutValidation(header.Key, header.Value);
                response.Dispose();
                if (ownsRequest) current.Dispose();
                current = next; ownsRequest = true;
            }
            throw new HttpRequestException("The public share returned too many redirects.");
        }
        finally { if (ownsRequest) current.Dispose(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _direct.Dispose();
            foreach (var invoker in _proxied.Values) invoker.Dispose();
        }
        base.Dispose(disposing);
    }
}

public static class PublicShareDownloadClients
{
    public const string PikPak = "Bakabase.PublicShare.PikPak";
    public const string SharePointAttachment = "Bakabase.PublicShare.SharePointAttachment";
    public const string SharePoint = "Bakabase.PublicShare.SharePoint";

    public static IServiceCollection AddPublicShareDownloadClients(this IServiceCollection services)
    {
        Add(PikPak, "pikpak"); Add(SharePoint, "oneDrive");
        Add(SharePointAttachment, "oneDrive", SharePointCloudProvider.RequireAttachmentRoute);
        return services;
        void Add(string name, string provider, Action<Uri, Uri>? route = null) => services.AddHttpClient(name, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 Bakabase-PublicShare/1.0");
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(sp => new ProxyAwarePublicHttpHandler(
            sp.GetRequiredService<BakabaseWebProxy>().ForThirdParty(CloudSites.Site(provider)),
            uri => IsProviderHost(provider, uri), validateRoute: route));
    }

    internal static bool IsProviderHost(string provider, Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || IPAddress.TryParse(uri.IdnHost, out _)) return false;
        var host = uri.IdnHost;
        return provider switch
        {
            "pikpak" => Domain(host, "mypikpak.com") || Domain(host, "mypikpak.net"),
            "oneDrive" => Domain(host, "sharepoint.com") || Domain(host, "sharepointonline.com") || Domain(host, "1drv.com"),
            _ => false
        };
    }

    private static bool Domain(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}

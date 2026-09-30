using System.Net;
using System.Text;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public class ExHentaiApiCookieTests
{
    [TestMethod]
    public async Task MetadataApi_StripsConfiguredAndDefaultAuthenticationHeaders()
    {
        using var preparation = new RequestPreparationHandler(new ThirdPartyCookieContainer());
        var requests = new List<(string Host, string? Cookie)>();
        using var http = new HttpClient(new OfflineTransport(preparation, requests));
        http.DefaultRequestHeaders.Add("Cookie", "default_cookie=secret");
        http.DefaultRequestHeaders.Add("Authorization", "Bearer default-secret");
        http.DefaultRequestHeaders.Add("Referer", "https://exhentai.org/fullimg.php?token=secret");
        var client = new ExHentaiClient(new ClientFactory(http), NullLoggerFactory.Instance);
        await client.GetGalleryMetadata("https://exhentai.org/g/2000001/abcdef0001/");
        Assert.IsNull(requests.Single().Cookie);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MetadataApiAndGalleryPages_KeepLoginCookiesIsolatedInEitherRequestOrder(bool metadataFirst)
    {
        using var preparation = new RequestPreparationHandler(new ThirdPartyCookieContainer());
        var requests = new List<(string Host, string? Cookie)>();
        using var http = new HttpClient(new OfflineTransport(preparation, requests));
        var client = new ExHentaiClient(new ClientFactory(http), NullLoggerFactory.Instance);

        if (metadataFirst)
        {
            await client.GetGalleryMetadata("https://exhentai.org/g/2000001/abcdef0001/");
            await client.ParseList("https://exhentai.org/", includeMetadata: false);
        }
        else
        {
            await client.ParseList("https://exhentai.org/", includeMetadata: false);
            await client.GetGalleryMetadata("https://exhentai.org/g/2000001/abcdef0001/");
        }

        Assert.AreEqual(2, requests.Count);
        Assert.IsNull(requests.Single(r => r.Host == "api.e-hentai.org").Cookie,
            "The public metadata API must not receive the user's login cookies.");
        var pageCookie = requests.Single(r => r.Host == "exhentai.org").Cookie;
        Assert.IsNotNull(pageCookie,
            "An API request must not seed the gallery page's cookie container to the API domain.");
        StringAssert.Contains(pageCookie, "ipb_member_id=123");
        StringAssert.Contains(pageCookie, "ipb_pass_hash=test_hash");
        StringAssert.Contains(pageCookie, "igneous=test_igneous");
    }

    private sealed class RequestPreparationHandler(IThirdPartyCookieContainer cookieContainer)
        : AbstractThirdPartyHttpMessageHandler<TestOptions>(
            new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance),
            ThirdPartyId.ExHentai,
            new BakabaseWebProxy(new NetworkOptionsProvider()),
            new TestOptions(), cookieContainer)
    {
        // Run the production header/cookie preparation with an offline transport below.
        public Task PrepareAsync(HttpRequestMessage request, CancellationToken ct) =>
            base.BeforeRequestingAsync(request, ct);
    }

    private sealed class OfflineTransport(RequestPreparationHandler preparation,
        List<(string Host, string? Cookie)> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await preparation.PrepareAsync(request, cancellationToken);
            var cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values)
                : null;
            requests.Add((request.RequestUri!.Host, cookie));

            var metadata = request.RequestUri.Host == "api.e-hentai.org";
            if (metadata)
            {
                Assert.IsFalse(request.Headers.Contains("Authorization"));
                Assert.IsFalse(request.Headers.Contains("Referer"));
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(metadata
                    ? """
                      {"gmetadata":[{"gid":2000001,"token":"abcdef0001","title":"Gallery",
                      "title_jpn":"","category":"Manga","posted":"1700000000",
                      "filecount":"10","rating":"4.5","torrentcount":"0","tags":[]}]}
                      """
                    : "<div class='itg'></div>", Encoding.UTF8,
                    metadata ? "application/json" : "text/html")
            };
        }
    }

    private sealed class TestOptions : IThirdPartyHttpClientOptions
    {
        public int MaxConcurrency => 1;
        public int RequestInterval => 0;
        public string? Cookie => "ipb_member_id=123; ipb_pass_hash=test_hash; igneous=test_igneous";
        public string? UserAgent => null;
        public string? Referer => null;
        public Dictionary<string, string>? Headers => new()
        {
            ["Cookie"] = "configured_cookie=secret",
            ["Authorization"] = "Bearer configured-secret"
        };
    }

    private sealed class NetworkOptionsProvider : IBOptions<NetworkOptions>
    {
        public NetworkOptions Value { get; } = new();
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

using System.Net;
using System.Text;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public sealed class ExHentaiTorrentSessionTests
{
    private const string GalleryUrl = "https://exhentai.org/g/4211605/beed393bce/";
    private const string WindowUrl = "https://exhentai.org/gallerytorrents.php?gid=4211605&t=beed393bce";
    // The torrent tracker ID can belong to an earlier gallery, rather than the current gid.
    private const string TorrentUrl = "https://exhentai.org/torrent/2366368/3c6bfcd543026d7930276972bca50389648f4144.torrent";
    private const string PersonalizedTorrentUrl = "https://exhentai.org/torrent/2366368/123-accesskey/3c6bfcd543026d7930276972bca50389648f4144.torrent";
    private static readonly byte[] Metadata = Encoding.ASCII.GetBytes(
        "d4:infod6:lengthi1e4:name1:x12:piece lengthi16384e6:pieces20:00000000000000000000ee");
    private string _directory = null!;
    private string DownloadPath => Path.Combine(_directory, "fixture.torrent");

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "BakabaseTorrentSession_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TorrentDownloadContinuesTheWindowSessionAndKeepsTheActualTrackerId(bool relativeLink)
    {
        using var fixture = new Fixture(new TestOptions(), request =>
        {
            if (request.Uri.Host == "api.e-hentai.org") return Json("""
                {"gmetadata":[{"gid":4211605,"token":"beed393bce","title":"Fixture gallery",
                "category":"Manga","posted":1700000000,"filecount":1,"rating":4.5,
                "torrentcount":1,"tags":[],"torrents":[{"hash":"3c6bfcd543026d7930276972bca50389648f4144",
                "added":1699999999,"fsize":1234567}]}]}
                """);
            if (request.Uri.AbsolutePath == "/gallerytorrents.php")
            {
                StringAssert.Contains(request.Cookie!, "igneous=old");
                var href = relativeLink ? new Uri(TorrentUrl).AbsolutePath : TorrentUrl;
                var response = Html($"""
                    <form><table>
                    <tr><td>Posted: 2023-11-14 00:00</td><td>Size: 1.2 MiB</td><td>Downloads: 42</td></tr>
                    <tr><td>Metadata</td></tr>
                    <tr><td><a href="{href}">Fixture torrent</a></td></tr>
                    </table></form>
                    """);
                response.Headers.Add("Set-Cookie", "igneous=refreshed; domain=.exhentai.org; path=/; secure");
                return response;
            }
            // Mirror an authenticated endpoint: dropping the cookie acquired on the window
            // produces HTML, even though both requests began with the same saved cookie.
            return request.Cookie?.Contains("igneous=refreshed", StringComparison.Ordinal) == true &&
                   request.Referer == WindowUrl && request.ConfiguredHeader == "enabled"
                ? Torrent()
                : Html("The torrent file could not be found.");
        });

        var gallery = await fixture.Client.ParseDetail(GalleryUrl, includeTorrents: true);
        var torrent = gallery.Torrents!.Single();
        Assert.AreEqual(TorrentUrl, torrent.DownloadUrl, "Do not rebuild the download path using the API gallery ID.");
        await fixture.Client.DownloadTorrent(torrent.DownloadUrl, DownloadPath);

        CollectionAssert.AreEqual(Metadata, await File.ReadAllBytesAsync(DownloadPath));
        var downloaded = fixture.Requests.Last();
        Assert.AreEqual("default", downloaded.AccountKey);
        StringAssert.Contains(downloaded.Cookie!, "igneous=refreshed");
        Assert.IsFalse(downloaded.Cookie!.Contains("igneous=old", StringComparison.Ordinal));
        Assert.AreEqual(WindowUrl, downloaded.Referer);
        Assert.AreEqual("enabled", downloaded.ConfiguredHeader);
        Assert.IsNull(fixture.Requests.Single(r => r.Uri.Host == "api.e-hentai.org").Cookie);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task PersonalizedClickAddressIsUsedInsteadOfThePublicHref(bool doubleQuotes, bool relativeHref)
    {
        var quote = doubleQuotes ? '"' : '\'';
        var onclick = $"document.location={quote}{PersonalizedTorrentUrl}{quote}; return false";
        using var fixture = new Fixture(new TestOptions(), request =>
        {
            if (request.Uri.Host == "api.e-hentai.org") return GalleryMetadata();
            if (request.Uri.AbsolutePath == "/gallerytorrents.php")
                return Html(TorrentWindow(relativeHref ? new Uri(TorrentUrl).AbsolutePath : TorrentUrl, onclick));
            return request.Uri.AbsoluteUri == PersonalizedTorrentUrl
                ? Torrent()
                : Html("The torrent file could not be found.");
        });

        var gallery = await fixture.Client.ParseDetail(GalleryUrl, includeTorrents: true);
        var torrent = gallery.Torrents!.Single();
        Assert.AreEqual(PersonalizedTorrentUrl, torrent.DownloadUrl);
        await fixture.Client.DownloadTorrent(torrent.DownloadUrl, DownloadPath);

        Assert.AreEqual(PersonalizedTorrentUrl, fixture.Requests.Last().Uri.AbsoluteUri);
        Assert.AreEqual(TorrentUrl.Replace("/2366368/", "/2366368/[redacted]/"),
            fixture.Requests.Last().LogKey);
        Assert.IsFalse(fixture.Requests.Last().LogKey!.Contains("accesskey", StringComparison.Ordinal));
        CollectionAssert.AreEqual(Metadata, await File.ReadAllBytesAsync(DownloadPath));
    }

    [TestMethod]
    public async Task MultipleTorrentFormsKeepTheirOwnLinksAndMetadataAndExcludeTheUploadForm()
    {
        var hashes = new[]
        {
            "3c6bfcd543026d7930276972bca50389648f4144",
            "242516dc92e1fab3b18b0584f935c691298296fa",
            "c22e1ffc6b1a1edc535398695887bb4378be6ffd"
        };
        var downloads = new[] {92, 30, 13};
        var sizes = new[] {109500000L, 273500000L, 284100000L};
        var added = new[] {1704083460L, 1778253360L, 1785335880L};
        var posted = new[] {"2024-01-01 04:31", "2026-05-08 15:16", "2026-07-29 14:38"};
        var htmlSizes = new[] {"109.5 MiB", "273.5 MiB", "284.1 MiB"};
        var urls = hashes.Select(hash => $"https://exhentai.org/torrent/2366368/123-accesskey/{hash}.torrent").ToArray();
        var bytes = Enumerable.Range(0, 3).Select(index => Encoding.ASCII.GetBytes(
            Encoding.ASCII.GetString(Metadata).Replace("4:name1:x", $"4:name1:{(char) ('x' + index)}"))).ToArray();
        var forms = string.Concat(Enumerable.Range(0, 3).Select(index => $"""
            <form method="post"><table>
            <tr><td>Posted: {posted[index]}</td><td>Size: {htmlSizes[index]}</td>
            <td>Torrent Size: 18 KiB</td><td>Seeds: 10</td><td>Peers: 2</td><td>Downloads: {downloads[index]}</td></tr>
            <tr><td colspan="6">Description {index + 1}</td></tr>
            <tr><td colspan="6"><a href="https://exhentai.org/torrent/2366368/{hashes[index]}.torrent"
            onclick="{WebUtility.HtmlEncode($"document.location='{urls[index]}'; return false")}">Torrent {index + 1}</a></td></tr>
            </table></form>
            """)) + "<form method='post' enctype='multipart/form-data'><input type='file' name='torrent' /><input type='submit' value='Upload' /></form>";
        var apiTorrents = Enumerable.Range(0, 3).Select(index => new
            {hash = hashes[index], added = added[index], fsize = sizes[index]}).ToArray();
        using var fixture = new Fixture(new TestOptions(), request =>
        {
            if (request.Uri.Host == "api.e-hentai.org") return Json(System.Text.Json.JsonSerializer.Serialize(new
            {
                gmetadata = new[] {new {gid = 4211605, token = "beed393bce", title = "Fixture gallery",
                    category = "Manga", posted = 1700000000, filecount = 1, rating = 4.5,
                    torrentcount = 3, tags = Array.Empty<string>(), torrents = apiTorrents}}
            }));
            if (request.Uri.AbsolutePath == "/gallerytorrents.php") return Html(forms);
            var index = Array.IndexOf(urls, request.Uri.AbsoluteUri);
            Assert.IsTrue(index >= 0, "Every torrent must retain its own personalized download address.");
            return new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(bytes[index])};
        });

        var gallery = await fixture.Client.ParseDetail(GalleryUrl, includeTorrents: true);

        Assert.AreEqual(3, gallery.Torrents!.Count, "The upload form is not a fourth torrent.");
        CollectionAssert.AreEqual(urls, gallery.Torrents.Select(torrent => torrent.DownloadUrl).ToArray());
        for (var index = 0; index < 3; index++)
        {
            var torrent = gallery.Torrents[index];
            Assert.AreEqual(downloads[index], torrent.Downloaded);
            Assert.AreEqual(sizes[index], torrent.Size);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(added[index]).UtcDateTime, torrent.UpdatedAt);
            var path = Path.Combine(_directory, $"{index}.torrent");
            await fixture.Client.DownloadTorrent(torrent.DownloadUrl, path);
            CollectionAssert.AreEqual(bytes[index], await File.ReadAllBytesAsync(path));
        }
        CollectionAssert.AreEqual(urls, fixture.Requests.Skip(2).Select(request => request.Uri.AbsoluteUri).ToArray());
    }

    [DataTestMethod]
    [DataRow("external")]
    [DataRow("other-account-host")]
    [DataRow("original-image")]
    [DataRow("tracker-id")]
    [DataRow("hash")]
    [DataRow("http")]
    [DataRow("url-credentials")]
    [DataRow("port")]
    [DataRow("query")]
    [DataRow("fragment")]
    [DataRow("script")]
    [DataRow("dynamic")]
    public async Task UnrecognizedOrUnsafeClickAddressesLeaveThePublicHrefUnchanged(string kind)
    {
        var location = kind switch
        {
            "external" => PersonalizedTorrentUrl.Replace("exhentai.org", "example.com"),
            "other-account-host" => PersonalizedTorrentUrl.Replace("exhentai.org", "e-hentai.org"),
            "original-image" => "https://exhentai.org/fullimg.php?gid=4211605&page=1&key=fixture",
            "tracker-id" => PersonalizedTorrentUrl.Replace("2366368", "4211605"),
            "hash" => PersonalizedTorrentUrl.Replace("3c6bfcd543026d7930276972bca50389648f4144",
                "242516dc92e1fab3b18b0584f935c691298296fa"),
            "http" => PersonalizedTorrentUrl.Replace("https:", "http:"),
            "url-credentials" => PersonalizedTorrentUrl.Replace("https://", "https://fixture@"),
            "port" => PersonalizedTorrentUrl.Replace("exhentai.org", "exhentai.org:444"),
            "query" => PersonalizedTorrentUrl + "?redirect=fullimg.php",
            "fragment" => PersonalizedTorrentUrl + "#fixture",
            _ => PersonalizedTorrentUrl
        };
        var onclick = $"document.location='{location}'; return false;";
        if (kind == "script") onclick += " document.location='https://example.com/';";
        if (kind == "dynamic") onclick = $"document.location='{location}' + '?fixture=1'; return false;";
        using var fixture = new Fixture(new TestOptions(), request => request.Uri.Host == "api.e-hentai.org"
            ? GalleryMetadata()
            : request.Uri.AbsolutePath == "/gallerytorrents.php"
                ? Html(TorrentWindow(TorrentUrl, onclick))
                : Torrent());

        var gallery = await fixture.Client.ParseDetail(GalleryUrl, includeTorrents: true);
        var torrent = gallery.Torrents!.Single();
        Assert.AreEqual(TorrentUrl, torrent.DownloadUrl);
        await fixture.Client.DownloadTorrent(torrent.DownloadUrl, DownloadPath);
        Assert.AreEqual(TorrentUrl, fixture.Requests.Last().Uri.AbsoluteUri);
        Assert.IsNull(fixture.Requests.Last().LogKey, "Ordinary torrent URLs retain the default request log key.");
        CollectionAssert.AreEqual(Metadata, await File.ReadAllBytesAsync(DownloadPath));
    }

    [TestMethod]
    public async Task AnUntrustedTorrentRedirectStillStripsConfiguredAndDefaultCredentials()
    {
        using var fixture = new Fixture(new TestOptions(), request => request.Uri.Host == "exhentai.org"
            ? new HttpResponseMessage(HttpStatusCode.Redirect)
                {Headers = {Location = new Uri("https://ehtracker.org/get/2366368/fixture.torrent")}}
            : Torrent());
        fixture.Http.DefaultRequestHeaders.Add("Cookie", "default_cookie=secret");
        fixture.Http.DefaultRequestHeaders.Add("Authorization", "Bearer default-secret");

        await fixture.Client.DownloadTorrent(TorrentUrl, DownloadPath);

        Assert.AreEqual(2, fixture.Requests.Count);
        var accountRequest = fixture.Requests[0];
        Assert.IsNotNull(accountRequest.Cookie);
        Assert.IsNotNull(accountRequest.Authorization);
        Assert.AreEqual(WindowUrl, accountRequest.Referer);
        var publicRequest = fixture.Requests[1];
        Assert.IsNull(publicRequest.Cookie);
        Assert.IsNull(publicRequest.Authorization);
        Assert.IsNull(publicRequest.Referer);
        Assert.IsNull(publicRequest.ConfiguredHeader);
        CollectionAssert.AreEqual(Metadata, await File.ReadAllBytesAsync(DownloadPath));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARedirectCannotSendAPersonalizedTorrentPathToAnotherSite(bool existingFile)
    {
        var previous = Encoding.ASCII.GetBytes("Preserve the existing file.");
        if (existingFile) await File.WriteAllBytesAsync(DownloadPath, previous);
        using var fixture = new Fixture(new TestOptions(), _ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = {Location = new Uri(PersonalizedTorrentUrl.Replace("exhentai.org", "example.com"))}
        });

        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            fixture.Client.DownloadTorrent(PersonalizedTorrentUrl, DownloadPath));

        Assert.AreEqual(1, fixture.Requests.Count, "Block the credential-bearing destination before sending it.");
        Assert.IsFalse(error.Message.Contains("accesskey", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Requests.Single().LogKey!.Contains("accesskey", StringComparison.Ordinal));
        if (existingFile) CollectionAssert.AreEqual(previous, await File.ReadAllBytesAsync(DownloadPath));
        else Assert.IsFalse(File.Exists(DownloadPath));
        Assert.AreEqual(existingFile ? 1 : 0, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public async Task AnExplicitAccountSnapshotStillOverridesAndIsolatesConfiguredHeaders()
    {
        var options = new TestOptions
        {
            Headers = new Dictionary<string, string>
            {
                ["Cookie"] = "configured_cookie=wrong_account",
                ["Authorization"] = "Bearer configured-secret"
            }
        };
        var context = new ExHentaiRequestContext("ipb_member_id=456; ipb_pass_hash=snapshot; igneous=snapshot");
        using var fixture = new Fixture(options, _ => Torrent());

        await fixture.Client.DownloadTorrent(TorrentUrl, DownloadPath, context: context);

        var request = fixture.Requests.Single();
        Assert.AreEqual(context.Cookie, request.Cookie);
        Assert.AreEqual(context.AccountKey + ":exhentai.org", request.AccountKey);
        Assert.IsNull(request.Authorization);
        Assert.IsNull(request.Referer);
        CollectionAssert.AreEqual(Metadata, await File.ReadAllBytesAsync(DownloadPath));
    }

    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK)
        {Content = new StringContent(body, Encoding.UTF8, "text/html")};
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {Content = new StringContent(body, Encoding.UTF8, "application/json")};
    private static HttpResponseMessage Torrent() => new(HttpStatusCode.OK)
        {Content = new ByteArrayContent(Metadata)};

    private static HttpResponseMessage GalleryMetadata() => Json("""
        {"gmetadata":[{"gid":4211605,"token":"beed393bce","title":"Fixture gallery",
        "category":"Manga","posted":1700000000,"filecount":1,"rating":4.5,
        "torrentcount":1,"tags":[],"torrents":[{"hash":"3c6bfcd543026d7930276972bca50389648f4144",
        "added":1699999999,"fsize":1234567}]}]}
        """);

    private static string TorrentWindow(string href, string onclick) => $"""
        <form><table>
        <tr><td>Posted: 2023-11-14 00:00</td><td>Size: 1.2 MiB</td><td>Downloads: 42</td></tr>
        <tr><td>Metadata</td></tr>
        <tr><td><a href="{WebUtility.HtmlEncode(href)}" onclick="{WebUtility.HtmlEncode(onclick)}">Fixture torrent</a></td></tr>
        </table></form>
        """;

    private sealed class TestOptions : IThirdPartyHttpClientOptions
    {
        public int MaxConcurrency => 1;
        public int RequestInterval => 0;
        public string? Cookie => "ipb_member_id=123; ipb_pass_hash=test_hash; igneous=old";
        public string? UserAgent => null;
        public string? Referer => WindowUrl;
        public Dictionary<string, string>? Headers {get; init;} = new() {["X-Fixture-Header"] = "enabled"};
    }

    private sealed class RequestPreparationHandler(TestOptions options, IThirdPartyCookieContainer cookieContainer)
        : AbstractThirdPartyHttpMessageHandler<TestOptions>(
            new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance),
            ThirdPartyId.ExHentai, new BakabaseWebProxy(new NetworkOptionsProvider()), options, cookieContainer)
    {
        public Task PrepareAsync(HttpRequestMessage request, CancellationToken ct) =>
            base.BeforeRequestingAsync(request, ct);
    }

    private sealed class NetworkOptionsProvider : IBOptions<NetworkOptions>
    {
        public NetworkOptions Value {get;} = new();
    }

    private sealed record Request(Uri Uri, string? Cookie, string? Referer, string? Authorization,
        string? ConfiguredHeader, string AccountKey, string? LogKey);

    private sealed class Fixture : IDisposable
    {
        private readonly RequestPreparationHandler _preparation;
        public readonly List<Request> Requests = [];
        public HttpClient Http {get;}
        public ExHentaiClient Client {get;}
        public Fixture(TestOptions options, Func<Request, HttpResponseMessage> respond)
        {
            var cookies = new ThirdPartyCookieContainer();
            _preparation = new RequestPreparationHandler(options, cookies);
            Http = new HttpClient(new OfflineTransport(_preparation, cookies, options, Requests, respond));
            Client = new ExHentaiClient(new Factory(Http), NullLoggerFactory.Instance);
        }
        public void Dispose() {Http.Dispose(); _preparation.Dispose();}
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class OfflineTransport(RequestPreparationHandler preparation, ThirdPartyCookieContainer cookies,
        TestOptions options, List<Request> requests, Func<Request, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await preparation.PrepareAsync(request, ct);
            string? Header(string name) => request.Headers.TryGetValues(name, out var values)
                ? string.Join("; ", values) : null;
            request.Options.TryGetValue(ThirdPartyRequestOptions.AccountKey, out var accountKey);
            request.Options.TryGetValue(ThirdPartyRequestOptions.Cookie, out var cookieOverride);
            request.Options.TryGetValue(ThirdPartyRequestOptions.RequestLogKey, out var logKey);
            var recorded = new Request(request.RequestUri!, Header("Cookie"), Header("Referer"),
                Header("Authorization"), Header("X-Fixture-Header"), accountKey ?? "default", logKey);
            requests.Add(recorded);
            var response = respond(recorded);
            // The real handler processes Set-Cookie against this same per-request key after
            // preparing/sending. Keep that production cookie-container behavior in the fixture.
            cookies.ProcessResponse($"{ThirdPartyId.ExHentai}:{recorded.AccountKey}",
                cookieOverride ?? options.Cookie, request.RequestUri!, response);
            return response;
        }
    }
}

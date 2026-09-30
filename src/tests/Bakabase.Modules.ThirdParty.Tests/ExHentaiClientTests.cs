using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.RequestModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public class ExHentaiClientTests
{
    private const int FirstId = 2000001;
    private const string FirstToken = "abcdef0001";
    private const string FirstUrl = "https://exhentai.org/g/2000001/abcdef0001/";
    private const string SecondUrl = "https://exhentai.org/g/2000002/abcdef0002/";

    [TestMethod]
    public async Task GetGalleryMetadata_UsesOfficialApiAndMapsStringAndNumericFields()
    {
        using var fixture = new ClientFixture((_, _) => Task.FromResult(JsonResponse("""
            {"gmetadata":[{
              "gid":2000001,"token":"abcdef0001",
              "title":"English &amp; Romaji","title_jpn":"日本語 &amp; 原題",
              "category":"Doujinshi","thumb":"https://ehgt.org/test.jpg",
              "posted":"1700000000","filecount":"329","rating":"4.68",
              "torrentcount":2,
              "tags":["artist:example","language:english","artist:other","other:full color"],
              "torrents":[{"hash":"1111111111111111111111111111111111111111",
                "added":"1699999999","fsize":"1234567"}]
            }]}
            """)));

        var resource = await fixture.Client.GetGalleryMetadata(FirstUrl);

        Assert.AreEqual(FirstId, resource.Id);
        Assert.AreEqual(FirstUrl, resource.Url);
        Assert.AreEqual("English & Romaji", resource.Name);
        Assert.AreEqual("日本語 & 原題", resource.RawName);
        Assert.AreEqual(ExHentaiCategory.Doushijin, resource.Category);
        Assert.AreEqual("https://ehgt.org/test.jpg", resource.CoverUrl);
        Assert.AreEqual(329, resource.FileCount);
        Assert.AreEqual(4.68m, resource.Rate);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, resource.UpdateDt);
        Assert.AreEqual(DateTimeKind.Utc, resource.UpdateDt.Kind);
        Assert.AreEqual(2, resource.TorrentCount);
        Assert.AreEqual(0, resource.PageCount);
        Assert.IsNull(resource.Torrents, "API torrent metadata does not provide authenticated download URLs.");
        CollectionAssert.AreEqual(new[] { "example", "other" }, resource.Tags["artist"]);
        CollectionAssert.AreEqual(new[] { "english" }, resource.Tags["language"]);

        var request = fixture.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("https://api.e-hentai.org/api.php", request.Uri.AbsoluteUri);
        Assert.AreEqual("application/json", request.ContentType);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.AreEqual("gdata", body.RootElement.GetProperty("method").GetString());
        Assert.AreEqual(1, body.RootElement.GetProperty("namespace").GetInt32());
        var key = body.RootElement.GetProperty("gidlist").EnumerateArray().Single();
        Assert.AreEqual(FirstId, key[0].GetInt32());
        Assert.AreEqual(FirstToken, key[1].GetString());
    }

    [DataTestMethod]
    [DataRow("Artist CG", ExHentaiCategory.ArtistCG)]
    [DataRow("Game CG", ExHentaiCategory.GameCG)]
    [DataRow("Image Set", ExHentaiCategory.ImageSet)]
    [DataRow("Non-H", ExHentaiCategory.NonH)]
    [DataRow("Private", ExHentaiCategory.Unknown)]
    public async Task GetGalleryMetadata_HandlesApiCategoryNamesAndFallsBackToEnglishTitle(
        string category, ExHentaiCategory expected)
    {
        using var fixture = new ClientFixture((_, _) => Task.FromResult(MetadataResponse(new[]
        {
            Metadata(FirstId, FirstToken, category)
        })));

        var resource = await fixture.Client.GetGalleryMetadata(FirstUrl);

        Assert.AreEqual(expected, resource.Category);
        Assert.AreEqual("Gallery 2000001", resource.RawName);
        Assert.AreEqual(10, resource.FileCount);
        Assert.AreEqual(4.5m, resource.Rate);
    }

    [TestMethod]
    public async Task MetadataProvider_RawJsonOmitsUnknownPageCountAndKeepsNormalizedCamelCaseFields()
    {
        using var fixture = new ClientFixture((_, _) => Task.FromResult(JsonResponse("""
            {"gmetadata":[{"gid":2000001,"token":"abcdef0001","title":"English",
            "title_jpn":"日本語 &amp; 原題","category":"Manga","posted":"1700000000",
            "filecount":"10","rating":"4.5","torrentcount":"0","tags":["artist:example"]}]}
            """)));
        var provider = new ExHentaiMetadataProvider(fixture.Client);

        var metadata = await provider.FetchMetadataAsync("2000001/abcdef0001", CancellationToken.None);

        Assert.IsNotNull(metadata);
        using var raw = JsonDocument.Parse(metadata.RawJson!);
        Assert.IsFalse(raw.RootElement.TryGetProperty("pageCount", out _));
        Assert.AreEqual("日本語 & 原題", raw.RootElement.GetProperty("rawName").GetString());
        Assert.AreEqual(4.5m, raw.RootElement.GetProperty("rate").GetDecimal());
        Assert.AreEqual("example", raw.RootElement.GetProperty("tags").GetProperty("artist")[0].GetString());
        Assert.IsNull(metadata.PredefinedFieldValues[nameof(ExHentaiMetadataField.PageCount)]);
    }

    [TestMethod]
    public async Task GetGalleryMetadata_BatchesUniqueKeysAndRestoresRequestedOrderIncludingDuplicates()
    {
        var urls = Enumerable.Range(0, 26).Select(index =>
            $"https://exhentai.org/g/{FirstId + index}/{TokenFor(FirstId + index)}/").ToList();
        urls.Add(urls[0]);
        urls.Add(urls[1].Replace("exhentai.org", "e-hentai.org"));
        using var fixture = new ClientFixture((request, _) =>
            Task.FromResult(MetadataForRequest(request, reverse: true)));

        var resources = await fixture.Client.GetGalleryMetadata(urls);

        CollectionAssert.AreEqual(
            Enumerable.Range(FirstId, 26).Concat(new[] { FirstId, FirstId + 1 }).ToArray(),
            resources.Select(resource => resource.Id).ToArray());
        CollectionAssert.AreEqual(urls.ToArray(), resources.Select(resource => resource.Url).ToArray());
        Assert.AreEqual(2, fixture.Requests.Count);
        var batches = fixture.Requests.Select(request =>
        {
            using var body = JsonDocument.Parse(request.Body!);
            return body.RootElement.GetProperty("gidlist").GetArrayLength();
        }).ToArray();
        CollectionAssert.AreEqual(new[] { 25, 1 }, batches);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task GetGalleryMetadata_RejectsPartialErrorsAndMissingEntriesWithGalleryId(bool explicitError)
    {
        var metadata = explicitError
            ? new object[] { Metadata(FirstId, FirstToken), new { gid = 2000002, error = "Incorrect key" } }
            : new object[] { Metadata(FirstId, FirstToken) };
        using var fixture = new ClientFixture((_, _) => Task.FromResult(MetadataResponse(metadata)));

        var error = await CaptureError(() => fixture.Client.GetGalleryMetadata(new[] { FirstUrl, SecondUrl }));

        Assert.IsTrue(error is InvalidOperationException or InvalidDataException, error.ToString());
        StringAssert.Contains(error.Message, "2000002");
    }

    [DataTestMethod]
    [DataRow("gltm")]
    [DataRow("gltc")]
    [DataRow("glte")]
    [DataRow("gld")]
    public async Task ParseList_DiscoveryWorksAcrossLayoutsWithoutParsingDisplayedMetadata(string layout)
    {
        var html = SearchHtml(layout);
        using var fixture = new ClientFixture((_, _) => Task.FromResult(HtmlResponse(html)));

        var list = await fixture.Client.ParseList("https://exhentai.org/?f_search=example", includeMetadata: false);

        Assert.AreEqual(1234, list.ResultCount);
        Assert.AreEqual("https://exhentai.org/?f_search=example&next=2000001", list.NextListUrl);
        CollectionAssert.AreEqual(new[] { 2000002, 2000001 }, list.Resources.Select(resource => resource.Id).ToArray());
        CollectionAssert.AreEqual(new[] { SecondUrl, FirstUrl }, list.Resources.Select(resource => resource.Url).ToArray());
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.AreEqual(HttpMethod.Get, fixture.Requests[0].Method);
    }

    [TestMethod]
    public async Task ParseList_EnrichesDiscoveredKeysThroughApiAndKeepsHtmlOrder()
    {
        using var fixture = new ClientFixture((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? HtmlResponse(SearchHtml("gld"))
                : MetadataForRequest(request, reverse: true)));

        var list = await fixture.Client.ParseList("https://exhentai.org/?f_search=example");

        CollectionAssert.AreEqual(new[] { 2000002, 2000001 }, list.Resources.Select(resource => resource.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "Gallery 2000002", "Gallery 2000001" },
            list.Resources.Select(resource => resource.Name).ToArray());
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreEqual(HttpMethod.Get, fixture.Requests[0].Method);
        Assert.AreEqual(HttpMethod.Post, fixture.Requests[1].Method);
    }

    [TestMethod]
    public async Task Search_FollowsSiteCursorsAndOnlyEnrichesTheRequestedPage()
    {
        var htmlRequests = 0;
        using var fixture = new ClientFixture((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(MetadataForRequest(request));

            htmlRequests++;
            var html = htmlRequests < 3
                ? $"<div class='itg'><a href='{FirstUrl}'>Gallery</a></div>" +
                  $"<a id='unext' href='/?f_search=example&amp;next={2000100 - htmlRequests}'>Next</a>"
                : SearchHtml("gld");
            return Task.FromResult(HtmlResponse(html));
        });

        var list = await fixture.Client.Search(new ExHentaiSearchRequestModel
        {
            Keyword = "example",
            PageIndex = 3
        });

        CollectionAssert.AreEqual(new[] { 2000002, 2000001 }, list.Resources.Select(resource => resource.Id).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            "https://exhentai.org/?f_search=example",
            "https://exhentai.org/?f_search=example&next=2000099",
            "https://exhentai.org/?f_search=example&next=2000098"
        }, fixture.Requests.Where(request => request.Method == HttpMethod.Get)
            .Select(request => request.Uri.AbsoluteUri).ToArray());
        Assert.AreEqual(1, fixture.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.AreEqual(HttpMethod.Post, fixture.Requests.Last().Method);
    }

    [TestMethod]
    public async Task ParseDetail_WithoutTorrentsDoesNotFetchGalleryHtmlOrInferThumbnailPageCount()
    {
        using var fixture = new ClientFixture((request, _) =>
            Task.FromResult(MetadataForRequest(request)));

        var resource = await fixture.Client.ParseDetail(FirstUrl, includeTorrents: false);

        Assert.AreEqual(FirstId, resource.Id);
        Assert.AreEqual(0, resource.PageCount);
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, fixture.Requests.Single().Method);
    }

    [DataTestMethod]
    [DataRow("<div id='gdt'></div><table class='ptt'><tr><td>&lt;</td><td><a>1</a></td><td><a>5</a></td><td>&gt;</td></tr></table>", 5)]
    [DataRow("<div id='gdt'></div>", 1)]
    public async Task GetGalleryPageCount_ReadsActualThumbnailPaginationOnlyWhenRequested(string html, int expected)
    {
        using var fixture = new ClientFixture((_, _) => Task.FromResult(HtmlResponse(html)));

        var pageCount = await fixture.Client.GetGalleryPageCount(FirstUrl);

        Assert.AreEqual(expected, pageCount);
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.AreEqual(HttpMethod.Get, fixture.Requests.Single().Method);
        Assert.AreEqual(FirstUrl, fixture.Requests.Single().Uri.AbsoluteUri);
    }

    [DataTestMethod]
    [DataRow("<html><form>Login required</form></html>")]
    [DataRow("<html>This gallery has been removed.</html>")]
    public async Task GetGalleryPageCount_RejectsUnavailableGalleryInsteadOfTreatingItAsOnePage(string html)
    {
        using var fixture = new ClientFixture((_, _) => Task.FromResult(HtmlResponse(html)));

        var error = await CaptureError(() => fixture.Client.GetGalleryPageCount(FirstUrl));

        Assert.IsInstanceOfType<InvalidDataException>(error);
        Assert.AreEqual(1, fixture.Requests.Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ParseDetail_TorrentDownloadsUseWindowLinksAndExactApiSizeAndTimestamp(bool relativeLink)
    {
        const string hash = "1111111111111111111111111111111111111111";
        const string torrentUrl = "https://exhentai.org/torrent/2000001/1111111111111111111111111111111111111111.torrent";
        const long exactSize = 1234567;
        var downloadLink = relativeLink ? new Uri(torrentUrl).AbsolutePath : torrentUrl;
        using var fixture = new ClientFixture((request, _) => Task.FromResult(
            request.Method == HttpMethod.Post
                ? JsonResponse("""
                    {"gmetadata":[{"gid":2000001,"token":"abcdef0001","title":"Gallery",
                    "title_jpn":"","category":"Manga","posted":"1700000000","filecount":"10",
                    "rating":"4.5","torrentcount":"1","tags":[],"torrents":[{
                    "hash":"1111111111111111111111111111111111111111","added":"1699999999",
                    "fsize":"1234567"}]}]}
                    """)
                : request.Uri.AbsolutePath.Contains("gallerytorrents.php", StringComparison.Ordinal)
                    ? HtmlResponse($"""
                        <form><table>
                        <tr><td>Posted: 2023-11-14 00:00</td><td>Size: 1.2 MiB</td><td>Downloads: 42</td></tr>
                        <tr><td>Metadata</td></tr>
                        <tr><td><a href="{downloadLink}">Download</a></td></tr>
                        </table></form>
                        """)
                    : HtmlResponse("""
                        <div id="gd5"><a onclick="return popUp('https://exhentai.org/gallerytorrents.php?gid=2000001&amp;t=abcdef0001',600,400)">Torrent Download (1)</a></div>
                        """)));

        var resource = await fixture.Client.ParseDetail(FirstUrl, includeTorrents: true);

        Assert.IsNotNull(resource.Torrents);
        var torrent = resource.Torrents.Single();
        Assert.AreEqual(torrentUrl, torrent.DownloadUrl);
        StringAssert.Contains(torrent.DownloadUrl, hash);
        Assert.AreEqual(exactSize, torrent.Size);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1699999999).UtcDateTime, torrent.UpdatedAt);
        Assert.AreEqual(DateTimeKind.Utc, torrent.UpdatedAt.Kind);
        Assert.AreEqual(42, torrent.Downloaded);
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreEqual("/gallerytorrents.php",
            fixture.Requests.Single(request => request.Method == HttpMethod.Get).Uri.AbsolutePath);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CancelledRequests_DoNotEnterHttpHandler(bool metadata)
    {
        using var fixture = new ClientFixture((_, _) => throw new AssertFailedException("Unexpected HTTP request."));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await CaptureError(() => metadata
            ? fixture.Client.GetGalleryMetadata(FirstUrl, cancellation.Token)
            : fixture.Client.ParseList("https://exhentai.org/", cancellation.Token));

        Assert.IsInstanceOfType<OperationCanceledException>(error);
        Assert.AreEqual(0, fixture.Requests.Count);
    }

    [TestMethod]
    public async Task GetGalleryMetadata_CancelsInFlightApiRequest()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new ClientFixture(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            throw new AssertFailedException("Cancellation was ignored.");
        });

        var error = await CaptureError(() => fixture.Client.GetGalleryMetadata(FirstUrl, cancellation.Token));

        Assert.IsInstanceOfType<OperationCanceledException>(error);
        Assert.AreEqual(1, fixture.Requests.Count);
    }

    [TestMethod]
    public async Task GetGalleryMetadata_CancelsWhileWaitingForApiGateAndLaterRequestsStillSucceed()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var fixture = new ClientFixture(async (request, token) =>
        {
            if (++calls == 1)
            {
                firstEntered.SetResult();
                return await firstResponse.Task.WaitAsync(token);
            }

            return MetadataForRequest(request);
        });
        var first = fixture.Client.GetGalleryMetadata(FirstUrl);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var queued = fixture.Client.GetGalleryMetadata(SecondUrl, cancellation.Token);
        cancellation.Cancel();

        try
        {
            var error = await CaptureError(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsInstanceOfType<OperationCanceledException>(error);
            Assert.AreEqual(1, fixture.Requests.Count, "The cancelled request must stay outside the HTTP handler.");
        }
        finally
        {
            firstResponse.TrySetResult(MetadataForRequest(fixture.Requests[0]));
        }

        Assert.AreEqual(FirstId, (await first).Id);
        Assert.AreEqual(2000002, (await fixture.Client.GetGalleryMetadata(SecondUrl)).Id);
        Assert.AreEqual(2, fixture.Requests.Count);
    }

    [TestMethod]
    public async Task GetGalleryMetadata_HttpFailureReleasesApiGateForNextRequest()
    {
        var calls = 0;
        using var fixture = new ClientFixture((request, _) => Task.FromResult(++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : MetadataForRequest(request)));

        var error = await CaptureError(() => fixture.Client.GetGalleryMetadata(FirstUrl));

        Assert.IsInstanceOfType<HttpRequestException>(error);
        Assert.AreEqual(FirstId, (await fixture.Client.GetGalleryMetadata(FirstUrl)
            .WaitAsync(TimeSpan.FromSeconds(5))).Id);
        Assert.AreEqual(2, fixture.Requests.Count);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task GetGalleryMetadata_RetriesTransientTimeoutOrEofThroughThirdAttempt(bool timeout)
    {
        var calls = 0;
        using var fixture = new ClientFixture((request, _) =>
        {
            if (++calls < 3) throw TransientFailure(timeout);
            return Task.FromResult(MetadataForRequest(request));
        });

        var resource = await fixture.Client.GetGalleryMetadata(FirstUrl);

        Assert.AreEqual(FirstId, resource.Id);
        Assert.AreEqual(3, fixture.Requests.Count);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task GetGalleryMetadata_StopsTransientRetriesAfterThreeAttempts(bool timeout)
    {
        using var fixture = new ClientFixture((_, _) => throw TransientFailure(timeout));

        var error = await CaptureError(() => fixture.Client.GetGalleryMetadata(FirstUrl));

        Assert.IsTrue(timeout ? error is TaskCanceledException : error is HttpRequestException, error.ToString());
        Assert.AreEqual(3, fixture.Requests.Count);
    }

    [TestMethod]
    public async Task GetGalleryMetadata_BurstPauseHonorsCancellationBeforeSendingFifthRequest()
    {
        using var fixture = new ClientFixture((request, _) => Task.FromResult(MetadataForRequest(request)));
        for (var request = 0; request < 4; request++)
            await fixture.Client.GetGalleryMetadata(FirstUrl);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var elapsed = Stopwatch.StartNew();
        var error = await CaptureError(() => fixture.Client.GetGalleryMetadata(FirstUrl, cancellation.Token));

        Assert.IsInstanceOfType<OperationCanceledException>(error);
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Cancellation must interrupt the API burst pause.");
        Assert.AreEqual(4, fixture.Requests.Count);
    }

    private static string SearchHtml(string layout)
    {
        const string anchors = """
            <a href="/g/2000002/abcdef0002/">Unrelated display text</a>
            <a href="https://exhentai.org/g/2000001/abcdef0001/">Title not used</a>
            <a href="https://exhentai.org/g/2000002/abcdef0002/">Duplicate thumbnail</a>
            <a href="https://example.com/g/2000003/abcdef0003/">External link</a>
            <a href="/g/2000004/bad/">Invalid token</a>
            <a href="/s/abcdef0002/2000002-1">Image page</a>
            """;
        var container = layout == "gld"
            ? $"<div class='itg {layout}'><div>{anchors}</div></div>"
            : $"<table class='itg {layout}'><tbody><tr><td>{anchors}</td></tr></tbody></table>";
        return $"""
            <div class="searchtext">Found about 1,234 results.</div>
            <a href="/g/2000005/abcdef0005/">Outside search results</a>
            {container}
            <a id="unext" href="/?f_search=example&amp;next=2000001">Next</a>
            """;
    }

    private static string TokenFor(int id) => $"{id:x10}";

    private static Exception TransientFailure(bool timeout) => timeout
        ? new TaskCanceledException("Request timed out", new TimeoutException())
        : new HttpRequestException("Connection closed", new IOException("Unexpected EOF"));

    private static object Metadata(int id, string token, string category = "Manga") => new
    {
        gid = id,
        token,
        title = $"Gallery {id}",
        title_jpn = "",
        category,
        thumb = "https://ehgt.org/test.jpg",
        posted = 1700000000,
        filecount = 10,
        rating = 4.5,
        torrentcount = 0,
        tags = Array.Empty<string>(),
        torrents = Array.Empty<object>()
    };

    private static HttpResponseMessage MetadataForRequest(RequestRecord request, bool reverse = false)
    {
        Assert.AreEqual(HttpMethod.Post, request.Method);
        using var body = JsonDocument.Parse(request.Body!);
        var metadata = body.RootElement.GetProperty("gidlist").EnumerateArray()
            .Select(key => Metadata(key[0].GetInt32(), key[1].GetString()!)).ToArray();
        return MetadataResponse(reverse ? metadata.Reverse() : metadata);
    }

    private static HttpResponseMessage MetadataResponse(IEnumerable<object> metadata) =>
        JsonResponse(JsonSerializer.Serialize(new { gmetadata = metadata }));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage HtmlResponse(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html")
    };

    private static async Task<Exception> CaptureError(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            return error;
        }

        throw new AssertFailedException("Expected an exception.");
    }

    private sealed record RequestRecord(HttpMethod Method, Uri Uri, string? Body, string? ContentType);

    private sealed class ClientFixture : IDisposable
    {
        private readonly HttpClient _httpClient;
        public List<RequestRecord> Requests { get; } = new();
        public ExHentaiClient Client { get; }

        public ClientFixture(Func<RequestRecord, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _httpClient = new HttpClient(new RecordingHandler(Requests, respond));
            Client = new ExHentaiClient(new ClientFactory(_httpClient), NullLoggerFactory.Instance);
        }

        public void Dispose() => _httpClient.Dispose();
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(List<RequestRecord> requests,
        Func<RequestRecord, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var recorded = new RequestRecord(request.Method, request.RequestUri!,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Content?.Headers.ContentType?.MediaType);
            requests.Add(recorded);
            return await respond(recorded, cancellationToken);
        }
    }
}

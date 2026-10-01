using System.Net;
using System.Text;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public sealed class ExHentaiTorrentDownloadTests
{
    private const string Url = "https://exhentai.org/torrent/123/hash.torrent";
    private string _directory = null!;
    private string PathToTorrent => Path.Combine(_directory, "test.torrent");
    private static readonly byte[] Metadata = Encoding.ASCII.GetBytes(
        "d4:infod6:lengthi1e4:name1:x12:piece lengthi16384e6:pieces20:00000000000000000000ee");

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "BakabaseTorrentResponse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [DataTestMethod]
    [DataRow("text/html", "The torrent file could not be found.")]
    [DataRow(null, "<html>Please log in</html>")]
    [DataRow("application/octet-stream", "not a torrent")]
    [DataRow("application/octet-stream", "")]
    public async Task ErrorResponses_AreRejectedWithoutCreatingOrCachingATorrent(string? mediaType, string body)
    {
        using var fixture = new Fixture(_ => Response(Encoding.UTF8.GetBytes(body), mediaType));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            fixture.Client.DownloadTorrent(Url, PathToTorrent));
        Assert.IsFalse(error.Message.Contains("4 MiB"));
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public async Task InvalidExistingFile_IsRefetchedAndAtomicallyReplacedOnRetry()
    {
        await File.WriteAllTextAsync(PathToTorrent, "<html>Old login page</html>");
        var attempt = 0;
        using var fixture = new Fixture(_ => ++attempt == 1
            ? Response(Encoding.UTF8.GetBytes("<html>Login failed</html>"), "text/html")
            : Response(Metadata, "application/x-bittorrent"));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Client.DownloadTorrent(Url, PathToTorrent));
        Assert.AreEqual("<html>Old login page</html>", await File.ReadAllTextAsync(PathToTorrent));
        CollectionAssert.AreEqual(new[] {PathToTorrent}, Directory.GetFiles(_directory));
        await fixture.Client.DownloadTorrent(Url, PathToTorrent);
        Assert.AreEqual(2, attempt, "An old filename must not suppress the retry request.");
        CollectionAssert.AreEqual(Metadata, await File.ReadAllBytesAsync(PathToTorrent));
        Assert.AreEqual(1, Directory.GetFiles(_directory).Length);
    }

    [DataTestMethod]
    [DataRow(200, false)]
    [DataRow(240, false)]
    [DataRow(240, true)]
    public async Task LongLegalTargetNames_AreReplacedAtomicallyWithoutLengtheningTheTemporaryBasename(
        int componentLength, bool unicode)
    {
        var path = LongTorrentPath(componentLength, unicode);
        await File.WriteAllTextAsync(path, "Previously downloaded content.");
        var requests = 0;
        using var fixture = new Fixture(_ => { requests++; return Response(Metadata, "application/x-bittorrent"); });

        await fixture.Client.DownloadTorrent(Url, path);

        Assert.AreEqual(1, requests);
        CollectionAssert.AreEqual(Metadata, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(new[] {path}, Directory.GetFiles(_directory));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LongTarget_InvalidResponseKeepsTheExistingFileAndLeavesNoStagingFiles(bool unicode)
    {
        var path = LongTorrentPath(240, unicode);
        var previous = Encoding.UTF8.GetBytes("Previous torrent must remain untouched.");
        await File.WriteAllBytesAsync(path, previous);
        using var fixture = new Fixture(_ => Response("<html>Login required</html>"u8.ToArray(), "text/html"));

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Client.DownloadTorrent(Url, path));

        CollectionAssert.AreEqual(previous, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(new[] {path}, Directory.GetFiles(_directory));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LongTarget_CancellationAfterReadingMetadataKeepsTheExistingFileAndCleansStaging(bool unicode)
    {
        var path = LongTorrentPath(240, unicode);
        var previous = Encoding.UTF8.GetBytes("Previous torrent must remain untouched.");
        await File.WriteAllBytesAsync(path, previous);
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {Content = new CancelAtEndContent(cancellation)});

        try
        {
            await fixture.Client.DownloadTorrent(Url, path, cancellation.Token);
            Assert.Fail("A cancelled download must not replace its target.");
        }
        catch (OperationCanceledException) { Assert.IsTrue(cancellation.IsCancellationRequested); }

        CollectionAssert.AreEqual(previous, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(new[] {path}, Directory.GetFiles(_directory));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LongTarget_PromotionFailureCleansStagingAndPreservesExistingDirectoryContents(bool unicode)
    {
        var path = LongTorrentPath(240, unicode);
        Directory.CreateDirectory(path);
        var previous = Path.Combine(path, "keep.txt");
        await File.WriteAllTextAsync(previous, "Existing contents.");
        using var fixture = new Fixture(_ => Response(Metadata, "application/x-bittorrent"));

        try
        {
            await fixture.Client.DownloadTorrent(Url, path);
            Assert.Fail("A torrent download cannot replace an existing directory.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

        Assert.AreEqual("Existing contents.", await File.ReadAllTextAsync(previous));
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
        CollectionAssert.AreEqual(new[] {path}, Directory.GetDirectories(_directory));
    }

    [TestMethod]
    public async Task ValidBinaryWithNoContentType_IsAccepted()
    {
        using var fixture = new Fixture(_ => Response(Metadata));
        await fixture.Client.DownloadTorrent(Url, PathToTorrent);
        CollectionAssert.AreEqual(Metadata, await File.ReadAllBytesAsync(PathToTorrent));
    }

    [TestMethod]
    public async Task RedirectToAnotherHost_SuppressesAccountAndConfiguredHeaders()
    {
        var requests = 0;
        using var fixture = new Fixture(request =>
        {
            requests++;
            if (requests == 1)
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                    {Headers = {Location = new Uri("https://ehtracker.org/get/public.torrent")}};
            Assert.IsTrue(request.Options.TryGetValue(ThirdPartyRequestOptions.SkipConfiguredHeaders, out var skip) && skip);
            Assert.IsTrue(request.Options.TryGetValue(ThirdPartyRequestOptions.SuppressSensitiveHeaders, out var suppress) && suppress);
            Assert.IsTrue(request.Options.TryGetValue(ThirdPartyRequestOptions.Cookie, out var cookie) && cookie == "");
            return Response(Metadata);
        });
        await fixture.Client.DownloadTorrent(Url, PathToTorrent);
        Assert.AreEqual(2, requests);
    }

    [TestMethod]
    public async Task UnsafeOrLoopingRedirects_DoNotWriteFiles()
    {
        using (var fixture = new Fixture(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
               {Headers = {Location = new Uri("file:///private.torrent")}}))
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Client.DownloadTorrent(Url, PathToTorrent));
        var requests = 0;
        using (var fixture = new Fixture(_ =>
               {
                   requests++;
                   return new HttpResponseMessage(HttpStatusCode.Redirect) {Headers = {Location = new Uri(Url)}};
               }))
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Client.DownloadTorrent(Url, PathToTorrent));
        Assert.AreEqual(6, requests);
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public async Task OversizedResponses_KeepTheActualSizeLimitError()
    {
        using var fixture = new Fixture(_ => Response(new byte[4 * 1024 * 1024 + 1]));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Client.DownloadTorrent(Url, PathToTorrent));
        StringAssert.Contains(error.Message, "4 MiB");
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
    }

    [DataTestMethod]
    [DataRow("https://exhentai.org/fullimg/123/1/key/image.jpg")]
    [DataRow("https://exhentai.org/%66ullimg.php?gid=123&page=1")]
    [DataRow("https://exhentai.org/fullimg%2F123%2F1%2Fkey")]
    [DataRow("http://exhentai.org/torrent/123/hash.torrent")]
    public async Task RedirectsCannotReachAFeeEndpointOrDowngradeAccountCredentials(string location)
    {
        var requests = 0;
        using var fixture = new Fixture(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.Redirect) {Headers = {Location = new Uri(location)}};
        });
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Client.DownloadTorrent(Url, PathToTorrent));
        Assert.AreEqual(1, requests, "Reject the destination before making its potentially charged request.");
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
    }

    private static HttpResponseMessage Response(byte[] bytes, string? mediaType = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(bytes)};
        if (mediaType != null) response.Content.Headers.ContentType = new(mediaType);
        return response;
    }

    private string LongTorrentPath(int componentLength, bool unicode)
    {
        const string extension = ".torrent";
        var available = componentLength - extension.Length;
        var name = unicode ? new string('图', available / 3) + new string('x', available % 3) : new string('x', available);
        var filename = name + extension;
        Assert.AreEqual(componentLength, unicode ? Encoding.UTF8.GetByteCount(filename) : filename.Length);
        return Path.Combine(_directory, filename);
    }

    private sealed class CancelAtEndContent(CancellationTokenSource cancellation) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = Metadata.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new CancelAtEndStream(cancellation));
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken ct) =>
            CreateContentReadStreamAsync();
    }

    private sealed class CancelAtEndStream(CancellationTokenSource cancellation) : MemoryStream(Metadata, writable: false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await base.ReadAsync(buffer, ct);
            // Complete EOF normally, then cancel exactly before the validated response can
            // be written/promoted. No timing-dependent file watcher or real HTTP is needed.
            if (read == 0) cancellation.Cancel();
            return read;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public ExHentaiClient Client {get;}
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _http = new HttpClient(new Handler(respond));
            Client = new ExHentaiClient(new Factory(_http), NullLoggerFactory.Instance);
        }
        public void Dispose() => _http.Dispose();
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}

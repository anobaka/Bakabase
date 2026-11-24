using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.SourceAvailability.Probes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class SharePointAttachmentTests
{
    private const string Share = "https://fixture-my.sharepoint.com/personal/owner_example/_layouts/52/download.aspx?share=test_fixture";
    private const string Redirected = "https://fixture-my.sharepoint.com/personal/owner_example/download/item?temporary=test-signed-location";
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("a complete fixture archive");
    private static readonly CloudSource Source = new() { Id = "file", ProviderId = "oneDrive", MethodId = "oneDrive.sharePoint", Url = Share };

    [TestMethod]
    [DataRow(null)]
    [DataRow("oneDrive.sharePoint")]
    public async Task Automatic_and_previously_pinned_method_accept_the_same_public_attachment(string? method)
    {
        var adapter = new SharePointCloudProvider(Path.GetTempPath(), null!, null!);
        var registry = new DownloadMethodRegistry([adapter], [], null!, null!);
        var service = new DownloadIntentService(registry, null!, new UserStoragePolicy(false), null!, null!, null!, null!, null!, null!);
        var view = await service.PreflightAsync(new() { Candidates = [new() {Id = "file", Url = Share, MethodId = method}] }, Path.GetTempPath(), default);
        Assert.IsTrue(view.Candidates.Single().CanSelect);
        Assert.AreEqual("oneDrive", CloudSourceClassifier.Classify(Share));
        Assert.IsTrue(CloudSourceClassifier.IsCompleteShare(Source));
    }

    [TestMethod]
    [DataRow("https://fixture-my.sharepoint.com/personal/owner/_layouts/15/download.aspx")]
    [DataRow("https://fixture-my.sharepoint.com/personal/owner/_layouts/15/download.aspx?share=a&share=b")]
    [DataRow("https://fixture-my.sharepoint.com/personal/owner/_layouts/15/download.aspx?share=a&file=x")]
    [DataRow("https://fixture.sharepoint.com/personal/owner/_layouts/15/download.aspx?share=x")]
    [DataRow("https://fixture-my.sharepoint.com:8443/personal/owner/_layouts/15/download.aspx?share=x")]
    public void Other_personal_and_ambiguous_file_links_remain_unsupported(string url) =>
        Assert.IsFalse(SharePointCloudProvider.SupportsPublicLink(url));

    [TestMethod]
    [DataRow("https://other-my.sharepoint.com/file", "unknown")]
    [DataRow("https://login.microsoftonline.com/common/authorize", "accessRequired")]
    [DataRow("https://fixture-my.sharepoint.com/_layouts/15/Authenticate.aspx", "accessRequired")]
    public async Task Out_of_scope_redirect_is_classified_without_contacting_its_origin(string target, string status)
    {
        var count = 0;
        using var factory = new Clients(_ => { count++; return Redirect(target); });
        var check = await new SharePointAvailabilityProbe(factory).CheckAsync(Source, default);
        Assert.AreEqual(status, check.Status);
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    [DataRow(200, "available")]
    [DataRow(206, "available")]
    [DataRow(401, "accessRequired")]
    [DataRow(403, "accessRequired")]
    [DataRow(404, "unknown")]
    [DataRow(410, "unknown")]
    [DataRow(429, "temporarilyUnavailable")]
    [DataRow(503, "temporarilyUnavailable")]
    public async Task Availability_uses_ordinary_metadata_only(int status, string expected)
    {
        var count = 0;
        using var factory = new Clients(request =>
        {
            Assert.AreEqual(HttpMethod.Head, request.Method); count++;
            return request.RequestUri!.AbsoluteUri == Share ? Redirect(Redirected) :
                Attachment(request, (HttpStatusCode)status);
        });
        var check = await new SharePointAvailabilityProbe(factory).CheckAsync(Source, default);
        Assert.AreEqual(expected, check.Status); Assert.AreEqual(2, count);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(check).Contains("test_fixture", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Only_an_explicit_withdrawal_of_the_original_link_confirms_removal()
    {
        using var factory = new Clients(request => Attachment(request, HttpStatusCode.Gone));
        var check = await new SharePointAvailabilityProbe(factory).CheckAsync(Source, default);
        Assert.AreEqual("confirmedUnavailable", check.Status);
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("size")]
    [DataRow("etag")]
    [DataRow("html")]
    [DataRow("video")]
    [DataRow("inline")]
    [DataRow("missing-version")]
    [DataRow("unsafe-name")]
    public async Task Transferring_a_different_representation_never_publishes_it(string change)
    {
        var folder = Directory.CreateTempSubdirectory("sp-attachment-");
        try
        {
            var gets = 0;
            using var factory = new Clients(request =>
            {
                if (request.RequestUri!.AbsoluteUri == Share) return Redirect(Redirected);
                var reply = Attachment(request);
                if (request.Method != HttpMethod.Get) return reply;
                gets++;
                switch (change)
                {
                    case "unsafe-name": reply.Content.Headers.ContentDisposition!.FileNameStar = "../outside.7z"; break;
                    case "name": reply.Content.Headers.ContentDisposition!.FileNameStar = "other.7z"; break;
                    case "size": reply.Content.Headers.ContentLength = Payload.Length + 1; break;
                    case "etag": reply.Headers.ETag = new("\"changed\""); break;
                    case "html": reply.Content.Headers.ContentType = new("text/html"); break;
                    case "video": reply.Content.Headers.ContentType = new("video/mp4"); break;
                    case "inline": reply.Content.Headers.ContentDisposition = new("inline"); break;
                    case "missing-version": reply.Headers.ETag = null; break;
                }
                return reply;
            });
            var (provider, request) = Provider(folder, factory);
            var failure = false;
            try { await provider.DownloadAsync(request, _ => Task.CompletedTask, default); }
            catch (Exception error) when (error is HttpRequestException or InvalidOperationException or IOException or InvalidDataException or AggregateException) { failure = true; }
            Assert.IsTrue(failure); Assert.IsTrue(gets > 0);
            Assert.IsFalse(File.Exists(Path.Combine(request.OutputDirectory, "例.7z")));
        }
        finally {folder.Delete(true);}
    }

    [TestMethod]
    public async Task Native_provider_downloads_the_named_original_once_and_reuses_only_the_verified_receipt()
    {
        var folder = Directory.CreateTempSubdirectory("sp-attachment-");
        try
        {
            var gets = 0;
            using var factory = new Clients(request =>
            {
                if (request.RequestUri!.AbsoluteUri == Share) return Redirect(Redirected);
                if (request.Method == HttpMethod.Get) gets++;
                return Attachment(request);
            });
            var (provider, request) = Provider(folder, factory);
            var download = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
            var file = download.Files.Single();
            Assert.AreEqual("例.7z", Path.GetFileName(file));
            CollectionAssert.AreEqual(Payload, await File.ReadAllBytesAsync(file));
            Assert.IsTrue(gets > 0);
            var completed = gets;
            _ = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
            Assert.AreEqual(completed, gets);
            foreach (var receipt in Directory.EnumerateFiles(folder.FullName, "*.json", SearchOption.AllDirectories))
            {
                var text = await File.ReadAllTextAsync(receipt);
                Assert.IsFalse(text.Contains("test-signed-location"));
                Assert.IsFalse(text.Contains("test_fixture"));
            }
        }
        finally {folder.Delete(true);}
    }

    private static (SharePointCloudProvider, CloudDownloadRequest) Provider(DirectoryInfo folder, Clients factory)
    {
        var downloader = new HttpDownloader(() => Path.Combine(folder.FullName, "cache"), factory, NullLoggerFactory.Instance);
        return (new(Path.Combine(folder.FullName, "app"), downloader, factory),
            new("fixture", Source, null, null, Path.Combine(folder.FullName, "output")));
    }

    private static HttpResponseMessage Redirect(string target) => new(HttpStatusCode.Found) {Headers = {Location = new(target)}};
    private static HttpResponseMessage Attachment(HttpRequestMessage request, HttpStatusCode status = HttpStatusCode.OK)
    {
        var reply = new HttpResponseMessage(status) {RequestMessage = request, Content = new ByteArrayContent(Payload)};
        reply.Headers.ETag = new("\"fixture-version\"");
        reply.Content.Headers.ContentType = new("application/octet-stream");
        reply.Content.Headers.ContentDisposition = new("attachment") {FileName = "fallback.7z", FileNameStar = "例.7z"};
        reply.Content.Headers.ContentLength = Payload.Length;
        if (status == HttpStatusCode.PartialContent) reply.Content.Headers.ContentRange = new(0, Payload.Length - 1, Payload.Length);
        return reply;
    }

    private sealed class Clients(Func<HttpRequestMessage, HttpResponseMessage> reply) : IHttpClientFactory, IDisposable
    {
        private readonly HttpMessageHandler handler = new ProxyAwarePublicHttpHandler(new WebProxy(),
            uri => PublicShareDownloadClients.IsProviderHost("oneDrive", uri), (_, _) => Task.CompletedTask, new Handler(reply),
            (_, _) => throw new AssertFailedException(), validateRoute: SharePointCloudProvider.RequireAttachmentRoute);
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual(PublicShareDownloadClients.SharePointAttachment, name);
            return new(handler, disposeHandler: false);
        }
        public void Dispose() => handler.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}

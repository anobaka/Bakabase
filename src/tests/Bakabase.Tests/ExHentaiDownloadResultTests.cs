using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MonoTorrent;

namespace Bakabase.Tests;

[TestClass]
public sealed class ExHentaiDownloadResultTests
{
    private string _root = null!;
    private BakabaseDbContext _db = null!;
    private DownloadResultService _results = null!;
    private byte[] _metadata = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabaseDownloadResults_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options);
        await _db.Database.EnsureCreatedAsync();
        _results = new DownloadResultService(_db, () => Path.Combine(_root, "appdata"));
        var payload = Path.Combine(_root, "fixture.txt");
        await File.WriteAllTextAsync(payload, "Test gallery content.");
        _metadata = (await new TorrentCreator(TorrentType.V1Only) {PieceLength = 16384}
            .CreateAsync(new TorrentFileSource(payload))).Encode();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task TorrentResult_RetainsManagedMetadata_AndDeduplicatesAcrossServiceInstances()
    {
        var original = Path.Combine(_root, "source.torrent");
        await File.WriteAllBytesAsync(original, _metadata);
        var first = await _results.RecordTorrentAsync(10, ThirdParty, "12345/abc", "Work", original, 3);
        var nextService = new DownloadResultService(_db, () => Path.Combine(_root, "appdata"));
        var second = await nextService.RecordTorrentAsync(10, ThirdParty, "12345/abc", "Work", original, 99);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(3, second.WorkflowDefinitionId, "A retry must not retarget an existing result.");
        Assert.AreEqual(_root, first.DownloadDirectory);
        File.Delete(original);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(first.Path));
        Assert.AreEqual(1, await _db.DownloadResults.CountAsync());
    }

    [TestMethod]
    public async Task TorrentResults_SharedMetadataStillBelongsToEachSourceWork()
    {
        var original = Path.Combine(_root, "source.torrent");
        await File.WriteAllBytesAsync(original, _metadata);
        var a = await _results.RecordTorrentAsync(10, ThirdParty, "12345/abc", "A", original, null);
        var b = await _results.RecordTorrentAsync(10, ThirdParty, "12346/abc", "B", original, null);
        Assert.AreNotEqual(a.Id, b.Id);
        Assert.AreEqual(a.Path, b.Path, "Identical metadata may share a managed cache file.");
    }

    [TestMethod]
    public async Task InvalidMetadata_CannotCreateAResult()
    {
        var original = Path.Combine(_root, "invalid.torrent");
        await File.WriteAllTextAsync(original, "not torrent metadata");
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            _results.RecordTorrentAsync(10, ThirdParty, "12345/abc", "Work", original, 3));
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    [TestMethod]
    public async Task LocalResults_ContainOnlyExplicitFiles_AndDetectChangedContent()
    {
        var own = Path.Combine(_root, "own.txt");
        var other = Path.Combine(_root, "other.txt");
        await File.WriteAllTextAsync(own, "A");
        await File.WriteAllTextAsync(other, "B");
        var first = await _results.RecordFilesAsync(10, ThirdParty, "12345/abc", "Work", _root, [own], 3);
        CollectionAssert.AreEqual(new[] {own}, JsonSerializer.Deserialize<string[]>(first.FilesJson));
        var same = await _results.RecordFilesAsync(10, ThirdParty, "12345/abc", "Work", _root, [own, own], 3);
        Assert.AreEqual(first.Id, same.Id);
        await File.WriteAllTextAsync(own, "C");
        var changed = await _results.RecordFilesAsync(10, ThirdParty, "12345/abc", "Work", _root, [own], 3);
        Assert.AreNotEqual(first.Id, changed.Id);
    }

    [TestMethod]
    public async Task LocalResults_RejectFilesOutsideTheirRoot()
    {
        var folder = Path.Combine(_root, "child");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(_root, "fixture.txt");
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            _results.RecordFilesAsync(10, ThirdParty, "12345/abc", "Work", folder, [file], null));
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    [DataTestMethod]
    [DataRow("https://exhentai.org/g/12345/AbCd/?p=1", "12345/abcd")]
    [DataRow("https://e-hentai.org/g/12345/abcd", "12345/abcd")]
    public void GalleryIdentity_DoesNotDependOnHostOrPage(string url, string expected) =>
        Assert.AreEqual(expected, ExHentaiDownloadResultHelper.NormalizeSourceKey(url));

    [TestMethod]
    public void AutomaticHandoffs_IsolateEachWork_EvenWithFlatOrIdenticalNames()
    {
        var first = ExHentaiDownloadResultHelper.GetWorkDirectory(_root, "https://exhentai.org/g/12345/abcd/", 7);
        var second = ExHentaiDownloadResultHelper.GetWorkDirectory(_root, "https://exhentai.org/g/12346/abcd/", 7);
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(_root, ExHentaiDownloadResultHelper.GetWorkDirectory(_root,
            "https://exhentai.org/g/12345/abcd/", null), "Existing save-only task paths must stay unchanged.");
    }

    [TestMethod]
    public void TaskDefaults_AreFrozen_AndAnExplicitDisableIsPreserved()
    {
        var raw = ExHentaiDownloaderHelper.ApplyDefaultTaskOptions(null, true, 12);
        var options = JsonSerializer.Deserialize<ExHentaiTaskOptions>(raw, JsonSerializerOptions.Web)!;
        Assert.AreEqual(12, options.DownloadResultWorkflowId);
        raw = ExHentaiDownloaderHelper.ApplyDefaultTaskOptions("{\"downloadResultWorkflowId\":0}", true, 12);
        options = JsonSerializer.Deserialize<ExHentaiTaskOptions>(raw, JsonSerializerOptions.Web)!;
        Assert.IsNull(options.DownloadResultWorkflowId);
        raw = ExHentaiDownloaderHelper.ApplyDefaultTaskOptions("{\"downloadResultWorkflowId\":null}", true, 12);
        options = JsonSerializer.Deserialize<ExHentaiTaskOptions>(raw, JsonSerializerOptions.Web)!;
        Assert.IsNull(options.DownloadResultWorkflowId, "An acquisition-owned task explicitly disables independent dispatch.");
        Assert.IsNull(JsonSerializer.Deserialize<ExHentaiTaskOptions>("{}", JsonSerializerOptions.Web)!.DownloadResultWorkflowId);
    }

    [TestMethod]
    public async Task RecordedTorrent_IsRecoveredBeforeCheckpoint_WithoutRevisitingTheGallery()
    {
        var original = Path.Combine(_root, "Existing work.torrent");
        await File.WriteAllBytesAsync(original, _metadata);
        await _results.RecordTorrentAsync(10, ThirdParty, "12345/abcd", "Existing work", original, 7);
        var handler = new GalleryHandler {RejectRequests = true};
        var producer = await BuildProducer(handler);
        var checkpoints = 0;
        await RunProducer(producer, "https://exhentai.org/g/12345/abcd/", async _ =>
        {
            Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
            checkpoints++;
        });
        Assert.AreEqual(1, checkpoints);
        Assert.AreEqual(0, handler.Requests);
        File.Delete(original);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcd/", _ => Task.CompletedTask);
        Assert.AreEqual(0, handler.Requests, "The managed result must survive removal of the user's torrent copy.");
        Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task SameNamedTorrentWithoutAResult_IsFetchedFromTheActualGalleryBeforeRecording()
    {
        var stale = Path.Combine(_root, "Gallery 12345.torrent");
        await File.WriteAllTextAsync(stale, "This is another work's stale torrent.");
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        var producer = await BuildProducer(handler);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcd/", async _ =>
        {
            Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
        });
        Assert.AreEqual(1, handler.TorrentRequests);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(stale));
        Assert.AreEqual("12345/abcd", (await _results.GetByTaskAsync(10)).Single().SourceKey);
    }

    [TestMethod]
    public async Task MultipleWorks_InOneTask_RecordSeparateFileSetsBeforeEachCompletion()
    {
        var handler = new GalleryHandler();
        var producer = await BuildProducer(handler);
        var unrelated = Path.Combine(_root, "unrelated.txt");
        await File.WriteAllTextAsync(unrelated, "Another task's file");
        for (var work = 0; work < 2; work++)
        {
            var expected = work + 1;
            await RunProducer(producer, $"https://exhentai.org/g/{12345 + work}/abcd/", async _ =>
            {
                Assert.AreEqual(expected, (await _results.GetByTaskAsync(10)).Count);
            }, preferTorrent: false);
        }
        var results = await _results.GetByTaskAsync(10);
        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(x => x.Kind == DownloadResultKind.LocalFiles));
        var a = JsonSerializer.Deserialize<string[]>(results[0].FilesJson)!;
        var b = JsonSerializer.Deserialize<string[]>(results[1].FilesJson)!;
        Assert.AreEqual(1, a.Length);
        Assert.AreEqual(1, b.Length);
        Assert.AreNotEqual(a[0], b[0]);
        Assert.IsFalse(a.Contains(unrelated) || b.Contains(unrelated));
    }

    private const string TrailingDotsGallery = "[fantia] RENA_bootleg 2025_11 女の子の部屋に連れ込まれて...";
    private const string SanitizedGalleryFile = "[Misc] [fantia] RENA_bootleg 2025_11 女の子の部屋に連れ込まれて/Page 1_ _1.webp";

    [DataTestMethod]
    [DataRow(TrailingDotsGallery, "Page 1_ _1.webp", null, SanitizedGalleryFile)]
    [DataRow("Gallery", "page.01.webp", "Downloads... /{RawName}... /{PageTitle}{Extension}",
        "Downloads/Gallery/page.01.webp")]
    [DataRow("CON", "NUL.webp", "{RawName}/{PageTitle}{Extension}", "_CON/_NUL.webp")]
    [DataRow("...", ".hidden.webp", "{RawName}/{PageTitle}{Extension}", "_/.hidden.webp")]
    [DataRow("Gallery:2025?", "001.jpg", "{RawName}/{PageTitle}{Extension}", "Gallery_2025_/001.jpg")]
    [DataRow("Gallery/part\\extra", "001.jpg", "{RawName}/{PageTitle}{Extension}", "Gallery_part_extra/001.jpg")]
    [DataRow("Gallery.v1...", "Page 1..webp", "{RawName} [edition]/{PageTitle}{Extension}",
        "Gallery.v1... [edition]/Page 1..webp")]
    public async Task ImageDownloads_SanitizeFinalPathComponentsBeforeWritingAndRecording(
        string galleryName, string pageTitle, string? namingConvention, string expectedRelativePath)
    {
        var handler = new GalleryHandler {GalleryName = galleryName, PageTitle = pageTitle};
        var producer = await BuildProducer(handler, namingConvention);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcd/", _ => Task.CompletedTask,
            preferTorrent: false);

        var expected = Path.Combine(_root, expectedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var result = (await _results.GetByTaskAsync(10)).Single();
        CollectionAssert.AreEqual(new[] {expected}, JsonSerializer.Deserialize<string[]>(result.FilesJson));
        Assert.AreEqual("fixture image /image/12345", await File.ReadAllTextAsync(expected));
        Assert.AreEqual(1, handler.ImageRequests);
    }

    [TestMethod]
    public async Task ImageDownloads_ReuseTheSanitizedPathWithoutRequestingAnExistingImage()
    {
        var expected = Path.Combine(_root, SanitizedGalleryFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        await File.WriteAllTextAsync(expected, "Previously downloaded image.");
        var handler = new GalleryHandler {GalleryName = TrailingDotsGallery, PageTitle = "Page 1_ _1.webp"};
        var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcd/", _ => Task.CompletedTask,
            preferTorrent: false);

        Assert.AreEqual(0, handler.ImageRequests);
        Assert.AreEqual(0, handler.ImagePageRequests);
        Assert.AreEqual("Previously downloaded image.", await File.ReadAllTextAsync(expected));
        var result = (await _results.GetByTaskAsync(10)).Single();
        CollectionAssert.AreEqual(new[] {expected}, JsonSerializer.Deserialize<string[]>(result.FilesJson));
    }

    private async Task<ExHentaiSingleWorkDownloader> BuildProducer(GalleryHandler handler,
        string? namingConvention = null)
    {
        var provider = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton(_results));
        provider.GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value.NamingConvention = namingConvention;
        var client = new ExHentaiClient(new Factory(new HttpClient(handler)), NullLoggerFactory.Instance);
        return new ExHentaiSingleWorkDownloader(provider,
            provider.GetRequiredService<IStringLocalizer<SharedResource>>(), client,
            provider.GetRequiredService<ITextVocabularyService>(),
            provider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());
    }

    private Task RunProducer(ExHentaiSingleWorkDownloader producer, string url,
        Func<string, Task> checkpoint, bool preferTorrent = true)
    {
        var method = typeof(AbstractExHentaiDownloader).GetMethod("DownloadSingleWork", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task) method.Invoke(producer, [10, url, null, _root,
            (Func<string, Task>)(_ => Task.CompletedTask), (Func<string, Task>)(_ => Task.CompletedTask),
            (Func<decimal, Task>)(_ => Task.CompletedTask), checkpoint, CancellationToken.None,
            preferTorrent, false, null, null, null, 7])!;
    }

    private const Bakabase.InsideWorld.Models.Constants.ThirdPartyId ThirdParty =
        Bakabase.InsideWorld.Models.Constants.ThirdPartyId.ExHentai;

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class GalleryHandler : HttpMessageHandler
    {
        public bool RejectRequests;
        public byte[]? TorrentBytes;
        public int TorrentRequests;
        public int Requests;
        public string? GalleryName;
        public string PageTitle = "001.jpg";
        public int ImageRequests;
        public int ImagePageRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (RejectRequests) throw new InvalidOperationException("This completed work must not revisit the gallery.");
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/metadata.torrent")
            {
                TorrentRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {Content = new ByteArrayContent(TorrentBytes!)});
            }
            if (uri.AbsolutePath == "/torrents")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new StringContent("<form><table><tr><td>Size: 1 KiB</td><td>Downloads: 1</td><td>Posted: 2026-09-14</td></tr><tr><td></td></tr><tr><td><a href='https://exhentai.org/metadata.torrent'>Download</a></td></tr></table></form>")});
            if (uri.AbsolutePath.StartsWith("/image/"))
            {
                ImageRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new ByteArrayContent(Encoding.UTF8.GetBytes("fixture image " + uri.AbsolutePath))});
            }
            if (uri.AbsolutePath.StartsWith("/s/"))
            {
                ImagePageRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new StringContent($"<img id='img' src='https://exhentai.org/image/{uri.Segments.Last()}' />")});
            }
            var id = uri.Segments[2].Trim('/');
            var galleryName = WebUtility.HtmlEncode(GalleryName ?? $"Gallery {id}");
            var torrentLink = TorrentBytes == null ? "" : "<div id='gd5'><a onclick=\"popUp('https://exhentai.org/torrents')\">Torrent (1)</a></div>";
            var html = $"""
                {torrentLink}
                <div id='gn'>{galleryName}</div><div id='gj'>{galleryName}</div>
                <div id='gdc'><div class='cs ct1'>Doujinshi</div></div>
                <div id='gdd'><table><tr><td>Length:</td><td>1 pages</td></tr></table></div>
                <div id='gdt'><a href='https://exhentai.org/s/{id}'><div title='{WebUtility.HtmlEncode(PageTitle)}'></div></a></div>
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(html)});
        }
    }
}

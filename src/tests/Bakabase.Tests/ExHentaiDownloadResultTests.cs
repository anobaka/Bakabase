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
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;
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
public sealed partial class ExHentaiDownloadResultTests
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
    [DataRow("https://exhentai.org/g/12345/AbCdEf0123/?p=1", "12345/abcdef0123")]
    [DataRow("https://e-hentai.org/g/12345/abcdef0123", "12345/abcdef0123")]
    public void GalleryIdentity_DoesNotDependOnHostOrPage(string url, string expected) =>
        Assert.AreEqual(expected, ExHentaiDownloadResultHelper.NormalizeSourceKey(url));

    [TestMethod]
    public void AutomaticHandoffs_IsolateEachWork_EvenWithFlatOrIdenticalNames()
    {
        var first = ExHentaiDownloadResultHelper.GetWorkDirectory(_root, "https://exhentai.org/g/12345/abcdef0123/", 7);
        var second = ExHentaiDownloadResultHelper.GetWorkDirectory(_root, "https://exhentai.org/g/12346/abcdef0123/", 7);
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(_root, ExHentaiDownloadResultHelper.GetWorkDirectory(_root,
            "https://exhentai.org/g/12345/abcdef0123/", null), "Existing save-only task paths must stay unchanged.");
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
        await _results.RecordTorrentAsync(10, ThirdParty, "12345/abcdef0123", "Existing work", original, 7);
        var handler = new GalleryHandler {RejectRequests = true};
        var producer = await BuildProducer(handler);
        var accountedFiles = new System.Collections.Generic.List<(string Path, long Size)>();
        producer.OnFileDownloaded += (path, size) =>
        {
            accountedFiles.Add((path, size));
            return Task.CompletedTask;
        };
        var checkpoints = 0;
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", async _ =>
        {
            Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
            checkpoints++;
        });
        Assert.AreEqual(1, checkpoints);
        Assert.AreEqual(0, handler.Requests);
        CollectionAssert.AreEqual(new[] {(Path.GetFullPath(original), (long) _metadata.Length)},
            accountedFiles);
        File.Delete(original);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask);
        Assert.AreEqual(0, handler.Requests, "The managed result must survive removal of the user's torrent copy.");
        Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(1, accountedFiles.Count, "The managed metadata copy is not a user download.");
    }

    [TestMethod]
    public async Task SameNamedTorrentWithoutAResult_PreservesOldIdlessFileAndFetchesActualGallery()
    {
        var stale = Path.Combine(_root, "Gallery 12345.torrent");
        await File.WriteAllTextAsync(stale, "This is another work's stale torrent.");
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        var producer = await BuildProducer(handler);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", async _ =>
        {
            Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
        });
        Assert.AreEqual(1, handler.TorrentRequests);
        Assert.AreEqual(1, handler.ApiRequests);
        Assert.AreEqual(0, handler.GalleryRequests, "Torrent downloads must not fetch gallery thumbnail pages.");
        Assert.AreEqual("This is another work's stale torrent.", await File.ReadAllTextAsync(stale));
        var managedMetadata = Path.Combine(_root, "appdata", "downloader", "torrent-metadata")
            + Path.DirectorySeparatorChar;
        var downloaded = Directory.GetFiles(_root, "*.torrent", SearchOption.AllDirectories)
            .Single(path => !string.Equals(path, stale, StringComparison.Ordinal)
                            && !path.StartsWith(managedMetadata, StringComparison.Ordinal));
        Assert.AreEqual(Path.Combine(_root, "[Misc] Gallery 12345 [g12345]", "Gallery 12345.torrent"), downloaded);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(downloaded));
        Assert.AreEqual("12345/abcdef0123", (await _results.GetByTaskAsync(10)).Single().SourceKey);
    }

    [TestMethod]
    public async Task SameNamedTorrentGalleries_SaveSeparateUserCopies()
    {
        var handler = new GalleryHandler {GalleryName = "Same Gallery", TorrentBytes = _metadata};
        var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask);
        await RunProducer(producer, "https://exhentai.org/g/12346/abcdef0123/", _ => Task.CompletedTask);

        var copies = Directory.GetFiles(_root, "Same Gallery.torrent", SearchOption.AllDirectories);
        Assert.AreEqual(2, copies.Length);
        CollectionAssert.AreEquivalent(new[]
        {
            Path.Combine(_root, "[Misc] Same Gallery [g12345]", "Same Gallery.torrent"),
            Path.Combine(_root, "[Misc] Same Gallery [g12346]", "Same Gallery.torrent")
        }, copies);
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(copies[0]));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(copies[1]));
        Assert.AreEqual(2, handler.TorrentRequests);
        var sourceKeys = (await _results.GetByTaskAsync(10)).Select(result => result.SourceKey).ToArray();
        CollectionAssert.AreEquivalent(new[] {"12345/abcdef0123", "12346/abcdef0123"}, sourceKeys);
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
            await RunProducer(producer, $"https://exhentai.org/g/{12345 + work}/abcdef0123/", async _ =>
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

    [TestMethod]
    public async Task SameNamedGalleries_DoNotReuseAnotherGallerysImage()
    {
        var handler = new GalleryHandler {GalleryName = "Same Gallery"};
        var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);
        await RunProducer(producer, "https://exhentai.org/g/12346/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        var results = (await _results.GetByTaskAsync(10)).ToDictionary(x => x.SourceKey);
        var first = JsonSerializer.Deserialize<string[]>(results["12345/abcdef0123"].FilesJson)!.Single();
        var second = JsonSerializer.Deserialize<string[]>(results["12346/abcdef0123"].FilesJson)!.Single();
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(Path.Combine(_root, "[Misc] Same Gallery [g12345]", "001.jpg"), first);
        Assert.AreEqual(Path.Combine(_root, "[Misc] Same Gallery [g12346]", "001.jpg"), second);
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(first));
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(second));
        Assert.AreEqual(2, handler.ImageRequests);
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(first)!, ".bakabase-exhentai-gallery.json")));
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(second)!, ".bakabase-exhentai-gallery.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, ".bakabase-exhentai-gallery-index")));
    }

    [TestMethod]
    public async Task NewDownloadRoot_IsCreatedBeforeWritingGalleryFiles()
    {
        var downloadRoot = Path.Combine(_root, "new-download-root");
        var handler = new GalleryHandler {GalleryName = "Gallery"};
        var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false, downloadPath: downloadRoot, resultWorkflowId: null);

        var result = (await _results.GetByTaskAsync(10)).Single();
        var file = JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single();
        Assert.IsTrue(File.Exists(file));
        Assert.AreEqual(Path.Combine(downloadRoot, "[Misc] Gallery [g12345]", "001.jpg"), file);
    }

    [TestMethod]
    public async Task CustomNamingConventionWithoutId_CanReuseTheSameOutputPath()
    {
        var handler = new GalleryHandler {GalleryName = "Same Gallery"};
        var producer = await BuildProducer(handler, "Images/{PageTitle}{Extension}");

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);
        await RunProducer(producer, "https://exhentai.org/g/12346/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        var results = (await _results.GetByTaskAsync(10)).ToDictionary(x => x.SourceKey);
        var first = JsonSerializer.Deserialize<string[]>(results["12345/abcdef0123"].FilesJson)!.Single();
        var second = JsonSerializer.Deserialize<string[]>(results["12346/abcdef0123"].FilesJson)!.Single();
        Assert.AreEqual(Path.Combine(_root, "Images", "001.jpg"), first);
        Assert.AreEqual(first, second, "An explicit template without GalleryId does not separate galleries.");
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(first));
        Assert.AreEqual(1, handler.ImageRequests, "The second gallery encounters the custom template's existing file.");
    }

    [TestMethod]
    public async Task PageOnlyNamingConvention_UsesTheLegacyWrapperWithoutId()
    {
        var handler = new GalleryHandler {GalleryName = "Same Gallery"};
        var producer = await BuildProducer(handler, "{PageTitle}{Extension}");

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);
        await RunProducer(producer, "https://exhentai.org/g/12346/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        var results = (await _results.GetByTaskAsync(10)).ToDictionary(x => x.SourceKey);
        var first = JsonSerializer.Deserialize<string[]>(results["12345/abcdef0123"].FilesJson)!.Single();
        var second = JsonSerializer.Deserialize<string[]>(results["12346/abcdef0123"].FilesJson)!.Single();
        Assert.AreEqual(Path.Combine(_root, "[Misc] Same Gallery", "001.jpg"), first);
        Assert.AreEqual(first, second);
        Assert.AreEqual(1, handler.ImageRequests);
    }

    [TestMethod]
    public async Task GalleryIdAndTokenFields_AreAvailableToNamingConvention()
    {
        var handler = new GalleryHandler {GalleryName = "Gallery"};
        var producer = await BuildProducer(handler, "{RawName} [{GalleryId}]/{GalleryToken}_{PageTitle}{Extension}");

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        var result = (await _results.GetByTaskAsync(10)).Single();
        var file = JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single();
        Assert.AreEqual(Path.Combine(_root, "Gallery [12345]", "abcdef0123_001.jpg"), file);
    }

    private const string TrailingDotsGallery = "[fantia] RENA_bootleg 2025_11 女の子の部屋に連れ込まれて...";
    private const string LegacySanitizedGalleryFile = "[Misc] [fantia] RENA_bootleg 2025_11 女の子の部屋に連れ込まれて/Page 1_ _1.webp";
    private const string IdSanitizedGalleryFile = "[Misc] [fantia] RENA_bootleg 2025_11 女の子の部屋に連れ込まれて... [g12345]/Page 1_ _1.webp";

    [DataTestMethod]
    [DataRow(TrailingDotsGallery, "Page 1_ _1.webp", null, IdSanitizedGalleryFile)]
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

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        var expected = Path.Combine(_root, expectedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var result = (await _results.GetByTaskAsync(10)).Single();
        CollectionAssert.AreEqual(new[] {expected}, JsonSerializer.Deserialize<string[]>(result.FilesJson));
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(expected));
        Assert.AreEqual(1, handler.ImageRequests);
    }

    [TestMethod]
    public async Task CustomTemplateParentSegments_CannotEscapeTheDownloadRoot()
    {
        var handler = new GalleryHandler {GalleryName = "Gallery"};
        var producer = await BuildProducer(handler, "../../outside/{PageTitle}{Extension}");

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        var expected = Path.Combine(_root, "_", "_", "outside", "001.jpg");
        var result = (await _results.GetByTaskAsync(10)).Single();
        CollectionAssert.AreEqual(new[] {expected}, JsonSerializer.Deserialize<string[]>(result.FilesJson));
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(expected));
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "outside", "001.jpg")));
    }

    [TestMethod]
    public async Task CustomTemplateLinkedDirectory_CannotRedirectDownloadedFiles()
    {
        if (OperatingSystem.IsWindows()) return;

        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(Path.Combine(_root, "linked"), target);
        var handler = new GalleryHandler {GalleryName = "Gallery"};
        var producer = await BuildProducer(handler, "linked/{PageTitle}{Extension}");

        await Assert.ThrowsExceptionAsync<IOException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));

        Assert.IsFalse(File.Exists(Path.Combine(target, "001.jpg")));
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    [TestMethod]
    public async Task DefaultNaming_DoesNotReuseOldIdlessDirectory()
    {
        var legacy = Path.Combine(_root, LegacySanitizedGalleryFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        await File.WriteAllTextAsync(legacy, "Unknown gallery's image.");
        var handler = new GalleryHandler {GalleryName = TrailingDotsGallery, PageTitle = "Page 1_ _1.webp"};
        var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        Assert.AreEqual(1, handler.ImageRequests);
        Assert.AreEqual("Unknown gallery's image.", await File.ReadAllTextAsync(legacy));
        var result = (await _results.GetByTaskAsync(10)).Single();
        var downloaded = JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single();
        Assert.AreEqual(Path.Combine(_root, IdSanitizedGalleryFile.Replace('/', Path.DirectorySeparatorChar)), downloaded);
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(downloaded));
    }

    [TestMethod]
    public async Task ImageDownloads_ReuseExistingIdNamedPathWithoutRequestingExistingImage()
    {
        var expected = Path.Combine(_root, IdSanitizedGalleryFile.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(expected)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(expected, "Previously downloaded image.");
        var handler = new GalleryHandler {GalleryName = TrailingDotsGallery, PageTitle = "Page 1_ _1.webp"};
        var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        Assert.AreEqual(0, handler.ImageRequests);
        Assert.AreEqual(0, handler.ImagePageRequests);
        Assert.AreEqual("Previously downloaded image.", await File.ReadAllTextAsync(expected));
        var result = (await _results.GetByTaskAsync(10)).Single();
        CollectionAssert.AreEqual(new[] {expected}, JsonSerializer.Deserialize<string[]>(result.FilesJson));
    }

    [TestMethod]
    public async Task NoTorrentProbe_DefersUsingApi_WithoutFetchingThumbnailPages()
    {
        var handler = new GalleryHandler();
        var producer = await BuildProducer(handler);
        var noTorrentDetected = 0;

        await Assert.ThrowsExceptionAsync<DownloadDeferredException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            deferIfNoTorrent: true, onNoTorrentDetected: () =>
            {
                noTorrentDetected++;
                return Task.CompletedTask;
            }));

        Assert.AreEqual(1, handler.ApiRequests);
        Assert.AreEqual(0, handler.GalleryRequests);
        Assert.AreEqual(0, handler.TorrentListRequests);
        Assert.AreEqual(1, noTorrentDetected);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task MissingTorrentLinks_DoesNotCacheANegativeVerdictOrDownloadImages()
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata, EmptyTorrentList = true};
        var producer = await BuildProducer(handler);
        var noTorrentDetected = 0;

        await Assert.ThrowsExceptionAsync<Exception>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            onNoTorrentDetected: () =>
            {
                noTorrentDetected++;
                return Task.CompletedTask;
            }));

        Assert.AreEqual(0, noTorrentDetected);
        Assert.AreEqual(0, handler.GalleryRequests);
        Assert.AreEqual(0, handler.TorrentRequests);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task ImageDownload_ReadsThumbnailPagination_AndRecordsEveryImage()
    {
        var handler = new GalleryHandler {GalleryPageCount = 2, ImagesPerPage = 2};
        var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        var result = (await _results.GetByTaskAsync(10)).Single();
        var files = JsonSerializer.Deserialize<string[]>(result.FilesJson)!;
        Assert.AreEqual(DownloadResultKind.LocalFiles, result.Kind);
        Assert.AreEqual(4, files.Length);
        Assert.IsTrue(files.All(File.Exists));
        Assert.AreEqual(1, handler.ApiRequests);
        Assert.AreEqual(4, handler.ImageRequests);
        Assert.IsTrue(handler.GalleryRequests <= 3, "Image count from the API must not become thumbnail page count.");
        Assert.AreEqual(0, handler.TorrentListRequests);
    }

    private async Task<ExHentaiSingleWorkDownloader> BuildProducer(GalleryHandler handler,
        string? namingConvention = null, Action<ExHentaiOptions>? configure = null)
    {
        var provider = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton(_results));
        provider.GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value.NamingConvention = namingConvention;
        configure?.Invoke(provider.GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value);
        var client = new ExHentaiClient(new Factory(new HttpClient(handler)), NullLoggerFactory.Instance);
        return new ExHentaiSingleWorkDownloader(provider,
            provider.GetRequiredService<IStringLocalizer<SharedResource>>(), client,
            provider.GetRequiredService<ITextVocabularyService>(),
            provider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());
    }

    [TestMethod]
    public async Task DirectImages_AfterATorrentResultDownloadsImagesAndRetainsTheTorrent()
    {
        var original = Path.Combine(_root, "Existing work.torrent");
        await File.WriteAllBytesAsync(original, _metadata);
        var oldResult = await _results.RecordTorrentAsync(10, ThirdParty, "12345/abcdef0123",
            "Existing work", original, 7);
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: false);

        var results = await _results.GetByTaskAsync(10);
        Assert.AreEqual(2, results.Count);
        var images = results.Single(x => x.Kind == DownloadResultKind.LocalFiles);
        Assert.IsTrue(JsonSerializer.Deserialize<string[]>(images.FilesJson)!.All(File.Exists));
        Assert.AreEqual(1, handler.ImageRequests);
        Assert.AreEqual(0, handler.TorrentListRequests);
        Assert.AreEqual(0, handler.TorrentRequests);
        Assert.IsTrue(results.Any(x => x.Id == oldResult.Id && x.Kind == DownloadResultKind.TorrentMetadata));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(original));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(oldResult.Path));
    }

    private Task RunProducer(ExHentaiSingleWorkDownloader producer, string url,
        Func<string, Task> checkpoint, bool preferTorrent = true, string? downloadPath = null,
        int? resultWorkflowId = 7, bool deferIfNoTorrent = false,
        Func<Task>? onNoTorrentDetected = null)
    {
        var method = typeof(AbstractExHentaiDownloader).GetMethod("DownloadSingleWork", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task) method.Invoke(producer, [10, url, null, downloadPath ?? _root,
            (Func<string, Task>)(_ => Task.CompletedTask), (Func<string, Task>)(_ => Task.CompletedTask),
            (Func<decimal, Task>)(_ => Task.CompletedTask), checkpoint, CancellationToken.None,
            preferTorrent, deferIfNoTorrent, onNoTorrentDetected, null, null, resultWorkflowId])!;
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
        public bool EmptyTorrentList;
        public byte[]? TorrentBytes;
        public int TorrentRequests;
        public int Requests;
        public int ApiRequests;
        public int GalleryRequests;
        public int TorrentListRequests;
        public string? GalleryName;
        public string PageTitle = "001.jpg";
        public int ImagePageRequests;
        public int ImageRequests;
        public int GalleryPageCount = 1;
        public int ImagesPerPage = 1;
        public string? OriginalLinkSize;
        public int OriginalRequests;
        public bool OriginalReturnsHtml;
        public int BalanceRequests;
        public string BalanceHtml = "<div class='stuffbox'>Available: 50,000 Credits Available: 200 kGP</div>";
        public long Posted = 1770000000;
        public Action? BeforeOriginalSending;
        public bool IncludeServerDate = true;
        public byte[]? ImageBytes;
        public byte[] GetImageBytes()
        {
            if (ImageBytes != null) return ImageBytes;
            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1);
            using var stream = new MemoryStream();
            image.Save(stream, PageTitle.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                ? new SixLabors.ImageSharp.Formats.Webp.WebpEncoder()
                : (SixLabors.ImageSharp.Formats.IImageEncoder)new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
            return stream.ToArray();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await HandleAsync(request, ct);
            response.Headers.Date = IncludeServerDate ? DateTimeOffset.UtcNow : null;
            return response;
        }

        private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/fullimg.php") BeforeOriginalSending?.Invoke();
            if (request.Options.TryGetValue(Bakabase.Modules.ThirdParty.Abstractions.Http.ThirdPartyRequestOptions.BeforeSend,
                    out var beforeSend)) await beforeSend(ct);
            Requests++;
            if (RejectRequests) throw new InvalidOperationException("This completed work must not revisit the gallery.");
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/api.php")
            {
                ApiRequests++;
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var galleries = json.RootElement.GetProperty("gidlist").EnumerateArray().Select(entry =>
                {
                    var id = entry[0].GetInt64();
                    return new
                    {
                        gid = id, token = entry[1].GetString(), title = GalleryName ?? $"Gallery {id}",
                        title_jpn = GalleryName ?? $"Gallery {id}", category = "Misc", thumb = "https://exhentai.org/cover.jpg",
                        posted = Posted.ToString(), filecount = (GalleryPageCount * ImagesPerPage).ToString(), filesize = 1024,
                        rating = "4.5", torrentcount = TorrentBytes == null ? "0" : "1", tags = new[] {"artist:test"},
                        torrents = TorrentBytes == null ? Array.Empty<object>() : new object[]
                        {
                            new {hash = "1234567890123456789012345678901234567890", added = "1770000000",
                                name = $"Gallery {id}.zip", tsize = "100", fsize = "1024"}
                        }
                    };
                }).ToArray();
                return new HttpResponseMessage(HttpStatusCode.OK)
                    {Content = new StringContent(JsonSerializer.Serialize(new {gmetadata = galleries}), Encoding.UTF8, "application/json")};
            }
            if (uri.AbsolutePath == "/metadata.torrent")
            {
                TorrentRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                    {Content = new ByteArrayContent(TorrentBytes!)};
            }
            if (uri.AbsolutePath == "/torrents" || uri.AbsolutePath == "/gallerytorrents.php")
            {
                TorrentListRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new StringContent(EmptyTorrentList ? "" : "<form><table><tr><td>Size: 1 KiB</td><td>Downloads: 1</td><td>Posted: 2026-09-14</td></tr><tr><td></td></tr><tr><td><a href='https://exhentai.org/metadata.torrent'>Download</a></td></tr></table></form>")};
            }
            if (uri.AbsolutePath.StartsWith("/image/"))
            {
                ImageRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new ByteArrayContent(GetImageBytes())};
            }
            if (uri.AbsolutePath == "/exchange.php")
            {
                BalanceRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(BalanceHtml)};
            }
            if (uri.AbsolutePath == "/fullimg.php")
            {
                OriginalRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = OriginalReturnsHtml ? new StringContent("<html>Original download is unavailable.</html>") :
                        new ByteArrayContent(GetImageBytes())
                };
            }
            if (uri.AbsolutePath.StartsWith("/s/"))
            {
                ImagePageRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new StringContent($"<img id='img' src='https://exhentai.org/image/{uri.Segments.Last()}' />" +
                    (OriginalLinkSize == null ? "" : $"<div id='i7'><a href='https://exhentai.org/fullimg.php?gid=12345&amp;page=1&amp;key=abcdef'>Download original 1000 x 1000 {OriginalLinkSize}</a></div>"))};
            }
            GalleryRequests++;
            var id = uri.Segments[2].Trim('/');
            var torrentLink = TorrentBytes == null ? "" : "<div id='gd5'><a onclick=\"popUp('https://exhentai.org/torrents')\">Torrent (1)</a></div>";
            var page = uri.Query.Contains("p=1") ? 2 : 1;
            var pagination = GalleryPageCount == 1 ? "" :
                $"<table class='ptt'><tr><td>Previous</td><td><a href='/g/{id}/abcdef0123/?p=0'>1</a></td><td><a href='/g/{id}/abcdef0123/?p=1'>2</a></td><td>Next</td></tr></table>";
            var images = string.Concat(Enumerable.Range(1, ImagesPerPage).Select(index =>
            {
                var image = (page - 1) * ImagesPerPage + index;
                var imageKey = GalleryPageCount * ImagesPerPage == 1 ? id : $"{id}-{image}";
                var title = GalleryPageCount * ImagesPerPage == 1 ? PageTitle : $"{image:D3}.jpg";
                return $"<a href='https://exhentai.org/s/{imageKey}'><div title='{WebUtility.HtmlEncode(title)}'></div></a>";
            }));
            var galleryName = WebUtility.HtmlEncode(GalleryName ?? $"Gallery {id}");
            var html = $"""
                {torrentLink}
                {pagination}
                <div id='gn'>{galleryName}</div><div id='gj'>{galleryName}</div>
                <div id='gdc'><div class='cs ct1'>Doujinshi</div></div>
                <div id='gdd'><table><tr><td>Length:</td><td>{GalleryPageCount * ImagesPerPage} pages</td></tr></table></div>
                <div id='gdt'>{images}</div>
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(html)};
        }
    }
}

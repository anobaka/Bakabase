using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadTaskOutputLocatorTests
{
    private string _root = null!;
    private string _downloads = null!;
    private BakabaseDbContext _db = null!;
    private ExHentaiDownloadLedger _ledger = null!;
    private DownloadTaskOutputLocator _locator = null!;
    private DownloadTaskDbModel _task = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabaseOutputLocator_" + Guid.NewGuid().ToString("N"));
        _downloads = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(_downloads);
        _db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "test.db")).Options);
        await _db.Database.EnsureCreatedAsync();
        _task = new DownloadTaskDbModel
        {
            Id = 17, Key = "https://exhentai.org/g/12345/abc/", ThirdPartyId = ThirdPartyId.ExHentai,
            DownloadPath = _downloads, Options = "{\"preferTorrent\":true}"
        };
        _db.DownloadTasks.Add(_task);
        await _db.SaveChangesAsync();
        _ledger = new ExHentaiDownloadLedger(() => Path.Combine(_root, "appdata"));
        _locator = new DownloadTaskOutputLocator(_db, _ledger);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<string> WriteFile(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "Task-owned test output.");
        return path;
    }

    private async Task<DownloadResultDbModel> Record(DownloadResultKind kind, string sourceKey,
        string directory, string path, params string[] files)
    {
        var result = new DownloadResultDbModel
        {
            DownloadTaskId = _task.Id, ThirdPartyId = ThirdPartyId.ExHentai, SourceKey = sourceKey,
            Name = "Gallery", Kind = kind, Path = path, DownloadDirectory = directory,
            FilesJson = JsonSerializer.Serialize(files), Fingerprint = Guid.NewGuid().ToString("N"),
            DeduplicationKey = Guid.NewGuid().ToString("N")
        };
        _db.DownloadResults.Add(result);
        await _db.SaveChangesAsync();
        return result;
    }

    [TestMethod]
    public async Task Images_OpenDeepestCommonTemplateDirectory_InsteadOfConfiguredRoot()
    {
        var first = await WriteFile("downloads/Category/Gallery/Images/001.webp");
        var second = await WriteFile("downloads/Category/Gallery/Images/002.jpg");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, first, second);
        _task.DownloadPath = Path.Combine(_root, "different-current-config");
        await _db.SaveChangesAsync();

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.GetDirectoryName(first)!, false),
            await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task MultipleGalleries_OpenTheirCommonAncestor_OnDirectoryBoundaries()
    {
        var first = await WriteFile("downloads/Category/Gallery/001.png");
        var second = await WriteFile("downloads/Category/Gallery-extra/Nested/002.png");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, first);
        await Record(DownloadResultKind.LocalFiles, "12346/def", _downloads, _downloads, second);

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.Combine(_downloads, "Category"), false),
            await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task LatestTemplateDirectory_ExcludesSupersededTaskFilesAndOldLedgerPageUrls()
    {
        var oldImage = await WriteFile("downloads/OldGallery/Images/001.webp");
        var newImage = await WriteFile("downloads/NewGallery/Images/001.webp");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, oldImage);
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, newImage);
        _db.DownloadTaskFiles.AddRange(
            new DownloadTaskFileDbModel { DownloadTaskId = _task.Id, Path = oldImage, Size = new FileInfo(oldImage).Length },
            new DownloadTaskFileDbModel { DownloadTaskId = _task.Id, Path = newImage, Size = new FileInfo(newImage).Length });
        await _db.SaveChangesAsync();
        await _ledger.RecordImageAsync(_task.Id, "12345/abc", "https://exhentai.org/s/aaaa/12345-1",
            oldImage, false, default);
        await _ledger.RecordImageAsync(_task.Id, "12345/abc", "https://exhentai.org/s/bbbb/12345-1",
            newImage, true, default);

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.GetDirectoryName(newImage)!, false),
            await _locator.GetAsync(_task.Id));
        Assert.IsTrue(File.Exists(oldImage), "Opening the latest output must retain the historical download.");
    }

    [TestMethod]
    public async Task LatestResult_KeepsUnrecordedPartialImagesWhenExcludingHistoricalPaths()
    {
        var oldImage = await WriteFile("downloads/OldGallery/001.webp");
        var newImage = await WriteFile("downloads/NewGallery/Images/001.webp");
        var partialImage = await WriteFile("downloads/NewGallery/OtherImages/002.webp");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, oldImage);
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, newImage);
        _db.DownloadTaskFiles.AddRange(
            new DownloadTaskFileDbModel { DownloadTaskId = _task.Id, Path = oldImage, Size = new FileInfo(oldImage).Length },
            new DownloadTaskFileDbModel { DownloadTaskId = _task.Id, Path = partialImage, Size = new FileInfo(partialImage).Length });
        await _db.SaveChangesAsync();
        await _ledger.RecordImageAsync(_task.Id, "12345/abc", "https://exhentai.org/s/cccc/12345-2",
            partialImage, false, default);

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.Combine(_downloads, "NewGallery"), false),
            await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task AnotherGallerysLatestResult_CanStillOwnAPathSupersededByTheFirstGallery()
    {
        var sharedImage = await WriteFile("downloads/GalleryA/001.webp");
        var newImage = await WriteFile("downloads/GalleryB/001.webp");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, sharedImage);
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, newImage);
        await Record(DownloadResultKind.LocalFiles, "12346/def", _downloads, _downloads, sharedImage);

        Assert.AreEqual(new DownloadTaskOpenTarget(_downloads, false), await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task LatestTorrent_DoesNotRetireImagesAvailableForDirectDownloadNavigation()
    {
        var image = await WriteFile("downloads/Gallery/Images/001.webp");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, image);
        var cache = await WriteFile("appdata/downloader/torrent-metadata/hash.torrent");
        var torrent = await WriteFile("downloads/Gallery.torrent");
        await Record(DownloadResultKind.TorrentMetadata, "12345/abc", _downloads, cache, cache, torrent);
        _task.Options = "{\"preferTorrent\":false}";
        await _db.SaveChangesAsync();
        await _ledger.RecordImageAsync(_task.Id, "12345/abc", "https://exhentai.org/s/aaaa/12345-1",
            image, false, default);

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.GetDirectoryName(image)!, false),
            await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task LatestTorrent_RevealsActualUserFile_WithoutSelectingManagedCacheOrOldImages()
    {
        var image = await WriteFile("downloads/OldGallery/001.webp");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, image);
        var cache = await WriteFile("appdata/downloader/torrent-metadata/hash.torrent");
        var torrent = await WriteFile("downloads/[Manga] Gallery [g12345].torrent");
        await Record(DownloadResultKind.TorrentMetadata, "12345/abc", _downloads, cache, cache, torrent);

        Assert.AreEqual(new DownloadTaskOpenTarget(torrent, true), await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task DirectDownload_PartialLedgerImagesOverrideHistoricalTorrent()
    {
        var cache = await WriteFile("appdata/downloader/torrent-metadata/hash.torrent");
        var torrent = await WriteFile("downloads/Gallery.torrent");
        await Record(DownloadResultKind.TorrentMetadata, "12345/abc", _downloads, cache, cache, torrent);
        _task.Options = "{\"preferTorrent\":false}";
        await _db.SaveChangesAsync();
        var image = await WriteFile("downloads/Category/Gallery/Images/001.webp");
        await _ledger.RecordImageAsync(_task.Id, "12345/abc", "https://exhentai.org/s/abcdef/12345-1",
            image, false, default);

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.GetDirectoryName(image)!, false),
            await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task LatestImageResult_OverridesHistoricalTorrent_EvenWhenTorrentIsPreferred()
    {
        var cache = await WriteFile("appdata/downloader/torrent-metadata/hash.torrent");
        var torrent = await WriteFile("downloads/Gallery.torrent");
        await Record(DownloadResultKind.TorrentMetadata, "12345/abc", _downloads, cache, cache, torrent);
        var image = await WriteFile("downloads/Category/Gallery/001.webp");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, image);

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.GetDirectoryName(image)!, false),
            await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task PartialTaskFiles_DoNotScanUnrelatedFilesInSharedDirectory()
    {
        var image = await WriteFile("downloads/Category/Gallery/Images/001.webp");
        await WriteFile("downloads/UnrelatedGallery/001.webp");
        _db.DownloadTaskFiles.Add(new DownloadTaskFileDbModel
            { DownloadTaskId = _task.Id, Path = image, Size = new FileInfo(image).Length });
        await _db.SaveChangesAsync();

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.GetDirectoryName(image)!, false),
            await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task MissingTorrent_OpensRecordedDownloadDirectory_WithoutOpeningManagedMetadata()
    {
        var cache = await WriteFile("appdata/downloader/torrent-metadata/hash.torrent");
        var torrent = Path.Combine(_downloads, "Gallery.torrent");
        await Record(DownloadResultKind.TorrentMetadata, "12345/abc", _downloads, cache, cache, torrent);
        _task.DownloadPath = Path.Combine(_root, "different-current-config");
        await _db.SaveChangesAsync();

        Assert.AreEqual(new DownloadTaskOpenTarget(_downloads, false), await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task LegacyCacheOnlyTorrentResult_RevealsOriginalNamedFile()
    {
        var cache = await WriteFile("appdata/downloader/torrent-metadata/hash.torrent");
        var torrent = await WriteFile("downloads/Gallery.torrent");
        await Record(DownloadResultKind.TorrentMetadata, "12345/abc", _downloads, cache, cache);

        Assert.AreEqual(new DownloadTaskOpenTarget(torrent, true), await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task ResultPathsOutsideRecordedRoot_CannotDetermineImageFolder()
    {
        var other = await WriteFile("unrelated/001.webp");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, other);

        Assert.AreEqual(new DownloadTaskOpenTarget(_downloads, false), await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task MalformedFilesJson_FallsBackToRecordedDirectory()
    {
        var result = await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads);
        result.FilesJson = "{";
        await _db.SaveChangesAsync();

        Assert.AreEqual(new DownloadTaskOpenTarget(_downloads, false), await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task DamagedLedger_DoesNotBlockResultNavigation_AndStillBlocksGpSpending()
    {
        var image = await WriteFile("downloads/Gallery/Images/001.png");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, image);
        var ledgerFile = Path.Combine(_root, "appdata", "downloader", "exhentai-state", _task.Id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerFile)!);
        await File.WriteAllTextAsync(ledgerFile, "{");

        Assert.AreEqual(new DownloadTaskOpenTarget(Path.GetDirectoryName(image)!, false),
            await _locator.GetAsync(_task.Id));
        await Assert.ThrowsExceptionAsync<JsonException>(() => _ledger.ReserveGpAsync(_task.Id, 1000, 100000, default));
    }

    [TestMethod]
    public async Task OtherSources_KeepConfiguredFolder_EvenWhenAResultHasNestedFiles()
    {
        _task.ThirdPartyId = ThirdPartyId.Bilibili;
        _task.DownloadPath = Path.Combine(_root, "unchanged-generic-output");
        await _db.SaveChangesAsync();
        var image = await WriteFile("downloads/Gallery/Images/001.png");
        await Record(DownloadResultKind.LocalFiles, "12345/abc", _downloads, _downloads, image);

        Assert.AreEqual(new DownloadTaskOpenTarget(_task.DownloadPath, false), await _locator.GetAsync(_task.Id));
    }

    [TestMethod]
    public async Task NoOutputAndNoExistingConfiguredFolder_ReturnsNoTarget()
    {
        _task.DownloadPath = Path.Combine(_root, "not-created");
        await _db.SaveChangesAsync();

        Assert.IsNull(await _locator.GetAsync(_task.Id));
        Assert.IsNull(await _locator.GetAsync(999));
    }
}

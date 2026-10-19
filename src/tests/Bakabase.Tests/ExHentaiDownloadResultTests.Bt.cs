using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    [TestMethod]
    public async Task SwitchingBtToTorrentFile_ExportsAtSelectedRootAndPreservesResultAndDownloadedPieces()
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler);
        const string url = "https://exhentai.org/g/12345/abcdef0123/";
        var galleryDirectory = Path.Combine(_root, "gallery-12345-abcdef0123");
        await RunProducer(producer, url, _ => Task.CompletedTask, downloadPath: galleryDirectory,
            resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent, fallbackToDirect: false);
        var saved = (await _results.GetByTaskAsync(10)).Single();
        var partialFile = Path.Combine(galleryDirectory, "existing-partial.bin");
        await File.WriteAllTextAsync(partialFile, "keep existing BT pieces");

        await RunProducer(producer, url, _ => Task.CompletedTask, downloadPath: _root,
            resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.TorrentFile);

        var exported = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(saved.Id, exported.Id);
        Assert.AreEqual(saved.Fingerprint, exported.Fingerprint);
        Assert.IsFalse(exported.RequiresContentsDownload);
        Assert.AreEqual(_root, exported.DownloadDirectory);
        var torrentPath = ExHentaiDownloadResultHelper.GetTorrentDownloadPath(exported)!;
        Assert.AreEqual(_root, Path.GetDirectoryName(torrentPath));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(torrentPath));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(exported.Path));
        Assert.AreEqual("keep existing BT pieces", await File.ReadAllTextAsync(partialFile));
        Assert.AreEqual(1, handler.TorrentRequests, "Conversion reuses verified managed metadata.");
        Assert.AreEqual(0, handler.ImageRequests);

        await RunProducer(producer, url, _ => Task.CompletedTask, downloadPath: galleryDirectory,
            resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent, fallbackToDirect: false);
        var resumedIntent = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(saved.Id, resumedIntent.Id);
        Assert.IsTrue(resumedIntent.RequiresContentsDownload);
        Assert.AreEqual(galleryDirectory, resumedIntent.DownloadDirectory);
        Assert.AreEqual("keep existing BT pieces", await File.ReadAllTextAsync(partialFile));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SwitchingSameNamedBtGalleriesToTorrentFiles_KeepsSeparateExports(bool exportDuringBt)
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata, GalleryName = "Identical gallery name"};
        using var producer = await BuildProducer(handler);
        foreach (var id in new[] {12345, 12346})
        {
            var url = $"https://exhentai.org/g/{id}/abcdef0123/";
            await RunProducer(producer, url, _ => Task.CompletedTask,
                downloadPath: Path.Combine(_root, $"gallery-{id}-abcdef0123"),
                resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent, saveTorrentFile: exportDuringBt);
            await RunProducer(producer, url, _ => Task.CompletedTask, downloadPath: _root,
                resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.TorrentFile);
        }

        var paths = (await _results.GetByTaskAsync(10)).Select(ExHentaiDownloadResultHelper.GetTorrentDownloadPath).ToArray();
        Assert.AreEqual(2, paths.Distinct().Count());
        Assert.AreEqual(2, Directory.GetFiles(_root, "*.torrent").Length);
        foreach (var path in paths)
            CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(path!));
        Assert.AreEqual(0, handler.ImageRequests);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TorrentFileMode_ExportsMetadataEvenWhenDirectContentsAlreadyExist(bool legacy)
    {
        const string url = "https://exhentai.org/g/12345/abcdef0123/";
        var directHandler = new GalleryHandler();
        using (var directProducer = await BuildProducer(directHandler))
            await RunProducer(directProducer, url, _ => Task.CompletedTask, preferTorrent: false,
                resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Direct);
        var direct = (await _results.GetByTaskAsync(10)).Single();
        var originalFiles = JsonSerializer.Deserialize<string[]>(direct.FilesJson)!;
        var contents = await File.ReadAllBytesAsync(originalFiles.Single());
        var torrentHandler = new GalleryHandler {TorrentBytes = _metadata};
        using var torrentProducer = await BuildProducer(torrentHandler);

        await RunProducer(torrentProducer, url, _ => Task.CompletedTask, resultWorkflowId: null,
            downloadMode: legacy ? null : ExHentaiDownloadMode.TorrentFile);

        var results = await _results.GetByTaskAsync(10);
        Assert.AreEqual(2, results.Count);
        var exported = results.Single(r => r.Kind == DownloadResultKind.TorrentMetadata);
        Assert.IsFalse(exported.RequiresContentsDownload);
        CollectionAssert.AreEqual(_metadata,
            await File.ReadAllBytesAsync(ExHentaiDownloadResultHelper.GetTorrentDownloadPath(exported)!));
        CollectionAssert.AreEqual(contents, await File.ReadAllBytesAsync(originalFiles.Single()));
        Assert.AreEqual(1, torrentHandler.TorrentRequests);
        Assert.AreEqual(0, torrentHandler.ImageRequests);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task TorrentFileMode_NeverUsesPaidFallbackIncludingLegacyOptions(bool legacy, bool probeTorrent)
    {
        var handler = new GalleryHandler();
        using var producer = await BuildProducer(handler);
        var options = JsonSerializer.Deserialize<ExHentaiTaskOptions>(legacy
            ? "{\"preferTorrent\":true,\"fallbackToDirect\":true}"
            : "{\"downloadMode\":3,\"fallbackToDirect\":true}", JsonSerializerOptions.Web)!;
        await Assert.ThrowsExceptionAsync<System.InvalidOperationException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            preferTorrent: probeTorrent, resultWorkflowId: null,
            downloadMode: options.EffectiveDownloadMode, fallbackToDirect: options.FallbackToDirect));
        Assert.AreEqual(0, handler.ImageRequests, "Even a fresh negative torrent verdict must never select GP downloads.");
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task SwitchingTorrentFileToBt_PromotesExistingMetadataWithoutTreatingItAsDownloadedContents()
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler);
        const string url = "https://exhentai.org/g/12345/abcdef0123/";
        await RunProducer(producer, url, _ => Task.CompletedTask, resultWorkflowId: null,
            downloadMode: ExHentaiDownloadMode.TorrentFile, fallbackToDirect: false);
        var saved = (await _results.GetByTaskAsync(10)).Single();
        Assert.IsFalse(saved.RequiresContentsDownload);
        var destination = Path.Combine(_root, "gallery-12345-abcdef0123");

        await RunProducer(producer, url, _ => Task.CompletedTask, downloadPath: destination,
            resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent, fallbackToDirect: false);

        var promoted = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(saved.Id, promoted.Id);
        Assert.AreEqual(saved.Fingerprint, promoted.Fingerprint);
        Assert.IsTrue(promoted.RequiresContentsDownload);
        Assert.AreEqual(destination, promoted.DownloadDirectory);
        Assert.IsNull(await _results.GetAvailableContentsAsync(promoted), "Metadata is not completed BT content.");
        Assert.AreEqual(1, handler.TorrentRequests, "Verified saved metadata is reused without another site request.");
        Assert.AreEqual(0, handler.ImageRequests, "The BT shortcut never falls back to GP-consuming images.");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BtMode_RecordsIntentAndExactDestination_WithOptionalTorrentExport(bool export)
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler);
        var destination = Path.Combine(_root, "gallery-12345-abcdef0123");
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            downloadPath: destination, resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent,
            saveTorrentFile: export);

        var result = (await _results.GetByTaskAsync(10)).Single();
        Assert.IsTrue(result.RequiresContentsDownload);
        Assert.AreEqual(destination, result.DownloadDirectory);
        Assert.IsNull(result.WorkflowDefinitionId, "BT is usable without creating a workflow.");
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(result.Path));
        Assert.AreEqual(export ? 1 : 0, Directory.GetFiles(destination, "*.torrent", SearchOption.AllDirectories).Length);
        Assert.AreEqual(0, handler.ImageRequests);
        Assert.AreEqual(export ? 2 : 1, JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Length);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            downloadPath: destination, resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent,
            saveTorrentFile: export);
        Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(1, handler.TorrentRequests, "Restart reuses verified managed metadata.");
    }

    [TestMethod]
    public async Task BtMode_NoTorrentAndFallbackDisabled_DoesNotSpendGp()
    {
        var handler = new GalleryHandler();
        using var producer = await BuildProducer(handler);
        await Assert.ThrowsExceptionAsync<System.InvalidOperationException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent, fallbackToDirect: false));
        Assert.AreEqual(0, handler.ImageRequests);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task BtMode_NoTorrentAndFallbackEnabled_ProducesFilesWithoutBtIntent()
    {
        var handler = new GalleryHandler();
        using var producer = await BuildProducer(handler);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent);
        var result = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(DownloadResultKind.LocalFiles, result.Kind);
        Assert.IsFalse(result.RequiresContentsDownload);
        Assert.AreEqual(1, handler.ImageRequests);
    }

    [TestMethod]
    public void DownloadMode_DefaultsPreserveLegacyAndExplicitTaskChoices()
    {
        static ExHentaiTaskOptions Read(string? json, ExHentaiDownloadMode? mode = null) =>
            JsonSerializer.Deserialize<ExHentaiTaskOptions>(ExHentaiDownloaderHelper.ApplyDefaultTaskOptions(
                json, true, null, mode, true, false), JsonSerializerOptions.Web)!;
        Assert.AreEqual(ExHentaiDownloadMode.TorrentFile, Read(null).EffectiveDownloadMode);
        Assert.AreEqual(ExHentaiDownloadMode.Torrent, Read(null, ExHentaiDownloadMode.Torrent).EffectiveDownloadMode);
        Assert.AreEqual(ExHentaiDownloadMode.Direct, Read("{\"preferTorrent\":false}", ExHentaiDownloadMode.Torrent).EffectiveDownloadMode);
        Assert.AreEqual(ExHentaiDownloadMode.TorrentFile, Read("{\"downloadMode\":3}", ExHentaiDownloadMode.Direct).EffectiveDownloadMode);
        Assert.IsTrue(Read(null).SaveTorrentFile);
        Assert.IsFalse(Read(null).FallbackToDirect);
    }
}

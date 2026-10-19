using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    [DataTestMethod]
    [DataRow(null)]
    [DataRow(7)]
    public async Task BtMode_UsesTheSelectedRootBeforeResolvingItsTemplate_EvenWithAWorkflow(int? workflowId)
    {
        const string url = "https://exhentai.org/g/12345/abcdef0123/";
        var workRoot = ExHentaiDownloadResultHelper.GetWorkDirectory(_root, url, workflowId, downloadContents: true);
        Assert.AreEqual(_root, workRoot, "BT content names are resolved from gallery metadata, not the URL token.");
        using var producer = await BuildProducer(new GalleryHandler {TorrentBytes = _metadata});

        await RunProducer(producer, url, _ => Task.CompletedTask, downloadPath: workRoot,
            resultWorkflowId: workflowId, downloadMode: ExHentaiDownloadMode.Torrent, saveTorrentFile: true);

        var result = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(Path.Combine(_root, "[Misc] Gallery 12345 [g12345]"), result.DownloadDirectory);
        Assert.AreEqual(Path.Combine(_root, "[Misc] Gallery 12345 [g12345].torrent"),
            ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result));
        Assert.AreEqual(workflowId, result.WorkflowDefinitionId);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "gallery-12345-abcdef0123")));
        Assert.AreEqual(Path.Combine(_root, "gallery-12345-abcdef0123"),
            ExHentaiDownloadResultHelper.GetWorkDirectory(_root, url, 7),
            "Legacy non-BT workflow handoffs retain their isolated work directory.");
    }

    [DataTestMethod]
    [DataRow("Library/{Category}/{RawName} [g{GalleryId}]/{PageTitle}{Extension}",
        "Library/Misc/Gallery 12345 [g12345]")]
    [DataRow("{RawName}/{GalleryId}/{PageTitle}{Extension}",
        "Gallery 12345/12345")]
    [DataRow("{PageTitle}{Extension}", "[Misc] Gallery 12345")]
    public async Task BtMode_CustomTemplateNamesContentsAndItsSiblingTorrentFile(
        string convention, string relativeDirectory)
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler, convention);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent, saveTorrentFile: true);

        var result = (await _results.GetByTaskAsync(10)).Single();
        var expectedDirectory = Path.Combine(_root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        var expectedTorrent = expectedDirectory + ".torrent";
        Assert.AreEqual(expectedDirectory, result.DownloadDirectory);
        Assert.AreEqual(expectedTorrent, ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result));
        CollectionAssert.AreEqual(new[] {result.Path, expectedTorrent}, JsonSerializer.Deserialize<string[]>(result.FilesJson));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(expectedTorrent));
        Assert.AreEqual(0, Directory.GetFiles(expectedDirectory, "*.torrent", SearchOption.AllDirectories).Length);
        Assert.AreEqual(0, handler.ImageRequests);
    }

    [TestMethod]
    public async Task BtMode_CustomTemplateWithoutIdentityCanIntentionallyShareAnOutputDirectory()
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata, GalleryName = "Same Gallery"};
        using var producer = await BuildProducer(handler, "Images/{PageTitle}{Extension}");
        foreach (var id in new[] {12345, 12346})
            await RunProducer(producer, $"https://exhentai.org/g/{id}/abcdef0123/", _ => Task.CompletedTask,
                resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent, saveTorrentFile: true);

        var results = await _results.GetByTaskAsync(10);
        Assert.AreEqual(2, results.Count, "Each source retains its own result even when its custom output path is shared.");
        Assert.IsTrue(results.All(result => result.DownloadDirectory == Path.Combine(_root, "Images")));
        Assert.IsTrue(results.All(result => ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result) ==
                                           Path.Combine(_root, "Images.torrent")));
        Assert.AreEqual(1, Directory.GetFiles(_root, "*.torrent").Length);
        Assert.IsFalse(Directory.GetDirectories(_root, "gallery-*").Any());
    }

    [TestMethod]
    public async Task BtMode_DeletedExportIsRestoredOfflineWithoutChangingTheContentDirectoryOrTemplate()
    {
        const string url = "https://exhentai.org/g/12345/abcdef0123/";
        using (var producer = await BuildProducer(new GalleryHandler {TorrentBytes = _metadata},
                   "Archive/{RawName} [g{GalleryId}]/{PageTitle}{Extension}"))
            await RunProducer(producer, url, _ => Task.CompletedTask, resultWorkflowId: null,
                downloadMode: ExHentaiDownloadMode.Torrent, saveTorrentFile: true);
        var saved = (await _results.GetByTaskAsync(10)).Single();
        var torrent = ExHentaiDownloadResultHelper.GetTorrentDownloadPath(saved)!;
        var partialFile = Path.Combine(saved.DownloadDirectory, "partial.bin");
        await File.WriteAllTextAsync(partialFile, "downloaded BT pieces");
        File.Delete(torrent);
        var handler = new GalleryHandler {RejectRequests = true};
        using var retry = await BuildProducer(handler, "Changed/{RawName}/{PageTitle}{Extension}");
        var accounted = new System.Collections.Generic.List<string>();
        retry.OnFileDownloaded += (path, _) => {accounted.Add(path); return Task.CompletedTask;};

        await RunProducer(retry, url, _ => Task.CompletedTask, resultWorkflowId: null,
            downloadMode: ExHentaiDownloadMode.Torrent, saveTorrentFile: true);

        var recovered = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(saved.Id, recovered.Id);
        Assert.AreEqual(saved.DownloadDirectory, recovered.DownloadDirectory);
        Assert.AreEqual(torrent, ExHentaiDownloadResultHelper.GetTorrentDownloadPath(recovered));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(torrent));
        CollectionAssert.AreEqual(new[] {torrent}, accounted);
        Assert.AreEqual("downloaded BT pieces", await File.ReadAllTextAsync(partialFile));
        Assert.AreEqual(0, handler.Requests);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Changed")));
    }

    [TestMethod]
    public async Task BtMode_RetryPreservesALegacyContentDirectoryWithoutRevisitingTheGallery()
    {
        var directory = Path.Combine(_root, "gallery-12345-abcdef0123");
        Directory.CreateDirectory(directory);
        var partialFile = Path.Combine(directory, "partial.bin");
        await File.WriteAllTextAsync(partialFile, "legacy BT pieces");
        var previous = await _results.RecordTorrentMetadataAsync(10, ThirdParty, "12345/abcdef0123", "Gallery 12345",
            _metadata, directory, null, requiresContentsDownload: true);
        var handler = new GalleryHandler {RejectRequests = true};
        using var producer = await BuildProducer(handler);

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null, downloadMode: ExHentaiDownloadMode.Torrent);

        var resumed = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(previous.Id, resumed.Id);
        Assert.AreEqual(directory, resumed.DownloadDirectory, "Retries must continue using already downloaded BT pieces.");
        Assert.AreEqual("legacy BT pieces", await File.ReadAllTextAsync(partialFile));
        Assert.AreEqual(0, handler.Requests);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "[Misc] Gallery 12345 [g12345]")));
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.torrent").Length);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SwitchingBtOutputChoices_AfterTemplateChangesKeepsTheSavedSiblingPathsOffline(bool startWithTorrentFile)
    {
        const string url = "https://exhentai.org/g/12345/abcdef0123/";
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler, "Archive/{RawName} [{GalleryId}]/{PageTitle}{Extension}");
        await RunProducer(producer, url, _ => Task.CompletedTask, resultWorkflowId: null,
            downloadMode: startWithTorrentFile ? ExHentaiDownloadMode.TorrentFile : ExHentaiDownloadMode.Torrent);
        var saved = (await _results.GetByTaskAsync(10)).Single();
        var offlineHandler = new GalleryHandler {RejectRequests = true};
        using var retry = await BuildProducer(offlineHandler, "Changed/{GalleryId}/{PageTitle}{Extension}");

        await RunProducer(retry, url, _ => Task.CompletedTask, resultWorkflowId: null,
            downloadMode: ExHentaiDownloadMode.Torrent, saveTorrentFile: true);

        var changed = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(saved.Id, changed.Id);
        Assert.IsTrue(changed.RequiresContentsDownload);
        Assert.AreEqual(Path.Combine(_root, "Archive", "Gallery 12345 [12345]"), changed.DownloadDirectory);
        var exported = Path.Combine(_root, "Archive", "Gallery 12345 [12345].torrent");
        Assert.AreEqual(exported, ExHentaiDownloadResultHelper.GetTorrentDownloadPath(changed));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(exported));
        Assert.AreEqual(1, handler.TorrentRequests);
        Assert.AreEqual(0, offlineHandler.Requests,
            "Adding the other BT output uses the first output's saved template path without another gallery request.");
        Assert.AreEqual(0, handler.ImageRequests);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Changed")));
    }

    [TestMethod]
    public async Task TorrentOutputLookup_PrefersRecordedUserCopyOutsideTheBtContentDirectory()
    {
        var directory = Path.Combine(_root, "Archive", "Gallery 12345 [g12345]");
        var torrent = Path.Combine(_root, "Gallery 12345 [g12345].torrent");
        await File.WriteAllBytesAsync(torrent, _metadata);
        var result = await _results.RecordTorrentMetadataAsync(10, ThirdParty, "12345/abcdef0123", "Gallery 12345",
            _metadata, directory, null, requiresContentsDownload: true, exportedTorrentPath: torrent);

        Assert.AreEqual(torrent, ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result));
        File.Delete(torrent);
        Assert.AreEqual(torrent, ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result),
            "Missing exports still need their recorded path for offline recovery.");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TorrentOutputs_CustomTemplateCannotWriteThroughALinkedParent(bool downloadContents)
    {
        if (OperatingSystem.IsWindows()) return;
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(Path.Combine(_root, "linked"), target);
        using var producer = await BuildProducer(new GalleryHandler {TorrentBytes = _metadata},
            "linked/{RawName}/{PageTitle}{Extension}");

        await Assert.ThrowsExceptionAsync<IOException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, resultWorkflowId: null,
            downloadMode: downloadContents ? ExHentaiDownloadMode.Torrent : ExHentaiDownloadMode.TorrentFile,
            saveTorrentFile: true));

        Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TorrentOutputs_ParentSegmentsInCustomTemplateStayInsideTheSelectedRoot(bool downloadContents)
    {
        using var producer = await BuildProducer(new GalleryHandler {TorrentBytes = _metadata},
            "../../outside/{PageTitle}{Extension}");

        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null,
            downloadMode: downloadContents ? ExHentaiDownloadMode.Torrent : ExHentaiDownloadMode.TorrentFile,
            saveTorrentFile: true);

        var expectedDirectory = Path.Combine(_root, "_", "_", "outside");
        var result = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(expectedDirectory + ".torrent", ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result));
        Assert.AreEqual(downloadContents ? expectedDirectory : Path.GetDirectoryName(expectedDirectory), result.DownloadDirectory);
        Assert.AreEqual(downloadContents, Directory.Exists(expectedDirectory));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(expectedDirectory + ".torrent"));
    }
}

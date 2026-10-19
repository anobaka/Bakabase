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

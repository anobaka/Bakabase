using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TorrentPreference_TakesPriorityOverOriginalsAndNeverFallsThroughOnFailure(bool rejected)
    {
        var handler = new GalleryHandler
        {
            TorrentBytes = rejected ? Encoding.ASCII.GetBytes("d4:infodee") : _metadata,
            OriginalLinkSize = "1 MB",
            Posted = 1_600_000_000
        };
        using var producer = await BuildProducer(handler, configure: OriginalOptions);
        if (rejected)
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => RunProducer(producer,
                "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask));
        else
            await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask);

        Assert.AreEqual(1, handler.TorrentRequests);
        Assert.AreEqual(0, handler.ImagePageRequests);
        Assert.AreEqual(0, handler.ImageRequests);
        Assert.AreEqual(0, handler.OriginalRequests);
        Assert.AreEqual(0, handler.BalanceRequests);
    }

    [TestMethod]
    [DataRow("Archive/{RawName} [g{GalleryId}]/{PageTitle}{Extension}", "Gallery 12345 [g12345].torrent")]
    [DataRow("Archive/{GalleryId}/{RawName}/{PageTitle}{Extension}", "12345 Gallery 12345.torrent")]
    [DataRow("{RawName}/{GalleryId}{Extension}", "Gallery 12345 12345.torrent")]
    [DataRow("{RawName}/{PageTitle} [g{GalleryId}]{Extension}", "Gallery 12345 [g12345].torrent")]
    [DataRow("{GalleryId}/{RawName}{Extension}", "12345 Gallery 12345.torrent")]
    [DataRow("{GalleryId}-{PageTitle}/{RawName}{Extension}", "12345-Gallery 12345 Gallery 12345.torrent")]
    [DataRow("Images/{PageTitle}{Extension}", "Gallery 12345.torrent")]
    [DataRow("{RawName} [g{GalleryId}]{Extension}", "Gallery 12345 [g12345].torrent")]
    public async Task Torrents_UseTemplateNamesWithoutCreatingTemplateDirectories(string convention, string filename)
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata};
        using var producer = await BuildProducer(handler, convention);
        var accounted = new List<string>();
        producer.OnFileDownloaded += (path, _) => {accounted.Add(path); return Task.CompletedTask;};
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            resultWorkflowId: null);

        var expected = Path.Combine(_root, filename);
        Assert.IsTrue(File.Exists(expected));
        CollectionAssert.AreEqual(_metadata, await File.ReadAllBytesAsync(expected));
        var result = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(_root, result.DownloadDirectory);
        Assert.AreEqual(expected, ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result));
        CollectionAssert.AreEqual(new[] {result.Path, expected}, JsonSerializer.Deserialize<string[]>(result.FilesJson));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Archive")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Images")));

        // Restarts recover the persisted filename even after the naming template changes.
        using var retry = await BuildProducer(new GalleryHandler {RejectRequests = true}, "Changed/{PageTitle}{Extension}");
        retry.OnFileDownloaded += (path, _) => {accounted.Add(path); return Task.CompletedTask;};
        await RunProducer(retry, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask);
        CollectionAssert.AreEqual(new[] {expected, expected}, accounted);
    }

    [TestMethod]
    public async Task TorrentRejectedByEngine_IsNotPromotedOrCached_AndAValidRetrySucceeds()
    {
        // This is structurally bencoded, but is not usable BitTorrent metadata. The transport
        // accepts its shape; the producer must reject it before moving it to the user's filename.
        var handler = new GalleryHandler {TorrentBytes = Encoding.ASCII.GetBytes("d4:infodee")};
        var producer = await BuildProducer(handler);
        var error = await Assert.ThrowsExceptionAsync<ArgumentException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask));
        StringAssert.Contains(error.Message, "not valid BitTorrent metadata");
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.torrent", SearchOption.AllDirectories).Length);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Length);

        handler.TorrentBytes = _metadata;
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask);
        Assert.AreEqual(2, handler.TorrentRequests);
        Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
    }
}

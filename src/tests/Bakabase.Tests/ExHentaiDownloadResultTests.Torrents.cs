using System.Text;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
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

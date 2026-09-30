using System.Text;
using Bakabase.Modules.Downloader.Components;
using MonoTorrent.BEncoding;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public sealed class TorrentMetadataTests
{
    [TestMethod]
    public void ValidSmallTorrent_IsAcceptedWithoutChangingItsBytesOrInfoHash()
    {
        var bytes = Metadata();
        Assert.IsTrue(bytes.Length < 20 * 1024);
        var before = MonoTorrent.Torrent.Load(bytes).InfoHashes;
        TorrentMetadata.Validate(bytes);
        Assert.AreEqual(before, TorrentMetadata.Parse(bytes).InfoHashes);
    }

    [TestMethod]
    public void SizeAndFormatFailures_AreReportedSeparately()
    {
        var empty = Assert.ThrowsException<ArgumentException>(() => TorrentMetadata.Validate([]));
        StringAssert.Contains(empty.Message, "empty");
        var large = Assert.ThrowsException<ArgumentException>(() =>
            TorrentMetadata.Validate(new byte[TorrentMetadata.MaxMetadataBytes + 1]));
        StringAssert.Contains(large.Message, "4 MiB");
        var invalid = Assert.ThrowsException<ArgumentException>(() =>
            TorrentMetadata.Validate(Encoding.UTF8.GetBytes("d4:infodee")));
        StringAssert.Contains(invalid.Message, "not valid BitTorrent metadata");
        Assert.IsNotNull(invalid.InnerException, "Preserve the engine's format failure for diagnosis.");
        Assert.IsFalse(invalid.Message.Contains("4 MiB"));
    }

    [TestMethod]
    public void HtmlResponse_IsNotMisreportedAsAnOversizedTorrent()
    {
        var error = Assert.ThrowsException<ArgumentException>(() => TorrentMetadata.Validate(
            Encoding.UTF8.GetBytes("\uFEFF \r\n<html><body>Please log in</body></html>")));
        StringAssert.Contains(error.Message, "HTML");
        Assert.IsFalse(error.Message.Contains("4 MiB"));
    }

    [TestMethod]
    public async Task BoundedReader_StillRejectsAnActualOversizedResponse()
    {
        await using var stream = new MemoryStream(new byte[TorrentMetadata.MaxMetadataBytes + 1]);
        var error = await Assert.ThrowsExceptionAsync<ArgumentException>(() => TorrentMetadata.ReadBoundedAsync(stream));
        StringAssert.Contains(error.Message, "4 MiB");
    }

    private static byte[] Metadata() => new BEncodedDictionary
    {
        ["info"] = new BEncodedDictionary
        {
            ["name"] = new BEncodedString("一张图片.jpg"),
            ["length"] = new BEncodedNumber(1),
            ["piece length"] = new BEncodedNumber(16384),
            ["pieces"] = new BEncodedString(new byte[20])
        }
    }.Encode();
}

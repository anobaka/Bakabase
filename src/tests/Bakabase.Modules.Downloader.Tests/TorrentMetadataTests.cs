using System.Text;
using System.Security.Cryptography;
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

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HybridTorrent_FinalPaddingVariantsPreserveMetadataAndBothInfoHashes(bool finalPadding)
    {
        var metadata = HybridMetadata(finalPadding);
        var infoBytes = ((BEncodedDictionary)metadata["info"]).Encode();
        var bytes = metadata.Encode();
        var original = bytes.ToArray();

        var torrent = TorrentMetadata.Parse(bytes);

        CollectionAssert.AreEqual(original, bytes, "Compatibility must not rewrite the torrent metadata.");
        CollectionAssert.AreEqual(SHA1.HashData(infoBytes), torrent.InfoHashes.V1!.Span.ToArray());
        CollectionAssert.AreEqual(SHA256.HashData(infoBytes), torrent.InfoHashes.V2!.Span.ToArray());
        Assert.AreEqual(2, torrent.Files.Count, "Synthetic padding must not become a user file.");
        Assert.AreEqual("file1.txt", torrent.Files[0].Path);
        Assert.AreEqual(9L, torrent.Files[0].Length);
        Assert.AreEqual(32759, torrent.Files[0].Padding);
        Assert.AreEqual("file2.txt", torrent.Files[1].Path);
        Assert.AreEqual(14L, torrent.Files[1].Length);
        Assert.AreEqual(finalPadding ? 32754 : 0, torrent.Files[1].Padding);
    }

    [DataTestMethod]
    [DataRow("middlePadding", "padding mismatch")]
    [DataRow("length", "length mismatch")]
    [DataRow("path", "mismatch in v1 and v2 files")]
    [DataRow("fileCount", "number of files differ")]
    public void HybridTorrent_RealRepresentationMismatchesAreStillRejected(string change, string expectedReason)
    {
        var metadata = HybridMetadata(true);
        var info = (BEncodedDictionary)metadata["info"];
        var files = (BEncodedList)info["files"];
        var tree = (BEncodedDictionary)info["file tree"];
        switch (change)
        {
            case "middlePadding": ((BEncodedDictionary)files[1])["length"] = new BEncodedNumber(32758); break;
            case "length": ((BEncodedDictionary)((BEncodedDictionary)tree["file2.txt"])[""])["length"] = new BEncodedNumber(15); break;
            case "path": ((BEncodedDictionary)files[2])["path"] = new BEncodedList { new BEncodedString("file3.txt") }; break;
            case "fileCount": tree.Remove("file2.txt"); break;
        }
        var bytes = metadata.Encode();
        var original = bytes.ToArray();

        var error = Assert.ThrowsException<ArgumentException>(() => TorrentMetadata.Parse(bytes));

        Assert.IsNotNull(error.InnerException);
        StringAssert.Contains(error.InnerException.Message, expectedReason);
        CollectionAssert.AreEqual(original, bytes);
    }

    // The upstream #688 regression uses this pair of tiny files. Real hashes here also prove that
    // accepting trailing padding retains both original swarm identities, instead of stripping info keys.
    private static BEncodedDictionary HybridMetadata(bool finalPadding)
    {
        const int pieceLength = 32768;
        var first = "123456789"u8.ToArray();
        var second = "second payload"u8.ToArray();
        var v1Files = new BEncodedList
        {
            new BEncodedDictionary { ["length"] = new BEncodedNumber(first.Length), ["path"] = new BEncodedList { new BEncodedString("file1.txt") } },
            new BEncodedDictionary { ["attr"] = new BEncodedString("p"), ["length"] = new BEncodedNumber(pieceLength - first.Length), ["path"] = new BEncodedList { new BEncodedString(".pad"), new BEncodedString("32759") } },
            new BEncodedDictionary { ["length"] = new BEncodedNumber(second.Length), ["path"] = new BEncodedList { new BEncodedString("file2.txt") } }
        };
        if (finalPadding) v1Files.Add(new BEncodedDictionary
        {
            ["attr"] = new BEncodedString("p"), ["length"] = new BEncodedNumber(pieceLength - second.Length),
            ["path"] = new BEncodedList { new BEncodedString(".pad"), new BEncodedString("32754") }
        });
        byte[] PieceHash(byte[] contents, bool padded)
        {
            var piece = new byte[padded ? pieceLength : contents.Length];
            contents.CopyTo(piece, 0);
            return SHA1.HashData(piece);
        }
        BEncodedDictionary FileNode(byte[] contents) => new()
        {
            [""] = new BEncodedDictionary
            {
                ["length"] = new BEncodedNumber(contents.Length), ["pieces root"] = new BEncodedString(SHA256.HashData(contents))
            }
        };
        return new BEncodedDictionary
        {
            ["info"] = new BEncodedDictionary
            {
                ["name"] = new BEncodedString("hybrid-padding"), ["meta version"] = new BEncodedNumber(2),
                ["piece length"] = new BEncodedNumber(pieceLength), ["files"] = v1Files,
                ["file tree"] = new BEncodedDictionary { ["file1.txt"] = FileNode(first), ["file2.txt"] = FileNode(second) },
                ["pieces"] = new BEncodedString(PieceHash(first, true).Concat(PieceHash(second, finalPadding)).ToArray())
            }
        };
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

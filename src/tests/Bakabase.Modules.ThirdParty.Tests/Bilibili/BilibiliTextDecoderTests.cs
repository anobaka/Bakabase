using System.IO.Compression;
using System.Text;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliTextDecoderTests
{
    private const string Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><i><d p=\"0.5,1,25,16777215\">弹幕</d></i>";
    private static readonly byte[] XmlBytes = Encoding.UTF8.GetBytes(Xml);

    private static byte[] Compress(byte[] input, Func<Stream, Stream> compressor)
    {
        using var output = new MemoryStream();
        using (var stream = compressor(output))
        {
            stream.Write(input);
        }

        return output.ToArray();
    }

    private static byte[] RawDeflate(byte[] b) => Compress(b, s => new DeflateStream(s, CompressionLevel.Optimal, true));
    private static byte[] Zlib(byte[] b) => Compress(b, s => new ZLibStream(s, CompressionLevel.Optimal, true));
    private static byte[] Gzip(byte[] b) => Compress(b, s => new GZipStream(s, CompressionLevel.Optimal, true));
    private static byte[] Brotli(byte[] b) => Compress(b, s => new BrotliStream(s, CompressionLevel.Optimal, true));

    [TestMethod]
    public void RawDeflateAsCommentBilibiliSendsIt()
    {
        CollectionAssert.AreEqual(XmlBytes, BilibiliTextDecoder.Decode(RawDeflate(XmlBytes), ["deflate"]));
        Assert.AreEqual(Xml, BilibiliTextDecoder.DecodeDanmakuXml(RawDeflate(XmlBytes), ["deflate"]));
    }

    [TestMethod]
    public void ZlibDeflate()
    {
        CollectionAssert.AreEqual(XmlBytes, BilibiliTextDecoder.Decode(Zlib(XmlBytes), ["deflate"]));
    }

    [TestMethod]
    public void GzipSubtitleJson()
    {
        var json = Encoding.UTF8.GetBytes(BilibiliFixtures.Read("subtitle-human.json"));
        CollectionAssert.AreEqual(json, BilibiliTextDecoder.Decode(Gzip(json), ["gzip"]));
    }

    [TestMethod]
    public void BrotliAndStackedEncodings()
    {
        CollectionAssert.AreEqual(XmlBytes, BilibiliTextDecoder.Decode(Brotli(XmlBytes), ["br"]));
        // Content-Encoding: deflate, gzip → gzip was applied last, so it is undone first.
        CollectionAssert.AreEqual(XmlBytes, BilibiliTextDecoder.Decode(Gzip(RawDeflate(XmlBytes)), ["deflate, gzip"]));
        CollectionAssert.AreEqual(XmlBytes, BilibiliTextDecoder.Decode(XmlBytes, []));
        CollectionAssert.AreEqual(XmlBytes, BilibiliTextDecoder.Decode(XmlBytes, ["identity"]));
    }

    [TestMethod]
    public void UndeclaredRawDeflateIsStillRecognisedAsDanmaku()
    {
        Assert.AreEqual(Xml, BilibiliTextDecoder.DecodeDanmakuXml(RawDeflate(XmlBytes), []));
    }

    [TestMethod]
    public void TheBomIsStripped()
    {
        var withBom = Encoding.UTF8.GetPreamble().Concat(XmlBytes).ToArray();
        Assert.AreEqual(Xml, BilibiliTextDecoder.DecodeDanmakuXml(withBom, []));
    }

    [TestMethod]
    public void GarbageAndUnknownEncodingsThrow()
    {
        Assert.ThrowsException<InvalidDataException>(() =>
            BilibiliTextDecoder.DecodeDanmakuXml([1, 2, 3, 4, 5, 6, 7, 8], []));
        Assert.ThrowsException<InvalidDataException>(() =>
            BilibiliTextDecoder.DecodeDanmakuXml(Encoding.UTF8.GetBytes("<html>error</html>"), []));
        Assert.ThrowsException<InvalidDataException>(() => BilibiliTextDecoder.Decode(XmlBytes, ["compress"]));
    }

    [TestMethod]
    public void OutputIsCapped()
    {
        var huge = RawDeflate(new byte[BilibiliTextDecoder.MaxDecodedBytes + 1]);
        Assert.ThrowsException<InvalidDataException>(() => BilibiliTextDecoder.Decode(huge, ["deflate"]));
    }
}

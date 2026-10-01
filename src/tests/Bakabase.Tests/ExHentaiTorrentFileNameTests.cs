using System;
using System.Linq;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Tests;

[TestClass]
public sealed class ExHentaiTorrentFileNameTests
{
    [TestMethod]
    [DataRow("Custom name.torrent")]
    [DataRow("[Misc] Gallery [g12345].torrent")]
    public void ShortNamesRemainUnchanged(string name) =>
        Assert.AreEqual(name, ExHentaiTorrentFileName.Limit(name, "12345/abc"));

    [TestMethod]
    public void ExactComponentLimitRemainsUnchanged()
    {
        var name = new string('a', 247) + ".torrent";
        Assert.AreEqual(255, Encoding.UTF8.GetByteCount(name));
        Assert.AreEqual(name, ExHentaiTorrentFileName.Limit(name, "12345/abc"));
    }

    [TestMethod]
    [DataRow("ascii")]
    [DataRow("cjk")]
    [DataRow("emoji")]
    public void OversizedNamesKeepGalleryIdAndExtensionWithoutSplittingUnicode(string kind)
    {
        var title = kind switch
        {
            "cjk" => new string('画', 100),
            "emoji" => string.Concat(Enumerable.Repeat("🎨", 100)),
            _ => new string('a', 248)
        };
        var name = title + ".torrent";
        var result = ExHentaiTorrentFileName.Limit(name, "12345/abc");
        Assert.AreNotEqual(name, result);
        Assert.IsTrue(result.Length <= 255);
        Assert.IsTrue(new UTF8Encoding(false, true).GetByteCount(result) <= 255);
        StringAssert.Contains(result, " [g12345] [");
        Assert.IsTrue(result.EndsWith(".torrent", StringComparison.Ordinal));
        Assert.AreEqual(result, ExHentaiTorrentFileName.Limit(name, "12345/abc"));
    }

    [TestMethod]
    public void TruncatedNamesDistinguishDifferentTitlesAndGallerySources()
    {
        var prefix = new string('x', 300);
        var first = ExHentaiTorrentFileName.Limit(prefix + "A.torrent", "12345/abc");
        Assert.AreNotEqual(first, ExHentaiTorrentFileName.Limit(prefix + "B.torrent", "12345/abc"));
        Assert.AreNotEqual(first, ExHentaiTorrentFileName.Limit(prefix + "A.torrent", "12346/abc"));
        Assert.AreNotEqual(first, ExHentaiTorrentFileName.Limit(prefix + "A.torrent", "12345/def"));
    }
}

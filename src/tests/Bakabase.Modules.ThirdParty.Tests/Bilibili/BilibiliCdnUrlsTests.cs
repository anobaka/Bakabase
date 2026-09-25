using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliCdnUrlsTests
{
    private const string Query =
        "?deadline=1900000000&gen=playurlv3&os=upos&upsig=00000000000000000000000000000000&uparams=e,deadline&bw=1";

    private const string Mcdn = "https://xy0x0x0x0xy.mcdn.bilivideo.cn:8082/upgcxcode/00/00/5001/5001-1-100026-80.m4s" + Query;
    private const string Edge = "https://b-example.edge.mountaintoys.cn:4483/upgcxcode/00/00/5001/5001-1-100026-80.m4s" + Query;
    private const string Other = "https://overseas.example.com/upgcxcode/00/00/5001/5001-1-100026-80.m4s" + Query;
    private const string Cn = "https://cn-example-ct-01-01.bilivideo.com/upgcxcode/00/00/5001/5001-1-100026-80.m4s" + Query;
    private const string Upos = "https://upos-sz-example.bilivideo.com/upgcxcode/00/00/5001/5001-1-100026-80.m4s" + Query;

    [TestMethod]
    public void RankedStablyDeduplicatedAndHttpOnly()
    {
        var urls = BilibiliCdnUrls.Candidates(Mcdn, [Edge, Other, Cn, Upos, Mcdn, "ftp://x/y", "/relative", "", "not a url"]);
        CollectionAssert.AreEqual(new[] {Upos, Cn, Other, Mcdn, Edge}, urls.ToArray());
        Assert.AreEqual(0, BilibiliCdnUrls.Candidates(null, null).Count);
        CollectionAssert.AreEqual(new[] {Upos}, BilibiliCdnUrls.Candidates(null, [Upos]).ToArray());
    }

    [DataTestMethod]
    [DataRow("https://upos-sz-mirrorcos.bilivideo.com/a", 0)]
    [DataRow("https://cn-gdfs-ct-01-01.bilivideo.com/a", 1)]
    [DataRow("https://upos-hz-mirrorakam.akamaized.net/a", 0, DisplayName = "upos- prefix wins even off bilivideo.com")]
    [DataRow("https://overseas.example.com/a", 2)]
    [DataRow("https://xy0x0x0x0xy.mcdn.bilivideo.cn/a", 3)]
    [DataRow("https://xy0x0x0x0xy.mcdn.bilivideo.cn:8082/a", 3)]
    [DataRow("https://b-example.edge.mountaintoys.cn:4483/a", 3)]
    [DataRow("https://example.szbdyd.com/a", 3)]
    [DataRow("https://upos-sz-example.bilivideo.com:8080/a", 3)]
    public void HostRank(string url, int rank)
    {
        Assert.AreEqual(rank, BilibiliCdnUrls.HostRank(new Uri(url)));
    }

    [TestMethod]
    public void Deadline()
    {
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1900000000), BilibiliCdnUrls.TryGetDeadline(Upos));
        Assert.IsNull(BilibiliCdnUrls.TryGetDeadline("https://upos-sz-example.bilivideo.com/a.m4s"));
        Assert.IsNull(BilibiliCdnUrls.TryGetDeadline("https://upos-sz-example.bilivideo.com/a.m4s?deadline=soon"));
        Assert.IsNull(BilibiliCdnUrls.TryGetDeadline("garbage"));
    }

    [TestMethod]
    public void RedactKeepsHostPortAndFileNameOnly()
    {
        Assert.AreEqual("https://xy0x0x0x0xy.mcdn.bilivideo.cn:8082/…/5001-1-100026-80.m4s", BilibiliCdnUrls.Redact(Mcdn));
        Assert.AreEqual("https://upos-sz-example.bilivideo.com/…/5001-1-100026-80.m4s", BilibiliCdnUrls.Redact(Upos));
        Assert.AreEqual("https://aisubtitle.hdslb.com/…/0001.json",
            BilibiliCdnUrls.Redact("//aisubtitle.hdslb.com/bfs/subtitle/0001.json?auth_key=1-0-0-0"));
        Assert.AreEqual("https://comment.bilibili.com/5001.xml", BilibiliCdnUrls.Redact("https://comment.bilibili.com/5001.xml"));
        Assert.AreEqual("https://h.example.com/", BilibiliCdnUrls.Redact("https://h.example.com?oi=1"));
        Assert.AreEqual("(no url)", BilibiliCdnUrls.Redact(null));
        Assert.AreEqual("(invalid url)", BilibiliCdnUrls.Redact("not a url?upsig=1"));
    }

    [TestMethod]
    public void Extensions()
    {
        Assert.AreEqual(".m4s", BilibiliCdnUrls.GetExtension(Upos));
        Assert.AreEqual(".flv", BilibiliCdnUrls.GetExtension("https://upos-sz-example.bilivideo.com/a/441330-1-32.FLV?x=.mp4"));
        Assert.AreEqual("", BilibiliCdnUrls.GetExtension("https://upos-sz-example.bilivideo.com/a/file"));
        Assert.AreEqual("", BilibiliCdnUrls.GetExtension("garbage"));
        Assert.AreEqual("5001-1-100026-80.m4s", BilibiliCdnUrls.GetFileName(Upos));
    }
}

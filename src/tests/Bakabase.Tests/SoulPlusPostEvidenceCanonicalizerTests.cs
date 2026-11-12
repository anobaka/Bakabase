using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class SoulPlusPostEvidenceCanonicalizerTests
{
    private const string Url = "https://www.north-plus.net/read.php?tid=42";
    private static string Sale(string buyers = "12", string nonce = "old", string pid = "tpc", string price = "1") =>
        $"<h6 class='quote jumbotron'><span class='s3'>此帖售价 {price} SP币,已有 {buyers} 人购买</span>" +
        $"<input type='button' value='愿意购买,我买,我付钱' onclick=\"location.href='job.php?action=buytopic&amp;tid=42&amp;pid={pid}&amp;verify={nonce}'\"/></h6>";
    private static string Page(string main, string reply = "") =>
        "<html><body><h1 id='subject_tpc'>Fixture</h1><div class='tpc_content'><div class='f14' id='read_tpc'>" + main + "</div></div>" +
        (reply == "" ? "" : "<table><tr><td><div class='tiptop'>B2F</div><a class='author'>Fixture author</a>" +
            "<div class='tpc_content'><div id='read_102' class='f14'>" + reply + "</div></div></td></tr></table>") + "</body></html>";
    private static string Document(string buyers = "12", string nonce = "old") => Page(
        "<p>下载后解压。密码：a  b</p><a href='https://files.example.test/archive?verify=resource-token'>下载</a><p>下载信息：</p>" + Sale(buyers, nonce),
        "<p>自购证明：</p>" + Sale(buyers, nonce, "102", "999"));
    private static async Task<PostContent> Read(string html) => LegacyPostContentService.ToContent(
        await new SoulPlusContentFetcher(new Client(html)).ReadAsync(Url, default), "SoulPlus");
    private static PostContent Legacy(PostContent content) => content with
    {
        SourceEvidenceFingerprint = SoulPlusPostParser.LegacySnapshotFingerprint(content.Title, content.MainHtml,
            content.Comments.Select(c => new SoulPlusPostComment {Id = c.Id, Floor = c.Floor, Author = c.Author, Html = c.Html}).ToList()),
        ReaderEvidence = null
    };
    private static string Canonical(PostContent content)
    {
        Assert.IsTrue(new SoulPlusPostEvidenceCanonicalizer().TryCanonicalSourceFingerprint(content, out var hash));
        return hash;
    }

    [TestMethod]
    public async Task VerifiedLegacySnapshotAndFreshNonceCounterShareCanonicalEvidenceWithoutRewritingHistoryHash()
    {
        var old = Legacy(await Read(Document())); var original = old.SourceEvidenceFingerprint;
        var fresh = await Read(Document("94", "new"));
        Assert.AreNotEqual(old.SourceEvidenceFingerprint, fresh.SourceEvidenceFingerprint);
        Assert.AreEqual(Canonical(old), Canonical(fresh));
        Assert.AreEqual(original, old.SourceEvidenceFingerprint);
        Assert.IsNull(old.ReaderEvidence);
    }

    [TestMethod]
    public async Task FreshSourceHashIgnoresOnlySiteCaptionCounterAndPurchaseNonce()
    {
        var before = await Read(Document()); var after = await Read(Document("94", "new"));
        Assert.AreEqual(before.SourceEvidenceFingerprint, after.SourceEvidenceFingerprint);
        CollectionAssert.AreEqual(before.Locks.Select(l => l.PurchaseTargetId).ToArray(), after.Locks.Select(l => l.PurchaseTargetId).ToArray());
        Assert.AreNotEqual(before.Locks[0].Url, after.Locks[0].Url);
    }

    [TestMethod]
    public async Task LiteralAndNumericEntityCaptionInputsProduceTheSameProof()
    {
        var literal = await Read(Document());
        var numeric = await Read(Document().Replace("已有", "&#24050;&#26377;").Replace("人购买", "&#20154;&#36141;&#20080;"));
        Assert.AreEqual(Canonical(literal), Canonical(numeric));
    }

    [TestMethod]
    [DataRow("body")][DataRow("password")][DataRow("title")][DataRow("price")]
    [DataRow("download-token")][DataRow("floor")][DataRow("owner")][DataRow("author")]
    [DataRow("new-slot")][DataRow("payment-parameter")]
    public async Task MaterialChangesCannotReuseALegacySnapshot(string change)
    {
        var original = Document();
        var changed = change switch
        {
            "body" => original.Replace("下载后解压", "下载后重命名"),
            "password" => original.Replace("a  b", "a b"),
            "title" => original.Replace(">Fixture</h1>", ">Different</h1>"),
            "price" => original.Replace("售价 1 SP", "售价 2 SP"),
            "download-token" => original.Replace("resource-token", "another-token"),
            "floor" => original.Replace("B2F", "B3F"),
            "owner" => original.Replace("read_102", "read_103").Replace("pid=102", "pid=103"),
            "author" => original.Replace("Fixture author", "Another author"),
            "new-slot" => original.Replace("<p>自购证明：</p>", "<p>自购证明：</p>" + Sale(pid: "102")),
            _ => original.Replace("verify=old", "page=2&amp;verify=old")
        };
        Assert.AreNotEqual(Canonical(Legacy(await Read(original))), Canonical(await Read(changed)));
    }

    [TestMethod]
    [DataRow("plain")][DataRow("password")][DataRow("fake-caption")][DataRow("download-payment-url")]
    public async Task CounterPhrasesAndPaymentNoncesInAuthorContentRemainMaterial(string kind)
    {
        var protectedHtml = kind switch
        {
            "plain" => "<p>已有 12 人购买</p>",
            "password" => "<pre>密码：已有 12 人购买</pre>",
            "fake-caption" => "<h6 class='quote'><span class='s3'>此帖售价 1 SP币,已有 12 人购买</span></h6>",
            _ => "<a href='https://files.example.test/job.php?action=buytopic&amp;verify=resource-token'>下载</a>"
        };
        var original = Page(protectedHtml + "<p>下载：</p>" + Sale());
        var changed = Page(protectedHtml.Replace("12", "13").Replace("resource-token", "changed-token") + "<p>下载：</p>" + Sale());
        Assert.AreNotEqual(Canonical(Legacy(await Read(original))), Canonical(await Read(changed)));
    }

    [TestMethod]
    public async Task BoughtCaptionCounterIsVolatileButItsReadablePasswordIsNot()
    {
        const string sale = "<h6 class='quote'><span class='s3 f12 fn'>此帖售价 1 SP币,已有 12 人购买</span><pre>密码：已有 12 人购买</pre></h6>";
        var before = await Read(Page(sale));
        var counter = await Read(Page(sale.Replace("已有 12 人购买</span>", "已有 13 人购买</span>")));
        var password = await Read(Page(sale.Replace("密码：已有 12", "密码：已有 13")));
        Assert.AreEqual(Canonical(Legacy(before)), Canonical(counter));
        Assert.AreNotEqual(Canonical(Legacy(before)), Canonical(password));
    }

    [TestMethod]
    [DataRow("hash")][DataRow("lock-price")][DataRow("lock-floor")][DataRow("lock-target")]
    [DataRow("comment-list")][DataRow("source")][DataRow("missing-source")]
    public async Task UnauthenticatedOrInconsistentSnapshotsCannotProvideProof(string change)
    {
        var old = Legacy(await Read(Document()));
        old = change switch
        {
            "hash" => old with {SourceEvidenceFingerprint = new string('A', 64)},
            "lock-price" => old with {Locks = old.Locks.Select((l, i) => i == 0 ? l with {Price = 2} : l).ToList()},
            "lock-floor" => old with {Locks = old.Locks.Select((l, i) => i == 0 ? l with {Floor = "2"} : l).ToList()},
            "lock-target" => old with {Locks = old.Locks.Select((l, i) => i == 0 ? l with {PurchaseTargetId = "changed"} : l).ToList()},
            "comment-list" => old with {CommentHtmlList = ["Another comment"]},
            "source" => old with {SourceHint = "Other"},
            _ => old with {SourceEvidenceFingerprint = null}
        };
        Assert.IsFalse(new SoulPlusPostEvidenceCanonicalizer().TryCanonicalSourceFingerprint(old, out _));
    }

    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
    private sealed class Options : IBOptions<ISoulPlusOptions>
    {
        public ISoulPlusOptions Value { get; } = new SoulPlusOptions {Cookie = "synthetic", RequestInterval = 0};
    }
    private sealed class Client(string html) : SoulPlusClient(new Factory(), NullLoggerFactory.Instance, new Options())
    {
        protected override Task<(int StatusCode, string Text)> SendAsync(string url, Dictionary<string, string> headers, string preset) =>
            Task.FromResult((200, html));
    }
}

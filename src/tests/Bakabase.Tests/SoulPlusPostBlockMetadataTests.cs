using System;
using System.Linq;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

namespace Bakabase.Tests;

[TestClass]
public class SoulPlusPostBlockMetadataTests
{
    private const string SourceUrl = "https://www.north-plus.net/read.php?tid=42";

    // Synthetic markup follows the site's sale containers, with no saved account or post data.
    private static string Sale(string price, string nonce = "fixture-one", string buyers = "12", string pid = "tpc") =>
        $"<h6 class=\"quote jumbotron\"><span class=\"s3\">此帖售价 {price} SP币,已有 {buyers} 人购买</span>" +
        "<input type=\"button\" value=\"愿意购买,我买,我付钱\" " +
        $"onclick=\"location.href='job.php?action=buytopic&amp;tid=42&amp;pid={pid}&amp;verify={nonce}'\"/></h6>";

    private static string Page(string main, string reply = "", string title = "Fixture resource") =>
        $"<html><body><h1 id=\"subject_tpc\">{title}</h1><table><tr><td>" +
        "<div class=\"tiptop\">楼主</div><div class=\"tpc_content\"><div id=\"read_tpc\" class=\"f14\">" +
        main + "</div></div></td></tr></table>" +
        (reply.Length == 0 ? "" : "<table><tr><td><div class=\"tiptop\">B2F</div>" +
            "<div class=\"tpc_content\"><div id=\"read_102\" class=\"f14\">" + reply + "</div></div></td></tr></table>") +
        "</body></html>";

    [TestMethod]
    public void DifferentPricesAtOnePaymentEndpointKeepDistinctBlocksAndLocalPurposes()
    {
        var post = SoulPlusPostParser.Parse(Page("密码：" + Sale("1") +
            "<br/>如果你想要支持我：<br/>" + Sale("5")), SourceUrl);
        var blocks = post.LockedContents!.Where(b => !b.IsBought).ToList();

        Assert.AreEqual(2, blocks.Count);
        CollectionAssert.AreEqual(new[] {"0:block:0", "0:block:1"}, blocks.Select(b => b.BlockId).ToArray());
        CollectionAssert.AreEqual(new decimal?[] {1m, 5m}, blocks.Select(b => b.Price).ToArray());
        Assert.AreEqual(blocks[0].Url, blocks[1].Url);
        Assert.IsFalse(string.IsNullOrWhiteSpace(blocks[0].PurchaseTargetId));
        Assert.AreEqual(blocks[0].PurchaseTargetId, blocks[1].PurchaseTargetId);
        Assert.AreEqual("密码：", blocks[0].ContextText);
        Assert.AreEqual("如果你想要支持我：", blocks[1].ContextText);
    }

    [TestMethod]
    public void BlockContextCannotBorrowThePreviousSalePurpose()
    {
        var blocks = SoulPlusPostParser.Parse(Page("自购证明：" + Sale("999") +
            "<br/><!-- separation --><br/>" + Sale("1")), SourceUrl).LockedContents!;

        Assert.AreEqual("自购证明：", blocks[0].ContextText);
        Assert.IsNull(blocks[1].ContextText);
    }

    [TestMethod]
    public void InlineLabelMarkupIsReadButHiddenAndQuotedProofCannotBecomeContext()
    {
        var blocks = SoulPlusPostParser.Parse(Page("<font><span>自購證明：</span></font><br/>" + Sale("999") +
            "<blockquote>自购证明</blockquote>" + Sale("1") +
            "<span hidden>自购证明</span>" + Sale("2")), SourceUrl).LockedContents!;

        Assert.AreEqual("自購證明：", blocks[0].ContextText);
        Assert.IsNull(blocks[1].ContextText);
        Assert.IsNull(blocks[2].ContextText);
    }

    [TestMethod]
    public void BoughtContainerStillOccupiesItsOriginalOrdinal()
    {
        var before = SoulPlusPostParser.Parse(Page("密码：" + Sale("1") + "自购：" + Sale("999")), SourceUrl);
        var after = SoulPlusPostParser.Parse(Page("密码：<h6 class=\"quote jumbotron\">" +
            "<span class=\"s3 f12 fn\">您已经购买此帖</span><p>fixture-password</p></h6>" +
            "自购：" + Sale("999")), SourceUrl);

        Assert.AreEqual("0:block:1", before.LockedContents![1].BlockId);
        Assert.AreEqual(before.LockedContents[1].BlockId, after.LockedContents!.Single(b => !b.IsBought).BlockId);
        Assert.AreEqual("0:block:0", after.LockedContents.Single(b => b.IsBought).BlockId);
    }

    [TestMethod]
    public void BlockOrdinalsAreIndependentForEachFloor()
    {
        var blocks = SoulPlusPostParser.Parse(Page("密码：" + Sale("1"),
            "自购证明：" + Sale("999", pid: "102")), SourceUrl).LockedContents!;

        CollectionAssert.AreEqual(new[] {"0:block:0", "2:block:0"}, blocks.Select(b => b.BlockId).ToArray());
        Assert.AreNotEqual(blocks[0].PurchaseTargetId, blocks[1].PurchaseTargetId);
        Assert.AreEqual("自购证明：", blocks[1].ContextText);
    }

    [TestMethod]
    public void PaymentNonceAndBuyerCounterDoNotChangeEditorialEvidenceOrPaymentTarget()
    {
        var before = SoulPlusPostParser.Parse(Page("密码：" + Sale("1"), "自购：" + Sale("999", pid: "102")), SourceUrl);
        var after = SoulPlusPostParser.Parse(Page("密码：" + Sale("1", "fixture-two", "93"),
            "自购：" + Sale("999", "fixture-three", "94", "102")), SourceUrl);

        Assert.AreEqual(before.EvidenceFingerprint, after.EvidenceFingerprint);
        CollectionAssert.AreEqual(before.LockedContents!.Select(b => b.PurchaseTargetId).ToArray(),
            after.LockedContents!.Select(b => b.PurchaseTargetId).ToArray());
        Assert.AreNotEqual(before.LockedContents[0].Url, after.LockedContents[0].Url);
    }

    [TestMethod]
    public void SignificantWhitespaceInsideArchivePasswordInvalidatesEvidence()
    {
        var original = Page("<pre>解压密码：a  b</pre><p>自购证明：</p>" + Sale("999"));
        var changed = original.Replace("a  b", "a b");

        Assert.AreNotEqual(SoulPlusPostParser.Parse(original, SourceUrl).EvidenceFingerprint,
            SoulPlusPostParser.Parse(changed, SourceUrl).EvidenceFingerprint,
            "A password containing two spaces is different evidence from a password containing one space.");
    }

    [TestMethod]
    [DataRow("body")]
    [DataRow("password")]
    [DataRow("price")]
    [DataRow("download-query")]
    [DataRow("reply")]
    [DataRow("title")]
    public void EditorialAndDownloadCredentialChangesInvalidateEvidence(string change)
    {
        const string body = "<p>下载后解压。密码：fixture-password</p>" +
            "<a href=\"https://files.example.test/archive?verify=download-token\">下载</a>";
        var original = Page(body + Sale("1"), "补充说明：保留目录结构。", "Fixture resource");
        var changed = change switch
        {
            "body" => original.Replace("下载后解压", "下载后保持原样"),
            "password" => original.Replace("fixture-password", "different-password"),
            "price" => original.Replace("售价 1 SP币", "售价 2 SP币"),
            "download-query" => original.Replace("download-token", "different-download-token"),
            "reply" => original.Replace("保留目录结构", "移动到目标目录"),
            _ => original.Replace("Fixture resource", "Another fixture resource")
        };

        Assert.AreNotEqual(SoulPlusPostParser.Parse(original, SourceUrl).EvidenceFingerprint,
            SoulPlusPostParser.Parse(changed, SourceUrl).EvidenceFingerprint);
    }
}

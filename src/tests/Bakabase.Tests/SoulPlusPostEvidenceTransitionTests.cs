using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using Bakabase.Modules.ThirdParty.Helpers;
using CsQuery;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class SoulPlusPostEvidenceTransitionTests
{
    private const string Url = "https://www.north-plus.net/read.php?tid=42";
    private static string Sale(int price, string pid = "tpc", string nonce = "one", string extra = "") =>
        $"<h6 class='quote'><span class='s3'>此帖售价 {price} SP币,已有 1 人购买</span>" +
        $"<input type='button' value='愿意购买,我买,我付钱' onclick=\"location.href='job.php?action=buytopic&amp;tid=42&amp;pid={pid}&amp;verify={nonce}{extra}'\"/></h6>";
    private static string Bought(string text, int? price = null) =>
        "<h6 class='quote'><span class='s3 f12 fn'>" +
        (price == null ? "您已经购买此帖" : $"此帖售价 {price} SP币,已有 2 人购买") + $"</span><p>{text}</p></h6>";
    private static string Page(string main, string reply, string before = "<p>下载后解压。</p>", string floor = "2") =>
        "<html><body><h1 id='subject_tpc'>Fixture</h1><div class='tpc_content'><div id='read_tpc' class='f14'>" + before +
        main + "</div></div><table><tr><td><div class='tiptop'>B" + floor + "F</div><a class='author'>Fixture author</a>" +
        "<div class='tpc_content'><div id='read_102' class='f14'>自购证明：" + reply + "</div></div></td></tr></table></body></html>";
    private static string Before(string? reply = null) => Page("密码：" + Sale(5) + "说明：" + Sale(0), reply ?? Sale(999, "102"));
    private static string After(string? reply = null) => Page("密码：" + Bought("fixture-password", 5) + "说明：" + Bought("Keep the folder", 0), reply ?? Sale(999, "102"));

    private static async Task<PostContent> Read(string html)
    {
        var legacy = await new SoulPlusContentFetcher(new Client(html)).ReadAsync(Url, default);
        return LegacyPostContentService.ToContent(legacy, "SoulPlus");
    }

    private static string MainTarget(PostContent content) => content.ReaderEvidence!.Slots.First(s => s.Floor == "0").CanonicalTargetId;

    [TestMethod]
    public async Task ApprovedSharedTargetUnlockCarriesOnlyUnchangedReplySlotsWithoutChangingOldFingerprints()
    {
        var before = await Read(Before()); var after = await Read(After());
        Assert.IsNotNull(before.ReaderEvidence); Assert.IsNotNull(after.ReaderEvidence);
        Assert.AreEqual(before.ReaderEvidence.OutsideSlotsDigest, after.ReaderEvidence.OutsideSlotsDigest);
        Assert.AreNotEqual(before.SourceEvidenceFingerprint, after.SourceEvidenceFingerprint);
        Assert.AreNotEqual(PostContentReadinessPolicy.Fingerprint(before), PostContentReadinessPolicy.Fingerprint(after));
        Assert.AreEqual(PostContentReadinessPolicy.Fingerprint(before), PostContentReadinessPolicy.Fingerprint(before with {ReaderEvidence = null}));
        Assert.IsTrue(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after, [MainTarget(before)], out var transition));
        CollectionAssert.AreEqual(new[] {"2:block:0"}, transition!.UnchangedBlockIds.ToArray());
        CollectionAssert.AreEqual(new[] {"0:block:0", "0:block:1"}, transition.UnlockedBlockIds.ToArray());
        Assert.IsTrue(after.Locks.Where(l => l.IsBought).All(l => l.Price == null && l.PurchaseTargetId == null && l.ContextText == null),
            "Structured evidence must not alter the legacy whole-post fingerprint's bought-block metadata.");
    }

    [TestMethod]
    public async Task BoughtCaptionMayOmitPriceAndEndpointOnlyWhenReaderStillMatchesTheSameOwnerAndSlot()
    {
        var before = await Read(Before());
        var after = await Read(Page("密码：" + Bought("fixture-password") + "说明：" + Bought("Keep the folder"), Sale(999, "102")));
        Assert.IsTrue(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after, [MainTarget(before)], out _));
    }

    [TestMethod]
    public async Task NoncePageAndQueryOrderChangesDoNotAlterAnUnchangedTarget()
    {
        var before = await Read(Before());
        var reply = Sale(999, "102", "two", "&amp;page=2").Replace("tid=42&amp;pid=102", "pid=0102&amp;tid=042");
        var after = await Read(After(reply));
        Assert.AreNotEqual(before.Locks.Single(l => l.Floor == "2").PurchaseTargetId, after.Locks.Single(l => l.Floor == "2").PurchaseTargetId,
            "The established public identity retains its old exact-URL semantics.");
        Assert.AreEqual(before.ReaderEvidence!.Slots.Single(s => s.Floor == "2").CanonicalTargetId,
            after.ReaderEvidence!.Slots.Single(s => s.Floor == "2").CanonicalTargetId);
        Assert.IsTrue(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after, [MainTarget(before)], out _));
    }

    [TestMethod]
    [DataRow("body")]
    [DataRow("whitespace-password")]
    [DataRow("title")]
    [DataRow("reply-author")]
    [DataRow("price")]
    [DataRow("new-slot")]
    [DataRow("floor")]
    [DataRow("owner")]
    [DataRow("container-attribute")]
    [DataRow("same-context-hidden-change")]
    public async Task RealEvidenceChangesInvalidateCarryEvenWhenBlockIdsAndLocalPurposeStillMatch(string change)
    {
        var beforeHtml = Before(); var afterHtml = After();
        switch (change)
        {
            case "body": afterHtml = afterHtml.Replace("下载后解压", "下载后重命名"); break;
            case "whitespace-password": beforeHtml = Before().Replace("下载后解压。", "密码：a  b"); afterHtml = After().Replace("下载后解压。", "密码：a b"); break;
            case "title": afterHtml = afterHtml.Replace("<h1 id='subject_tpc'>Fixture", "<h1 id='subject_tpc'>Changed"); break;
            case "reply-author": afterHtml = afterHtml.Replace("Fixture author", "Another author"); break;
            case "price": afterHtml = afterHtml.Replace("售价 999 SP币", "售价 998 SP币"); break;
            case "new-slot": afterHtml = After(Sale(999, "102") + Sale(2, "102")); break;
            case "floor": afterHtml = afterHtml.Replace("B2F", "B3F"); break;
            case "owner": afterHtml = afterHtml.Replace("read_102", "read_103").Replace("pid=102", "pid=103"); break;
            case "container-attribute": afterHtml = afterHtml.Replace("<h6 class='quote'>", "<h6 class='quote' data-owner='changed'>"); break;
            case "same-context-hidden-change": afterHtml = After(Sale(999, "102").Replace("</h6>", "<span hidden>Changed source data</span></h6>")); break;
        }
        var before = await Read(beforeHtml); var after = await Read(afterHtml);
        Assert.IsFalse(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after, [MainTarget(before)], out _));
    }

    [TestMethod]
    public async Task UnchangedAlreadyReadableContentIsComparedInsteadOfBeingMaskedAway()
    {
        var before = await Read(Before(Bought("Existing password is a  b", 999)));
        var after = await Read(After(Bought("Existing password is a b", 999)));
        Assert.AreEqual(before.ReaderEvidence!.OutsideSlotsDigest, after.ReaderEvidence!.OutsideSlotsDigest);
        Assert.IsFalse(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after, [MainTarget(before)], out _));
    }

    [TestMethod]
    public async Task UnchangedReadableBuyerCounterIsNotEditorialEvidence()
    {
        var before = await Read(Before(Bought("Existing content", 999)));
        var after = await Read(After(Bought("Existing content", 999).Replace("已有 2 人购买", "已有 40 人购买")));
        Assert.IsTrue(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after, [MainTarget(before)], out _));
    }

    [TestMethod]
    [DataRow("partial")]
    [DataRow("unapproved-reply")]
    [DataRow("visible-price-increase")]
    [DataRow("visible-price-decrease")]
    [DataRow("unknown-first-price")]
    [DataRow("no-transition")]
    public async Task PurchaseTransitionMustBeCompleteAuthorizedAndConsistentWithTheReviewedPrice(string change)
    {
        var beforeHtml = Before(); var afterHtml = After();
        switch (change)
        {
            case "partial": afterHtml = Page("密码：" + Bought("fixture-password", 5) + "说明：" + Sale(0), Sale(999, "102")); break;
            case "unapproved-reply": afterHtml = After(Bought("Unapproved hidden content", 999)); break;
            case "visible-price-increase": afterHtml = afterHtml.Replace("售价 5 SP币", "售价 6 SP币"); break;
            case "visible-price-decrease": afterHtml = afterHtml.Replace("售价 5 SP币", "售价 4 SP币"); break;
            case "unknown-first-price": beforeHtml = beforeHtml.Replace("售价 5 SP币", "价格未知"); break;
            case "no-transition": afterHtml = beforeHtml; break;
        }
        var before = await Read(beforeHtml); var after = await Read(afterHtml);
        Assert.IsFalse(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after, [MainTarget(before)], out _));
    }

    [TestMethod]
    public async Task GenericOrHistoricalSnapshotsAndMalformedOwnershipStayConservative()
    {
        var before = await Read(Before()); var after = await Read(After());
        Assert.IsFalse(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before with {ReaderEvidence = null}, after, [MainTarget(before)], out _));
        Assert.IsFalse(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after with {ReaderEvidence = null}, [MainTarget(before)], out _));
        Assert.IsFalse(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(before, after, ["unapproved-target"], out _));
        var wrongOwner = await Read(Before().Replace("pid=102", "pid=103"));
        Assert.IsNull(wrongOwner.ReaderEvidence, "Payment pid must agree with the actual containing reply, not just the same block id.");
    }

    [TestMethod]
    public async Task SavedV1PendingPurchaseEvidenceSurvivesCaptionCounterChangesAndAuthorizedUnlock()
    {
        var before = await Read(Before());
        var sales = new CQ(Before())["h6.quote"].ToList();
        var slots = before.ReaderEvidence!.Slots.Select((slot, index) =>
        {
            // Freeze the previously shipped v1 rendering, independent of the new normalizer:
            // it replaced the full counter phrase, including its surrounding number spaces.
            var sale = sales[index].Cq().Clone();
            foreach (var button in sale.Find("input[value=\"愿意购买,我买,我付钱\"]"))
            {
                button.SetAttribute("onclick", "location.href='payment-target:" + slot.CanonicalTargetId + "'");
                var caption = button.Cq().Prev();
                caption.Text(Regex.Replace(caption.Text(), @"已有\s*\d+\s*人购买", "已有<人数>人购买"));
            }
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HtmlContentSnapshot.Capture(sale))));
            Assert.AreEqual(digest, slot.ContentDigest, "Normal site captions must retain the v1 slot digest bytes.");
            return slot with {ContentDigest = digest};
        }).ToList();
        var legacyHash = SoulPlusPostParser.LegacySnapshotFingerprint(before.Title, before.MainHtml,
            before.Comments.Select(c => new SoulPlusPostComment {Id = c.Id, Floor = c.Floor, Author = c.Author, Html = c.Html}).ToList());
        // Purchase journals deliberately retain only the source digest and reader slots, not full text.
        var pending = before with {MainHtml = "", CommentHtmlList = [], Comments = [], SourceEvidenceFingerprint = legacyHash,
            ReaderEvidence = before.ReaderEvidence with {ContentFingerprint = legacyHash, Slots = slots}};
        var after = await Read(After(Sale(999, "102", "new-nonce").Replace("已有 1 人购买", "已有 94 人购买")));
        Assert.IsTrue(PostContentEvidenceTransition.TryValidateAuthorizedUnlock(pending, after, [MainTarget(before)], out var transition));
        CollectionAssert.AreEqual(new[] {"2:block:0"}, transition!.UnchangedBlockIds.ToArray());
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

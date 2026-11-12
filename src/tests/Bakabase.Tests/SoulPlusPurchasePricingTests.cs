using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using PostContent = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.Tests;

[TestClass]
public sealed class SoulPlusPurchasePricingTests
{
    private const string PostUrl = "https://www.north-plus.net/read.php?tid-42.html";
    private const string MainUrl = "https://www.north-plus.net/job.php?action=buytopic&tid=42&pid=tpc&verify=fixture";
    private const string ReplyUrl = "https://www.north-plus.net/job.php?action=buytopic&tid=42&pid=102&verify=fixture";

    private static PostContent Content(params PostContentLock[] blocks) => new()
        {SourceHint = "SoulPlus", SourceUrl = PostUrl, Balance = 100, Locks = blocks.ToList()};
    private static PostContentLock Block(decimal? price, string url = MainUrl, string floor = "0") =>
        new(url, price, false) {Floor = floor};

    [TestMethod]
    public void ParsedDomOrderSetsEachTargetsFirstPriceWithoutChangingBlockPrices()
    {
        static string Paid(int price, string url) => $"<h6 class='quote'><span>售价 {price} SP币</span>" +
            $"<input value='愿意购买,我买,我付钱' onclick=\"location.href='{url}'\" /></h6>";
        var html = "<html><body><h1 id='subject_tpc'>Fixture</h1><div class='tpc_content'><div id='read_tpc' class='f14'>" +
            Paid(5, MainUrl) + Paid(0, MainUrl) + Paid(999999999, MainUrl) + "</div></div>" +
            "<table><tr><td><div class='tiptop'>B2F</div><div class='tpc_content'><div id='read_102' class='f14'>" +
            Paid(3, ReplyUrl) + Paid(1, ReplyUrl) + "</div></div></td></tr></table></body></html>";
        var parsed = SoulPlusPostParser.Parse(html, PostUrl);
        var content = Content(parsed.LockedContents!.Select(l => Block(l.Price, l.Url!, l.Floor!)).ToArray());

        var quote = PostParserPurchaseQuote.Create(content, 10, 0);

        Assert.AreEqual(8m, quote.EligibleTotal);
        CollectionAssert.AreEqual(new[] {MainUrl, ReplyUrl}, quote.EligibleLockUrls);
        CollectionAssert.AreEqual(new decimal?[] {5, 0, 999999999, 3, 1}, content.Locks.Select(l => l.Price).ToArray());
        Assert.IsTrue(content.Locks.All(l => !l.IsBought));
    }

    [TestMethod]
    public void OptionalFirstBlockStillPricesTheNeededPaymentTarget()
    {
        var content = Content(Block(7) with {BlockId = "proof", ContextText = "自购证明"},
            Block(2) with {BlockId = "credentials", ContextText = "下载密码"});
        content = PostContentReadinessPolicy.Apply(content, PostContentReadinessPolicy.FromAnalysis(content,
            [new() {BlockId = "proof", Relevance = "optional", Purpose = "purchaseProof", Evidence = ["自购证明"]}]));
        Assert.AreEqual("credentials", PostContentReadinessPolicy.BlockingLocks(content).Single().BlockId);

        var quote = PostParserPurchaseQuote.Create(content, 10, 0);

        Assert.AreEqual(7m, quote.EligibleTotal, "Do not quote the first remaining required block or the minimum.");
        Assert.AreEqual(0, PostParserPurchaseQuote.Create(content, 5, 0).EligibleLockUrls.Count);
    }

    [TestMethod]
    public void NoncePageAndQueryOrderDoNotSplitTheSameTargetOrChangeItsFirstUrl()
    {
        var otherUrl = MainUrl.Replace("verify=fixture", "verify=another&page=2");
        var reordered = "https://www.north-plus.net/job.php?pid=tpc&tid=42&action=buytopic&verify=third";
        var quote = PostParserPurchaseQuote.Create(Content(Block(5), Block(0, otherUrl), Block(999999999, reordered),
            Block(3, ReplyUrl, "2"), Block(1, ReplyUrl.Replace("pid=102", "pid=0102").Replace("tid=42", "tid=042"), "2")), 10, 0);

        CollectionAssert.AreEqual(new[] {MainUrl, ReplyUrl}, quote.EligibleLockUrls);
        Assert.AreEqual(8m, quote.EligibleTotal);
    }

    [TestMethod]
    public void CanonicalTargetWithContradictoryFloorsStaysOneUnknownQuote()
    {
        var otherUrl = ReplyUrl.Replace("verify=fixture", "verify=another&page=2");
        var quote = PostParserPurchaseQuote.Create(Content(Block(2, ReplyUrl, "2"), Block(2, otherUrl, "3")), 10, 0);
        Assert.AreEqual(0, quote.EligibleLockUrls.Count);
        Assert.AreEqual(1, quote.UnknownPriceCount);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(-1)]
    public void UnknownOrInvalidFirstPriceNeverFallsBackToALaterPrice(int? first)
    {
        var quote = PostParserPurchaseQuote.Create(Content(Block(first), Block(0), Block(1)), 10, 0);
        Assert.AreEqual(0, quote.EligibleLockUrls.Count);
        Assert.AreEqual(1, quote.UnknownPriceCount);
    }

    [TestMethod]
    public void ExplicitZeroFirstPriceIsOneFreeTargetWithoutBalance()
    {
        var quote = PostParserPurchaseQuote.Create(Content(Block(0), Block(999999999)) with {Balance = null}, 0, 1000);
        Assert.AreEqual(1, quote.EligibleLockUrls.Count);
        Assert.AreEqual(0m, quote.EligibleTotal);
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("foreign-source")]
    [DataRow("other-thread")]
    [DataRow("foreign-target")]
    [DataRow("other-action")]
    [DataRow("duplicate-pid")]
    [DataRow("missing-pid")]
    [DataRow("missing-floor")]
    [DataRow("mixed-floors")]
    [DataRow("main-pid-reply-floor")]
    [DataRow("reply-pid-main-floor")]
    [DataRow("zero-pid")]
    [DataRow("unknown-parameter")]
    public void UnverifiedSourceOrPaymentScopeRetainsConservativeConflictingPriceRule(string change)
    {
        var content = Content(Block(2), Block(5));
        content = change switch
        {
            "source" => content with {SourceHint = "Generic"},
            "foreign-source" => content with {SourceUrl = "https://example.test/read.php?tid=42"},
            "other-thread" => content with {SourceUrl = PostUrl.Replace("42", "43")},
            "foreign-target" => content with {Locks = content.Locks.Select(l => l with {Url = l.Url!.Replace("north-plus.net", "example.test")}).ToList()},
            "other-action" => content with {Locks = content.Locks.Select(l => l with {Url = l.Url!.Replace("buytopic", "buyother")}).ToList()},
            "duplicate-pid" => content with {Locks = content.Locks.Select(l => l with {Url = l.Url + "&pid=102"}).ToList()},
            "missing-pid" => content with {Locks = content.Locks.Select(l => l with {Url = l.Url!.Replace("&pid=tpc", "")}).ToList()},
            "missing-floor" => content with {Locks = content.Locks.Select(l => l with {Floor = null}).ToList()},
            "mixed-floors" => content with {Locks = [content.Locks[0], content.Locks[1] with {Floor = "2"}]},
            "main-pid-reply-floor" => content with {Locks = content.Locks.Select(l => l with {Floor = "2"}).ToList()},
            "reply-pid-main-floor" => content with {Locks = content.Locks.Select(l => l with {Url = ReplyUrl}).ToList()},
            "zero-pid" => content with {Locks = content.Locks.Select(l => l with {Url = l.Url!.Replace("pid=tpc", "pid=0")}).ToList()},
            "unknown-parameter" => content with {Locks = content.Locks.Select(l => l with {Url = l.Url + "&price=1"}).ToList()},
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        var quote = PostParserPurchaseQuote.Create(content, 10, 0);

        Assert.AreEqual(0, quote.EligibleLockUrls.Count);
        Assert.AreEqual(1, quote.UnknownPriceCount);
    }

    [TestMethod]
    public async Task PreviewAndExecutionUseTheSameFirstPriceAndPurchaseOnlyOnce()
    {
        var account = new Account {Content = Content(Block(5), Block(0), Block(999999999))};
        var quote = PostParserPurchaseQuote.Create(account.Content, 10, 0);
        var result = await account.Policy().PurchaseAsync(PostUrl, "SoulPlus", 10, 0,
            new Dictionary<string, decimal?> {[MainUrl] = quote.EligibleTotal}, enforceThreshold: true,
            maxTotalCost: quote.EligibleTotal, expectedAccountKeyHash: SharedContentPurchasePolicy.HashAccountKey(account.AccountKey));

        Assert.AreEqual(5m, quote.EligibleTotal);
        Assert.AreEqual(1, account.Purchases);
        Assert.AreEqual(95m, result.Content.Balance);
        CollectionAssert.AreEqual(new[] {MainUrl}, result.PurchasedUrls);
        Assert.IsTrue(result.Content.Locks.All(l => l.IsBought));
        Assert.AreEqual(0, (await account.Policy().PurchaseAsync(PostUrl, "SoulPlus", 10, 0)).PurchasedUrls.Count);
        Assert.AreEqual(1, account.Purchases);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CanonicalTargetIsPurchasedOnceAndFreshRemainingBlocksAreChecked(bool unlockAll)
    {
        var account = new Account {Content = Content(Block(5), Block(0, MainUrl + "&page=2")), UnlockAll = unlockAll};
        var quote = PostParserPurchaseQuote.Create(account.Content, 10, 0);
        var result = await account.Policy().PurchaseAsync(PostUrl, "SoulPlus", 10, 0,
            new Dictionary<string, decimal?> {[MainUrl] = quote.EligibleTotal}, enforceThreshold: true, maxTotalCost: quote.EligibleTotal);

        Assert.AreEqual(1, account.Purchases);
        Assert.AreEqual(95m, result.Content.Balance);
        Assert.AreEqual(unlockAll ? 1 : 0, result.PurchasedUrls.Count);
        Assert.AreEqual(unlockAll ? 0 : 1, result.Warnings.Count);
    }

    [TestMethod]
    [DataRow("increase")]
    [DataRow("unknown")]
    [DataRow("account")]
    [DataRow("reserve")]
    [DataRow("total")]
    public async Task FreshFirstQuoteAndExistingApprovalGuardsStillRejectSpending(string change)
    {
        var account = new Account {Content = Content(Block(5), Block(0))};
        var approvedAccount = SharedContentPurchasePolicy.HashAccountKey(account.AccountKey);
        account.OnRead = reads =>
        {
            if (reads != 2) return;
            if (change is "increase" or "unknown") account.Content = account.Content with
                {Locks = [account.Content.Locks[0] with {Price = change == "increase" ? 6 : null}, account.Content.Locks[1]]};
            if (change == "account") account.AccountKey = "changed-account";
            if (change == "reserve") account.Content = account.Content with {Balance = 9};
        };
        Task<SharedContentPurchaseResult> Purchase() => account.Policy().PurchaseAsync(PostUrl, "SoulPlus", 10, 5,
            new Dictionary<string, decimal?> {[MainUrl] = 5}, enforceThreshold: true,
            maxTotalCost: change == "total" ? 4 : 5, expectedAccountKeyHash: approvedAccount);

        if (change == "account") await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Purchase());
        else Assert.AreEqual(0, (await Purchase()).PurchasedUrls.Count);
        Assert.AreEqual(0, account.Purchases);
    }

    [TestMethod]
    public async Task InterruptedPostPurchaseReadRecoversWithoutRepeatingTheFirstBlockPurchase()
    {
        var account = new Account {Content = Content(Block(5), Block(0)), FailReadAfterPurchase = true};
        await Assert.ThrowsExceptionAsync<IOException>(() => account.Policy().PurchaseAsync(PostUrl, "SoulPlus", 10, 0));
        Assert.AreEqual(1, account.Purchases);
        account.FailReadAfterPurchase = false;

        var recovered = await account.Policy().PurchaseAsync(PostUrl, "SoulPlus", 10, 0);

        Assert.AreEqual(1, account.Purchases);
        Assert.AreEqual(0, recovered.PurchasedUrls.Count);
        Assert.IsTrue(recovered.Content.Locks.All(l => l.IsBought));
    }

    private sealed class Account : IPostContentService, ISharedContentPurchaser
    {
        public PostContent Content = null!;
        public int Purchases;
        public int Reads;
        public bool FailReadAfterPurchase;
        public bool UnlockAll;
        public Action<int>? OnRead;
        public string AccountKey { get; set; } = Guid.NewGuid().ToString();
        public PostParserSource Source => PostParserSource.SoulPlus;
        public bool CanRead(string reference, string? sourceHint = null) => true;
        public SharedContentPurchasePolicy Policy() => new(this, [this]);
        public Task<PostContent> ReadAsync(string reference, string? sourceHint = null, CancellationToken ct = default)
        {
            Reads++;
            OnRead?.Invoke(Reads);
            if (FailReadAfterPurchase && Purchases > 0) throw new IOException("Fixture read failure after purchase.");
            return Task.FromResult(Content);
        }
        public Task BuyAsync(string lockUrl, CancellationToken ct)
        {
            Purchases++;
            var price = Content.Locks.First(l => !l.IsBought && l.Url == lockUrl).Price;
            Content = Content with {Balance = Content.Balance - price,
                Locks = Content.Locks.Select(l => UnlockAll || l.Url == lockUrl ? l with {IsBought = true} : l).ToList()};
            return Task.CompletedTask;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;

namespace Bakabase.Tests;

[TestClass]
public class SharedContentPurchasePolicyTests
{
    [TestMethod]
    public async Task AutoPurchaseIsInclusiveAndKeepsTheMinimumBalance()
    {
        var account = new Account {Balance = 30};
        account.Parts.AddRange([new("cheap", 10, false), new("expensive", 11, false), new("unknown", null, false)]);
        var result = await account.Policy().PurchaseAsync("thread", "SoulPlus", 10, 20);
        CollectionAssert.AreEqual(new[] {"cheap"}, result.PurchasedUrls);
        Assert.AreEqual(20m, account.Balance);
        Assert.AreEqual(2, result.Content.Locks.Count(l => !l.IsBought));
    }

    [TestMethod]
    public async Task ManualApprovalOverridesThresholdButNeverReserveOrApprovedPrice()
    {
        var account = new Account {Balance = 30};
        account.Parts.AddRange([new("valid", 20, false), new("reserve", 10, false), new("increased", 6, false)]);
        var result = await account.Policy().PurchaseAsync("thread", "SoulPlus", 0, 10,
            new Dictionary<string, decimal?> { ["valid"] = 20, ["reserve"] = 10, ["increased"] = 5 });
        CollectionAssert.AreEqual(new[] {"valid"}, result.PurchasedUrls);
        Assert.AreEqual(10m, account.Balance);
        Assert.AreEqual(2, result.Warnings.Count);
    }

    [TestMethod]
    public async Task UnknownBalanceDoesNotAuthorizeSpendingEvenWithZeroReserve()
    {
        var account = new Account {Balance = null};
        account.Parts.Add(new("paid", 1, false));
        var result = await account.Policy().PurchaseAsync("thread", "SoulPlus", 10, 0);
        Assert.AreEqual(0, result.PurchasedUrls.Count);
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("balance")));
    }

    [TestMethod]
    public async Task FreeItemsWorkAtTheDefaultZeroThresholdWithoutAnUnknownBalanceBlockingThem()
    {
        var account = new Account {Balance = null};
        account.Parts.Add(new("free", 0, false));
        var result = await account.Policy().PurchaseAsync("thread", "SoulPlus", 0, 0);
        Assert.AreEqual("free", result.PurchasedUrls.Single());
    }

    [TestMethod]
    public async Task SeparatePolicyInstancesSerializePurchasesForTheSameAccount()
    {
        var account = new Account {Balance = 15, FilterByReference = true};
        account.Parts.AddRange([new("first", 10, false), new("second", 10, false)]);
        var results = await Task.WhenAll(account.Policy().PurchaseAsync("first", "SoulPlus", 10, 0),
            account.Policy().PurchaseAsync("second", "SoulPlus", 10, 0));
        Assert.AreEqual(1, results.Sum(r => r.PurchasedUrls.Count));
        Assert.AreEqual(5m, account.Balance);
    }

    [TestMethod]
    public async Task NewLocksAreNotIncludedInAnEarlierManualApproval()
    {
        var account = new Account {Balance = 50};
        account.Parts.AddRange([new("approved", 5, false), new("new", 5, false)]);
        var result = await account.Policy().PurchaseAsync("thread", "SoulPlus", 0, 0,
            new Dictionary<string, decimal?> {["approved"] = 5});
        CollectionAssert.AreEqual(new[] {"approved"}, result.PurchasedUrls);
        Assert.IsTrue(result.Content.Locks.Single(l => l.Url == "new").IsBought == false);
    }

    [TestMethod]
    public async Task OneClickApprovalKeepsTheConfiguredThresholdAndApprovedTotal()
    {
        var account = new Account {Balance = 100};
        account.Parts.AddRange([new("first", 5, false), new("second", 5, false), new("dear", 10, false)]);
        var result = await account.Policy().PurchaseAsync("thread", "SoulPlus", 5, 0,
            new Dictionary<string, decimal?> {["first"] = 5, ["second"] = 5, ["dear"] = 10},
            enforceThreshold: true, maxTotalCost: 5);

        CollectionAssert.AreEqual(new[] {"first"}, result.PurchasedUrls);
        Assert.AreEqual(95m, account.Balance);
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("total cost")));
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("automatic purchase limit")));
    }

    [TestMethod]
    public async Task SharedEndpointIsBoughtOnceAndConflictingPricesNeverAuthorizeSpending()
    {
        var account = new Account {Balance = 100};
        account.Parts.AddRange([new("shared", 5, false), new("shared", 5, false),
            new("conflict", 3, false), new("conflict", 4, false)]);
        var result = await account.Policy().PurchaseAsync("thread", "SoulPlus", 10, 0);

        CollectionAssert.AreEqual(new[] {"shared"}, result.PurchasedUrls);
        Assert.AreEqual(95m, account.Balance);
        Assert.AreEqual(2, result.Content.Locks.Count(l => !l.IsBought));
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("price is unknown")));
    }

    [TestMethod]
    public async Task RefreshAfterPurchaseSkipsOtherItemsUnlockedByTheSite()
    {
        var account = new Account {Balance = 100, UnlockAll = true};
        account.Parts.AddRange([new("first", 5, false), new("also-unlocked", 5, false)]);
        var result = await account.Policy().PurchaseAsync("thread", "SoulPlus", 5, 0);

        CollectionAssert.AreEqual(new[] {"first"}, result.PurchasedUrls);
        Assert.AreEqual(95m, account.Balance);
        Assert.IsTrue(result.Content.Locks.All(l => l.IsBought));
    }

    private sealed class Account : IPostContentService, ISharedContentPurchaser
    {
        public decimal? Balance;
        public bool FilterByReference;
        public bool UnlockAll;
        public List<PostContentLock> Parts { get; } = [];
        public string AccountKey { get; } = Guid.NewGuid().ToString();
        public PostParserSource Source => PostParserSource.SoulPlus;
        public SharedContentPurchasePolicy Policy() => new(this, [this]);
        public bool CanRead(string reference, string? sourceHint = null) => true;
        public async Task<PostContent> ReadAsync(string reference, string? sourceHint = null, CancellationToken ct = default)
        {
            await Task.Yield();
            return new() {SourceHint = "SoulPlus", Balance = Balance, Locks = Parts.Where(l => !FilterByReference || l.Url == reference).ToList()};
        }
        public async Task BuyAsync(string lockUrl, CancellationToken ct)
        {
            await Task.Yield();
            var index = Parts.FindIndex(l => l.Url == lockUrl);
            var part = Parts[index];
            if (Balance != null) Balance -= part.Price;
            for (var i = 0; i < Parts.Count; i++)
                if (UnlockAll || Parts[i].Url == lockUrl) Parts[i] = Parts[i] with {IsBought = true};
        }
    }
}

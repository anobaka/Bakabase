using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using PostContent = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserPurchaseQuoteTests
{
    [TestMethod]
    public void SharedPurchaseEndpointsAreQuotedOnceAndDifferentFloorsRemainSeparate()
    {
        var quote = PostParserPurchaseQuote.Create(new PostContent {Balance = 100, Locks =
        [
            new("job.php?action=buytopic&tid=42&pid=0", 5, false),
            new("job.php?action=buytopic&tid=42&pid=0", 5, false),
            new("job.php?action=buytopic&tid=42&pid=102", 6, false),
            new("bought", 30, true)
        ]}, 10, 0);

        Assert.AreEqual(11m, quote.EligibleTotal);
        Assert.AreEqual(2, quote.EligibleLockUrls.Count);
        Assert.AreEqual(0, quote.ExcludedCount);
    }

    [TestMethod]
    public void QuoteAppliesInclusiveThresholdAndCumulativeReserve()
    {
        var quote = PostParserPurchaseQuote.Create(new PostContent {Balance = 17, Locks =
        [new("first", 5, false), new("dear", 6, false), new("second", 5, false), new("third", 5, false)]}, 5, 7);

        CollectionAssert.AreEqual(new[] {"first", "second"}, quote.EligibleLockUrls);
        Assert.AreEqual(10m, quote.EligibleTotal);
        Assert.AreEqual(11m, quote.ExcludedTotal);
        Assert.AreEqual(2, quote.ExcludedCount);
        Assert.AreEqual(0, quote.UnknownPriceCount);
    }

    [TestMethod]
    public void ConflictingUnknownAndMissingEndpointQuotesAreExcluded()
    {
        var quote = PostParserPurchaseQuote.Create(new PostContent {Balance = 100, Locks =
        [
            new("conflict", 1, false), new("conflict", 2, false),
            new("unknown", null, false), new("negative", -1, false), new(null, 3, false)
        ]}, 10, 0);

        Assert.AreEqual(0m, quote.EligibleTotal);
        Assert.AreEqual(0, quote.EligibleLockUrls.Count);
        Assert.AreEqual(3m, quote.ExcludedTotal);
        Assert.AreEqual(4, quote.ExcludedCount);
        Assert.AreEqual(3, quote.UnknownPriceCount);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    public void UnknownBalanceOnlyAllowsFreeUnlocksWithoutAReserve(int reserve, int expectedEligible)
    {
        var quote = PostParserPurchaseQuote.Create(new PostContent {Locks =
            [new("free", 0, false), new("paid", 1, false)]}, 10, reserve);

        Assert.AreEqual(expectedEligible, quote.EligibleLockUrls.Count);
        Assert.AreEqual(0m, quote.EligibleTotal);
        Assert.AreEqual(1m, quote.ExcludedTotal);
    }

    [TestMethod]
    public void TaskQuoteUsesTheCurrentConfiguredLimitsWithoutChangingTheSnapshot()
    {
        var task = new PostParserTask {ContentSnapshot = new PostContent
            {Balance = 100, Locks = [new("lock", 5, false)]}};
        Assert.AreEqual(0, task.PurchaseQuote!.EligibleLockUrls.Count);
        task.AutoBuyThreshold = 5;
        Assert.AreEqual(5m, task.PurchaseQuote!.EligibleTotal);
        task.MinimumRemainingCoins = 96;
        Assert.AreEqual(0, task.PurchaseQuote!.EligibleLockUrls.Count);
        Assert.AreEqual(5m, task.PurchaseQuote!.ExcludedTotal);
    }
}

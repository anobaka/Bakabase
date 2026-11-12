using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>A conservative quote from the saved page; execution rechecks current prices and balance.</summary>
public sealed record PostParserPurchaseQuote
{
    public string? CurrencyCode { get; init; }
    public string? CurrencyName { get; init; }
    public string EligibleTotalText => EligibleTotal.ToString(CultureInfo.InvariantCulture);
    public string ExcludedTotalText => ExcludedTotal.ToString(CultureInfo.InvariantCulture);
    public List<string> EligibleLockUrls { get; init; } = [];
    public decimal EligibleTotal { get; init; }
    public decimal ExcludedTotal { get; init; }
    public int ExcludedCount { get; init; }
    public int UnknownPriceCount { get; init; }

    public static PostParserPurchaseQuote Create(Bakabase.Modules.PostParser.Models.Domain.PostContent content,
        decimal threshold, decimal minimumRemainingCoins)
    {
        var eligible = new List<string>();
        decimal total = 0, excludedTotal = 0;
        var excludedCount = 0;
        var unknownPriceCount = 0;
        var remaining = content.Balance;
        foreach (var offer in Offers(content))
        {
            var price = offer.Price;
            var affordable = price is >= 0 && threshold >= 0 && minimumRemainingCoins >= 0 &&
                price <= threshold && (price == 0 || remaining is { } balance && balance - price >= minimumRemainingCoins);
            if (!string.IsNullOrWhiteSpace(offer.Url) && affordable)
            {
                eligible.Add(offer.Url);
                total += price!.Value;
                remaining -= price.Value;
            }
            else
            {
                excludedCount++;
                if (price is >= 0) excludedTotal += price.Value;
                else unknownPriceCount++;
            }
        }
        return new() {CurrencyCode = content.CurrencyCode, CurrencyName = content.CurrencyName, EligibleLockUrls = eligible, EligibleTotal = total, ExcludedTotal = excludedTotal,
            ExcludedCount = excludedCount, UnknownPriceCount = unknownPriceCount};
    }

    /// <summary>One endpoint is one purchase; source policies define its quote without merging semantic blocks.</summary>
    internal static IEnumerable<PostContentLock> Offers(Bakabase.Modules.PostParser.Models.Domain.PostContent content, IReadOnlyList<PostContentLock>? blocking = null)
    {
        var needed = blocking ?? PostContentReadinessPolicy.BlockingLocks(content);
        var neededUrls = needed.Where(l => !string.IsNullOrWhiteSpace(l.Url)).Select(l => l.Url).ToHashSet(StringComparer.Ordinal);
        return content.Locks.Where(l => !l.IsBought)
            .Select((item, index) => (item, key: SharedContentPurchasePricing.TargetKey(content, item) ?? $"missing:{index}"))
            .GroupBy(l => l.key, StringComparer.Ordinal)
            .Where(group => group.Any(l => string.IsNullOrWhiteSpace(l.item.Url) ? needed.Contains(l.item) : neededUrls.Contains(l.item.Url)))
            .Select(group =>
            {
                // Quote the complete ordered group before relevance filtering can discard its first block.
                var blocks = group.Select(l => l.item).ToList();
                return blocks[0] with {Price = SharedContentPurchasePricing.Quote(content, blocks)};
            });
    }
}

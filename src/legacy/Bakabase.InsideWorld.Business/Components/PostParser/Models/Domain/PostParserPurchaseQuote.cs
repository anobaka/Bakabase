using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>A conservative quote from the saved page; execution rechecks current prices and balance.</summary>
public sealed record PostParserPurchaseQuote
{
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
                price <= threshold && (remaining is { } balance
                    ? balance - price >= minimumRemainingCoins
                    : price == 0 && minimumRemainingCoins == 0);
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
        return new() {EligibleLockUrls = eligible, EligibleTotal = total, ExcludedTotal = excludedTotal,
            ExcludedCount = excludedCount, UnknownPriceCount = unknownPriceCount};
    }

    /// <summary>One endpoint is one purchase, even when several blocks share it. Never guess between conflicting prices.</summary>
    internal static IEnumerable<PostContentLock> Offers(Bakabase.Modules.PostParser.Models.Domain.PostContent content) =>
        content.Locks.Where(l => !l.IsBought)
            .Select((item, index) => (item, key: string.IsNullOrWhiteSpace(item.Url) ? $"missing:{index}" : $"url:{item.Url}"))
            .GroupBy(l => l.key, StringComparer.Ordinal)
            .Select(group =>
            {
                var prices = group.Select(l => l.item.Price).Distinct().ToList();
                return group.First().item with {Price = prices.Count == 1 && prices[0] is >= 0 ? prices[0] : null};
            });
}

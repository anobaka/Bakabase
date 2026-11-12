using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

/// <summary>A source may define a payment unit differently from its individual content blocks.</summary>
internal interface ISharedContentPurchasePricing
{
    bool TryGetTargetKey(PostContent content, PostContentLock block, out string key);
    bool TryQuote(PostContent content, IReadOnlyList<PostContentLock> orderedBlocks, out decimal? price);
}

internal static class SharedContentPurchasePricing
{
    // Pure source policies also work on saved snapshots; neither quoting nor rendering performs I/O.
    private static readonly ISharedContentPurchasePricing[] Policies = [new SoulPlusPurchasePricing()];

    public static string? TargetKey(PostContent content, PostContentLock block)
    {
        foreach (var policy in Policies)
            if (policy.TryGetTargetKey(content, block, out var key)) return key;
        return string.IsNullOrWhiteSpace(block.Url) ? null : "url:" + block.Url;
    }

    public static decimal? Quote(PostContent content, IReadOnlyList<PostContentLock> orderedBlocks)
    {
        foreach (var policy in Policies)
            if (policy.TryQuote(content, orderedBlocks, out var price)) return price;

        // Unrecognized sources/targets retain the conservative shared-endpoint rule.
        var prices = orderedBlocks.Select(block => block.Price).Distinct().ToList();
        return prices.Count == 1 && prices[0] is >= 0 ? prices[0] : null;
    }
}

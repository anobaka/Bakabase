using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using PostContent = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

public record SharedContentPurchaseResult(PostContent Content, List<string> PurchasedUrls, List<string> Warnings);

/// <summary>Applies price and balance checks under one account lock. Site readers supply fresh authoritative values.</summary>
public class SharedContentPurchasePolicy(IPostContentService reader, IEnumerable<ISharedContentPurchaser> purchasers)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AccountLocks = new(StringComparer.Ordinal);

    public async Task<SharedContentPurchaseResult> PurchaseAsync(string reference, string? sourceHint,
        decimal threshold, decimal minimumRemainingCoins,
        IReadOnlyDictionary<string, decimal?>? approvedPrices = null, CancellationToken ct = default,
        bool enforceThreshold = false, decimal? maxTotalCost = null)
    {
        if (threshold < 0 || minimumRemainingCoins < 0) throw new ArgumentOutOfRangeException(nameof(threshold), "Purchase limits cannot be negative.");
        if (maxTotalCost < 0) throw new ArgumentOutOfRangeException(nameof(maxTotalCost), "The approved total cannot be negative.");
        var purchaser = purchasers.FirstOrDefault(p => string.Equals(p.Source.ToString(), sourceHint, StringComparison.OrdinalIgnoreCase));
        if (purchaser == null)
            return new(await reader.ReadAsync(reference, sourceHint, ct), [], ["No purchaser is available for this source."]);
        var accountKey = purchaser.AccountKey;
        var gate = AccountLocks.GetOrAdd(accountKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var content = await reader.ReadAsync(reference, sourceHint, ct);
            var purchased = new List<string>();
            var warnings = new List<string>();
            decimal spent = 0;
            var candidates = PostParserPurchaseQuote.Offers(content).Select(l => l.Url).ToList();
            foreach (var url in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(url)) { warnings.Add("Locked content has no purchase URL."); continue; }
                if (approvedPrices != null && !approvedPrices.ContainsKey(url)) continue;
                if (purchaser.AccountKey != accountKey)
                    throw new InvalidOperationException("The purchase account changed. Read the post again before buying.");
                content = await reader.ReadAsync(reference, sourceHint, ct);
                var part = PostParserPurchaseQuote.Offers(content).FirstOrDefault(l => l.Url == url);
                if (part == null) continue;
                if (part.Price is not { } price || price < 0)
                { warnings.Add("A locked item's price is unknown; it was not purchased."); continue; }
                if ((approvedPrices == null || enforceThreshold) && price > threshold)
                { warnings.Add($"A locked item costs {price}, above the automatic purchase limit {threshold}."); continue; }
                if (approvedPrices != null && (approvedPrices[url] is not { } approved || price > approved))
                { warnings.Add("An item's price is unknown or has increased since approval; review its new price before purchasing."); continue; }
                if (maxTotalCost is { } maximum && spent + price > maximum)
                { warnings.Add("This purchase would exceed the total cost approved for this request."); continue; }
                if (content.Balance is not { } balance)
                {
                    if (price > 0 || minimumRemainingCoins > 0)
                    { warnings.Add("The account balance could not be verified; no coins were spent."); continue; }
                }
                else if (balance - price < minimumRemainingCoins)
                { warnings.Add($"This purchase would leave fewer than {minimumRemainingCoins} coins in the account."); continue; }
                if (purchaser.AccountKey != accountKey)
                    throw new InvalidOperationException("The purchase account changed. Read the post again before buying.");
                // Never retry a purchase automatically after an ambiguous response: re-read first on the next user action.
                await purchaser.BuyAsync(url, ct);
                spent += price;
                content = await reader.ReadAsync(reference, sourceHint, ct);
                if (content.Locks.Any(l => !l.IsBought && l.Url == url))
                {
                    warnings.Add("The site still reports a purchased item as locked. Review it before retrying.");
                    break;
                }
                purchased.Add(url);
            }
            return new(content, purchased, warnings.Distinct().ToList());
        }
        finally { gate.Release(); }
    }
}

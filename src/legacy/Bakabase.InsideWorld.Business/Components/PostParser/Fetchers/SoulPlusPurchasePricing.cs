using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

/// <summary>SoulPlus buytopic charges once per post/reply, using its first sell block's price, including zero.</summary>
internal sealed class SoulPlusPurchasePricing : ISharedContentPurchasePricing
{
    private const string InvalidFloor = "soulplus:invalid-floor:";

    public bool TryGetTargetKey(PostContent content, PostContentLock block, out string key)
    {
        key = "";
        if (!string.Equals(content.SourceHint, "SoulPlus", StringComparison.OrdinalIgnoreCase) ||
            content.SourceUrl == null || !SoulPlusPostParser.IsSupportedUrl(content.SourceUrl) ||
            !Uri.TryCreate(content.SourceUrl, UriKind.Absolute, out var post) || post.AbsolutePath != "/read.php") return false;

        var sourceIds = Regex.Matches(post.Query.TrimStart('?'), @"(?:^|&)(?:tid=|tid-)(?<id>[0-9]+)(?:\.html)?(?=&|$|-)");
        if (sourceIds.Count != 1 || !PositiveId(sourceIds[0].Groups["id"].Value, out var threadId)) return false;

        if (!TryEndpoint(post, threadId, block.Url, out var target, out var mainPost)) return false;
        var floors = content.Locks.Where(l => !l.IsBought && TryEndpoint(post, threadId, l.Url, out var other, out _) && other == target)
            .Select(l => l.Floor).Distinct(StringComparer.Ordinal).ToList();
        // A single payment target appearing in contradictory floors is not two billable targets.
        // Keep it as one unknown quote, never select one of the inconsistent block prices.
        if (floors.Count != 1 || !int.TryParse(floors[0], NumberStyles.None, CultureInfo.InvariantCulture, out var floor) ||
            (mainPost ? floor != 0 : floor <= 0))
            key = InvalidFloor + target;
        else key = "soulplus:" + target + ":floor:" + floor.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryEndpoint(Uri post, long threadId, string? url, out string target, out bool mainPost)
    {
        target = "";
        mainPost = false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var purchase) ||
            !SoulPlusPostParser.IsSupportedUrl(purchase.AbsoluteUri) ||
            purchase.Scheme != post.Scheme || purchase.Authority != post.Authority ||
            purchase.AbsolutePath != "/job.php" || purchase.Fragment.Length != 0) return false;

        var query = HttpUtility.ParseQueryString(purchase.Query);
        // Only the known nonce and page parameters may differ for this payment target. An unknown
        // parameter could change purchase semantics, so leave that URL to the conservative fallback.
        if (query.AllKeys.Any(k => k is not ("action" or "tid" or "pid" or "verify" or "page")) ||
            query.AllKeys.Any(k => query.GetValues(k)?.Length != 1) ||
            query.GetValues("action") is not {Length: 1} actions || actions[0] != "buytopic" ||
            query.GetValues("tid") is not {Length: 1} tids || !PositiveId(tids[0], out var targetThread) || targetThread != threadId ||
            query.GetValues("pid") is not {Length: 1} pids) return false;

        // PHPWind treats only "tpc" as the main post. Reply IDs are not floor numbers; numeric zero
        // is not a main-post alias and stays outside this source-specific pricing rule.
        mainPost = pids[0] == "tpc";
        long replyId = 0;
        if (!mainPost && !PositiveId(pids[0], out replyId)) return false;
        target = purchase.GetLeftPart(UriPartial.Authority) + ":" + threadId.ToString(CultureInfo.InvariantCulture) + ":" +
            (mainPost ? "tpc" : replyId.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    public bool TryQuote(PostContent content, IReadOnlyList<PostContentLock> orderedBlocks, out decimal? price)
    {
        price = null;
        if (orderedBlocks.Count == 0 || !TryGetTargetKey(content, orderedBlocks[0], out var key) ||
            orderedBlocks.Any(block => !TryGetTargetKey(content, block, out var other) || other != key)) return false;
        if (key.StartsWith(InvalidFloor, StringComparison.Ordinal)) return true;

        // The caller supplies the complete unbought target group in saved DOM order, including
        // optional blocks. Never select a cheaper/later block or fill an unknown first price.
        price = orderedBlocks[0].Price is >= 0 ? orderedBlocks[0].Price : null;
        return true;
    }

    private static bool PositiveId(string? value, out long id) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}

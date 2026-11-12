using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Bakabase.Modules.ThirdParty.Helpers;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public static partial class SoulPlusPostParser
{
    private static string HashEvidence(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool SourceIdentity(string? url, out Uri post, out long tid)
    {
        post = null!; tid = 0;
        if (url == null || !IsSupportedUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out post!) ||
            post.AbsolutePath != "/read.php") return false;
        var ids = Regex.Matches(post.Query.TrimStart('?'), @"(?:^|&)(?:tid=|tid-)(?<id>[0-9]+)(?:\.html)?(?=&|$|-)");
        return ids.Count == 1 && PositiveId(ids[0].Groups["id"].Value, out tid);
    }

    private static bool PositiveId(string? value, out long id) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private static string? TargetIdentity(Uri post, long tid, string? pid, string? floor)
    {
        if (!int.TryParse(floor, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0) return null;
        if (pid == "tpc") { if (number != 0) return null; }
        else
        {
            if (number == 0 || !PositiveId(pid, out var replyId)) return null;
            pid = replyId.ToString(CultureInfo.InvariantCulture);
        }
        return HashEvidence(JsonSerializer.Serialize(new {Origin = post.GetLeftPart(UriPartial.Authority), Thread = tid, Post = pid, Floor = number}));
    }

    private static string? PurchaseTarget(string source, string? url, string? floor)
    {
        if (!SourceIdentity(source, out var post, out var tid) || !Uri.TryCreate(url, UriKind.Absolute, out var purchase) ||
            !IsSupportedUrl(purchase.AbsoluteUri) || purchase.Scheme != post.Scheme || purchase.Authority != post.Authority ||
            purchase.AbsolutePath != "/job.php" || purchase.Fragment.Length != 0) return null;
        var query = HttpUtility.ParseQueryString(purchase.Query);
        if (query.AllKeys.Any(k => k is not ("action" or "tid" or "pid" or "verify" or "page")) ||
            query.AllKeys.Any(k => query.GetValues(k)?.Length != 1) || query["action"] != "buytopic" ||
            !PositiveId(query["tid"], out var targetThread) || targetThread != tid) return null;
        return TargetIdentity(post, tid, query["pid"], floor);
    }

    private static string? OwnerPurchaseTarget(string source, CQ owner, string? floor)
    {
        var id = owner.Attr("id");
        if (owner.Length != 1 || owner.Parents(".tpc_content").Length == 0 || id == null ||
            !id.StartsWith("read_", StringComparison.Ordinal) || !SourceIdentity(source, out var post, out var tid)) return null;
        return TargetIdentity(post, tid, id[5..], floor);
    }

    private static decimal? SalePrice(string? text)
    {
        var match = Regex.Match(text ?? "", @"(?<price>\d+(?:\.\d+)?)\s*SP(?:币|幣)?", RegexOptions.IgnoreCase);
        return decimal.TryParse(match.Groups["price"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ? price : null;
    }

    private static bool BoughtMarker(IDomObject element) => element.Cq().Is(".s3.f12.fn") &&
        Regex.IsMatch(element.Cq().Text(), "购买|购买者|出售");

    private static SoulPlusPostEvidence? BuildReaderEvidence(CQ cq, SoulPlusPost post, Dictionary<IDomObject, string> blockIds)
    {
        if (!SourceIdentity(post.SourceUrl, out var source, out var tid) || post.EvidenceFingerprint == null ||
            post.LockedContents is not {Count: > 0 and <= 256}) return null;
        var containers = cq["h6.quote"].ToList();
        var slots = new List<SoulPlusPostEvidenceSlot>();
        var masked = new List<(int Index, string BlockId)>();
        for (var i = 0; i < containers.Count; i++)
        {
            var element = containers[i]; var sale = element.Cq();
            var buttons = sale.Find("input[value=\"愿意购买,我买,我付钱\"]").ToList();
            var markers = sale.Find(".s3.f12.fn").Where(BoughtMarker).ToList();
            if (buttons.Count == 0 && markers.Count == 0) continue;
            // Only the known top-level sale shape can authorize a transition. Nested/ambiguous
            // sale markup remains fully readable in snapshots, but cannot carry manual decisions.
            if (buttons.Count + markers.Count != 1 || sale.Parents("h6.quote").Length != 0 ||
                (buttons.Count == 1 ? buttons[0] : markers[0]).ParentNode != element ||
                !blockIds.TryGetValue(element, out var blockId)) return null;
            var owner = sale.Parents(".f14").First();
            var floor = Floor(sale);
            var target = OwnerPurchaseTarget(post.SourceUrl!, owner, floor);
            if (target == null || !int.TryParse(floor, NumberStyles.None, CultureInfo.InvariantCulture, out var floorNumber)) return null;
            var locked = post.LockedContents.Where(l => l.BlockId == blockId).ToList();
            if (locked.Count != 1 || locked[0].IsBought != (markers.Count == 1) ||
                buttons.Count == 1 && PurchaseTarget(post.SourceUrl!, locked[0].Url, floor) != target) return null;
            var attributes = element.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new {a.Key, a.Value}).ToList();
            var metadata = HashEvidence(JsonSerializer.Serialize(new {Block = blockId, Owner = owner.Attr("id"),
                Floor = floorNumber, Target = target, Tag = element.NodeName, Attributes = attributes,
                Context = PurposeContext(sale)}));
            slots.Add(new(blockId, owner.Attr("id"), floorNumber.ToString(CultureInfo.InvariantCulture), target,
                locked[0].PurchaseTargetId, markers.Count == 1,
                markers.Count == 1 ? SalePrice(markers[0].Cq().Text()) : locked[0].Price,
                metadata, SlotDigest(sale, post.SourceUrl!, floor!, target)));
            masked.Add((i, blockId));
        }
        if (slots.Count != post.LockedContents.Count || slots.Select(s => s.BlockId).Distinct().Count() != slots.Count) return null;
        var clone = cq.Clone(); var clonedSales = clone["h6.quote"].ToList();
        foreach (var (index, blockId) in masked)
            clonedSales[index].Cq().Html("<span data-bakabase-payment-slot=\"" + blockId + "\"></span>");
        var outside = JsonSerializer.Serialize(new {post.Title, Main = HtmlContentSnapshot.Capture(clone["#read_tpc"].First()),
            Comments = clone[".tpc_content>.f14:not(#read_tpc)"].Select(element => new
            {
                Id = element.GetAttribute("id"), Floor = Floor(element.Cq()),
                Author = Blank(Container(element.Cq()).Find("a[href*='showuid'],a[href*='uid='],.author").First().Text()),
                Html = HtmlContentSnapshot.Capture(element.Cq())
            }).ToList()});
        return new(HashEvidence(JsonSerializer.Serialize(new {Origin = source.GetLeftPart(UriPartial.Authority), Thread = tid})),
            post.EvidenceFingerprint, HashEvidence(outside), slots);
    }

    private static string SlotDigest(CQ sale, string source, string floor, string target)
    {
        var copy = sale.Clone();
        foreach (var button in copy.Find("input[value=\"愿意购买,我买,我付钱\"]"))
        {
            var onclick = button.GetAttribute("onclick") ?? "";
            onclick = Regex.Replace(onclick, "['\"](?<url>[^'\"]+)['\"]", match =>
                Uri.TryCreate(new Uri(source), WebUtility.HtmlDecode(match.Groups["url"].Value), out var url) &&
                PurchaseTarget(source, url.AbsoluteUri, floor) == target
                    ? match.Value.Replace(match.Groups["url"].Value, "payment-target:" + target, StringComparison.Ordinal) : match.Value);
            button.SetAttribute("onclick", onclick);
            NormalizeSaleCaption(button.Cq().Prev(), legacySlotRendering: true);
        }
        foreach (var marker in copy.Find(".s3.f12.fn").Where(BoughtMarker))
            NormalizeSaleCaption(marker.Cq(), legacySlotRendering: true);
        return HashEvidence(HtmlContentSnapshot.Capture(copy));
    }
}

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.Helpers;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public static partial class SoulPlusPostParser
{
    public const string EvidencePolicyId = "soulplus-evidence-v2";

    /// <summary>Reproduces the historical source hash; it never establishes equivalence by itself.</summary>
    public static string LegacySnapshotFingerprint(string title, string mainHtml, IReadOnlyList<SoulPlusPostComment> comments) =>
        HashEvidence(JsonSerializer.Serialize(new
        {
            Title = title, Main = LegacyStableEvidence(mainHtml),
            Comments = comments.Select(c => new {c.Id, c.Floor, c.Author, Html = LegacyStableEvidence(c.Html)})
        }));

    private static string CanonicalSnapshotFingerprint(string source, string title, string mainHtml,
        IReadOnlyList<SoulPlusPostComment> comments) => HashEvidence(JsonSerializer.Serialize(new
    {
        Policy = EvidencePolicyId, Source = source, Title = title,
        Main = NormalizeSaleEvidence(mainHtml, source, "read_tpc", "0"),
        Comments = comments.Select(c => new {c.Id, c.Floor, c.Author,
            Html = NormalizeSaleEvidence(c.Html, source, c.Id, c.Floor)})
    }));

    /// <summary>
    /// Authenticates a saved source snapshot using its original hash, then re-parses its payment
    /// metadata. Callers must compare those locks with the saved lock descriptors before using
    /// the canonical hash. No history hash or saved snapshot is changed by this method.
    /// </summary>
    public static bool TryCanonicalSnapshotFingerprint(string source, string title, string mainHtml,
        IReadOnlyList<SoulPlusPostComment> comments, string? savedFingerprint, out string fingerprint,
        out IReadOnlyList<SoulPlusPostLockedContent> parsedLocks)
    {
        fingerprint = ""; parsedLocks = [];
        if (!SourceIdentity(source, out _, out _) || savedFingerprint is not {Length: 64} ||
            !savedFingerprint.All(Uri.IsHexDigit) || mainHtml.Length == 0 || comments.Count > 2000 ||
            mainHtml.Length + comments.Sum(c => c.Html.Length) > 8 * 1024 * 1024 ||
            comments.Any(c => c.Id == null || !Regex.IsMatch(c.Id, @"\Aread_[0-9]+\z") ||
                !PositiveId(c.Id[5..], out _) || !int.TryParse(c.Floor, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var floor) || floor <= 0) ||
            comments.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != comments.Count) return false;
        var canonical = CanonicalSnapshotFingerprint(source, title, mainHtml, comments);
        if (savedFingerprint != canonical && savedFingerprint != LegacySnapshotFingerprint(title, mainHtml, comments)) return false;

        // Rebuild only the reader-owned container metadata already saved with each section.
        // HTML remains HTML; visible strings/attributes are encoded independently.
        var page = new StringBuilder("<html><body><h1 id='subject_tpc'>")
            .Append(WebUtility.HtmlEncode(title)).Append("</h1><div class='tpc_content'><div id='read_tpc' class='f14'>")
            .Append(mainHtml).Append("</div></div>");
        foreach (var c in comments)
            page.Append("<table><tr><td><div class='tiptop'>B").Append(c.Floor).Append("F</div><a class='author'>")
                .Append(WebUtility.HtmlEncode(c.Author ?? "")).Append("</a><div class='tpc_content'><div class='f14' id='")
                .Append(c.Id).Append("'>").Append(c.Html).Append("</div></div></td></tr></table>");
        page.Append("</body></html>");
        try
        {
            var parsed = Parse(page.ToString(), source);
            // Malformed fragments must not escape their saved owner or lose evidence in parsing.
            if (parsed.EvidenceFingerprint != canonical) return false;
            parsedLocks = parsed.LockedContents ?? [];
            fingerprint = canonical;
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }

    private static string NormalizeSaleEvidence(string html, string source, string? ownerId, string? floor)
    {
        var fragment = CQ.CreateFragment("<div></div>");
        fragment.Html(html);
        var target = ownerId?.StartsWith("read_", StringComparison.Ordinal) == true && SourceIdentity(source, out var post, out var tid)
            ? TargetIdentity(post, tid, ownerId[5..], floor) : null;
        if (target != null)
        {
            foreach (var element in fragment.Find("h6.quote"))
            {
                var sale = element.Cq();
                if (sale.Parents("h6.quote").Length != 0) continue;
                var buttons = sale.Find("input[value=\"愿意购买,我买,我付钱\"]").ToList();
                var markers = sale.Find(".s3.f12.fn").Where(BoughtMarker).ToList();
                if (buttons.Count + markers.Count != 1) continue;
                var control = buttons.Count == 1 ? buttons[0] : markers[0];
                if (control.ParentNode != element) continue;
                if (buttons.Count == 1)
                {
                    var button = buttons[0];
                    var onclick = button.GetAttribute("onclick") ?? "";
                    var matches = Regex.Matches(onclick, "['\"](?<url>[^'\"]+)['\"]").Where(m =>
                        Uri.TryCreate(new Uri(source), WebUtility.HtmlDecode(m.Groups["url"].Value), out var url) &&
                        PurchaseTarget(source, url.AbsoluteUri, floor) == target).ToList();
                    if (matches.Count != 1) continue;
                    var payment = matches[0];
                    var normalizedUrl = Regex.Replace(payment.Groups["url"].Value,
                        @"([?&](?:amp;)?verify=)[^&#]*", "$1<nonce>", RegexOptions.IgnoreCase);
                    var start = payment.Groups["url"].Index;
                    button.SetAttribute("onclick", onclick[..start] + normalizedUrl + onclick[(start + payment.Groups["url"].Length)..]);
                    NormalizeSaleCaption(button.Cq().Prev());
                }
                else NormalizeSaleCaption(markers[0].Cq());
            }
        }
        return HtmlContentSnapshot.Capture(fragment);
    }

    private static void NormalizeSaleCaption(CQ caption, bool legacySlotRendering = false)
    {
        if (caption.Length != 1 || !caption.Is("span.s3") || caption[0].ChildNodes.Any(n => n.NodeType != NodeType.TEXT_NODE)) return;
        var text = caption.Text();
        // A complete site-owned price/counter caption, not a phrase inside author prose or a
        // password. Parsing entities before this comparison also handles numeric HTML entities.
        var match = Regex.Match(text, @"\A(?<prefix>此帖售价\s+\d+(?:\.\d+)?\s*SP(?:币|幣)?\s*[,，]\s*已有\s*)\d+(?<suffix>\s*人购买[。.]?\s*)\z");
        if (match.Success) caption.Text(legacySlotRendering
            // Structured v1 purchase journals already contain this exact digest representation.
            // Keep their bytes compatible while restricting normalization to the trusted caption.
            ? Regex.Replace(text, @"已有\s*\d+\s*人购买", "已有<人数>人购买")
            : match.Groups["prefix"].Value + "<人数>" + match.Groups["suffix"].Value);
    }
}

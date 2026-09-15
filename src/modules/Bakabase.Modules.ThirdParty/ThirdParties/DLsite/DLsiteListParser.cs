using System.Text.RegularExpressions;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

/// <summary>One work as a circle or series page lists it.</summary>
public record DLsiteListedWork
{
    /// <summary>The work id — RJ / VJ / BJ — which is also its identity everywhere else.</summary>
    public string WorkId { get; set; } = null!;

    public string? Title { get; set; }

    public string? CoverUrl { get; set; }
}

/// <summary>
/// Reads the works out of a circle's or series' page.
/// <para>
/// Structural, like every other listing parser here: a work is a link carrying a product id.
/// DLsite reskins its listings regularly and the class names go with them, but a work link still
/// says which work it is.
/// </para>
/// </summary>
public static partial class DLsiteListParser
{
    public static List<DLsiteListedWork> Parse(string html)
    {
        var cq = new CQ(html);
        var works = new List<DLsiteListedWork>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in cq["a[href*='product_id']"])
        {
            var href = anchor.GetAttribute("href");

            if (string.IsNullOrWhiteSpace(href)) continue;

            var match = WorkIdRegex().Match(href);

            if (!match.Success) continue;

            var workId = match.Groups["id"].Value.ToUpperInvariant();

            if (!seen.Add(workId)) continue;

            var element = anchor.Cq();
            // A listing links each work several times — cover, title, "add to cart". The title
            // is whichever of them carries text or an image alt.
            var title = element.Text().Trim();
            var image = element.Find("img").FirstOrDefault();

            if (title.Length == 0 && image != null)
            {
                title = (image.GetAttribute("alt") ?? "").Trim();
            }

            works.Add(new DLsiteListedWork
            {
                WorkId = workId,
                Title = title.Length == 0 ? null : title,
                CoverUrl = CoverOf(image),
            });
        }

        // A second pass for the titles: the first link to a work is often the cover image, and
        // the one with the actual title comes later.
        foreach (var anchor in cq["a[href*='product_id']"])
        {
            var href = anchor.GetAttribute("href");
            var match = href == null ? Match.Empty : WorkIdRegex().Match(href);

            if (!match.Success) continue;

            var text = anchor.Cq().Text().Trim();

            if (text.Length == 0) continue;

            var work = works.FirstOrDefault(w =>
                w.WorkId.Equals(match.Groups["id"].Value, StringComparison.OrdinalIgnoreCase));

            if (work is {Title: null}) work.Title = text;
        }

        return works;
    }

    /// <summary>The next page of the listing, or null on the last one.</summary>
    public static string? FindNextPageUrl(string html, string pageUrl)
    {
        var cq = new CQ(html);

        foreach (var anchor in cq[".page_no a, .pagination a, a[rel='next']"])
        {
            var text = anchor.Cq().Text().Trim();

            if (text is not ("次へ" or "next" or "Next" or ">" or "»")) continue;

            var href = anchor.GetAttribute("href");

            if (string.IsNullOrWhiteSpace(href)) continue;

            var next = Uri.TryCreate(new Uri(pageUrl), href, out var absolute) ? absolute.ToString() : href;

            return next == pageUrl ? null : next;
        }

        return null;
    }

    private static string? CoverOf(IDomObject? image)
    {
        // Listings lazy-load: the real cover is in data-src while src is a placeholder.
        var url = image?.GetAttribute("data-src") ?? image?.GetAttribute("src");

        if (string.IsNullOrWhiteSpace(url)) return null;

        return url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url;
    }

    [GeneratedRegex(@"product_id/(?<id>[RVB]J\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WorkIdRegex();
}

using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

/// <summary>
/// Reads a board's thread list out of its HTML.
/// <para>
/// Deliberately structural rather than cosmetic: it looks for links to <c>read.php?tid=</c> and
/// takes their text, instead of depending on the class names of whatever skin the forum is
/// wearing. A layout change breaks a class-name parser silently and often; it does not move where
/// a thread link points.
/// </para>
/// </summary>
public static partial class SoulPlusListParser
{
    /// <summary>Threads on one list page, in the order the page shows them, deduplicated by id.</summary>
    public static List<SoulPlusThread> Parse(string html, string pageUrl)
    {
        var cq = new CQ(html);
        var threads = new List<SoulPlusThread>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var anchor in cq["a[href*='read.php']"])
        {
            var href = anchor.GetAttribute("href");

            if (string.IsNullOrWhiteSpace(href)) continue;

            var tid = TidRegex().Match(href);

            if (!tid.Success || !seen.Add(tid.Groups["tid"].Value)) continue;

            var title = anchor.Cq().Text().Trim();

            // A thread list is full of links that are not the title — "last reply", the page
            // numbers inside a long thread. The title is the one with text.
            if (title.Length == 0) continue;

            threads.Add(new SoulPlusThread
            {
                Tid = tid.Groups["tid"].Value,
                Title = title,
                Url = Absolute(href, pageUrl),
            });
        }

        return threads;
    }

    /// <summary>
    /// The next page's URL, or null on the last one.
    /// <para>
    /// Read from the pager's own "next" link where there is one; a forum that renders no pager at
    /// all is one page, which is the answer the caller needs anyway.
    /// </para>
    /// </summary>
    public static string? FindNextPageUrl(string html, string pageUrl)
    {
        var cq = new CQ(html);

        foreach (var anchor in cq["a"])
        {
            var text = anchor.Cq().Text().Trim();

            if (text is not ("下一页" or "下页" or "next" or "Next" or "»")) continue;

            var href = anchor.GetAttribute("href");

            if (string.IsNullOrWhiteSpace(href)) continue;

            var next = Absolute(href, pageUrl);

            // A "next" that points at the page you are on is how the last page says it is last.
            return next == pageUrl ? null : next;
        }

        return null;
    }

    private static string Absolute(string href, string pageUrl) =>
        Uri.TryCreate(new Uri(pageUrl), href, out var absolute) ? absolute.ToString() : href;

    [GeneratedRegex(@"read\.php\?.*?tid=(?<tid>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TidRegex();
}

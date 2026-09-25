using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sentry;

namespace Bakabase.Service.Components;

/// <summary>
/// Keeps signed URLs out of Sentry breadcrumbs. Information-level logs and every HttpClient request become
/// breadcrumbs, and a URL's query can carry secrets: signed CDN links (Bilibili's <c>upsig</c>/<c>deadline</c>/
/// <c>oi</c>/<c>mid</c>), <c>auth_key</c> tokens, API keys. Only scheme, host, port and path are kept.
/// </summary>
public static partial class SentryBreadcrumbScrubber
{
    /// <summary>Breadcrumb data keys that hold a URL (the HttpClient handler writes <c>url</c>).</summary>
    private static readonly HashSet<string> UrlDataKeys = ["url", "http.url", "to", "from"];

    /// <summary>
    /// <paramref name="url"/> without its query and fragment; anything that is not an absolute URL is returned
    /// with everything from the first <c>?</c> or <c>#</c> removed.
    /// </summary>
    public static string StripQueryAndFragment(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        var cut = url.IndexOfAny(['?', '#']);
        return cut < 0 ? url : url[..cut];
    }

    /// <summary>
    /// Every http(s) URL in <paramref name="text"/> with its query and fragment removed, protocol-relative ones
    /// (<c>//host/path?…</c>, the form Bilibili gives subtitle URLs in) included.
    /// </summary>
    public static string StripQueriesInText(string text) =>
        string.IsNullOrEmpty(text) ? text : UrlWithQuery().Replace(text, m => m.Groups["base"].Value);

    /// <summary>
    /// The breadcrumb to record instead of <paramref name="breadcrumb"/>: URL-valued data and URLs inside the
    /// message lose their query and fragment. Returns the same instance when nothing needed changing.
    /// </summary>
    public static Breadcrumb Scrub(Breadcrumb breadcrumb)
    {
        var message = breadcrumb.Message is { } m ? StripQueriesInText(m) : null;
        var dataChanged = false;
        Dictionary<string, string>? data = null;
        if (breadcrumb.Data != null)
        {
            data = new Dictionary<string, string>(breadcrumb.Data.Count);
            foreach (var (key, value) in breadcrumb.Data)
            {
                var scrubbed = value == null
                    ? value!
                    : UrlDataKeys.Contains(key)
                        ? StripQueryAndFragment(value)
                        : StripQueriesInText(value);
                dataChanged |= !string.Equals(scrubbed, value);
                data[key] = scrubbed;
            }
        }

        if (!dataChanged && string.Equals(message, breadcrumb.Message))
        {
            return breadcrumb;
        }

        return new Breadcrumb(message!, breadcrumb.Type!, dataChanged ? data : breadcrumb.Data, breadcrumb.Category,
            breadcrumb.Level);
    }

    [GeneratedRegex(@"(?<base>(?:https?:)?//[^\s?#""'<>]+)[?#][^\s""'<>]*", RegexOptions.IgnoreCase)]
    private static partial Regex UrlWithQuery();
}

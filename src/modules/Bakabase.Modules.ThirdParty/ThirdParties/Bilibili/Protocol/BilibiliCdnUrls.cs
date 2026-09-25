using System.Globalization;
using System.Web;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

/// <summary>
/// Signed CDN URLs (<c>upos-*.bilivideo.com</c>, regional nodes, PCDN on non-default ports). Their query carries
/// the user's IP (<c>oi</c>), <c>mid</c> and signatures: log and report them only through <see cref="Redact"/>.
/// </summary>
public static class BilibiliCdnUrls
{
    /// <summary>
    /// The absolute http(s) URLs among <paramref name="baseUrl"/> and <paramref name="backupUrls"/>,
    /// de-duplicated (first occurrence wins) and stably ordered by <see cref="HostRank"/>.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? baseUrl, IEnumerable<string>? backupUrls)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var urls = new List<(string Url, int Rank)>();
        foreach (var url in new[] {baseUrl}.Concat(backupUrls ?? []))
        {
            if (string.IsNullOrWhiteSpace(url) || !TryParseHttp(url, out var uri) || !seen.Add(url))
            {
                continue;
            }

            urls.Add((url, HostRank(uri)));
        }

        // OrderBy is stable: the original order is kept within a rank.
        return urls.OrderBy(u => u.Rank).Select(u => u.Url).ToList();
    }

    /// <summary>
    /// 0 <c>upos-*</c>; 1 other <c>*.bilivideo.com</c> (regional nodes); 2 any other host on the default
    /// port; 3 a non-default port or a PCDN host (<c>*.mcdn.bilivideo.cn</c>, <c>*.szbdyd.com</c>).
    /// No allow-list: new PCDN domains appear without notice.
    /// </summary>
    public static int HostRank(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        if (!uri.IsDefaultPort || host.EndsWith(".mcdn.bilivideo.cn", StringComparison.Ordinal) ||
            host.EndsWith(".szbdyd.com", StringComparison.Ordinal))
        {
            return 3;
        }

        if (host.StartsWith("upos-", StringComparison.Ordinal))
        {
            return 0;
        }

        return host.EndsWith(".bilivideo.com", StringComparison.Ordinal) ? 1 : 2;
    }

    /// <summary>
    /// The <c>deadline</c> query value (unix seconds), for diagnostics only. It is the server's clock; never
    /// compare it with the local clock to skip a URL that has not actually failed.
    /// </summary>
    public static DateTimeOffset? TryGetDeadline(string url)
    {
        if (!TryParseHttp(url, out var uri))
        {
            return null;
        }

        var value = HttpUtility.ParseQueryString(uri.Query)["deadline"];
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
               seconds is > 0 and < 253402300800
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    /// <summary>
    /// <c>scheme://host[:port]/…/{last segment}</c> — no query, no fragment, no other path segment. The only
    /// form in which a CDN, subtitle or cover URL may appear in logs, exceptions and messages.
    /// </summary>
    public static string Redact(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "(no url)";
        }

        var candidate = url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return "(invalid url)";
        }

        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = segments.Length switch
        {
            0 => "/",
            1 => "/" + segments[0],
            _ => "/…/" + segments[^1],
        };
        return $"{uri.Scheme}://{authority}{path}";
    }

    /// <summary>The lower-case extension of the path (".m4s", ".flv", ".mp4"…), or "".</summary>
    public static string GetExtension(string url) =>
        TryParseHttp(url, out var uri) ? Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() : "";

    /// <summary>The last path segment (e.g. "25540578-1-16.mp4"), or "".</summary>
    public static string GetFileName(string url) =>
        TryParseHttp(url, out var uri)
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? ""
            : "";

    private static bool TryParseHttp(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}

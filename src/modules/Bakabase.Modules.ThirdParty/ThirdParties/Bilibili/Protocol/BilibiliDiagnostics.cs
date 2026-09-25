using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

/// <summary>Makes Bilibili responses and messages safe to log (Debug/Warning diagnostics only).</summary>
public static partial class BilibiliDiagnostics
{
    /// <summary>
    /// A response body with every URL reduced to <see cref="BilibiliCdnUrls.Redact"/> (no query: no <c>oi</c>,
    /// <c>mid</c>, <c>upsig</c>, <c>auth_key</c>), <c>mid</c>/<c>oi</c>/<c>uid</c> numbers zeroed, truncated.
    /// </summary>
    public static string RedactJson(string? body, int maxLength = 2048)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "";
        }

        var redacted = UrlRegex().Replace(body, m => BilibiliCdnUrls.Redact(m.Value));
        redacted = IdRegex().Replace(redacted, m => $"\"{m.Groups[1].Value}\":0");
        return Truncate(redacted, maxLength);
    }

    /// <summary>A short text (e.g. Bilibili's <c>message</c>) with URLs redacted and the length capped.</summary>
    public static string RedactText(string? text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return Truncate(UrlRegex().Replace(text, m => BilibiliCdnUrls.Redact(m.Value)), maxLength);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(0, maxLength)] + "…";

    [GeneratedRegex(@"(https?:)?//[^""\s<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"""(mid|oi|uid)""\s*:\s*(""\d+""|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex IdRegex();
}

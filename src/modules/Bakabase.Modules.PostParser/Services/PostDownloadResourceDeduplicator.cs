using System.Text.RegularExpressions;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Merges repeated locations only when their credentials and file instructions agree.</summary>
public static class PostDownloadResourceDeduplicator
{
    public static List<PostDownloadResource> Deduplicate(IEnumerable<PostDownloadResource> resources)
    {
        var result = new List<PostDownloadResource>();
        var identities = new List<LinkIdentity>();
        foreach (var resource in resources)
        {
            var identity = Identify(resource.Link, resource.Code);
            if (identity.Key.Length == 0)
            {
                result.Add(resource);
                identities.Add(identity);
                continue;
            }
            var merged = false;
            for (var i = 0; i < result.Count; i++)
            {
                if (identity.Key != identities[i].Key || identity.HasConflictingCode || identities[i].HasConflictingCode ||
                    !TryMerge(result[i], identities[i], resource, identity, out var replacement)) continue;
                result[i] = replacement!;
                identities[i] = Identify(replacement!.Link, replacement.Code);
                merged = true;
                break;
            }
            if (!merged)
            {
                result.Add(resource);
                identities.Add(identity);
            }
        }
        return result;
    }

    private static bool TryMerge(PostDownloadResource first, LinkIdentity firstIdentity,
        PostDownloadResource next, LinkIdentity nextIdentity, out PostDownloadResource? merged)
    {
        merged = null;
        if (Conflicts(firstIdentity.Code, nextIdentity.Code) || Conflicts(first.Password, next.Password) ||
            !TryMergePlans(first.Extraction, next.Extraction, out var plan) ||
            (first.LinkHealth != null && next.LinkHealth != null &&
             (first.LinkHealth.Status != next.LinkHealth.Status || first.LinkHealth.Reason != next.LinkHealth.Reason)))
            return false;
        var health = first.LinkHealth == null || next.LinkHealth?.CheckedAt > first.LinkHealth.CheckedAt
            ? next.LinkHealth : first.LinkHealth;
        merged = first with
        {
            Code = Present(first.Code) ?? Present(next.Code) ?? firstIdentity.Code ?? nextIdentity.Code,
            Password = Present(first.Password) ?? Present(next.Password),
            Extraction = plan,
            LinkHealth = health
        };
        return true;
    }

    private static bool TryMergePlans(PostExtractionPlan? first, PostExtractionPlan? next,
        out PostExtractionPlan? merged)
    {
        merged = first ?? next;
        if (first == null || next == null) return true;
        if (first.Steps == null || next.Steps == null || first.Evidence == null || next.Evidence == null)
            return false;
        var firstIncomplete = first.Requirement == "unknown" && first.Steps.Count == 0;
        var nextIncomplete = next.Requirement == "unknown" && next.Steps.Count == 0;
        if (!firstIncomplete && !nextIncomplete &&
            (first.Requirement != next.Requirement || !first.Steps.SequenceEqual(next.Steps))) return false;
        var evidence = first.Evidence.Concat(next.Evidence).Distinct(StringComparer.Ordinal).ToList();
        if (evidence.Count > 32) return false;
        merged = (firstIncomplete ? next : first) with {Evidence = evidence};
        return true;
    }

    private sealed record LinkIdentity(string Key, string? Code, bool HasConflictingCode = false);

    private static LinkIdentity Identify(string? link, string? code)
    {
        var value = link?.Trim() ?? "";
        code = Present(code);
        var raw = new LinkIdentity(value, code);
        // Keep path, query order/encoding and fragments verbatim. URI serializers can rewrite these.
        var match = Regex.Match(value, @"^(https?)://([^/?#]+)([^?#]*)(\?[^#]*)?(#.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0)
            return raw;
        var scheme = match.Groups[1].Value.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        var query = match.Groups[4].Value;
        string? embeddedCode = null;
        if (host is "pan.baidu.com" or "yun.baidu.com" && query.Length > 0)
        {
            var parts = query[1..].Split('&');
            var passwords = parts.Select((part, index) => (Part: part, Index: index))
                .Where(p => p.Part.StartsWith("pwd=", StringComparison.Ordinal)).ToList();
            if (passwords.Count == 1)
            {
                embeddedCode = Present(Uri.UnescapeDataString(passwords[0].Part[4..].Replace('+', ' ')));
                if (embeddedCode != null)
                {
                    var remaining = parts.Where((_, index) => index != passwords[0].Index).ToList();
                    query = remaining.Count == 0 ? "" : "?" + string.Join('&', remaining);
                }
            }
        }
        return new LinkIdentity($"{scheme}://{host}{port}{match.Groups[3].Value}{query}{match.Groups[5].Value}",
            code ?? embeddedCode, Conflicts(code, embeddedCode));
    }

    private static bool Conflicts(string? first, string? next) =>
        Present(first) != null && Present(next) != null && first != next;

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

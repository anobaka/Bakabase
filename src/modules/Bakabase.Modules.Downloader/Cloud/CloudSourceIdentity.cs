using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Provider adapter identity; UI grouping and workflow origin are deliberately excluded.</summary>
public static class CloudSourceIdentity
{
    /// <summary>Exact resolved locator, credentials and account generation. Callers own the
    /// scope and authorization of any decision attached to this fingerprint.</summary>
    public static string Fingerprint(CloudSource source) => Hash(JsonSerializer.Serialize(new[] {JsonSerializer.Serialize(new
    {
        source.Url, source.Kind, source.ProviderId, source.MethodId, source.AccountId, source.AccountVersion,
        source.AccessCode, source.ArchivePassword
    })}));

    public static string Key(CloudSource source, string? accountId = null)
    {
        var locator = CanonicalLocator(source);
        if (source.AccountVersion != null || source.MethodScope != null)
            return Hash(JsonSerializer.Serialize(new[] {"source-v2", source.ProviderId, source.Kind,
                locator, source.Version ?? "", accountId ?? source.AccountId ?? "", source.MethodId ?? "",
                source.AccountVersion ?? "", source.MethodScope ?? "", Hash(source.AccessCode ?? "")}));
        return Hash(JsonSerializer.Serialize(new[] {"source-v1", source.ProviderId, source.Kind,
            locator, source.Version ?? "", accountId ?? source.AccountId ?? "", Hash(source.AccessCode ?? "")}));
    }

    public static string CanonicalLocator(CloudSource source)
    {
        if (source.Kind == "remote") return source.Url.TrimEnd('/');
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)) return source.Url;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).GroupBy(p => Uri.UnescapeDataString(p[0]), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First().ElementAtOrDefault(1) ?? ""));
        if (source.ProviderId == "googleDrive")
        {
            var match = Regex.Match(uri.AbsolutePath, @"/(folders|d)/([^/]+)");
            var id = match.Success ? match.Groups[2].Value : query.GetValueOrDefault("id");
            if (!string.IsNullOrWhiteSpace(id)) return "drive:" + id;
        }
        if (source.ProviderId == "gofile" && uri.AbsolutePath.StartsWith("/d/", StringComparison.Ordinal))
            return "gofile:" + uri.AbsolutePath[3..].TrimEnd('/');
        // Keep authorization/version parameters and selected list-item fragments. The hostname
        // aliases identify the same provider, but a share folder and an individual item do not.
        if (source.ProviderId is "pikpak" or "pixeldrain")
            return source.ProviderId + ":" + uri.AbsolutePath.TrimEnd('/') + uri.Query + uri.Fragment;
        if (source.ProviderId == "terabox" && TeraBoxShareLocator.TryParse(source.Url, out var teraBox))
            return "terabox:" + teraBox!.ShareId + uri.Query;
        if (source.ProviderId == "baidu")
        {
            if (uri.AbsolutePath.StartsWith("/s/", StringComparison.Ordinal)) return "baidu:" + uri.AbsolutePath[3..].TrimEnd('/');
            if (query.TryGetValue("surl", out var id)) return "baidu:1" + id;
        }
        // Signed query parameters and fragments can carry content/version/authorization. Preserve
        // them for unknown adapters rather than incorrectly treating two resources as identical.
        return uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

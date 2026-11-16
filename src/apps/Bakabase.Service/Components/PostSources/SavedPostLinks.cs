using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Service.Components.DownloadIntents;
using CsQuery;

namespace Bakabase.Service.Components.PostSources;

public sealed record SavedPostLink(string Url, string MethodId, int Floor, string? AccessCode = null);

internal static class PostSourceUrl
{
    internal static string? MatchMethod(string? url, bool shareOnly = false)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 || url.Any(char.IsWhiteSpace) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        try
        {
            if (uri.Scheme != "magnet") PublicCloudHttp.EnsureSafeUri(uri);
            var source = DownloadMethodRegistry.Normalize(new DownloadCandidate {Id = "post", Url = url});
            var method = DownloadMethodCatalog.Select(source);
            if (method == null || method.Availability != null || method.ValidateSource?.Invoke(source) != null) return null;
            if (shareOnly && !CloudSourceClassifier.IsCompleteShare(source)) return null;
            return method.Id;
        }
        catch (ArgumentException) { return null; }
    }
}

/// <summary>Locally read a bounded set of supported share links for explicit user review.</summary>
internal sealed partial class SavedPostLinks
{
    private readonly Dictionary<string, SavedPostLink> _links = new(StringComparer.Ordinal);
    private int _nodesLeft = 32000;
    private int _charactersLeft = 1024 * 1024;

    internal IReadOnlyList<SavedPostLink> Items => _links.Values.ToArray();

    internal void Collect(CQ document, int floor)
    {
        foreach (var node in document) Visit(node, 0);

        void Visit(IDomObject node, int depth)
        {
            if (_nodesLeft <= 0 || _charactersLeft <= 0 || _links.Count >= 64 || depth > 64) return;
            _nodesLeft--;
            if (node.NodeType == NodeType.TEXT_NODE)
            {
                var value = node.NodeValue ?? "";
                var length = Math.Min(value.Length, _charactersLeft);
                _charactersLeft -= length;
                foreach (var match in PlainUrl().EnumerateMatches(value.AsSpan(0, length)))
                {
                    if (_links.Count >= 64) break;
                    if (match.Length <= 2048 && !(length < value.Length && match.Index + match.Length == length))
                        Add(value.Substring(match.Index, match.Length).TrimEnd('.', ',', ';', '!', ')', ']'));
                }
                return;
            }
            if (node.NodeType != NodeType.ELEMENT_NODE) return;
            switch (node.NodeName.ToUpperInvariant())
            {
                case "A" when node.GetAttribute("href") is { } target:
                    Add(target.Trim());
                    return; // Ignore a misleading display URL or thumbnail in an anchor.
                case "IMG": case "SCRIPT": case "STYLE": case "TEMPLATE": case "NOSCRIPT":
                    return;
            }
            foreach (var child in node.ChildNodes) Visit(child, depth + 1);
        }

        void Add(string url)
        {
            if (PostSourceUrl.MatchMethod(url, shareOnly: true) is not { } method) return;
            var normalized = new Uri(url).AbsoluteUri;
            _links.TryAdd(normalized, new(url, method, floor, PostDownloadResourceDeduplicator.ReadEmbeddedAccessCode(url)));
        }
    }

    [GeneratedRegex("""(?:https?://|magnet:\?)[^\s<>"'`，。；、【】（）]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex PlainUrl();
}

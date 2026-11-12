using Bakabase.Modules.PostParser.Models.Domain;
using CsQuery;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Separates presentation images using captured source evidence, without fetching URLs or changing saved results.</summary>
public static class PostDownloadMedia
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, (PostDownloadMediaView View, int Size)> Cache = [];
    private static readonly Queue<string> CacheOrder = new();
    private static int _cacheSize;

    public static PostDownloadMediaView AnalyzeSaved(PostContent? content, PostDownloadInfo info)
    {
        // Version 4 is written only after separation. Ordinary list polling need not parse its HTML again.
        if (info.SchemaVersion >= 4 || info.Resources.Count == 0 || content == null)
            return new() {CoverCandidates = NormalizeCovers(info.CoverCandidates)};
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
        }
        Append(JsonSerializer.Serialize(content)); Append(JsonSerializer.Serialize(info));
        var key = Convert.ToHexString(hash.GetHashAndReset());
        lock (CacheGate) if (Cache.TryGetValue(key, out var cached)) return Copy(cached.View);
        var view = Analyze(content, info);
        // Limit both entry count and individual view size. No full source text is retained in this cache.
        var size = JsonSerializer.Serialize(view).Length * 2 + 2048;
        if (size <= 262144)
        {
            lock (CacheGate)
            {
                if (!Cache.ContainsKey(key))
                {
                    while (Cache.Count >= 2048 || _cacheSize + size > 32 * 1024 * 1024)
                    {
                        var expired = CacheOrder.Dequeue();
                        _cacheSize -= Cache[expired].Size; Cache.Remove(expired);
                    }
                    Cache[key] = (Copy(view), size); CacheOrder.Enqueue(key); _cacheSize += size;
                }
            }
        }
        return view;
    }

    private static PostDownloadMediaView Copy(PostDownloadMediaView view) => new()
    {ExcludedResourceIndices = view.ExcludedResourceIndices.ToList(), CoverCandidates = view.CoverCandidates
        .Select(c => c with {Evidence = c.Evidence.ToList()}).ToList()};

    private static List<PostCoverCandidate> NormalizeCovers(IEnumerable<PostCoverCandidate>? covers) =>
        (covers ?? []).Where(c => c != null && SafeUrl(c.Url) && c.Kind is "cover" or "preview")
            .DistinctBy(c => c.Url, StringComparer.Ordinal).Take(128)
            .Select(c => c with {Evidence = Evidence(c.Evidence, "", false)}).ToList();

    public static PostDownloadMediaView Analyze(PostContent? content, PostDownloadInfo info)
    {
        var images = new HashSet<string>(StringComparer.Ordinal);
        var independentLinks = new HashSet<string>(StringComparer.Ordinal);
        var text = new List<string>();
        var source = content == null ? Array.Empty<string>() : new[] {content.MainHtml}.Concat(content.Comments.Count > 0
            ? content.Comments.Select(c => c.Html) : content.CommentHtmlList).ToArray();
        foreach (var html in source)
        {
            if (content?.Scope == "pastedText") {text.Add(html); continue;}
            try
            {
                var document = CQ.CreateFragment(html);
                text.Add(document.Text());
                foreach (var node in DescendantsAndSelf(document))
                {
                    if (node.NodeType != NodeType.ELEMENT_NODE) continue;
                    if (node.NodeName.Equals("img", StringComparison.OrdinalIgnoreCase))
                        foreach (var name in new[] {"src", "data-original-src", "data-src", "data-original"})
                            if (node.GetAttribute(name) is { } value && SafeUrl(value)) images.Add(value);
                    // Image-only wrappers still describe the preview, not a download instruction.
                    if (node.NodeName.Equals("a", StringComparison.OrdinalIgnoreCase) &&
                        node.GetAttribute("href") is { } href && !string.IsNullOrWhiteSpace(CQ.Create(node).Text()))
                        independentLinks.Add(href);
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
            {text.Add(html);}
        }
        var body = string.Join("\n", text);
        var groups = (info.Groups ?? []).Where(g => g != null && g.Id != null)
            .GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
        var exclusions = new List<int>();
        var covers = new List<PostCoverCandidate>();
        foreach (var candidate in info.CoverCandidates ?? [])
        {
            if (candidate == null || !SafeUrl(candidate.Url) || candidate.Kind is not ("cover" or "preview")) continue;
            // The model cannot invent an image URL. Restored results without snapshots keep their
            // already saved metadata, but cannot create new exclusion decisions.
            if (content != null && !images.Contains(candidate.Url) && !independentLinks.Contains(candidate.Url) &&
                !body.Contains(candidate.Url, StringComparison.Ordinal)) continue;
            covers.Add(candidate with {Evidence = Evidence(candidate.Evidence, body, content != null)});
        }
        for (var index = 0; index < info.Resources.Count; index++)
        {
            var resource = info.Resources[index];
            if (resource == null || !images.Contains(resource.Link)) continue;
            var group = resource.GroupId == null ? null : groups.GetValueOrDefault(resource.GroupId);
            // An image suffix can carry an archive. Keep explicitly instructed operations, or an
            // independently supplied download link; extension or image size alone never decides.
            var instructed = resource.Extraction is {Requirement: "required" or "notRequired"} plan &&
                plan.Evidence?.Any(e => !string.IsNullOrWhiteSpace(e) && body.Contains(e, StringComparison.Ordinal)) == true;
            if (instructed || independentLinks.Contains(resource.Link) || body.Contains(resource.Link, StringComparison.Ordinal)) continue;
            exclusions.Add(index);
            covers.Add(new() {Url = resource.Link, Kind = "preview", Evidence = Evidence(group?.Evidence, body, true)});
        }
        return new()
        {
            ExcludedResourceIndices = exclusions,
            CoverCandidates = covers.DistinctBy(c => c.Url, StringComparer.Ordinal).Take(128).ToList()
        };
    }

    /// <summary>Only for newly generated results, before result identities have been persisted.</summary>
    public static PostDownloadInfo SeparateNewResult(PostContent content, PostDownloadInfo info)
    {
        var media = Analyze(content, info);
        var excluded = media.ExcludedResourceIndices.ToHashSet();
        var resources = info.Resources.Where((_, index) => !excluded.Contains(index)).ToList();
        var usedGroups = resources.Select(r => r.GroupId).ToHashSet();
        return info with {Resources = resources, Groups = info.Groups.Where(g => usedGroups.Contains(g.Id)).ToList(),
            CoverCandidates = media.CoverCandidates};
    }

    private static List<string> Evidence(IEnumerable<string>? values, string body, bool validate) =>
        (values ?? []).Where(e => !string.IsNullOrWhiteSpace(e) && e.Length <= 300 &&
            (!validate || body.Contains(e, StringComparison.Ordinal))).Distinct(StringComparer.Ordinal).Take(8).ToList();

    private static IEnumerable<IDomObject> DescendantsAndSelf(IEnumerable<IDomObject> roots)
    {
        var pending = new Stack<IDomObject>(roots.Reverse());
        while (pending.TryPop(out var node))
        {
            yield return node;
            if (node.ChildNodes is { } children)
                foreach (var child in children.Reverse()) pending.Push(child);
        }
    }

    public static bool SafeUrl(string? value) => value is {Length: > 0 and <= 2048} &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;
}

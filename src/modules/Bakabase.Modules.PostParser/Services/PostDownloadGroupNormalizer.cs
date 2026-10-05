using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Bounds AI grouping metadata without guessing content identity or dropping download locations.</summary>
public static class PostDownloadGroupNormalizer
{
    internal const int MaxGroups = 128;
    private const int MaxIdLength = 80;

    /// <summary>Normalizes metadata while retaining every original resource position for index-based imports.</summary>
    public static PostDownloadInfo Normalize(PostDownloadInfo result) => Normalize(result, result.Groups ?? []);

    internal static PostDownloadInfo Normalize(PostDownloadInfo result, IEnumerable<PostDownloadGroup?> groups)
    {
        var definitions = groups.Where(g => g != null)
            .Select(g => (Group: g!, Id: Identifier(g!.Id)))
            .Where(g => g.Id != null)
            .GroupBy(g => g.Id!, StringComparer.Ordinal);
        var referencedIds = result.Resources.Select(r => Identifier(r.GroupId)).Where(id => id != null)
            .ToHashSet(StringComparer.Ordinal);
        var normalized = new List<PostDownloadGroup>();
        foreach (var definition in definitions)
        {
            // Duplicate IDs are ambiguous even when their labels happen to match.
            if (definition.Count() != 1 || !referencedIds.Contains(definition.Key)) continue;
            var source = definition.Single().Group;
            var title = Text(source.Title, 160);
            if (title == null) continue;
            var kind = source.Kind?.Trim().ToLowerInvariant();
            normalized.Add(source with
            {
                Id = definition.Key,
                Title = title,
                Kind = kind is "main" or "preview" or "supplement" or "related" or "tool" ? kind : "unknown",
                Summary = Text(source.Summary, 500),
                Evidence = (source.Evidence ?? []).Select(e => Text(e, 300)).OfType<string>()
                    .Distinct(StringComparer.Ordinal).Take(8).ToList()
            });
            if (normalized.Count == MaxGroups) break;
        }
        var validIds = normalized.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return result with
        {
            Groups = normalized,
            Resources = result.Resources.Select(r => r with
            {
                GroupId = Identifier(r.GroupId) is { } id && validIds.Contains(id) ? id : null
            }).ToList()
        };
    }

    private static string? Identifier(string? value) =>
        value?.Trim() is {Length: > 0 and <= MaxIdLength} id ? id : null;

    private static string? Text(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length > maxLength ? text[..maxLength] : text;
    }
}

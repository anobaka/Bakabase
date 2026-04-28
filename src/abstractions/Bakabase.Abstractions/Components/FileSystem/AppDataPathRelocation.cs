using Bakabase.Abstractions.Extensions;

namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>
/// Pure-function path transformations used by <see cref="IAppDataPathRelocator"/>.
/// No I/O. No service dependencies. Directly unit-testable.
/// </summary>
public static class AppDataPathRelocation
{
    /// <summary>
    /// Resolve a stored path to a current absolute path.
    /// </summary>
    public static string? Resolve(string? stored, string currentRoot, IEnumerable<string?> additionalOldRoots)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (LooksLikeUrl(stored)) return stored;

        var current = currentRoot.StandardizePath()!.TrimEnd('/');
        var standardized = stored.StandardizePath()!;

        // Guard against non-path-shaped strings: opaque tokens (UUIDs, choice IDs, tag IDs)
        // never contain a separator. Treat them as values, not paths, and pass through.
        // Every AppData-relative path written by our helpers includes at least one separator
        // (e.g. "data/covers/..."), so this guard never excludes a legitimate path.
        if (!standardized.Contains('/')) return stored;

        if (!IsAbsolutePath(standardized))
        {
            return Path.Combine(current, standardized).StandardizePath();
        }

        foreach (var root in BuildCandidateRoots(currentRoot, additionalOldRoots))
        {
            if (TryStripPrefix(standardized, root, out var relative))
            {
                if (string.Equals(root, current, StringComparison.OrdinalIgnoreCase))
                {
                    return standardized;
                }

                return relative.Length == 0
                    ? current
                    : Path.Combine(current, relative).StandardizePath();
            }
        }

        return stored;
    }

    /// <summary>
    /// Convert an absolute path to AppData-relative form when it falls under any candidate root.
    /// Already-relative paths, URLs, and absolute paths outside all candidates are returned unchanged.
    /// </summary>
    public static string? Relativize(string? path, string currentRoot, IEnumerable<string?> additionalOldRoots)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (LooksLikeUrl(path)) return path;

        var standardized = path.StandardizePath()!;
        if (!IsAbsolutePath(standardized)) return path;

        foreach (var root in BuildCandidateRoots(currentRoot, additionalOldRoots))
        {
            if (TryStripPrefix(standardized, root, out var relative))
            {
                return relative;
            }
        }

        return path;
    }

    /// <summary>
    /// Velopack heuristic: ".../current/AppData[/...]" → ".../AppData[/...]" (case-insensitive).
    /// Removes the "/current" segment that immediately precedes the last "/AppData" segment.
    /// Returns null when no "/current/AppData" segment is present.
    /// </summary>
    public static string? TryStripCurrentSegment(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var standardized = path.StandardizePath()!;
        var idx = standardized.LastIndexOf("/current/AppData", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return standardized.Substring(0, idx) + standardized.Substring(idx + "/current".Length);
    }

    /// <summary>
    /// Builds the candidate-root set, normalized + deduplicated, sorted by length descending so
    /// that the longest matching prefix wins (e.g. "/a/b/c" before "/a/b").
    /// </summary>
    public static IReadOnlyList<string> BuildCandidateRoots(string currentRoot,
        IEnumerable<string?> additionalOldRoots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        Add(currentRoot);
        foreach (var r in additionalOldRoots) Add(r);

        list.Sort((a, b) => b.Length - a.Length);
        return list;

        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            var normalized = candidate.StandardizePath()!.TrimEnd('/');
            if (normalized.Length == 0) return;
            if (seen.Add(normalized)) list.Add(normalized);
        }
    }

    private static bool TryStripPrefix(string path, string prefix, out string relative)
    {
        if (path.Length < prefix.Length ||
            !path.AsSpan(0, prefix.Length).Equals(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            relative = "";
            return false;
        }

        if (path.Length == prefix.Length)
        {
            relative = "";
            return true;
        }

        var sep = path[prefix.Length];
        if (sep != '/' && sep != '\\')
        {
            relative = "";
            return false;
        }

        relative = path.Substring(prefix.Length + 1);
        return true;
    }

    private static bool LooksLikeUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cross-platform "absolute path" check on a standardized (forward-slash) path.
    /// Recognizes Unix roots ("/..."), UNC ("//server/..."), and Windows drives ("C:/...").
    /// </summary>
    private static bool IsAbsolutePath(string standardizedPath)
    {
        if (string.IsNullOrEmpty(standardizedPath)) return false;
        if (standardizedPath[0] == '/') return true;
        return standardizedPath.Length >= 2 && standardizedPath[1] == ':';
    }
}

using Bakabase.Remoting.Abstractions.Models;

namespace Bakabase.Remoting.Components.Paths;

/// <summary>The path on this machine, or the reason there is none.</summary>
public sealed record PathMappingResult(string? LocalPath, string? UnmappedServerPath)
{
    public bool Mapped => LocalPath != null;

    public static PathMappingResult Ok(string localPath) => new(localPath, null);

    public static PathMappingResult NotMapped(string? serverPath) => new(null, serverPath);
}

/// <summary>
/// Translates a path the server named into one this machine can open.
/// </summary>
/// <remarks>
/// <para>
/// The two sides of a split install genuinely disagree about where files are, and
/// nothing can infer the correspondence — only the person who mounted the share knows
/// it. So this is configuration, and a path with no mapping is refused by name rather
/// than guessed at: opening the wrong folder, or none, is worse than saying which
/// library has not been set up here.
/// </para>
/// <para>
/// Separators are translated too, not just prefixes. A container serving
/// <c>/data/media/x</c> and a desktop mounting it at <c>Z:\media</c> disagree about the
/// separator as well as the root, and half a translation produces a path that exists
/// nowhere.
/// </para>
/// </remarks>
public static class ClientPathMapper
{
    /// <summary>
    /// Maps <paramref name="serverPath"/> through the first mapping that covers it.
    /// </summary>
    /// <remarks>
    /// The longest matching prefix wins. Libraries nest — <c>/data</c> and
    /// <c>/data/media</c> can both be mapped, to different places — and taking the first
    /// match in list order would make the answer depend on what the user happened to add
    /// first.
    /// </remarks>
    public static PathMappingResult Map(string? serverPath, IEnumerable<ClientPathMapping> mappings)
    {
        if (string.IsNullOrWhiteSpace(serverPath))
        {
            return PathMappingResult.NotMapped(serverPath);
        }

        ClientPathMapping? best = null;
        string? bestRemainder = null;

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.ServerPath) || string.IsNullOrWhiteSpace(mapping.LocalPath))
            {
                continue;
            }

            var remainder = Remainder(serverPath, mapping.ServerPath);

            if (remainder == null)
            {
                continue;
            }

            if (best == null || mapping.ServerPath.Length > best.ServerPath.Length)
            {
                best = mapping;
                bestRemainder = remainder;
            }
        }

        if (best == null)
        {
            return PathMappingResult.NotMapped(serverPath);
        }

        return PathMappingResult.Ok(Join(best.LocalPath, bestRemainder!));
    }

    /// <summary>
    /// What is left of <paramref name="path"/> after <paramref name="prefix"/>, or null
    /// when the prefix does not cover it.
    /// </summary>
    /// <remarks>
    /// Compared segment by segment, not as text. A textual prefix check would have
    /// <c>/data/media</c> swallow <c>/data/media-backup/x</c> and send the user's opener
    /// into a directory that has nothing to do with the one they asked for.
    /// </remarks>
    private static string? Remainder(string path, string prefix)
    {
        var pathSegments = Split(path);
        var prefixSegments = Split(prefix);

        if (prefixSegments.Length == 0 || pathSegments.Length < prefixSegments.Length)
        {
            return null;
        }

        var comparison = IsWindowsStyle(prefix) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        for (var i = 0; i < prefixSegments.Length; i++)
        {
            if (!string.Equals(pathSegments[i], prefixSegments[i], comparison))
            {
                return null;
            }
        }

        return string.Join('/', pathSegments.Skip(prefixSegments.Length));
    }

    /// <summary>
    /// Appends a remainder to a local root, in the local root's own separator.
    /// </summary>
    private static string Join(string localRoot, string remainder)
    {
        var separator = SeparatorFor(localRoot);
        var root = localRoot.TrimEnd('/', '\\');

        // The mapped path is the root itself when the server named the library's own
        // folder rather than something inside it.
        if (remainder.Length == 0)
        {
            return root;
        }

        return root + separator + remainder.Replace('/', separator);
    }

    private static string[] Split(string path) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The separator the user wrote this root with, rather than the one this process
    /// happens to run under. It keeps the answer the same wherever the tests run, and it
    /// respects what the person configuring it clearly meant.
    /// </summary>
    private static char SeparatorFor(string localRoot) => IsWindowsStyle(localRoot) ? '\\' : '/';

    /// <summary>
    /// Whether a root is a Windows one: a drive letter, a UNC share, or simply written
    /// with backslashes. Decides both the separator and whether case matters, since
    /// those are the same question about the same filesystem.
    /// </summary>
    private static bool IsWindowsStyle(string path)
    {
        if (path.StartsWith('/'))
        {
            return false;
        }

        return path.Contains('\\') || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
    }
}

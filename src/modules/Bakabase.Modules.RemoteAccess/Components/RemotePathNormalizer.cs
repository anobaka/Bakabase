using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// Turns a client-supplied path into the single canonical form the guard compares
/// against, and answers containment questions about it.
/// </summary>
public static class RemotePathNormalizer
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Canonicalizes a path for comparison, or returns null when it cannot be one:
    /// empty, relative, containing a NUL, or otherwise rejected by the runtime.
    /// <para>
    /// <c>..</c> and <c>.</c> segments are collapsed here — that is the traversal
    /// vector reachable over HTTP. Symlinks are deliberately NOT followed: users
    /// legitimately assemble libraries out of symlinks and junctions, and following
    /// them would deny those setups, while exploiting one requires write access
    /// inside a media root, at which point remote read is not the interesting
    /// attack.
    /// </para>
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
        {
            return null;
        }

        var target = StripArchiveEntry(path);
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        // Rootedness is judged on the input, not on the result: Path.GetFullPath
        // resolves a relative path against the process's working directory, which
        // would turn "../../etc/passwd" into a perfectly rooted path pointing
        // wherever Bakabase happens to have been started from.
        if (!Path.IsPathRooted(target))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(target).StandardizePath();
        }
        catch
        {
            // Malformed for this platform (bad characters, too long, a Windows path
            // handed to a POSIX host, …) — not something we will serve.
            return null;
        }
    }

    /// <summary>
    /// Drops the <c>!entry/inside.jpg</c> suffix of the archive-entry path syntax the
    /// file and thumbnail endpoints accept, leaving the archive's own path. A path
    /// without that syntax comes back unchanged.
    /// </summary>
    public static string StripArchiveEntry(string path)
    {
        if (!path.Contains(InternalOptions.CompressedFileRootSeparator))
        {
            return path;
        }

        foreach (var compressedExt in InternalOptions.CompressedFileExtensions)
        {
            var pattern = $"{compressedExt}{InternalOptions.CompressedFileRootSeparator}";
            var idx = path.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                return path[..(idx + compressedExt.Length)];
            }
        }

        return path;
    }

    /// <summary>
    /// Whether an already-normalized <paramref name="candidate"/> is the same as, or
    /// sits beneath, an already-normalized <paramref name="root"/>. Comparison
    /// follows the host filesystem's case rules, so a case-sensitive filesystem does
    /// not let <c>/media/foo</c> pass as <c>/media/Foo</c>.
    /// </summary>
    public static bool IsUnder(string candidate, string root)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(root))
        {
            return false;
        }

        if (string.Equals(candidate, root, PathComparison))
        {
            return true;
        }

        // Anchor on the separator so /media/library-other does not match /media/library.
        var prefix = root.EndsWith(InternalOptions.DirSeparator)
            ? root
            : root + InternalOptions.DirSeparator;

        return candidate.StartsWith(prefix, PathComparison);
    }
}

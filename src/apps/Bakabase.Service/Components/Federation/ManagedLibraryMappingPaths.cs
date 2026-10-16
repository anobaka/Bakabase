using System;
using System.Linq;

namespace Bakabase.Service.Components.Federation;

/// <summary>Pure lexical source metadata. It never resolves links or probes source directories.</summary>
internal static class ManagedLibraryMappingPaths
{
    public const int MaximumPathChars = 4096;
    public static bool IsWindows(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
        path[2] is '/' or '\\' || path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("\\\\", StringComparison.Ordinal);

    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathChars || path.Contains('\0')) return null;
        var windows = IsWindows(path);
        var value = windows ? path.Replace('\\', '/') : path;
        if (!value.StartsWith('/') && !(windows && value.Length >= 3 && value[1] == ':')) return null;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(x => x is "." or "..")) return null;
        if (value.StartsWith("//", StringComparison.Ordinal) && segments.Length < 2) return null;
        if (value.Length > 2 && value[2..].Contains("//", StringComparison.Ordinal) || value.StartsWith("///", StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) && segments.Length < 2) return null;
        var trimmed = value.TrimEnd('/');
        if (trimmed.Length == 0) return "/";
        if (windows && trimmed.Length == 2 && trimmed[1] == ':') return trimmed + "/";
        return trimmed;
    }

    // Match the tree's OrdinalIgnoreCase collation, including Unicode pairs such as σ/ς.
    public static string Canonical(string path) => IsWindows(path) ? path.ToUpperInvariant() : path;
    public static bool Within(string path, string root) => Canonical(path) == Canonical(root) ||
        Canonical(path).StartsWith(Canonical(root).TrimEnd('/') + "/", StringComparison.Ordinal);
    public static string Name(string directory) => directory == "/" ? "/" :
        directory.TrimEnd('/').Split('/').Last();

}

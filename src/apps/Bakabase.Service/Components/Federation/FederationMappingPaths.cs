using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bakabase.Modules.Federation.Media;

namespace Bakabase.Service.Components.Federation;

/// <summary>Pure lexical source metadata. It never resolves links or probes source directories.</summary>
internal static class FederationMappingPaths
{
    public const int MaximumPathChars = 4096;
    public const long MaximumLocationBytes = 32 * 1024;
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
    public static string GroupId(string directory) => "g" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(directory)))).ToLowerInvariant();
    public static string Name(string directory) => directory == "/" ? "Shared folders" :
        directory.TrimEnd('/').Split('/').Last();

    public static string? Parent(string directory)
    {
        if (directory == "/" || directory.Length == 3 && directory[1] == ':') return null;
        var slash = directory.LastIndexOf('/');
        if (slash == 0) return "/";
        if (slash == 2 && directory[1] == ':') return directory[..3];
        if (slash < 0 || directory.StartsWith("//", StringComparison.Ordinal) && slash == directory.IndexOf('/', 2)) return null;
        return directory[..slash];
    }

    public static string FallbackAnchor(string directory)
    {
        if (IsWindows(directory) && directory[1] == ':') return directory[..3];
        var start = directory.StartsWith("//", StringComparison.Ordinal) ? 2 : 1;
        var first = directory.IndexOf('/', start);
        if (first < 0) return directory;
        var second = directory.IndexOf('/', first + 1);
        return second < 0 ? directory : directory[..second];
    }

    public static string Anchor(string directory, IReadOnlyList<string> storageRoots, IReadOnlyList<string> marks) =>
        storageRoots.FirstOrDefault(root => Within(directory, root)) ??
        marks.FirstOrDefault(root => Within(directory, root)) ?? FallbackAnchor(directory);

    public static FederatedMappingLocation[] Locations(string resourceRoot, string targetPath,
        IReadOnlyList<string> storageRoots, IReadOnlyList<string> marks, string? legacyRoot = null,
        string? legacyRelative = null, bool targetIsDirectory = false)
    {
        var root = Normalize(resourceRoot);
        var target = Normalize(targetPath);
        if (root == null || target == null || !Within(target, root)) return [];
        if (legacyRelative != null && !FederationPathMappingResolver.CanRepresentRelative(legacyRelative)) return [];
        var anchor = Anchor(root, storageRoots, marks);
        var result = new List<FederatedMappingLocation>();
        var bytes = 0L;
        for (var directory = targetIsDirectory ? target : Parent(target); directory != null && Within(directory, anchor); directory = Parent(directory))
        {
            // Excessively deep metadata retains legacy exact mapping/streaming, without rejecting the resource.
            if (result.Count >= FederationPathMappingResolver.MaximumLocations) return [];
            var relative = Canonical(target) == Canonical(directory) ? "." : target[(directory.TrimEnd('/').Length + 1)..];
            if (!FederationPathMappingResolver.CanRepresentRelative(relative)) return [];
            if (legacyRoot != null && Canonical(directory) == Canonical(root))
            {
                result.Add(new(legacyRoot, legacyRelative ?? relative));
                bytes += 128L + 2L * (legacyRoot.Length + (legacyRelative ?? relative).Length);
                if (result.Count >= FederationPathMappingResolver.MaximumLocations) return [];
            }
            result.Add(new(GroupId(directory), relative));
            bytes += 258L + 2L * relative.Length;
            if (bytes > MaximumLocationBytes) return [];
            if (Canonical(directory) == Canonical(anchor)) break;
        }
        return result.ToArray();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.Resolvers;

/// <summary>
/// Matches persisted resource paths against a resource mark without checking the filesystem.
/// Extension group references must be resolved into <see cref="ResourceMarkConfig.Extensions"/>
/// by the caller, just as they are before filesystem discovery.
/// </summary>
public static class ResourceMarkPathMatcher
{
    /// <summary>
    /// Prepares a mark once for matching many stored paths. Invalid regular expressions throw
    /// here, including when there are no candidate resources to examine.
    /// </summary>
    public static Func<string, bool, bool> Create(string markPath, ResourceMarkConfig config,
        IEnumerable<string>? boundaryPaths = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var root = Normalize(markPath);
        ArgumentException.ThrowIfNullOrEmpty(root, nameof(markPath));

        var scope = config.ApplyScope;
        var fsType = config.FsTypeFilter;
        var extensions = config.Extensions?.ToArray();
        var boundaries = (boundaryPaths ?? []).Select(Normalize)
            .Where(path => path.Length > 0 && IsUnder(path, root))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var layer = config.MatchMode == PathMatchMode.Layer ? config.Layer : null;
        string? layerRoot = root;
        if (layer is < 0)
        {
            for (var remaining = -(long) layer.Value; remaining > 0 && layerRoot != null; remaining--)
                layerRoot = ParentOf(layerRoot);
        }

        Regex? regex = null;
        if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
        {
            var pattern = config.Regex;
            if (scope == PathMarkApplyScope.MatchedOnly)
            {
                // The suffix can accidentally close a malformed character class ("[").
                // Validate the persisted pattern before applying discovery's adjustment.
                _ = new Regex(pattern);
                pattern = (pattern.EndsWith('$') ? pattern[..^1] : pattern) + @"[^/\\]*$";
            }
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        return (candidatePath, isFile) =>
        {
            var candidate = Normalize(candidatePath);
            if (candidate.Length == 0 ||
                scope is not (PathMarkApplyScope.MatchedOnly or PathMarkApplyScope.MatchedAndSubdirectories) ||
                !AcceptsType(isFile, fsType) ||
                (isFile && extensions is {Length: > 0} &&
                 !extensions.Any(extension => candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase))) ||
                boundaries.Any(boundary => IsEqualOrUnder(candidate, boundary)))
                return false;

            if (layer.HasValue)
            {
                // Discovery always starts at a directory. A zero/negative layer cannot select
                // a file, and a file-only rule cannot expand from that directory either.
                if (layer.Value <= 0)
                    return fsType != PathFilterFsType.File && layerRoot != null &&
                           ((!isFile && candidate.Equals(layerRoot, StringComparison.OrdinalIgnoreCase)) ||
                            (scope == PathMarkApplyScope.MatchedAndSubdirectories &&
                             IsUnder(candidate, layerRoot)));

                if (!IsUnder(candidate, root)) return false;
                var depth = RelativeTo(candidate, root).Count(character => character == '/') + 1;
                return depth == layer.Value ||
                       (scope == PathMarkApplyScope.MatchedAndSubdirectories &&
                        fsType != PathFilterFsType.File && depth > layer.Value);
            }

            if (regex == null || !IsUnder(candidate, root)) return false;
            if (regex.IsMatch(RelativeTo(candidate, root))) return true;
            if (scope != PathMarkApplyScope.MatchedAndSubdirectories || fsType == PathFilterFsType.File)
                return false;

            // A matching ancestor directory includes its descendants; the root itself is not
            // enumerated by the regex resolver and therefore cannot be an initial match.
            for (var parent = ParentOf(candidate); parent != null && IsUnder(parent, root); parent = ParentOf(parent))
                if (regex.IsMatch(RelativeTo(parent, root))) return true;

            return false;
        };
    }

    private static bool AcceptsType(bool isFile, PathFilterFsType? filter) => filter switch
    {
        null => true,
        PathFilterFsType.File => isFile,
        PathFilterFsType.Directory => !isFile,
        _ => false
    };

    private static bool IsEqualOrUnder(string candidate, string root) =>
        candidate.Equals(root, StringComparison.OrdinalIgnoreCase) || IsUnder(candidate, root);

    private static bool IsUnder(string candidate, string root)
    {
        var prefix = root.EndsWith('/') ? root : root + '/';
        return candidate.Length > prefix.Length && candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string RelativeTo(string candidate, string root) => candidate[root.Length..].TrimStart('/');

    private static string? ParentOf(string path)
    {
        var rootLength = RootLength(path);
        if (path.Length <= rootLength) return null;
        var separator = path.LastIndexOf('/');
        if (separator < 0) return null;
        return separator < rootLength ? path[..rootLength] : path[..separator];
    }

    private static int RootLength(string path)
    {
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            var serverEnd = path.IndexOf('/', 2);
            if (serverEnd < 0) return path.Length;
            var shareEnd = path.IndexOf('/', serverEnd + 1);
            return shareEnd < 0 ? path.Length : shareEnd;
        }

        if (path.Length >= 3 && path[1] == ':' && path[2] == '/') return 3;
        return path.StartsWith('/') ? 1 : 0;
    }

    private static string Normalize(string path)
    {
        // Use the same segment/trailing-space and case comparison rules as stored resource
        // paths, while keeping Windows drive/UNC roots independent of the current host OS.
        var slashed = path.Replace('\\', '/');
        var normalized = string.Join('/', slashed.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.TrimEnd()).Where(segment => segment.Length > 0));
        if (slashed.StartsWith("//", StringComparison.Ordinal)) return "//" + normalized;
        if (slashed.StartsWith('/')) return '/' + normalized;
        return normalized.EndsWith(':') ? normalized + '/' : normalized;
    }
}

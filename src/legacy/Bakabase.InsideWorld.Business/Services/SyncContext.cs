using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Abstractions.Models.Db;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Cached context for sync operations to avoid repeated database calls and computations
/// </summary>
internal class SyncContext
{
    public List<Resource> AllResources { get; set; } = new();
    public Dictionary<string, Resource> PathToResource { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExistingPathSet { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, Resource> IdToResource { get; } = new();

    // Caches for standardized paths
    public Dictionary<string, string> StandardizedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> PathSegments { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Regex cache
    public ConcurrentDictionary<string, Regex> RegexCache { get; } = new();

    // Batch updates
    public List<int> SuccessfulMarkIds { get; } = new();
    public List<(int MarkId, string Error)> FailedMarks { get; } = new();
    public List<CustomPropertyValueDbModel> PropertyValuesToAdd { get; } = new();
    public List<CustomPropertyValueDbModel> PropertyValuesToUpdate { get; } = new();

    // Pre-loaded property values for batch processing
    public Dictionary<(int ResourceId, int PropertyId), CustomPropertyValueDbModel> ExistingPropertyValues { get; } = new();

    public void BuildIndexes()
    {
        PathToResource.Clear();
        ExistingPathSet.Clear();
        IdToResource.Clear();

        foreach (var resource in AllResources)
        {
            var standardizedPath = GetStandardizedPath(resource.Path);
            if (!string.IsNullOrEmpty(standardizedPath))
            {
                PathToResource[standardizedPath] = resource;
                ExistingPathSet.Add(standardizedPath);
            }
            IdToResource[resource.Id] = resource;
        }
    }

    public string GetStandardizedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        if (!StandardizedPaths.TryGetValue(path, out var standardized))
        {
            standardized = path.StandardizePath()!;
            StandardizedPaths[path] = standardized;
        }
        return standardized;
    }

    public string[] GetPathSegments(string path)
    {
        var standardized = GetStandardizedPath(path);
        if (string.IsNullOrEmpty(standardized)) return Array.Empty<string>();

        if (!PathSegments.TryGetValue(standardized, out var segments))
        {
            segments = standardized.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            PathSegments[standardized] = segments;
        }
        return segments;
    }

    public Regex GetOrCreateRegex(string pattern)
    {
        return RegexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled));
    }

    /// <summary>
    /// Check if childPath is under parentPath (or equal to it)
    /// </summary>
    public bool IsPathUnderParent(string childPath, string parentPath)
    {
        var normalizedChild = GetStandardizedPath(childPath);
        var normalizedParent = GetStandardizedPath(parentPath);

        if (string.IsNullOrEmpty(normalizedChild) || string.IsNullOrEmpty(normalizedParent))
            return false;

        return normalizedChild.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedChild.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}

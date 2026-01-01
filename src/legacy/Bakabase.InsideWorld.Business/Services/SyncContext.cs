using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
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

    // Batch status updates
    public List<int> SuccessfulMarkIds { get; } = new();
    public List<(int MarkId, string Error)> FailedMarks { get; } = new();

    // Pre-loaded media library cache for dynamic mode (name -> id)
    public Dictionary<string, int> MediaLibraryCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Resource boundary paths (paths where IsResourceBoundary = true)
    // Key: boundary path, Value: mark id that defines this boundary
    public Dictionary<string, int> ResourceBoundaryPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    #region PathMark Effect Tracking (Effect-Centric Flow)

    // ===== Phase 1: Collect Effects =====
    // New effects collected during this sync (what marks WANT to do)
    public List<ResourceMarkEffect> CollectedResourceEffects { get; } = new();
    public List<PropertyMarkEffect> CollectedPropertyEffects { get; } = new();

    // Old effects loaded at start of sync (for diff calculation)
    // Key: MarkId
    public Dictionary<int, List<ResourceMarkEffect>> OldResourceEffectsByMarkId { get; } = new();
    public Dictionary<int, List<PropertyMarkEffect>> OldPropertyEffectsByMarkId { get; } = new();

    // ===== Phase 2: Compute Final State =====
    // Final computed property values after combining all effects
    // Key: (ResourceId, PropertyPool, PropertyId), Value: serialized combined value
    public Dictionary<(int ResourceId, PropertyPool Pool, int PropertyId), string?> FinalPropertyValues { get; } = new();

    // Final computed resource paths (paths that should exist as resources)
    // Key: standardized path, Value: list of mark IDs that want this resource
    public Dictionary<string, List<int>> FinalResourcePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Final computed media library mappings
    // Key: ResourceId, Value: list of MediaLibraryIds
    public Dictionary<int, HashSet<int>> FinalMediaLibraryMappings { get; } = new();

    // ===== Phase 3: Apply Changes =====
    // Resources to create (path -> resource data)
    public Dictionary<string, Resource> ResourcesToCreate { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Resources to delete (paths no longer needed)
    public HashSet<string> ResourcePathsToDelete { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Property values to write/update (already combined)
    public List<CustomPropertyValueDbModel> FinalPropertyValuesToWrite { get; } = new();

    // Property values to delete (no longer have any effects)
    public List<(int ResourceId, int PropertyId)> PropertyValuesToDelete { get; } = new();

    // Media library mappings to ensure
    public List<(int ResourceId, int MediaLibraryId)> FinalMappingsToEnsure { get; } = new();

    // Media library mappings to delete (no longer have any effects)
    public List<(int ResourceId, int MediaLibraryId)> MediaLibraryMappingsToDelete { get; } = new();

    // ===== Phase 4: Persist Effects =====
    // Effects to add to DB (new ones)
    public List<ResourceMarkEffect> ResourceEffectsToAdd { get; } = new();
    public List<PropertyMarkEffect> PropertyEffectsToAdd { get; } = new();

    // Effects to delete from DB (stale ones)
    public List<int> ResourceEffectIdsToDelete { get; } = new();
    public List<int> PropertyEffectIdsToDelete { get; } = new();

    // ===== Tracking =====
    // Resources affected by effect changes
    public HashSet<int> AffectedResourceIds { get; } = new();

    // Paths affected by resource effect changes
    public HashSet<string> AffectedResourcePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Effect keys for current sync (used for diff detection)
    public HashSet<(int MarkId, PropertyPool Pool, int PropertyId, int ResourceId)> CurrentPropertyEffectKeys { get; } = new();
    public HashSet<(int MarkId, string Path)> CurrentResourceEffectKeys { get; } = new(new ResourceEffectKeyComparer());

    private class ResourceEffectKeyComparer : IEqualityComparer<(int MarkId, string Path)>
    {
        public bool Equals((int MarkId, string Path) x, (int MarkId, string Path) y)
            => x.MarkId == y.MarkId && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int MarkId, string Path) obj)
            => HashCode.Combine(obj.MarkId, obj.Path?.ToLowerInvariant());
    }

    #endregion

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
            segments = standardized.Split(InternalOptions.DirSeparator,
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

        return normalizedChild.StartsWith(normalizedParent + InternalOptions.DirSeparator, StringComparison.OrdinalIgnoreCase) ||
               normalizedChild.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if a path is blocked by a resource boundary from another mark.
    /// A path is blocked if it's under a boundary path that belongs to a different mark.
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <param name="currentMarkId">The current mark's ID (boundaries from this mark don't block itself)</param>
    /// <returns>True if the path is blocked by another mark's boundary</returns>
    public bool IsPathBlockedByBoundary(string path, int currentMarkId)
    {
        var normalizedPath = GetStandardizedPath(path);
        if (string.IsNullOrEmpty(normalizedPath))
            return false;

        foreach (var (boundaryPath, markId) in ResourceBoundaryPaths)
        {
            // Skip boundaries from the current mark itself
            if (markId == currentMarkId)
                continue;

            // Check if the path is under this boundary
            if (IsPathUnderParent(normalizedPath, boundaryPath))
                return true;
        }

        return false;
    }
}

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
/// Cached context for property/media library sync operations.
/// Used by PathMarkSyncService to avoid repeated database calls and computations.
/// Resource discovery context is handled separately by ResourceSyncContext.
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

    #region PathMark Effect Tracking (Property/MediaLibrary Only)

    // ===== Phase 1: Collect Effects =====
    public List<PropertyMarkEffect> CollectedPropertyEffects { get; } = new();

    // Old effects loaded at start of sync (for diff calculation)
    // Key: MarkId
    public Dictionary<int, List<PropertyMarkEffect>> OldPropertyEffectsByMarkId { get; } = new();

    // ===== Phase 2: Compute Final State =====
    // Final computed property values after combining all effects
    // Key: (ResourceId, PropertyPool, PropertyId), Value: serialized combined value
    public Dictionary<(int ResourceId, PropertyPool Pool, int PropertyId), string?> FinalPropertyValues { get; } =
        new();

    // Final computed media library mappings
    // Key: ResourceId, Value: list of MediaLibraryIds
    public Dictionary<int, HashSet<int>> FinalMediaLibraryMappings { get; } = new();

    // ===== Phase 3: Apply Changes =====
    // Property values to write/update (already combined)
    public List<CustomPropertyValueDbModel> FinalPropertyValuesToWrite { get; } = new();

    // Property values to delete (no longer have any effects)
    public List<(int ResourceId, int PropertyId)> PropertyValuesToDelete { get; } = new();

    // Media library mappings to ensure
    public List<(int ResourceId, int MediaLibraryId)> FinalMappingsToEnsure { get; } = new();

    // Media library mappings to delete (no longer have any effects)
    public List<(int ResourceId, int MediaLibraryId)> MediaLibraryMappingsToDelete { get; } = new();

    // ===== Phase 4: Persist Effects =====
    public List<PropertyMarkEffect> PropertyEffectsToAdd { get; } = new();
    public List<int> PropertyEffectIdsToDelete { get; } = new();

    // ===== Tracking =====
    public HashSet<(int MarkId, PropertyPool Pool, int PropertyId, int ResourceId)> CurrentPropertyEffectKeys
    {
        get;
    } = new();

    #endregion

    public void BuildIndexes(List<ResourceSourceLink>? allSourceLinks = null)
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

        return normalizedChild.StartsWith(normalizedParent + InternalOptions.DirSeparator,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedChild.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}

using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

public interface IResourceSourceLinkService
{
    Task<List<ResourceSourceLink>> GetAll();
    Task<List<ResourceSourceLink>> GetByResourceId(int resourceId);
    Task<List<ResourceSourceLink>> GetByResourceIds(int[] resourceIds);
    Task<Dictionary<int, List<ResourceSourceLink>>> GetByResourceIdsGrouped(int[] resourceIds);

    /// <summary>
    /// Find a resource that has ALL the given source links (subset match).
    /// Returns the resource ID if found, null otherwise.
    /// </summary>
    Task<int?> FindResourceBySourceLinks(List<(ResourceSource Source, string SourceKey)> sourceLinks);

    /// <summary>
    /// Find resources that have ANY overlapping source links with the given resource.
    /// These are "conflict" resources.
    /// </summary>
    Task<List<int>> FindConflictingResourceIds(int resourceId);

    Task<ResourceSourceLink> Add(ResourceSourceLink link);
    Task AddRange(IEnumerable<ResourceSourceLink> links);

    /// <summary>
    /// Ensure source links exist for a resource (add missing, update CoverUrls on existing if not already set).
    /// </summary>
    Task EnsureLinks(int resourceId, IEnumerable<ResourceSourceLink> links);

    Task DeleteByResourceId(int resourceId);
    Task DeleteByResourceIds(IEnumerable<int> resourceIds);

    /// <summary>
    /// Get source links that have CoverUrls but no LocalCoverPaths (need cover download).
    /// </summary>
    Task<List<ResourceSourceLink>> GetPendingCoverDownloads();

    /// <summary>
    /// Update an existing source link.
    /// </summary>
    Task Update(ResourceSourceLink link);

    /// <summary>
    /// Clear LocalCoverPaths for a specific resource and source (triggers re-download).
    /// </summary>
    Task ClearLocalCoverPaths(int resourceId, ResourceSource source);

    /// <summary>
    /// Clear LocalCoverPaths for all resources of a given source.
    /// </summary>
    Task ClearAllLocalCoverPaths(ResourceSource source);

    /// <summary>
    /// Get source links that need metadata fetch (non-FileSystem, MetadataFetchedAt is null).
    /// </summary>
    Task<List<ResourceSourceLink>> GetPendingMetadataFetches();

    /// <summary>
    /// Clear MetadataJson and MetadataFetchedAt for all source links of a given source.
    /// </summary>
    Task ClearAllMetadata(ResourceSource source);
}

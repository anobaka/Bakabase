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
    /// Ensure source links exist for a resource (add missing, preserve existing).
    /// </summary>
    Task EnsureLinks(int resourceId, IEnumerable<(ResourceSource Source, string SourceKey)> links);

    Task DeleteByResourceId(int resourceId);
    Task DeleteByResourceIds(IEnumerable<int> resourceIds);
}

using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

public interface IMediaLibraryResourceMappingService
{
    /// <summary>
    /// Get all mappings
    /// </summary>
    Task<List<MediaLibraryResourceMapping>> GetAll(
        Expression<Func<MediaLibraryResourceMappingDbModel, bool>>? filter = null);

    /// <summary>
    /// Get mappings by resource ID
    /// </summary>
    Task<List<MediaLibraryResourceMapping>> GetByResourceId(int resourceId);

    /// <summary>
    /// Get mappings by media library ID
    /// </summary>
    Task<List<MediaLibraryResourceMapping>> GetByMediaLibraryId(int mediaLibraryId);

    /// <summary>
    /// Get mappings by media library IDs
    /// </summary>
    Task<Dictionary<int, List<MediaLibraryResourceMapping>>> GetByMediaLibraryIds(int[] mediaLibraryIds);

    /// <summary>
    /// Get mappings by multiple resource IDs
    /// </summary>
    Task<List<MediaLibraryResourceMapping>> GetByResourceIds(int[] resourceIds);

    #region Fast O(1) lookups using bidirectional indices

    /// <summary>
    /// Get resource IDs by media library ID (O(1) lookup)
    /// </summary>
    Task<HashSet<int>> GetResourceIdsByMediaLibraryId(int mediaLibraryId);

    /// <summary>
    /// Get resource IDs by multiple media library IDs (O(m) lookup where m is the number of media library IDs)
    /// </summary>
    Task<HashSet<int>> GetResourceIdsByMediaLibraryIds(IEnumerable<int> mediaLibraryIds);

    /// <summary>
    /// Get media library IDs by resource ID (O(1) lookup)
    /// </summary>
    Task<HashSet<int>> GetMediaLibraryIdsByResourceId(int resourceId);

    /// <summary>
    /// Get media library IDs by multiple resource IDs (O(m) lookup where m is the number of resource IDs)
    /// </summary>
    Task<Dictionary<int, HashSet<int>>> GetMediaLibraryIdsByResourceIds(IEnumerable<int> resourceIds);

    #endregion

    /// <summary>
    /// Add a new mapping
    /// </summary>
    Task<MediaLibraryResourceMapping> Add(MediaLibraryResourceMapping mapping);

    /// <summary>
    /// Add multiple mappings
    /// </summary>
    Task AddRange(IEnumerable<MediaLibraryResourceMapping> mappings);

    /// <summary>
    /// Delete a mapping by ID
    /// </summary>
    Task Delete(int id);

    /// <summary>
    /// Delete mappings by resource ID
    /// </summary>
    Task DeleteByResourceId(int resourceId);

    /// <summary>
    /// Delete mappings by media library ID
    /// </summary>
    Task DeleteByMediaLibraryId(int mediaLibraryId);

    /// <summary>
    /// Ensure mappings exist for a resource (add missing, preserve existing)
    /// </summary>
    Task EnsureMappings(int resourceId, IEnumerable<int> mediaLibraryIds);

    /// <summary>
    /// Batch ensure mappings for multiple resources (add missing, preserve existing)
    /// Much more efficient than calling EnsureMappings for each resource individually.
    /// </summary>
    Task EnsureMappingsRange(IEnumerable<(int ResourceId, int MediaLibraryId)> mappings);

    /// <summary>
    /// Replace all mappings for a resource
    /// </summary>
    Task ReplaceMappings(int resourceId, IEnumerable<int> mediaLibraryIds);

    /// <summary>
    /// Delete specific mappings by (ResourceId, MediaLibraryId) pairs.
    /// Much more efficient than deleting individually.
    /// </summary>
    Task DeleteMappingsRange(IEnumerable<(int ResourceId, int MediaLibraryId)> mappings);
}

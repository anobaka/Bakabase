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
    /// Replace all mappings for a resource
    /// </summary>
    Task ReplaceMappings(int resourceId, IEnumerable<int> mediaLibraryIds);
}

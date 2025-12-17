using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

public interface IResourceProfileService
{
    /// <summary>
    /// Get all resource profiles
    /// </summary>
    Task<List<ResourceProfile>> GetAll(Expression<Func<ResourceProfileDbModel, bool>>? filter = null);

    /// <summary>
    /// Get a resource profile by ID
    /// </summary>
    Task<ResourceProfile?> Get(int id);

    /// <summary>
    /// Get matching profiles for a resource (ordered by priority)
    /// </summary>
    Task<List<ResourceProfile>> GetMatchingProfiles(Resource resource);

    /// <summary>
    /// Get effective name template for a resource
    /// </summary>
    Task<string?> GetEffectiveNameTemplate(Resource resource);

    /// <summary>
    /// Get effective enhancer options for a resource
    /// </summary>
    Task<ResourceProfileEnhancerOptions?> GetEffectiveEnhancerOptions(Resource resource);

    /// <summary>
    /// Get effective enhancer options for multiple resources (batch operation for performance)
    /// Returns a dictionary mapping resource ID to its enhancer options
    /// </summary>
    Task<Dictionary<int, ResourceProfileEnhancerOptions>> GetEffectiveEnhancerOptionsForResources(IEnumerable<Resource> resources);

    /// <summary>
    /// Get effective playable file options for a resource
    /// </summary>
    Task<ResourceProfilePlayableFileOptions?> GetEffectivePlayableFileOptions(Resource resource);

    /// <summary>
    /// Get effective player options for a resource
    /// </summary>
    Task<ResourceProfilePlayerOptions?> GetEffectivePlayerOptions(Resource resource);

    /// <summary>
    /// Add a new resource profile
    /// </summary>
    Task<ResourceProfile> Add(ResourceProfile profile);

    /// <summary>
    /// Update an existing resource profile
    /// </summary>
    Task Update(ResourceProfile profile);

    /// <summary>
    /// Delete a resource profile by ID
    /// </summary>
    Task Delete(int id);
}

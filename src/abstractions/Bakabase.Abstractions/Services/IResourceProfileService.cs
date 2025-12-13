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
    /// Get effective enhancer settings for a resource
    /// </summary>
    Task<EnhancerSettings?> GetEffectiveEnhancerSettings(Resource resource);

    /// <summary>
    /// Get effective playable file settings for a resource
    /// </summary>
    Task<PlayableFileSettings?> GetEffectivePlayableFileSettings(Resource resource);

    /// <summary>
    /// Get effective player settings for a resource
    /// </summary>
    Task<PlayerSettings?> GetEffectivePlayerSettings(Resource resource);

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

    /// <summary>
    /// Test search criteria and return count of matching resources
    /// </summary>
    Task<int> TestSearchCriteria(SearchCriteria criteria);

    /// <summary>
    /// Get resources matching search criteria
    /// </summary>
    Task<List<Resource>> GetMatchingResources(SearchCriteria criteria, int? limit = null);
}

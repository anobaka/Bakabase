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
    /// Get effective name templates for multiple resources (batch operation for performance).
    /// Returns a dictionary mapping resource ID to its name template.
    /// Only resources with matching profiles are included in the result.
    /// </summary>
    Task<Dictionary<int, string?>> GetEffectiveNameTemplatesForResources(int[] resourceIds);

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
    /// <param name="name">Profile name</param>
    /// <param name="searchJson">Serialized ResourceSearchDbModel JSON</param>
    /// <param name="nameTemplate">Optional name template</param>
    /// <param name="enhancerOptions">Optional enhancer options</param>
    /// <param name="playableFileOptions">Optional playable file options</param>
    /// <param name="playerOptions">Optional player options</param>
    /// <param name="priority">Priority for matching</param>
    Task<ResourceProfile> Add(
        string name,
        string? searchJson,
        string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions,
        ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions,
        int priority);

    /// <summary>
    /// Update an existing resource profile
    /// </summary>
    /// <param name="id">Profile ID</param>
    /// <param name="name">Profile name</param>
    /// <param name="searchJson">Serialized ResourceSearchDbModel JSON</param>
    /// <param name="nameTemplate">Optional name template</param>
    /// <param name="enhancerOptions">Optional enhancer options</param>
    /// <param name="playableFileOptions">Optional playable file options</param>
    /// <param name="playerOptions">Optional player options</param>
    /// <param name="priority">Priority for matching</param>
    Task Update(
        int id,
        string name,
        string? searchJson,
        string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions,
        ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions,
        int priority);

    /// <summary>
    /// Delete a resource profile by ID
    /// </summary>
    Task Delete(int id);

    /// <summary>
    /// Get resource IDs matching a profile's search criteria
    /// </summary>
    Task<HashSet<int>> GetMatchingResourceIds(int profileId);
}

using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Aggregated effective profile data for a resource
/// </summary>
public record ResourceProfileEffectiveData(
    string? NameTemplate,
    ResourceProfilePropertyOptions? PropertyOptions
);

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
    /// Get effective property options for a resource
    /// </summary>
    Task<ResourceProfilePropertyOptions?> GetEffectivePropertyOptions(Resource resource);

    /// <summary>
    /// Get effective property options for multiple resources (batch operation for performance)
    /// Returns a dictionary mapping resource ID to its property options
    /// </summary>
    Task<Dictionary<int, ResourceProfilePropertyOptions>> GetEffectivePropertyOptionsForResources(IEnumerable<Resource> resources);

    /// <summary>
    /// Get aggregated effective profile data for multiple resources in a single call.
    /// This is more efficient than calling individual methods when multiple data types are needed.
    /// </summary>
    /// <param name="resourceIds">Resource IDs to query</param>
    /// <param name="includeNameTemplate">Whether to include name templates</param>
    /// <param name="includePropertyOptions">Whether to include property options</param>
    /// <returns>Dictionary mapping resource ID to its effective profile data</returns>
    Task<Dictionary<int, ResourceProfileEffectiveData>> GetEffectiveDataForResources(
        int[] resourceIds,
        bool includeNameTemplate = false,
        bool includePropertyOptions = false);

    /// <summary>
    /// Add a new resource profile
    /// </summary>
    /// <param name="name">Profile name</param>
    /// <param name="searchJson">Serialized ResourceSearchDbModel JSON</param>
    /// <param name="nameTemplate">Optional name template</param>
    /// <param name="enhancerOptions">Optional enhancer options</param>
    /// <param name="playableFileOptions">Optional playable file options</param>
    /// <param name="playerOptions">Optional player options</param>
    /// <param name="propertyOptions">Optional property options</param>
    /// <param name="priority">Priority for matching</param>
    Task<ResourceProfile> Add(
        string name,
        string? searchJson,
        string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions,
        ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions,
        ResourceProfilePropertyOptions? propertyOptions,
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
    /// <param name="propertyOptions">Optional property options</param>
    /// <param name="priority">Priority for matching</param>
    Task Update(
        int id,
        string name,
        string? searchJson,
        string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions,
        ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions,
        ResourceProfilePropertyOptions? propertyOptions,
        int priority);

    /// <summary>
    /// Delete a resource profile by ID
    /// </summary>
    Task Delete(int id);

    /// <summary>
    /// Get resource IDs matching a profile's search criteria
    /// </summary>
    Task<HashSet<int>> GetMatchingResourceIds(int profileId);

    /// <summary>
    /// Get resource IDs matching search criteria directly (bypasses index cache)
    /// </summary>
    Task<HashSet<int>> GetMatchingResourceIds(ResourceSearch? search);

    /// <summary>
    /// Get resource IDs matching a search JSON string
    /// </summary>
    Task<HashSet<int>> GetMatchingResourceIdsBySearchJson(string? searchJson);
}

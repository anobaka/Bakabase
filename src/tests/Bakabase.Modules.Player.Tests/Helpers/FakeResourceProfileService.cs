using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;

namespace Bakabase.Modules.Player.Tests.Helpers;

/// <summary>
/// In-memory stand-in for <see cref="IResourceProfileService"/>. Tests seed
/// per-resource player options; everything the batch-play service does not
/// touch throws so a future caller change is loud.
/// </summary>
internal sealed class FakeResourceProfileService : IResourceProfileService
{
    private readonly Dictionary<int, ResourceProfilePlayerOptions> _playerOptions = [];

    public void SetPlayerOptions(int resourceId, ResourceProfilePlayerOptions options)
        => _playerOptions[resourceId] = options;

    public Task<ResourceProfilePlayerOptions?> GetEffectivePlayerOptions(Resource resource)
        => Task.FromResult(_playerOptions.GetValueOrDefault(resource.Id));

    // === Unused by batch play. ===
    public Task<List<ResourceProfile>> GetAll(Expression<Func<ResourceProfileDbModel, bool>>? filter = null)
        => throw new NotImplementedException();
    public Task<ResourceProfile?> Get(int id) => throw new NotImplementedException();
    public Task<List<ResourceProfile>> GetMatchingProfiles(Resource resource) => throw new NotImplementedException();
    public Task<string?> GetEffectiveNameTemplate(Resource resource) => throw new NotImplementedException();
    public Task<Dictionary<int, string?>> GetEffectiveNameTemplatesForResources(int[] resourceIds)
        => throw new NotImplementedException();
    public Task<List<EnhancerFullOptions>> GetEffectiveEnhancerOptions(Resource resource)
        => throw new NotImplementedException();
    public Task<Dictionary<int, List<EnhancerFullOptions>>> GetEffectiveEnhancerOptionsForResources(
        IEnumerable<Resource> resources) => throw new NotImplementedException();
    public Task<ResourceProfilePlayableFileOptions?> GetEffectivePlayableFileOptions(Resource resource)
        => throw new NotImplementedException();
    public Task<ResourceProfilePropertyOptions?> GetEffectivePropertyOptions(Resource resource)
        => throw new NotImplementedException();
    public Task<Dictionary<int, ResourceProfilePropertyOptions>> GetEffectivePropertyOptionsForResources(
        IEnumerable<Resource> resources) => throw new NotImplementedException();
    public Task<Dictionary<int, ResourceProfileEffectiveData>> GetEffectiveDataForResources(
        int[] resourceIds, bool includeNameTemplate = false, bool includePropertyOptions = false)
        => throw new NotImplementedException();
    public Task<ResourceProfile> Add(string name, string? searchJson, string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions, ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions, ResourceProfilePropertyOptions? propertyOptions, int priority)
        => throw new NotImplementedException();
    public Task Update(int id, string name, string? searchJson, string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions, ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions, ResourceProfilePropertyOptions? propertyOptions, int priority)
        => throw new NotImplementedException();
    public Task Delete(int id) => throw new NotImplementedException();
    public Task<HashSet<int>> GetMatchingResourceIds(int profileId) => throw new NotImplementedException();
    public Task<HashSet<int>> GetMatchingResourceIds(ResourceSearch? search) => throw new NotImplementedException();
    public Task<HashSet<int>> GetMatchingResourceIdsBySearchJson(string? searchJson)
        => throw new NotImplementedException();
}

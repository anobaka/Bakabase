using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Search.Extensions;
using Bakabase.Modules.Search.Models.Db;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using StackExchange.Profiling;

namespace Bakabase.InsideWorld.Business.Services;

public class ResourceProfileService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, ResourceProfileDbModel, int> orm,
    IServiceProvider serviceProvider
) : ScopedService(serviceProvider), IResourceProfileService where TDbContext : DbContext
{
    protected IResourceService ResourceService => GetRequiredService<IResourceService>();
    protected IPropertyService PropertyService => GetRequiredService<IPropertyService>();
    protected IResourceProfileIndexService IndexService => GetRequiredService<IResourceProfileIndexService>();

    public async Task<List<ResourceProfile>> GetAll(Expression<Func<ResourceProfileDbModel, bool>>? filter = null)
    {
        using (MiniProfiler.Current.Step(nameof(GetAll)))
        {
            List<ResourceProfileDbModel> dbModels;
            using (MiniProfiler.Current.Step("orm.GetAll"))
            {
                dbModels = filter != null
                    ? await orm.GetAll(filter)
                    : await orm.GetAll();
            }

            // Pre-fetch all properties once to avoid N+1 queries
            Dictionary<PropertyPool, Dictionary<int, Abstractions.Models.Domain.Property>> propertyMap;
            using (MiniProfiler.Current.Step("PropertyService.GetProperties(All)"))
            {
                var allProperties = await PropertyService.GetProperties(PropertyPool.All);
                propertyMap = allProperties
                    .GroupBy(d => d.Pool)
                    .ToDictionary(d => d.Key, d => d.ToDictionary(a => a.Id, a => a));
            }

            var profiles = new List<ResourceProfile>();
            using (MiniProfiler.Current.Step($"ParseSearchFromDbModel x{dbModels.Count}"))
            {
                foreach (var dbModel in dbModels)
                {
                    var effective = await ResolveScope(dbModel);
                    var search = effective.SourceDeviceId == null ? ParseSearchFromDbModelWithPropertyMap(effective, propertyMap) : null;
                    profiles.Add(effective.ToDomainModel(search));
                }
            }

            return profiles.OrderByDescending(p => p.Priority).ToList();
        }
    }

    public async Task<ResourceProfile?> Get(int id)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel == null) return null;

        dbModel = await ResolveScope(dbModel);
        var search = await ParseSearchFromDbModel(dbModel);
        return dbModel.ToDomainModel(search);
    }

    public async Task<List<ResourceProfile>> GetSourceProfiles(string sourceDeviceId, string? libraryEpoch = null)
    {
        if (string.IsNullOrWhiteSpace(sourceDeviceId)) throw new ArgumentException("A source device is required.", nameof(sourceDeviceId));
        if (string.IsNullOrWhiteSpace(libraryEpoch)) return [];
        return (await GetAll(p => p.SourceDeviceId == sourceDeviceId))
            .Where(p => p.SourceLibraryEpoch == libraryEpoch).ToList();
    }

    public async Task<List<ResourceProfile>> GetMatchingProfiles(Resource resource)
    {
        // Use index service for faster lookup if available
        if (!PersistedReadScope.IsActive && IndexService.IsReady)
        {
            var profileIds = await IndexService.GetMatchingProfileIds(resource.Id);
            if (profileIds.Count == 0)
            {
                return [];
            }

            var allProfiles = await GetAll(p => p.SourceDeviceId == null);
            var profileMap = allProfiles.ToDictionary(p => p.Id);

            // profileIds are already sorted by priority
            return profileIds
                .Where(id => profileMap.ContainsKey(id))
                .Select(id => profileMap[id])
                .ToList();
        }

        // Fallback to direct evaluation
        var allProfilesFallback = await GetAll(p => p.SourceDeviceId == null);
        var matchingProfiles = new List<ResourceProfile>();

        foreach (var profile in allProfilesFallback)
        {
            var matchingIds = await GetMatchingResourceIds(profile.Search);
            if (matchingIds.Contains(resource.Id))
            {
                matchingProfiles.Add(profile);
            }
        }

        return matchingProfiles.OrderByDescending(p => p.Priority).ToList();
    }

    public async Task<string?> GetEffectiveNameTemplate(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return profiles.FirstOrDefault(p => !string.IsNullOrEmpty(p.NameTemplate))?.NameTemplate;
    }

    public async Task<Dictionary<int, string?>> GetEffectiveNameTemplatesForResources(int[] resourceIds)
    {
        using (MiniProfiler.Current.Step("GetEffectiveNameTemplatesForResources"))
        {
            var result = new Dictionary<int, string?>();

            List<ResourceProfile> allProfiles;
            using (MiniProfiler.Current.Step("GetAll profiles"))
            {
                allProfiles = await GetAll(p => p.SourceDeviceId == null);
            }

            List<ResourceProfile> profilesWithNameTemplate;
            using (MiniProfiler.Current.Step("Filter profiles with NameTemplate"))
            {
                profilesWithNameTemplate = allProfiles.Where(p => !string.IsNullOrEmpty(p.NameTemplate)).ToList();
            }

            if (profilesWithNameTemplate.Count == 0)
            {
                return result;
            }

            var profileMap = profilesWithNameTemplate.ToDictionary(p => p.Id, p => p);

            Dictionary<int, IReadOnlyList<int>> allProfileIds;
            using (MiniProfiler.Current.Step("IndexService.GetMatchingProfileIdsForResources"))
            {
                // Wait for index service - much faster than fallback N+1 queries
                allProfileIds = await GetMatchingProfileIdsForResources(resourceIds);
            }

            using (MiniProfiler.Current.Step("Build result map"))
            {
                foreach (var (resourceId, profileIds) in allProfileIds)
                {
                    // profileIds are sorted by priority (highest first)
                    var matchingProfile = profileIds
                        .Select(pid => profileMap.GetValueOrDefault(pid))
                        .FirstOrDefault(p => p != null);

                    if (matchingProfile != null)
                    {
                        result[resourceId] = matchingProfile.NameTemplate;
                    }
                }
            }

            return result;
        }
    }

    public async Task<List<EnhancerFullOptions>> GetEffectiveEnhancerOptions(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return AggregateEnhancerOptions(profiles);
    }

    /// <summary>
    /// Aggregates enhancer options from multiple profiles.
    /// For each enhancer id, takes the configuration from the highest priority profile.
    /// Profiles should already be sorted by priority (highest first).
    /// </summary>
    private static List<EnhancerFullOptions> AggregateEnhancerOptions(List<ResourceProfile> profiles)
    {
        var aggregatedEnhancers = new Dictionary<int, EnhancerFullOptions>();
        var seenEnhancers = new HashSet<int>();

        foreach (var profile in profiles)
        {
            if (profile.EnhancerOptions?.Enhancers == null)
            {
                continue;
            }

            foreach (var enhancer in profile.EnhancerOptions.Enhancers)
            {
                // Only add if we haven't seen this enhancer id yet (first one wins = highest priority)
                // A higher-priority off-device rule still shadows lower-priority local rules.
                // Its durable processing job writes results; the legacy automatic task must not
                // silently run it against this host's paths or numeric ids.
                if (seenEnhancers.Add(enhancer.EnhancerId) &&
                    profile.EnhancerOptions.ExecutionTarget?.Kind != ResourceProcessingExecutionKind.Managed)
                    aggregatedEnhancers.Add(enhancer.EnhancerId, enhancer);
            }
        }

        return aggregatedEnhancers.Values.ToList();
    }

    public async Task<Dictionary<int, List<EnhancerFullOptions>>> GetEffectiveEnhancerOptionsForResources(
        IEnumerable<Resource> resources)
    {
        var result = new Dictionary<int, List<EnhancerFullOptions>>();
        var allProfiles = await GetAll(p => p.SourceDeviceId == null);
        var resourcesList = resources.ToList();
        var profilesWithEnhancerOptions = allProfiles.Where(p => p.EnhancerOptions != null).ToList();

        if (profilesWithEnhancerOptions.Count == 0)
        {
            return result;
        }

        var profileMap = allProfiles.ToDictionary(p => p.Id);

        // Wait for index service - much faster than fallback N+1 queries
        var resourceIds = resourcesList.Select(r => r.Id);
        var allProfileIds = await GetMatchingProfileIdsForResources(resourceIds);
        foreach (var (resourceId, profileIds) in allProfileIds)
        {
            // profileIds are sorted by priority (highest first)
            // Get all matching profiles in priority order
            var matchingProfiles = profileIds
                .Select(pid => profileMap.GetValueOrDefault(pid))
                .Where(p => p != null)
                .ToList()!;

            var aggregated = AggregateEnhancerOptions(matchingProfiles!);
            if (aggregated.Count > 0)
            {
                result[resourceId] = aggregated;
            }
        }

        return result;
    }

    public async Task<ResourceProfilePlayableFileOptions?> GetEffectivePlayableFileOptions(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return profiles.FirstOrDefault(p => p.PlayableFileOptions != null)?.PlayableFileOptions;
    }

    public async Task<ResourceProfilePlayerOptions?> GetEffectivePlayerOptions(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return profiles.FirstOrDefault(p => p.PlayerOptions != null)?.PlayerOptions;
    }

    public async Task<ResourceProfilePropertyOptions?> GetEffectivePropertyOptions(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return profiles.FirstOrDefault(p => p.PropertyOptions != null)?.PropertyOptions;
    }

    public async Task<Dictionary<int, ResourceProfilePropertyOptions>> GetEffectivePropertyOptionsForResources(
        IEnumerable<Resource> resources)
    {
        var result = new Dictionary<int, ResourceProfilePropertyOptions>();
        var allProfiles = await GetAll(p => p.SourceDeviceId == null);
        var resourcesList = resources.ToList();
        var profilesWithPropertyOptions = allProfiles.Where(p => p.PropertyOptions != null).ToList();

        if (profilesWithPropertyOptions.Count == 0)
        {
            return result;
        }

        // Wait for index service - much faster than fallback N+1 queries
        var resourceIds = resourcesList.Select(r => r.Id);
        var allProfileIds = await GetMatchingProfileIdsForResources(resourceIds);
        foreach (var (resourceId, profileIds) in allProfileIds)
        {
            // profileIds are sorted by priority (highest first)
            var matchingProfile = profileIds
                .Select(pid => profilesWithPropertyOptions.FirstOrDefault(p => p.Id == pid))
                .FirstOrDefault(p => p != null);

            if (matchingProfile != null)
            {
                result[resourceId] = matchingProfile.PropertyOptions!;
            }
        }

        return result;
    }

    private async Task<Dictionary<int, IReadOnlyList<int>>> GetMatchingProfileIdsForResources(IEnumerable<int> candidateIds)
    {
        if (!PersistedReadScope.IsActive) return await IndexService.GetMatchingProfileIdsForResources(candidateIds);
        var resourceIds = candidateIds.Distinct().ToArray();
        using var candidates = ResourceMetadataReadScope.Enter(resourceIds);
        var matches = resourceIds.ToDictionary(id => id, _ => new List<int>());
        foreach (var profile in await GetAll(p => p.SourceDeviceId == null))
            foreach (var id in await GetMatchingResourceIds(profile.Search))
                if (matches.TryGetValue(id, out var list)) list.Add(profile.Id);
        return matches.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value);
    }

    public async Task<Dictionary<int, ResourceProfileEffectiveData>> GetEffectiveDataForResources(
        int[] resourceIds,
        bool includeNameTemplate = false,
        bool includePropertyOptions = false)
    {
        using (MiniProfiler.Current.Step("GetEffectiveDataForResources"))
        {
            var result = new Dictionary<int, ResourceProfileEffectiveData>();

            // Early return if nothing is requested
            if (!includeNameTemplate && !includePropertyOptions)
            {
                return result;
            }

            // Optimization: Get DbModels directly from ORM instead of calling GetAll()
            // This avoids expensive ParseSearchFromDbModel calls since we don't need the search data
            List<ResourceProfileDbModel> allProfileDbModels;
            using (MiniProfiler.Current.Step("Get profile DbModels from ORM"))
            {
                allProfileDbModels = await orm.GetAll();
            }

            // Filter profiles based on what data is needed and build lookup maps directly from DbModels
            Dictionary<int, (int Priority, string NameTemplate)> nameTemplateMap;
            Dictionary<int, (int Priority, ResourceProfilePropertyOptions PropertyOptions)> propertyOptionsMap;

            using (MiniProfiler.Current.Step("Build profile maps from DbModels"))
            {
                nameTemplateMap = includeNameTemplate
                    ? allProfileDbModels
                        .Where(p => !string.IsNullOrEmpty(p.NameTemplate))
                        .ToDictionary(p => p.Id, p => (p.Priority, p.NameTemplate!))
                    : new Dictionary<int, (int, string)>();

                propertyOptionsMap = includePropertyOptions
                    ? allProfileDbModels
                        .Where(p => !string.IsNullOrEmpty(p.PropertiesJson))
                        .Select(p => new
                        {
                            p.Id,
                            p.Priority,
                            PropertyOptions = JsonConvert.DeserializeObject<ResourceProfilePropertyOptions>(p.PropertiesJson!)
                        })
                        .Where(x => x.PropertyOptions != null)
                        .ToDictionary(x => x.Id, x => (x.Priority, x.PropertyOptions!))
                    : new Dictionary<int, (int, ResourceProfilePropertyOptions)>();
            }

            // Early return if no relevant profiles exist
            if (nameTemplateMap.Count == 0 && propertyOptionsMap.Count == 0)
            {
                return result;
            }

            // Single call to index service - this is the key optimization
            Dictionary<int, IReadOnlyList<int>> allProfileIds;
            using (MiniProfiler.Current.Step("IndexService.GetMatchingProfileIdsForResources (unified)"))
            {
                allProfileIds = await GetMatchingProfileIdsForResources(resourceIds);
            }

            using (MiniProfiler.Current.Step("Build unified result map"))
            {
                foreach (var (resourceId, profileIds) in allProfileIds)
                {
                    string? nameTemplate = null;
                    ResourceProfilePropertyOptions? propertyOptions = null;

                    // profileIds are already sorted by priority (highest first)
                    if (includeNameTemplate)
                    {
                        foreach (var pid in profileIds)
                        {
                            if (nameTemplateMap.TryGetValue(pid, out var data))
                            {
                                nameTemplate = data.NameTemplate;
                                break;
                            }
                        }
                    }

                    if (includePropertyOptions)
                    {
                        foreach (var pid in profileIds)
                        {
                            if (propertyOptionsMap.TryGetValue(pid, out var data))
                            {
                                propertyOptions = data.PropertyOptions;
                                break;
                            }
                        }
                    }

                    // Only add to result if we found any data
                    if (nameTemplate != null || propertyOptions != null)
                    {
                        result[resourceId] = new ResourceProfileEffectiveData(nameTemplate, propertyOptions);
                    }
                }
            }

            return result;
        }
    }

    public async Task<ResourceProfile> Add(
        string name,
        string? searchJson,
        string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions,
        ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions,
        ResourceProfilePropertyOptions? propertyOptions,
        int priority,
        string? sourceDeviceId = null,
        string? sourceLibraryEpoch = null,
        string? openingMode = null)
    {
        ResourceProfileScope.Validate(sourceDeviceId, sourceLibraryEpoch, openingMode, nameTemplate,
            enhancerOptions, playableFileOptions, propertyOptions);
        var now = DateTime.UtcNow;
        var dbModel = new ResourceProfileDbModel
        {
            Name = name,
            SourceDeviceId = sourceDeviceId,
            SourceLibraryEpoch = sourceLibraryEpoch,
            OpeningMode = openingMode,
            SearchJson = searchJson,
            NameTemplate = nameTemplate,
            EnhancerSettingsJson = enhancerOptions != null
                ? JsonConvert.SerializeObject(enhancerOptions)
                : null,
            PlayableFileSettingsJson = playableFileOptions != null
                ? JsonConvert.SerializeObject(playableFileOptions)
                : null,
            PlayerSettingsJson = playerOptions != null
                ? JsonConvert.SerializeObject(playerOptions)
                : null,
            PropertiesJson = propertyOptions != null
                ? JsonConvert.SerializeObject(propertyOptions)
                : null,
            Priority = priority,
            CreatedAt = now,
            UpdatedAt = now
        };

        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);

        // Invalidate index for new profile
        IndexService.InvalidateProfile(dbModel.Id);

        var search = await ParseSearchFromDbModel(dbModel);
        return dbModel.ToDomainModel(search);
    }

    public async Task Update(
        int id,
        string name,
        string? searchJson,
        string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions,
        ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions,
        ResourceProfilePropertyOptions? propertyOptions,
        int priority,
        string? sourceDeviceId = null,
        string? sourceLibraryEpoch = null,
        string? openingMode = null)
    {
        ResourceProfileScope.Validate(sourceDeviceId, sourceLibraryEpoch, openingMode, nameTemplate,
            enhancerOptions, playableFileOptions, propertyOptions);
        // Get old profile to detect changes
        var oldDbModel = await orm.GetByKey(id);
        if (oldDbModel?.ResourceScopeId != null)
        {
            oldDbModel = await ResolveScope(oldDbModel);
            if (oldDbModel.SourceDeviceId != sourceDeviceId || oldDbModel.SourceLibraryEpoch != sourceLibraryEpoch)
                throw new ArgumentException("Unlink the reusable condition before changing the rule's source device.");
            // The referenced scope is authoritative. Legacy full-profile updates retain the link.
            searchJson = oldDbModel.SearchJson;
        }
        var oldSearchJson = oldDbModel?.SearchJson;
        var oldPlayableFileOptionsJson = oldDbModel?.PlayableFileSettingsJson;

        var newPlayableFileOptionsJson = playableFileOptions != null
            ? JsonConvert.SerializeObject(playableFileOptions)
            : null;

        var dbModel = new ResourceProfileDbModel
        {
            Id = id,
            ResourceScopeId = oldDbModel?.ResourceScopeId,
            Name = name,
            SourceDeviceId = sourceDeviceId,
            SourceLibraryEpoch = sourceLibraryEpoch,
            OpeningMode = openingMode,
            SearchJson = searchJson,
            NameTemplate = nameTemplate,
            EnhancerSettingsJson = enhancerOptions != null
                ? JsonConvert.SerializeObject(enhancerOptions)
                : null,
            PlayableFileSettingsJson = newPlayableFileOptionsJson,
            PlayerSettingsJson = playerOptions != null
                ? JsonConvert.SerializeObject(playerOptions)
                : null,
            PropertiesJson = propertyOptions != null
                ? JsonConvert.SerializeObject(propertyOptions)
                : null,
            Priority = priority,
            UpdatedAt = DateTime.UtcNow
        };

        await orm.Update(dbModel);

        // Invalidate index for updated profile (search criteria or priority might have changed)
        IndexService.InvalidateProfile(id);

        // Check if we need to invalidate PlayableFiles cache
        var searchChanged = oldSearchJson != searchJson || oldDbModel?.SourceDeviceId != sourceDeviceId;
        var playableOptionsChanged = oldPlayableFileOptionsJson != newPlayableFileOptionsJson;

        // Only invalidate cache if PlayableFileOptions is involved (was set or is now set)
        if ((searchChanged || playableOptionsChanged) &&
            (oldPlayableFileOptionsJson != null || newPlayableFileOptionsJson != null))
        {
            var resourceIds = new HashSet<int>();

            // Get matching resources based on what changed
            var oldMatchingIds = oldDbModel?.SourceDeviceId == null && oldSearchJson != null
                ? await GetMatchingResourceIdsBySearchJson(oldSearchJson)
                : new HashSet<int>();
            var newMatchingIds = sourceDeviceId == null ? await GetMatchingResourceIdsBySearchJson(searchJson) : [];

            if (searchChanged && playableOptionsChanged)
            {
                // Both changed - invalidate union of old and new matches
                resourceIds.UnionWith(oldMatchingIds);
                resourceIds.UnionWith(newMatchingIds);
            }
            else if (searchChanged)
            {
                // Only search changed - use symmetric difference
                // Resources that stayed matched don't need invalidation (same playable options)
                // Only resources that lost match or gained match need cache reset
                resourceIds.UnionWith(oldMatchingIds);
                resourceIds.SymmetricExceptWith(newMatchingIds);
            }
            else
            {
                // Only playable options changed - invalidate current matches
                // (old matches == new matches since search didn't change)
                resourceIds.UnionWith(newMatchingIds);
            }

            if (resourceIds.Count > 0)
            {
                await ResourceService.DeleteResourceCacheByResourceIdsAndCacheType(
                    resourceIds,
                    ResourceCacheType.PlayableFiles);
            }
        }
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);

        // Invalidate index for deleted profile
        IndexService.InvalidateProfile(id);
    }

    public async Task<HashSet<int>> GetMatchingResourceIds(int profileId)
    {
        var profile = await Get(profileId);
        if (profile == null || profile.SourceDeviceId != null) return [];
        // Use index service for faster lookup if available
        if (!PersistedReadScope.IsActive && IndexService.IsReady)
        {
            var resourceIds = await IndexService.GetMatchingResourceIds(profileId);
            return resourceIds.ToHashSet();
        }

        // Fallback to direct evaluation
        return await GetMatchingResourceIds(profile.Search);
    }

    public async Task<HashSet<int>> GetMatchingResourceIdsBySearchJson(string? searchJson)
    {
        if (string.IsNullOrEmpty(searchJson))
        {
            return [];
        }

        try
        {
            var searchDbModel = JsonConvert.DeserializeObject<ResourceSearchDbModel>(searchJson);
            if (searchDbModel == null)
            {
                return [];
            }

            var search = await searchDbModel.ToDomainModel(PropertyService);
            return await GetMatchingResourceIds(search);
        }
        catch when (!PersistedReadScope.IsActive)
        {
            return [];
        }
    }

    public async Task<HashSet<int>> GetMatchingResourceIds(ResourceSearch? search)
    {
        if (search == null)
        {
            return [];
        }

        // Use GetAllIds to avoid circular dependency (Search -> DisplayName -> ResourceProfile -> Search)
        var searchForMatching = new ResourceSearch
        {
            Group = search.Group,
            Tags = search.Tags,
            PageIndex = 1,
            PageSize = int.MaxValue
        };

        var ids = await ResourceService.GetAllIds(searchForMatching);
        return ids.ToHashSet();
    }

    private async Task<ResourceProfileDbModel> ResolveScope(ResourceProfileDbModel profile)
    {
        if (profile.ResourceScopeId == null) return profile;
        var scope = await GetRequiredService<FullMemoryCacheResourceService<TDbContext, ResourceScopeDbModel, int>>()
            .GetByKey(profile.ResourceScopeId.Value);
        if (scope == null || scope.SourceDeviceId != profile.SourceDeviceId || scope.SourceLibraryEpoch != profile.SourceLibraryEpoch)
            throw new ResourceScopeConflictException("The reusable condition is missing or belongs to another source library. Reload and repair this rule.");
        return profile with { SearchJson = scope.SearchJson };
    }

    private async Task<ResourceSearch?> ParseSearchFromDbModel(ResourceProfileDbModel dbModel)
    {
        using (MiniProfiler.Current.Step($"ParseSearchFromDbModel(id={dbModel.Id})"))
        {
            if (dbModel.SourceDeviceId != null) return null;
            if (string.IsNullOrEmpty(dbModel.SearchJson))
            {
                return null;
            }

            try
            {
                ResourceSearchDbModel? searchDbModel;
                using (MiniProfiler.Current.Step("JsonConvert.DeserializeObject"))
                {
                    searchDbModel = JsonConvert.DeserializeObject<ResourceSearchDbModel>(dbModel.SearchJson);
                }

                if (searchDbModel == null)
                {
                    return null;
                }

                using (MiniProfiler.Current.Step("searchDbModel.ToDomainModel"))
                {
                    return await searchDbModel.ToDomainModel(PropertyService);
                }
            }
            catch
            {
                return null;
            }
        }
    }

    private ResourceSearch? ParseSearchFromDbModelWithPropertyMap(
        ResourceProfileDbModel dbModel,
        Dictionary<PropertyPool, Dictionary<int, Abstractions.Models.Domain.Property>> propertyMap)
    {
        if (string.IsNullOrEmpty(dbModel.SearchJson))
        {
            return null;
        }

        try
        {
            var searchDbModel = JsonConvert.DeserializeObject<ResourceSearchDbModel>(dbModel.SearchJson);
            return searchDbModel?.ToDomainModel(propertyMap);
        }
        catch
        {
            return null;
        }
    }

}

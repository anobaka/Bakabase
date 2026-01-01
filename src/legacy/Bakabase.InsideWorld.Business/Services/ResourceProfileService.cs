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
                    var search = ParseSearchFromDbModelWithPropertyMap(dbModel, propertyMap);
                    profiles.Add(dbModel.ToDomainModel(search));
                }
            }

            return profiles.OrderByDescending(p => p.Priority).ToList();
        }
    }

    public async Task<ResourceProfile?> Get(int id)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel == null) return null;

        var search = await ParseSearchFromDbModel(dbModel);
        return dbModel.ToDomainModel(search);
    }

    public async Task<List<ResourceProfile>> GetMatchingProfiles(Resource resource)
    {
        // Use index service for faster lookup if available
        if (IndexService.IsReady)
        {
            var profileIds = await IndexService.GetMatchingProfileIds(resource.Id);
            if (profileIds.Count == 0)
            {
                return [];
            }

            var allProfiles = await GetAll();
            var profileMap = allProfiles.ToDictionary(p => p.Id);

            // profileIds are already sorted by priority
            return profileIds
                .Where(id => profileMap.ContainsKey(id))
                .Select(id => profileMap[id])
                .ToList();
        }

        // Fallback to direct evaluation
        var allProfilesFallback = await GetAll();
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
                allProfiles = await GetAll();
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
                allProfileIds = await IndexService.GetMatchingProfileIdsForResources(resourceIds);
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

    public async Task<ResourceProfileEnhancerOptions?> GetEffectiveEnhancerOptions(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return profiles.FirstOrDefault(p => p.EnhancerOptions != null)?.EnhancerOptions;
    }

    public async Task<Dictionary<int, ResourceProfileEnhancerOptions>> GetEffectiveEnhancerOptionsForResources(
        IEnumerable<Resource> resources)
    {
        var result = new Dictionary<int, ResourceProfileEnhancerOptions>();
        var allProfiles = await GetAll();
        var resourcesList = resources.ToList();
        var profilesWithEnhancerOptions = allProfiles.Where(p => p.EnhancerOptions != null).ToList();

        if (profilesWithEnhancerOptions.Count == 0)
        {
            return result;
        }

        // Wait for index service - much faster than fallback N+1 queries
        var resourceIds = resourcesList.Select(r => r.Id);
        var allProfileIds = await IndexService.GetMatchingProfileIdsForResources(resourceIds);
        foreach (var (resourceId, profileIds) in allProfileIds)
        {
            // profileIds are sorted by priority (highest first)
            var matchingProfile = profileIds
                .Select(pid => profilesWithEnhancerOptions.FirstOrDefault(p => p.Id == pid))
                .FirstOrDefault(p => p != null);

            if (matchingProfile != null)
            {
                result[resourceId] = matchingProfile.EnhancerOptions!;
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
        var allProfiles = await GetAll();
        var resourcesList = resources.ToList();
        var profilesWithPropertyOptions = allProfiles.Where(p => p.PropertyOptions != null).ToList();

        if (profilesWithPropertyOptions.Count == 0)
        {
            return result;
        }

        // Wait for index service - much faster than fallback N+1 queries
        var resourceIds = resourcesList.Select(r => r.Id);
        var allProfileIds = await IndexService.GetMatchingProfileIdsForResources(resourceIds);
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
                allProfileIds = await IndexService.GetMatchingProfileIdsForResources(resourceIds);
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
        int priority)
    {
        var now = DateTime.UtcNow;
        var dbModel = new ResourceProfileDbModel
        {
            Name = name,
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
        int priority)
    {
        // Get old profile to detect changes
        var oldDbModel = await orm.GetByKey(id);
        var oldSearchJson = oldDbModel?.SearchJson;
        var oldPlayableFileOptionsJson = oldDbModel?.PlayableFileSettingsJson;

        var newPlayableFileOptionsJson = playableFileOptions != null
            ? JsonConvert.SerializeObject(playableFileOptions)
            : null;

        var dbModel = new ResourceProfileDbModel
        {
            Id = id,
            Name = name,
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
        var searchChanged = oldSearchJson != searchJson;
        var playableOptionsChanged = oldPlayableFileOptionsJson != newPlayableFileOptionsJson;

        // Only invalidate cache if PlayableFileOptions is involved (was set or is now set)
        if ((searchChanged || playableOptionsChanged) &&
            (oldPlayableFileOptionsJson != null || newPlayableFileOptionsJson != null))
        {
            var resourceIds = new HashSet<int>();

            // Get old matching resources (if search changed or playable options changed)
            if (oldSearchJson != null)
            {
                var oldMatchingIds = await GetMatchingResourceIdsBySearchJson(oldSearchJson);
                resourceIds.UnionWith(oldMatchingIds);
            }

            // Get new matching resources
            var newMatchingIds = await GetMatchingResourceIdsBySearchJson(searchJson);
            resourceIds.UnionWith(newMatchingIds);

            if (resourceIds.Count > 0)
            {
                await ResourceService.DeleteResourceCacheByResourceIdsAndCacheType(
                    resourceIds,
                    Abstractions.Models.Domain.Constants.ResourceCacheType.PlayableFiles);
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
        // Use index service for faster lookup if available
        if (IndexService.IsReady)
        {
            var resourceIds = await IndexService.GetMatchingResourceIds(profileId);
            return resourceIds.ToHashSet();
        }

        // Fallback to direct evaluation
        var profile = await Get(profileId);
        return profile == null ? [] : await GetMatchingResourceIds(profile.Search);
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
        catch
        {
            return [];
        }
    }

    private async Task<HashSet<int>> GetMatchingResourceIds(ResourceSearch? search)
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

    private async Task<ResourceSearch?> ParseSearchFromDbModel(ResourceProfileDbModel dbModel)
    {
        using (MiniProfiler.Current.Step($"ParseSearchFromDbModel(id={dbModel.Id})"))
        {
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

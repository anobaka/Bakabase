using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
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

namespace Bakabase.InsideWorld.Business.Services;

public class ResourceProfileService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, ResourceProfileDbModel, int> orm,
    IServiceProvider serviceProvider
) : ScopedService(serviceProvider), IResourceProfileService where TDbContext : DbContext
{
    protected IResourceService ResourceService => GetRequiredService<IResourceService>();
    protected IPropertyService PropertyService => GetRequiredService<IPropertyService>();

    public async Task<List<ResourceProfile>> GetAll(Expression<Func<ResourceProfileDbModel, bool>>? filter = null)
    {
        var dbModels = filter != null
            ? await orm.GetAll(filter)
            : await orm.GetAll();

        var profiles = new List<ResourceProfile>();
        foreach (var dbModel in dbModels)
        {
            var search = await ParseSearchFromDbModel(dbModel);
            profiles.Add(dbModel.ToDomainModel(search));
        }

        return profiles.OrderByDescending(p => p.Priority).ToList();
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
        var allProfiles = await GetAll();
        var matchingProfiles = new List<ResourceProfile>();

        foreach (var profile in allProfiles)
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
        var result = new Dictionary<int, string?>();
        var resourceIdSet = resourceIds.ToHashSet();
        var allProfiles = await GetAll();

        // Process profiles in priority order (highest first, already sorted by GetAll)
        foreach (var profile in allProfiles.Where(p => !string.IsNullOrEmpty(p.NameTemplate)))
        {
            var matchingIds = await GetMatchingResourceIds(profile.Search);

            foreach (var id in matchingIds.Where(id => resourceIdSet.Contains(id)))
            {
                // Only set if not already set (higher priority profiles take precedence)
                result.TryAdd(id, profile.NameTemplate);
            }
        }

        return result;
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

        // For each profile, get matching resources and map enhancer options
        foreach (var profile in allProfiles.Where(p => p.EnhancerOptions != null))
        {
            var matchingIds = await GetMatchingResourceIds(profile.Search);

            foreach (var resource in resourcesList)
            {
                // Only set if not already set (higher priority profiles take precedence)
                if (!result.ContainsKey(resource.Id) && matchingIds.Contains(resource.Id))
                {
                    result[resource.Id] = profile.EnhancerOptions!;
                }
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

    public async Task<ResourceProfile> Add(
        string name,
        string? searchJson,
        string? nameTemplate,
        ResourceProfileEnhancerOptions? enhancerOptions,
        ResourceProfilePlayableFileOptions? playableFileOptions,
        ResourceProfilePlayerOptions? playerOptions,
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
            Priority = priority,
            CreatedAt = now,
            UpdatedAt = now
        };

        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);

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
        int priority)
    {
        var dbModel = new ResourceProfileDbModel
        {
            Id = id,
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
            Priority = priority,
            UpdatedAt = DateTime.UtcNow
        };

        await orm.Update(dbModel);
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
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
        if (string.IsNullOrEmpty(dbModel.SearchJson))
        {
            return null;
        }

        try
        {
            var searchDbModel = JsonConvert.DeserializeObject<ResourceSearchDbModel>(dbModel.SearchJson);
            if (searchDbModel == null)
            {
                return null;
            }

            return await searchDbModel.ToDomainModel(PropertyService);
        }
        catch
        {
            return null;
        }
    }

}

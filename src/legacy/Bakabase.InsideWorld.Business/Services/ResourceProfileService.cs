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
            var matchingResources = await GetMatchingResources(profile.Search, limit: null);
            if (matchingResources.Any(r => r.Id == resource.Id))
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
            var matchingResources = await GetMatchingResources(profile.Search, limit: null);
            var matchingIds = matchingResources.Select(r => r.Id).ToHashSet();

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

    public async Task<ResourceProfile> Add(ResourceProfile profile)
    {
        profile.CreatedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;

        var searchJson = SerializeSearchToJson(profile.Search);
        var dbModel = profile.ToDbModel(searchJson);
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        profile.Id = dbModel.Id;
        return profile;
    }

    public async Task Update(ResourceProfile profile)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        var searchJson = SerializeSearchToJson(profile.Search);
        await orm.Update(profile.ToDbModel(searchJson));
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

    private async Task<List<Resource>> GetMatchingResources(ResourceSearch search, int? limit = null)
    {
        // Ensure we don't use pagination/ordering for profile matching
        var searchForMatching = new ResourceSearch
        {
            Group = search.Group,
            Tags = search.Tags,
            PageIndex = 1,
            PageSize = limit ?? int.MaxValue
        };

        var searchResult = await ResourceService.Search(searchForMatching);
        return searchResult.Data?.ToList() ?? new List<Resource>();
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

    private string? SerializeSearchToJson(ResourceSearch? search)
    {
        if (search?.Group == null && search?.Tags == null)
        {
            return null;
        }

        var dbModel = search.ToDbModel();
        return JsonConvert.SerializeObject(dbModel);
    }
}

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
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Services;

public class ResourceProfileService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, ResourceProfileDbModel, int> orm,
    IServiceProvider serviceProvider
) : ScopedService(serviceProvider), IResourceProfileService where TDbContext : DbContext
{
    protected IResourceService ResourceService => GetRequiredService<IResourceService>();
    protected IMediaLibraryResourceMappingService MappingService => GetRequiredService<IMediaLibraryResourceMappingService>();

    public async Task<List<ResourceProfile>> GetAll(Expression<Func<ResourceProfileDbModel, bool>>? filter = null)
    {
        var dbModels = filter != null
            ? await orm.GetAll(filter)
            : await orm.GetAll();

        return dbModels
            .Select(d => d.ToDomainModel())
            .OrderByDescending(p => p.Priority)
            .ToList();
    }

    public async Task<ResourceProfile?> Get(int id)
    {
        var dbModel = await orm.GetByKey(id);
        return dbModel?.ToDomainModel();
    }

    public async Task<List<ResourceProfile>> GetMatchingProfiles(Resource resource)
    {
        var allProfiles = await GetAll();
        var matchingProfiles = new List<ResourceProfile>();

        // Get resource's media library IDs
        var mappings = await MappingService.GetByResourceId(resource.Id);
        var resourceMediaLibraryIds = mappings.Select(m => m.MediaLibraryId).ToHashSet();

        foreach (var profile in allProfiles)
        {
            if (MatchesCriteria(resource, profile.SearchCriteria, resourceMediaLibraryIds))
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

    public async Task<EnhancerSettings?> GetEffectiveEnhancerSettings(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return profiles.FirstOrDefault(p => p.EnhancerSettings != null)?.EnhancerSettings;
    }

    public async Task<PlayableFileSettings?> GetEffectivePlayableFileSettings(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return profiles.FirstOrDefault(p => p.PlayableFileSettings != null)?.PlayableFileSettings;
    }

    public async Task<PlayerSettings?> GetEffectivePlayerSettings(Resource resource)
    {
        var profiles = await GetMatchingProfiles(resource);
        return profiles.FirstOrDefault(p => p.PlayerSettings != null)?.PlayerSettings;
    }

    public async Task<ResourceProfile> Add(ResourceProfile profile)
    {
        profile.CreateDt = DateTime.UtcNow;
        profile.UpdateDt = DateTime.UtcNow;

        var dbModel = profile.ToDbModel();
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        profile.Id = dbModel.Id;
        return profile;
    }

    public async Task Update(ResourceProfile profile)
    {
        profile.UpdateDt = DateTime.UtcNow;
        await orm.Update(profile.ToDbModel());
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

    public async Task<int> TestSearchCriteria(SearchCriteria criteria)
    {
        var resources = await GetMatchingResources(criteria);
        return resources.Count;
    }

    public async Task<List<Resource>> GetMatchingResources(SearchCriteria criteria, int? limit = null)
    {
        // This is a simplified implementation
        // In a real scenario, you would build a more complex query
        var allResources = await ResourceService.GetAll();

        // Get all mappings for efficient lookup
        var allMappings = await MappingService.GetAll();
        var resourceMediaLibraryMap = allMappings
            .GroupBy(m => m.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.MediaLibraryId).ToHashSet());

        var matchingResources = allResources
            .Where(r =>
            {
                var resourceLibraryIds = resourceMediaLibraryMap.TryGetValue(r.Id, out var ids)
                    ? ids
                    : new HashSet<int>();
                return MatchesCriteria(r, criteria, resourceLibraryIds);
            })
            .ToList();

        if (limit.HasValue)
        {
            matchingResources = matchingResources.Take(limit.Value).ToList();
        }

        return matchingResources;
    }

    private bool MatchesCriteria(Resource resource, SearchCriteria criteria, HashSet<int> resourceMediaLibraryIds)
    {
        // Check media library filter
        if (criteria.MediaLibraryIds != null && criteria.MediaLibraryIds.Count > 0)
        {
            if (!criteria.MediaLibraryIds.Any(id => resourceMediaLibraryIds.Contains(id)))
            {
                return false;
            }
        }

        // Check path pattern
        if (!string.IsNullOrEmpty(criteria.PathPattern))
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(criteria.PathPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!regex.IsMatch(resource.Path))
                {
                    return false;
                }
            }
            catch
            {
                // Invalid regex, skip this filter
            }
        }

        // Check tag filter
        if (criteria.TagFilter.HasValue)
        {
            if (!resource.Tags.Contains(criteria.TagFilter.Value))
            {
                return false;
            }
        }

        // Property filters would require more complex logic with property values
        // This is a simplified implementation
        // In a real scenario, you would join with property value tables

        return true;
    }
}

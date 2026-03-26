using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Services;

public class ResourceSourceLinkService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, ResourceSourceLinkDbModel, int> orm,
    IServiceProvider serviceProvider
) : ScopedService(serviceProvider), IResourceSourceLinkService where TDbContext : DbContext
{
    public async Task<List<ResourceSourceLink>> GetAll()
    {
        var dbModels = await orm.GetAll();
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<List<ResourceSourceLink>> GetByResourceId(int resourceId)
    {
        var dbModels = await orm.GetAll(m => m.ResourceId == resourceId);
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<List<ResourceSourceLink>> GetByResourceIds(int[] resourceIds)
    {
        var dbModels = await orm.GetAll(m => resourceIds.Contains(m.ResourceId));
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<Dictionary<int, List<ResourceSourceLink>>> GetByResourceIdsGrouped(int[] resourceIds)
    {
        var links = await GetByResourceIds(resourceIds);
        return links.GroupBy(l => l.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<int?> FindResourceBySourceLinks(List<(ResourceSource Source, string SourceKey)> sourceLinks)
    {
        if (sourceLinks.Count == 0) return null;

        var allLinks = await orm.GetAll();

        // Group by resource ID
        var resourceLinks = allLinks.GroupBy(l => l.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(l => (l.Source, l.SourceKey)).ToHashSet());

        // Find a resource whose ALL source links are contained in the given sourceLinks
        var inputSet = sourceLinks.ToHashSet();
        foreach (var (resourceId, existingLinks) in resourceLinks)
        {
            if (existingLinks.All(el => inputSet.Contains(el)))
            {
                return resourceId;
            }
        }

        return null;
    }

    public async Task<List<int>> FindConflictingResourceIds(int resourceId)
    {
        var resourceLinks = await orm.GetAll(m => m.ResourceId == resourceId);
        if (resourceLinks.Count == 0) return [];

        var sourceKeys = resourceLinks.Select(l => (l.Source, l.SourceKey)).ToHashSet();

        // Find all links that match any of this resource's source+key pairs
        var allLinks = await orm.GetAll();
        var conflictingResourceIds = allLinks
            .Where(l => l.ResourceId != resourceId && sourceKeys.Contains((l.Source, l.SourceKey)))
            .Select(l => l.ResourceId)
            .Distinct()
            .ToList();

        return conflictingResourceIds;
    }

    public async Task<ResourceSourceLink> Add(ResourceSourceLink link)
    {
        link.CreateDt = DateTime.UtcNow;
        var dbModel = link.ToDbModel();
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        link.Id = dbModel.Id;
        return link;
    }

    public async Task AddRange(IEnumerable<ResourceSourceLink> links)
    {
        var now = DateTime.UtcNow;
        var dbModels = links.Select(l =>
        {
            l.CreateDt = now;
            return l.ToDbModel();
        }).ToList();

        if (dbModels.Count > 0)
        {
            await orm.AddRange(dbModels);
        }
    }

    public async Task EnsureLinks(int resourceId, IEnumerable<ResourceSourceLink> links)
    {
        var existing = await orm.GetAll(m => m.ResourceId == resourceId);
        var existingDict = existing.ToDictionary(l => (l.Source, l.SourceKey));

        var toAdd = new List<ResourceSourceLink>();
        var toUpdate = new List<ResourceSourceLinkDbModel>();

        foreach (var link in links)
        {
            if (existingDict.TryGetValue((link.Source, link.SourceKey), out var existingDb))
            {
                // Update CoverUrls if newly provided and not already set
                if (link.CoverUrls is { Count: > 0 }
                    && string.IsNullOrEmpty(existingDb.CoverUrls))
                {
                    existingDb.CoverUrls = StringListSerializer.Serialize(link.CoverUrls);
                    toUpdate.Add(existingDb);
                }
            }
            else
            {
                toAdd.Add(new ResourceSourceLink
                {
                    ResourceId = resourceId,
                    Source = link.Source,
                    SourceKey = link.SourceKey,
                    CoverUrls = link.CoverUrls,
                    CreateDt = DateTime.UtcNow
                });
            }
        }

        if (toAdd.Count > 0)
        {
            await AddRange(toAdd);
        }

        if (toUpdate.Count > 0)
        {
            await orm.UpdateRange(toUpdate);
        }
    }

    public async Task DeleteByResourceId(int resourceId)
    {
        var dbModels = await orm.GetAll(m => m.ResourceId == resourceId);
        if (dbModels.Count > 0)
        {
            await orm.RemoveRange(dbModels);
        }
    }

    public async Task DeleteByResourceIds(IEnumerable<int> resourceIds)
    {
        var ids = resourceIds.ToHashSet();
        var dbModels = await orm.GetAll(m => ids.Contains(m.ResourceId));
        if (dbModels.Count > 0)
        {
            await orm.RemoveRange(dbModels);
        }
    }

    public async Task<List<ResourceSourceLink>> GetPendingCoverDownloads()
    {
        var now = DateTime.Now;
        var dbModels = await orm.GetAll(m =>
            m.CoverUrls != null && m.CoverUrls != "" &&
            (m.LocalCoverPaths == null || m.LocalCoverPaths == "") &&
            (m.CoverDownloadFailedAt == null || m.CoverDownloadFailedAt.Value.AddHours(24) < now));
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task Update(ResourceSourceLink link)
    {
        var dbModel = link.ToDbModel();
        await orm.Update(dbModel);
    }

    public async Task ClearLocalCoverPaths(int resourceId, ResourceSource source)
    {
        var dbModels = await orm.GetAll(m => m.ResourceId == resourceId && m.Source == source);
        foreach (var dbModel in dbModels)
        {
            dbModel.LocalCoverPaths = null;
            await orm.Update(dbModel);
        }
    }

    public async Task ClearAllLocalCoverPaths(ResourceSource source)
    {
        var dbModels = await orm.GetAll(m =>
            m.Source == source && m.LocalCoverPaths != null && m.LocalCoverPaths != "");
        foreach (var dbModel in dbModels)
        {
            dbModel.LocalCoverPaths = null;
        }
        if (dbModels.Count > 0)
        {
            await orm.UpdateRange(dbModels);
        }
    }

    public async Task<List<ResourceSourceLink>> GetPendingMetadataFetches()
    {
        var dbModels = await orm.GetAll(m =>
            m.Source != ResourceSource.FileSystem && m.MetadataFetchedAt == null);
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task ClearAllMetadata(ResourceSource source)
    {
        var dbModels = await orm.GetAll(m =>
            m.Source == source && (m.MetadataJson != null || m.MetadataFetchedAt != null));
        foreach (var dbModel in dbModels)
        {
            dbModel.MetadataJson = null;
            dbModel.MetadataFetchedAt = null;
        }
        if (dbModels.Count > 0)
        {
            await orm.UpdateRange(dbModels);
        }
    }
}

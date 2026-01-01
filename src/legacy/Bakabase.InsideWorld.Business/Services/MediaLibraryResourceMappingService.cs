using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Services;

public class MediaLibraryResourceMappingService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryResourceMappingDbModel, int> orm,
    MediaLibraryResourceMappingIndexService indexService,
    IResourceDataChangeEventPublisher eventPublisher,
    IServiceProvider serviceProvider
) : ScopedService(serviceProvider), IMediaLibraryResourceMappingService where TDbContext : DbContext
{

    public async Task<List<MediaLibraryResourceMapping>> GetAll(
        Expression<Func<MediaLibraryResourceMappingDbModel, bool>>? filter = null)
    {
        var dbModels = filter != null
            ? await orm.GetAll(filter)
            : await orm.GetAll();

        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<List<MediaLibraryResourceMapping>> GetByResourceId(int resourceId)
    {
        var dbModels = await orm.GetAll(m => m.ResourceId == resourceId);
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<List<MediaLibraryResourceMapping>> GetByMediaLibraryId(int mediaLibraryId)
    {
        var dbModels = await orm.GetAll(m => m.MediaLibraryId == mediaLibraryId);
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<Dictionary<int, List<MediaLibraryResourceMapping>>> GetByMediaLibraryIds(int[] mediaLibraryIds)
    {
        var dbModels = await orm.GetAll(m => mediaLibraryIds.Contains(m.MediaLibraryId));
        return dbModels.GroupBy(d => d.MediaLibraryId)
            .ToDictionary(d => d.Key, d => d.Select(a => a.ToDomainModel()).ToList());
    }

    public async Task<List<MediaLibraryResourceMapping>> GetByResourceIds(int[] resourceIds)
    {
        var dbModels = await orm.GetAll(m => resourceIds.Contains(m.ResourceId));
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    #region Fast O(1) lookups using bidirectional indices

    public Task<HashSet<int>> GetResourceIdsByMediaLibraryId(int mediaLibraryId)
        => indexService.GetByKey1Async(mediaLibraryId);

    public Task<HashSet<int>> GetResourceIdsByMediaLibraryIds(IEnumerable<int> mediaLibraryIds)
        => indexService.GetByKey1sAsync(mediaLibraryIds);

    public Task<HashSet<int>> GetMediaLibraryIdsByResourceId(int resourceId)
        => indexService.GetByKey2Async(resourceId);

    public Task<Dictionary<int, HashSet<int>>> GetMediaLibraryIdsByResourceIds(IEnumerable<int> resourceIds)
        => indexService.GetByKey2sAsync(resourceIds);

    #endregion

    public async Task<MediaLibraryResourceMapping> Add(MediaLibraryResourceMapping mapping)
    {
        mapping.CreateDt = DateTime.UtcNow;
        var dbModel = mapping.ToDbModel();
        await orm.Add(dbModel);
        await indexService.AddAsync(dbModel);

        // Publish resource data changed event
        eventPublisher.PublishResourceChanged(mapping.ResourceId);

        orm.DbContext.Detach(dbModel);
        mapping.Id = dbModel.Id;
        return mapping;
    }

    public async Task AddRange(IEnumerable<MediaLibraryResourceMapping> mappings)
    {
        var now = DateTime.UtcNow;
        var mappingsList = mappings.ToList();
        var dbModels = mappingsList.Select(m =>
        {
            m.CreateDt = now;
            return m.ToDbModel();
        }).ToList();

        await orm.AddRange(dbModels);
        await indexService.AddRangeAsync(dbModels);

        // Publish resource data changed event
        var affectedResourceIds = mappingsList.Select(m => m.ResourceId).Distinct();
        eventPublisher.PublishResourcesChanged(affectedResourceIds);
    }

    public async Task Delete(int id)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel != null)
        {
            await orm.RemoveByKey(id);
            await indexService.RemoveAsync(dbModel);

            // Publish resource data changed event
            eventPublisher.PublishResourceChanged(dbModel.ResourceId);
        }
    }

    public async Task DeleteByResourceId(int resourceId)
    {
        var dbModels = await orm.GetAll(m => m.ResourceId == resourceId);
        if (dbModels.Any())
        {
            await orm.RemoveRange(dbModels);
            await indexService.RemoveRangeAsync(dbModels);

            // Publish resource data changed event
            eventPublisher.PublishResourceChanged(resourceId);
        }
    }

    public async Task DeleteByMediaLibraryId(int mediaLibraryId)
    {
        var dbModels = await orm.GetAll(m => m.MediaLibraryId == mediaLibraryId);
        if (dbModels.Any())
        {
            await orm.RemoveRange(dbModels);
            await indexService.RemoveRangeAsync(dbModels);

            // Publish resource data changed event
            var affectedResourceIds = dbModels.Select(m => m.ResourceId).Distinct();
            eventPublisher.PublishResourcesChanged(affectedResourceIds);
        }
    }

    public async Task EnsureMappings(int resourceId, IEnumerable<int> mediaLibraryIds)
    {
        var existingMappings = await GetByResourceId(resourceId);
        var existingLibraryIds = existingMappings.Select(m => m.MediaLibraryId).ToHashSet();
        var targetLibraryIds = mediaLibraryIds.ToHashSet();

        var toAdd = targetLibraryIds.Except(existingLibraryIds).ToList();

        if (toAdd.Any())
        {
            var newMappings = toAdd.Select(libraryId => new MediaLibraryResourceMapping
            {
                ResourceId = resourceId,
                MediaLibraryId = libraryId,
                CreateDt = DateTime.UtcNow
            });

            await AddRange(newMappings);
        }
    }

    public async Task EnsureMappingsRange(IEnumerable<(int ResourceId, int MediaLibraryId)> mappings)
    {
        var mappingsList = mappings.ToList();
        if (mappingsList.Count == 0) return;

        // Get all unique resource IDs
        var resourceIds = mappingsList.Select(m => m.ResourceId).Distinct().ToArray();

        // Get all existing mappings for these resources in one query
        var existingMappings = await GetByResourceIds(resourceIds);
        var existingSet = existingMappings
            .Select(m => (m.ResourceId, m.MediaLibraryId))
            .ToHashSet();

        // Find mappings that need to be added to database
        var now = DateTime.UtcNow;
        var toAdd = mappingsList
            .Where(m => !existingSet.Contains((m.ResourceId, m.MediaLibraryId)))
            .Distinct()
            .Select(m => new MediaLibraryResourceMapping
            {
                ResourceId = m.ResourceId,
                MediaLibraryId = m.MediaLibraryId,
                CreateDt = now
            })
            .ToList();

        if (toAdd.Any())
        {
            await AddRange(toAdd);
        }

        // Also ensure existing mappings are in the index
        // This handles the case where mappings exist in DB but index wasn't initialized when they were added
        if (existingMappings.Count > 0)
        {
            var existingDbModels = existingMappings.Select(m => m.ToDbModel()).ToList();
            await indexService.AddRangeAsync(existingDbModels);
        }
    }

    public async Task ReplaceMappings(int resourceId, IEnumerable<int> mediaLibraryIds)
    {
        // Delete existing mappings
        await DeleteByResourceId(resourceId);

        // Add new mappings
        var newMappings = mediaLibraryIds.Select(libraryId => new MediaLibraryResourceMapping
        {
            ResourceId = resourceId,
            MediaLibraryId = libraryId,
            CreateDt = DateTime.UtcNow
        });

        await AddRange(newMappings);
    }

    public async Task DeleteMappingsRange(IEnumerable<(int ResourceId, int MediaLibraryId)> mappings)
    {
        var mappingsList = mappings.ToList();
        if (mappingsList.Count == 0) return;

        // Get all unique resource IDs
        var resourceIds = mappingsList.Select(m => m.ResourceId).Distinct().ToArray();

        // Get all existing mappings for these resources in one query
        var existingMappings = await orm.GetAll(m => resourceIds.Contains(m.ResourceId));

        // Build a set of mappings to delete for fast lookup
        var toDeleteSet = mappingsList.ToHashSet();

        // Find mappings that need to be deleted
        var dbModelsToDelete = existingMappings
            .Where(m => toDeleteSet.Contains((m.ResourceId, m.MediaLibraryId)))
            .ToList();

        if (dbModelsToDelete.Any())
        {
            await orm.RemoveRange(dbModelsToDelete);
            await indexService.RemoveRangeAsync(dbModelsToDelete);

            // Publish resource data changed event
            var affectedResourceIds = dbModelsToDelete.Select(m => m.ResourceId).Distinct();
            eventPublisher.PublishResourcesChanged(affectedResourceIds);
        }
    }
}

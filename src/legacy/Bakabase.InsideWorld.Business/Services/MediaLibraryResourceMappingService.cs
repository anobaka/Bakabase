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

public class MediaLibraryResourceMappingService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryResourceMappingDbModel, int> orm,
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

    public async Task<List<MediaLibraryResourceMapping>> GetByResourceIds(int[] resourceIds)
    {
        var dbModels = await orm.GetAll(m => resourceIds.Contains(m.ResourceId));
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<MediaLibraryResourceMapping> Add(MediaLibraryResourceMapping mapping)
    {
        mapping.CreateDt = DateTime.UtcNow;
        var dbModel = mapping.ToDbModel();
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        mapping.Id = dbModel.Id;
        return mapping;
    }

    public async Task AddRange(IEnumerable<MediaLibraryResourceMapping> mappings)
    {
        var now = DateTime.UtcNow;
        var dbModels = mappings.Select(m =>
        {
            m.CreateDt = now;
            return m.ToDbModel();
        }).ToList();

        await orm.AddRange(dbModels);
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

    public async Task DeleteByResourceId(int resourceId)
    {
        var dbModels = await orm.GetAll(m => m.ResourceId == resourceId);
        if (dbModels.Any())
        {
            await orm.RemoveRange(dbModels);
        }
    }

    public async Task DeleteByMediaLibraryId(int mediaLibraryId)
    {
        var dbModels = await orm.GetAll(m => m.MediaLibraryId == mediaLibraryId);
        if (dbModels.Any())
        {
            await orm.RemoveRange(dbModels);
        }
    }

    public async Task DeleteBySourceRuleId(int ruleId)
    {
        var dbModels = await orm.GetAll(m => m.SourceRuleId == ruleId);
        if (dbModels.Any())
        {
            await orm.RemoveRange(dbModels);
        }
    }

    public async Task EnsureMappings(int resourceId, IEnumerable<int> mediaLibraryIds, MappingSource source,
        int? sourceRuleId = null)
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
                Source = source,
                SourceRuleId = sourceRuleId,
                CreateDt = DateTime.UtcNow
            });

            await AddRange(newMappings);
        }
    }

    public async Task ReplaceMappings(int resourceId, IEnumerable<int> mediaLibraryIds, MappingSource source,
        int? sourceRuleId = null)
    {
        // Delete existing mappings
        await DeleteByResourceId(resourceId);

        // Add new mappings
        var newMappings = mediaLibraryIds.Select(libraryId => new MediaLibraryResourceMapping
        {
            ResourceId = resourceId,
            MediaLibraryId = libraryId,
            Source = source,
            SourceRuleId = sourceRuleId,
            CreateDt = DateTime.UtcNow
        });

        await AddRange(newMappings);
    }
}

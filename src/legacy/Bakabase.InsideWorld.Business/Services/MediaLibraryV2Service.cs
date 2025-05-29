using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Configurations.Extensions;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Asn1.Sec;

namespace Bakabase.InsideWorld.Business.Services;

public class MediaLibraryV2Service<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryV2DbModel, int> orm,
    IMediaLibraryTemplateService templateService,
    IResourceService resourceService,
    IPropertyService propertyService,
    FullMemoryCacheResourceService<TDbContext, ResourceCacheDbModel, int> cacheOrm,
    IBOptions<ResourceOptions> resourceOptions)
    : IMediaLibraryV2Service where TDbContext : DbContext
{
    public async Task Add(MediaLibraryV2AddOrPutInputModel model)
    {
        await orm.Add(new MediaLibraryV2DbModel { Path = model.Path, Name = model.Name });
    }

    public async Task Put(int id, MediaLibraryV2AddOrPutInputModel model)
    {
        await orm.UpdateByKey(id, data =>
        {
            data.Name = model.Name;
            data.Path = model.Path;
        });
    }

    public async Task SaveAll(MediaLibraryV2[] models)
    {
        var newData = models.Where(x => x.Id == 0).ToArray();
        var dbData = models.Except(newData).ToArray();
        var ids = dbData.Select(x => x.Id).ToArray();
        await orm.RemoveAll(x => !ids.Contains(x.Id));
        await orm.AddRange(newData.Select(d => d.ToDbModel()).ToList());
        await orm.UpdateRange(dbData.Select(d => d.ToDbModel()).ToList());
    }

    public async Task<MediaLibraryV2> Get(int id)
    {
        return (await orm.GetByKey(id)).ToDomainModel();
    }

    public async Task<List<MediaLibraryV2>> GetAll()
    {
        return (await orm.GetAll()).Select(x => x.ToDomainModel()).ToList();
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

    public async Task Sync(int id)
    {
        throw new System.NotImplementedException();
    }

    public async Task SyncAll()
    {
        var data = await GetAll();
        var templateIds = data.Select(d => d.TemplateId).OfType<int>().ToHashSet();
        var templateMap = (await templateService.GetByKeys(templateIds.ToArray())).ToDictionary(d => d.Id, d => d);
        var mlResourceMap = (await resourceService.GetAllGeneratedByMediaLibraryV2()).GroupBy(d => d.MediaLibraryId)
            .ToDictionary(d => d.Key, d => d.ToList());
        var syncOptions = resourceOptions.Value.SynchronizationOptions;
        foreach (var ml in data)
        {
            var template = templateMap.GetValueOrDefault(ml.Id);
            if (template == null)
            {
                continue;
            }

            var treeTempSyncResources = template.DiscoverResources(ml.Path);
            var flattenTempSyncResources = treeTempSyncResources.SelectMany(d => d.Flatten()).ToList();
            var flattenTempResources = flattenTempSyncResources.Select(x => x.ToDomainModel()).ToList();
            var tempSyncResourcePaths = flattenTempResources.Select(d => d.Path).ToHashSet();

            var mlResources = mlResourceMap.GetValueOrDefault(ml.Id) ?? [];

            var unknownDbResources = mlResources.Where(r => !tempSyncResourcePaths.Contains(r.Path)).ToList();
            var conflictDbResources = mlResources.Except(unknownDbResources).ToList();
            var dbResourcePaths = mlResources.Select(x => x.Path).ToHashSet();
            var newTempSyncResources = flattenTempResources.Where(c => !dbResourcePaths.Contains(c.Path)).ToList();
            var tempSyncResourceMap = flattenTempResources.ToDictionary(d => d.Path);

            // delete
            var resourcesToBeDeleted =
                unknownDbResources.Where(x => x.ShouldBeDeletedSinceFileNotFound(syncOptions)).ToList();
            if (resourcesToBeDeleted.Any())
            {
                await resourceService.DeleteByKeys(resourcesToBeDeleted.Select(r => r.Id).ToArray(), false);
            }

            // add
            await resourceService.AddOrPutRange(newTempSyncResources);
            var allPathIdMap = mlResources.Concat(newTempSyncResources).ToDictionary(d => d.Path, d => d.Id);

            // merge and update
            var changedResources = new HashSet<Resource>();
            foreach (var cr in conflictDbResources)
            {
                var tmpResource = tempSyncResourceMap[cr.Path];
                if (cr.MergeOnSynchronization(tmpResource))
                {
                    cr.UpdatedAt = DateTime.Now;
                    changedResources.Add(cr);
                }
            }

            foreach (var dbResource in newTempSyncResources.Concat(conflictDbResources))
            {
                var tmpResource = tempSyncResourceMap[dbResource.Path];
                int? parentId = tmpResource.Parent == null ? null : allPathIdMap[tmpResource.Parent.Path];
                if (parentId != dbResource.ParentId)
                {
                    dbResource.ParentId = parentId;
                    changedResources.Add(dbResource);
                }
            }

            await resourceService.AddOrPutRange(changedResources.ToList());

            // clean cache
            await cacheOrm.RemoveByKeys(conflictDbResources.Select(r => r.Id));
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Configurations.Extensions;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.SS.Formula.Functions;
using Org.BouncyCastle.Asn1.Sec;

namespace Bakabase.InsideWorld.Business.Services;

public class MediaLibraryV2Service<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryV2DbModel, int> orm,
    IResourceService resourceService,
    IPropertyService propertyService,
    FullMemoryCacheResourceService<TDbContext, ResourceCacheDbModel, int> cacheOrm,
    IBOptions<ResourceOptions> resourceOptions,
    BTaskManager btm,
    IBakabaseLocalizer localizer,
    IServiceProvider serviceProvider
)
    : ScopedService(serviceProvider), IMediaLibraryV2Service where TDbContext : DbContext
{
    protected IMediaLibraryTemplateService TemplateService => GetRequiredService<IMediaLibraryTemplateService>();

    public async Task Add(MediaLibraryV2AddOrPutInputModel model)
    {
        await orm.Add(new MediaLibraryV2DbModel {Path = model.Path, Name = model.Name});
    }

    public async Task Put(int id, MediaLibraryV2AddOrPutInputModel model)
    {
        await orm.UpdateByKey(id, data =>
        {
            data.Name = model.Name;
            data.Path = model.Path;
        });
    }

    public async Task Patch(int id, MediaLibraryV2PatchInputModel model)
    {
        await orm.UpdateByKey(id, data =>
        {
            if (model.Path.IsNotEmpty())
            {
                data.Path = model.Path;
            }

            if (model.Name.IsNotEmpty())
            {
                data.Name = model.Name;
            }

            if (model.ResourceCount.HasValue)
            {
                data.ResourceCount = model.ResourceCount.Value;
            }
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

    protected async Task Populate(List<MediaLibraryV2> models,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None)
    {
        foreach (var ai in SpecificEnumUtils<MediaLibraryV2AdditionalItem>.Values)
        {
            if (additionalItems.HasFlag(ai))
            {
                switch (ai)
                {
                    case MediaLibraryV2AdditionalItem.None:
                        break;
                    case MediaLibraryV2AdditionalItem.Template:
                    {
                        var templateIds = models.Select(d => d.TemplateId).OfType<int>().ToHashSet();
                        var templates =
                            (await TemplateService.GetByKeys(templateIds.ToArray())).ToDictionary(d => d.Id);
                        foreach (var model in models)
                        {
                            if (model.TemplateId.HasValue)
                            {
                                model.Template = templates.GetValueOrDefault(model.TemplateId.Value);
                            }
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }

    public async Task<MediaLibraryV2> Get(int id)
    {
        return (await orm.GetByKey(id)).ToDomainModel();
    }

    public Task<List<MediaLibraryV2>> GetByKeys(int[] ids,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None) =>
        GetAll(x => ids.Contains(x.Id), additionalItems);

    public async Task<List<MediaLibraryV2>> GetAll(Expression<Func<MediaLibraryV2DbModel, bool>>? filter = null,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None)
    {
        var data = (await orm.GetAll(filter)).Select(x => x.ToDomainModel()).ToList();
        await Populate(data, additionalItems);
        return data;
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

    public async Task<MediaLibrarySyncResultViewModel> Sync(int id, Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct)
    {
        return await SyncAll([id], onProgressChange, onProcessChange, pt, ct);
    }

    public async Task<MediaLibrarySyncResultViewModel> SyncAll(int[]? ids, Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct)
    {
        var data = await (ids == null ? GetAll() : GetByKeys(ids));
        var templateIds = data.Select(d => d.TemplateId).OfType<int>().ToHashSet();
        var templateMap = (await TemplateService.GetByKeys(templateIds.ToArray())).ToDictionary(d => d.Id, d => d);
        var mlResourceMap = (await resourceService.GetAllGeneratedByMediaLibraryV2(ids, ResourceAdditionalItem.All))
            .GroupBy(d => d.MediaLibraryId)
            .ToDictionary(d => d.Key, d => d.ToList());
        var syncOptions = resourceOptions.Value.SynchronizationOptions;

        var progressPerMediaLibrary = ids?.Length > 0 ? 100f / ids.Length : 0;
        var baseProgress = 0f;

        var ret = new MediaLibrarySyncResultViewModel();
        for (var index = 0; index < data.Count; index++)
        {
            var ml = data[index];
            if (ml.TemplateId.HasValue && templateMap.TryGetValue(ml.TemplateId.Value, out var template))
            {
                #region Discovery

                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_DiscoverResources(ml.Name));
                }

                var progressForDiscovering = progressPerMediaLibrary * 0.5f;
                var treeTempSyncResources = await template.DiscoverResources(ml.Path,
                    onProgressChange.ScaleInSubTask(baseProgress, progressForDiscovering), null, pt, ct);
                var flattenTempSyncResources = treeTempSyncResources.SelectMany(d => d.Flatten()).ToList();
                var flattenTempResources = flattenTempSyncResources.Select(x => x.ToDomainModel(ml.Id)).ToList();
                var tempSyncResourcePaths = flattenTempResources.Select(d => d.Path).ToHashSet();

                var mlResources = mlResourceMap.GetValueOrDefault(ml.Id) ?? [];

                var unknownDbResources = mlResources.Where(r => !tempSyncResourcePaths.Contains(r.Path)).ToList();
                var conflictDbResources = mlResources.Except(unknownDbResources).ToList();
                var dbResourcePaths = mlResources.Select(x => x.Path).ToHashSet();
                var newTempSyncResources = flattenTempResources.Where(c => !dbResourcePaths.Contains(c.Path)).ToList();
                var tempSyncResourceMap = flattenTempResources.ToDictionary(d => d.Path);
                baseProgress = await onProgressChange.TriggerOnJumpingOver(baseProgress, progressForDiscovering);

                #endregion

                #region Clean

                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_CleanupResources(ml.Name));
                }

                var progressForCleaningResources = progressPerMediaLibrary * 0.1f;
                var resourcesToBeDeleted =
                    unknownDbResources.Where(x => x.ShouldBeDeletedSinceFileNotFound(syncOptions)).ToList();
                if (resourcesToBeDeleted.Any())
                {
                    await resourceService.DeleteByKeys(resourcesToBeDeleted.Select(r => r.Id).ToArray(), false);

                    ret.Deleted += resourcesToBeDeleted.Count;
                }

                baseProgress = await onProgressChange.TriggerOnJumpingOver(baseProgress, progressForCleaningResources);

                #endregion

                #region Add

                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_AddResources(ml.Name));
                }

                var progressForAddingResources = progressPerMediaLibrary * 0.1f;
                await resourceService.AddOrPutRange(newTempSyncResources);
                var allPathIdMap = mlResources.Concat(newTempSyncResources).ToDictionary(d => d.Path, d => d.Id);
                ret.Added += newTempSyncResources.Count;

                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_UpdateResources(ml.Name));
                }

                baseProgress = await onProgressChange.TriggerOnJumpingOver(baseProgress, progressForAddingResources);

                #endregion

                #region Merge and update

                var progressForUpdatingResources = progressPerMediaLibrary * 0.2f;
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

                ret.Updated += changedResources.Count;

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

                baseProgress = await onProgressChange.TriggerOnJumpingOver(baseProgress, progressForUpdatingResources);

                #endregion

                #region Finishing up

                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_AlmostComplete(ml.Name));
                }

                var progressForFinishingUp = progressPerMediaLibrary * 0.1f;
                await resourceService.AddOrPutRange(changedResources.ToList());
                await Patch(ml.Id,
                    new MediaLibraryV2PatchInputModel
                    {
                        ResourceCount = mlResources.Count - resourcesToBeDeleted.Count + newTempSyncResources.Count
                    });

                // clean cache
                await cacheOrm.RemoveByKeys(conflictDbResources.Select(r => r.Id));
                baseProgress = await onProgressChange.TriggerOnJumpingOver(baseProgress, progressForFinishingUp);

                #endregion

                if (onProcessChange != null)
                {
                    await onProcessChange(null);
                }
            }

            baseProgress = (index + 1) * progressPerMediaLibrary;
            if (onProgressChange != null)
            {
                await onProgressChange((int) baseProgress);
            }
        }

        return ret;
    }

    public async Task StartSyncAll(int[]? ids = null)
    {
        if (ids?.Any() != true)
        {
            ids = (await GetAll()).Select(d => d.Id).ToArray();
        }

        const string taskConflictKey = "SyncMediaLibrary";
        var mlMap = (await GetByKeys(ids)).ToDictionary(d => d.Id, d => d);

        foreach (var id in ids)
        {
            var taskId = $"{taskConflictKey}_{id}";
            if (!btm.IsPending(taskId))
            {
                var taskHandlerBuilder = new BTaskHandlerBuilder
                {
                    ConflictKeys = [taskConflictKey],
                    Id = taskId,
                    Run = async args =>
                    {
                        var scope = args.RootServiceProvider.CreateAsyncScope();
                        var service = scope.ServiceProvider.GetRequiredService<IMediaLibraryV2Service>();
                        var result = await service.Sync(id,
                            async p =>
                            {
                                // if (p == 24)
                                // {
                                //
                                // }
                                // Console.WriteLine($"{DateTime.Now:HH:mm:ss}-{p}");
                                await args.UpdateTask(t => t.Percentage = p);
                            },
                            async p => await args.UpdateTask(t => t.Process = p), args.PauseToken,
                            args.CancellationToken);
                        await args.UpdateTask(t =>
                        {
                            t.Data = result;
                        });
                    },
                    GetName = () =>
                        localizer.SyncMediaLibrary(mlMap?.GetValueOrDefault(id)?.Name ?? localizer.Unknown()),
                    ResourceType = BTaskResourceType.Any,
                    Type = BTaskType.Any,
                    DuplicateIdHandling = BTaskDuplicateIdHandling.Replace
                };
                await btm.Enqueue(taskHandlerBuilder);
            }
        }
    }
}
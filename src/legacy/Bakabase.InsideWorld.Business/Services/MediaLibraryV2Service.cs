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
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.InsideWorld.Business.Components.Configurations.Extensions;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Components.Tasks;
using Bootstrap.Components.Tasks.Progressor.Abstractions;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;
using Org.BouncyCastle.Asn1.Sec;
using Bakabase.Abstractions.Components.Configuration;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Bakabase.InsideWorld.Business.Services;

public class MediaLibraryV2Service<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryV2DbModel, int> orm,
    IResourceService resourceService,
    IPropertyService propertyService,
    FullMemoryCacheResourceService<TDbContext, ResourceCacheDbModel, int> cacheOrm,
    IBOptions<ResourceOptions> resourceOptions,
    BTaskManager btm,
    IBakabaseLocalizer localizer,
    IServiceProvider serviceProvider,
    ISystemService systemService
)
    : ScopedService(serviceProvider), IMediaLibraryV2Service where TDbContext : DbContext
{
    protected IMediaLibraryTemplateService TemplateService => GetRequiredService<IMediaLibraryTemplateService>();

    public async Task<MediaLibraryV2> Add(MediaLibraryV2AddOrPutInputModel model)
    {
        var domainModel = new MediaLibraryV2 { Paths = model.Paths, Name = model.Name, Color = model.Color };
        var dbModel = domainModel.ToDbModel();
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        domainModel.Id = dbModel.Id;
        return domainModel;
    }

    public async Task Put(int id, MediaLibraryV2AddOrPutInputModel model)
    {
        var data = await Get(id);
        data.Name = model.Name;
        data.Color = model.Color;
        data.Players = model.Players;
        if (!data.Paths.SequenceEqual(model.Paths))
        {
            data.Paths = model.Paths;
            data.SyncVersion = null;
        }

        await orm.Update(data.ToDbModel());
    }

    public async Task Put(IEnumerable<MediaLibraryV2> data)
    {
        await orm.UpdateRange(data.Select(d => d.ToDbModel()).ToList());
    }

    public async Task Patch(int id, MediaLibraryV2PatchInputModel model)
    {
        var data = await Get(id);

        if (model.SyncVersion.IsNotEmpty())
        {
            data.SyncVersion = model.SyncVersion;
        }

        if (model.Paths != null)
        {
            if (!data.Paths.SequenceEqual(model.Paths))
            {
                data.SyncVersion = null;
                data.Paths = model.Paths;
            }
        }

        if (model.TemplateId.HasValue)
        {
            data.TemplateId = model.TemplateId;
        }

        if (model.Name.IsNotEmpty())
        {
            data.Name = model.Name;
        }

        if (model.ResourceCount.HasValue)
        {
            data.ResourceCount = model.ResourceCount.Value;
        }

        if (model.Color != null)
        {
            data.Color = model.Color;
        }

        await orm.Update(data.ToDbModel());
    }

    public async Task MarkAsSynced(int[] ids)
    {
        var data = await GetByKeys(ids, MediaLibraryV2AdditionalItem.Template);
        foreach (var ml in data)
        {
            ml.SyncVersion = ml.Template?.GetSyncVersion();
        }

        await Put(data);
    }

    public async Task MarkAsSynced(int id, int resourceCount, string? syncVersion)
    {
        await orm.UpdateByKey(id, d =>
        {
            d.ResourceCount = resourceCount;
            d.SyncVersion = syncVersion;
        });
    }

    public async Task<IEnumerable<MediaLibraryV2>> GetAllSyncMayBeOutdated()
    {
        var data = await GetAll(null, MediaLibraryV2AdditionalItem.Template);
        return data.Where(x => x.SyncMayBeOutdated);
    }

    public async Task RefreshResourceCount(int id)
    {
        var count = (await resourceService.GetAllGeneratedByMediaLibraryV2(new[] { id })).Length;
        await orm.UpdateByKey(id, d =>
        {
            d.ResourceCount = count;
        });
    }

    /// <summary>
    /// <inheritdoc cref="IMediaLibraryV2Service.ReplaceAll"/>
    /// </summary>
    /// <param name="models"></param>
    /// <returns></returns>
    public async Task ReplaceAll(MediaLibraryV2[] models)
    {
        var currentData = (await GetAll()).ToDictionary(d => d.Id, d => d);

        foreach (var m in models)
        {
            m.Paths = m.Paths.Select(p => p.StandardizePath()!).Distinct().ToList();
            if (currentData.TryGetValue(m.Id, out var current))
            {
                if (!current.Paths.SequenceEqual(m.Paths) || current.TemplateId != m.TemplateId)
                {
                    m.SyncVersion = null;
                }
            }
        }

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

    public async Task<MediaLibraryV2> Get(int id,
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None)
    {
        var domainModel = (await orm.GetByKey(id)).ToDomainModel();
        await Populate([domainModel], additionalItems);
        return domainModel;
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

    private class ProgressPerMediaLibrary
    {
        public const int Discovery = 50;
        public const int Clean = 10;
        public const int Add = 10;
        public const int Update = 20;
        public const int Finish = 10;
    }

    public async Task<MediaLibrarySyncResultViewModel> SyncAll(int[]? ids, Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct)
    {
        var data = await (ids == null ? GetAll() : GetByKeys(ids));
        var templateIds = data.Select(d => d.TemplateId).OfType<int>().ToHashSet();
        var templateMap =
            (await TemplateService.GetByKeys(templateIds.ToArray(), MediaLibraryTemplateAdditionalItem.ChildTemplate))
            .ToDictionary(d => d.Id, d => d);
        var mlResourceMap = (await resourceService.GetAllGeneratedByMediaLibraryV2(ids, ResourceAdditionalItem.All))
            .GroupBy(d => d.MediaLibraryId)
            .ToDictionary(d => d.Key, d => d.ToList());
        var syncOptions = resourceOptions.Value.SynchronizationOptions;

        await using var progressor = new BProgressor(onProgressChange);

        var progressPerMediaLibrary = ids?.Length > 0 ? 100f / ids.Length : 0;

        var ret = new MediaLibrarySyncResultViewModel();
        for (var index = 0; index < data.Count; index++)
        {
            var baseProgress = progressPerMediaLibrary * index;
            await using var mlProgressor = progressor.CreateNewScope(baseProgress, progressPerMediaLibrary);

            var ml = data[index];
            if (ml.TemplateId.HasValue && templateMap.TryGetValue(ml.TemplateId.Value, out var template))
            {
                #region Discovery

                var discoveryProgressor = mlProgressor.CreateNewScope(0, ProgressPerMediaLibrary.Discovery);
                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_DiscoverResources(ml.Name));
                }

                var maxThreads = Math.Max(1,
                    resourceOptions.Value.SynchronizationOptions?.MaxThreads ??
                    (int)(Environment.ProcessorCount * 0.4));
                var treeTempSyncResources = new List<TempSyncResource>();
                var totalDiscoveryProgressPerPath = ProgressPerMediaLibrary.Discovery / (float)ml.Paths.Count;
                for (var i = 0; i < ml.Paths.Count; i++)
                {
                    var path = ml.Paths[i];
                    var discoveryProgressorByPath =
                        discoveryProgressor.CreateNewScope(totalDiscoveryProgressPerPath * i,
                            totalDiscoveryProgressPerPath);
                    var rs = await template.DiscoverResources(path,
                        async p => await discoveryProgressorByPath.Set(p), null, pt, ct, maxThreads);
                    treeTempSyncResources.AddRange(rs);
                }

                var flattenScannedResources = treeTempSyncResources.SelectMany(x => x.Flatten(ml.Id, null)).ToList();
                // var scannedResourcePaths = flattenScannedResources.Select(d => d.Path).ToHashSet();
                var scannedResourceMap = flattenScannedResources.ToDictionary(d => d.Path);

                var mlDbResources = mlResourceMap.GetValueOrDefault(ml.Id) ?? [];
                var mlDbResourcePathMap = mlDbResources.ToDictionary(d => d.Path, d => d);
                var mlDbResourceMap = mlDbResources.ToDictionary(d => d.Id);
                // var mlDbResourcePaths = mlDbResourcePathMap.Keys.ToHashSet();
                // scanned - db, and scanned is new when db is null.
                var resourceTrackingMap = new Dictionary<Resource, Resource?>();
                var trackingDbResources = new HashSet<Resource>();
                foreach (var tr in flattenScannedResources)
                {
                    // 1. track by marker file
                    if (resourceOptions.Value.KeepResourcesOnPathChange && !tr.IsFile)
                    {
                        try
                        {
                            var marker = Path.Combine(tr.Path, InternalOptions.ResourceMarkerFileName);
                            if (File.Exists(marker))
                            {
                                var json = await File.ReadAllTextAsync(marker, ct);
                                var markerData = Newtonsoft.Json.Linq.JObject.Parse(json);
                                var rIds = markerData["ids"]?.ToObject<int[]>();
                                if (rIds != null)
                                {
                                    foreach (var rId in rIds)
                                    {
                                        var existed = mlDbResourceMap.GetValueOrDefault(rId);
                                        if (existed != null && trackingDbResources.Add(existed))
                                        {
                                            resourceTrackingMap[tr] = existed;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // ignore parse errors
                        }
                    }

                    // 2. track by path
                    if (!resourceTrackingMap.ContainsKey(tr))
                    {
                        var dbr = mlDbResourcePathMap.GetValueOrDefault(tr.Path);
                        if (dbr != null && trackingDbResources.Add(dbr))
                        {
                            resourceTrackingMap[tr] = dbr;
                        }
                        else
                        {
                            resourceTrackingMap[tr] = null;
                        }
                    }
                }

                await discoveryProgressor.DisposeAsync();
                baseProgress += ProgressPerMediaLibrary.Discovery;

                #endregion

                #region Clean

                var unknownDbResources = mlDbResources.Except(trackingDbResources).ToList();
                var changedResources = new HashSet<Resource>();
                var cleanProgressor = mlProgressor.CreateNewScope(baseProgress, ProgressPerMediaLibrary.Clean);
                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_CleanupResources(ml.Name));
                }

                var resourcesToBeDeleted = new List<Resource>();
                foreach (var unknownDbResource in unknownDbResources)
                {
                    if (unknownDbResource.ShouldBeDeletedSinceFileNotFound(syncOptions))
                    {
                        resourcesToBeDeleted.Add(unknownDbResource);
                    }
                    else
                    {
                        if (unknownDbResource.Tags.Add(ResourceTag.PathDoesNotExist))
                        {
                            changedResources.Add(unknownDbResource);
                        }
                    }
                }

                if (resourcesToBeDeleted.Any())
                {
                    await resourceService.DeleteByKeys(resourcesToBeDeleted.Select(r => r.Id).ToArray(), false);

                    ret.Deleted += resourcesToBeDeleted.Count;
                }

                var remainingUnknownResourceCount = unknownDbResources.Count - resourcesToBeDeleted.Count;

                await cleanProgressor.DisposeAsync();
                baseProgress += ProgressPerMediaLibrary.Clean;

                #endregion

                #region Add

                var addProgressor = mlProgressor.CreateNewScope(baseProgress, ProgressPerMediaLibrary.Add);
                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_AddResources(ml.Name));
                }

                var scannedNewResources = resourceTrackingMap.Where(d => d.Value == null).Select(d => d.Key).ToList();
                await resourceService.AddOrPutRange(scannedNewResources);
                var allPathIdMap = mlDbResources.Concat(scannedNewResources).ToDictionary(d => d.Path, d => d.Id);
                ret.Added += scannedNewResources.Count;

                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_UpdateResources(ml.Name));
                }

                await addProgressor.DisposeAsync();
                baseProgress += ProgressPerMediaLibrary.Add;

                #endregion

                #region Merge and update

                var updateProgressor = mlProgressor.CreateNewScope(baseProgress, ProgressPerMediaLibrary.Update);
                foreach (var (scanned, db) in resourceTrackingMap)
                {
                    if (db != null && db.MergeOnSynchronization(scanned))
                    {
                        db.UpdatedAt = DateTime.Now;
                        changedResources.Add(db);
                    }
                }

                ret.Updated += changedResources.Count;

                foreach (var dbResource in scannedNewResources.Concat(resourceTrackingMap.Values.OfType<Resource>()))
                {
                    var tmpResource = scannedResourceMap[dbResource.Path];
                    int? parentId = tmpResource.Parent == null ? null : allPathIdMap[tmpResource.Parent.Path];
                    if (parentId != dbResource.ParentId)
                    {
                        dbResource.ParentId = parentId;
                        changedResources.Add(dbResource);
                    }
                }

                await updateProgressor.DisposeAsync();
                baseProgress += ProgressPerMediaLibrary.Update;

                #endregion

                #region Finishing up

                var finishProgressor = mlProgressor.CreateNewScope(baseProgress, ProgressPerMediaLibrary.Finish);

                if (onProcessChange != null)
                {
                    await onProcessChange(localizer.SyncMediaLibrary_TaskProcess_AlmostComplete(ml.Name));
                }

                await resourceService.AddOrPutRange(changedResources.ToList());
                var resourceCount = resourceTrackingMap.Count + remainingUnknownResourceCount;
                await MarkAsSynced(ml.Id, resourceCount, template.GetSyncVersion());

                // clean cache
                await cacheOrm.RemoveByKeys(resourceTrackingMap.Values.OfType<Resource>().Select(r => r.Id));
                await finishProgressor.DisposeAsync();

                #endregion

                if (onProcessChange != null)
                {
                    await onProcessChange(null);
                }
            }
        }

        await resourceService.RefreshParentTag();

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
                        await args.UpdateTask(t => { t.Data = result; });
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
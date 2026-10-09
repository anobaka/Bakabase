using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using StackExchange.Profiling;

namespace Bakabase.InsideWorld.Business.Services;

public class PathMarkService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, PathMarkDbModel, int> orm,
    IServiceProvider serviceProvider,
    IBOptions<ResourceOptions> resourceOptions,
    IHubContext<WebGuiHub, IWebGuiClient> uiHub,
    IExtensionGroupService extensionGroupService,
    IUserStoragePolicy storage
) : ScopedService(serviceProvider), IPathMarkService where TDbContext : DbContext
{
    private async Task NotifyPathMarkStatusChange(PathMarkDbModel dbModel)
    {
        var mark = dbModel.ToDomainModel();
        await uiHub.Clients.All.GetIncrementalData("PathMark", mark);
    }

    private async Task NotifyPathMarkStatusChangeBatch(IEnumerable<PathMarkDbModel> dbModels)
    {
        foreach (var dbModel in dbModels)
        {
            await NotifyPathMarkStatusChange(dbModel);
        }
    }

    private async Task TriggerImmediateSyncIfEnabled(int markId)
    {
        if (resourceOptions.Value.SynchronizationOptions?.SyncMarksImmediately != true) return;

        try
        {
            // Use IServiceProvider to avoid circular dependency
            var syncService = serviceProvider.GetService<IPathMarkSyncService>();
            if (syncService != null)
            {
                await syncService.EnqueueSync(markId);
            }
        }
        catch
        {
            // Ignore sync trigger errors
        }
    }

    public async Task<List<PathMark>> GetAll(Expression<Func<PathMarkDbModel, bool>>? filter = null, PathMarkAdditionalItem additionalItems = PathMarkAdditionalItem.None)
    {
        using (MiniProfiler.Current.Step("PathMarkService.GetAll"))
        {
            List<PathMarkDbModel> dbModels;
            using (MiniProfiler.Current.Step("ORM.GetAll"))
            {
                dbModels = filter != null
                    ? await orm.GetAll(filter)
                    : await orm.GetAll();
            }

            List<PathMark> marks;
            using (MiniProfiler.Current.Step($"ToDomainModel ({dbModels.Count} marks)"))
            {
                marks = dbModels.Select(d => d.ToDomainModel()).ToList();
            }

            // Populate additional items if requested
            if (additionalItems != PathMarkAdditionalItem.None)
            {
                await PopulateAdditionalItems(marks, additionalItems);
            }

            return marks;
        }
    }

    private async Task PopulateAdditionalItems(List<PathMark> marks, PathMarkAdditionalItem additionalItems)
    {
        using (MiniProfiler.Current.Step($"PopulateAdditionalItems ({marks.Count} marks)"))
        {
            // Cache parsed configs to avoid double JSON deserialization
            var propertyConfigs = new Dictionary<int, PropertyMarkConfig>();
            var mediaLibraryConfigs = new Dictionary<int, MediaLibraryMarkConfig>();
            var propertyKeys = new HashSet<(PropertyPool pool, int id)>();
            var mediaLibraryIds = new HashSet<int>();

            using (MiniProfiler.Current.Step("CollectIds"))
            {
                foreach (var mark in marks)
                {
                    try
                    {
                        if (mark.Type == PathMarkType.Property && additionalItems.HasFlag(PathMarkAdditionalItem.Property))
                        {
                            var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
                            if (config != null)
                            {
                                propertyConfigs[mark.Id] = config;
                                propertyKeys.Add((config.Pool, config.PropertyId));
                            }
                        }
                        else if (mark.Type == PathMarkType.MediaLibrary && additionalItems.HasFlag(PathMarkAdditionalItem.MediaLibrary))
                        {
                            var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
                            if (config != null)
                            {
                                mediaLibraryConfigs[mark.Id] = config;
                                if (config.MediaLibraryId.HasValue)
                                {
                                    mediaLibraryIds.Add(config.MediaLibraryId.Value);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore JSON parse errors
                    }
                }
            }

            // Load properties and media libraries in parallel
            Dictionary<(PropertyPool pool, int id), Property> propertiesMap = new();
            Dictionary<int, MediaLibraryV2> mediaLibrariesMap = new();

            var loadPropertiesTask = Task.CompletedTask;
            var loadMediaLibrariesTask = Task.CompletedTask;

            if (additionalItems.HasFlag(PathMarkAdditionalItem.Property) && propertyKeys.Count > 0)
            {
                loadPropertiesTask = Task.Run(async () =>
                {
                    using (MiniProfiler.Current.Step($"LoadProperties ({propertyKeys.Count} keys)"))
                    {
                        var propertyService = serviceProvider.GetService<IPropertyService>();
                        if (propertyService != null)
                        {
                            var pools = propertyKeys.Select(k => k.pool).Distinct().ToList();
                            var tasks = pools.Select(async pool =>
                            {
                                var properties = await propertyService.GetProperties(pool);
                                return (pool, properties);
                            });

                            var results = await Task.WhenAll(tasks);
                            foreach (var (pool, properties) in results)
                            {
                                foreach (var prop in properties)
                                {
                                    propertiesMap[(prop.Pool, prop.Id)] = prop;
                                }
                            }
                        }
                    }
                });
            }

            if (additionalItems.HasFlag(PathMarkAdditionalItem.MediaLibrary) && mediaLibraryIds.Count > 0)
            {
                loadMediaLibrariesTask = Task.Run(async () =>
                {
                    using (MiniProfiler.Current.Step($"LoadMediaLibraries ({mediaLibraryIds.Count} ids)"))
                    {
                        var mediaLibraryService = serviceProvider.GetService<IMediaLibraryV2Service>();
                        if (mediaLibraryService != null)
                        {
                            var libraries = await mediaLibraryService.GetAll();
                            foreach (var lib in libraries.Where(l => mediaLibraryIds.Contains(l.Id)))
                            {
                                mediaLibrariesMap[lib.Id] = lib;
                            }
                        }
                    }
                });
            }

            await Task.WhenAll(loadPropertiesTask, loadMediaLibrariesTask);

            // Assign loaded data to marks using cached configs
            using (MiniProfiler.Current.Step("AssignData"))
            {
                foreach (var mark in marks)
                {
                    if (mark.Type == PathMarkType.Property && propertyConfigs.TryGetValue(mark.Id, out var propConfig))
                    {
                        if (propertiesMap.TryGetValue((propConfig.Pool, propConfig.PropertyId), out var prop))
                        {
                            mark.Property = prop;
                        }
                    }
                    else if (mark.Type == PathMarkType.MediaLibrary && mediaLibraryConfigs.TryGetValue(mark.Id, out var mlConfig))
                    {
                        if (mlConfig.MediaLibraryId.HasValue && mediaLibrariesMap.TryGetValue(mlConfig.MediaLibraryId.Value, out var lib))
                        {
                            mark.MediaLibrary = lib;
                        }
                    }
                }
            }
        }
    }

    public async Task<PathMark?> Get(int id)
    {
        var dbModel = await orm.GetByKey(id);
        return dbModel?.ToDomainModel();
    }

    public async Task<List<PathMark>> GetByPath(string path, bool includeDeleted = false)
    {
        var normalizedPath = path.StandardizePath()!;
        var query = await orm.GetAll(m => m.Path == normalizedPath);

        if (!includeDeleted)
        {
            query = query.Where(m => !m.IsDeleted).ToList();
        }

        return query.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<List<string>> GetAllPaths()
    {
        var allMarks = await orm.GetAll(m => !m.IsDeleted);
        return allMarks.Select(m => m.Path).Distinct().ToList();
    }

    public async Task<List<PathMark>> GetPendingMarks()
    {
        var dbModels = await orm.GetAll(m =>
            m.SyncStatus == PathMarkSyncStatus.Pending ||
            m.SyncStatus == PathMarkSyncStatus.PendingDelete);

        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<List<PathMark>> GetBySyncStatus(PathMarkSyncStatus status)
    {
        var dbModels = await orm.GetAll(m => m.SyncStatus == status);
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<PathMark> Add(PathMark mark)
    {
        storage.EnsurePathAllowed(mark.Path);
        using (MiniProfiler.Current.Step("PathMarkService.Add"))
        {
            using (MiniProfiler.Current.Step("Prepare"))
            {
                mark.Path = mark.Path.StandardizePath()!;
                mark.CreatedAt = DateTime.UtcNow;
                mark.UpdatedAt = DateTime.UtcNow;
                mark.SyncStatus = PathMarkSyncStatus.Pending;
            }

            var dbModel = mark.ToDbModel();

            using (MiniProfiler.Current.Step("ORM.Add"))
            {
                await orm.Add(dbModel);
            }

            using (MiniProfiler.Current.Step("Detach"))
            {
                orm.DbContext.Detach(dbModel);
            }

            mark.Id = dbModel.Id;

            // Trigger immediate sync if enabled
            using (MiniProfiler.Current.Step("TriggerImmediateSync"))
            {
                await TriggerImmediateSyncIfEnabled(mark.Id);
            }

            return mark;
        }
    }

    public async Task<List<PathMark>> AddRange(List<PathMark> marks)
    {
        foreach (var mark in marks) storage.EnsurePathAllowed(mark.Path);
        using (MiniProfiler.Current.Step($"PathMarkService.AddRange ({marks.Count} marks)"))
        {
            using (MiniProfiler.Current.Step("Prepare"))
            {
                var now = DateTime.UtcNow;
                foreach (var mark in marks)
                {
                    mark.Path = mark.Path.StandardizePath()!;
                    mark.CreatedAt = now;
                    mark.UpdatedAt = now;
                    mark.SyncStatus = PathMarkSyncStatus.Pending;
                }
            }

            var dbModels = marks.Select(m => m.ToDbModel()).ToList();

            using (MiniProfiler.Current.Step("ORM.AddRange"))
            {
                await orm.AddRange(dbModels);
            }

            for (int i = 0; i < marks.Count; i++)
            {
                marks[i].Id = dbModels[i].Id;
            }

            return marks;
        }
    }

    public async Task Update(PathMark mark)
    {
        storage.EnsurePathAllowed(mark.Path);
        mark.Path = mark.Path.StandardizePath()!;
        mark.UpdatedAt = DateTime.UtcNow;

        // If mark is already synced and we're updating it, set to pending again
        if (mark.SyncStatus == PathMarkSyncStatus.Synced)
        {
            mark.SyncStatus = PathMarkSyncStatus.Pending;
        }

        await orm.Update(mark.ToDbModel());

        // Trigger immediate sync if enabled
        await TriggerImmediateSyncIfEnabled(mark.Id);
    }

    public async Task SoftDelete(int id)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel != null)
        {
            dbModel.IsDeleted = true;
            dbModel.DeletedAt = DateTime.UtcNow;
            dbModel.SyncStatus = PathMarkSyncStatus.PendingDelete;
            dbModel.UpdatedAt = DateTime.UtcNow;
            await orm.Update(dbModel);

            // Trigger immediate sync if enabled
            await TriggerImmediateSyncIfEnabled(id);
        }
    }

    public async Task SoftDeleteByPath(string path)
    {
        var normalizedPath = path.StandardizePath()!;
        var marks = await orm.GetAll(m => m.Path == normalizedPath && !m.IsDeleted);
        var now = DateTime.UtcNow;

        foreach (var mark in marks)
        {
            mark.IsDeleted = true;
            mark.DeletedAt = now;
            mark.SyncStatus = PathMarkSyncStatus.PendingDelete;
            mark.UpdatedAt = now;
            await orm.Update(mark);
        }
    }

    public async Task HardDelete(int id)
    {
        await orm.RemoveByKey(id);
    }

    public async Task HardDeleteBatch(IEnumerable<int> ids)
    {
        await orm.RemoveByKeys(ids);
    }

    public async Task MarkAsSyncing(int id)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel != null)
        {
            dbModel.SyncStatus = PathMarkSyncStatus.Syncing;
            dbModel.UpdatedAt = DateTime.UtcNow;
            await orm.Update(dbModel);
            await NotifyPathMarkStatusChange(dbModel);
        }
    }

    public async Task MarkAsSynced(int id)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel != null)
        {
            dbModel.SyncStatus = PathMarkSyncStatus.Synced;
            dbModel.SyncedAt = DateTime.UtcNow;
            dbModel.SyncError = null;
            dbModel.UpdatedAt = DateTime.UtcNow;
            await orm.Update(dbModel);
            await NotifyPathMarkStatusChange(dbModel);
        }
    }

    public async Task MarkAsSyncedBatch(IEnumerable<int> ids)
    {
        var idSet = ids.ToHashSet();
        if (idSet.Count == 0) return;

        var dbModels = await orm.GetAll(m => idSet.Contains(m.Id));
        var now = DateTime.UtcNow;

        foreach (var dbModel in dbModels)
        {
            dbModel.SyncStatus = PathMarkSyncStatus.Synced;
            dbModel.SyncedAt = now;
            dbModel.SyncError = null;
            dbModel.UpdatedAt = now;
        }

        await orm.UpdateRange(dbModels);
        await NotifyPathMarkStatusChangeBatch(dbModels);
    }

    public async Task MarkAsFailed(int id, string? error)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel != null)
        {
            dbModel.SyncStatus = PathMarkSyncStatus.Failed;
            dbModel.SyncError = error;
            dbModel.UpdatedAt = DateTime.UtcNow;
            await orm.Update(dbModel);
            await NotifyPathMarkStatusChange(dbModel);
        }
    }

    public async Task MarkAsPending(int id)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel != null)
        {
            dbModel.SyncStatus = PathMarkSyncStatus.Pending;
            dbModel.SyncError = null;
            dbModel.UpdatedAt = DateTime.UtcNow;
            await orm.Update(dbModel);
            await NotifyPathMarkStatusChange(dbModel);
        }
    }

    public async Task MarkAsPendingBatch(IEnumerable<int> ids)
    {
        var idSet = ids.ToHashSet();
        if (idSet.Count == 0) return;

        var dbModels = await orm.GetAll(m => idSet.Contains(m.Id));
        var now = DateTime.UtcNow;

        // Skip marks that are pending deletion — resetting them to Pending
        // would prevent them from being hard-deleted during sync.
        var modelsToUpdate = new List<PathMarkDbModel>();
        foreach (var dbModel in dbModels)
        {
            if (dbModel.SyncStatus == PathMarkSyncStatus.PendingDelete) continue;
            dbModel.SyncStatus = PathMarkSyncStatus.Pending;
            dbModel.SyncError = null;
            dbModel.UpdatedAt = now;
            modelsToUpdate.Add(dbModel);
        }

        if (modelsToUpdate.Count > 0)
        {
            await orm.UpdateRange(modelsToUpdate);
            await NotifyPathMarkStatusChangeBatch(modelsToUpdate);
        }
    }

    public async Task<int> GetPendingCount()
    {
        var allMarks = await orm.GetAll(m =>
            m.SyncStatus == PathMarkSyncStatus.Pending ||
            m.SyncStatus == PathMarkSyncStatus.PendingDelete);
        return allMarks.Count;
    }

    public async Task<List<PathMarkPreviewResult>> PreviewMatchedPaths(PathMarkPreviewRequest request,
        CancellationToken ct = default)
    {
        var rootPath = request.Path.StandardizePath()!;
        var results = new List<PathMarkPreviewResult>();

        if (!Directory.Exists(rootPath))
        {
            return results;
        }

        // Step 1: Parse base match config (common to all mark types)
        PathMatchMode matchMode;
        int? layer = null;
        string? regex = null;
        PathMarkApplyScope? applyScope = null;

        // Step 2: Parse type-specific filter/value config
        PathFilterFsType? fsTypeFilter = null;
        List<string>? extensions = null;

        PropertyValueType? valueType = null;
        object? fixedValue = null;
        int? valueLayer = null;
        string? valueRegex = null;
        PropertyMarkConfig? propertyConfig = null;
        CustomProperty? propertyValueDefinition = null;
        IStandardValueService? propertyValueConverter = null;

        switch (request.Type)
        {
            case PathMarkType.Resource:
            {
                var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(request.ConfigJson);
                if (config == null) return results;

                matchMode = config.MatchMode;
                layer = config.Layer;
                regex = config.Regex;
                applyScope = config.ApplyScope;
                fsTypeFilter = config.FsTypeFilter;
                extensions = await ResolveExtensions(config.Extensions, config.ExtensionGroupIds);
                break;
            }
            case PathMarkType.Property:
            {
                var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(request.ConfigJson);
                if (config == null) return results;

                propertyConfig = config;
                (matchMode, layer, regex, applyScope) = config.GetEffectiveApplicability();
                valueType = config.ValueType;
                fixedValue = config.FixedValue;
                valueLayer = config.ValueLayer;
                valueRegex = config.ValueRegex;
                if (config.Pool == PropertyPool.Custom &&
                    config.UsesResourceRelativeValueRegex() &&
                    !string.IsNullOrEmpty(config.ValueRegex))
                {
                    propertyValueDefinition = await serviceProvider.GetRequiredService<ICustomPropertyService>()
                        .GetByKey(config.PropertyId);
                    propertyValueConverter = serviceProvider.GetRequiredService<IStandardValueService>();
                }
                break;
            }
            case PathMarkType.MediaLibrary:
            {
                var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(request.ConfigJson);
                if (config == null) return results;

                matchMode = config.MatchMode;
                layer = config.Layer;
                regex = config.Regex;
                applyScope = config.ApplyScope;
                valueType = config.ValueType;
                fixedValue = config.MediaLibraryId; // For Fixed, the value is MediaLibraryId
                valueLayer = config.LayerToMediaLibrary;
                valueRegex = config.RegexToMediaLibrary;
                break;
            }
            default:
                return results;
        }

        // Step 3: Get matched paths using common logic
        var matchedPaths = GetMatchingPaths(rootPath, matchMode, layer, regex, ct, applyScope, fsTypeFilter,
            extensions);
        if (request.Type == PathMarkType.Property && matchMode == PathMatchMode.Layer && layer != -1 &&
            applyScope == PathMarkApplyScope.MatchedAndSubdirectories)
        {
            foreach (var match in matchedPaths.Where(Directory.Exists).ToList())
            {
                ct.ThrowIfCancellationRequested();
                matchedPaths.AddRange(GetAllEntries(match, fsTypeFilter, extensions, ct));
            }
        }

        var rootSegments = rootPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        // Step 4: Build results based on mark type
        foreach (var matchedPath in matchedPaths)
        {
            var matchedSegments = matchedPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            var result = new PathMarkPreviewResult { Path = matchedPath };

            if (request.Type == PathMarkType.Resource)
            {
                // For Resource: calculate resource layer index
                (result.ResourceLayerIndex, result.ResourceSegmentName) = CalculateResourceLayerInfo(
                    rootPath, matchedPath, rootSegments, matchedSegments, matchMode, layer, regex);
            }
            else if (request.Type == PathMarkType.Property)
            {
                result.PropertyValue = await CalculatePropertyMarkValue(
                    rootPath, matchedPath, rootSegments, matchedSegments,
                    propertyConfig!, propertyValueDefinition, propertyValueConverter);
            }
            else if (request.Type == PathMarkType.MediaLibrary)
            {
                result.PropertyValue = CalculatePropertyValue(
                    rootPath, matchedPath, rootSegments, matchedSegments,
                    valueType, fixedValue, valueLayer, valueRegex);
            }

            results.Add(result);
        }

        return results;
    }

    private async Task<List<string>?> ResolveExtensions(List<string>? extensions, List<int>? extensionGroupIds)
    {
        if (extensionGroupIds == null || extensionGroupIds.Count == 0)
            return extensions;

        var allGroups = await extensionGroupService.GetAll();
        var groupMap = allGroups.ToDictionary(g => g.Id, g => g);
        var groupExtensions = extensionGroupIds
            .Select(id => groupMap.GetValueOrDefault(id))
            .Where(g => g?.Extensions != null)
            .SelectMany(g => g!.Extensions!);

        var merged = new List<string>(extensions ?? []);
        merged.AddRange(groupExtensions);
        return merged.Count > 0 ? merged.Distinct().ToList() : null;
    }

    /// <summary>
    /// Common path matching logic for all mark types
    /// </summary>
    private List<string> GetMatchingPaths(string rootPath, PathMatchMode matchMode, int? layer, string? regex,
        CancellationToken ct,
        PathMarkApplyScope? applyScope = null, PathFilterFsType? fsTypeFilter = null, List<string>? extensions = null)
    {
        storage.EnsurePathAllowed(rootPath);
        var matchedPaths = new List<string>();

        try
        {
            if (matchMode == PathMatchMode.Layer)
            {
                if (layer == null) return matchedPaths;

                if (layer == -1)
                {
                    // All layers - recursively get all directories/files
                    matchedPaths.AddRange(GetAllEntries(rootPath, fsTypeFilter, extensions, ct));
                }
                else
                {
                    // Specific layer
                    matchedPaths.AddRange(GetEntriesAtLayer(rootPath, layer.Value, fsTypeFilter, extensions, ct));
                }
            }
            else if (matchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(regex))
            {
                // For MatchedOnly: modify regex to not match sub-paths
                // by ensuring no path separator follows the match
                var regexPattern = regex;
                if (applyScope == PathMarkApplyScope.MatchedOnly)
                {
                    // If pattern ends with $, replace it; otherwise append
                    regexPattern = regexPattern.EndsWith("$")
                        ? regexPattern.Substring(0, regexPattern.Length - 1) + @"[^/\\]*$"
                        : regexPattern + @"[^/\\]*$";
                }

                var regexObj = new Regex(regexPattern, RegexOptions.IgnoreCase);
                var entries = GetAllEntries(rootPath, fsTypeFilter, extensions, ct);

                foreach (var entry in entries)
                {
                    var relativePath = entry.Substring(rootPath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (regexObj.IsMatch(relativePath))
                    {
                        matchedPaths.Add(entry);
                    }
                }
            }
        }
        // Cancellation is not an access error. These catches exist to let an unreadable directory
        // be skipped rather than fail the preview, and a broad one swallows the abort too.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Ignore access errors
        }

        return matchedPaths.Where(path => storage.IsPathAllowed(path)).ToList();
    }

    /// <summary>
    /// Calculate resource layer info for Resource mark type
    /// </summary>
    private (int? layerIndex, string? segmentName) CalculateResourceLayerInfo(
        string rootPath, string matchedPath, string[] rootSegments, string[] matchedSegments,
        PathMatchMode matchMode, int? layer, string? regex)
    {
        int? resourceLayerIndex = null;
        string? resourceSegmentName = null;

        if (matchMode == PathMatchMode.Layer && layer.HasValue)
        {
            if (layer > 0)
            {
                // Layer is 1-based (1 = first level subdirectory)
                var targetIndex = rootSegments.Length + layer.Value - 1;
                if (targetIndex < matchedSegments.Length)
                {
                    resourceLayerIndex = targetIndex;
                    resourceSegmentName = matchedSegments[targetIndex];
                }
            }
            else if (layer == -1)
            {
                // All layers - the resource is the matched path itself
                resourceLayerIndex = matchedSegments.Length - 1;
                resourceSegmentName = matchedSegments[matchedSegments.Length - 1];
            }
        }
        else if (matchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(regex))
        {
            // For regex, find which segment matches
            var relativePath = matchedPath.Substring(rootPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relativeSegments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            var regexObj = new Regex(regex, RegexOptions.IgnoreCase);
            for (int i = 0; i < relativeSegments.Length; i++)
            {
                if (regexObj.IsMatch(relativeSegments[i]))
                {
                    resourceLayerIndex = rootSegments.Length + i;
                    resourceSegmentName = relativeSegments[i];
                    break;
                }
            }

            // If no segment matches, use the last segment
            if (!resourceLayerIndex.HasValue && relativeSegments.Length > 0)
            {
                resourceLayerIndex = rootSegments.Length + relativeSegments.Length - 1;
                resourceSegmentName = relativeSegments[relativeSegments.Length - 1];
            }
        }

        return (resourceLayerIndex, resourceSegmentName);
    }

    /// <summary>
    /// Calculate a property-mark value independently from its applicability selector.
    /// </summary>
    private async Task<string?> CalculatePropertyMarkValue(
        string rootPath, string matchedPath, string[] rootSegments, string[] matchedSegments,
        PropertyMarkConfig config, CustomProperty? customProperty, IStandardValueService? valueConverter)
    {
        if (config.ValueType == PropertyValueType.Fixed)
        {
            return config.FixedValue?.ToString();
        }

        if (config.ValueType != PropertyValueType.Dynamic)
        {
            return null;
        }

        if (config.UsesResourceRelativeValueRegex())
        {
            if (string.IsNullOrEmpty(config.ValueRegex) || customProperty == null || valueConverter == null)
            {
                return null;
            }

            var relativePath = matchedPath.Length <= rootPath.Length
                ? string.Empty
                : matchedPath[rootPath.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var values = Regex.Matches(relativePath, config.ValueRegex)
                .SelectMany(match => match.Groups.Values.Skip(1).Select(group => group.Value))
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct()
                .ToList();
            var bizValue = values.Count == 0
                ? null
                : await valueConverter.Convert(
                    values, StandardValueType.ListString, customProperty.Type.GetBizValueType());

            return bizValue?.SerializeAsStandardValue(customProperty.Type.GetBizValueType());
        }

        // Modern property regex extraction is intentionally mark-relative. It remains independent
        // from the applicability selector, which may use a layer at the same time.
        if (!string.IsNullOrEmpty(config.ValueRegex))
        {
            var markDirectoryName = Path.GetFileName(rootPath);
            var regex = new Regex(config.ValueRegex, RegexOptions.IgnoreCase);
            var match = regex.Match(markDirectoryName);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
            if (match.Success)
            {
                return match.Value;
            }

            return null;
        }

        if (!config.ValueLayer.HasValue)
        {
            return null;
        }

        var layer = config.ValueLayer.Value;
        if (layer == 0)
        {
            return Path.GetFileName(rootPath);
        }

        // Positive = forward from root, negative = backward
        var targetIndex = rootSegments.Length + layer - 1;
        if (targetIndex >= 0 && targetIndex < matchedSegments.Length)
        {
            return matchedSegments[targetIndex];
        }
        return null;
    }

    /// <summary>
    /// Calculate media-library mark values using the existing extraction semantics.
    /// </summary>
    private string? CalculatePropertyValue(
        string rootPath, string matchedPath, string[] rootSegments, string[] matchedSegments,
        PropertyValueType? valueType, object? fixedValue, int? valueLayer, string? valueRegex)
    {
        if (valueType == PropertyValueType.Fixed)
        {
            return fixedValue?.ToString();
        }

        if (valueType != PropertyValueType.Dynamic)
        {
            return null;
        }

        // Dynamic value extraction
        if (valueLayer.HasValue)
        {
            var layer = valueLayer.Value;
            // 0 = matched item itself
            if (layer == 0)
            {
                var relativePath = matchedPath.Substring(rootPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fileName = Path.GetFileName(relativePath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    return fileName;
                }
                return relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            }
            else
            {
                // Positive = forward from root, negative = backward
                var targetIndex = rootSegments.Length + layer - 1;
                if (targetIndex >= 0 && targetIndex < matchedSegments.Length)
                {
                    return matchedSegments[targetIndex];
                }
            }
        }
        else if (!string.IsNullOrEmpty(valueRegex))
        {
            var relativePath = matchedPath.Substring(rootPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var regex = new Regex(valueRegex, RegexOptions.IgnoreCase);
            var match = regex.Match(relativePath);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
            if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }


    private static bool MatchesEntry(bool directory, string path, PathFilterFsType? filter, List<string>? extensions) =>
        directory ? filter is null or PathFilterFsType.Directory :
        filter is null or PathFilterFsType.File &&
        (extensions is not {Count: > 0} || extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

    private IEnumerable<(string Path, bool IsDirectory, int Depth)> EnumerateMountedEntries(string root, int maxDepth,
        CancellationToken ct)
    {
        var pending = new Stack<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) {Path.GetFullPath(root)};
        pending.Push((root, 0));
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            if (!storage.IsPathAllowed(current.Path)) continue;
            foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
            {
                ct.ThrowIfCancellationRequested();
                if (!storage.IsPathAllowed(entry)) continue;
                var directory = Directory.Exists(entry);
                var depth = current.Depth + 1;
                yield return (entry, directory, depth);
                if (!directory || depth >= maxDepth) continue;
                var info = new DirectoryInfo(entry);
                var resolved = info.LinkTarget == null ? info.FullName : info.ResolveLinkTarget(true)?.FullName;
                if (resolved != null && visited.Add(resolved)) pending.Push((entry, depth));
            }
        }
    }

    private List<string> GetAllEntries(string rootPath, PathFilterFsType? fsTypeFilter, List<string>? extensions,
        CancellationToken ct)
    {
        storage.EnsurePathAllowed(rootPath);
        var entries = new List<string>();

        try
        {
            if (storage.IsRestricted)
                return EnumerateMountedEntries(rootPath, int.MaxValue, ct)
                    .Where(entry => MatchesEntry(entry.IsDirectory, entry.Path, fsTypeFilter, extensions))
                    .Select(entry => entry.Path.StandardizePath()!).ToList();
            // Enumerate lazily and check per entry rather than calling Get*: the walk itself is the
            // long part, so materialising it first would leave nothing for the check to shorten.
            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
            {
                foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    entries.Add(dir);
                }
            }

            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
            {
                var hasExtensionFilter = extensions is {Count: > 0};
                foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();

                    if (!hasExtensionFilter ||
                        extensions!.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        entries.Add(file);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList();
    }

    private List<string> GetEntriesAtLayer(string rootPath, int layer, PathFilterFsType? fsTypeFilter,
        List<string>? extensions, CancellationToken ct)
    {
        storage.EnsurePathAllowed(rootPath);
        var entries = new List<string>();

        try
        {
            if (storage.IsRestricted && layer > 0)
                return EnumerateMountedEntries(rootPath, layer, ct)
                    .Where(entry => entry.Depth == layer && MatchesEntry(entry.IsDirectory, entry.Path, fsTypeFilter, extensions))
                    .Select(entry => entry.Path.StandardizePath()!).ToList();
            var currentPaths = new List<string> { rootPath };

            for (int i = 1; i <= layer; i++)
            {
                var nextPaths = new List<string>();
                foreach (var path in currentPaths)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        nextPaths.AddRange(Directory.GetDirectories(path));
                        if (i == layer && (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File))
                        {
                            var files = Directory.GetFiles(path);
                            if (extensions != null && extensions.Count > 0)
                            {
                                files = files.Where(f => extensions.Any(ext =>
                                    f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToArray();
                            }
                            nextPaths.AddRange(files);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore access errors
                    }
                }
                currentPaths = nextPaths;
            }

            if (fsTypeFilter == PathFilterFsType.Directory)
            {
                entries.AddRange(currentPaths.Where(Directory.Exists));
            }
            else if (fsTypeFilter == PathFilterFsType.File)
            {
                entries.AddRange(currentPaths.Where(File.Exists));
            }
            else
            {
                entries.AddRange(currentPaths);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList();
    }


    public async Task MigratePath(string oldPath, string newPath)
    {
        storage.EnsurePathAllowed(newPath);
        var normalizedOldPath = oldPath.StandardizePath()!;
        var normalizedNewPath = newPath.StandardizePath()!;

        // Get all marks for the old path (including deleted ones)
        var marks = await orm.GetAll(m => m.Path == normalizedOldPath);
        var now = DateTime.UtcNow;

        foreach (var mark in marks)
        {
            mark.Path = normalizedNewPath;
            mark.UpdatedAt = now;

            // Reset to pending status so it will be re-synced with the new path
            if (mark.SyncStatus == PathMarkSyncStatus.Synced)
            {
                mark.SyncStatus = PathMarkSyncStatus.Pending;
            }

            await orm.Update(mark);

            // Trigger immediate sync if enabled
            await TriggerImmediateSyncIfEnabled(mark.Id);
        }
    }
}

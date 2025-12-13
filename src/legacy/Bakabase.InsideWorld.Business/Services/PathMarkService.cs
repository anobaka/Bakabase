using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.Modules.Property.Abstractions.Services;
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
    IHubContext<WebGuiHub, IWebGuiClient> uiHub
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

        foreach (var dbModel in dbModels)
        {
            dbModel.SyncStatus = PathMarkSyncStatus.Pending;
            dbModel.SyncError = null;
            dbModel.UpdatedAt = now;
        }

        await orm.UpdateRange(dbModels);
        await NotifyPathMarkStatusChangeBatch(dbModels);
    }

    public async Task<int> GetPendingCount()
    {
        var allMarks = await orm.GetAll(m =>
            m.SyncStatus == PathMarkSyncStatus.Pending ||
            m.SyncStatus == PathMarkSyncStatus.PendingDelete);
        return allMarks.Count;
    }

    public Task<List<PathMarkPreviewResult>> PreviewMatchedPaths(PathMarkPreviewRequest request)
    {
        var rootPath = request.Path.StandardizePath()!;
        var results = new List<PathMarkPreviewResult>();

        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(results);
        }

        // Step 1: Parse base match config (common to all mark types)
        PathMatchMode matchMode;
        int? layer = null;
        string? regex = null;

        // Step 2: Parse type-specific filter/value config
        PathFilterFsType? fsTypeFilter = null;
        List<string>? extensions = null;

        PropertyValueType? valueType = null;
        object? fixedValue = null;
        int? valueLayer = null;
        string? valueRegex = null;

        switch (request.Type)
        {
            case PathMarkType.Resource:
            {
                var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(request.ConfigJson);
                if (config == null) return Task.FromResult(results);

                matchMode = config.MatchMode;
                layer = config.Layer;
                regex = config.Regex;
                fsTypeFilter = config.FsTypeFilter;
                extensions = config.Extensions;
                // Note: ExtensionGroupIds would need to be resolved to actual extensions here if needed
                break;
            }
            case PathMarkType.Property:
            {
                var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(request.ConfigJson);
                if (config == null) return Task.FromResult(results);

                matchMode = config.MatchMode;
                layer = config.Layer;
                regex = config.Regex;
                valueType = config.ValueType;
                fixedValue = config.FixedValue;
                valueLayer = config.ValueLayer;
                valueRegex = config.ValueRegex;
                break;
            }
            case PathMarkType.MediaLibrary:
            {
                var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(request.ConfigJson);
                if (config == null) return Task.FromResult(results);

                matchMode = config.MatchMode;
                layer = config.Layer;
                regex = config.Regex;
                valueType = config.ValueType;
                fixedValue = config.MediaLibraryId; // For Fixed, the value is MediaLibraryId
                valueLayer = config.LayerToMediaLibrary;
                valueRegex = config.RegexToMediaLibrary;
                break;
            }
            default:
                return Task.FromResult(results);
        }

        // Step 3: Get matched paths using common logic
        var matchedPaths = GetMatchingPaths(rootPath, matchMode, layer, regex, fsTypeFilter, extensions);

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
            else if (request.Type == PathMarkType.Property || request.Type == PathMarkType.MediaLibrary)
            {
                // For Property and MediaLibrary: calculate value
                result.PropertyValue = CalculatePropertyValue(
                    rootPath, matchedPath, rootSegments, matchedSegments,
                    valueType, fixedValue, valueLayer, valueRegex);
            }

            results.Add(result);
        }

        return Task.FromResult(results);
    }

    /// <summary>
    /// Common path matching logic for all mark types
    /// </summary>
    private List<string> GetMatchingPaths(string rootPath, PathMatchMode matchMode, int? layer, string? regex,
        PathFilterFsType? fsTypeFilter = null, List<string>? extensions = null)
    {
        var matchedPaths = new List<string>();

        try
        {
            if (matchMode == PathMatchMode.Layer)
            {
                if (layer == null) return matchedPaths;

                if (layer == -1)
                {
                    // All layers - recursively get all directories/files
                    matchedPaths.AddRange(GetAllEntries(rootPath, fsTypeFilter, extensions));
                }
                else
                {
                    // Specific layer
                    matchedPaths.AddRange(GetEntriesAtLayer(rootPath, layer.Value, fsTypeFilter, extensions));
                }
            }
            else if (matchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(regex))
            {
                var regexObj = new Regex(regex, RegexOptions.IgnoreCase);
                var entries = GetAllEntries(rootPath, fsTypeFilter, extensions);

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
        catch (Exception)
        {
            // Ignore access errors
        }

        return matchedPaths;
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
    /// Calculate property value for Property and MediaLibrary mark types
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


    private List<string> GetAllEntries(string rootPath, PathFilterFsType? fsTypeFilter, List<string>? extensions)
    {
        var entries = new List<string>();

        try
        {
            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
            {
                entries.AddRange(Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories));
            }

            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
            {
                var files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
                if (extensions != null && extensions.Count > 0)
                {
                    files = files.Where(f => extensions.Any(ext =>
                        f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToArray();
                }
                entries.AddRange(files);
            }
        }
        catch (Exception)
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList();
    }

    private List<string> GetEntriesAtLayer(string rootPath, int layer, PathFilterFsType? fsTypeFilter, List<string>? extensions)
    {
        var entries = new List<string>();

        try
        {
            var currentPaths = new List<string> { rootPath };

            for (int i = 1; i <= layer; i++)
            {
                var nextPaths = new List<string>();
                foreach (var path in currentPaths)
                {
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
        catch (Exception)
        {
            // Ignore access errors
        }

        return entries.Select(x => x.StandardizePath()!).ToList();
    }


    public async Task MigratePath(string oldPath, string newPath)
    {
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

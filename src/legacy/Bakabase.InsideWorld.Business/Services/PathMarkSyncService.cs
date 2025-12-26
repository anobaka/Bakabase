using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

public class PathMarkSyncService : ScopedService, IPathMarkSyncService
{
    private readonly IPathMarkService _pathMarkService;
    private readonly IResourceService _resourceService;
    private readonly IMediaLibraryResourceMappingService _mappingService;
    private readonly IMediaLibraryV2Service _mediaLibraryV2Service;
    private readonly ICustomPropertyValueService _customPropertyValueService;
    private readonly IBOptions<ResourceOptions> _resourceOptions;
    private readonly BTaskManager _btm;
    private readonly IBakabaseLocalizer _localizer;
    private readonly IServiceProvider _serviceProvider;

    private const string SyncAllTaskId = "SyncPathMarks";
    private const string SyncImmediateTaskIdPrefix = "SyncPathMark_";

    public PathMarkSyncService(
        IServiceProvider serviceProvider,
        IPathMarkService pathMarkService,
        IResourceService resourceService,
        IMediaLibraryResourceMappingService mappingService,
        IMediaLibraryV2Service mediaLibraryV2Service,
        ICustomPropertyValueService customPropertyValueService,
        IBOptions<ResourceOptions> resourceOptions,
        BTaskManager btm,
        IBakabaseLocalizer localizer) : base(serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _pathMarkService = pathMarkService;
        _resourceService = resourceService;
        _mappingService = mappingService;
        _mediaLibraryV2Service = mediaLibraryV2Service;
        _customPropertyValueService = customPropertyValueService;
        _resourceOptions = resourceOptions;
        _btm = btm;
        _localizer = localizer;
    }

    public async Task StartSyncAll()
    {
        await _btm.Enqueue(new BTaskHandlerBuilder
        {
            Id = SyncAllTaskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => _localizer.BTask_Name("SyncPathMarks"),
            GetDescription = () => _localizer.BTask_Description("SyncPathMarks"),
            GetMessageOnInterruption = () => _localizer.BTask_MessageOnInterruption("SyncPathMarks"),
            ConflictKeys = [SyncAllTaskId],
            Level = BTaskLevel.Default,
            IsPersistent = false,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Reject,
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>();
                var result = await syncService.SyncMarks(
                    null,
                    async p => await args.UpdateTask(t => t.Percentage = p),
                    async p => await args.UpdateTask(t => t.Process = p),
                    args.PauseToken,
                    args.CancellationToken);
                await args.UpdateTask(t => t.Data = result);
            }
        });
    }

    public async Task StartSyncImmediate(int[] markIds)
    {
        var taskId = $"{SyncImmediateTaskIdPrefix}{string.Join("_", markIds)}";

        await _btm.Enqueue(new BTaskHandlerBuilder
        {
            Id = taskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => _localizer.BTask_Name("SyncPathMark_Single"),
            GetDescription = () => _localizer.BTask_Description("SyncPathMark_Single"),
            GetMessageOnInterruption = () => _localizer.BTask_MessageOnInterruption("SyncPathMark_Single"),
            ConflictKeys = [SyncAllTaskId],  // All sync operations share the same conflict key to prevent concurrent sync
            Level = BTaskLevel.Default,
            IsPersistent = false,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Replace,
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>();
                var result = await syncService.SyncMarks(
                    markIds,
                    async p => await args.UpdateTask(t => t.Percentage = p),
                    async p => await args.UpdateTask(t => t.Process = p),
                    args.PauseToken,
                    args.CancellationToken);
                await args.UpdateTask(t => t.Data = result);
            }
        });
    }

    public async Task<PathMarkSyncResult> SyncMarks(
        int[]? markIds,
        Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct)
    {
        var result = new PathMarkSyncResult();
        var ctx = new SyncContext();

        try
        {
            // Phase 1: Collect marks and preload data (0-5%)
            await ReportProgress(onProgressChange, onProcessChange, 0, _localizer.SyncPathMark_Collecting());

            List<PathMark> marks;
            if (markIds != null && markIds.Length > 0)
            {
                var allMarks = await _pathMarkService.GetAll();
                marks = allMarks.Where(m => markIds.Contains(m.Id)).ToList();
            }
            else
            {
                marks = await _pathMarkService.GetPendingMarks();
            }

            if (marks.Count == 0)
            {
                await ReportProgress(onProgressChange, onProcessChange, 100, _localizer.SyncPathMark_Complete());
                return result;
            }

            // Preload all resources once - this is the key optimization
            ctx.AllResources = await _resourceService.GetAll();
            ctx.BuildIndexes();

            // Separate marks by type
            var resourceMarks = marks.Where(m => m.Type == PathMarkType.Resource).OrderByDescending(m => m.Priority).ToList();
            var propertyMarks = marks.Where(m => m.Type == PathMarkType.Property).OrderByDescending(m => m.Priority).ToList();
            var mediaLibraryMarks = marks.Where(m => m.Type == PathMarkType.MediaLibrary).OrderByDescending(m => m.Priority).ToList();

            await ReportProgress(onProgressChange, onProcessChange, 5, _localizer.SyncPathMark_Collected(marks.Count));

            // Track created resources for property mark processing
            var createdResourcePaths = new List<string>();
            var allResourcePaths = new List<string>();
            var marksToDelete = new List<int>();

            // Phase 2: Process Resource Marks (5-40%)
            var resourceMarkCount = resourceMarks.Count;
            for (var i = 0; i < resourceMarkCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = resourceMarks[i];
                var progress = 5 + (int)(35.0 * (i + 1) / resourceMarkCount);

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    await ReportProgress(onProgressChange, onProcessChange, progress,
                        _localizer.SyncPathMark_ProcessingResource(mark.Path));

                    if (mark.SyncStatus == PathMarkSyncStatus.PendingDelete)
                    {
                        var deletedCount = await ProcessResourceMarkDelete(mark, ctx, ct);
                        result.ResourcesDeleted += deletedCount;
                        marksToDelete.Add(mark.Id);
                    }
                    else
                    {
                        var (created, paths) = await ProcessResourceMark(mark, ctx, ct);
                        result.ResourcesCreated += created;
                        createdResourcePaths.AddRange(paths);
                        allResourcePaths.AddRange(paths);
                        ctx.SuccessfulMarkIds.Add(mark.Id);
                    }
                }
                catch (Exception ex)
                {
                    ctx.FailedMarks.Add((mark.Id, ex.Message));
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError
                    {
                        MarkId = mark.Id,
                        Path = mark.Path,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Refresh context after resource creation
            if (createdResourcePaths.Count > 0)
            {
                ctx.AllResources = await _resourceService.GetAll();
                ctx.BuildIndexes();
            }

            // Phase 3: Find related Property/MediaLibrary Marks (40-45%)
            await ReportProgress(onProgressChange, onProcessChange, 40, _localizer.SyncPathMark_FindingRelated());

            var additionalPropertyMarks = await FindRelatedMarksAsync(allResourcePaths, PathMarkType.Property, propertyMarks, ctx);
            var allPropertyMarks = propertyMarks.Concat(additionalPropertyMarks).DistinctBy(m => m.Id).OrderByDescending(m => m.Priority).ToList();

            var additionalMediaLibraryMarks = await FindRelatedMarksAsync(allResourcePaths, PathMarkType.MediaLibrary, mediaLibraryMarks, ctx);
            var allMediaLibraryMarks = mediaLibraryMarks.Concat(additionalMediaLibraryMarks).DistinctBy(m => m.Id).OrderByDescending(m => m.Priority).ToList();

            await ReportProgress(onProgressChange, onProcessChange, 45, _localizer.SyncPathMark_FoundRelated(additionalPropertyMarks.Count + additionalMediaLibraryMarks.Count));

            // Phase 4 & 5: Process Property Marks (45-80%)
            var propertyMarkCount = allPropertyMarks.Count;
            for (var i = 0; i < propertyMarkCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = allPropertyMarks[i];
                var progress = 45 + (int)(35.0 * (i + 1) / propertyMarkCount);

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    await ReportProgress(onProgressChange, onProcessChange, progress,
                        _localizer.SyncPathMark_ProcessingProperty(mark.Path));

                    if (mark.SyncStatus == PathMarkSyncStatus.PendingDelete)
                    {
                        var deletedCount = await ProcessPropertyMarkDelete(mark, ctx, ct);
                        result.PropertiesDeleted += deletedCount;
                        marksToDelete.Add(mark.Id);
                    }
                    else
                    {
                        var appliedCount = await ProcessPropertyMark(mark, ctx, ct);
                        result.PropertiesApplied += appliedCount;
                        ctx.SuccessfulMarkIds.Add(mark.Id);
                    }
                }
                catch (Exception ex)
                {
                    ctx.FailedMarks.Add((mark.Id, ex.Message));
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError
                    {
                        MarkId = mark.Id,
                        Path = mark.Path,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Batch flush property values
            await FlushPropertyValues(ctx);

            // Phase 6: Process MediaLibrary Marks (80-90%)
            var mediaLibraryMarkCount = allMediaLibraryMarks.Count;
            for (var i = 0; i < mediaLibraryMarkCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = allMediaLibraryMarks[i];
                var progress = 80 + (int)(10.0 * (i + 1) / mediaLibraryMarkCount);

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    await ReportProgress(onProgressChange, onProcessChange, progress,
                        _localizer.SyncPathMark_ProcessingMediaLibrary(mark.Path));

                    if (mark.SyncStatus == PathMarkSyncStatus.PendingDelete)
                    {
                        var deletedCount = await ProcessMediaLibraryMarkDelete(mark, ctx, ct);
                        result.MediaLibraryMappingsDeleted += deletedCount;
                        marksToDelete.Add(mark.Id);
                    }
                    else
                    {
                        var createdCount = await ProcessMediaLibraryMark(mark, ctx, ct);
                        result.MediaLibraryMappingsCreated += createdCount;
                        ctx.SuccessfulMarkIds.Add(mark.Id);
                    }
                }
                catch (Exception ex)
                {
                    ctx.FailedMarks.Add((mark.Id, ex.Message));
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError
                    {
                        MarkId = mark.Id,
                        Path = mark.Path,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Batch update mark statuses
            await BatchUpdateMarkStatuses(ctx, marksToDelete);

            // Phase 7: Establish parent-child relationships (90-100%)
            await ReportProgress(onProgressChange, onProcessChange, 90, _localizer.SyncPathMark_EstablishingRelationships());

            if (createdResourcePaths.Count > 0)
            {
                await EstablishParentChildRelationships(createdResourcePaths, ctx, ct);
            }

            await _resourceService.RefreshParentTag();

            await ReportProgress(onProgressChange, onProcessChange, 100, _localizer.SyncPathMark_Complete());
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected, don't treat as error
            throw;
        }

        return result;
    }

    private async Task FlushPropertyValues(SyncContext ctx)
    {
        if (ctx.PropertyValuesToAdd.Count > 0)
        {
            await _customPropertyValueService.AddDbModelRange(ctx.PropertyValuesToAdd);
            ctx.PropertyValuesToAdd.Clear();
        }

        if (ctx.PropertyValuesToUpdate.Count > 0)
        {
            await _customPropertyValueService.UpdateDbModelRange(ctx.PropertyValuesToUpdate);
            ctx.PropertyValuesToUpdate.Clear();
        }
    }

    private async Task BatchUpdateMarkStatuses(SyncContext ctx, List<int> marksToDelete)
    {
        // Batch mark as synced
        if (ctx.SuccessfulMarkIds.Count > 0)
        {
            await _pathMarkService.MarkAsSyncedBatch(ctx.SuccessfulMarkIds);
        }

        // Batch mark as failed
        foreach (var (markId, error) in ctx.FailedMarks)
        {
            await _pathMarkService.MarkAsFailed(markId, error);
        }

        // Batch delete
        if (marksToDelete.Count > 0)
        {
            await _pathMarkService.HardDeleteBatch(marksToDelete);
        }
    }

    public async Task StopSync()
    {
        await _btm.Stop(SyncAllTaskId);
    }

    #region Private Methods

    private async Task ReportProgress(Func<int, Task>? onProgressChange, Func<string?, Task>? onProcessChange, int progress, string? process)
    {
        if (onProgressChange != null) await onProgressChange(progress);
        if (onProcessChange != null) await onProcessChange(process);
    }

    private async Task<(int Created, List<string> Paths)> ProcessResourceMark(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
        if (config == null) return (0, new List<string>());

        var matchedPaths = GetMatchingPathsForResourceMark(mark.Path, config, ctx);
        var createdPaths = new List<string>();
        var createdCount = 0;
        var resourcesToCreate = new List<Resource>();

        foreach (var path in matchedPaths)
        {
            ct.ThrowIfCancellationRequested();

            var standardizedPath = ctx.GetStandardizedPath(path);

            // Check if resource already exists using cached data
            if (ctx.ExistingPathSet.Contains(standardizedPath))
            {
                createdPaths.Add(path);
                continue;
            }

            // Check for bakabase.json marker
            var existingResourceId = await CheckBakabaseJsonMarker(path);
            if (existingResourceId.HasValue && ctx.IdToResource.TryGetValue(existingResourceId.Value, out var existingResource))
            {
                // Resource exists but path changed, update it
                existingResource.Path = path;
                existingResource.UpdatedAt = DateTime.Now;
                resourcesToCreate.Add(existingResource);
                createdPaths.Add(path);
                continue;
            }

            // Create new resource
            var isFile = File.Exists(path);
            var isDirectory = Directory.Exists(path);

            if (!isFile && !isDirectory) continue;

            DateTime fileCreatedAt, fileModifiedAt;
            if (isFile)
            {
                var fileInfo = new FileInfo(path);
                fileCreatedAt = fileInfo.CreationTime;
                fileModifiedAt = fileInfo.LastWriteTime;
            }
            else
            {
                var dirInfo = new DirectoryInfo(path);
                fileCreatedAt = dirInfo.CreationTime;
                fileModifiedAt = dirInfo.LastWriteTime;
            }

            var resource = new Resource
            {
                Path = path,
                IsFile = isFile,
                CategoryId = 0,
                MediaLibraryId = 0,
                FileCreatedAt = fileCreatedAt,
                FileModifiedAt = fileModifiedAt,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            resourcesToCreate.Add(resource);
            createdPaths.Add(path);
            createdCount++;
        }

        if (resourcesToCreate.Count > 0)
        {
            await _resourceService.AddOrPutRange(resourcesToCreate);

            // Create bakabase.json markers for new folder resources
            if (_resourceOptions.Value.KeepResourcesOnPathChange)
            {
                foreach (var resource in resourcesToCreate.Where(r => !r.IsFile && r.Id > 0))
                {
                    await CreateBakabaseJsonMarker(resource.Path, resource.Id);
                }
            }
        }

        return (createdCount, createdPaths);
    }

    private async Task<int> ProcessResourceMarkDelete(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        var matchedPaths = GetMatchingPathsForResourceMark(mark.Path, config, ctx);
        var resourcesToDelete = new List<int>();

        foreach (var path in matchedPaths)
        {
            ct.ThrowIfCancellationRequested();

            var standardizedPath = ctx.GetStandardizedPath(path);
            if (!ctx.PathToResource.TryGetValue(standardizedPath, out var resource)) continue;

            // Check bakabase.json preservation
            if (_resourceOptions.Value.KeepResourcesOnPathChange && !resource.IsFile)
            {
                var markerPath = Path.Combine(path, InternalOptions.ResourceMarkerFileName);
                if (File.Exists(markerPath))
                {
                    continue;
                }
            }

            resourcesToDelete.Add(resource.Id);
        }

        if (resourcesToDelete.Count > 0)
        {
            await _resourceService.DeleteByKeys(resourcesToDelete.ToArray());
        }

        return resourcesToDelete.Count;
    }

    private async Task<int> ProcessPropertyMark(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        // Use cached resources
        var matchedResources = ctx.AllResources
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        if (matchedResources.Count == 0) return 0;

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);
        var appliedCount = 0;

        // Pre-load existing property values for batch processing
        if (config.Pool == PropertyPool.Custom && filteredResources.Count > 0)
        {
            var resourceIds = filteredResources.Select(r => r.Id).ToList();
            var existingValues = await _customPropertyValueService.GetAllDbModels(x =>
                resourceIds.Contains(x.ResourceId) &&
                x.PropertyId == config.PropertyId &&
                x.Scope == (int)PropertyValueScope.Synchronization);

            foreach (var v in existingValues)
            {
                ctx.ExistingPropertyValues[(v.ResourceId, v.PropertyId)] = v;
            }
        }

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var propertyValue = ExtractPropertyValue(resource.Path, mark.Path, config, ctx);
                if (propertyValue == null) continue;

                CollectPropertyValue(resource, config, propertyValue, ctx);
                appliedCount++;
            }
            catch
            {
                // Skip individual property errors
            }
        }

        return appliedCount;
    }

    private async Task<int> ProcessPropertyMarkDelete(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        var matchedResources = ctx.AllResources
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);
        var deletedCount = 0;

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await RemovePropertyValue(resource, config);
                deletedCount++;
            }
            catch
            {
                // Skip individual property errors
            }
        }

        return deletedCount;
    }

    private async Task<int> ProcessMediaLibraryMark(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        var matchedResources = ctx.AllResources
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);
        var createdCount = 0;

        // Cache for dynamic mode: media library name -> id
        var mediaLibraryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pre-load existing media libraries for dynamic mode
        if (config.ValueType == PropertyValueType.Dynamic)
        {
            var existingLibraries = await _mediaLibraryV2Service.GetAll();
            foreach (var lib in existingLibraries)
            {
                if (!string.IsNullOrEmpty(lib.Name) && !mediaLibraryCache.ContainsKey(lib.Name))
                {
                    mediaLibraryCache[lib.Name] = lib.Id;
                }
            }
        }

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                int mediaLibraryId;

                if (config.ValueType == PropertyValueType.Fixed)
                {
                    if (!config.MediaLibraryId.HasValue) continue;
                    mediaLibraryId = config.MediaLibraryId.Value;
                }
                else
                {
                    var mediaLibraryName = ExtractMediaLibraryName(resource.Path, mark.Path, config, ctx);
                    if (string.IsNullOrEmpty(mediaLibraryName)) continue;

                    if (!mediaLibraryCache.TryGetValue(mediaLibraryName, out mediaLibraryId))
                    {
                        var newLibrary = await _mediaLibraryV2Service.Add(new MediaLibraryV2AddOrPutInputModel(
                            Name: mediaLibraryName,
                            Paths: new List<string>()
                        ));
                        mediaLibraryId = newLibrary.Id;
                        mediaLibraryCache[mediaLibraryName] = mediaLibraryId;
                    }
                }

                await _mappingService.EnsureMappings(resource.Id, new[] { mediaLibraryId });
                createdCount++;
            }
            catch
            {
                // Skip individual mapping errors
            }
        }

        return createdCount;
    }

    private string? ExtractMediaLibraryName(string resourcePath, string markPath, MediaLibraryMarkConfig config, SyncContext ctx)
    {
        var resourceSegments = ctx.GetPathSegments(resourcePath);

        // Calculate the target index based on LayerToMediaLibrary
        var layer = config.LayerToMediaLibrary ?? 0;
        int targetIndex;

        if (layer == 0)
        {
            targetIndex = resourceSegments.Length - 1;
        }
        else
        {
            targetIndex = resourceSegments.Length - 1 - layer;
        }

        if (targetIndex < 0 || targetIndex >= resourceSegments.Length)
        {
            return null;
        }

        var directoryName = resourceSegments[targetIndex];

        // Apply regex extraction if configured
        if (!string.IsNullOrEmpty(config.RegexToMediaLibrary))
        {
            try
            {
                var regex = ctx.GetOrCreateRegex(config.RegexToMediaLibrary);
                var match = regex.Match(directoryName);
                if (match.Success)
                {
                    return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                }
                return null;
            }
            catch
            {
                return directoryName;
            }
        }

        return directoryName;
    }

    private async Task<int> ProcessMediaLibraryMarkDelete(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
        if (config == null) return 0;

        var matchedResources = ctx.AllResources
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);
        var deletedCount = 0;

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await _mappingService.DeleteByResourceId(resource.Id);
                deletedCount++;
            }
            catch
            {
                // Skip individual deletion errors
            }
        }

        return deletedCount;
    }

    private async Task<List<PathMark>> FindRelatedMarksAsync(List<string> resourcePaths, PathMarkType type, List<PathMark> excludeMarks, SyncContext ctx)
    {
        var excludeIds = excludeMarks.Select(m => m.Id).ToHashSet();

        // Precompute standardized resource paths for efficient lookup
        var standardizedResourcePaths = resourcePaths
            .Select(p => ctx.GetStandardizedPath(p))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        // Sort paths to enable prefix-based optimization
        standardizedResourcePaths.Sort(StringComparer.OrdinalIgnoreCase);

        var allMarks = await _pathMarkService.GetAll(m => m.Type == type && !excludeIds.Contains(m.Id));

        var relatedMarks = new List<PathMark>();

        foreach (var mark in allMarks)
        {
            if (mark.SyncStatus is not (PathMarkSyncStatus.Pending or PathMarkSyncStatus.PendingDelete or PathMarkSyncStatus.Synced))
                continue;

            var markPath = ctx.GetStandardizedPath(mark.Path);
            if (string.IsNullOrEmpty(markPath)) continue;

            // Check if any resource path starts with this mark's path
            var markPathWithSeparator = markPath + Path.DirectorySeparatorChar;

            foreach (var resourcePath in standardizedResourcePaths)
            {
                if (resourcePath.StartsWith(markPathWithSeparator, StringComparison.OrdinalIgnoreCase) ||
                    resourcePath.Equals(markPath, StringComparison.OrdinalIgnoreCase))
                {
                    relatedMarks.Add(mark);
                    break;
                }
            }
        }

        return relatedMarks;
    }

    private async Task EstablishParentChildRelationships(List<string> resourcePaths, SyncContext ctx, CancellationToken ct)
    {
        // Use cached path-to-resource map
        var sortedPaths = resourcePaths
            .Select(p => ctx.GetStandardizedPath(p))
            .Where(p => !string.IsNullOrEmpty(p) && ctx.PathToResource.ContainsKey(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changedResources = new List<Resource>();
        var parentStack = new Stack<(string Path, Resource Resource)>();

        foreach (var path in sortedPaths)
        {
            ct.ThrowIfCancellationRequested();

            var resource = ctx.PathToResource[path];

            while (parentStack.Count > 0)
            {
                var top = parentStack.Peek();
                var parentPathWithSeparator = top.Path + Path.DirectorySeparatorChar;

                if (path.StartsWith(parentPathWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                parentStack.Pop();
            }

            if (parentStack.Count > 0)
            {
                var parent = parentStack.Peek();
                if (resource.ParentId != parent.Resource.Id)
                {
                    resource.ParentId = parent.Resource.Id;
                    changedResources.Add(resource);
                }
            }
            else if (resource.ParentId != null)
            {
                resource.ParentId = null;
                changedResources.Add(resource);
            }

            parentStack.Push((path, resource));
        }

        if (changedResources.Count > 0)
        {
            await _resourceService.AddOrPutRange(changedResources);
        }
    }

    private List<string> GetMatchingPathsForResourceMark(string rootPath, ResourceMarkConfig config, SyncContext ctx)
    {
        var matchedPaths = new List<string>();
        var normalizedRoot = ctx.GetStandardizedPath(rootPath);

        if (!Directory.Exists(normalizedRoot)) return matchedPaths;

        try
        {
            List<string> initialMatches;

            if (config.MatchMode == PathMatchMode.Layer)
            {
                if (config.Layer == null) return matchedPaths;

                var layer = config.Layer.Value;
                if (layer < 0)
                {
                    initialMatches = GetParentAtLayer(normalizedRoot, Math.Abs(layer), config.FsTypeFilter, ctx);
                }
                else
                {
                    initialMatches = GetEntriesAtLayer(normalizedRoot, layer, config.FsTypeFilter, config.Extensions, ctx);
                }
            }
            else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
            {
                var regex = ctx.GetOrCreateRegex(config.Regex);
                var entries = GetAllEntries(normalizedRoot, config.FsTypeFilter, config.Extensions, ctx);
                initialMatches = new List<string>();

                foreach (var entry in entries)
                {
                    var relativePath = entry.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (regex.IsMatch(relativePath))
                    {
                        initialMatches.Add(entry);
                    }
                }
            }
            else
            {
                return matchedPaths;
            }

            if (config.ApplyScope == PathMarkApplyScope.MatchedOnly)
            {
                matchedPaths.AddRange(initialMatches);
            }
            else if (config.ApplyScope == PathMarkApplyScope.MatchedAndSubdirectories)
            {
                matchedPaths.AddRange(initialMatches);
                foreach (var match in initialMatches)
                {
                    if (Directory.Exists(match))
                    {
                        var subdirEntries = GetAllEntries(match, config.FsTypeFilter, config.Extensions, ctx);
                        matchedPaths.AddRange(subdirEntries);
                    }
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return matchedPaths;
    }

    private List<string> GetAllEntries(string rootPath, PathFilterFsType? fsTypeFilter, List<string>? extensions, SyncContext ctx)
    {
        var entries = new List<string>();

        try
        {
            // Use EnumerateDirectories/EnumerateFiles for better memory efficiency
            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
            {
                foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
                {
                    entries.Add(ctx.GetStandardizedPath(dir));
                }
            }

            if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
            {
                var extensionSet = extensions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
                {
                    if (extensionSet == null || extensionSet.Count == 0 ||
                        extensionSet.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        entries.Add(ctx.GetStandardizedPath(file));
                    }
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return entries;
    }

    private List<string> GetEntriesAtLayer(string rootPath, int layer, PathFilterFsType? fsTypeFilter, List<string>? extensions, SyncContext ctx)
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
                        nextPaths.AddRange(Directory.EnumerateDirectories(path));
                        if (i == layer && (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File))
                        {
                            var extensionSet = extensions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
                            foreach (var file in Directory.EnumerateFiles(path))
                            {
                                if (extensionSet == null || extensionSet.Count == 0 ||
                                    extensionSet.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                                {
                                    nextPaths.Add(file);
                                }
                            }
                        }
                    }
                    catch
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
        catch
        {
            // Ignore access errors
        }

        return entries.Select(x => ctx.GetStandardizedPath(x)).ToList();
    }

    private List<string> GetParentAtLayer(string rootPath, int absLayer, PathFilterFsType? fsTypeFilter, SyncContext ctx)
    {
        var entries = new List<string>();

        try
        {
            var currentPath = rootPath;
            for (int i = 0; i < absLayer; i++)
            {
                var parentPath = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(parentPath))
                {
                    return entries;
                }
                currentPath = parentPath;
            }

            if (Directory.Exists(currentPath))
            {
                if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.Directory)
                {
                    entries.Add(ctx.GetStandardizedPath(currentPath));
                }
            }
            else if (File.Exists(currentPath))
            {
                if (fsTypeFilter == null || fsTypeFilter == PathFilterFsType.File)
                {
                    entries.Add(ctx.GetStandardizedPath(currentPath));
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        return entries;
    }

    // Unified filter method for both PropertyMarkConfig and MediaLibraryMarkConfig
    private List<Resource> FilterResourcesByMarkConfig<TConfig>(List<Resource> resources, string markPath, TConfig config, SyncContext ctx)
        where TConfig : class
    {
        var normalizedMarkPath = ctx.GetStandardizedPath(markPath);

        PathMatchMode? matchMode = null;
        int? layer = null;
        string? regex = null;
        PathMarkApplyScope? applyScope = null;

        if (config is PropertyMarkConfig pmc)
        {
            matchMode = pmc.MatchMode;
            layer = pmc.Layer;
            regex = pmc.Regex;
            applyScope = pmc.ApplyScope;
        }
        else if (config is MediaLibraryMarkConfig mlmc)
        {
            matchMode = mlmc.MatchMode;
            layer = mlmc.Layer;
            regex = mlmc.Regex;
            applyScope = mlmc.ApplyScope;
        }

        // First filter by MatchMode
        var filteredResources = matchMode switch
        {
            PathMatchMode.Layer when layer.HasValue => FilterByLayer(resources, normalizedMarkPath, layer.Value, ctx),
            PathMatchMode.Regex when !string.IsNullOrEmpty(regex) => FilterByRegex(resources, normalizedMarkPath, regex, ctx),
            _ => resources
        };

        if (applyScope == PathMarkApplyScope.MatchedOnly)
        {
            return filteredResources;
        }
        else if (applyScope == PathMarkApplyScope.MatchedAndSubdirectories)
        {
            var result = new List<Resource>(filteredResources);
            var matchedPaths = filteredResources.Select(r => ctx.GetStandardizedPath(r.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var resource in resources)
            {
                var resourcePath = ctx.GetStandardizedPath(resource.Path);
                if (matchedPaths.Contains(resourcePath)) continue;

                foreach (var matchedPath in matchedPaths)
                {
                    if (ctx.IsPathUnderParent(resourcePath, matchedPath) &&
                        !resourcePath.Equals(matchedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(resource);
                        break;
                    }
                }
            }

            return result;
        }

        return filteredResources;
    }

    private List<Resource> FilterByLayer(List<Resource> resources, string rootPath, int layer, SyncContext ctx)
    {
        var rootSegments = ctx.GetPathSegments(rootPath);

        if (layer < 0)
        {
            var absLayer = Math.Abs(layer);
            var targetSegmentCount = rootSegments.Length - absLayer;
            if (targetSegmentCount <= 0) return new List<Resource>();

            return resources.Where(r =>
            {
                var resourceSegments = ctx.GetPathSegments(r.Path);
                return resourceSegments.Length == targetSegmentCount;
            }).ToList();
        }

        return resources.Where(r =>
        {
            var resourceSegments = ctx.GetPathSegments(r.Path);
            var relativeDepth = resourceSegments.Length - rootSegments.Length;
            return relativeDepth == layer;
        }).ToList();
    }

    private List<Resource> FilterByRegex(List<Resource> resources, string rootPath, string pattern, SyncContext ctx)
    {
        var regex = ctx.GetOrCreateRegex(pattern);

        return resources.Where(r =>
        {
            var standardizedPath = ctx.GetStandardizedPath(r.Path);
            var relativePath = standardizedPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return regex.IsMatch(relativePath);
        }).ToList();
    }

    private object? ExtractPropertyValue(string resourcePath, string markPath, PropertyMarkConfig config, SyncContext ctx)
    {
        if (config.ValueType == PropertyValueType.Fixed)
        {
            return config.FixedValue;
        }

        var resourceSegments = ctx.GetPathSegments(resourcePath);
        var markSegments = ctx.GetPathSegments(markPath);

        if (config.MatchMode == PathMatchMode.Layer && config.ValueLayer.HasValue)
        {
            var valueLayer = config.ValueLayer.Value;
            if (valueLayer == 0)
            {
                return Path.GetFileName(ctx.GetStandardizedPath(resourcePath));
            }

            var targetIndex = markSegments.Length + valueLayer - 1;
            if (targetIndex >= 0 && targetIndex < resourceSegments.Length)
            {
                return resourceSegments[targetIndex];
            }
        }
        else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.ValueRegex))
        {
            var normalizedResourcePath = ctx.GetStandardizedPath(resourcePath);
            var normalizedMarkPath = ctx.GetStandardizedPath(markPath);
            var relativePath = normalizedResourcePath.Substring(normalizedMarkPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var regex = ctx.GetOrCreateRegex(config.ValueRegex);
            var match = regex.Match(relativePath);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
            else if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }

    // Collect property values for batch processing instead of immediate DB operations
    private void CollectPropertyValue(Resource resource, PropertyMarkConfig config, object? value, SyncContext ctx)
    {
        if (value == null) return;
        if (config.Pool != PropertyPool.Custom) return;

        var valueString = value is string s ? s : System.Text.Json.JsonSerializer.Serialize(value);

        if (ctx.ExistingPropertyValues.TryGetValue((resource.Id, config.PropertyId), out var existingValue))
        {
            existingValue.Value = valueString;
            ctx.PropertyValuesToUpdate.Add(existingValue);
        }
        else
        {
            var newValue = new CustomPropertyValueDbModel
            {
                ResourceId = resource.Id,
                PropertyId = config.PropertyId,
                Value = valueString,
                Scope = (int)PropertyValueScope.Synchronization
            };
            ctx.PropertyValuesToAdd.Add(newValue);
        }
    }

    private async Task RemovePropertyValue(Resource resource, PropertyMarkConfig config)
    {
        // Only support Custom properties for now (Pool.Custom)
        if (config.Pool != PropertyPool.Custom) return;

        // Remove Synchronization scope values
        await _customPropertyValueService.RemoveAll(x =>
            x.ResourceId == resource.Id &&
            x.PropertyId == config.PropertyId &&
            x.Scope == (int)PropertyValueScope.Synchronization);
    }

    private async Task<int?> CheckBakabaseJsonMarker(string path)
    {
        if (!_resourceOptions.Value.KeepResourcesOnPathChange) return null;
        if (File.Exists(path)) return null; // Only for directories

        var markerPath = Path.Combine(path, InternalOptions.ResourceMarkerFileName);
        if (!File.Exists(markerPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(markerPath);
            var markerData = System.Text.Json.JsonDocument.Parse(json);
            if (markerData.RootElement.TryGetProperty("ids", out var idsElement))
            {
                var ids = idsElement.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                return ids.FirstOrDefault();
            }
        }
        catch
        {
            // Ignore marker read errors
        }

        return null;
    }

    private async Task CreateBakabaseJsonMarker(string path, int resourceId)
    {
        try
        {
            var markerPath = Path.Combine(path, InternalOptions.ResourceMarkerFileName);
            var content = System.Text.Json.JsonSerializer.Serialize(new { ids = new[] { resourceId } });
            await File.WriteAllTextAsync(markerPath, content);
            File.SetAttributes(markerPath, File.GetAttributes(markerPath) | FileAttributes.Hidden);
        }
        catch
        {
            // Ignore marker creation errors
        }
    }

    #endregion
}

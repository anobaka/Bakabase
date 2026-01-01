using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using CustomProperty = Bakabase.Abstractions.Models.Domain.CustomProperty;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Service that handles the actual synchronization logic for path marks.
/// This service is NOT thread-safe. Callers must ensure that only one synchronization
/// operation is running at a time. Use <see cref="Components.PathSyncManager"/> to
/// coordinate synchronization requests.
/// </summary>
public class PathMarkSyncService : ScopedService
{
    private readonly IPathMarkService _pathMarkService;
    private readonly IResourceService _resourceService;
    private readonly IMediaLibraryResourceMappingService _mappingService;
    private readonly IMediaLibraryV2Service _mediaLibraryV2Service;
    private readonly ICustomPropertyValueService _customPropertyValueService;
    private readonly ICustomPropertyService _customPropertyService;
    private readonly IPathMarkEffectService _effectService;
    private readonly IBOptions<ResourceOptions> _resourceOptions;
    private readonly IBakabaseLocalizer _localizer;
    private readonly ILogger<PathMarkSyncService> _logger;

    public PathMarkSyncService(
        IServiceProvider serviceProvider,
        IPathMarkService pathMarkService,
        IResourceService resourceService,
        IMediaLibraryResourceMappingService mappingService,
        IMediaLibraryV2Service mediaLibraryV2Service,
        ICustomPropertyValueService customPropertyValueService,
        ICustomPropertyService customPropertyService,
        IPathMarkEffectService effectService,
        IBOptions<ResourceOptions> resourceOptions,
        IBakabaseLocalizer localizer,
        ILogger<PathMarkSyncService> logger) : base(serviceProvider)
    {
        _pathMarkService = pathMarkService;
        _resourceService = resourceService;
        _mappingService = mappingService;
        _mediaLibraryV2Service = mediaLibraryV2Service;
        _customPropertyValueService = customPropertyValueService;
        _customPropertyService = customPropertyService;
        _effectService = effectService;
        _resourceOptions = resourceOptions;
        _localizer = localizer;
        _logger = logger;
    }

    /// <summary>
    /// Synchronize the specified path marks using Effect-Centric flow.
    ///
    /// Flow:
    /// 1. Phase 1: Collect Effects - Process marks and collect what they WANT to do
    /// 2. Phase 2: Compute Final State - Use Combine to merge effects into final values
    /// 3. Phase 3: Apply Changes - Create/update/delete based on final state
    /// 4. Phase 4: Persist Effects - Save effects to DB
    ///
    /// WARNING: This method is NOT thread-safe. Use <see cref="Components.PathSyncManager"/> to coordinate.
    /// </summary>
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
            // ===== Initialization (0-5%) =====
            await ReportProgress(onProgressChange, onProcessChange, 0, _localizer.SyncPathMark_Collecting());

            List<PathMark> marks;
            if (markIds != null && markIds.Length > 0)
            {
                markIds = markIds.Distinct().ToArray();
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

            // Preload all resources
            ctx.AllResources = await _resourceService.GetAll();
            ctx.BuildIndexes();

            // Load old effects for diff calculation
            var allMarkIds = marks.Select(m => m.Id).ToList();
            await LoadOldEffects(allMarkIds, ctx);

            // Separate marks by type and status
            var resourceMarks = marks.Where(m => m.Type == PathMarkType.Resource).OrderByDescending(m => m.Priority).ToList();
            var propertyMarks = marks.Where(m => m.Type == PathMarkType.Property).OrderByDescending(m => m.Priority).ToList();
            var mediaLibraryMarks = marks.Where(m => m.Type == PathMarkType.MediaLibrary).OrderByDescending(m => m.Priority).ToList();

            var marksToDelete = marks.Where(m => m.SyncStatus == PathMarkSyncStatus.PendingDelete).Select(m => m.Id).ToList();
            var activeResourceMarks = resourceMarks.Where(m => m.SyncStatus != PathMarkSyncStatus.PendingDelete).ToList();
            var activePropertyMarks = propertyMarks.Where(m => m.SyncStatus != PathMarkSyncStatus.PendingDelete).ToList();
            var activeMediaLibraryMarks = mediaLibraryMarks.Where(m => m.SyncStatus != PathMarkSyncStatus.PendingDelete).ToList();

            // Pre-collect resource boundary paths
            foreach (var mark in activeResourceMarks)
            {
                var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
                if (config is { IsResourceBoundary: true })
                {
                    var normalizedPath = ctx.GetStandardizedPath(mark.Path);
                    if (!string.IsNullOrEmpty(normalizedPath))
                    {
                        ctx.ResourceBoundaryPaths[normalizedPath] = mark.Id;
                    }
                }
            }

            await ReportProgress(onProgressChange, onProcessChange, 5, _localizer.SyncPathMark_Collected(marks.Count));

            // ===== Phase 1: Collect Effects (5-30%) =====
            await ReportProgress(onProgressChange, onProcessChange, 5, "Collecting resource effects...");

            // 1a. Collect resource effects
            var collectedResourcePaths = new List<string>();
            for (var i = 0; i < activeResourceMarks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = activeResourceMarks[i];
                var progress = 5 + (int)(10.0 * (i + 1) / Math.Max(1, activeResourceMarks.Count));
                await ReportProgress(onProgressChange, onProcessChange, progress,
                    _localizer.SyncPathMark_ProcessingResource(mark.Path));

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    var paths = await CollectResourceEffects(mark, ctx, ct);
                    collectedResourcePaths.AddRange(paths);
                    ctx.SuccessfulMarkIds.Add(mark.Id);
                }
                catch (Exception ex)
                {
                    ctx.FailedMarks.Add((mark.Id, ex.Message));
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError { MarkId = mark.Id, Path = mark.Path, ErrorMessage = ex.Message });
                }
            }

            // Find related Property/MediaLibrary marks
            var additionalPropertyMarks = await FindRelatedMarksAsync(collectedResourcePaths, PathMarkType.Property, propertyMarks, ctx);
            var allActivePropertyMarks = activePropertyMarks.Concat(additionalPropertyMarks).DistinctBy(m => m.Id).OrderByDescending(m => m.Priority).ToList();

            var additionalMediaLibraryMarks = await FindRelatedMarksAsync(collectedResourcePaths, PathMarkType.MediaLibrary, mediaLibraryMarks, ctx);
            var allActiveMediaLibraryMarks = activeMediaLibraryMarks.Concat(additionalMediaLibraryMarks).DistinctBy(m => m.Id).OrderByDescending(m => m.Priority).ToList();

            // Load old effects for additional marks (they were not loaded initially because they are already synced)
            var additionalMarkIds = additionalPropertyMarks.Concat(additionalMediaLibraryMarks)
                .Select(m => m.Id)
                .Where(id => !allMarkIds.Contains(id))
                .Distinct()
                .ToList();
            if (additionalMarkIds.Count > 0)
            {
                await LoadOldEffects(additionalMarkIds, ctx);
            }

            // Pre-create dynamic media libraries
            await PreCreateDynamicMediaLibraries(allActiveMediaLibraryMarks, ctx, ct);

            // 1b. Collect property effects
            await ReportProgress(onProgressChange, onProcessChange, 15, "Collecting property effects...");
            for (var i = 0; i < allActivePropertyMarks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = allActivePropertyMarks[i];
                var progress = 15 + (int)(10.0 * (i + 1) / Math.Max(1, allActivePropertyMarks.Count));
                await ReportProgress(onProgressChange, onProcessChange, progress,
                    _localizer.SyncPathMark_ProcessingProperty(mark.Path));

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    await CollectPropertyEffects(mark, ctx, ct);
                    ctx.SuccessfulMarkIds.Add(mark.Id);
                }
                catch (Exception ex)
                {
                    ctx.FailedMarks.Add((mark.Id, ex.Message));
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError { MarkId = mark.Id, Path = mark.Path, ErrorMessage = ex.Message });
                }
            }

            // 1c. Collect media library effects
            await ReportProgress(onProgressChange, onProcessChange, 25, "Collecting media library effects...");
            for (var i = 0; i < allActiveMediaLibraryMarks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = allActiveMediaLibraryMarks[i];
                var progress = 25 + (int)(5.0 * (i + 1) / Math.Max(1, allActiveMediaLibraryMarks.Count));
                await ReportProgress(onProgressChange, onProcessChange, progress,
                    _localizer.SyncPathMark_ProcessingMediaLibrary(mark.Path));

                try
                {
                    await _pathMarkService.MarkAsSyncing(mark.Id);
                    await CollectMediaLibraryEffects(mark, ctx, ct);
                    ctx.SuccessfulMarkIds.Add(mark.Id);
                }
                catch (Exception ex)
                {
                    ctx.FailedMarks.Add((mark.Id, ex.Message));
                    result.FailedMarks++;
                    result.Errors.Add(new PathMarkSyncError { MarkId = mark.Id, Path = mark.Path, ErrorMessage = ex.Message });
                }
            }

            // ===== Phase 2: Compute Final State (30-40%) =====
            await ReportProgress(onProgressChange, onProcessChange, 30, "Computing final state...");
            await ComputeFinalResourceState(ctx);
            await ComputeFinalPropertyState(ctx);
            await ComputeFinalMediaLibraryState(ctx);

            // ===== Phase 3: Apply Changes (40-80%) =====
            await ReportProgress(onProgressChange, onProcessChange, 40, "Applying resource changes...");
            var resourceResult = await ApplyResourceChanges(ctx, ct);
            result.ResourcesCreated = resourceResult.Created;
            result.ResourcesDeleted = resourceResult.Deleted;

            // Refresh context after resource changes
            if (resourceResult.Created > 0 || resourceResult.Deleted > 0)
            {
                ctx.AllResources = await _resourceService.GetAll();
                ctx.BuildIndexes();

                // For newly created resources, collect effects from all related Property/MediaLibrary marks
                if (resourceResult.Created > 0)
                {
                    var newResourcePaths = ctx.ResourcesToCreate.Keys.ToList();
                    await CollectEffectsForNewResources(newResourcePaths, allActivePropertyMarks, allActiveMediaLibraryMarks, ctx, ct);

                    // Recompute final state for the new effects
                    await ComputeFinalPropertyState(ctx);
                    await ComputeFinalMediaLibraryState(ctx);
                }
            }

            await ReportProgress(onProgressChange, onProcessChange, 55, "Applying property changes...");
            var propertyResult = await ApplyPropertyChanges(ctx, ct);
            result.PropertiesApplied = propertyResult.Applied;
            result.PropertiesDeleted = propertyResult.Deleted;

            await ReportProgress(onProgressChange, onProcessChange, 70, "Applying media library changes...");
            var mappingResult = await ApplyMediaLibraryChanges(ctx, ct);
            result.MediaLibraryMappingsCreated = mappingResult.Created;
            result.MediaLibraryMappingsDeleted = mappingResult.Deleted;

            // ===== Phase 4: Persist Effects (80-90%) =====
            await ReportProgress(onProgressChange, onProcessChange, 80, "Persisting effects...");
            await ComputeEffectDiff(ctx);
            await PersistEffects(ctx);

            // ===== Cleanup (90-100%) =====
            await ReportProgress(onProgressChange, onProcessChange, 90, _localizer.SyncPathMark_EstablishingRelationships());

            // Establish parent-child relationships
            var createdPaths = ctx.ResourcesToCreate.Keys.ToList();
            if (createdPaths.Count > 0)
            {
                await EstablishParentChildRelationships(createdPaths, ctx, ct);
            }

            await _resourceService.RefreshParentTag();

            // Update mark statuses
            await BatchUpdateMarkStatuses(ctx, marksToDelete);

            await ReportProgress(onProgressChange, onProcessChange, 100, _localizer.SyncPathMark_Complete());
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return result;
    }

    private async Task LoadOldEffects(List<int> markIds, SyncContext ctx)
    {
        var oldResourceEffects = await _effectService.GetResourceEffectsByMarkIds(markIds);
        foreach (var effect in oldResourceEffects)
        {
            if (!ctx.OldResourceEffectsByMarkId.TryGetValue(effect.MarkId, out var list))
            {
                list = new List<ResourceMarkEffect>();
                ctx.OldResourceEffectsByMarkId[effect.MarkId] = list;
            }
            list.Add(effect);
        }

        var oldPropertyEffects = await _effectService.GetPropertyEffectsByMarkIds(markIds);
        foreach (var effect in oldPropertyEffects)
        {
            if (!ctx.OldPropertyEffectsByMarkId.TryGetValue(effect.MarkId, out var list))
            {
                list = new List<PropertyMarkEffect>();
                ctx.OldPropertyEffectsByMarkId[effect.MarkId] = list;
            }
            list.Add(effect);
        }
    }

    private const int BatchSize = 500; // Process in chunks to reduce lock contention

    #region Phase 1: Collect Effects

    /// <summary>
    /// Collects resource effects from a mark WITHOUT creating any resources.
    /// Returns the list of paths that the mark wants to create resources for.
    /// </summary>
    private async Task<List<string>> CollectResourceEffects(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
        if (config == null) return new List<string>();

        var matchedPaths = GetMatchingPathsForResourceMark(mark.Path, config, ctx);
        var collectedPaths = new List<string>();

        foreach (var path in matchedPaths)
        {
            ct.ThrowIfCancellationRequested();

            var standardizedPath = ctx.GetStandardizedPath(path);

            // Skip if blocked by another mark's boundary
            if (ctx.IsPathBlockedByBoundary(standardizedPath, mark.Id))
                continue;

            // Check if path exists on filesystem
            var isFile = File.Exists(path);
            var isDirectory = Directory.Exists(path);
            if (!isFile && !isDirectory)
                continue;

            // Record the effect - this mark wants a resource at this path
            ctx.CollectedResourceEffects.Add(new ResourceMarkEffect
            {
                MarkId = mark.Id,
                Path = standardizedPath
            });
            ctx.CurrentResourceEffectKeys.Add((mark.Id, standardizedPath));

            // Track for finding related marks
            collectedPaths.Add(path);

            // Build FinalResourcePaths - aggregate all marks that want this path
            if (!ctx.FinalResourcePaths.TryGetValue(standardizedPath, out var markList))
            {
                markList = new List<int>();
                ctx.FinalResourcePaths[standardizedPath] = markList;
            }
            markList.Add(mark.Id);
        }

        return collectedPaths;
    }

    /// <summary>
    /// Collects property effects from a mark WITHOUT setting any property values.
    /// </summary>
    private Task CollectPropertyEffects(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
        if (config == null) return Task.CompletedTask;

        // Only support Custom properties for now
        if (config.Pool != PropertyPool.Custom) return Task.CompletedTask;

        // Use cached resources
        var matchedResources = ctx.AllResources
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        if (matchedResources.Count == 0) return Task.CompletedTask;

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);
        if (filteredResources.Count == 0) return Task.CompletedTask;

        var valueType = config.ValueType;
        var valueLayer = config.ValueLayer;
        var needsPerResourceExtraction = valueType == PropertyValueType.Dynamic && valueLayer.HasValue && valueLayer.Value > 0;

        // For fixed values or dynamic values with valueLayer <= 0, extract once
        object? sharedValue = null;
        if (valueType == PropertyValueType.Fixed)
        {
            sharedValue = config.FixedValue;
            if (sharedValue == null) return Task.CompletedTask;
        }
        else if (!needsPerResourceExtraction)
        {
            sharedValue = ExtractDynamicValue(mark.Path, null, config.MatchMode, valueLayer, config.ValueRegex, ctx);
            if (sharedValue == null) return Task.CompletedTask;
        }

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            object? value;
            if (needsPerResourceExtraction)
            {
                value = ExtractDynamicValue(mark.Path, resource.Path, config.MatchMode, valueLayer, config.ValueRegex, ctx);
                if (value == null) continue;
            }
            else
            {
                value = sharedValue!;
            }

            var valueString = value is string s ? s : System.Text.Json.JsonSerializer.Serialize(value);

            // Record the effect
            ctx.CollectedPropertyEffects.Add(new PropertyMarkEffect
            {
                MarkId = mark.Id,
                PropertyPool = config.Pool,
                PropertyId = config.PropertyId,
                ResourceId = resource.Id,
                Value = valueString,
                Priority = mark.Priority
            });
            ctx.CurrentPropertyEffectKeys.Add((mark.Id, config.Pool, config.PropertyId, resource.Id));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Collects media library effects from a mark WITHOUT creating any mappings.
    /// </summary>
    private Task CollectMediaLibraryEffects(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
        if (config == null) return Task.CompletedTask;

        // MediaLibrary effects use PropertyPool.Internal and PropertyId = 25 (MediaLibraryV2Multi)
        const int mediaLibraryPropertyId = 25;

        var matchedResources = ctx.AllResources
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        if (matchedResources.Count == 0) return Task.CompletedTask;

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);
        if (filteredResources.Count == 0) return Task.CompletedTask;

        var valueType = config.ValueType;
        var valueLayer = config.LayerToMediaLibrary;
        var needsPerResourceExtraction = valueType == PropertyValueType.Dynamic && valueLayer.HasValue && valueLayer.Value > 0;

        // For fixed values
        int? sharedMediaLibraryId = null;
        string? sharedMediaLibraryName = null;

        if (valueType == PropertyValueType.Fixed)
        {
            sharedMediaLibraryId = config.MediaLibraryId;
            if (!sharedMediaLibraryId.HasValue) return Task.CompletedTask;
        }
        else if (!needsPerResourceExtraction)
        {
            sharedMediaLibraryName = ExtractDynamicValue(mark.Path, null, config.MatchMode, valueLayer, config.RegexToMediaLibrary, ctx);
            if (string.IsNullOrEmpty(sharedMediaLibraryName)) return Task.CompletedTask;
            if (!ctx.MediaLibraryCache.TryGetValue(sharedMediaLibraryName, out var id)) return Task.CompletedTask;
            sharedMediaLibraryId = id;
        }

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            int mediaLibraryId;
            if (needsPerResourceExtraction)
            {
                var name = ExtractDynamicValue(mark.Path, resource.Path, config.MatchMode, valueLayer, config.RegexToMediaLibrary, ctx);
                if (string.IsNullOrEmpty(name)) continue;
                if (!ctx.MediaLibraryCache.TryGetValue(name, out mediaLibraryId)) continue;
            }
            else
            {
                mediaLibraryId = sharedMediaLibraryId!.Value;
            }

            // Record the effect (using Internal pool and MediaLibraryV2Multi property)
            ctx.CollectedPropertyEffects.Add(new PropertyMarkEffect
            {
                MarkId = mark.Id,
                PropertyPool = PropertyPool.Internal,
                PropertyId = mediaLibraryPropertyId,
                ResourceId = resource.Id,
                Value = mediaLibraryId.ToString(),
                Priority = mark.Priority
            });
            ctx.CurrentPropertyEffectKeys.Add((mark.Id, PropertyPool.Internal, mediaLibraryPropertyId, resource.Id));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Collects Property and MediaLibrary effects for newly created resources.
    /// This includes effects from all related marks that cover the new resource paths.
    /// Note: currentPropertyMarks/currentMediaLibraryMarks were processed in Phase 1, but they
    /// couldn't collect effects for new resources (which didn't exist at that time).
    /// So we need to process them again here for the new resources.
    /// </summary>
    private async Task CollectEffectsForNewResources(
        List<string> newResourcePaths,
        List<PathMark> currentPropertyMarks,
        List<PathMark> currentMediaLibraryMarks,
        SyncContext ctx,
        CancellationToken ct)
    {
        if (newResourcePaths.Count == 0) return;

        // Get all synced Property/MediaLibrary marks (not just the ones being synced now)
        var allSyncedPropertyMarks = await _pathMarkService.GetAll(m =>
            m.Type == PathMarkType.Property &&
            m.SyncStatus == PathMarkSyncStatus.Synced);

        var allSyncedMediaLibraryMarks = await _pathMarkService.GetAll(m =>
            m.Type == PathMarkType.MediaLibrary &&
            m.SyncStatus == PathMarkSyncStatus.Synced);

        // Combine all marks: currentMarks + syncedMarks (deduplicated)
        var currentPropertyMarkIds = currentPropertyMarks.Select(m => m.Id).ToHashSet();
        var currentMediaLibraryMarkIds = currentMediaLibraryMarks.Select(m => m.Id).ToHashSet();

        var allPropertyMarks = currentPropertyMarks
            .Concat(allSyncedPropertyMarks.Where(m => !currentPropertyMarkIds.Contains(m.Id)))
            .ToList();

        var allMediaLibraryMarks = currentMediaLibraryMarks
            .Concat(allSyncedMediaLibraryMarks.Where(m => !currentMediaLibraryMarkIds.Contains(m.Id)))
            .ToList();

        // Find which marks cover the new resource paths
        var relevantPropertyMarks = new List<PathMark>();
        var relevantMediaLibraryMarks = new List<PathMark>();

        foreach (var resourcePath in newResourcePaths)
        {
            var standardizedPath = ctx.GetStandardizedPath(resourcePath);
            if (string.IsNullOrEmpty(standardizedPath)) continue;

            foreach (var mark in allPropertyMarks)
            {
                var markPath = ctx.GetStandardizedPath(mark.Path);
                if (ctx.IsPathUnderParent(standardizedPath, markPath))
                {
                    if (!relevantPropertyMarks.Any(m => m.Id == mark.Id))
                    {
                        relevantPropertyMarks.Add(mark);
                    }
                }
            }

            foreach (var mark in allMediaLibraryMarks)
            {
                var markPath = ctx.GetStandardizedPath(mark.Path);
                if (ctx.IsPathUnderParent(standardizedPath, markPath))
                {
                    if (!relevantMediaLibraryMarks.Any(m => m.Id == mark.Id))
                    {
                        relevantMediaLibraryMarks.Add(mark);
                    }
                }
            }
        }

        // Collect effects from relevant marks for new resources only
        foreach (var mark in relevantPropertyMarks.OrderByDescending(m => m.Priority))
        {
            ct.ThrowIfCancellationRequested();
            await CollectPropertyEffectsForNewResources(mark, newResourcePaths, ctx, ct);
        }

        foreach (var mark in relevantMediaLibraryMarks.OrderByDescending(m => m.Priority))
        {
            ct.ThrowIfCancellationRequested();
            await CollectMediaLibraryEffectsForNewResources(mark, newResourcePaths, ctx, ct);
        }
    }

    /// <summary>
    /// Collects property effects from a mark for specific new resources only.
    /// </summary>
    private Task CollectPropertyEffectsForNewResources(PathMark mark, List<string> newResourcePaths, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
        if (config == null) return Task.CompletedTask;

        // Only support Custom properties for now
        if (config.Pool != PropertyPool.Custom) return Task.CompletedTask;

        // Get new resources that are under this mark's path
        var newResources = newResourcePaths
            .Select(p => ctx.GetStandardizedPath(p))
            .Where(p => !string.IsNullOrEmpty(p) && ctx.PathToResource.ContainsKey(p))
            .Select(p => ctx.PathToResource[p])
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        if (newResources.Count == 0) return Task.CompletedTask;

        var filteredResources = FilterResourcesByMarkConfig(newResources, mark.Path, config, ctx);
        if (filteredResources.Count == 0) return Task.CompletedTask;

        var valueType = config.ValueType;
        var valueLayer = config.ValueLayer;
        var needsPerResourceExtraction = valueType == PropertyValueType.Dynamic && valueLayer.HasValue && valueLayer.Value > 0;

        // For fixed values or dynamic values with valueLayer <= 0, extract once
        object? sharedValue = null;
        if (valueType == PropertyValueType.Fixed)
        {
            sharedValue = config.FixedValue;
            if (sharedValue == null) return Task.CompletedTask;
        }
        else if (!needsPerResourceExtraction)
        {
            sharedValue = ExtractDynamicValue(mark.Path, null, config.MatchMode, valueLayer, config.ValueRegex, ctx);
            if (sharedValue == null) return Task.CompletedTask;
        }

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            object? value;
            if (needsPerResourceExtraction)
            {
                value = ExtractDynamicValue(mark.Path, resource.Path, config.MatchMode, valueLayer, config.ValueRegex, ctx);
                if (value == null) continue;
            }
            else
            {
                value = sharedValue!;
            }

            var valueString = value is string s ? s : System.Text.Json.JsonSerializer.Serialize(value);

            // Record the effect
            ctx.CollectedPropertyEffects.Add(new PropertyMarkEffect
            {
                MarkId = mark.Id,
                PropertyPool = config.Pool,
                PropertyId = config.PropertyId,
                ResourceId = resource.Id,
                Value = valueString,
                Priority = mark.Priority
            });
            ctx.CurrentPropertyEffectKeys.Add((mark.Id, config.Pool, config.PropertyId, resource.Id));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Collects media library effects from a mark for specific new resources only.
    /// </summary>
    private Task CollectMediaLibraryEffectsForNewResources(PathMark mark, List<string> newResourcePaths, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
        if (config == null) return Task.CompletedTask;

        const int mediaLibraryPropertyId = 25;

        // Get new resources that are under this mark's path
        var newResources = newResourcePaths
            .Select(p => ctx.GetStandardizedPath(p))
            .Where(p => !string.IsNullOrEmpty(p) && ctx.PathToResource.ContainsKey(p))
            .Select(p => ctx.PathToResource[p])
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        if (newResources.Count == 0) return Task.CompletedTask;

        var filteredResources = FilterResourcesByMarkConfig(newResources, mark.Path, config, ctx);
        if (filteredResources.Count == 0) return Task.CompletedTask;

        var valueType = config.ValueType;
        var valueLayer = config.LayerToMediaLibrary;
        var needsPerResourceExtraction = valueType == PropertyValueType.Dynamic && valueLayer.HasValue && valueLayer.Value > 0;

        // For fixed values
        int? sharedMediaLibraryId = null;

        if (valueType == PropertyValueType.Fixed)
        {
            sharedMediaLibraryId = config.MediaLibraryId;
            if (!sharedMediaLibraryId.HasValue) return Task.CompletedTask;
        }
        else if (!needsPerResourceExtraction)
        {
            var mediaLibraryName = ExtractDynamicValue(mark.Path, null, config.MatchMode, valueLayer, config.RegexToMediaLibrary, ctx);
            if (string.IsNullOrEmpty(mediaLibraryName)) return Task.CompletedTask;
            if (!ctx.MediaLibraryCache.TryGetValue(mediaLibraryName, out var id)) return Task.CompletedTask;
            sharedMediaLibraryId = id;
        }

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            int mediaLibraryId;
            if (needsPerResourceExtraction)
            {
                var name = ExtractDynamicValue(mark.Path, resource.Path, config.MatchMode, valueLayer, config.RegexToMediaLibrary, ctx);
                if (string.IsNullOrEmpty(name)) continue;
                if (!ctx.MediaLibraryCache.TryGetValue(name, out mediaLibraryId)) continue;
            }
            else
            {
                mediaLibraryId = sharedMediaLibraryId!.Value;
            }

            // Record the effect
            ctx.CollectedPropertyEffects.Add(new PropertyMarkEffect
            {
                MarkId = mark.Id,
                PropertyPool = PropertyPool.Internal,
                PropertyId = mediaLibraryPropertyId,
                ResourceId = resource.Id,
                Value = mediaLibraryId.ToString(),
                Priority = mark.Priority
            });
            ctx.CurrentPropertyEffectKeys.Add((mark.Id, PropertyPool.Internal, mediaLibraryPropertyId, resource.Id));
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Phase 2: Compute Final State

    /// <summary>
    /// Computes which resources should exist based on collected effects.
    /// Prepares ResourcesToCreate for resources that don't exist yet.
    /// </summary>
    private async Task ComputeFinalResourceState(SyncContext ctx)
    {
        foreach (var (path, markIds) in ctx.FinalResourcePaths)
        {
            // If resource already exists, no need to create
            if (ctx.ExistingPathSet.Contains(path))
            {
                // But check for bakabase.json marker for path recovery
                var existingResourceId = await CheckBakabaseJsonMarker(path);
                if (existingResourceId.HasValue && ctx.IdToResource.TryGetValue(existingResourceId.Value, out var existingResource))
                {
                    if (!existingResource.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                    {
                        // Resource exists but path changed - update it
                        existingResource.Path = path;
                        existingResource.UpdatedAt = DateTime.Now;
                        ctx.ResourcesToCreate[path] = existingResource;
                    }
                }
                continue;
            }

            // Check for bakabase.json recovery
            var markerResourceId = await CheckBakabaseJsonMarker(path);
            if (markerResourceId.HasValue && ctx.IdToResource.TryGetValue(markerResourceId.Value, out var markerResource))
            {
                markerResource.Path = path;
                markerResource.UpdatedAt = DateTime.Now;
                ctx.ResourcesToCreate[path] = markerResource;
                continue;
            }

            // Need to create new resource
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

            ctx.ResourcesToCreate[path] = resource;
        }

        // Determine resources to delete: paths that had effects before but don't anymore
        foreach (var (markId, oldEffects) in ctx.OldResourceEffectsByMarkId)
        {
            foreach (var effect in oldEffects)
            {
                // If this path is no longer in FinalResourcePaths, it might need deletion
                if (!ctx.FinalResourcePaths.ContainsKey(effect.Path))
                {
                    ctx.ResourcePathsToDelete.Add(effect.Path);
                }
            }
        }
    }

    /// <summary>
    /// Computes final property values by combining all effects using PropertySystem.Combine.
    /// </summary>
    private async Task ComputeFinalPropertyState(SyncContext ctx)
    {
        // Group effects by (ResourceId, PropertyPool, PropertyId)
        var groupedEffects = ctx.CollectedPropertyEffects
            .GroupBy(e => (e.ResourceId, e.PropertyPool, e.PropertyId))
            .ToList();

        // Pre-load custom property definitions for type lookup
        var customPropertyIds = groupedEffects
            .Where(g => g.Key.PropertyPool == PropertyPool.Custom)
            .Select(g => g.Key.PropertyId)
            .Distinct()
            .ToList();

        var customProperties = customPropertyIds.Count > 0
            ? (await _customPropertyService.GetByKeys(customPropertyIds)).ToDictionary(p => p.Id)
            : new Dictionary<int, CustomProperty>();

        // Pre-load existing property values for comparison
        var resourceIds = groupedEffects.Select(g => g.Key.ResourceId).Distinct().ToList();
        var existingValues = resourceIds.Count > 0
            ? await _customPropertyValueService.GetAllDbModels(x =>
                resourceIds.Contains(x.ResourceId) &&
                x.Scope == (int)PropertyValueScope.Synchronization)
            : new List<CustomPropertyValueDbModel>();

        var existingValueMap = existingValues.ToDictionary(
            v => (v.ResourceId, v.PropertyId),
            v => v);

        foreach (var group in groupedEffects)
        {
            var (resourceId, pool, propertyId) = group.Key;

            // Skip Internal pool (handled separately for media library)
            if (pool == PropertyPool.Internal) continue;

            // Order by priority (descending) - for single-value properties, first wins
            var effects = group.OrderByDescending(e => e.Priority).ThenByDescending(e => e.MarkId).ToList();
            if (effects.Count == 0) continue;

            // Get PropertyType
            PropertyType? propertyType = pool switch
            {
                PropertyPool.Custom when customProperties.TryGetValue(propertyId, out var cp) => cp.Type,
                PropertyPool.Reserved => PropertySystem.Builtin.TryGet((ResourceProperty)propertyId)?.Type,
                _ => null
            };

            if (propertyType == null) continue;

            // Use PropertySystem to combine all effect values
            var combinedValue = PropertySystem.Property.CombineSerializedDbValues(
                propertyType.Value,
                effects.Select(e => e.Value));

            ctx.FinalPropertyValues[(resourceId, pool, propertyId)] = combinedValue;

            // Prepare DB model
            if (pool == PropertyPool.Custom)
            {
                if (existingValueMap.TryGetValue((resourceId, propertyId), out var existingValue))
                {
                    // Update existing
                    existingValue.Value = combinedValue;
                    ctx.FinalPropertyValuesToWrite.Add(existingValue);
                }
                else if (combinedValue != null)
                {
                    // Create new
                    ctx.FinalPropertyValuesToWrite.Add(new CustomPropertyValueDbModel
                    {
                        ResourceId = resourceId,
                        PropertyId = propertyId,
                        Value = combinedValue,
                        Scope = (int)PropertyValueScope.Synchronization
                    });
                }
            }
        }

        // Find properties to delete: existed before but no longer have any effects
        foreach (var (markId, oldEffects) in ctx.OldPropertyEffectsByMarkId)
        {
            foreach (var effect in oldEffects)
            {
                if (effect.PropertyPool == PropertyPool.Internal) continue;
                var key = (effect.ResourceId, effect.PropertyPool, effect.PropertyId);
                if (!ctx.FinalPropertyValues.ContainsKey(key))
                {
                    ctx.PropertyValuesToDelete.Add((effect.ResourceId, effect.PropertyId));
                }
            }
        }
    }

    /// <summary>
    /// Computes final media library mappings from collected effects.
    /// </summary>
    private async Task ComputeFinalMediaLibraryState(SyncContext ctx)
    {
        const int mediaLibraryPropertyId = 25;

        // Filter media library effects (Internal pool, PropertyId = 25)
        var mediaLibraryEffects = ctx.CollectedPropertyEffects
            .Where(e => e.PropertyPool == PropertyPool.Internal && e.PropertyId == mediaLibraryPropertyId)
            .ToList();

        // Group by ResourceId and aggregate all media library IDs
        var groupedEffects = mediaLibraryEffects
            .GroupBy(e => e.ResourceId)
            .ToList();

        foreach (var group in groupedEffects)
        {
            var resourceId = group.Key;
            var mediaLibraryIds = new HashSet<int>();

            foreach (var effect in group)
            {
                if (int.TryParse(effect.Value, out var mlId))
                {
                    mediaLibraryIds.Add(mlId);
                }
            }

            if (mediaLibraryIds.Count > 0)
            {
                ctx.FinalMediaLibraryMappings[resourceId] = mediaLibraryIds;

                // Add to final mappings list
                foreach (var mlId in mediaLibraryIds)
                {
                    ctx.FinalMappingsToEnsure.Add((resourceId, mlId));
                }
            }
        }

        // Find media library mappings to delete: existed before but no longer have any effects
        // Build a set of current mappings for fast lookup
        var currentMappings = new HashSet<(int ResourceId, int MediaLibraryId)>(ctx.FinalMappingsToEnsure);

        // Collect all potential mappings to delete (from old effects of synced marks)
        var potentialDeletes = new HashSet<(int ResourceId, int MediaLibraryId)>();

        foreach (var (markId, oldEffects) in ctx.OldPropertyEffectsByMarkId)
        {
            foreach (var effect in oldEffects)
            {
                // Only process media library effects (Internal pool, PropertyId = 25)
                if (effect.PropertyPool != PropertyPool.Internal || effect.PropertyId != mediaLibraryPropertyId)
                    continue;

                if (!int.TryParse(effect.Value, out var mediaLibraryId))
                    continue;

                var mapping = (effect.ResourceId, mediaLibraryId);

                // If this mapping is no longer in the final state of synced marks, it's a potential delete
                if (!currentMappings.Contains(mapping))
                {
                    potentialDeletes.Add(mapping);
                }
            }
        }

        // For each potential delete, check if there are other marks (not being synced) that still need this mapping
        if (potentialDeletes.Count > 0)
        {
            var syncedMarkIds = ctx.OldPropertyEffectsByMarkId.Keys.ToHashSet();
            var resourceIds = potentialDeletes.Select(m => m.ResourceId).Distinct().ToList();

            // Get all effects for these resources (including effects from marks not being synced)
            var allEffects = await _effectService.GetPropertyEffectsByResources(resourceIds, PropertyPool.Internal, mediaLibraryPropertyId);

            // Build a set of mappings that are still needed by other marks (not being synced)
            var mappingsStillNeeded = new HashSet<(int ResourceId, int MediaLibraryId)>();
            foreach (var effect in allEffects)
            {
                // Skip effects from marks that are being synced (we already know their new state)
                if (syncedMarkIds.Contains(effect.MarkId))
                    continue;

                if (int.TryParse(effect.Value, out var mediaLibraryId))
                {
                    mappingsStillNeeded.Add((effect.ResourceId, mediaLibraryId));
                }
            }

            // Only delete mappings that are not needed by any other marks
            foreach (var mapping in potentialDeletes)
            {
                if (!mappingsStillNeeded.Contains(mapping))
                {
                    ctx.MediaLibraryMappingsToDelete.Add(mapping);
                }
            }
        }
    }

    #endregion

    #region Phase 3: Apply Changes

    /// <summary>
    /// Creates and deletes resources based on computed final state.
    /// </summary>
    private async Task<(int Created, int Deleted)> ApplyResourceChanges(SyncContext ctx, CancellationToken ct)
    {
        var created = 0;
        var deleted = 0;

        // Create new resources
        if (ctx.ResourcesToCreate.Count > 0)
        {
            var resourcesToCreate = ctx.ResourcesToCreate.Values.ToList();
            var sw = Stopwatch.StartNew();

            var batches = resourcesToCreate.Chunk(BatchSize).ToList();
            for (var i = 0; i < batches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _resourceService.AddOrPutRange(batches[i].ToList());
                if (i < batches.Count - 1) await Task.Delay(5);
            }

            sw.Stop();
            _logger.LogInformation("[Sync] Created {Count} resources in {ElapsedMs}ms",
                resourcesToCreate.Count, sw.ElapsedMilliseconds);

            created = resourcesToCreate.Count;

            // Create bakabase.json markers for new folder resources
            if (_resourceOptions.Value.KeepResourcesOnPathChange)
            {
                foreach (var resource in resourcesToCreate.Where(r => !r.IsFile && r.Id > 0))
                {
                    await CreateBakabaseJsonMarker(resource.Path, resource.Id);
                }
            }
        }

        // Delete resources that no longer have any effects
        if (ctx.ResourcePathsToDelete.Count > 0)
        {
            var resourcesToDelete = new List<int>();
            foreach (var path in ctx.ResourcePathsToDelete)
            {
                ct.ThrowIfCancellationRequested();

                if (!ctx.PathToResource.TryGetValue(path, out var resource))
                    continue;

                // Check bakabase.json preservation
                if (_resourceOptions.Value.KeepResourcesOnPathChange && !resource.IsFile)
                {
                    var markerPath = Path.Combine(path, InternalOptions.ResourceMarkerFileName);
                    if (File.Exists(markerPath))
                        continue;
                }

                resourcesToDelete.Add(resource.Id);
            }

            if (resourcesToDelete.Count > 0)
            {
                await _resourceService.DeleteByKeys(resourcesToDelete.ToArray());
                deleted = resourcesToDelete.Count;
                _logger.LogInformation("[Sync] Deleted {Count} resources", deleted);
            }
        }

        return (created, deleted);
    }

    /// <summary>
    /// Applies final property values to database.
    /// </summary>
    private async Task<(int Applied, int Deleted)> ApplyPropertyChanges(SyncContext ctx, CancellationToken ct)
    {
        var applied = 0;
        var deleted = 0;

        // Write property values
        if (ctx.FinalPropertyValuesToWrite.Count > 0)
        {
            var toAdd = ctx.FinalPropertyValuesToWrite.Where(v => v.Id == 0).ToList();
            var toUpdate = ctx.FinalPropertyValuesToWrite.Where(v => v.Id > 0).ToList();

            if (toAdd.Count > 0)
            {
                var batches = toAdd.Chunk(BatchSize).ToList();
                for (var i = 0; i < batches.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await _customPropertyValueService.AddDbModelRange(batches[i].ToList());
                    if (i < batches.Count - 1) await Task.Delay(5);
                }
                _logger.LogInformation("[Sync] Added {Count} property values", toAdd.Count);
            }

            if (toUpdate.Count > 0)
            {
                var batches = toUpdate.Chunk(BatchSize).ToList();
                for (var i = 0; i < batches.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await _customPropertyValueService.UpdateDbModelRange(batches[i].ToList());
                    if (i < batches.Count - 1) await Task.Delay(5);
                }
                _logger.LogInformation("[Sync] Updated {Count} property values", toUpdate.Count);
            }

            applied = ctx.FinalPropertyValuesToWrite.Count;
        }

        // Delete property values that no longer have effects
        var distinctDeletes = ctx.PropertyValuesToDelete.Distinct().ToList();
        if (distinctDeletes.Count > 0)
        {
            foreach (var (resourceId, propertyId) in distinctDeletes)
            {
                ct.ThrowIfCancellationRequested();
                await _customPropertyValueService.RemoveAll(x =>
                    x.ResourceId == resourceId &&
                    x.PropertyId == propertyId &&
                    x.Scope == (int)PropertyValueScope.Synchronization);
            }
            deleted = distinctDeletes.Count;
            _logger.LogInformation("[Sync] Deleted {Count} property values", deleted);
        }

        return (applied, deleted);
    }

    /// <summary>
    /// Applies final media library mappings.
    /// </summary>
    private async Task<(int Created, int Deleted)> ApplyMediaLibraryChanges(SyncContext ctx, CancellationToken ct)
    {
        var created = 0;
        var deleted = 0;

        // Delete mappings that no longer have effects
        var distinctDeletes = ctx.MediaLibraryMappingsToDelete.Distinct().ToList();
        if (distinctDeletes.Count > 0)
        {
            var batches = distinctDeletes.Chunk(BatchSize).ToList();
            for (var i = 0; i < batches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _mappingService.DeleteMappingsRange(batches[i].ToList());
                if (i < batches.Count - 1) await Task.Delay(5);
            }
            deleted = distinctDeletes.Count;
            _logger.LogInformation("[Sync] Deleted {Count} media library mappings", deleted);
        }

        // Ensure new mappings exist
        if (ctx.FinalMappingsToEnsure.Count > 0)
        {
            var batches = ctx.FinalMappingsToEnsure.Chunk(BatchSize).ToList();
            for (var i = 0; i < batches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _mappingService.EnsureMappingsRange(batches[i].ToList());
                if (i < batches.Count - 1) await Task.Delay(5);
            }
            created = ctx.FinalMappingsToEnsure.Count;
            _logger.LogInformation("[Sync] Ensured {Count} media library mappings", created);
        }

        return (created, deleted);
    }

    #endregion

    #region Phase 4: Persist Effects

    /// <summary>
    /// Computes which effects to add and delete by comparing collected vs old.
    /// </summary>
    private Task ComputeEffectDiff(SyncContext ctx)
    {
        // Resource effects to add: in collected but not in old
        var oldResourceKeys = ctx.OldResourceEffectsByMarkId
            .SelectMany(kvp => kvp.Value.Select(e => (e.MarkId, e.Path)))
            .ToHashSet(new ResourceEffectKeyComparer());

        // Track added keys to avoid duplicates in CollectedResourceEffects
        var addedResourceKeys = new HashSet<(int MarkId, string Path)>(new ResourceEffectKeyComparer());

        foreach (var effect in ctx.CollectedResourceEffects)
        {
            var key = (effect.MarkId, effect.Path);
            if (!oldResourceKeys.Contains(key) && addedResourceKeys.Add(key))
            {
                ctx.ResourceEffectsToAdd.Add(effect);
            }
        }

        // Resource effects to delete: in old but not in collected
        foreach (var (markId, oldEffects) in ctx.OldResourceEffectsByMarkId)
        {
            foreach (var effect in oldEffects)
            {
                if (!ctx.CurrentResourceEffectKeys.Contains((effect.MarkId, effect.Path)))
                {
                    ctx.ResourceEffectIdsToDelete.Add(effect.Id);
                }
            }
        }

        // Property effects to add: in collected but not in old
        var oldPropertyKeys = ctx.OldPropertyEffectsByMarkId
            .SelectMany(kvp => kvp.Value.Select(e => (e.MarkId, e.PropertyPool, e.PropertyId, e.ResourceId)))
            .ToHashSet();

        // Track added keys to avoid duplicates in CollectedPropertyEffects
        var addedPropertyKeys = new HashSet<(int MarkId, PropertyPool Pool, int PropertyId, int ResourceId)>();

        foreach (var effect in ctx.CollectedPropertyEffects)
        {
            var key = (effect.MarkId, effect.PropertyPool, effect.PropertyId, effect.ResourceId);
            if (!oldPropertyKeys.Contains(key) && addedPropertyKeys.Add(key))
            {
                ctx.PropertyEffectsToAdd.Add(effect);
            }
        }

        // Property effects to delete: in old but not in collected
        foreach (var (markId, oldEffects) in ctx.OldPropertyEffectsByMarkId)
        {
            foreach (var effect in oldEffects)
            {
                if (!ctx.CurrentPropertyEffectKeys.Contains((effect.MarkId, effect.PropertyPool, effect.PropertyId, effect.ResourceId)))
                {
                    ctx.PropertyEffectIdsToDelete.Add(effect.Id);
                }
            }
        }

        _logger.LogInformation(
            "[Sync] Effect diff: ResourceEffects +{AddRes}/-{DelRes}, PropertyEffects +{AddProp}/-{DelProp}",
            ctx.ResourceEffectsToAdd.Count, ctx.ResourceEffectIdsToDelete.Count,
            ctx.PropertyEffectsToAdd.Count, ctx.PropertyEffectIdsToDelete.Count);

        return Task.CompletedTask;
    }

    private class ResourceEffectKeyComparer : IEqualityComparer<(int MarkId, string Path)>
    {
        public bool Equals((int MarkId, string Path) x, (int MarkId, string Path) y)
            => x.MarkId == y.MarkId && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int MarkId, string Path) obj)
            => HashCode.Combine(obj.MarkId, obj.Path?.ToLowerInvariant());
    }

    /// <summary>
    /// Persists effect changes to database.
    /// </summary>
    private async Task PersistEffects(SyncContext ctx)
    {
        var sw = Stopwatch.StartNew();

        // Add new resource effects
        if (ctx.ResourceEffectsToAdd.Count > 0)
        {
            var batches = ctx.ResourceEffectsToAdd.Chunk(BatchSize).ToList();
            for (var i = 0; i < batches.Count; i++)
            {
                await _effectService.AddResourceEffects(batches[i]);
                if (i < batches.Count - 1) await Task.Delay(5);
            }
        }

        // Delete stale resource effects
        if (ctx.ResourceEffectIdsToDelete.Count > 0)
        {
            await _effectService.DeleteResourceEffects(ctx.ResourceEffectIdsToDelete);
        }

        // Add new property effects
        if (ctx.PropertyEffectsToAdd.Count > 0)
        {
            var batches = ctx.PropertyEffectsToAdd.Chunk(BatchSize).ToList();
            for (var i = 0; i < batches.Count; i++)
            {
                await _effectService.AddPropertyEffects(batches[i]);
                if (i < batches.Count - 1) await Task.Delay(5);
            }
        }

        // Delete stale property effects
        if (ctx.PropertyEffectIdsToDelete.Count > 0)
        {
            await _effectService.DeletePropertyEffects(ctx.PropertyEffectIdsToDelete);
        }

        sw.Stop();
        _logger.LogInformation("[Sync] PersistEffects took {ElapsedMs}ms", sw.ElapsedMilliseconds);
    }

    #endregion

    #region Helper Methods

    private async Task BatchUpdateMarkStatuses(SyncContext ctx, List<int> marksToDelete)
    {
        var totalSw = Stopwatch.StartNew();

        // Batch mark as synced
        if (ctx.SuccessfulMarkIds.Count > 0)
        {
            var count = ctx.SuccessfulMarkIds.Count;
            var sw = Stopwatch.StartNew();
            await _pathMarkService.MarkAsSyncedBatch(ctx.SuccessfulMarkIds);
            sw.Stop();
            _logger.LogInformation("[Sync] MarkAsSyncedBatch ({Count}) took {ElapsedMs}ms", count, sw.ElapsedMilliseconds);
        }

        // Batch mark as failed
        foreach (var (markId, error) in ctx.FailedMarks)
        {
            await _pathMarkService.MarkAsFailed(markId, error);
        }

        // Batch delete
        if (marksToDelete.Count > 0)
        {
            var count = marksToDelete.Count;
            var sw = Stopwatch.StartNew();
            await _pathMarkService.HardDeleteBatch(marksToDelete);
            sw.Stop();
            _logger.LogInformation("[Sync] HardDeleteBatch ({Count}) took {ElapsedMs}ms", count, sw.ElapsedMilliseconds);
        }

        totalSw.Stop();
        _logger.LogInformation("[Sync] BatchUpdateMarkStatuses total took {ElapsedMs}ms", totalSw.ElapsedMilliseconds);
    }

    #endregion

    #region Private Methods

    private async Task ReportProgress(Func<int, Task>? onProgressChange, Func<string?, Task>? onProcessChange,
        int progress, string? process)
    {
        if (onProgressChange != null) await onProgressChange(progress);
        if (onProcessChange != null) await onProcessChange(process);
    }

    /// <summary>
    /// Unified dynamic value extraction method for both Property and MediaLibrary marks.
    ///
    /// Layer semantics (consistent with FilterByLayer):
    /// - valueLayer = 0: mark path's directory name itself
    /// - valueLayer &lt; 0 (e.g., -1, -2): go UP from mark path (parent directories)
    /// - valueLayer &gt; 0 (e.g., 1, 2): go DOWN from mark path (child directories), requires resourcePath
    ///
    /// When valueLayer &gt; 0, there can be multiple values (one per child), so we use resourcePath
    /// to determine which child directory's name to extract.
    /// </summary>
    private string? ExtractDynamicValue(
        string markPath,
        string? resourcePath,
        PathMatchMode matchMode,
        int? valueLayer,
        string? valueRegex,
        SyncContext ctx)
    {
        var markSegments = ctx.GetPathSegments(markPath);

        if (matchMode == PathMatchMode.Layer)
        {
            var layer = valueLayer ?? 0;

            if (layer == 0)
            {
                // layer=0 means the mark path's directory name itself
                var targetIndex = markSegments.Length - 1;
                if (targetIndex < 0) return null;
                return markSegments[targetIndex];
            }
            else if (layer < 0)
            {
                // Negative layer: go UP from mark path (parent directories)
                // -1 = parent, -2 = grandparent, etc.
                var targetIndex = markSegments.Length - 1 + layer; // layer is negative, so this goes up
                if (targetIndex < 0 || targetIndex >= markSegments.Length)
                {
                    return null;
                }
                return markSegments[targetIndex];
            }
            else
            {
                // Positive layer: go DOWN from mark path (child directories)
                // 1 = first level child, 2 = second level child, etc.
                // We need resourcePath to determine which child directory to use
                if (string.IsNullOrEmpty(resourcePath)) return null;

                var resourceSegments = ctx.GetPathSegments(resourcePath);
                // The target segment is markSegments.Length + layer - 1
                // e.g., mark = /a/b (2 segments), layer = 1, target = segment index 2 (first child)
                var targetIndex = markSegments.Length + layer - 1;
                if (targetIndex < 0 || targetIndex >= resourceSegments.Length)
                {
                    return null;
                }
                return resourceSegments[targetIndex];
            }
        }
        else if (matchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(valueRegex))
        {
            // For regex mode, apply regex on the mark path's directory name
            var normalizedMarkPath = ctx.GetStandardizedPath(markPath);
            var markDirectoryName = Path.GetFileName(normalizedMarkPath);
            try
            {
                var regex = ctx.GetOrCreateRegex(valueRegex);
                var match = regex.Match(markDirectoryName);
                if (match.Success)
                {
                    return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                }
                return null;
            }
            catch
            {
                return markDirectoryName;
            }
        }

        return null;
    }

    private async Task PreCreateDynamicMediaLibraries(List<PathMark> mediaLibraryMarks, SyncContext ctx,
        CancellationToken ct)
    {
        // First, load all existing media libraries into cache
        var existingLibraries = await _mediaLibraryV2Service.GetAll();
        foreach (var lib in existingLibraries)
        {
            if (!string.IsNullOrEmpty(lib.Name) && !ctx.MediaLibraryCache.ContainsKey(lib.Name))
            {
                ctx.MediaLibraryCache[lib.Name] = lib.Id;
            }
        }

        // Collect all dynamic media library names that don't exist yet
        var newMediaLibraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mark in mediaLibraryMarks)
        {
            ct.ThrowIfCancellationRequested();

            if (mark.SyncStatus == PathMarkSyncStatus.PendingDelete) continue;

            var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
            if (config == null || config.ValueType != PropertyValueType.Dynamic) continue;

            var valueLayer = config.LayerToMediaLibrary ?? 0;

            if (valueLayer > 0)
            {
                // When valueLayer > 0, we need to extract values from each relevant resource
                var matchedResources = ctx.AllResources
                    .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
                    .ToList();
                var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);

                foreach (var resource in filteredResources)
                {
                    var mediaLibraryName = ExtractDynamicValue(
                        mark.Path,
                        resource.Path,
                        config.MatchMode,
                        config.LayerToMediaLibrary,
                        config.RegexToMediaLibrary,
                        ctx);

                    if (!string.IsNullOrEmpty(mediaLibraryName) && !ctx.MediaLibraryCache.ContainsKey(mediaLibraryName))
                    {
                        newMediaLibraryNames.Add(mediaLibraryName);
                    }
                }
            }
            else
            {
                // When valueLayer <= 0, extract once from mark path
                var mediaLibraryName = ExtractDynamicValue(
                    mark.Path,
                    null,
                    config.MatchMode,
                    config.LayerToMediaLibrary,
                    config.RegexToMediaLibrary,
                    ctx);

                if (!string.IsNullOrEmpty(mediaLibraryName) && !ctx.MediaLibraryCache.ContainsKey(mediaLibraryName))
                {
                    newMediaLibraryNames.Add(mediaLibraryName);
                }
            }
        }

        // Batch create new media libraries
        foreach (var name in newMediaLibraryNames)
        {
            ct.ThrowIfCancellationRequested();

            var newLibrary = await _mediaLibraryV2Service.Add(new MediaLibraryV2AddOrPutInputModel(
                Name: name,
                Paths: new List<string>()
            ));
            ctx.MediaLibraryCache[name] = newLibrary.Id;
        }
    }

    private async Task<List<PathMark>> FindRelatedMarksAsync(List<string> resourcePaths, PathMarkType type,
        List<PathMark> excludeMarks, SyncContext ctx)
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
            if (mark.SyncStatus is not (PathMarkSyncStatus.Pending or PathMarkSyncStatus.PendingDelete
                or PathMarkSyncStatus.Synced))
                continue;

            var markPath = ctx.GetStandardizedPath(mark.Path);
            if (string.IsNullOrEmpty(markPath)) continue;

            // Check if any resource path starts with this mark's path
            var markPathWithSeparator = markPath + InternalOptions.DirSeparator;

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

    private async Task EstablishParentChildRelationships(List<string> resourcePaths, SyncContext ctx,
        CancellationToken ct)
    {
        var changedResources = new Dictionary<int, Resource>();

        foreach (var path in resourcePaths)
        {
            ct.ThrowIfCancellationRequested();

            var standardizedPath = ctx.GetStandardizedPath(path);
            if (string.IsNullOrEmpty(standardizedPath) || !ctx.PathToResource.TryGetValue(standardizedPath, out var resource))
                continue;

            // 1. Walk up the directory tree to find the closest parent resource
            var parentPath = Path.GetDirectoryName(standardizedPath);
            int? parentResourceId = null;

            while (!string.IsNullOrEmpty(parentPath))
            {
                if (ctx.PathToResource.TryGetValue(parentPath, out var parentResource))
                {
                    parentResourceId = parentResource.Id;
                    break;
                }
                parentPath = Path.GetDirectoryName(parentPath);
            }

            if (resource.ParentId != parentResourceId)
            {
                resource.ParentId = parentResourceId;
                changedResources[resource.Id] = resource;
            }

            // 2. Find child resources that should have this resource as their parent
            var pathWithSeparator = standardizedPath + Path.DirectorySeparatorChar;
            foreach (var (childPath, childResource) in ctx.PathToResource)
            {
                // Skip self
                if (childPath.Equals(standardizedPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check if childPath is under this resource's path
                if (!childPath.StartsWith(pathWithSeparator, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Find the closest parent for this child (it might be this resource or a deeper one)
                var childParentPath = Path.GetDirectoryName(childPath);
                int? childParentResourceId = null;

                while (!string.IsNullOrEmpty(childParentPath))
                {
                    if (ctx.PathToResource.TryGetValue(childParentPath, out var childParentResource))
                    {
                        childParentResourceId = childParentResource.Id;
                        break;
                    }
                    childParentPath = Path.GetDirectoryName(childParentPath);
                }

                if (childResource.ParentId != childParentResourceId)
                {
                    childResource.ParentId = childParentResourceId;
                    changedResources[childResource.Id] = childResource;
                }
            }
        }

        if (changedResources.Count > 0)
        {
            await _resourceService.AddOrPutRange(changedResources.Values.ToList());
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
                    initialMatches = GetEntriesAtLayer(normalizedRoot, layer, config.FsTypeFilter, config.Extensions,
                        ctx);
                }
            }
            else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
            {
                var regex = ctx.GetOrCreateRegex(config.Regex);
                var entries = GetAllEntries(normalizedRoot, config.FsTypeFilter, config.Extensions, ctx);
                initialMatches = new List<string>();

                foreach (var entry in entries)
                {
                    var relativePath = entry.Substring(normalizedRoot.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

    private List<string> GetAllEntries(string rootPath, PathFilterFsType? fsTypeFilter, List<string>? extensions,
        SyncContext ctx)
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

    private List<string> GetEntriesAtLayer(string rootPath, int layer, PathFilterFsType? fsTypeFilter,
        List<string>? extensions, SyncContext ctx)
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

    private List<string> GetParentAtLayer(string rootPath, int absLayer, PathFilterFsType? fsTypeFilter,
        SyncContext ctx)
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
    private List<Resource> FilterResourcesByMarkConfig<TConfig>(List<Resource> resources, string markPath,
        TConfig config, SyncContext ctx)
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

        // If no valid matchMode is configured, return empty list (no resources should be processed)
        if (matchMode == null) return new List<Resource>();

        var includeSubdirectories = applyScope == PathMarkApplyScope.MatchedAndSubdirectories;

        return matchMode switch
        {
            PathMatchMode.Layer when layer.HasValue => FilterByLayer(resources, normalizedMarkPath, layer.Value,
                includeSubdirectories, ctx),
            PathMatchMode.Regex when !string.IsNullOrEmpty(regex) => FilterByRegex(resources, normalizedMarkPath, regex,
                includeSubdirectories, ctx),
            _ => new List<Resource>()
        };
    }

    private List<Resource> FilterByLayer(List<Resource> resources, string rootPath, int layer,
        bool includeSubdirectories, SyncContext ctx)
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
                if (includeSubdirectories)
                {
                    // Include resources at target layer and below (deeper)
                    return resourceSegments.Length >= targetSegmentCount;
                }

                return resourceSegments.Length == targetSegmentCount;
            }).ToList();
        }

        var targetDepth = layer;
        return resources.Where(r =>
        {
            var resourceSegments = ctx.GetPathSegments(r.Path);
            var relativeDepth = resourceSegments.Length - rootSegments.Length;
            if (includeSubdirectories)
            {
                // Include resources at target layer and below (deeper)
                return relativeDepth >= targetDepth;
            }

            return relativeDepth == targetDepth;
        }).ToList();
    }

    private List<Resource> FilterByRegex(List<Resource> resources, string rootPath, string pattern,
        bool includeSubdirectories, SyncContext ctx)
    {
        var regex = ctx.GetOrCreateRegex(pattern);

        // First find resources that match the regex
        var matchedResources = resources.Where(r =>
        {
            var standardizedPath = ctx.GetStandardizedPath(r.Path);
            var relativePath = standardizedPath.Substring(rootPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return regex.IsMatch(relativePath);
        }).ToList();

        if (!includeSubdirectories)
        {
            return matchedResources;
        }

        // Include subdirectories of matched resources
        var matchedPaths = matchedResources.Select(r => ctx.GetStandardizedPath(r.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<Resource>(matchedResources);

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
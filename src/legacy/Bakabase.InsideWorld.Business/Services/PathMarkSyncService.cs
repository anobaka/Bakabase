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
/// Service that handles property and media library synchronization for path marks.
/// This service processes pending Property and MediaLibrary path marks only.
/// Resource discovery is handled separately by <see cref="ResourceSyncService"/>.
///
/// This service always loads ALL resources to compute property/media library effects.
/// Only marks with status=Pending are processed.
///
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
    private readonly IResourceSourceLinkService _sourceLinkService;
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
        IResourceSourceLinkService sourceLinkService,
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
        _sourceLinkService = sourceLinkService;
        _localizer = localizer;
        _logger = logger;
    }

    /// <summary>
    /// Synchronize pending Property and MediaLibrary path marks.
    ///
    /// Flow:
    /// 1. Phase 1: Collect Effects - Process pending marks and collect what they WANT to do
    /// 2. Phase 2: Compute Final State - Use Combine to merge effects into final values
    /// 3. Phase 3: Apply Changes - Create/update/delete property values and media library mappings
    /// 4. Phase 4: Persist Effects - Save effects to DB
    ///
    /// All resources are loaded as context for effect computation.
    ///
    /// WARNING: This method is NOT thread-safe. Use <see cref="Components.PathSyncManager"/> to coordinate.
    /// </summary>
    /// <param name="onProgressChange">Progress callback (0-100).</param>
    /// <param name="onProcessChange">Process description callback.</param>
    /// <param name="pt">Pause token.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<PathMarkSyncResult> SyncMarks(
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

            // Load all pending Property and MediaLibrary marks
            var allMarks = await _pathMarkService.GetAll();
            var pendingMarks = allMarks
                .Where(m => m.SyncStatus is PathMarkSyncStatus.Pending or PathMarkSyncStatus.PendingDelete)
                .Where(m => m.Type is PathMarkType.Property or PathMarkType.MediaLibrary)
                .ToList();

            if (pendingMarks.Count == 0)
            {
                await ReportProgress(onProgressChange, onProcessChange, 100, _localizer.SyncPathMark_Complete());
                return result;
            }

            // Preload ALL resources (context for property/ML sync)
            ctx.AllResources = await _resourceService.GetAll();
            var allSourceLinks = await _sourceLinkService.GetAll();
            ctx.BuildIndexes(allSourceLinks);

            // Separate marks by type and status
            var propertyMarks = pendingMarks
                .Where(m => m.Type == PathMarkType.Property)
                .OrderByDescending(m => m.Priority)
                .ToList();
            var mediaLibraryMarks = pendingMarks
                .Where(m => m.Type == PathMarkType.MediaLibrary)
                .OrderByDescending(m => m.Priority)
                .ToList();

            var marksToDelete = pendingMarks
                .Where(m => m.SyncStatus == PathMarkSyncStatus.PendingDelete)
                .Select(m => m.Id)
                .ToList();
            var activePropertyMarks = propertyMarks
                .Where(m => m.SyncStatus != PathMarkSyncStatus.PendingDelete)
                .ToList();
            var activeMediaLibraryMarks = mediaLibraryMarks
                .Where(m => m.SyncStatus != PathMarkSyncStatus.PendingDelete)
                .ToList();

            // Load old effects for diff calculation
            var allMarkIds = pendingMarks.Select(m => m.Id).ToList();
            await LoadOldEffects(allMarkIds, ctx);

            // Also load old effects from all synced marks that might be relevant
            // (to correctly compute final state considering all marks, not just pending ones)
            var syncedMarkIds = allMarks
                .Where(m => m.SyncStatus == PathMarkSyncStatus.Synced
                            && m.Type is PathMarkType.Property or PathMarkType.MediaLibrary)
                .Select(m => m.Id)
                .Where(id => !allMarkIds.Contains(id))
                .ToList();
            if (syncedMarkIds.Count > 0)
            {
                await LoadOldEffects(syncedMarkIds, ctx);
            }

            await ReportProgress(onProgressChange, onProcessChange, 5,
                _localizer.SyncPathMark_Collected(pendingMarks.Count));

            // Pre-create dynamic media libraries
            await PreCreateDynamicMediaLibraries(activeMediaLibraryMarks, ctx, ct);

            // ===== Phase 1: Collect Effects (5-30%) =====

            // 1a. Collect property effects
            await ReportProgress(onProgressChange, onProcessChange, 5, "Collecting property effects...");
            for (var i = 0; i < activePropertyMarks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = activePropertyMarks[i];
                var progress = 5 + (int)(15.0 * (i + 1) / Math.Max(1, activePropertyMarks.Count));
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
                    result.Errors.Add(new PathMarkSyncError
                        { MarkId = mark.Id, Path = mark.Path, ErrorMessage = ex.Message });
                }
            }

            // 1b. Collect media library effects
            await ReportProgress(onProgressChange, onProcessChange, 20, "Collecting media library effects...");
            for (var i = 0; i < activeMediaLibraryMarks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = activeMediaLibraryMarks[i];
                var progress = 20 + (int)(10.0 * (i + 1) / Math.Max(1, activeMediaLibraryMarks.Count));
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
                    result.Errors.Add(new PathMarkSyncError
                        { MarkId = mark.Id, Path = mark.Path, ErrorMessage = ex.Message });
                }
            }

            // ===== Phase 2: Compute Final State (30-50%) =====
            await ReportProgress(onProgressChange, onProcessChange, 30, "Computing final state...");
            await ComputeFinalPropertyState(ctx);
            await ComputeFinalMediaLibraryState(ctx);

            // ===== Phase 3: Apply Changes (50-80%) =====
            await ReportProgress(onProgressChange, onProcessChange, 50, "Applying property changes...");
            var propertyResult = await ApplyPropertyChanges(ctx, ct);
            result.PropertiesApplied = propertyResult.Applied;
            result.PropertiesDeleted = propertyResult.Deleted;

            await ReportProgress(onProgressChange, onProcessChange, 65, "Applying media library changes...");
            var mappingResult = await ApplyMediaLibraryChanges(ctx, ct);
            result.MediaLibraryMappingsCreated = mappingResult.Created;
            result.MediaLibraryMappingsDeleted = mappingResult.Deleted;

            // Refresh resource count for affected media libraries
            if (mappingResult.Created > 0 || mappingResult.Deleted > 0)
            {
                var affectedMediaLibraryIds = ctx.FinalMappingsToEnsure
                    .Select(m => m.MediaLibraryId)
                    .Concat(ctx.MediaLibraryMappingsToDelete.Select(m => m.MediaLibraryId))
                    .Distinct()
                    .ToList();

                foreach (var mlId in affectedMediaLibraryIds)
                {
                    await _mediaLibraryV2Service.RefreshResourceCount(mlId);
                }

                _logger.LogInformation("[Sync] Refreshed resource count for {Count} media libraries",
                    affectedMediaLibraryIds.Count);
            }

            // ===== Phase 4: Persist Effects (80-95%) =====
            await ReportProgress(onProgressChange, onProcessChange, 80, "Persisting effects...");
            await ComputeEffectDiff(ctx);
            await PersistEffects(ctx);

            // ===== Cleanup (95-100%) =====
            await ReportProgress(onProgressChange, onProcessChange, 95, "Updating mark statuses...");
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

    private const int BatchSize = 500;

    #region Phase 1: Collect Effects

    /// <summary>
    /// Collects property effects from a mark WITHOUT setting any property values.
    /// </summary>
    private Task CollectPropertyEffects(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
        if (config == null) return Task.CompletedTask;

        // Only support Custom properties for now
        if (config.Pool != PropertyPool.Custom) return Task.CompletedTask;

        // Use cached resources - match ALL resources under this mark's path
        var matchedResources = ctx.AllResources
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        if (matchedResources.Count == 0) return Task.CompletedTask;

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);
        if (filteredResources.Count == 0) return Task.CompletedTask;

        var valueType = config.ValueType;
        var valueLayer = config.ValueLayer;
        var needsPerResourceExtraction =
            valueType == PropertyValueType.Dynamic && valueLayer.HasValue && valueLayer.Value > 0;

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
                value = ExtractDynamicValue(mark.Path, resource.Path, config.MatchMode, valueLayer, config.ValueRegex,
                    ctx);
                if (value == null) continue;
            }
            else
            {
                value = sharedValue!;
            }

            var valueString = value is string s ? s : System.Text.Json.JsonSerializer.Serialize(value, System.Text.Json.JsonSerializerOptions.Web);

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
        var needsPerResourceExtraction =
            valueType == PropertyValueType.Dynamic && valueLayer.HasValue && valueLayer.Value > 0;

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
            sharedMediaLibraryName = ExtractDynamicValue(mark.Path, null, config.MatchMode, valueLayer,
                config.RegexToMediaLibrary, ctx);
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
                var name = ExtractDynamicValue(mark.Path, resource.Path, config.MatchMode, valueLayer,
                    config.RegexToMediaLibrary, ctx);
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

    #endregion

    #region Phase 2: Compute Final State

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
        var currentMappings = new HashSet<(int ResourceId, int MediaLibraryId)>(ctx.FinalMappingsToEnsure);

        var potentialDeletes = new HashSet<(int ResourceId, int MediaLibraryId)>();

        foreach (var (markId, oldEffects) in ctx.OldPropertyEffectsByMarkId)
        {
            foreach (var effect in oldEffects)
            {
                if (effect.PropertyPool != PropertyPool.Internal || effect.PropertyId != mediaLibraryPropertyId)
                    continue;

                if (!int.TryParse(effect.Value, out var mediaLibraryId))
                    continue;

                var mapping = (effect.ResourceId, mediaLibraryId);

                if (!currentMappings.Contains(mapping))
                {
                    potentialDeletes.Add(mapping);
                }
            }
        }

        // For each potential delete, check if there are other marks that still need this mapping
        if (potentialDeletes.Count > 0)
        {
            var syncedMarkIds = ctx.OldPropertyEffectsByMarkId.Keys.ToHashSet();
            var resourceIds = potentialDeletes.Select(m => m.ResourceId).Distinct().ToList();

            var allEffects = await _effectService.GetPropertyEffectsByResources(resourceIds, PropertyPool.Internal,
                mediaLibraryPropertyId);

            var mappingsStillNeeded = new HashSet<(int ResourceId, int MediaLibraryId)>();
            foreach (var effect in allEffects)
            {
                if (syncedMarkIds.Contains(effect.MarkId))
                    continue;

                if (int.TryParse(effect.Value, out var mediaLibraryId))
                {
                    mappingsStillNeeded.Add((effect.ResourceId, mediaLibraryId));
                }
            }

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
        // Property effects to add: in collected but not in old
        var oldPropertyKeys = ctx.OldPropertyEffectsByMarkId
            .SelectMany(kvp => kvp.Value.Select(e => (e.MarkId, e.PropertyPool, e.PropertyId, e.ResourceId)))
            .ToHashSet();

        var addedPropertyKeys =
            new HashSet<(int MarkId, PropertyPool Pool, int PropertyId, int ResourceId)>();

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
                if (!ctx.CurrentPropertyEffectKeys.Contains((effect.MarkId, effect.PropertyPool, effect.PropertyId,
                        effect.ResourceId)))
                {
                    ctx.PropertyEffectIdsToDelete.Add(effect.Id);
                }
            }
        }

        _logger.LogInformation(
            "[Sync] Effect diff: PropertyEffects +{AddProp}/-{DelProp}",
            ctx.PropertyEffectsToAdd.Count, ctx.PropertyEffectIdsToDelete.Count);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists effect changes to database.
    /// </summary>
    private async Task PersistEffects(SyncContext ctx)
    {
        var sw = Stopwatch.StartNew();

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
            _logger.LogInformation("[Sync] MarkAsSyncedBatch ({Count}) took {ElapsedMs}ms", count,
                sw.ElapsedMilliseconds);
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
            _logger.LogInformation("[Sync] HardDeleteBatch ({Count}) took {ElapsedMs}ms", count,
                sw.ElapsedMilliseconds);
        }

        totalSw.Stop();
        _logger.LogInformation("[Sync] BatchUpdateMarkStatuses total took {ElapsedMs}ms",
            totalSw.ElapsedMilliseconds);
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
                var targetIndex = markSegments.Length - 1;
                if (targetIndex < 0) return null;
                return markSegments[targetIndex];
            }
            else if (layer < 0)
            {
                var targetIndex = markSegments.Length - 1 + layer;
                if (targetIndex < 0 || targetIndex >= markSegments.Length)
                {
                    return null;
                }

                return markSegments[targetIndex];
            }
            else
            {
                if (string.IsNullOrEmpty(resourcePath)) return null;

                var resourceSegments = ctx.GetPathSegments(resourcePath);
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

                    if (!string.IsNullOrEmpty(mediaLibraryName) &&
                        !ctx.MediaLibraryCache.ContainsKey(mediaLibraryName))
                    {
                        newMediaLibraryNames.Add(mediaLibraryName);
                    }
                }
            }
            else
            {
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

        if (matchMode == null) return new List<Resource>();

        var includeSubdirectories = applyScope == PathMarkApplyScope.MatchedAndSubdirectories;

        return matchMode switch
        {
            PathMatchMode.Layer when layer.HasValue => FilterByLayer(resources, normalizedMarkPath, layer.Value,
                includeSubdirectories, ctx),
            PathMatchMode.Regex when !string.IsNullOrEmpty(regex) => FilterByRegex(resources, normalizedMarkPath,
                regex,
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
                return relativeDepth >= targetDepth;
            }

            return relativeDepth == targetDepth;
        }).ToList();
    }

    private List<Resource> FilterByRegex(List<Resource> resources, string rootPath, string pattern,
        bool includeSubdirectories, SyncContext ctx)
    {
        var regex = ctx.GetOrCreateRegex(pattern);

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

    #endregion
}

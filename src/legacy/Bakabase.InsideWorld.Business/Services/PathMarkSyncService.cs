using Bakabase.Abstractions.Components.Tasks;
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
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
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
    private readonly IResourceSearchIndexService _resourceSearchIndexService;
    private readonly IMediaLibraryResourceMappingService _mappingService;
    private readonly IMediaLibraryV2Service _mediaLibraryV2Service;
    private readonly ICustomPropertyValueService _customPropertyValueService;
    private readonly ICustomPropertyService _customPropertyService;
    private readonly IStandardValueService _standardValueService;
    private readonly IPathMarkEffectService _effectService;
    private readonly IResourceSourceLinkService _sourceLinkService;
    private readonly IBakabaseLocalizer _localizer;
    private readonly ILogger<PathMarkSyncService> _logger;

    public PathMarkSyncService(
        IServiceProvider serviceProvider,
        IPathMarkService pathMarkService,
        IResourceService resourceService,
        IResourceSearchIndexService resourceSearchIndexService,
        IMediaLibraryResourceMappingService mappingService,
        IMediaLibraryV2Service mediaLibraryV2Service,
        ICustomPropertyValueService customPropertyValueService,
        ICustomPropertyService customPropertyService,
        IStandardValueService standardValueService,
        IPathMarkEffectService effectService,
        IResourceSourceLinkService sourceLinkService,
        IBakabaseLocalizer localizer,
        ILogger<PathMarkSyncService> logger) : base(serviceProvider)
    {
        _pathMarkService = pathMarkService;
        _resourceService = resourceService;
        _resourceSearchIndexService = resourceSearchIndexService;
        _mappingService = mappingService;
        _mediaLibraryV2Service = mediaLibraryV2Service;
        _customPropertyValueService = customPropertyValueService;
        _customPropertyService = customPropertyService;
        _standardValueService = standardValueService;
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
    /// 5. Phase 5: Wait for the resource search index to catch up before marking the sync complete
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
        Func<BTaskText?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct)
    {
        var result = new PathMarkSyncResult();
        var ctx = new SyncContext();

        try
        {
            // ===== Initialization (0-5%) =====
            await ReportProgress(onProgressChange, onProcessChange, 0, BTaskText.Localize(_localizer, "SyncPathMark_Collecting"));

            // Load all pending Property and MediaLibrary marks
            var allMarks = await _pathMarkService.GetAll();
            foreach (var mark in allMarks)
            {
                ctx.PathMarksById[mark.Id] = mark;
                if (mark.Type == PathMarkType.Property)
                {
                    try
                    {
                        ctx.PropertyMarkConfigsByMarkId[mark.Id] =
                            JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
                    }
                    catch
                    {
                        // The mark's normal collection path will report malformed config
                        // if it is pending. For a context mark, it simply cannot help
                        // disambiguate a historical reference value.
                        ctx.PropertyMarkConfigsByMarkId[mark.Id] = null;
                    }
                }
            }
            var pendingMarks = allMarks
                .Where(m => m.SyncStatus is PathMarkSyncStatus.Pending or PathMarkSyncStatus.PendingDelete)
                .Where(m => m.Type is PathMarkType.Property or PathMarkType.MediaLibrary)
                .ToList();

            if (pendingMarks.Count == 0)
            {
                await ReportProgress(onProgressChange, onProcessChange, 100, BTaskText.Localize(_localizer, "SyncPathMark_Complete"));
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

            // Load old effects for marks being synced this run (delete-candidates + effect diff).
            var runMarkIds = pendingMarks.Select(m => m.Id).ToList();
            foreach (var id in runMarkIds) ctx.RunMarkIds.Add(id);
            await LoadOldEffects(runMarkIds, ctx.RunOldEffectsByMarkId);

            // Load old effects from already-synced Property/MediaLibrary marks as
            // *context only* — used to recompute combined values and to protect
            // mappings contributed by other marks. They are never deleted here;
            // legacy DB-valued reference effects can be normalized to the canonical
            // business-value representation during final-state computation.
            var contextMarkIds = allMarks
                .Where(m => m.SyncStatus == PathMarkSyncStatus.Synced
                            && m.Type is PathMarkType.Property or PathMarkType.MediaLibrary)
                .Select(m => m.Id)
                .Where(id => !ctx.RunMarkIds.Contains(id))
                .ToList();
            if (contextMarkIds.Count > 0)
            {
                await LoadOldEffects(contextMarkIds, ctx.ContextOldEffectsByMarkId);
            }

            await ReportProgress(onProgressChange, onProcessChange, 5,
                BTaskText.Localize(_localizer, "SyncPathMark_Collected", pendingMarks.Count));

            // Pre-create dynamic media libraries
            await PreCreateDynamicMediaLibraries(activeMediaLibraryMarks, ctx, ct);

            // ===== Phase 1: Collect Effects (5-30%) =====

            // 1a. Collect property effects
            await ReportProgress(onProgressChange, onProcessChange, 5,
                BTaskText.Localize(_localizer, "SyncPathMark_CollectingPropertyEffects"));
            for (var i = 0; i < activePropertyMarks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = activePropertyMarks[i];
                var progress = 5 + (int)(15.0 * (i + 1) / Math.Max(1, activePropertyMarks.Count));
                await ReportProgress(onProgressChange, onProcessChange, progress,
                    BTaskText.Localize(_localizer, "SyncPathMark_ProcessingProperty", mark.Path));

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
            await ReportProgress(onProgressChange, onProcessChange, 20,
                BTaskText.Localize(_localizer, "SyncPathMark_CollectingMediaLibraryEffects"));
            for (var i = 0; i < activeMediaLibraryMarks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var mark = activeMediaLibraryMarks[i];
                var progress = 20 + (int)(10.0 * (i + 1) / Math.Max(1, activeMediaLibraryMarks.Count));
                await ReportProgress(onProgressChange, onProcessChange, progress,
                    BTaskText.Localize(_localizer, "SyncPathMark_ProcessingMediaLibrary", mark.Path));

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
            await ReportProgress(onProgressChange, onProcessChange, 30,
                BTaskText.Localize(_localizer, "SyncPathMark_ComputingFinalState"));
            await ComputeFinalPropertyState(ctx);
            await ComputeFinalMediaLibraryState(ctx);

            // ===== Phase 3: Apply Changes (50-80%) =====
            await ReportProgress(onProgressChange, onProcessChange, 50,
                BTaskText.Localize(_localizer, "SyncPathMark_ApplyingPropertyChanges"));
            var propertyResult = await ApplyPropertyChanges(ctx, ct);
            result.PropertiesApplied = propertyResult.Applied;
            result.PropertiesDeleted = propertyResult.Deleted;

            await ReportProgress(onProgressChange, onProcessChange, 65,
                BTaskText.Localize(_localizer, "SyncPathMark_ApplyingMediaLibraryChanges"));
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

            // ===== Phase 4: Persist Effects (80-90%) =====
            await ReportProgress(onProgressChange, onProcessChange, 80,
                BTaskText.Localize(_localizer, "SyncPathMark_PersistingEffects"));
            await ComputeEffectDiff(ctx);
            await PersistEffects(ctx, onProgressChange, ct);

            // ===== Phase 5: Wait for Search Index (90-95%) =====
            // Property and mapping writes enqueue background index reads through a separate
            // DbContext. Drain those reads before writing mark statuses so SQLite does not see
            // a reader/writer race, and only report the marks as synced once search is current.
            await ReportProgress(onProgressChange, onProcessChange, 90,
                BTaskText.Localize(_localizer, "SyncPathMark_UpdatingSearchIndex"));
            var indexBarrierSw = Stopwatch.StartNew();
            try
            {
                await _resourceSearchIndexService.WaitForPendingUpdatesAsync(ct);
            }
            finally
            {
                indexBarrierSw.Stop();
                _logger.LogInformation("[Sync] Resource search index barrier took {ElapsedMs}ms",
                    indexBarrierSw.ElapsedMilliseconds);
            }

            // ===== Cleanup (95-100%) =====
            await ReportProgress(onProgressChange, onProcessChange, 95,
                BTaskText.Localize(_localizer, "SyncPathMark_UpdatingMarkStatuses"));
            await BatchUpdateMarkStatuses(ctx, marksToDelete);

            await ReportProgress(onProgressChange, onProcessChange, 100, BTaskText.Localize(_localizer, "SyncPathMark_Complete"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return result;
    }

    private async Task LoadOldEffects(
        List<int> markIds,
        Dictionary<int, List<PropertyMarkEffect>> target)
    {
        var oldPropertyEffects = await _effectService.GetPropertyEffectsByMarkIds(markIds);
        foreach (var effect in oldPropertyEffects)
        {
            if (!target.TryGetValue(effect.MarkId, out var list))
            {
                list = new List<PropertyMarkEffect>();
                target[effect.MarkId] = list;
            }

            list.Add(effect);
        }
    }

    private const int BatchSize = 500;

    #region Phase 1: Collect Effects

    /// <summary>
    /// Collects property effects from a mark WITHOUT setting any property values.
    /// </summary>
    private async Task CollectPropertyEffects(PathMark mark, SyncContext ctx, CancellationToken ct)
    {
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
        if (config == null) return;

        // Only support Custom properties for now
        if (config.Pool != PropertyPool.Custom) return;

        if (config.ValueType == PropertyValueType.Dynamic &&
            ((config.ValueRegexMatchesResourcePath && string.IsNullOrEmpty(config.ValueRegex)) ||
             (string.IsNullOrEmpty(config.ValueRegex) && !config.ValueLayer.HasValue)))
        {
            // An incomplete regex extractor must never silently turn into layer 0. Besides being
            // surprising for modern marks, that fallback could overwrite an entire migrated tree.
            return;
        }

        // V220 regex locators operated on each resource-relative path and converted all
        // capture groups to the target property's business value type.
        CustomProperty? valueProperty = null;
        if (config.UsesResourceRelativeValueRegex())
        {
            valueProperty = await _customPropertyService.GetByKey(config.PropertyId);
        }

        // Use cached resources - match ALL resources under this mark's path
        var matchedResources = ctx.AllResources
            .Where(r => ctx.IsPathUnderParent(r.Path, mark.Path))
            .ToList();

        if (matchedResources.Count == 0) return;

        var filteredResources = FilterResourcesByMarkConfig(matchedResources, mark.Path, config, ctx);
        if (filteredResources.Count == 0) return;

        var valueType = config.ValueType;
        var needsPerResourceExtraction = valueType == PropertyValueType.Dynamic &&
                                         PropertyDynamicValueDependsOnResource(config);

        // For fixed values or dynamic values with valueLayer <= 0, extract once
        object? sharedValue = null;
        if (valueType == PropertyValueType.Fixed)
        {
            sharedValue = config.FixedValue;
            if (sharedValue == null) return;
        }
        else if (!needsPerResourceExtraction)
        {
            sharedValue = ExtractPropertyDynamicValue(mark.Path, null, config, valueProperty, ctx);
            if (sharedValue == null) return;
        }

        foreach (var resource in filteredResources)
        {
            ct.ThrowIfCancellationRequested();

            object? value;
            if (needsPerResourceExtraction)
            {
                value = config.UsesResourceRelativeValueRegex()
                    ? await ExtractResourceRelativeRegexValueAsync(
                        mark.Path, resource.Path, config.ValueRegex!, valueProperty!, ctx)
                    : ExtractPropertyDynamicValue(mark.Path, resource.Path, config, valueProperty, ctx);
                if (value == null) continue;
            }
            else
            {
                value = sharedValue!;
            }

            var valueString = SerializePropertyMarkEffectValue(value);

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
    ///
    /// Effective effects = freshly collected effects of run marks (succeeded ones) + old
    /// effects of failed run marks (preserve previous contribution) + old effects of context
    /// marks (their contribution is assumed unchanged because they weren't re-collected).
    ///
    /// Deletion only fires when a run mark previously contributed to a (resource, property)
    /// key but the merged effective effects no longer produce any value for it.
    /// </summary>
    private async Task ComputeFinalPropertyState(SyncContext ctx)
    {
        var successfulRunMarkIds = ctx.SuccessfulMarkIds.ToHashSet();

        // Build the "effective" effect list: what every active mark currently contributes.
        var effectiveEffects = new List<PropertyMarkEffect>(ctx.CollectedPropertyEffects);

        // Failed run marks: preserve their previous contributions.
        foreach (var (markId, oldEffects) in ctx.RunOldEffectsByMarkId)
        {
            if (successfulRunMarkIds.Contains(markId)) continue;
            effectiveEffects.AddRange(oldEffects);
        }

        // Context marks: carry over their last-known contributions.
        foreach (var oldEffects in ctx.ContextOldEffectsByMarkId.Values)
        {
            effectiveEffects.AddRange(oldEffects);
        }

        // Pre-load custom property definitions for type lookup
        var customPropertyIds = effectiveEffects
            .Where(e => e.PropertyPool == PropertyPool.Custom)
            .Select(e => e.PropertyId)
            .Distinct()
            .ToList();

        var customProperties = customPropertyIds.Count > 0
            ? (await _customPropertyService.GetByKeys(customPropertyIds)).ToDictionary(p => p.Id)
            : new Dictionary<int, CustomProperty>();

        // Property-mark effects have one canonical contract: Value is serialized with
        // the property's BizValueType. V220 seeded effects by copying CustomPropertyValue.Value,
        // so those rows contain serialized DB ids instead. Normalize persisted reference
        // effects lazily before combining them. Only deterministic re-extraction from
        // the mark/config/resource can prove what the business value was; if that is
        // unavailable, preserve the existing property value instead of guessing.
        NormalizePersistedReferenceEffects(effectiveEffects, customProperties, ctx);

        // Group effects by (ResourceId, PropertyPool, PropertyId) after normalization;
        // unresolved legacy references are excluded rather than auto-created as labels.
        var groupedEffects = effectiveEffects
            .GroupBy(e => (e.ResourceId, e.PropertyPool, e.PropertyId))
            .ToList();

        // Pre-load existing property values for comparison. Include resources that previously
        // had effects from run marks too so we can correctly upsert / leave-alone.
        var resourceIds = groupedEffects.Select(g => g.Key.ResourceId)
            .Concat(ctx.RunOldEffectsByMarkId.Values.SelectMany(v => v).Select(e => e.ResourceId))
            .Distinct()
            .ToList();
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

            // An unresolved legacy id means this group's current combined DB value is
            // the only lossless representation left. Do not overwrite it from an
            // incomplete subset of effects; a full re-sync of that mark will replace
            // the ambiguous row with a freshly extracted business value.
            if (ctx.PropertyValueKeysToPreserve.Contains(group.Key)) continue;

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

            // A mark always yields human-readable text (a directory name, a regex capture),
            // never an option id. For reference types (choice / tags / multilevel) that text
            // is a biz value: combine it as one, then convert it into the db value so the
            // option is created on the property.
            string? combinedValue;
            if (pool == PropertyPool.Custom &&
                PropertySystem.Property.IsReferenceValueType(propertyType.Value) &&
                customProperties.TryGetValue(propertyId, out var referenceProperty))
            {
                combinedValue = ToReferenceDbValue(referenceProperty, effects.Select(e => e.Value), ctx);
            }
            else
            {
                combinedValue = PropertySystem.Property.CombineSerializedDbValues(
                    propertyType.Value,
                    effects.Select(e => e.Value));
            }

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

        // Delete property values only when a run mark previously contributed to a key AND
        // no effective effect produces it anymore. Context marks alone keep the value alive.
        foreach (var (markId, oldEffects) in ctx.RunOldEffectsByMarkId)
        {
            foreach (var effect in oldEffects)
            {
                if (effect.PropertyPool == PropertyPool.Internal) continue;
                var key = (effect.ResourceId, effect.PropertyPool, effect.PropertyId);
                if (!ctx.FinalPropertyValues.ContainsKey(key) &&
                    !ctx.PropertyValueKeysToPreserve.Contains(key))
                {
                    ctx.PropertyValuesToDelete.Add((effect.ResourceId, effect.PropertyId));
                }
            }
        }
    }

    /// <summary>
    /// Combines the texts a set of marks contributed to a reference-typed property
    /// (choice / tags / multilevel) and converts them into that property's db value,
    /// creating an option for every label that doesn't have one yet. Properties whose
    /// options grew are recorded on the context so Phase 3 persists them ahead of the
    /// values pointing at them.
    /// </summary>
    private static string? ToReferenceDbValue(CustomProperty customProperty, IEnumerable<string?> texts,
        SyncContext ctx)
    {
        var bizValueType = PropertySystem.Property.GetBizValueType(customProperty.Type);
        var bizValues = texts
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!.DeserializeAsStandardValue(bizValueType))
            .ToList();
        if (bizValues.Count == 0) return null;

        // customProperty is the single instance loaded for this sync, so an option added
        // for one resource is already on it when the next resource carrying the same
        // label comes through — the label maps to one option id, not one per resource.
        var property = customProperty.ToProperty();
        var (dbValue, propertyChanged) =
            PropertySystem.Property.ToDbValue(property, StandardValueSystem.Combine(bizValues, bizValueType));

        if (propertyChanged)
        {
            customProperty.Options = property.Options;
            ctx.ChangedCustomProperties[customProperty.Id] = customProperty;
        }

        return dbValue?.SerializeAsStandardValue(PropertySystem.Property.GetDbValueType(customProperty.Type));
    }

    /// <summary>
    /// Converts V220's persisted DB-valued reference effects to the canonical serialized
    /// business value. Only persisted effects are considered; freshly collected effects
    /// already contain business values.
    ///
    /// The current mark configuration and resource path are the strongest source of truth,
    /// so they are used first to re-extract the business value. This makes the repair
    /// deterministic even for a legitimate UUID label or an old id whose option was deleted.
    /// If re-extraction is impossible, the representation is ambiguous: reference DB and
    /// business values can have the same lexical shape, so even a DB -> Biz -> DB round-trip
    /// is not independent proof. Such an effect is omitted and its combined property value
    /// is preserved until the mark can be fully re-collected.
    /// </summary>
    private void NormalizePersistedReferenceEffects(
        List<PropertyMarkEffect> effectiveEffects,
        IReadOnlyDictionary<int, CustomProperty> customProperties,
        SyncContext ctx)
    {
        var unresolvedEffectIds = new HashSet<int>();
        var unresolvedCounts = new Dictionary<(int MarkId, int PropertyId), int>();

        foreach (var effect in effectiveEffects.Where(e => e.Id > 0 && e.PropertyPool == PropertyPool.Custom))
        {
            if (!customProperties.TryGetValue(effect.PropertyId, out var customProperty) ||
                !PropertySystem.Property.IsReferenceValueType(customProperty.Type) ||
                string.IsNullOrEmpty(effect.Value))
            {
                continue;
            }

            // This is the same applicability/extraction path used by CollectPropertyEffects.
            // If it succeeds there is no representation guess: persist exactly what a fresh
            // collection would have produced.
            if (TryExtractCurrentBusinessValue(effect, customProperty, ctx, out var extractedBizValue))
            {
                if (effect.Value != extractedBizValue)
                {
                    effect.Value = extractedBizValue;
                    ctx.PropertyEffectsToUpdate[effect.Id] = effect;
                }

                continue;
            }

            // Never feed an unverified persisted reference value into AutoCreateOptions:
            // it may be a dangling legacy DB id, while a value that happens to match an
            // option id may instead be a perfectly legitimate business label.
            unresolvedEffectIds.Add(effect.Id);
            ctx.PropertyValueKeysToPreserve.Add(
                (effect.ResourceId, effect.PropertyPool, effect.PropertyId));
            var warningKey = (effect.MarkId, effect.PropertyId);
            unresolvedCounts[warningKey] = unresolvedCounts.GetValueOrDefault(warningKey) + 1;
        }

        foreach (var ((markId, propertyId), count) in unresolvedCounts)
        {
            _logger.LogWarning(
                "[Sync] Skipped {Count} unresolved legacy reference effects from mark {MarkId} for custom " +
                "property {PropertyId}; a full mark re-sync is required to recover their business labels",
                count, markId, propertyId);
        }

        if (unresolvedEffectIds.Count > 0)
        {
            effectiveEffects.RemoveAll(e => unresolvedEffectIds.Contains(e.Id));
        }
    }

    /// <summary>
    /// Rebuilds the effect through the same resource applicability and value extraction
    /// rules as <see cref="CollectPropertyEffects"/>. Returning false means the current
    /// mark/resource cannot prove what the persisted representation is.
    /// </summary>
    private bool TryExtractCurrentBusinessValue(
        PropertyMarkEffect effect,
        CustomProperty customProperty,
        SyncContext ctx,
        out string businessValue)
    {
        businessValue = null!;

        if (!ctx.PathMarksById.TryGetValue(effect.MarkId, out var mark) ||
            mark.Type != PathMarkType.Property ||
            !ctx.IdToResource.TryGetValue(effect.ResourceId, out var resource) ||
            string.IsNullOrEmpty(resource.Path))
        {
            return false;
        }

        if (!ctx.PropertyMarkConfigsByMarkId.TryGetValue(mark.Id, out var config) ||
            config == null || config.Pool != effect.PropertyPool || config.PropertyId != effect.PropertyId)
        {
            return false;
        }

        try
        {
            if (!ctx.IsPathUnderParent(resource.Path, mark.Path) ||
                !FilterResourcesByMarkConfig([resource], mark.Path, config, ctx)
                    .Any(r => r.Id == resource.Id))
            {
                return false;
            }

            object? extractedValue;
            if (config.ValueType == PropertyValueType.Fixed)
            {
                extractedValue = config.FixedValue;
            }
            else
            {
                var resourcePath = PropertyDynamicValueDependsOnResource(config)
                    ? resource.Path
                    : null;
                extractedValue = ExtractPropertyDynamicValue(mark.Path, resourcePath, config, customProperty, ctx);
            }

            if (extractedValue == null)
            {
                return false;
            }

            businessValue = SerializePropertyMarkEffectValue(extractedValue);
            return true;
        }
        catch (Exception ex)
        {
            // Historical context must never turn a malformed/stale mark config into
            // a failure of an otherwise unrelated partial sync.
            _logger.LogDebug(ex,
                "[Sync] Could not re-extract business value for legacy effect {EffectId}", effect.Id);
            return false;
        }
    }

    private static string SerializePropertyMarkEffectValue(object value) => value is string s
        ? s
        : System.Text.Json.JsonSerializer.Serialize(value, System.Text.Json.JsonSerializerOptions.Web);

    /// <summary>
    /// Computes final media library mappings from effective effects.
    ///
    /// Effective effects = freshly collected effects of run marks (succeeded ones) +
    /// old effects of failed run marks (preserve previous contribution) + old effects
    /// of context marks (their contribution is assumed unchanged).
    ///
    /// Deletion only fires when a run mark previously contributed a (resource, library)
    /// mapping but the merged effective effects no longer produce it.
    /// </summary>
    private Task ComputeFinalMediaLibraryState(SyncContext ctx)
    {
        const int mediaLibraryPropertyId = 25;

        var successfulRunMarkIds = ctx.SuccessfulMarkIds.ToHashSet();

        bool IsMediaLibraryEffect(PropertyMarkEffect e) =>
            e.PropertyPool == PropertyPool.Internal && e.PropertyId == mediaLibraryPropertyId;

        // Build effective effects (run-collected + failed run carry-over + context carry-over),
        // restricted to media library effects.
        var effectiveMediaLibraryEffects = new List<PropertyMarkEffect>();
        effectiveMediaLibraryEffects.AddRange(
            ctx.CollectedPropertyEffects.Where(IsMediaLibraryEffect));

        foreach (var (markId, oldEffects) in ctx.RunOldEffectsByMarkId)
        {
            if (successfulRunMarkIds.Contains(markId)) continue;
            effectiveMediaLibraryEffects.AddRange(oldEffects.Where(IsMediaLibraryEffect));
        }

        foreach (var oldEffects in ctx.ContextOldEffectsByMarkId.Values)
        {
            effectiveMediaLibraryEffects.AddRange(oldEffects.Where(IsMediaLibraryEffect));
        }

        // Group by ResourceId and aggregate all media library IDs.
        var groupedEffects = effectiveMediaLibraryEffects
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

                foreach (var mlId in mediaLibraryIds)
                {
                    ctx.FinalMappingsToEnsure.Add((resourceId, mlId));
                }
            }
        }

        // Delete a mapping only when a run mark previously contributed it AND the merged
        // effective state no longer produces it. Context marks alone keep it alive.
        var currentMappings = new HashSet<(int ResourceId, int MediaLibraryId)>(ctx.FinalMappingsToEnsure);

        foreach (var (markId, oldEffects) in ctx.RunOldEffectsByMarkId)
        {
            foreach (var effect in oldEffects)
            {
                if (!IsMediaLibraryEffect(effect)) continue;
                if (!int.TryParse(effect.Value, out var mediaLibraryId)) continue;

                var mapping = (effect.ResourceId, mediaLibraryId);
                if (!currentMappings.Contains(mapping))
                {
                    ctx.MediaLibraryMappingsToDelete.Add(mapping);
                }
            }
        }

        return Task.CompletedTask;
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

        // Options first: a value referencing an option that isn't on the property yet
        // reads back as empty.
        if (ctx.ChangedCustomProperties.Count > 0)
        {
            await _customPropertyService.UpdateRange(
                ctx.ChangedCustomProperties.Values.Select(p => p.ToDbModel()).ToList());
            _logger.LogInformation("[Sync] Updated options of {Count} custom properties",
                ctx.ChangedCustomProperties.Count);
        }

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
            var deletedResourceIds = new HashSet<int>();
            foreach (var (resourceId, propertyId) in distinctDeletes)
            {
                ct.ThrowIfCancellationRequested();
                await _customPropertyValueService.RemoveAll(x =>
                    x.ResourceId == resourceId &&
                    x.PropertyId == propertyId &&
                    x.Scope == (int)PropertyValueScope.Synchronization);
                // Reindex even when the delete was already idempotently satisfied: an
                // older failed update may still have left this value in the index.
                deletedResourceIds.Add(resourceId);
            }

            if (deletedResourceIds.Count > 0)
            {
                _resourceSearchIndexService.InvalidateResources(deletedResourceIds);
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
    /// Computes which effects to add, update and delete by comparing collected vs old.
    /// </summary>
    private Task ComputeEffectDiff(SyncContext ctx)
    {
        // Collected effects either insert a new key or refresh the existing row's value
        // and priority. Reusing the old row preserves its id/CreatedAt and avoids the
        // unique-key race of delete-then-add. Context rows are touched only when Phase 2
        // already queued a legacy representation normalization.
        var runOldByKey = ctx.RunOldEffectsByMarkId
            .SelectMany(kvp => kvp.Value)
            .ToDictionary(
                e => (e.MarkId, e.PropertyPool, e.PropertyId, e.ResourceId),
                e => e);

        var addedPropertyKeys =
            new HashSet<(int MarkId, PropertyPool Pool, int PropertyId, int ResourceId)>();

        foreach (var effect in ctx.CollectedPropertyEffects)
        {
            var key = (effect.MarkId, effect.PropertyPool, effect.PropertyId, effect.ResourceId);
            if (!addedPropertyKeys.Add(key))
            {
                continue;
            }

            if (runOldByKey.TryGetValue(key, out var oldEffect))
            {
                if (oldEffect.Value != effect.Value || oldEffect.Priority != effect.Priority)
                {
                    oldEffect.Value = effect.Value;
                    oldEffect.Priority = effect.Priority;
                    ctx.PropertyEffectsToUpdate[oldEffect.Id] = oldEffect;
                }
            }
            else
            {
                ctx.PropertyEffectsToAdd.Add(effect);
            }
        }

        // Effect records to delete: only for run marks that successfully re-collected this
        // run. Failed run marks keep their old records (they didn't produce new ones).
        // Context marks keep theirs (they weren't synced).
        var successfulRunMarkIds = ctx.SuccessfulMarkIds.ToHashSet();
        foreach (var (markId, oldEffects) in ctx.RunOldEffectsByMarkId)
        {
            if (!successfulRunMarkIds.Contains(markId)) continue;
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
            "[Sync] Effect diff: PropertyEffects +{AddProp}/~{UpdateProp}/-{DelProp}",
            ctx.PropertyEffectsToAdd.Count, ctx.PropertyEffectsToUpdate.Count,
            ctx.PropertyEffectIdsToDelete.Count);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists effect changes to database.
    /// </summary>
    private async Task PersistEffects(
        SyncContext ctx,
        Func<int, Task>? onProgressChange,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var addBatches = ctx.PropertyEffectsToAdd.Chunk(BatchSize).ToList();
        var updateBatches = ctx.PropertyEffectsToUpdate.Values.Chunk(BatchSize).ToList();
        var operationCount = addBatches.Count + updateBatches.Count +
                             (ctx.PropertyEffectIdsToDelete.Count > 0 ? 1 : 0);
        var completedOperations = 0;

        async Task ReportOperationCompleted()
        {
            completedOperations++;
            if (onProgressChange != null)
            {
                await onProgressChange(80 + (int)(10.0 * completedOperations / operationCount));
            }
        }

        // Add new property effects
        if (addBatches.Count > 0)
        {
            for (var i = 0; i < addBatches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _effectService.AddPropertyEffects(addBatches[i]);
                await ReportOperationCompleted();
                if (i < addBatches.Count - 1) await Task.Delay(5, ct);
            }
        }

        // Update same-key effects and lazily normalized V220 effects.
        if (updateBatches.Count > 0)
        {
            for (var i = 0; i < updateBatches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _effectService.UpdatePropertyEffects(updateBatches[i]);
                await ReportOperationCompleted();
                if (i < updateBatches.Count - 1) await Task.Delay(5, ct);
            }
        }

        // Delete stale property effects
        if (ctx.PropertyEffectIdsToDelete.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            await _effectService.DeletePropertyEffects(ctx.PropertyEffectIdsToDelete);
            await ReportOperationCompleted();
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

    private async Task ReportProgress(Func<int, Task>? onProgressChange, Func<BTaskText?, Task>? onProcessChange,
        int progress, BTaskText? process)
    {
        if (onProgressChange != null) await onProgressChange(progress);
        if (onProcessChange != null) await onProcessChange(process);
    }

    /// <summary>
    /// Positive layer extraction and legacy resource-relative regex extraction need the concrete
    /// resource path; mark-relative layers and modern regex extraction are shared by every resource.
    /// </summary>
    private static bool PropertyDynamicValueDependsOnResource(PropertyMarkConfig config) =>
        config.UsesResourceRelativeValueRegex() ||
        (string.IsNullOrEmpty(config.ValueRegex) && config.ValueLayer is > 0);

    /// <summary>
    /// Property value extraction has its own selector. It must not inherit the applicability
    /// MatchMode: a property may cover resources by layer while extracting a regex capture,
    /// which is also the explicit shape emitted by the corrected V220 converter.
    /// </summary>
    private object? ExtractPropertyDynamicValue(
        string markPath,
        string? resourcePath,
        PropertyMarkConfig config,
        CustomProperty? customProperty,
        SyncContext ctx) =>
        config.UsesResourceRelativeValueRegex()
            ? string.IsNullOrEmpty(config.ValueRegex) || customProperty == null
                ? null
                : ExtractResourceRelativeRegexValue(markPath, resourcePath, config.ValueRegex, customProperty, ctx)
            : !string.IsNullOrEmpty(config.ValueRegex)
                ? ExtractMarkDirectoryRegexValue(markPath, config.ValueRegex, ctx)
                : config.ValueLayer.HasValue
                    ? ExtractDynamicValue(markPath, resourcePath, PathMatchMode.Layer, config.ValueLayer, null, ctx)
                    : null;

    private static object? ExtractResourceRelativeRegexValue(
        string markPath,
        string? resourcePath,
        string valueRegex,
        CustomProperty customProperty,
        SyncContext ctx)
    {
        var values = ExtractResourceRelativeRegexCaptures(markPath, resourcePath, valueRegex, ctx);

        if (values.Count == 0) return null;

        var bizValueType = customProperty.Type.GetBizValueType();
        return StandardValueSystem.Convert(values, StandardValueType.ListString, bizValueType)
            ?.SerializeAsStandardValue(bizValueType);
    }

    private async Task<object?> ExtractResourceRelativeRegexValueAsync(
        string markPath,
        string? resourcePath,
        string valueRegex,
        CustomProperty customProperty,
        SyncContext ctx)
    {
        var values = ExtractResourceRelativeRegexCaptures(markPath, resourcePath, valueRegex, ctx);
        if (values.Count == 0) return null;

        var bizValueType = customProperty.Type.GetBizValueType();
        return (await _standardValueService.Convert(values, StandardValueType.ListString, bizValueType))
            ?.SerializeAsStandardValue(bizValueType);
    }

    private static List<string> ExtractResourceRelativeRegexCaptures(
        string markPath,
        string? resourcePath,
        string valueRegex,
        SyncContext ctx)
    {
        if (string.IsNullOrEmpty(resourcePath)) return [];

        var normalizedMarkPath = ctx.GetStandardizedPath(markPath);
        var normalizedResourcePath = ctx.GetStandardizedPath(resourcePath);
        var relativePath = normalizedResourcePath.Length <= normalizedMarkPath.Length
            ? string.Empty
            : normalizedResourcePath[normalizedMarkPath.Length..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Regex.Matches(relativePath, valueRegex)
            .SelectMany(match => match.Groups.Values.Skip(1).Select(group => group.Value))
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct()
            .ToList();
    }

    private static string? ExtractMarkDirectoryRegexValue(string markPath, string valueRegex, SyncContext ctx)
    {
        var markDirectoryName = Path.GetFileName(ctx.GetStandardizedPath(markPath));
        var match = ctx.GetOrCreateRegex(valueRegex).Match(markDirectoryName);
        return match.Success ? match.Groups.Count > 1 ? match.Groups[1].Value : match.Value : null;
    }

    /// <summary>
    /// Low-level extractor retained for media-library marks, whose existing behavior selects
    /// the extraction strategy through their applicability MatchMode.
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
            (matchMode, layer, regex, applyScope) = pmc.GetEffectiveApplicability();
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

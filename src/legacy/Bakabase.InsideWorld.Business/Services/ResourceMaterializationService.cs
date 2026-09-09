using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Workflow;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

/// <inheritdoc />
public class ResourceMaterializationService : IResourceMaterializationService
{
    private readonly IResourceService _resourceService;
    private readonly ResourceStructureService _structureService;
    private readonly IPathMarkSyncService _pathMarkSyncService;
    private readonly IResourceDataChangeEventPublisher _eventPublisher;
    private readonly IResourceSourceLinkService _sourceLinkService;
    private readonly ICustomPropertyValueService _customPropertyValueService;
    private readonly IReservedPropertyValueService _reservedPropertyValueService;
    private readonly IEnhancerService _enhancerService;
    private readonly IWorkflowEventBus _workflowEventBus;
    private readonly ILogger<ResourceMaterializationService> _logger;

    public ResourceMaterializationService(
        IResourceService resourceService,
        ResourceStructureService structureService,
        IPathMarkSyncService pathMarkSyncService,
        IResourceDataChangeEventPublisher eventPublisher,
        IResourceSourceLinkService sourceLinkService,
        ICustomPropertyValueService customPropertyValueService,
        IReservedPropertyValueService reservedPropertyValueService,
        IEnhancerService enhancerService,
        IWorkflowEventBus workflowEventBus,
        ILogger<ResourceMaterializationService> logger)
    {
        _resourceService = resourceService;
        _structureService = structureService;
        _pathMarkSyncService = pathMarkSyncService;
        _eventPublisher = eventPublisher;
        _sourceLinkService = sourceLinkService;
        _customPropertyValueService = customPropertyValueService;
        _reservedPropertyValueService = reservedPropertyValueService;
        _enhancerService = enhancerService;
        _workflowEventBus = workflowEventBus;
        _logger = logger;
    }

    public async Task<MaterializationResult> MaterializeAsync(int resourceId, string path,
        MaterializationOptions? options = null, CancellationToken ct = default)
    {
        options ??= MaterializationOptions.Default;

        var standardizedPath = path.StandardizePath();
        if (string.IsNullOrEmpty(standardizedPath))
        {
            throw new ArgumentException("A materialization path cannot be empty.", nameof(path));
        }

        var isFile = File.Exists(standardizedPath);
        var isDirectory = Directory.Exists(standardizedPath);
        if (!isFile && !isDirectory)
        {
            throw new ArgumentException($"Path does not exist: {standardizedPath}", nameof(path));
        }

        var resource = await _resourceService.Get(resourceId);
        if (resource == null)
        {
            throw new InvalidOperationException($"Resource {resourceId} does not exist.");
        }

        // The path may already belong to another resource — most often a path-mark sync found the
        // folder before the user connected it to the resource they had been tracking. Absorbing
        // that resource has to happen before the path is written, because two resources sharing a
        // path is exactly the state everything downstream assumes cannot happen.
        var mergedResourceId = await AbsorbOccupantIfAny(resourceId, standardizedPath, options, ct);

        DateTime fileCreatedAt, fileModifiedAt;
        if (isFile)
        {
            var fi = new FileInfo(standardizedPath);
            fileCreatedAt = fi.CreationTime.TruncateToMilliseconds();
            fileModifiedAt = fi.LastWriteTime.TruncateToMilliseconds();
        }
        else
        {
            var di = new DirectoryInfo(standardizedPath);
            fileCreatedAt = di.CreationTime.TruncateToMilliseconds();
            fileModifiedAt = di.LastWriteTime.TruncateToMilliseconds();
        }

        resource.Path = standardizedPath;
        resource.IsFile = isFile;
        resource.FileCreatedAt = fileCreatedAt;
        resource.FileModifiedAt = fileModifiedAt;
        resource.UpdatedAt = DateTime.Now;
        // Status is deliberately untouched: it says whether the external source still lists the
        // resource, which is a different question from whether we hold its files.
        await _resourceService.AddOrPutRange([resource]);

        await ApplyPathChangeSideEffects(resourceId, [standardizedPath], options.EnqueuePathMarkSync, ct);

        // An enhancer that ran while the resource had no files stored an empty result and is
        // never retried; clearing those records is what lets it run again now that there is
        // something to read.
        await _enhancerService.ClearEmptyEnhancementRecords([resourceId], ct);

        _logger.LogInformation(
            "[Materialization] Resource {ResourceId} materialized at {Path}{Merged}",
            resourceId, standardizedPath,
            mergedResourceId.HasValue ? $" (absorbed resource {mergedResourceId})" : null);

        // Published last, once everything the chain might read is settled. Workflows are additive:
        // a definition that does not care about this resource simply does not run.
        await PublishMaterializedEvent(resourceId, standardizedPath, ct);

        return new MaterializationResult(resourceId, standardizedPath, mergedResourceId);
    }

    public async Task DematerializeAsync(int resourceId, CancellationToken ct = default)
    {
        var resource = await _resourceService.Get(resourceId);
        if (resource == null)
        {
            throw new InvalidOperationException($"Resource {resourceId} does not exist.");
        }

        if (!resource.HasLocalPath)
        {
            return;
        }

        var oldPath = resource.Path!;

        resource.Path = null;
        // Nothing can be the child of a resource with no path, and the rebuild below only walks
        // resources that have one, so this resource's own link has to be cut here.
        resource.ParentId = null;
        resource.UpdatedAt = DateTime.Now;
        await _resourceService.AddOrPutRange([resource]);

        // Both the old and the (now absent) new location matter: marks covering the old path
        // describe a resource that is no longer there.
        await ApplyPathChangeSideEffects(resourceId, [oldPath], false, ct);

        _logger.LogInformation("[Materialization] Resource {ResourceId} dematerialized (was at {Path})",
            resourceId, oldPath);
    }

    private async Task PublishMaterializedEvent(int resourceId, string path, CancellationToken ct)
    {
        try
        {
            var links = await _sourceLinkService.GetByResourceIds([resourceId]);
            var reserved = await _reservedPropertyValueService.GetAll(v => v.ResourceId == resourceId);

            await _workflowEventBus.PublishAsync(ResourceWorkflowKinds.TriggerMaterialized,
                new ResourceMaterializedPayload
                {
                    ResourceId = resourceId,
                    Path = path,
                    Name = reserved.Select(v => v.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ??
                           System.IO.Path.GetFileName(path),
                    SourceLinks = links
                        .Select(l => new ResourceWorkflowSourceLink(l.Source, l.SourceKey))
                        .ToList()
                }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The resource is materialized either way; a workflow failing to start is not a reason
            // to undo that.
            _logger.LogError(ex, "[Materialization] Could not publish the materialized event for {ResourceId}",
                resourceId);
        }
    }

    /// <summary>
    /// Everything that has to happen after a resource's path is written, in the order the ordering
    /// actually matters in: caches are invalidated only once the new path is persisted, so a
    /// concurrent read cannot re-cache against the old one.
    /// </summary>
    private async Task ApplyPathChangeSideEffects(int resourceId, IReadOnlyCollection<string> affectedPaths,
        bool enqueuePathMarkSync, CancellationToken ct)
    {
        // One call per flag: the per-type switch behind this API has no case for a combined value
        // and would throw.
        await _resourceService.DeleteResourceCacheByResourceIdsAndCacheType([resourceId], ResourceCacheType.Covers);
        await _resourceService.DeleteResourceCacheByResourceIdsAndCacheType([resourceId],
            ResourceCacheType.PlayableFiles);

        await _structureService.RebuildParentChildRelationships(ct);
        await _structureService.MarkPathMarksAsPendingForPaths(affectedPaths, ct);
        if (enqueuePathMarkSync)
        {
            await _pathMarkSyncService.EnqueueSync();
        }

        // The write above published already, but the parent-child rebuild ran after it and only
        // publishes the resources whose parent actually moved. This is the one publish guaranteed
        // to describe the final state.
        _eventPublisher.PublishResourcesChanged([resourceId]);
    }

    /// <summary>
    /// Merges away whatever resource currently owns <paramref name="standardizedPath"/>. Returns the
    /// id of the absorbed resource, or null when the path was free.
    /// </summary>
    private async Task<int?> AbsorbOccupantIfAny(int resourceId, string standardizedPath,
        MaterializationOptions options, CancellationToken ct)
    {
        var occupant = (await _resourceService.GetAllDbModels(r => r.Id != resourceId))
            .FirstOrDefault(r => !string.IsNullOrEmpty(r.Path) &&
                                 string.Equals(r.Path, standardizedPath, StringComparison.OrdinalIgnoreCase));
        if (occupant == null)
        {
            return null;
        }

        if (!options.MergeIfPathOwnedByAnotherResource)
        {
            throw new InvalidOperationException(
                $"Path '{standardizedPath}' already belongs to resource {occupant.Id}.");
        }

        // MergeResources consolidates source links and media library mappings and then deletes the
        // absorbed resource — property values are explicitly the caller's job, and deletion takes
        // them with it, so they have to move first.
        await TransferPropertyValues(occupant.Id, resourceId);
        await _resourceService.MergeResources(new ResourceMergeInputModel
        {
            TargetResourceId = resourceId,
            SourceResourceIds = [occupant.Id]
        });

        return occupant.Id;
    }

    /// <summary>
    /// Moves property values from a resource about to be deleted onto the surviving one, without
    /// overwriting anything the survivor already says. Values are re-pointed rather than re-created
    /// so nothing is re-serialized on the way.
    /// </summary>
    private async Task TransferPropertyValues(int fromResourceId, int toResourceId)
    {
        var customValues = await _customPropertyValueService.GetAllDbModels(
            v => v.ResourceId == fromResourceId || v.ResourceId == toResourceId);
        var occupiedCustomKeys = customValues
            .Where(v => v.ResourceId == toResourceId)
            .Select(v => (v.PropertyId, v.Scope))
            .ToHashSet();
        var customToMove = customValues
            .Where(v => v.ResourceId == fromResourceId && !occupiedCustomKeys.Contains((v.PropertyId, v.Scope)))
            .ToList();

        if (customToMove.Count > 0)
        {
            // Re-inserted rather than updated in place: the update path skips any row whose Value
            // is unchanged, which is exactly the shape of a move.
            await _customPropertyValueService.RemoveRange(customToMove);
            await _customPropertyValueService.AddDbModelRange(customToMove.Select(v =>
                new CustomPropertyValueDbModel
                {
                    ResourceId = toResourceId,
                    PropertyId = v.PropertyId,
                    Scope = v.Scope,
                    Value = v.Value
                }));
        }

        var reservedValues = await _reservedPropertyValueService.GetAll(
            v => v.ResourceId == fromResourceId || v.ResourceId == toResourceId);
        var targetByScope = reservedValues
            .Where(v => v.ResourceId == toResourceId)
            .ToDictionary(v => v.Scope);

        var reservedToUpdate = new List<ReservedPropertyValue>();
        foreach (var source in reservedValues.Where(v => v.ResourceId == fromResourceId))
        {
            if (!targetByScope.TryGetValue(source.Scope, out var target))
            {
                // The survivor says nothing in this scope — hand the whole row over.
                source.ResourceId = toResourceId;
                reservedToUpdate.Add(source);
                continue;
            }

            // Both hold this scope: fill only the fields the survivor left empty. Enumerating the
            // enum rather than the columns means a new reserved property is covered here for free.
            var changed = false;
            foreach (var property in Enum.GetValues<ReservedProperty>())
            {
                if (!ReservedProperties.TryGet(property, out var definition))
                {
                    continue;
                }

                if (IsEmpty(definition.Read(target)) && !IsEmpty(definition.Read(source)))
                {
                    definition.Write(target, definition.Read(source));
                    changed = true;
                }
            }

            if (changed)
            {
                reservedToUpdate.Add(target);
            }
        }

        if (reservedToUpdate.Count > 0)
        {
            await _reservedPropertyValueService.UpdateRange(reservedToUpdate);
        }

        if (customToMove.Count > 0 || reservedToUpdate.Count > 0)
        {
            _logger.LogInformation(
                "[Materialization] Transferred {Custom} custom and {Reserved} reserved property values from resource {From} to {To}",
                customToMove.Count, reservedToUpdate.Count, fromResourceId, toResourceId);
        }
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrEmpty(s),
        System.Collections.ICollection c => c.Count == 0,
        _ => false
    };
}

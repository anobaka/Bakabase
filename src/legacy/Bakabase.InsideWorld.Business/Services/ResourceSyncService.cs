using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Property.Abstractions.Components;
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
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Resolvers;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Service that handles resource discovery and creation from a specific source.
/// After discovering new resources, it:
/// 1. Marks related path marks as pending (for PathMarkSyncService to handle property sync)
/// 2. Rebuilds parent-child relationships for all resources
///
/// This service is NOT thread-safe. Callers must ensure only one sync per source runs at a time.
/// </summary>
public class ResourceSyncService : ScopedService
{
    private readonly IResourceService _resourceService;
    private readonly IResourceSourceLinkService _sourceLinkService;
    private readonly IPathMarkService _pathMarkService;
    private readonly IBOptions<ResourceOptions> _resourceOptions;
    private readonly IBakabaseLocalizer _localizer;
    private readonly IPropertyLocalizer _propertyLocalizer;
    private readonly ILogger<ResourceSyncService> _logger;
    private readonly IEnumerable<IResourceResolver> _resourceResolvers;
    private readonly IReservedPropertyValueService _reservedPropertyValueService;
    private readonly ResourceStructureService _structureService;
    private readonly IResourceMaterializationService _materializationService;

    public ResourceSyncService(
        IServiceProvider serviceProvider,
        IResourceService resourceService,
        IResourceSourceLinkService sourceLinkService,
        IPathMarkService pathMarkService,
        IBOptions<ResourceOptions> resourceOptions,
        IBakabaseLocalizer localizer,
        ILogger<ResourceSyncService> logger,
        IEnumerable<IResourceResolver> resourceResolvers,
        IReservedPropertyValueService reservedPropertyValueService,
        ResourceStructureService structureService,
        IResourceMaterializationService materializationService,
        IPropertyLocalizer propertyLocalizer) : base(serviceProvider)
    {
        _resourceService = resourceService;
        _sourceLinkService = sourceLinkService;
        _pathMarkService = pathMarkService;
        _resourceOptions = resourceOptions;
        _localizer = localizer;
        _propertyLocalizer = propertyLocalizer;
        _logger = logger;
        _resourceResolvers = resourceResolvers;
        _reservedPropertyValueService = reservedPropertyValueService;
        _structureService = structureService;
        _materializationService = materializationService;
    }

    /// <summary>
    /// Discover resources from the specified source and create/update them in the database.
    /// Returns true if new resources were created (triggering downstream sync).
    /// </summary>
    public async Task<ResourceSyncResult> SyncResources(
        ResourceSource source,
        Func<int, Task>? onProgressChange,
        Func<BTaskText?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct)
    {
        var result = new ResourceSyncResult();
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("[ResourceSync] Starting resource sync for source: {Source}", source);

        // ===== Step 1: Discover resources (0-50%) =====
        await ReportProgress(onProgressChange, onProcessChange, 0, BTaskText.Localize(_localizer, "BTask_Process_DiscoveringResources", BTaskText.Deferred(() => _propertyLocalizer.ResourceSourceName(source))));
        ct.ThrowIfCancellationRequested();
        var deletionSnapshots = source == ResourceSource.PathMark
            ? (await _pathMarkService.GetPendingMarks()).Where(m => m.Type == PathMarkType.Resource &&
                m.SyncStatus == PathMarkSyncStatus.PendingDelete && m.IsDeleted).ToList()
            : new List<PathMark>();

        var resolver = _resourceResolvers.FirstOrDefault(r => r.Source == source);
        if (resolver == null)
        {
            if (deletionSnapshots.Count > 0)
                throw new InvalidOperationException("The filesystem resolver is unavailable; pending resource mark deletions were retained.");
            _logger.LogWarning("[ResourceSync] No resolver found for source {Source}", source);
            await ReportProgress(onProgressChange, onProcessChange, 100, BTaskText.Localize(_localizer, "BTask_Process_ResolverNotFound"));
            return result;
        }

        // Load existing resources and source links for context
        var allResources = await _resourceService.GetAll();
        var allSourceLinks = await _sourceLinkService.GetAll();
        var ctx = new ResourceSyncContext();
        ctx.BuildIndexes(allResources, allSourceLinks);

        List<DiscoveredResourceEntry> discoveredEntries;

        if (source == ResourceSource.PathMark)
        {
            discoveredEntries = await DiscoverFileSystemResources(resolver, ctx, onProgressChange, onProcessChange, ct);
        }
        else
        {
            discoveredEntries = await DiscoverResolverResources(resolver, ctx, onProgressChange, onProcessChange, ct);
        }

        await ReportProgress(onProgressChange, onProcessChange, 50,
            BTaskText.Localize(_localizer, "BTask_Process_ResourcesDiscovered", discoveredEntries.Count, BTaskText.Deferred(() => _propertyLocalizer.ResourceSourceName(source))));

        // ===== Step 2: Create/Update resources (50-75%) =====
        await ReportProgress(onProgressChange, onProcessChange, 50, BTaskText.Localize(_localizer, "BTask_Process_UpdatingResources"));

        var resourcesToCreateOrUpdate = new List<Resource>();
        var newResourcePaths = new List<string>();
        var newResourceCount = 0;

        foreach (var entry in discoveredEntries)
        {
            ct.ThrowIfCancellationRequested();
            await pt.WaitWhilePausedAsync(ct);

            var resource = ResolveResourceForEntry(entry, ctx);
            if (resource != null)
            {
                resourcesToCreateOrUpdate.Add(resource);
                if (resource.Id == 0)
                {
                    newResourceCount++;
                    // Only a real path counts. A virtual entry ("dlsite://RJ01") names a resource
                    // we know about but hold no files for, and feeding that string to the marker
                    // writer or to path-mark coverage matching describes nothing on disk.
                    if (resource.HasLocalPath)
                    {
                        newResourcePaths.Add(resource.Path!);
                    }
                }
            }
        }

        if (resourcesToCreateOrUpdate.Count > 0)
        {
            // 100/batch keeps each AddOrPutRange short enough that a Stop click
            // is observed within seconds rather than minutes when the import
            // is tens of thousands of resources (issue #1098).
            var batches = resourcesToCreateOrUpdate.Chunk(100).ToList();
            for (var i = 0; i < batches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _resourceService.AddOrPutRange(batches[i].ToList());
                if (i < batches.Count - 1) await Task.Delay(5);

                var progress = 50 + (int)(25.0 * (i + 1) / batches.Count);
                await ReportProgress(onProgressChange, onProcessChange, progress, BTaskText.Localize(_localizer, "BTask_Process_UpdatingResources"));
            }

            result.ResourcesCreated = newResourceCount;
            result.ResourcesUpdated = resourcesToCreateOrUpdate.Count - newResourceCount;

            _logger.LogInformation(
                "[ResourceSync] Created {Created}, Updated {Updated} resources from {Source}",
                result.ResourcesCreated, result.ResourcesUpdated, source);

            // Create bakabase.json markers for new folder resources
            if (_resourceOptions.Value.KeepResourcesOnPathChange)
            {
                // Reload to get IDs
                var updatedResources = await _resourceService.GetAll();
                var pathToResource = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in updatedResources)
                {
                    if (r.HasLocalPath)
                        pathToResource[r.Path!] = r;
                }

                foreach (var path in newResourcePaths)
                {
                    if (pathToResource.TryGetValue(path, out var r) && !r.IsFile && r.Id > 0)
                    {
                        await CreateBakabaseJsonMarker(r.Path!, r.Id);
                    }
                }
            }
        }

        // ===== Step 2.5: Materialize resources whose files just showed up =====
        result.ResourcesMaterialized = await ApplyPendingMaterializations(ctx, ct);

        // ===== Step 2.6: Auto-populate Name reserved property =====
        if (resourcesToCreateOrUpdate.Count > 0)
        {
            await PopulateResourceNames(source, discoveredEntries, ct);
        }

        // ===== Step 3: Update resource statuses for resolver sources (75-80%) =====
        await ReportProgress(onProgressChange, onProcessChange, 75, BTaskText.Localize(_localizer, "BTask_Process_UpdatingResourceStatuses"));
        await UpdateResolverResourceStatuses(source, discoveredEntries, ctx, ct);

        // ===== Step 4: Delete orphaned resources (80-85%) =====
        await ReportProgress(onProgressChange, onProcessChange, 80, BTaskText.Localize(_localizer, "BTask_Process_CheckingOrphanedResources"));
        var deleted = await DeleteOrphanedResources(source, deletionSnapshots, ct);
        result.ResourcesDeleted = deleted;

        // ===== Step 5: Rebuild parent-child relationships if new resources (85-92%) =====
        if (result.ResourcesCreated > 0 || result.ResourcesDeleted > 0)
        {
            await ReportProgress(onProgressChange, onProcessChange, 85, BTaskText.Localize(_localizer, "BTask_Process_RebuildingRelationships"));
            await _structureService.RebuildParentChildRelationships(ct);
            result.ParentChildRebuilt = true;
        }

        // ===== Step 6: Mark related path marks as pending (92-98%) =====
        if (newResourcePaths.Count > 0)
        {
            await ReportProgress(onProgressChange, onProcessChange, 92, BTaskText.Localize(_localizer, "BTask_Process_QueuingRelatedPathMarks"));
            await _structureService.MarkPathMarksAsPendingForPaths(newResourcePaths, ct);
            result.PathMarksMarkedPending = true;
        }

        if (deletionSnapshots.Count > 0)
        {
            await ReportProgress(onProgressChange, onProcessChange, 98,
                BTaskText.Localize(_localizer, "SyncPathMark_UpdatingSearchIndex"));
            await GetRequiredService<IResourceSearchIndexService>().WaitForPendingUpdatesAsync(ct);
            ct.ThrowIfCancellationRequested();
            await _pathMarkService.FinalizeResourceDeletions(deletionSnapshots, ct);
        }
        await ReportProgress(onProgressChange, onProcessChange, 100, BTaskText.Localize(_localizer, "ResourceSync_Complete"));

        sw.Stop();
        _logger.LogInformation(
            "[ResourceSync] Completed {Source} sync in {ElapsedMs}ms: Created={Created}, Updated={Updated}, Deleted={Deleted}",
            source, sw.ElapsedMilliseconds, result.ResourcesCreated, result.ResourcesUpdated, result.ResourcesDeleted);

        return result;
    }

    #region Resource Discovery

    private async Task<List<DiscoveredResourceEntry>> DiscoverFileSystemResources(
        IResourceResolver resolver,
        ResourceSyncContext ctx,
        Func<int, Task>? onProgressChange,
        Func<BTaskText?, Task>? onProcessChange,
        CancellationToken ct)
    {
        var fsResolver = resolver as FileSystemResolver;
        if (fsResolver == null)
        {
            _logger.LogWarning("[ResourceSync] FileSystem resolver is not a FileSystemResolver");
            return new List<DiscoveredResourceEntry>();
        }

        // Get pending resource marks for FileSystem
        var allMarks = await _pathMarkService.GetAll();
        var pendingResourceMarks = allMarks
            .Where(m => m.Type == PathMarkType.Resource &&
                         m.SyncStatus == PathMarkSyncStatus.Pending)
            .OrderByDescending(m => m.Priority)
            .ToList();

        if (pendingResourceMarks.Count == 0)
        {
            _logger.LogDebug("[ResourceSync] No pending resource marks for FileSystem");
            return new List<DiscoveredResourceEntry>();
        }

        // Mark as syncing
        foreach (var mark in pendingResourceMarks)
        {
            await _pathMarkService.MarkAsSyncing(mark.Id);
        }

        await ReportProgress(onProgressChange, onProcessChange, 10,
            BTaskText.Localize(_localizer, "BTask_Process_DiscoveringPathMarks", pendingResourceMarks.Count));

        try
        {
            var discovered = await fsResolver.DiscoverFromMarks(pendingResourceMarks, ct,
                allMarks.Where(mark => mark.Type == PathMarkType.Resource && !mark.IsDeleted &&
                                       mark.SyncStatus != PathMarkSyncStatus.PendingDelete).ToList());

            // Acquisition persists its boundary before creating category directories. If this
            // scan saw a new category using an older mark snapshot, its boundary must now exist.
            // Recheck before turning discovery candidates into resources.
            var currentMarks = (await _pathMarkService.GetAll())
                .Where(mark => mark.Type == PathMarkType.Resource && !mark.IsDeleted &&
                               mark.SyncStatus != PathMarkSyncStatus.PendingDelete).ToList();
            discovered = FileSystemResolver.ApplyResourceBoundaries(discovered, pendingResourceMarks, currentMarks);

            var entries = discovered.Select(d => new DiscoveredResourceEntry
            {
                EffectivePath = d.Path,
                Source = ResourceSource.PathMark,
                SourceKey = d.Path,
                MarkId = d.MarkId
            }).ToList();

            // Mark resource marks as synced
            var markIds = pendingResourceMarks.Select(m => m.Id).ToList();
            await _pathMarkService.MarkAsSyncedBatch(markIds);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ResourceSync] FileSystem discovery failed");
            // Mark as failed
            foreach (var mark in pendingResourceMarks)
            {
                await _pathMarkService.MarkAsFailed(mark.Id, ex.Message);
            }
            throw;
        }
    }

    private async Task<List<DiscoveredResourceEntry>> DiscoverResolverResources(
        IResourceResolver resolver,
        ResourceSyncContext ctx,
        Func<int, Task>? onProgressChange,
        Func<BTaskText?, Task>? onProcessChange,
        CancellationToken ct)
    {
        var sourceName = resolver.Source.ToString();

        await ReportProgress(onProgressChange, onProcessChange, 10,
            BTaskText.Localize(_localizer, "BTask_Process_DiscoveringResources", BTaskText.Deferred(() => _propertyLocalizer.ResourceSourceName(resolver.Source))));

        var resolvedResources = await resolver.DiscoverResources(ct);
        _logger.LogInformation("[ResourceSync] {Source} discovered {Count} resources",
            sourceName, resolvedResources.Count);

        return resolvedResources.Select(r => new DiscoveredResourceEntry
        {
            EffectivePath = !string.IsNullOrEmpty(r.Path)
                ? r.Path.StandardizePath()!
                : BuildVirtualPath(r.Source, r.SourceKey),
            Source = r.Source,
            SourceKey = r.SourceKey,
            DisplayName = r.DisplayName,
            LocalPath = r.Path,
            CoverUrls = r.CoverUrls
        }).ToList();
    }

    #endregion

    #region Resource Resolution

    /// <summary>
    /// Resolves a discovered entry to a Resource (existing or new).
    /// Returns null if the entry should be skipped.
    /// </summary>
    private Resource? ResolveResourceForEntry(DiscoveredResourceEntry entry, ResourceSyncContext ctx)
    {
        var isVirtual = IsVirtualPath(entry.EffectivePath);

        // Check if resource already exists by path
        if (ctx.PathToResource.TryGetValue(entry.EffectivePath, out var existingByPath))
        {
            // Check for conflicting source links: same source but different key means they are different resources
            if (HasConflictingSourceLink(existingByPath, entry.Source, entry.SourceKey, ctx))
            {
                // Cannot merge - same source with different key indicates a different resource
                _logger.LogDebug(
                    "[ResourceSync] Path {Path} matched existing resource {ResourceId} but has conflicting source link ({Source}), creating new resource",
                    entry.EffectivePath, existingByPath.Id, entry.Source);
            }
            else
            {
                EnsureSourceLink(existingByPath, entry.Source, entry.SourceKey, entry.CoverUrls);
                return existingByPath;
            }
        }

        // Check if resource exists by source link
        if (ctx.SourceLinkToResourceId.TryGetValue((entry.Source, entry.SourceKey), out var linkedResourceId)
            && ctx.IdToResource.TryGetValue(linkedResourceId, out var linkedResource))
        {
            var newPath = isVirtual ? entry.LocalPath : entry.EffectivePath;
            if (newPath != linkedResource.Path)
            {
                // Writing Path here would leave IsFile, the file times, the filesystem caches, the
                // parent-child tree and the covering path marks all describing the state before
                // the files arrived. Gaining or losing local files is a transition with side
                // effects, so it is deferred to the one service that applies all of them.
                if (string.IsNullOrEmpty(newPath))
                {
                    ctx.PendingDematerializations.Add(linkedResource.Id);
                }
                else
                {
                    ctx.PendingMaterializations[linkedResource.Id] = newPath;
                }
            }
            EnsureSourceLink(linkedResource, entry.Source, entry.SourceKey, entry.CoverUrls);
            return linkedResource;
        }

        // Check bakabase.json marker for path recovery (filesystem only)
        if (!isVirtual)
        {
            var markerResourceId = CheckBakabaseJsonMarkerSync(entry.EffectivePath);
            if (markerResourceId.HasValue && ctx.IdToResource.TryGetValue(markerResourceId.Value, out var markerResource))
            {
                markerResource.Path = entry.EffectivePath;
                markerResource.UpdatedAt = DateTime.Now;
                EnsureSourceLink(markerResource, entry.Source, entry.SourceKey, entry.CoverUrls);
                return markerResource;
            }
        }

        // Create new resource
        if (isVirtual)
        {
            return new Resource
            {
                Path = entry.LocalPath,
                IsFile = false,
                Status = ResourceStatus.Active,
                DisplayName = entry.DisplayName,
                MediaLibraryId = 0,
                FileCreatedAt = DateTime.Now,
                FileModifiedAt = DateTime.Now,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                SourceLinks = [new ResourceSourceLink { Source = entry.Source, SourceKey = entry.SourceKey, CoverUrls = entry.CoverUrls }]
            };
        }
        else
        {
            var isFile = File.Exists(entry.EffectivePath);
            var isDirectory = Directory.Exists(entry.EffectivePath);
            if (!isFile && !isDirectory) return null;

            DateTime fileCreatedAt, fileModifiedAt;
            if (isFile)
            {
                var fi = new FileInfo(entry.EffectivePath);
                fileCreatedAt = fi.CreationTime.TruncateToMilliseconds();
                fileModifiedAt = fi.LastWriteTime.TruncateToMilliseconds();
            }
            else
            {
                var di = new DirectoryInfo(entry.EffectivePath);
                fileCreatedAt = di.CreationTime.TruncateToMilliseconds();
                fileModifiedAt = di.LastWriteTime.TruncateToMilliseconds();
            }

            return new Resource
            {
                Path = entry.EffectivePath,
                IsFile = isFile,
                Status = ResourceStatus.Active,
                MediaLibraryId = 0,
                FileCreatedAt = fileCreatedAt,
                FileModifiedAt = fileModifiedAt,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                SourceLinks = [new ResourceSourceLink { Source = entry.Source, SourceKey = entry.SourceKey, CoverUrls = entry.CoverUrls }]
            };
        }
    }

    #endregion

    #region Post-Discovery Actions

    /// <summary>
    /// Auto-populates the Name reserved property for discovered resources.
    /// - PathMark: filename without extension (fallback only — does not overwrite existing Name,
    ///   which may have been set by a user-configured property PathMark targeting Reserved.Name)
    /// - External sources: DisplayName from resolver (always updates)
    /// </summary>
    private async Task PopulateResourceNames(
        ResourceSource source,
        List<DiscoveredResourceEntry> discoveredEntries,
        CancellationToken ct)
    {
        // Reload resources and links to get assigned IDs
        var allResources = await _resourceService.GetAll();
        var pathToResource = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);
        var idToResource = new Dictionary<int, Resource>();
        foreach (var r in allResources)
        {
            if (r.HasLocalPath)
                pathToResource[r.Path!] = r;
            idToResource[r.Id] = r;
        }

        // A resource we hold no files for has no path to look it up by, so its identity — the
        // source link — is the only handle. Without this an undownloaded DLsite work never got a
        // Name and showed up blank in the UI.
        var sourceLinkToResourceId = new Dictionary<(ResourceSource Source, string SourceKey), int>();
        foreach (var link in await _sourceLinkService.GetAll())
        {
            sourceLinkToResourceId[(link.Source, link.SourceKey)] = link.ResourceId;
        }

        var scope = source.GetPropertyValueScope();

        // Collect resource IDs that need Name populated
        var resourceNames = new Dictionary<int, string>();

        foreach (var entry in discoveredEntries)
        {
            ct.ThrowIfCancellationRequested();

            // Find the resource by its path, falling back to its source link — the only handle a
            // resource without local files has.
            Resource? resource = null;
            if (!string.IsNullOrEmpty(entry.EffectivePath) && !IsVirtualPath(entry.EffectivePath))
                pathToResource.TryGetValue(entry.EffectivePath, out resource);
            if (resource == null && !string.IsNullOrEmpty(entry.LocalPath))
                pathToResource.TryGetValue(entry.LocalPath, out resource);
            if (resource == null &&
                sourceLinkToResourceId.TryGetValue((entry.Source, entry.SourceKey), out var linkedResourceId))
                idToResource.TryGetValue(linkedResourceId, out resource);

            if (resource == null || resource.Id == 0) continue;

            string? name;
            if (source == ResourceSource.PathMark)
            {
                // For PathMark: filename without extension
                name = resource.HasLocalPath
                    ? Path.GetFileNameWithoutExtension(resource.Path!)
                    : null;
            }
            else
            {
                // For external sources: DisplayName from resolver
                name = entry.DisplayName;
            }

            if (!string.IsNullOrEmpty(name))
            {
                resourceNames[resource.Id] = name;
            }
        }

        if (resourceNames.Count == 0) return;

        // Load existing Name reserved property values
        var resourceIds = resourceNames.Keys.ToList();
        var existingValues = await _reservedPropertyValueService.GetAll(
            v => resourceIds.Contains(v.ResourceId) && v.Scope == (int)scope);
        var existingMap = existingValues.ToDictionary(v => v.ResourceId, v => v);

        var toAdd = new List<ReservedPropertyValue>();
        var toUpdate = new List<ReservedPropertyValue>();

        foreach (var (resourceId, name) in resourceNames)
        {
            if (existingMap.TryGetValue(resourceId, out var existing))
            {
                // For PathMark: this is a fallback — do not overwrite existing Name
                // (it may have been set by a user-configured property PathMark targeting Reserved.Name)
                if (source == ResourceSource.PathMark)
                    continue;

                if (existing.Name != name)
                {
                    existing.Name = name;
                    toUpdate.Add(existing);
                }
            }
            else
            {
                toAdd.Add(new ReservedPropertyValue
                {
                    ResourceId = resourceId,
                    Scope = (int)scope,
                    Name = name
                });
            }
        }

        if (toAdd.Count > 0)
            await _reservedPropertyValueService.AddRange(toAdd);
        if (toUpdate.Count > 0)
            await _reservedPropertyValueService.UpdateRange(toUpdate);

        _logger.LogInformation(
            "[ResourceSync] Populated Name for {Total} resources (added: {Added}, updated: {Updated})",
            resourceNames.Count, toAdd.Count, toUpdate.Count);
    }

    /// <summary>
    /// Updates resource statuses based on resolver discovery results.
    /// </summary>
    private async Task UpdateResolverResourceStatuses(
        ResourceSource source,
        List<DiscoveredResourceEntry> discoveredEntries,
        ResourceSyncContext ctx,
        CancellationToken ct)
    {
        // Only for non-FileSystem sources
        if (source == ResourceSource.PathMark) return;

        var discoveredKeys = discoveredEntries
            .Select(e => (e.Source, e.SourceKey))
            .ToHashSet();

        var sourceResources = ctx.AllResources
            .Where(r => ctx.ResourceIdToSourceLinks.TryGetValue(r.Id, out var links)
                         && links.Any(l => l.Source == source))
            .ToList();

        var resourcesToUpdate = new List<Resource>();

        foreach (var resource in sourceResources)
        {
            ct.ThrowIfCancellationRequested();

            var resourceLinks = ctx.ResourceIdToSourceLinks.GetValueOrDefault(resource.Id);
            var hasDiscoveredLink = resourceLinks != null
                && resourceLinks.Any(l => l.Source == source && discoveredKeys.Contains(l));

            if (hasDiscoveredLink)
            {
                if (resource.Status != ResourceStatus.Active)
                {
                    resource.Status = ResourceStatus.Active;
                    resource.UpdatedAt = DateTime.Now;
                    resourcesToUpdate.Add(resource);
                }
            }
            else
            {
                if (resource.Status != ResourceStatus.Absent)
                {
                    resource.Status = ResourceStatus.Absent;
                    resource.UpdatedAt = DateTime.Now;
                    resourcesToUpdate.Add(resource);
                }
            }
        }

        if (resourcesToUpdate.Count > 0)
        {
            await _resourceService.AddOrPutRange(resourcesToUpdate);
            _logger.LogInformation("[ResourceSync] Updated status for {Count} resources from {Source}",
                resourcesToUpdate.Count, source);
        }
    }

    /// <summary>
    /// Cleans up resources tied to resource marks that the user just soft-deleted with the
    /// "remove effects" choice (controller maps that to <see cref="IPathMarkService.SoftDelete"/>,
    /// which sets <c>SyncStatus = PendingDelete</c>).
    ///
    /// Triggers ONLY on PendingDelete marks — never on the mere absence of coverage. That way:
    /// - User soft-deletes mark with removeEffects=true  → mark is PendingDelete → cleanup here.
    /// - User soft-deletes mark with removeEffects=false → controller calls HardDelete, the mark
    ///   row is gone, no PendingDelete entry exists, this method does NOTHING. Resource is kept
    ///   so the user can later merge it into a new resource (intentional UX).
    /// - Resource's file disappears from disk but mark still active → not handled here at all.
    ///
    /// Ownership is determined from the persisted mark ledger and current resource paths,
    /// never by requiring a deleted mark's directory to still exist on this machine.
    /// Shared-resource protection retains other active ledger/rule owners and other sources.
    ///
    /// Returns 0 for non-PathMark sources — resolvers mark missing items Absent and the user
    /// can decide what to do.
    /// </summary>
    private async Task<int> DeleteOrphanedResources(
        ResourceSource source, List<PathMark> snapshots, CancellationToken ct)
    {
        if (source != ResourceSource.PathMark || snapshots.Count == 0) return 0;
        var allMarks = await _pathMarkService.GetAll();
        var currentMarks = allMarks.ToDictionary(m => m.Id);
        var deleting = snapshots.Where(snapshot => currentMarks.TryGetValue(snapshot.Id, out var current) &&
            PathMarkService<BakabaseDbContext>.MatchesResourceDeletionSnapshot(current, snapshot)).ToList();
        if (deleting.Count == 0) return 0;
        var active = allMarks.Where(m => m.Type == PathMarkType.Resource && !m.IsDeleted &&
            m.SyncStatus != PathMarkSyncStatus.PendingDelete).ToList();
        var groups = (await GetRequiredService<IExtensionGroupService>().GetAll()).ToDictionary(g => g.Id);
        var configs = new Dictionary<int, ResourceMarkConfig>();
        foreach (var mark in deleting.Concat(active))
        {
            ct.ThrowIfCancellationRequested();
            var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson)
                         ?? throw new InvalidOperationException($"Resource mark {mark.Id} has no matching configuration.");
            if (config.MatchMode is not (PathMatchMode.Layer or PathMatchMode.Regex) ||
                config.MatchMode == PathMatchMode.Layer && !config.Layer.HasValue ||
                config.MatchMode == PathMatchMode.Regex && string.IsNullOrWhiteSpace(config.Regex) ||
                config.ApplyScope is not (PathMarkApplyScope.MatchedOnly or PathMarkApplyScope.MatchedAndSubdirectories) ||
                config.FsTypeFilter is not (null or PathFilterFsType.File or PathFilterFsType.Directory))
                throw new InvalidOperationException($"Resource mark {mark.Id} has an invalid matching configuration.");
            if (config.ExtensionGroupIds is {Count: > 0})
            {
                var extensions = new List<string>(config.Extensions ?? []);
                foreach (var groupId in config.ExtensionGroupIds)
                {
                    if (!groups.TryGetValue(groupId, out var group))
                        throw new InvalidOperationException($"Resource mark {mark.Id} refers to missing extension group {groupId}.");
                    extensions.AddRange(group.Extensions ?? []);
                }
                config.Extensions = extensions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            configs[mark.Id] = config;
        }
        var boundaryPaths = active.Where(m => configs[m.Id].IsResourceBoundary).Select(m => m.Path).ToArray();
        // Prepare every rule before touching resource rows, including empty/missing directories.
        // Invalid expressions must leave the deletion pending rather than report success.
        var deletedRules = deleting.ToDictionary(m => m.Id,
            m => ResourceMarkPathMatcher.Create(m.Path, configs[m.Id], boundaryPaths));
        var activeRules = active.Select(m => ResourceMarkPathMatcher.Create(m.Path, configs[m.Id], boundaryPaths)).ToArray();
        var effectService = GetRequiredService<IPathMarkEffectService>();
        var deletedEffects = await effectService.GetResourceEffectsByMarkIds(deleting.Select(m => m.Id));
        var activeEffects = await effectService.GetResourceEffectsByMarkIds(active.Select(m => m.Id));
        var deletedLedgerPaths = deletedEffects.GroupBy(e => e.MarkId).ToDictionary(group => group.Key,
            group => group.Select(e => e.Path.StandardizePath()!).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var activeLedgerPaths = activeEffects.Select(e => e.Path.StandardizePath()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Discovery can take a long time on network storage. Re-read current paths and
        // source ownership before cleanup so a move performed during discovery is kept.
        var currentResources = new ResourceSyncContext();
        currentResources.BuildIndexes(await _resourceService.GetAll(), await _sourceLinkService.GetAll());
        var orphanIds = new List<int>();
        var retirementEffects = new List<ResourceMarkEffect>();
        foreach (var resource in currentResources.IdToResource.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (!resource.HasLocalPath) continue;
            var links = currentResources.ResourceIdToSourceLinks.GetValueOrDefault(resource.Id);
            if (links?.Any(l => l.Source != ResourceSource.PathMark) == true) continue;
            var path = resource.Path!.StandardizePath()!;
            // Historical source keys may name a file the user has moved. Only current path
            // ownership or a current-path ledger entry is evidence for deleting metadata.
            // Imported resources predate source links. A mark-id/current-path ledger join
            // remains positive ownership evidence, including a retry after partial cleanup.
            var hasPathMarkSource = links?.Any(l => l.Source == ResourceSource.PathMark) == true;
            var owners = deleting.Where(mark =>
                (deletedLedgerPaths.GetValueOrDefault(mark.Id)?.Contains(path) == true ||
                 hasPathMarkSource && deletedRules[mark.Id](path, resource.IsFile)) &&
                // Boundaries exclude an ancestor mark's claim. A boundary equal to or
                // above this deleted mark does not veto this mark's explicit child rule.
                !boundaryPaths.Any(boundary => boundary.StandardizePath()!.IsPathUnder(mark.Path.StandardizePath()!) &&
                    path.IsPathEqualOrUnder(boundary.StandardizePath()!))).ToArray();
            if (owners.Length == 0 || activeLedgerPaths.Contains(path) || activeRules.Any(matches => matches(path, resource.IsFile))) continue;
            orphanIds.Add(resource.Id);
            foreach (var owner in owners)
            {
                if (deletedLedgerPaths.GetValueOrDefault(owner.Id)?.Contains(path) == true) continue;
                retirementEffects.Add(new ResourceMarkEffect {MarkId = owner.Id, Path = path});
            }
        }
        if (orphanIds.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            // Resource deletion removes source links before the resource row. Preserve the
            // verified ownership first so a failed partial cleanup remains safe to retry.
            // These effects are retired with the captured tombstone only after index drain.
            await effectService.AddResourceEffects(retirementEffects);
            ct.ThrowIfCancellationRequested();
            await _resourceService.DeleteByKeys(orphanIds.ToArray(), deleteFiles: false);
            _logger.LogInformation("[ResourceSync] Removed {Count} resource records owned by deleted marks; media files retained", orphanIds.Count);
        }
        return orphanIds.Count;
    }

    /// <summary>
    /// Recompute ParentId/IsParent after resource paths changed outside a sync pass
    /// (e.g. a resource move).
    /// </summary>
    public Task RebuildParentChildRelationships(CancellationToken ct) =>
        _structureService.RebuildParentChildRelationships(ct);

    /// <summary>
    /// Applies the path transitions collected during resolution. Each one goes through
    /// <see cref="IResourceMaterializationService"/> so a resource that just gained files ends up
    /// with correct file times, fresh caches and re-synced path marks — none of which the plain
    /// create/update batch would have given it.
    /// </summary>
    private async Task<int> ApplyPendingMaterializations(ResourceSyncContext ctx, CancellationToken ct)
    {
        if (ctx.PendingMaterializations.Count == 0 && ctx.PendingDematerializations.Count == 0)
        {
            return 0;
        }

        // The whole pass shares one path-mark sync, kicked once at the end instead of per resource.
        var options = new MaterializationOptions(EnqueuePathMarkSync: false);
        var applied = 0;

        foreach (var (resourceId, path) in ctx.PendingMaterializations)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _materializationService.MaterializeAsync(resourceId, path, options, ct);
                applied++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One resource failing to materialize (a path that vanished between discovery and
                // now, a conflict we refuse to resolve) must not abort the rest of the sync.
                _logger.LogError(ex, "[ResourceSync] Failed to materialize resource {ResourceId} at {Path}",
                    resourceId, path);
            }
        }

        foreach (var resourceId in ctx.PendingDematerializations)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _materializationService.DematerializeAsync(resourceId, ct);
                applied++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[ResourceSync] Failed to dematerialize resource {ResourceId}", resourceId);
            }
        }

        if (ctx.PendingMaterializations.Count > 0)
        {
            await GetRequiredService<IPathMarkSyncService>().EnqueueSync();
        }

        _logger.LogInformation("[ResourceSync] Applied {Count} materialization changes", applied);

        return applied;
    }

    #endregion

    #region Helpers

    internal static string BuildVirtualPath(ResourceSource source, string sourceKey)
    {
        return $"{source.ToString().ToLowerInvariant()}://{sourceKey}";
    }

    internal static bool IsVirtualPath(string path)
    {
        return path.Contains("://");
    }

    private static void EnsureSourceLink(Resource resource, ResourceSource source, string sourceKey, List<string>? coverUrls = null)
    {
        resource.SourceLinks ??= [];
        var existing = resource.SourceLinks.FirstOrDefault(l => l.Source == source && l.SourceKey == sourceKey);
        if (existing != null)
        {
            // Update cover URLs if newly provided and not already set
            if (coverUrls is { Count: > 0 } && existing.CoverUrls is not { Count: > 0 })
            {
                existing.CoverUrls = coverUrls;
            }
        }
        else
        {
            resource.SourceLinks.Add(new ResourceSourceLink { Source = source, SourceKey = sourceKey, CoverUrls = coverUrls });
        }
    }

    /// <summary>
    /// Checks if the existing resource has a source link with the same source but a different key.
    /// This indicates they are different resources from the same source and should NOT be merged.
    /// </summary>
    private static bool HasConflictingSourceLink(Resource existingResource, ResourceSource newSource, string newSourceKey, ResourceSyncContext ctx)
    {
        if (!ctx.ResourceIdToSourceLinks.TryGetValue(existingResource.Id, out var existingLinks))
        {
            // Also check in-memory source links (for newly created resources not yet in ctx)
            if (existingResource.SourceLinks != null)
            {
                return existingResource.SourceLinks.Any(l => l.Source == newSource && l.SourceKey != newSourceKey);
            }
            return false;
        }

        return existingLinks.Any(l => l.Source == newSource && l.SourceKey != newSourceKey);
    }

    private int? CheckBakabaseJsonMarkerSync(string path)
    {
        if (!_resourceOptions.Value.KeepResourcesOnPathChange) return null;
        if (File.Exists(path)) return null; // Only for directories

        var markerPath = Path.Combine(path, InternalOptions.ResourceMarkerFileName);
        if (!File.Exists(markerPath)) return null;

        try
        {
            var json = File.ReadAllText(markerPath);
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
            var content = System.Text.Json.JsonSerializer.Serialize(new { ids = new[] { resourceId } }, System.Text.Json.JsonSerializerOptions.Web);
            await File.WriteAllTextAsync(markerPath, content);
            File.SetAttributes(markerPath, File.GetAttributes(markerPath) | FileAttributes.Hidden);
        }
        catch
        {
            // Ignore marker creation errors
        }
    }

    private async Task ReportProgress(Func<int, Task>? onProgressChange, Func<BTaskText?, Task>? onProcessChange,
        int progress, BTaskText? process)
    {
        if (onProgressChange != null) await onProgressChange(progress);
        if (onProcessChange != null) await onProcessChange(process);
    }

    #endregion
}

/// <summary>
/// A resource entry discovered during sync, before resolution to actual Resource objects.
/// </summary>
internal class DiscoveredResourceEntry
{
    /// <summary>
    /// The effective path (real path for filesystem, virtual path for resolver-only resources).
    /// </summary>
    public string EffectivePath { get; init; } = null!;

    /// <summary>
    /// The resource source.
    /// </summary>
    public ResourceSource Source { get; init; }

    /// <summary>
    /// The source-specific key (path for filesystem, app ID for Steam, etc.).
    /// </summary>
    public string SourceKey { get; init; } = null!;

    /// <summary>
    /// Display name (for resolver resources).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Local path (may differ from EffectivePath for virtual resources that have a local presence).
    /// </summary>
    public string? LocalPath { get; init; }

    /// <summary>
    /// The mark ID that discovered this resource (filesystem only).
    /// </summary>
    public int? MarkId { get; init; }

    /// <summary>
    /// Cover image URLs from the external source.
    /// </summary>
    public List<string>? CoverUrls { get; init; }
}

/// <summary>
/// Cached context for resource sync operations.
/// </summary>
internal class ResourceSyncContext
{
    public List<Resource> AllResources { get; private set; } = new();
    public Dictionary<string, Resource> PathToResource { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, Resource> IdToResource { get; } = new();

    /// <summary>
    /// Resources discovered to have local files they did not have before, and where those files
    /// are. Applied after the create/update batch by <see cref="IResourceMaterializationService"/>.
    /// </summary>
    public Dictionary<int, string> PendingMaterializations { get; } = new();

    /// <summary>
    /// Resources whose local files the source no longer reports.
    /// </summary>
    public HashSet<int> PendingDematerializations { get; } = new();

    public Dictionary<(ResourceSource Source, string SourceKey), int> SourceLinkToResourceId { get; } = new();
    public Dictionary<int, HashSet<(ResourceSource Source, string SourceKey)>> ResourceIdToSourceLinks { get; } = new();

    public void BuildIndexes(List<Resource> allResources, List<ResourceSourceLink>? allSourceLinks = null)
    {
        AllResources = allResources;
        PathToResource.Clear();
        IdToResource.Clear();
        SourceLinkToResourceId.Clear();
        ResourceIdToSourceLinks.Clear();
        PendingMaterializations.Clear();
        PendingDematerializations.Clear();

        foreach (var resource in allResources)
        {
            if (resource.HasLocalPath)
            {
                PathToResource[resource.Path!] = resource;
            }
            IdToResource[resource.Id] = resource;
        }

        if (allSourceLinks != null)
        {
            foreach (var link in allSourceLinks)
            {
                SourceLinkToResourceId[(link.Source, link.SourceKey)] = link.ResourceId;

                if (!ResourceIdToSourceLinks.TryGetValue(link.ResourceId, out var linkSet))
                {
                    linkSet = new HashSet<(ResourceSource Source, string SourceKey)>();
                    ResourceIdToSourceLinks[link.ResourceId] = linkSet;
                }
                linkSet.Add((link.Source, link.SourceKey));
            }
        }
    }
}

public class ResourceSyncResult
{
    public int ResourcesCreated { get; set; }
    public int ResourcesUpdated { get; set; }
    public int ResourcesDeleted { get; set; }

    /// <summary>
    /// Resources that gained or lost their local files during this pass.
    /// </summary>
    public int ResourcesMaterialized { get; set; }
    public bool ParentChildRebuilt { get; set; }
    public bool PathMarksMarkedPending { get; set; }
}

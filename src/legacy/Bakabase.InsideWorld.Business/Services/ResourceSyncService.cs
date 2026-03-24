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
using Bakabase.Modules.ResourceResolver.Abstractions;
using Bakabase.Modules.ResourceResolver.Components;
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
    private readonly ILogger<ResourceSyncService> _logger;
    private readonly IEnumerable<IResourceResolver> _resourceResolvers;

    public ResourceSyncService(
        IServiceProvider serviceProvider,
        IResourceService resourceService,
        IResourceSourceLinkService sourceLinkService,
        IPathMarkService pathMarkService,
        IBOptions<ResourceOptions> resourceOptions,
        IBakabaseLocalizer localizer,
        ILogger<ResourceSyncService> logger,
        IEnumerable<IResourceResolver> resourceResolvers) : base(serviceProvider)
    {
        _resourceService = resourceService;
        _sourceLinkService = sourceLinkService;
        _pathMarkService = pathMarkService;
        _resourceOptions = resourceOptions;
        _localizer = localizer;
        _logger = logger;
        _resourceResolvers = resourceResolvers;
    }

    /// <summary>
    /// Discover resources from the specified source and create/update them in the database.
    /// Returns true if new resources were created (triggering downstream sync).
    /// </summary>
    public async Task<ResourceSyncResult> SyncResources(
        ResourceSource source,
        Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct)
    {
        var result = new ResourceSyncResult();
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("[ResourceSync] Starting resource sync for source: {Source}", source);

        // ===== Step 1: Discover resources (0-50%) =====
        await ReportProgress(onProgressChange, onProcessChange, 0, $"Discovering {source} resources...");

        var resolver = _resourceResolvers.FirstOrDefault(r => r.Source == source);
        if (resolver == null)
        {
            _logger.LogWarning("[ResourceSync] No resolver found for source {Source}", source);
            await ReportProgress(onProgressChange, onProcessChange, 100, "No resolver found");
            return result;
        }

        // Load existing resources and source links for context
        var allResources = await _resourceService.GetAll();
        var allSourceLinks = await _sourceLinkService.GetAll();
        var ctx = new ResourceSyncContext();
        ctx.BuildIndexes(allResources, allSourceLinks);

        List<DiscoveredResourceEntry> discoveredEntries;

        if (source == ResourceSource.FileSystem)
        {
            discoveredEntries = await DiscoverFileSystemResources(resolver, ctx, onProgressChange, onProcessChange, ct);
        }
        else
        {
            discoveredEntries = await DiscoverResolverResources(resolver, ctx, onProgressChange, onProcessChange, ct);
        }

        await ReportProgress(onProgressChange, onProcessChange, 50,
            $"Discovered {discoveredEntries.Count} resources from {source}");

        // ===== Step 2: Create/Update resources (50-75%) =====
        await ReportProgress(onProgressChange, onProcessChange, 50, "Creating/updating resources...");

        var resourcesToCreateOrUpdate = new List<Resource>();
        var newResourcePaths = new List<string>();

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
                    newResourcePaths.Add(entry.EffectivePath);
                }
            }
        }

        if (resourcesToCreateOrUpdate.Count > 0)
        {
            var batches = resourcesToCreateOrUpdate.Chunk(500).ToList();
            for (var i = 0; i < batches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await _resourceService.AddOrPutRange(batches[i].ToList());
                if (i < batches.Count - 1) await Task.Delay(5);

                var progress = 50 + (int)(25.0 * (i + 1) / batches.Count);
                await ReportProgress(onProgressChange, onProcessChange, progress, "Creating/updating resources...");
            }

            result.ResourcesCreated = newResourcePaths.Count;
            result.ResourcesUpdated = resourcesToCreateOrUpdate.Count - newResourcePaths.Count;

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
                    if (!string.IsNullOrEmpty(r.Path))
                        pathToResource[r.Path] = r;
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

        // ===== Step 3: Update resource statuses for resolver sources (75-80%) =====
        await ReportProgress(onProgressChange, onProcessChange, 75, "Updating resource statuses...");
        await UpdateResolverResourceStatuses(source, discoveredEntries, ctx, ct);

        // ===== Step 4: Delete orphaned resources (80-85%) =====
        await ReportProgress(onProgressChange, onProcessChange, 80, "Checking for orphaned resources...");
        var deleted = await DeleteOrphanedResources(source, discoveredEntries, ctx, ct);
        result.ResourcesDeleted = deleted;

        // ===== Step 5: Rebuild parent-child relationships if new resources (85-92%) =====
        if (result.ResourcesCreated > 0 || result.ResourcesDeleted > 0)
        {
            await ReportProgress(onProgressChange, onProcessChange, 85, "Rebuilding parent-child relationships...");
            await RebuildAllParentChildRelationships(ct);
            result.ParentChildRebuilt = true;
        }

        // ===== Step 6: Mark related path marks as pending (92-98%) =====
        if (result.ResourcesCreated > 0)
        {
            await ReportProgress(onProgressChange, onProcessChange, 92, "Marking related path marks as pending...");
            await MarkRelatedPathMarksAsPending(newResourcePaths, ct);
            result.PathMarksMarkedPending = true;
        }

        await ReportProgress(onProgressChange, onProcessChange, 100, "Resource sync complete");

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
        Func<string?, Task>? onProcessChange,
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
            $"Discovering filesystem resources from {pendingResourceMarks.Count} marks...");

        try
        {
            var discovered = await fsResolver.DiscoverFromMarks(pendingResourceMarks, ct);

            var entries = discovered.Select(d => new DiscoveredResourceEntry
            {
                EffectivePath = d.Path,
                Source = ResourceSource.FileSystem,
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
        Func<string?, Task>? onProcessChange,
        CancellationToken ct)
    {
        var sourceName = resolver.Source.ToString();

        await ReportProgress(onProgressChange, onProcessChange, 10,
            $"Discovering {sourceName} resources...");

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
            LocalPath = r.Path
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
                EnsureSourceLink(existingByPath, entry.Source, entry.SourceKey);
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
                linkedResource.Path = newPath;
                linkedResource.UpdatedAt = DateTime.Now;
            }
            EnsureSourceLink(linkedResource, entry.Source, entry.SourceKey);
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
                EnsureSourceLink(markerResource, entry.Source, entry.SourceKey);
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
                SourceLinks = [new ResourceSourceLink { Source = entry.Source, SourceKey = entry.SourceKey }]
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
                fileCreatedAt = fi.CreationTime;
                fileModifiedAt = fi.LastWriteTime;
            }
            else
            {
                var di = new DirectoryInfo(entry.EffectivePath);
                fileCreatedAt = di.CreationTime;
                fileModifiedAt = di.LastWriteTime;
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
                SourceLinks = [new ResourceSourceLink { Source = entry.Source, SourceKey = entry.SourceKey }]
            };
        }
    }

    #endregion

    #region Post-Discovery Actions

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
        if (source == ResourceSource.FileSystem) return;

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
    /// Deletes resources that were previously discovered by this source but are no longer present.
    /// For FileSystem: uses effect tracking (handled by PathMarkSyncService).
    /// For resolvers: compares discovered keys with existing source links.
    /// </summary>
    private async Task<int> DeleteOrphanedResources(
        ResourceSource source,
        List<DiscoveredResourceEntry> discoveredEntries,
        ResourceSyncContext ctx,
        CancellationToken ct)
    {
        // For FileSystem, orphan detection is handled via effect tracking in PathMarkSyncService
        if (source == ResourceSource.FileSystem) return 0;

        // For resolver sources, resources marked as Absent that have no other active source links
        // could potentially be deleted, but we keep them as Absent for now.
        // This is a conservative approach - actual deletion can be triggered by user action.
        return 0;
    }

    /// <summary>
    /// Rebuilds parent-child relationships for ALL resources based on path hierarchy.
    /// </summary>
    private async Task RebuildAllParentChildRelationships(CancellationToken ct)
    {
        var allResources = await _resourceService.GetAll();
        var pathToResource = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in allResources)
        {
            if (!string.IsNullOrEmpty(r.Path))
                pathToResource[r.Path] = r;
        }

        var changedResources = new Dictionary<int, Resource>();

        foreach (var resource in allResources)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(resource.Path)) continue;

            // Walk up directory tree to find closest parent resource
            var parentPath = Path.GetDirectoryName(resource.Path);
            int? parentResourceId = null;

            while (!string.IsNullOrEmpty(parentPath))
            {
                if (pathToResource.TryGetValue(parentPath, out var parentResource))
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
        }

        if (changedResources.Count > 0)
        {
            await _resourceService.AddOrPutRange(changedResources.Values.ToList());
            _logger.LogInformation("[ResourceSync] Updated parent-child for {Count} resources", changedResources.Count);
        }

        await _resourceService.RefreshParentTag();
    }

    /// <summary>
    /// Marks all property and media library path marks that cover the new resource paths as pending.
    /// This triggers PathMarkSyncService to sync properties for the new resources.
    /// </summary>
    private async Task MarkRelatedPathMarksAsPending(List<string> newResourcePaths, CancellationToken ct)
    {
        if (newResourcePaths.Count == 0) return;

        var allMarks = await _pathMarkService.GetAll();
        var propertyAndMlMarks = allMarks
            .Where(m => m.Type is PathMarkType.Property or PathMarkType.MediaLibrary
                         && m.SyncStatus == PathMarkSyncStatus.Synced)
            .ToList();

        var markIdsToMarkPending = new HashSet<int>();

        foreach (var mark in propertyAndMlMarks)
        {
            ct.ThrowIfCancellationRequested();

            var markPath = mark.Path.StandardizePath();
            if (string.IsNullOrEmpty(markPath)) continue;

            foreach (var resourcePath in newResourcePaths)
            {
                var standardizedResourcePath = resourcePath.StandardizePath();
                if (string.IsNullOrEmpty(standardizedResourcePath)) continue;

                // Check if the resource is under the mark's path
                if (standardizedResourcePath.StartsWith(markPath + InternalOptions.DirSeparator, StringComparison.OrdinalIgnoreCase) ||
                    standardizedResourcePath.Equals(markPath, StringComparison.OrdinalIgnoreCase))
                {
                    markIdsToMarkPending.Add(mark.Id);
                    break;
                }
            }
        }

        if (markIdsToMarkPending.Count > 0)
        {
            await _pathMarkService.MarkAsPendingBatch(markIdsToMarkPending);
            _logger.LogInformation("[ResourceSync] Marked {Count} property/media-library marks as pending for new resources",
                markIdsToMarkPending.Count);
        }
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

    private static void EnsureSourceLink(Resource resource, ResourceSource source, string sourceKey)
    {
        resource.SourceLinks ??= [];
        if (!resource.SourceLinks.Any(l => l.Source == source && l.SourceKey == sourceKey))
        {
            resource.SourceLinks.Add(new ResourceSourceLink { Source = source, SourceKey = sourceKey });
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
            var content = System.Text.Json.JsonSerializer.Serialize(new { ids = new[] { resourceId } });
            await File.WriteAllTextAsync(markerPath, content);
            File.SetAttributes(markerPath, File.GetAttributes(markerPath) | FileAttributes.Hidden);
        }
        catch
        {
            // Ignore marker creation errors
        }
    }

    private async Task ReportProgress(Func<int, Task>? onProgressChange, Func<string?, Task>? onProcessChange,
        int progress, string? process)
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
}

/// <summary>
/// Cached context for resource sync operations.
/// </summary>
internal class ResourceSyncContext
{
    public List<Resource> AllResources { get; private set; } = new();
    public Dictionary<string, Resource> PathToResource { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, Resource> IdToResource { get; } = new();
    public Dictionary<(ResourceSource Source, string SourceKey), int> SourceLinkToResourceId { get; } = new();
    public Dictionary<int, HashSet<(ResourceSource Source, string SourceKey)>> ResourceIdToSourceLinks { get; } = new();

    public void BuildIndexes(List<Resource> allResources, List<ResourceSourceLink>? allSourceLinks = null)
    {
        AllResources = allResources;
        PathToResource.Clear();
        IdToResource.Clear();
        SourceLinkToResourceId.Clear();
        ResourceIdToSourceLinks.Clear();

        foreach (var resource in allResources)
        {
            if (!string.IsNullOrEmpty(resource.Path))
            {
                PathToResource[resource.Path] = resource;
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
    public bool ParentChildRebuilt { get; set; }
    public bool PathMarksMarkedPending { get; set; }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// The two path-derived structures that have to be recomputed whenever a resource path appears,
/// disappears or moves: the parent-child tree, and which path marks still describe reality.
/// <para>
/// Both used to live inside <see cref="ResourceSyncService"/> as private helpers. They are here so
/// that anything changing a path — a sync pass, a move, a materialization — can fix up the
/// structure without depending on the sync service, and so no two of those callers can end up
/// depending on each other.
/// </para>
/// </summary>
public class ResourceStructureService
{
    private readonly IResourceService _resourceService;
    private readonly IPathMarkService _pathMarkService;
    private readonly ILogger<ResourceStructureService> _logger;

    public ResourceStructureService(
        IResourceService resourceService,
        IPathMarkService pathMarkService,
        ILogger<ResourceStructureService> logger)
    {
        _resourceService = resourceService;
        _pathMarkService = pathMarkService;
        _logger = logger;
    }

    /// <summary>
    /// Rebuilds parent-child relationships for ALL resources based on path hierarchy.
    /// </summary>
    public async Task RebuildParentChildRelationships(CancellationToken ct)
    {
        var allResources = await _resourceService.GetAll();
        var resourcesWithPaths = allResources.Where(r => r.HasLocalPath)
            .Select(r => (Resource: r, Path: r.Path.StandardizePath()!))
            .Where(r => !string.IsNullOrEmpty(r.Path))
            .ToList();
        var pathToResource = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);

        foreach (var (resource, path) in resourcesWithPaths)
        {
            pathToResource[path] = resource;
        }

        var changedResources = new Dictionary<int, Resource>();

        foreach (var (resource, path) in resourcesWithPaths)
        {
            ct.ThrowIfCancellationRequested();

            // Internal paths always use '/'. OS path APIs would turn these back into
            // backslashes on Windows, making them miss the standardized lookup keys.
            var parentPath = GetParentPath(path);
            int? parentResourceId = null;

            while (!string.IsNullOrEmpty(parentPath))
            {
                if (pathToResource.TryGetValue(parentPath, out var parentResource))
                {
                    parentResourceId = parentResource.Id;
                    break;
                }

                parentPath = GetParentPath(parentPath);
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
            _logger.LogInformation("[ResourceStructure] Updated parent-child for {Count} resources",
                changedResources.Count);
        }

        await _resourceService.RefreshParentTag();
    }

    private static string? GetParentPath(string path)
    {
        var separator = path.LastIndexOf(InternalOptions.DirSeparator);
        if (separator < 0) return null;
        if (separator == 0) return path.Length > 1 ? "/" : null;
        if (separator == 2 && path[1] == ':')
            return path.Length > 3 ? path[..3] : null;

        // A UNC share is a root, just like '/' or 'D:/'. Do not walk into the
        // server name or let resources from different shares become related.
        if (path.StartsWith(InternalOptions.UncPathPrefix, StringComparison.Ordinal))
        {
            var shareSeparator = path.IndexOf(InternalOptions.DirSeparator, 2);
            if (shareSeparator < 0 || separator <= shareSeparator) return null;
        }

        return path[..separator];
    }

    /// <summary>
    /// Marks every property and media library path mark covering one of <paramref name="paths"/> as
    /// pending, which is what makes <see cref="IPathMarkSyncService"/> sync properties for those
    /// paths on its next pass.
    /// </summary>
    public async Task MarkPathMarksAsPendingForPaths(IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return;

        var standardizedPaths = paths
            .Select(p => p.StandardizePath())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
        if (standardizedPaths.Count == 0) return;

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

            foreach (var resourcePath in standardizedPaths)
            {
                // Check if the resource is under the mark's path
                if (resourcePath!.StartsWith(markPath + InternalOptions.DirSeparator,
                        StringComparison.OrdinalIgnoreCase) ||
                    resourcePath.Equals(markPath, StringComparison.OrdinalIgnoreCase))
                {
                    markIdsToMarkPending.Add(mark.Id);
                    break;
                }
            }
        }

        if (markIdsToMarkPending.Count > 0)
        {
            await _pathMarkService.MarkAsPendingBatch(markIdsToMarkPending);
            _logger.LogInformation(
                "[ResourceStructure] Marked {Count} property/media-library marks as pending for {PathCount} paths",
                markIdsToMarkPending.Count, standardizedPaths.Count);
        }
    }
}

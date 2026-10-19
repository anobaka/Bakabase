using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Resolvers;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

public partial class ResourceSyncService
{
    /// <summary>
    /// A filesystem source is absent only when its reachable parent was fully enumerated.
    /// Disallowed/unreachable parents and partial recursive walks are not evidence that
    /// a resource went away. Keep the resource and all its metadata in either case.
    /// </summary>
    private async Task UpdatePathMarkResourceStatuses(IReadOnlyList<PathMark> snapshots, CancellationToken ct)
    {
        if (snapshots.Count == 0) return;
        var storage = GetRequiredService<IUserStoragePolicy>();
        var guard = GetRequiredService<ResourceMoveGuard>();
        var allMarks = await _pathMarkService.GetAll();
        var currentById = allMarks.ToDictionary(mark => mark.Id);
        var completed = snapshots.Where(snapshot => currentById.TryGetValue(snapshot.Id, out var current) &&
            MatchesAvailabilitySnapshot(current, snapshot)).Select(snapshot => currentById[snapshot.Id]).ToList();
        if (completed.Count == 0) return;

        var extensionGroups = (await GetRequiredService<IExtensionGroupService>().GetAll()).ToDictionary(group => group.Id);
        var resourceMarkRevisions = allMarks.Where(mark => mark.Type == PathMarkType.Resource && !mark.IsDeleted &&
            mark.SyncStatus != PathMarkSyncStatus.PendingDelete).ToDictionary(mark => mark.Id);
        var boundaryPaths = resourceMarkRevisions.Values
            .Where(mark => JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson) is { IsResourceBoundary: true })
            .Select(mark => mark.Path).ToArray();
        var rules = new List<(PathMark Mark, Func<string, bool, bool> Matches)>();
        foreach (var mark in completed)
        {
            ct.ThrowIfCancellationRequested();
            if (!CanReadDirectory(mark.Path, storage)) continue;
            var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson);
            if (config == null) continue;
            var extensions = new List<string>(config.Extensions ?? []);
            var missingGroup = false;
            foreach (var id in config.ExtensionGroupIds ?? [])
            {
                if (!extensionGroups.TryGetValue(id, out var group)) { missingGroup = true; break; }
                extensions.AddRange(group.Extensions ?? []);
            }
            if (missingGroup) continue;
            config.Extensions = extensions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            rules.Add((mark, ResourceMarkPathMatcher.Create(mark.Path, config, boundaryPaths)));
        }
        if (rules.Count == 0) return;
        var referencedGroupIds = rules.SelectMany(rule =>
                JsonConvert.DeserializeObject<ResourceMarkConfig>(rule.Mark.ConfigJson)!.ExtensionGroupIds ?? [])
            .Distinct().ToArray();

        // Source ownership and current paths are reloaded after discovery and any marker
        // recovery. A historical source key does not override the resource's current path.
        var resources = await _resourceService.GetAll();
        var links = (await _sourceLinkService.GetAll()).ToLookup(link => link.ResourceId);
        var candidates = resources.Where(resource => resource.HasLocalPath &&
            links[resource.Id].Any() && links[resource.Id].All(link => link.Source == ResourceSource.PathMark) &&
            !guard.IsResourceLocked(resource.Id) && rules.Any(rule => rule.Matches(resource.Path!, resource.IsFile)))
            .ToList();
        var parentEntries = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
        var db = GetRequiredService<BakabaseDbContext>();
        var changed = new List<int>();
        try
        {
            foreach (var resource in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var path = resource.Path!.StandardizePath()!;
                var parent = Path.GetDirectoryName(path)?.StandardizePath();
                if (string.IsNullOrEmpty(parent) || !storage.IsPathAllowed(parent)) continue;
                if (!parentEntries.TryGetValue(parent, out var entries))
                {
                    entries = ReadDirectoryEntries(parent, storage, ct);
                    parentEntries[parent] = entries;
                }
                if (entries == null) continue;
                ResourceStatus status;
                if (!entries.Contains(path)) status = ResourceStatus.Absent;
                else
                {
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        var isDirectory = (attributes & FileAttributes.Directory) != 0;
                        status = resource.IsFile != isDirectory ? ResourceStatus.Active : ResourceStatus.Absent;
                    }
                    catch (Exception error) when (IsStorageError(error)) { continue; }
                }
                if (status == resource.Status || guard.IsResourceLocked(resource.Id)) continue;
                var matching = rules.Where(rule => rule.Matches(path, resource.IsFile)).Select(rule => rule.Mark).ToArray();
                if (!matching.Any(mark => CanReadDirectory(mark.Path, storage)) ||
                    !CanReadDirectory(parent, storage) || guard.IsResourceLocked(resource.Id)) continue;
                // Keep the rule dependencies and the status write in one database transaction.
                // Changing an extension group does not change the referring mark's revision;
                // newly added/edited boundaries can also invalidate a previously matched path.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var currentMarks = await db.PathMarks.AsNoTracking().Where(mark => mark.Type == PathMarkType.Resource &&
                    !mark.IsDeleted && mark.SyncStatus != PathMarkSyncStatus.PendingDelete).ToListAsync(ct);
                if (currentMarks.Count != resourceMarkRevisions.Count || currentMarks.Any(current =>
                        !resourceMarkRevisions.TryGetValue(current.Id, out var revision) || current.Path != revision.Path ||
                        current.ConfigJson != revision.ConfigJson || current.Priority != revision.Priority ||
                        current.SyncStatus != revision.SyncStatus || current.UpdatedAt != revision.UpdatedAt)) continue;
                if (referencedGroupIds.Length > 0)
                {
                    var currentGroups = await db.ExtensionGroups.AsNoTracking()
                        .Where(group => referencedGroupIds.Contains(group.Id)).ToListAsync(ct);
                    if (currentGroups.Count != referencedGroupIds.Length || currentGroups.Any(current =>
                            !(current.ToDomainModel().Extensions ?? []).SetEquals(extensionGroups[current.Id].Extensions ?? []))) continue;
                }
                var verified = matching.FirstOrDefault(mark => currentMarks.Any(current => current.Id == mark.Id &&
                    current.Type == PathMarkType.Resource && !current.IsDeleted && current.SyncStatus == PathMarkSyncStatus.Synced &&
                    current.Path == mark.Path && current.ConfigJson == mark.ConfigJson && current.Priority == mark.Priority &&
                    current.UpdatedAt == mark.UpdatedAt));
                if (verified == null || guard.IsResourceLocked(resource.Id)) continue;

                // Write status alone, guarded by current source ownership/path and the exact
                // completed mark revision. Never let an old scan write back a pre-move path.
                var updated = await db.ResourcesV2.Where(row => row.Id == resource.Id && row.Path == path &&
                        row.IsFile == resource.IsFile && row.Status == resource.Status &&
                        db.ResourceSourceLinks.Any(link => link.ResourceId == row.Id && link.Source == ResourceSource.PathMark) &&
                        !db.ResourceSourceLinks.Any(link => link.ResourceId == row.Id && link.Source != ResourceSource.PathMark) &&
                        db.PathMarks.Any(mark => mark.Id == verified.Id && mark.Type == PathMarkType.Resource &&
                            !mark.IsDeleted && mark.SyncStatus == PathMarkSyncStatus.Synced && mark.Path == verified.Path &&
                            mark.ConfigJson == verified.ConfigJson && mark.Priority == verified.Priority && mark.UpdatedAt == verified.UpdatedAt))
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, status)
                        .SetProperty(row => row.UpdateDt, DateTime.Now), ct);
                await transaction.CommitAsync(ct);
                if (updated > 0) changed.Add(resource.Id);
            }
        }
        finally
        {
            if (changed.Count > 0)
            {
                GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>().ClearCache();
                GetRequiredService<IResourceDataChangeEventPublisher>().PublishResourcesChanged(changed);
                _logger.LogInformation("[ResourceSync] Updated filesystem availability for {Count} PathMark resources", changed.Count);
            }
        }
    }

    private static bool MatchesAvailabilitySnapshot(PathMark current, PathMark snapshot) =>
        current.Type == PathMarkType.Resource && !current.IsDeleted && current.SyncStatus == PathMarkSyncStatus.Synced &&
        current.Path == snapshot.Path && current.ConfigJson == snapshot.ConfigJson && current.Priority == snapshot.Priority;

    private static bool CanReadDirectory(string path, IUserStoragePolicy storage)
    {
        try
        {
            return storage.IsPathAllowed(path) && (File.GetAttributes(path) & FileAttributes.Directory) != 0;
        }
        catch (Exception error) when (IsStorageError(error)) { return false; }
    }

    private static HashSet<string>? ReadDirectoryEntries(string parent, IUserStoragePolicy storage, CancellationToken ct)
    {
        if (!CanReadDirectory(parent, storage)) return null;
        try
        {
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFileSystemEntries(parent))
            {
                ct.ThrowIfCancellationRequested();
                entries.Add(path.StandardizePath()!);
            }
            return entries;
        }
        catch (Exception error) when (IsStorageError(error)) { return null; }
    }

    private static bool IsStorageError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}

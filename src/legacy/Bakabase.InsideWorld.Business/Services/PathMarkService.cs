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
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

public class PathMarkService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, PathMarkDbModel, int> orm,
    IServiceProvider serviceProvider,
    IBOptions<ResourceOptions> resourceOptions
) : ScopedService(serviceProvider), IPathMarkService where TDbContext : DbContext
{
    private async Task TriggerImmediateSyncIfEnabled(int markId)
    {
        if (resourceOptions.Value.SynchronizationOptions?.SyncMarksImmediately != true) return;

        try
        {
            // Use IServiceProvider to avoid circular dependency
            var syncService = serviceProvider.GetService<IPathMarkSyncService>();
            if (syncService != null)
            {
                await syncService.StartSyncImmediate([markId]);
            }
        }
        catch
        {
            // Ignore sync trigger errors
        }
    }

    public async Task<List<PathMark>> GetAll(Expression<Func<PathMarkDbModel, bool>>? filter = null)
    {
        var dbModels = filter != null
            ? await orm.GetAll(filter)
            : await orm.GetAll();

        return dbModels.Select(d => d.ToDomainModel()).ToList();
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
        mark.Path = mark.Path.StandardizePath()!;
        mark.CreatedAt = DateTime.UtcNow;
        mark.UpdatedAt = DateTime.UtcNow;
        mark.SyncStatus = PathMarkSyncStatus.Pending;

        var dbModel = mark.ToDbModel();
        await orm.Add(dbModel);
        orm.DbContext.Detach(dbModel);
        mark.Id = dbModel.Id;

        // Trigger immediate sync if enabled
        await TriggerImmediateSyncIfEnabled(mark.Id);

        return mark;
    }

    public async Task<List<PathMark>> AddRange(List<PathMark> marks)
    {
        var now = DateTime.UtcNow;
        foreach (var mark in marks)
        {
            mark.Path = mark.Path.StandardizePath()!;
            mark.CreatedAt = now;
            mark.UpdatedAt = now;
            mark.SyncStatus = PathMarkSyncStatus.Pending;
        }

        var dbModels = marks.Select(m => m.ToDbModel()).ToList();
        await orm.AddRange(dbModels);

        for (int i = 0; i < marks.Count; i++)
        {
            marks[i].Id = dbModels[i].Id;
        }

        return marks;
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

    public async Task MarkAsSyncing(int id)
    {
        var dbModel = await orm.GetByKey(id);
        if (dbModel != null)
        {
            dbModel.SyncStatus = PathMarkSyncStatus.Syncing;
            dbModel.UpdatedAt = DateTime.UtcNow;
            await orm.Update(dbModel);
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
        }
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
        }
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
        return PreviewMatchedPathsDetailed(request);
    }

    private List<string> GetMatchingPathsForResourceMark(string rootPath, ResourceMarkConfig config)
    {
        var matchedPaths = new List<string>();

        try
        {
            if (config.MatchMode == PathMatchMode.Layer)
            {
                if (config.Layer == null) return matchedPaths;

                var layer = config.Layer.Value;
                if (layer == -1)
                {
                    // All layers - recursively get all directories/files
                    var entries = GetAllEntries(rootPath, config.FsTypeFilter, config.Extensions);
                    matchedPaths.AddRange(entries);
                }
                else
                {
                    // Specific layer
                    var entries = GetEntriesAtLayer(rootPath, layer, config.FsTypeFilter, config.Extensions);
                    matchedPaths.AddRange(entries);
                }
            }
            else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
            {
                var regex = new Regex(config.Regex, RegexOptions.IgnoreCase);
                var entries = GetAllEntries(rootPath, config.FsTypeFilter, config.Extensions);

                foreach (var entry in entries)
                {
                    var relativePath = entry.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (regex.IsMatch(relativePath))
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

    public Task<List<PathMarkPreviewResult>> PreviewMatchedPathsDetailed(PathMarkPreviewRequest request)
    {
        var rootPath = request.Path.StandardizePath()!;
        var results = new List<PathMarkPreviewResult>();

        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(results);
        }

        if (request.Type == PathMarkType.Resource)
        {
            var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(request.ConfigJson);
            if (config == null)
            {
                return Task.FromResult(results);
            }

            var matchedPaths = GetMatchingPathsForResourceMark(rootPath, config);
            var rootSegments = rootPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var matchedPath in matchedPaths)
            {
                var matchedSegments = matchedPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                // Calculate resource layer index (0-based from root)
                int? resourceLayerIndex = null;
                string? resourceSegmentName = null;

                if (config.MatchMode == PathMatchMode.Layer && config.Layer.HasValue)
                {
                    var layer = config.Layer.Value;
                    if (layer > 0)
                    {
                        // Layer is 1-based (1 = first level subdirectory)
                        var targetIndex = rootSegments.Length + layer - 1;
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
                else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
                {
                    // For regex, find which segment matches
                    var relativePath = matchedPath.Substring(rootPath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var relativeSegments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                        StringSplitOptions.RemoveEmptyEntries);

                    var regex = new Regex(config.Regex, RegexOptions.IgnoreCase);
                    for (int i = 0; i < relativeSegments.Length; i++)
                    {
                        if (regex.IsMatch(relativeSegments[i]))
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

                results.Add(new PathMarkPreviewResult
                {
                    Path = matchedPath,
                    ResourceLayerIndex = resourceLayerIndex,
                    ResourceSegmentName = resourceSegmentName
                });
            }
        }
        else if (request.Type == PathMarkType.Property)
        {
            var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(request.ConfigJson);
            if (config == null)
            {
                return Task.FromResult(results);
            }

            // Get matched paths (same logic as Resource, but we need to find paths that match the property mark's match mode)
            var matchedPaths = GetMatchingPathsForPropertyMark(rootPath, config);

            foreach (var matchedPath in matchedPaths)
            {
                string? propertyValue = null;

                if (config.ValueType == PropertyValueType.Fixed)
                {
                    propertyValue = config.FixedValue?.ToString();
                }
                else if (config.ValueType == PropertyValueType.Dynamic)
                {
                    var matchedSegments = matchedPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                        StringSplitOptions.RemoveEmptyEntries);
                    var rootSegments = rootPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                        StringSplitOptions.RemoveEmptyEntries);

                    if (config.MatchMode == PathMatchMode.Layer && config.ValueLayer.HasValue)
                    {
                        var valueLayer = config.ValueLayer.Value;
                        // 0 = matched item itself
                        if (valueLayer == 0)
                        {
                            var relativePath = matchedPath.Substring(rootPath.Length)
                                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            propertyValue = Path.GetFileName(relativePath);
                            if (string.IsNullOrEmpty(propertyValue))
                            {
                                propertyValue = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                                    StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                            }
                        }
                        else
                        {
                            // Positive = forward, negative = backward
                            var targetIndex = rootSegments.Length + valueLayer - 1;
                            if (targetIndex >= 0 && targetIndex < matchedSegments.Length)
                            {
                                propertyValue = matchedSegments[targetIndex];
                            }
                        }
                    }
                    else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.ValueRegex))
                    {
                        var relativePath = matchedPath.Substring(rootPath.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var regex = new Regex(config.ValueRegex, RegexOptions.IgnoreCase);
                        var match = regex.Match(relativePath);
                        if (match.Success && match.Groups.Count > 1)
                        {
                            propertyValue = match.Groups[1].Value;
                        }
                        else if (match.Success)
                        {
                            propertyValue = match.Value;
                        }
                    }
                }

                results.Add(new PathMarkPreviewResult
                {
                    Path = matchedPath,
                    PropertyValue = propertyValue
                });
            }
        }

        return Task.FromResult(results);
    }

    private List<string> GetMatchingPathsForPropertyMark(string rootPath, PropertyMarkConfig config)
    {
        var matchedPaths = new List<string>();

        try
        {
            if (config.MatchMode == PathMatchMode.Layer)
            {
                if (config.Layer == null) return matchedPaths;

                var layer = config.Layer.Value;
                if (layer == -1)
                {
                    // All layers - recursively get all directories/files
                    var entries = GetAllEntries(rootPath, null, null);
                    matchedPaths.AddRange(entries);
                }
                else
                {
                    // Specific layer
                    var entries = GetEntriesAtLayer(rootPath, layer, null, null);
                    matchedPaths.AddRange(entries);
                }
            }
            else if (config.MatchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(config.Regex))
            {
                var regex = new Regex(config.Regex, RegexOptions.IgnoreCase);
                var entries = GetAllEntries(rootPath, null, null);

                foreach (var entry in entries)
                {
                    var relativePath = entry.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (regex.IsMatch(relativePath))
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

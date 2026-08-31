using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Newtonsoft.Json;
using ResourceDomain = Bakabase.Abstractions.Models.Domain.Resource;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

public class ResourceMoveService(
    BakabaseDbContext db,
    IResourceService resourceService,
    IPathMarkService pathMarkService,
    IPathMarkSyncService pathMarkSyncService,
    IResourceSourceLinkService resourceSourceLinkService,
    ResourceSyncService resourceSyncService,
    BTaskManager taskManager,
    ResourceMoveGuard guard,
    IBakabaseLocalizer localizer,
    ILogger<ResourceMoveService> logger) : IResourceMoveService
{
    private DbSet<ResourceMoveRecordDbModel> Records => db.Set<ResourceMoveRecordDbModel>();

    private static string BuildTaskId(string batchId) => $"MoveResources:{batchId}";

    private static string BuildDestPath(string standardizedDestDir, string sourcePath) =>
        $"{standardizedDestDir}{InternalOptions.DirSeparator}{Path.GetFileName(sourcePath)}".StandardizePath()!;

    /// <summary>
    /// A selected resource sitting under another selected resource moves with its ancestor;
    /// keep only the top-level ones.
    /// </summary>
    private static List<ResourceDomain> CollapseNestedSelection(IReadOnlyCollection<ResourceDomain> resources) =>
        resources
            .Where(r => !string.IsNullOrEmpty(r.Path) &&
                        !resources.Any(o => o.Id != r.Id && r.Path.IsPathUnder(o.Path)))
            .ToList();

    public async Task<SingletonResponse<string>> CreateBatch(int[] resourceIds, string destDir)
    {
        var standardizedDestDir = destDir.StandardizePath();
        if (string.IsNullOrEmpty(standardizedDestDir) || !Directory.Exists(standardizedDestDir))
        {
            return SingletonResponseBuilder<string>.Build(ResponseCode.InvalidPayloadOrOperation,
                localizer.PathIsNotFound(destDir));
        }

        var resources = await resourceService.GetByKeys(resourceIds.Distinct().ToArray());
        var missingIds = resourceIds.Except(resources.Select(r => r.Id)).ToArray();
        if (missingIds.Any())
        {
            return SingletonResponseBuilder<string>.Build(ResponseCode.NotFound,
                localizer.Resource_NotFound(missingIds.First()));
        }

        var topLevel = CollapseNestedSelection(resources);
        if (!topLevel.Any())
        {
            return SingletonResponseBuilder<string>.BadRequest;
        }

        foreach (var r in topLevel)
        {
            if (standardizedDestDir.IsPathEqualOrUnder(r.Path))
            {
                return SingletonResponseBuilder<string>.Build(ResponseCode.InvalidPayloadOrOperation,
                    localizer.ResourceMove_DestinationInsideSource(r.Path, standardizedDestDir));
            }

            var destPath = BuildDestPath(standardizedDestDir, r.Path);
            if (string.Equals(destPath, r.Path.StandardizePath(), StringComparison.OrdinalIgnoreCase))
            {
                return SingletonResponseBuilder<string>.Build(ResponseCode.InvalidPayloadOrOperation,
                    localizer.ResourceMove_DestinationExists(destPath));
            }
        }

        var batchId = Guid.NewGuid().ToString("N")[..12];
        var now = DateTime.Now;
        var records = topLevel.Select(r => new ResourceMoveRecordDbModel
        {
            BatchId = batchId,
            ResourceId = r.Id,
            SourcePath = r.Path.StandardizePath()!,
            DestPath = BuildDestPath(standardizedDestDir, r.Path),
            Status = ResourceMoveRecordStatus.Pending,
            CreatedAt = now
        }).ToList();

        var reservedPaths = records.Select(r => r.SourcePath).Concat(records.Select(r => r.DestPath))
            .Distinct().ToList();
        var affectedResourceIds = await ComputeAffectedResourceIds(reservedPaths,
            records.Select(r => r.ResourceId));
        if (!guard.TryReserve(batchId, affectedResourceIds, reservedPaths, out var conflictPath))
        {
            return SingletonResponseBuilder<string>.Build(ResponseCode.Conflict,
                localizer.ResourceMove_ResourcesAreBeingMoved(conflictPath!));
        }

        try
        {
            Records.AddRange(records);
            await db.SaveChangesAsync();
            await EnqueueBatchTask(batchId, records.Count, standardizedDestDir, affectedResourceIds);
        }
        catch
        {
            guard.Release(batchId);
            throw;
        }

        return new SingletonResponse<string>(batchId);
    }

    /// <summary>
    /// Everything a batch touches: the moved resources themselves, every resource under a
    /// source or destination path (they move, or gain siblings), and every resource a source
    /// or destination path sits under (their content changes while files are in flight).
    /// </summary>
    private async Task<HashSet<int>> ComputeAffectedResourceIds(IReadOnlyCollection<string> reservedPaths,
        IEnumerable<int> seedResourceIds)
    {
        var allDbModels = await resourceService.GetAllDbModels();
        var affected = seedResourceIds.ToHashSet();
        foreach (var dbModel in allDbModels)
        {
            if (reservedPaths.Any(p => dbModel.Path.IsPathEqualOrUnder(p) || p.IsPathEqualOrUnder(dbModel.Path)))
            {
                affected.Add(dbModel.Id);
            }
        }

        return affected;
    }

    private async Task EnqueueBatchTask(string batchId, int recordCount, string destDir,
        IReadOnlyCollection<int> affectedResourceIds)
    {
        await taskManager.Enqueue(BTaskBuilder.Create(BuildTaskId(batchId))
            .Named(() => localizer.MoveResource())
            .Describe(() => localizer.ResourceMove_TaskDescription(recordCount, destDir))
            .InterruptionMessage(() => localizer.MessageOnInterruption_MoveResources())
            .OfType(BTaskType.MoveResources)
            .OfResourceType(BTaskResourceType.Resource)
            .ForResources(affectedResourceIds.Cast<object>().ToArray())
            // Serialize move batches among themselves and against the path-mark sync pipeline —
            // both walk the same resources and paths.
            .ConflictsWith("MoveResources", "SyncResources", "SyncPathMarks")
            .ReplaceIfExists()
            .Run(async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IResourceMoveService>();
                await service.ExecuteBatch(batchId, args);
            }));
    }

    public async Task ExecuteBatch(string batchId, BTaskArgs args)
    {
        var markIdsToSync = new HashSet<int>();
        var anySucceeded = false;
        try
        {
            var records = await Records.Where(r => r.BatchId == batchId &&
                                                   r.Status == ResourceMoveRecordStatus.Pending)
                .OrderBy(r => r.Id).ToListAsync();
            var total = records.Count;
            var done = 0;

            foreach (var record in records)
            {
                try
                {
                    await args.YieldAsync();
                }
                catch (OperationCanceledException)
                {
                    await MarkRemainingCancelled(records.Skip(done));
                    throw;
                }

                // Whether an earlier attempt of THIS record already ran the physical primitives —
                // that is what decides whether an existing destination is our own partial output
                // (safe to merge into) or foreign content (a hard conflict).
                var resume = record.PhysicalMoveStarted;

                record.Status = ResourceMoveRecordStatus.Moving;
                record.StartedAt = DateTime.Now;
                record.Attempts++;
                record.Error = null;
                await db.SaveChangesAsync();

                var doneSnapshot = done;

                async Task OnProgress(int p) =>
                    await args.UpdateTask(t => t.Percentage = (doneSnapshot * 100 + p) / total);

                await args.UpdateTask(t => t.Process = $"{done + 1}/{total} {Path.GetFileName(record.SourcePath)}");

                try
                {
                    await MoveRecordFiles(record, resume, OnProgress, args);
                    await ApplyPostMoveFixups(record, markIdsToSync);
                    anySucceeded = true;

                    record.Status = ResourceMoveRecordStatus.Succeeded;
                    record.CompletedAt = DateTime.Now;
                    await db.SaveChangesAsync();
                }
                catch (OperationCanceledException)
                {
                    record.Status = ResourceMoveRecordStatus.Cancelled;
                    record.CompletedAt = DateTime.Now;
                    await db.SaveChangesAsync();
                    await MarkRemainingCancelled(records.Skip(done + 1));
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to move resource {ResourceId} from {Source} to {Dest}",
                        record.ResourceId, record.SourcePath, record.DestPath);
                    record.Status = ResourceMoveRecordStatus.Failed;
                    record.Error = e is BTaskException bte ? bte.BriefMessage ?? e.Message : e.Message;
                    record.CompletedAt = DateTime.Now;
                    await db.SaveChangesAsync();
                }

                done++;
                await args.UpdateTask(t => t.Percentage = done * 100 / total);
            }

            var failed = records.Count(r => r.Status == ResourceMoveRecordStatus.Failed);
            if (failed > 0)
            {
                var firstError = records.First(r => r.Status == ResourceMoveRecordStatus.Failed).Error;
                throw new BTaskException($"{failed}/{total}: {firstError}",
                    string.Join(Environment.NewLine,
                        records.Where(r => r.Status == ResourceMoveRecordStatus.Failed)
                            .Select(r => $"{r.SourcePath}: {r.Error}")));
            }
        }
        finally
        {
            guard.Release(batchId);

            try
            {
                if (anySucceeded)
                {
                    await resourceSyncService.RebuildParentChildRelationships(CancellationToken.None);
                }

                if (markIdsToSync.Any())
                {
                    await pathMarkSyncService.EnqueueSync(markIdsToSync.ToArray());
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Post-move batch cleanup failed for batch {BatchId}", batchId);
            }
        }
    }

    private async Task MarkRemainingCancelled(IEnumerable<ResourceMoveRecordDbModel> records)
    {
        var now = DateTime.Now;
        foreach (var record in records.Where(r => r.Status is ResourceMoveRecordStatus.Pending
                     or ResourceMoveRecordStatus.Moving))
        {
            record.Status = ResourceMoveRecordStatus.Cancelled;
            record.CompletedAt = now;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Physically move the record's files. Retry-safe via <paramref name="resume"/> (a previous
    /// attempt of this record already ran the primitives): only then may an existing destination
    /// be treated as our own partial output — merged into with overwrite, or, when the source is
    /// fully gone, taken as an already-completed move. Without the flag an existing destination
    /// is foreign content and the record fails instead of touching it.
    /// </summary>
    private async Task MoveRecordFiles(ResourceMoveRecordDbModel record, bool resume, Func<int, Task> onProgress,
        BTaskArgs args)
    {
        var src = record.SourcePath;
        var dest = record.DestPath;
        var srcIsDirectory = Directory.Exists(src);
        var srcIsFile = File.Exists(src);
        var destExists = Directory.Exists(dest) || File.Exists(dest);

        if (!srcIsDirectory && !srcIsFile)
        {
            if (destExists && resume)
            {
                // Already moved by an earlier (interrupted) attempt of this record.
                await onProgress(100);
                return;
            }

            throw new BTaskException(localizer.ResourceMove_SourceMissing(src),
                localizer.ResourceMove_SourceMissing(src));
        }

        if (destExists && !resume)
        {
            throw new BTaskException(localizer.ResourceMove_DestinationExists(dest),
                localizer.ResourceMove_DestinationExists(dest));
        }

        // Merging only ever targets our own partial output from a previous attempt.
        var overwrite = destExists && resume;

        // The Bootstrap primitives pick rename-vs-copy from Path.GetPathRoot, which on POSIX
        // makes every cross-mount move take the rename path and die with EXDEV (a common
        // docker layout: /downloads and /media as separate volumes). Detect the real mounts
        // and copy+delete explicitly when they differ.
        var crossFileSystem = ResourceMoveFileSystem.AreOnSameFileSystem(src, dest) == false;

        // The Bootstrap directory primitives reject any destination whose raw path string
        // starts with the source's — the check is unanchored, so the legitimate sibling-prefix
        // shape (/media/a → /media/abc/a) trips it. Genuine containment was rejected at batch
        // creation, so hitting this here is always the false positive.
        var siblingPrefixCollision = srcIsDirectory && dest.StartsWith(src, StringComparison.OrdinalIgnoreCase);
        if (siblingPrefixCollision && (crossFileSystem || overwrite))
        {
            // No copy-based fallback exists for these without reimplementing the primitives.
            throw new BTaskException(localizer.ResourceMove_SiblingPrefixUnsupported(src, dest),
                localizer.ResourceMove_SiblingPrefixUnsupported(src, dest));
        }

        // Persist before the first byte moves; from here on an existing destination on retry
        // is our own output.
        if (!record.PhysicalMoveStarted)
        {
            record.PhysicalMoveStarted = true;
            await db.SaveChangesAsync();
        }

        if (siblingPrefixCollision)
        {
            // Same-filesystem and the destination is free — a native rename does the whole job.
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Directory.Move(src, dest);
            await onProgress(100);
            return;
        }

        if (srcIsDirectory)
        {
            try
            {
                if (crossFileSystem)
                {
                    // Source stays fully intact until the copy has completely landed, so a
                    // failure here can drop the half-written destination and keep retry clean.
                    try
                    {
                        await DirectoryUtils.CopyAsync(src, dest, overwrite, onProgress, args.PauseToken,
                            args.CancellationToken);
                    }
                    catch
                    {
                        if (!resume)
                        {
                            // First attempt only: the destination holds nothing but our
                            // half-written copy. On resume it may hold files whose source
                            // copies are already deleted — never destroy those.
                            ResourceMoveFileSystem.TryDeleteCopyDebris(dest);
                        }

                        throw;
                    }

                    Directory.Delete(src, true);
                }
                else
                {
                    await DirectoryUtils.MoveAsync(src, dest, overwrite, onProgress, args.PauseToken,
                        args.CancellationToken);
                }
            }
            catch
            {
                // A failed move leaves the destination's directory skeleton behind; a tree
                // with no files in it is pure debris and would break the retry probe.
                ResourceMoveFileSystem.TryDeleteFilelessDirectoryTree(dest);
                throw;
            }
        }
        else
        {
            if (crossFileSystem)
            {
                // FileUtils.CopyAsync creates the destination directory itself and deletes
                // its partial destination on failure.
                await FileUtils.CopyAsync(src, dest, overwrite, onProgress, args.PauseToken,
                    args.CancellationToken);
                File.Delete(src);
            }
            else
            {
                await FileUtils.MoveAsync(src, dest, overwrite, onProgress, args.PauseToken,
                    args.CancellationToken);
            }
        }
    }

    /// <summary>
    /// After the files landed: rewrite DB paths of the resource and every descendant resource,
    /// invalidate the path-valued filesystem caches, rekey path-keyed source links, and collect
    /// the ancestor property/media-library marks of both locations for re-sync.
    /// </summary>
    private async Task ApplyPostMoveFixups(ResourceMoveRecordDbModel record, ISet<int> markIdsToSync)
    {
        var allDbModels = await resourceService.GetAllDbModels();
        var newPathsByResourceId = new Dictionary<int, string> { [record.ResourceId] = record.DestPath };
        var oldPathsByResourceId = new Dictionary<int, string> { [record.ResourceId] = record.SourcePath };

        foreach (var dbModel in allDbModels)
        {
            if (dbModel.Id != record.ResourceId && dbModel.Path.IsPathUnder(record.SourcePath))
            {
                var standardized = dbModel.Path.StandardizePath()!;
                var suffix = standardized[record.SourcePath.Length..];
                newPathsByResourceId[dbModel.Id] = record.DestPath + suffix;
                oldPathsByResourceId[dbModel.Id] = standardized;
            }
        }

        var affectedIds = newPathsByResourceId.Keys.ToArray();

        await resourceService.ChangePath(affectedIds, newPathsByResourceId);

        // Invalidate AFTER the DB path update so the cover provider cannot re-cache "no cover"
        // against the stale path in between.
        await resourceService.DeleteResourceCacheByResourceIdsAndCacheType(affectedIds, ResourceCacheType.Covers);
        await resourceService.DeleteResourceCacheByResourceIdsAndCacheType(affectedIds,
            ResourceCacheType.PlayableFiles);

        // Path-mark source links carry the resource path as their key; rekey them so the next
        // sync recognizes the moved resource instead of spawning a duplicate at the new path.
        var linksByResourceId = await resourceSourceLinkService.GetByResourceIdsGrouped(affectedIds);
        foreach (var (resourceId, links) in linksByResourceId)
        {
            var oldPath = oldPathsByResourceId.GetValueOrDefault(resourceId);
            var newPath = newPathsByResourceId.GetValueOrDefault(resourceId);
            if (oldPath == null || newPath == null)
            {
                continue;
            }

            foreach (var link in links.Where(l => l.Source == ResourceSource.PathMark &&
                                                  string.Equals(l.SourceKey.StandardizePath(), oldPath,
                                                      StringComparison.OrdinalIgnoreCase)))
            {
                link.SourceKey = newPath;
                await resourceSourceLinkService.Update(link);
            }
        }

        // Re-apply path marks covering the old or the new location (R8): flag their ancestor
        // property/media-library marks; the batch enqueues one sync for all of them at the end.
        var allMarks = await pathMarkService.GetAll();
        foreach (var mark in allMarks.Where(m => m.Type is PathMarkType.Property or PathMarkType.MediaLibrary))
        {
            if (record.SourcePath.IsPathEqualOrUnder(mark.Path) ||
                record.DestPath.IsPathEqualOrUnder(mark.Path))
            {
                markIdsToSync.Add(mark.Id);
            }
        }
    }

    public async Task<SingletonResponse<ResourceMovePreviewViewModel>> Preview(int[] resourceIds, string destDir)
    {
        var standardizedDestDir = destDir.StandardizePath();
        if (string.IsNullOrEmpty(standardizedDestDir))
        {
            return SingletonResponseBuilder<ResourceMovePreviewViewModel>.BadRequest;
        }

        var resources = await resourceService.GetByKeys(resourceIds.Distinct().ToArray());
        var topLevel = CollapseNestedSelection(resources);

        var relevantMarks =
            (await pathMarkService.GetAll(m => !m.IsDeleted,
                PathMarkAdditionalItem.Property | PathMarkAdditionalItem.MediaLibrary))
            .Where(m => m.Type is PathMarkType.Property or PathMarkType.MediaLibrary)
            .ToList();

        var vm = new ResourceMovePreviewViewModel();
        foreach (var resource in topLevel)
        {
            var destPath = BuildDestPath(standardizedDestDir, resource.Path);
            var item = new ResourceMovePreviewViewModel.Item
            {
                ResourceId = resource.Id,
                SourcePath = resource.Path.StandardizePath()!,
                DestPath = destPath,
                DestConflict = Directory.Exists(destPath) || File.Exists(destPath),
                DestInsideSource = standardizedDestDir.IsPathEqualOrUnder(resource.Path)
            };

            foreach (var mark in relevantMarks.Where(m => destPath.IsPathEqualOrUnder(m.Path)))
            {
                var effect = new ResourceMovePreviewViewModel.MarkEffect
                {
                    MarkId = mark.Id,
                    Type = mark.Type,
                    MarkPath = mark.Path
                };

                switch (mark.Type)
                {
                    case PathMarkType.Property:
                    {
                        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson);
                        if (config == null)
                        {
                            continue;
                        }

                        effect.WillApply = PathMarkMatchEvaluator.Matches(config.MatchMode, config.Layer,
                            config.Regex, config.ApplyScope, mark.Path, destPath);
                        effect.PropertyName = mark.Property?.Name;
                        effect.IsDynamic = config.ValueType == PropertyValueType.Dynamic;
                        effect.FixedValue =
                            config.ValueType == PropertyValueType.Fixed ? config.FixedValue?.ToString() : null;
                        break;
                    }
                    case PathMarkType.MediaLibrary:
                    {
                        var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(mark.ConfigJson);
                        if (config == null)
                        {
                            continue;
                        }

                        effect.WillApply = PathMarkMatchEvaluator.Matches(config.MatchMode, config.Layer,
                            config.Regex, config.ApplyScope, mark.Path, destPath);
                        effect.IsDynamic = config.ValueType == PropertyValueType.Dynamic;
                        effect.MediaLibraryName = mark.MediaLibrary?.Name;
                        break;
                    }
                }

                item.Effects.Add(effect);
            }

            vm.Items.Add(item);
        }

        return new SingletonResponse<ResourceMovePreviewViewModel>(vm);
    }

    public async Task<List<ResourceMoveRecordDbModel>> GetRecords(int maxCount = 100)
    {
        return await Records.OrderByDescending(r => r.Id).Take(Math.Clamp(maxCount, 1, 1000)).ToListAsync();
    }

    public async Task<BaseResponse> Retry(int recordId)
    {
        var record = await Records.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record == null)
        {
            return BaseResponseBuilder.NotFound;
        }

        if (record.Status is ResourceMoveRecordStatus.Pending or ResourceMoveRecordStatus.Moving ||
            taskManager.IsPending(BuildTaskId(record.BatchId)))
        {
            return BaseResponseBuilder.BuildBadRequest(localizer.ResourceMove_RecordInProgress());
        }

        // Files may sit on either side after a partial move — reserve both subtrees.
        var reservedPaths = new[] { record.SourcePath, record.DestPath };
        var affectedResourceIds = await ComputeAffectedResourceIds(reservedPaths, [record.ResourceId]);

        if (!guard.TryReserve(record.BatchId, affectedResourceIds, reservedPaths, out var conflictPath))
        {
            return BaseResponseBuilder.Build(ResponseCode.Conflict,
                localizer.ResourceMove_ResourcesAreBeingMoved(conflictPath!));
        }

        try
        {
            record.Status = ResourceMoveRecordStatus.Pending;
            record.Error = null;
            record.CompletedAt = null;
            await db.SaveChangesAsync();

            await EnqueueBatchTask(record.BatchId, 1, Path.GetDirectoryName(record.DestPath).StandardizePath()!,
                affectedResourceIds);
        }
        catch
        {
            guard.Release(record.BatchId);
            throw;
        }

        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> DeleteRecord(int recordId)
    {
        var record = await Records.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record == null)
        {
            return BaseResponseBuilder.NotFound;
        }

        if (record.Status is ResourceMoveRecordStatus.Pending or ResourceMoveRecordStatus.Moving)
        {
            return BaseResponseBuilder.BuildBadRequest(localizer.ResourceMove_RecordInProgress());
        }

        Records.Remove(record);
        await db.SaveChangesAsync();
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> DeleteInactiveRecords()
    {
        await Records.Where(r => r.Status != ResourceMoveRecordStatus.Pending &&
                                 r.Status != ResourceMoveRecordStatus.Moving)
            .ExecuteDeleteAsync();
        return BaseResponseBuilder.Ok;
    }

    public async Task MarkInterruptedOnStartup()
    {
        var interruptedError = localizer.ResourceMove_InterruptedByRestart();
        var movingCount = await Records.Where(r => r.Status == ResourceMoveRecordStatus.Moving)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, _ => ResourceMoveRecordStatus.Interrupted)
                .SetProperty(r => r.CompletedAt, _ => DateTime.Now)
                .SetProperty(r => r.Error, _ => interruptedError));

        var pendingError = localizer.ResourceMove_InterruptedBeforeStart();
        var pendingCount = await Records.Where(r => r.Status == ResourceMoveRecordStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, _ => ResourceMoveRecordStatus.Interrupted)
                .SetProperty(r => r.CompletedAt, _ => DateTime.Now)
                .SetProperty(r => r.Error, _ => pendingError));

        if (movingCount + pendingCount > 0)
        {
            logger.LogInformation(
                "Marked {Moving} moving and {Pending} pending resource move records as interrupted on startup",
                movingCount, pendingCount);
        }
    }
}

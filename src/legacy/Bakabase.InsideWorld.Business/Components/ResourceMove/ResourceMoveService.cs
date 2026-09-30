using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Events;
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

public partial class ResourceMoveService(
    BakabaseDbContext db,
    IResourceService resourceService,
    IPathMarkService pathMarkService,
    IPathMarkSyncService pathMarkSyncService,
    ResourceSyncService resourceSyncService,
    BTaskManager taskManager,
    ResourceMoveGuard guard,
    IResourceMovePanelSettings panelSettings,
    IEnumerable<IResourceSourceMoveHandler> sourceHandlers,
    IEnumerable<IResourceMoveExecutor> executors,
    IResourceDataChangeEventPublisher resourceChangePublisher,
    IServiceScopeFactory scopeFactory,
    IBakabaseLocalizer localizer,
    ILogger<ResourceMoveService> logger) : IResourceMoveService
{
    private readonly Dictionary<ResourceSource, IResourceSourceMoveHandler> _sourceHandlers = sourceHandlers.ToDictionary(h => h.Source);
    private readonly Dictionary<string, IResourceMoveExecutor> _executors = executors.ToDictionary(e => e.Id);

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
            .Where(r => r.HasLocalPath &&
                        !resources.Any(o => o.Id != r.Id && r.Path!.IsPathUnder(o.Path)))
            .ToList();

    private static readonly SemaphoreSlim ControlGate = new(1, 1);
    private static readonly HashSet<string> Executing = [];
    private static bool IsRetained(ResourceMoveRecordStatus s) => s is ResourceMoveRecordStatus.Pending
        or ResourceMoveRecordStatus.Moving or ResourceMoveRecordStatus.WaitingForConflict or ResourceMoveRecordStatus.NeedsRecovery;

    public async Task<SingletonResponse<ResourceMoveBatchViewModel>> CreateBatch(int[] resourceIds, string destDir,
        ResourceMoveRequestOptions? options = null)
    {
        await ControlGate.WaitAsync();
        try { return await CreateBatchCore(resourceIds, destDir, options ?? new()); }
        finally { ControlGate.Release(); }
    }

    private async Task<SingletonResponse<ResourceMoveBatchViewModel>> CreateBatchCore(int[] resourceIds, string destDir,
        ResourceMoveRequestOptions options)
    {
        var standardizedDestDir = destDir.StandardizePath();
        if (string.IsNullOrEmpty(standardizedDestDir))
            return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.InvalidPayloadOrOperation, localizer.PathIsNotFound(destDir));
        if (options.ConflictPolicy is not ("inherit" or "ask" or "overwrite"))
            return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.InvalidPayloadOrOperation, "Unknown conflict policy");
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(new { ids = resourceIds.Distinct().OrderBy(x => x), dest = standardizedDestDir,
                options.Origin, options.SourceTabId, options.DestinationId, options.ConflictPolicy }))));
        if (!string.IsNullOrWhiteSpace(options.IdempotencyKey))
        {
            var existing = await Records.AsNoTracking().FirstOrDefaultAsync(r => r.IdempotencyKey == options.IdempotencyKey);
            if (existing != null)
                return existing.RequestFingerprint == fingerprint
                    ? new(new ResourceMoveBatchViewModel(existing.BatchId, 0))
                    : SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.Conflict, "Idempotency key belongs to a different move request");
        }
        if (!Directory.Exists(standardizedDestDir))
            return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.InvalidPayloadOrOperation, localizer.PathIsNotFound(destDir));
        var resources = await resourceService.GetByKeys(resourceIds.Distinct().ToArray());
        var missingIds = resourceIds.Except(resources.Select(r => r.Id)).ToArray();
        if (missingIds.Any()) return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.NotFound,
            localizer.Resource_NotFound(missingIds.First()));
        var topLevel = CollapseNestedSelection(resources);
        var skippedResourceCount = resources.Count(r => !r.HasLocalPath);
        if (!topLevel.Any()) return SingletonResponseBuilder<ResourceMoveBatchViewModel>.BadRequest;
        foreach (var r in topLevel)
        {
            if (standardizedDestDir.IsPathEqualOrUnder(r.Path))
                return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.InvalidPayloadOrOperation,
                    localizer.ResourceMove_DestinationInsideSource(r.Path, standardizedDestDir));
            if (string.Equals(BuildDestPath(standardizedDestDir, r.Path), r.Path.StandardizePath(), StringComparison.OrdinalIgnoreCase))
                return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.InvalidPayloadOrOperation,
                    localizer.ResourceMove_DestinationExists(r.Path));
        }
        if (topLevel.GroupBy(r => BuildDestPath(standardizedDestDir, r.Path), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.Conflict, "batchDestination: selected resources share the same destination");
        var sourcePlans = new Dictionary<int, ResourceMoveExecutionPlan>();
        var preview = await PreviewCore(resourceIds, destDir, sourcePlans);
        if (options.ExpectedPreviewFingerprint != null && preview.Data?.PreviewFingerprint != options.ExpectedPreviewFingerprint)
            return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.Conflict, "previewChanged: resource or destination changed; confirm a fresh preview");
        var sourceDenial = preview.Data?.Items.FirstOrDefault(i => i.UnavailableReason != null);
        if (sourceDenial != null)
            return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.Conflict, sourceDenial.UnavailableReason!);
        var batchId = Guid.NewGuid().ToString("N");
        var now = DateTime.Now;
        var records = topLevel.Select(r => new ResourceMoveRecordDbModel
        {
            BatchId = batchId, ResourceId = r.Id, SourcePath = r.Path.StandardizePath()!,
            DestPath = BuildDestPath(standardizedDestDir, r.Path), Status = ResourceMoveRecordStatus.Pending,
            CreatedAt = now, Origin = options.Origin, SourceTabId = options.SourceTabId,
            SourceTabName = options.SourceTabName, DestinationId = options.DestinationId,
            DestinationName = options.DestinationName, ConflictPolicy = options.ConflictPolicy,
        }).ToList();
        records[0].IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey;
        records[0].RequestFingerprint = fingerprint;
        foreach (var r in records)
        {
            var item = preview.Data!.Items.Single(i => i.ResourceId == r.ResourceId);
            if (item.SourcePath != r.SourcePath || item.DestPath != r.DestPath)
                return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.Conflict, "sourceLocationChanged");
            var snapshot = item.CoveredResources.ToDictionary(m => m.ResourceId, m => m.Path);
            snapshot[r.ResourceId] = r.SourcePath;
            r.SourceResourcePathsJson = JsonConvert.SerializeObject(snapshot);
            GetExecutor(sourcePlans[r.ResourceId]);
            r.ExecutionPlanJson = JsonConvert.SerializeObject(sourcePlans[r.ResourceId]);
        }
        var reservedPaths = records.SelectMany(r => new[] {r.SourcePath, r.DestPath}).Distinct().ToArray();
        var affected = await ComputeAffectedResourceIds(reservedPaths, records.Select(r => r.ResourceId));
        if (!guard.TryReserve(batchId, affected, reservedPaths, out var conflictPath))
            return SingletonResponseBuilder<ResourceMoveBatchViewModel>.Build(ResponseCode.Conflict,
                localizer.ResourceMove_ResourcesAreBeingMoved(conflictPath!));
        foreach (var record in records) record.ReservedResourceIdsJson = JsonConvert.SerializeObject(affected);
        try
        {
            Records.AddRange(records);
            await db.SaveChangesAsync();
            await EnqueueBatchTask(batchId, records.Count, standardizedDestDir, affected);
        }
        catch
        {
            // Persisted rows without an executor must stay recoverable and reserved.
            if (records.Any(r => r.Id != 0))
            {
                foreach (var record in records) { record.Status = ResourceMoveRecordStatus.Interrupted; record.ErrorCode = "enqueueFailed"; }
                await db.SaveChangesAsync();
            }
            guard.Release(batchId);
            throw;
        }
        return new(new ResourceMoveBatchViewModel(batchId, skippedResourceCount));
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
            // Serialize move batches among themselves, against the path-mark sync pipeline
            // (both walk the same resources and paths), and against the File Mover's recurring
            // MoveFiles task, whose configured source/target directories may overlap the very
            // files this batch is relocating.
            .ConflictsWith("MoveResources", "SyncResources", "SyncPathMarks", "MoveFiles")
            .ReplaceIfExists()
            .OnStop(async () =>
            {
                await using var stopScope = scopeFactory.CreateAsyncScope();
                await stopScope.ServiceProvider.GetRequiredService<IResourceMoveService>().CancelBatch(batchId);
            })
            .Run(async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IResourceMoveService>();
                await service.ExecuteBatch(batchId, args);
            }));
    }

    public async Task ExecuteBatch(string batchId, BTaskArgs args)
    {
        await ControlGate.WaitAsync();
        try
        {
            if (!Executing.Add(batchId)) throw new BTaskSuspendedException("Move executor is already running");
            guard.Resume(batchId);
        }
        finally { ControlGate.Release(); }
        var markIdsToSync = new HashSet<int>();
        var anySucceeded = false;
        try
        {
            var records = await Records.Where(r => r.BatchId == batchId).OrderBy(r => r.Id).ToListAsync();
            var total = records.Count;
            foreach (var record in records)
            {
                await ControlGate.WaitAsync();
                try
                {
                    await db.Entry(record).ReloadAsync();
                    if (record.Status != ResourceMoveRecordStatus.Pending) continue;
                    if (record.CancelRequested)
                    {
                        record.Status = record.PhysicalMoveStarted ? ResourceMoveRecordStatus.NeedsRecovery : ResourceMoveRecordStatus.Cancelled;
                        record.CompletedAt = DateTime.Now;
                        await db.SaveChangesAsync();
                        continue;
                    }
                    record.Status = ResourceMoveRecordStatus.Moving;
                    record.StartedAt = DateTime.Now;
                    record.Attempts++;
                    record.Error = null;
                    record.ErrorCode = null;
                    await db.SaveChangesAsync();
                }
                finally { ControlGate.Release(); }
                var completed = records.Count(r => r.Status is ResourceMoveRecordStatus.Succeeded or ResourceMoveRecordStatus.Skipped
                    or ResourceMoveRecordStatus.Failed or ResourceMoveRecordStatus.Cancelled);
                async Task Progress(int p) => await args.UpdateTask(t => t.Percentage = (completed * 100 + p) / Math.Max(1, total));
                await args.UpdateTask(t => t.Process = $"{completed + 1}/{total} {Path.GetFileName(record.SourcePath)}");
                try
                {
                    // Domain cancellation is intentionally checked at resource boundaries. Only
                    // shutdown/unexpected executor cancellation may interrupt the physical phase.
                    await args.YieldAsync();
                    await ValidateSourceSnapshot(record);
                    await CheckIdentityConflict(record);
                    if (record.PhysicalMoveStarted && record.MoveJournalJson == null)
                        throw new ResourceMoveConflictException("legacyRecovery", record.DestPath, false,
                            "Legacy move has no ownership journal; verify the source and destination manually before recovery");
                    var executionPlan = await GetExecutionPlan(record);
                    await ValidateSourcePlan(record, executionPlan);
                    var executor = GetExecutor(executionPlan);
                    var executionState = ExecutionState(record);
                    await executor.ExecuteOrResumeAsync(executionState, async () =>
                        {
                            record.MoveJournalJson = executionState.MoveJournalJson;
                            record.PhysicalMoveStarted = executionState.PhysicalMoveStarted;
                            await db.SaveChangesAsync();
                        }, Progress, args.PauseToken, args.CancellationToken,
                        (path, stamp) => IsOverwriteAuthorized(record, path, stamp));
                    await ApplyPostMoveFixups(record, markIdsToSync, executionPlan);
                    record.Status = ResourceMoveRecordStatus.Succeeded;
                    record.CompletedAt = DateTime.Now;
                    record.Error = null;
                    record.ErrorCode = null;
                    record.ConflictKind = null;
                    record.CanOverwrite = false;
                    await db.SaveChangesAsync();
                    anySucceeded = true;
                    try { resourceChangePublisher.PublishResourcesChanged((await GetSourceSnapshot(record)).Keys); }
                    catch (Exception e) { logger.LogError(e, "Failed to publish completed move for {RecordId}", record.Id); }
                    try { executor.Cleanup(ExecutionState(record)); }
                    catch (Exception e) { logger.LogWarning(e, "Could not clean successful move staging for {RecordId}", record.Id); }
                }
                catch (ResourceMoveConflictException conflict)
                {
                    record.Status = ResourceMoveRecordStatus.WaitingForConflict;
                    record.ConflictKind = conflict.Kind;
                    record.ConflictPath = conflict.Path;
                    record.ConflictFingerprint = conflict.Fingerprint;
                    record.ConflictVersion++;
                    record.CanOverwrite = conflict.CanOverwrite;
                    record.ErrorCode = conflict.Kind;
                    record.Error = conflict.Message;
                    await db.SaveChangesAsync();
                }
                catch (OperationCanceledException)
                {
                    record.Status = record.PhysicalMoveStarted ? ResourceMoveRecordStatus.NeedsRecovery : ResourceMoveRecordStatus.Interrupted;
                    record.ErrorCode = "interrupted";
                    record.Error = "Move interrupted; recover before moving to another destination";
                    record.CompletedAt = DateTime.Now;
                    await db.SaveChangesAsync();
                    await Records.Where(r => r.BatchId == batchId && r.Status == ResourceMoveRecordStatus.Pending)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ResourceMoveRecordStatus.Interrupted));
                    if (record.PhysicalMoveStarted)
                        throw new BTaskSuspendedException("Move recovery required", preserveOnCancellation: true);
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to move resource {ResourceId}", record.ResourceId);
                    record.Status = record.PhysicalMoveStarted ? ResourceMoveRecordStatus.NeedsRecovery : ResourceMoveRecordStatus.Failed;
                    record.ErrorCode = e is ResourceSourceMoveException sourceError ? sourceError.ReasonCode :
                        record.PhysicalMoveStarted ? "recoveryRequired" : "moveFailed";
                    record.Error = e is BTaskException bte ? bte.BriefMessage ?? e.Message : e.Message;
                    record.CompletedAt = DateTime.Now;
                    await db.SaveChangesAsync();
                }
            }
            if (await Records.AsNoTracking().AnyAsync(r => r.BatchId == batchId && r.CancelRequested))
                await CancelUnstartedRecords(batchId);
            var latest = await Records.AsNoTracking().Where(r => r.BatchId == batchId).ToListAsync();
            if (latest.Any(r => r.Status is ResourceMoveRecordStatus.WaitingForConflict or ResourceMoveRecordStatus.NeedsRecovery))
                throw new BTaskSuspendedException("Move needs attention",
                    preserveOnCancellation: latest.Any(r => r.Status == ResourceMoveRecordStatus.NeedsRecovery));
            if (latest.Any(r => r.CancelRequested)) await taskManager.MarkCancelled(BuildTaskId(batchId));
            if (latest.Any(r => r.Status == ResourceMoveRecordStatus.Failed))
                throw new BTaskException("Some resources could not be moved", latest.First(r => r.Status == ResourceMoveRecordStatus.Failed).Error);
        }
        finally
        {
            await ControlGate.WaitAsync();
            try
            {
                Executing.Remove(batchId);
                var retained = await Records.AsNoTracking().AnyAsync(r => r.BatchId == batchId &&
                    (r.Status == ResourceMoveRecordStatus.Pending || r.Status == ResourceMoveRecordStatus.Moving ||
                     r.Status == ResourceMoveRecordStatus.WaitingForConflict || r.Status == ResourceMoveRecordStatus.NeedsRecovery));
                if (retained) guard.Retain(batchId); else guard.Release(batchId);
            }
            finally { ControlGate.Release(); }
            try
            {
                if (anySucceeded) await resourceSyncService.RebuildParentChildRelationships(CancellationToken.None);
                if (markIdsToSync.Any()) await pathMarkSyncService.EnqueueSync(markIdsToSync.ToArray());
            }
            catch (Exception e) { logger.LogError(e, "Post-move cleanup failed for {BatchId}", batchId); }
        }
    }

    private bool IsOverwriteAuthorized(ResourceMoveRecordDbModel record, string path, string fingerprint)
    {
        var decisions = JsonConvert.DeserializeObject<Dictionary<string, string>>(record.ConflictDecisionsJson ?? "{}")!;
        if (decisions.GetValueOrDefault(path) == fingerprint) return true;
        var allowed = record.ConflictPolicy == "overwrite" || record.ConflictPolicy == "inherit" &&
            record.Origin == "move-panel" && panelSettings.AutoOverwrite;
        if (allowed)
        {
            // Captures the policy used without promoting an inherited decision into a permanent
            // individual authorization (switching the panel preference off remains effective).
            record.PolicyAuditJson = JsonConvert.SerializeObject(new { policy = record.ConflictPolicy, panel = panelSettings.AutoOverwrite,
                path, fingerprint, at = DateTime.UtcNow });
        }
        return allowed;
    }

    private async Task CheckIdentityConflict(ResourceMoveRecordDbModel record)
    {
        var all = await resourceService.GetAllDbModels();
        var ownIds = (await GetSourceSnapshot(record)).Keys.ToHashSet();
        var other = all.FirstOrDefault(r => !ownIds.Contains(r.Id) && r.Path.IsPathEqualOrUnder(record.DestPath));
        if (other != null)
            throw new ResourceMoveConflictException("resourceIdentity", record.DestPath, false, $"resource:{other.Id}:{other.Path}");
    }

    private async Task<Dictionary<int, string>> GetSourceSnapshot(ResourceMoveRecordDbModel record)
    {
        if (record.SourceResourcePathsJson != null)
            return JsonConvert.DeserializeObject<Dictionary<int,string>>(record.SourceResourcePathsJson)!;
        var all = await resourceService.GetAllDbModels();
        var snapshot = all.Where(r => r.Id == record.ResourceId || r.Path.IsPathUnder(record.SourcePath))
            .ToDictionary(r => r.Id, r => r.Id == record.ResourceId ? record.SourcePath : r.Path.StandardizePath()!);
        record.SourceResourcePathsJson = JsonConvert.SerializeObject(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }

    private async Task ValidateSourceSnapshot(ResourceMoveRecordDbModel record, bool validateRecovery = false)
    {
        var snapshot = await GetSourceSnapshot(record);
        if (record.PhysicalMoveStarted && !validateRecovery)
        {
            var ids = snapshot.Keys.ToArray();
            var allCurrentResources = await db.ResourcesV2.AsNoTracking().ToListAsync();
            var currentPaths = allCurrentResources.Where(r => ids.Contains(r.Id)).ToDictionary(r => r.Id, r => r.Path);
            if (currentPaths.Count != snapshot.Count || snapshot.Any(p =>
                !currentPaths.TryGetValue(p.Key, out var path) || (path.StandardizePath() != p.Value &&
                    path.StandardizePath() != (p.Key == record.ResourceId ? record.DestPath : record.DestPath + p.Value[record.SourcePath.Length..]))) ||
                allCurrentResources.Any(r => !ids.Contains(r.Id) && r.Path.IsPathEqualOrUnder(record.SourcePath)))
                throw new ResourceSourceMoveException("sourceLocationChanged", "Resource locations changed after the move began; restore the recorded association before recovery");
            return;
        }
        var all = await resourceService.GetAllDbModels();
        var current = all.Where(r => r.Id == record.ResourceId || r.Path.IsPathUnder(record.SourcePath))
            .ToDictionary(r => r.Id, r => r.Path.StandardizePath());
        if (current.Count != snapshot.Count || snapshot.Any(p => !current.TryGetValue(p.Key, out var value) || value != p.Value))
            throw new InvalidOperationException("sourceChanged: resource locations changed; create a new move from a fresh preview");
    }

    private async Task CancelUnstartedRecords(string batchId)
    {
        var pending = await Records.Where(r => r.BatchId == batchId && (r.Status == ResourceMoveRecordStatus.Pending ||
            r.Status == ResourceMoveRecordStatus.WaitingForConflict)).ToListAsync();
        foreach (var r in pending)
        {
            r.Status = r.PhysicalMoveStarted ? ResourceMoveRecordStatus.NeedsRecovery : ResourceMoveRecordStatus.Cancelled;
            r.CompletedAt = DateTime.Now;
            if (r.PhysicalMoveStarted) r.ErrorCode = "recoveryRequired";
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// After the files landed: rewrite DB paths of the resource and every descendant resource,
    /// invalidate the path-valued filesystem caches, rekey path-keyed source links, and collect
    /// the ancestor property/media-library marks of both locations for re-sync.
    /// </summary>
    private async Task ApplyPostMoveFixups(ResourceMoveRecordDbModel record, ISet<int> markIdsToSync,
        ResourceMoveExecutionPlan executionPlan)
    {
        var oldPathsByResourceId = await GetSourceSnapshot(record);
        var newPathsByResourceId = oldPathsByResourceId.ToDictionary(p => p.Key,
            p => p.Key == record.ResourceId ? record.DestPath : record.DestPath + p.Value[record.SourcePath.Length..]);

        var affectedIds = newPathsByResourceId.Keys.ToArray();

        var pathChange = await resourceService.ChangePath(affectedIds, newPathsByResourceId, publishChange: false, expectedPaths: oldPathsByResourceId);
        if (pathChange.Code != 0) throw new ResourceSourceMoveException("sourceLocationChanged", "Could not commit moved resource paths");

        await ApplySourcePlan(record, executionPlan);

        // Invalidate AFTER the DB path update so the cover provider cannot re-cache "no cover"
        // against the stale path in between.
        await resourceService.DeleteResourceCacheByResourceIdsAndCacheType(affectedIds, ResourceCacheType.Covers);
        await resourceService.DeleteResourceCacheByResourceIdsAndCacheType(affectedIds,
            ResourceCacheType.PlayableFiles);

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

    public Task<SingletonResponse<ResourceMovePreviewViewModel>> Preview(int[] resourceIds, string destDir) =>
        PreviewCore(resourceIds, destDir);

    private async Task<SingletonResponse<ResourceMovePreviewViewModel>> PreviewCore(int[] resourceIds, string destDir,
        Dictionary<int, ResourceMoveExecutionPlan>? plans = null)
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

        var selectedIds = resourceIds.ToHashSet();
        var allDbModels = await resourceService.GetAllDbModels();

        var vm = new ResourceMovePreviewViewModel
        {
            SkippedResourceIds = resources.Where(r => !r.HasLocalPath).Select(r => r.Id).ToArray(),
            DuplicateDestinationPaths = topLevel.GroupBy(r => BuildDestPath(standardizedDestDir, r.Path), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToArray()
        };
        foreach (var resource in resources.Where(r => !r.HasLocalPath))
            vm.ExcludedResources.Add(new()
            {
                ResourceId = resource.Id, DisplayName = resource.DisplayName ?? resource.FileName ?? resource.Id.ToString(),
                Path = resource.Path, ReasonCode = "noLocalFiles"
            });
        var sourceFingerprints = new Dictionary<int, string>();
        foreach (var resource in topLevel)
        {
            var destPath = BuildDestPath(standardizedDestDir, resource.Path);
            var item = new ResourceMovePreviewViewModel.Item
            {
                ResourceId = resource.Id,
                SourcePath = resource.Path.StandardizePath()!,
                DestPath = destPath,
                DestConflict = Directory.Exists(destPath) || File.Exists(destPath),
                DestInsideSource = standardizedDestDir.IsPathEqualOrUnder(resource.Path),
                // Everything inside this resource's directory rides along, selected or not —
                // the confirmation lists them so nothing moves silently.
                CoveredResources = allDbModels
                    .Where(m => m.Id != resource.Id && m.Path.IsPathUnder(resource.Path))
                    .OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new ResourceMovePreviewViewModel.CoveredResource
                    {
                        ResourceId = m.Id,
                        Path = m.Path.StandardizePath()!,
                        WasSelected = selectedIds.Contains(m.Id)
                    })
                    .ToList()
            };

            var sourceSnapshot = item.CoveredResources.ToDictionary(r => r.ResourceId, r => r.Path);
            sourceSnapshot[resource.Id] = item.SourcePath;
            var sourcePlan = await BuildSourcePlan(resource.Id, item.SourcePath, item.DestPath, sourceSnapshot);
            plans?.Add(resource.Id, sourcePlan.Plan);
            sourceFingerprints[resource.Id] = JsonConvert.SerializeObject(sourcePlan.Plan);
            item.UnavailableReason = sourcePlan.ReasonCode ?? (guard.IsResourceLocked(resource.Id) ? "resourceLocked" :
                !Directory.Exists(standardizedDestDir) ? "destinationMissing" :
                !Directory.Exists(item.SourcePath) && !File.Exists(item.SourcePath) ? "sourceMissing" :
                item.DestInsideSource ? "destinationInsideSource" :
                item.SourcePath == item.DestPath ? "alreadyAtDestination" : null);
            if (item.UnavailableReason != null)
                vm.ExcludedResources.Add(new()
                {
                    ResourceId = resource.Id, DisplayName = resource.DisplayName ?? resource.FileName ?? resource.Id.ToString(),
                    Path = resource.Path, ReasonCode = item.UnavailableReason,
                    BlockingResourceIds = sourcePlan.BlockingIds.Length == 0 ? null : sourcePlan.BlockingIds
                });
            if (vm.DuplicateDestinationPaths.Contains(destPath)) item.ConflictKind = "batchDestination";
            else if (allDbModels.Any(m => m.Id != resource.Id && !m.Path.IsPathUnder(resource.Path) && m.Path.IsPathEqualOrUnder(destPath)))
                item.ConflictKind = "resourceIdentity";
            else if (item.DestConflict)
            {
                item.ConflictKind = Directory.Exists(item.SourcePath) == Directory.Exists(destPath) ? "destinationExists" : "typeMismatch";
                item.CanOverwrite = item.ConflictKind == "destinationExists";
            }

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

                        effect.WillApply = PathMarkMatchEvaluator.Matches(config, mark.Path, destPath);
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

        vm.PreviewFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(vm.Items.Where(i => i.UnavailableReason == null)
                .OrderBy(i => i.ResourceId).Select(i => new { item = i, sourcePlan = sourceFingerprints[i.ResourceId] })))));
        return new SingletonResponse<ResourceMovePreviewViewModel>(vm);
    }

    public async Task<List<ResourceMoveRecordDbModel>> GetRecords(int maxCount = 100) =>
        await Records.AsNoTracking().OrderByDescending(r => r.Id).Take(Math.Clamp(maxCount, 1, 1000)).ToListAsync();

    public async Task<ResourceMoveBatchDetailViewModel?> GetBatch(string batchId)
    {
        var rows = await Records.AsNoTracking().Where(r => r.BatchId == batchId).OrderBy(r => r.Id).ToListAsync();
        return rows.Count == 0 ? null : BuildBatch(rows);
    }

    public async Task<List<ResourceMoveBatchDetailViewModel>> GetBatches(string? origin = null, string? sourceTabId = null,
        bool activeOnly = false, int skip = 0, int take = 100)
    {
        var query = Records.AsNoTracking().AsQueryable();
        if (origin != null) query = query.Where(r => r.Origin == origin);
        if (sourceTabId != null) query = query.Where(r => r.SourceTabId == sourceTabId);
        if (activeOnly) query = query.Where(r => r.Status == ResourceMoveRecordStatus.Pending || r.Status == ResourceMoveRecordStatus.Moving ||
            r.Status == ResourceMoveRecordStatus.WaitingForConflict || r.Status == ResourceMoveRecordStatus.NeedsRecovery);
        var ids = await query.GroupBy(r => r.BatchId).Select(g => new {Id = g.Key, Last = g.Max(r => r.Id)})
            .OrderByDescending(g => g.Last).Skip(Math.Max(0, skip)).Take(activeOnly ? 10000 : Math.Clamp(take, 1, 500))
            .Select(g => g.Id).ToListAsync();
        var rows = await Records.AsNoTracking().Where(r => ids.Contains(r.BatchId)).OrderBy(r => r.Id).ToListAsync();
        return rows.GroupBy(r => r.BatchId).Select(g => BuildBatch(g.ToList())).OrderByDescending(b => b.CreatedAt).ToList();
    }

    private ResourceMoveBatchDetailViewModel BuildBatch(List<ResourceMoveRecordDbModel> records)
    {
        var first = records[0];
        var counts = new ResourceMoveBatchCounts
        {
            Total = records.Count,
            Succeeded = records.Count(r => r.Status == ResourceMoveRecordStatus.Succeeded),
            Failed = records.Count(r => r.Status is ResourceMoveRecordStatus.Failed or ResourceMoveRecordStatus.Interrupted or ResourceMoveRecordStatus.NeedsRecovery),
            Cancelled = records.Count(r => r.Status == ResourceMoveRecordStatus.Cancelled),
            Skipped = records.Count(r => r.Status == ResourceMoveRecordStatus.Skipped),
            Waiting = records.Count(r => r.Status == ResourceMoveRecordStatus.WaitingForConflict)
        };
        var running = records.Any(r => r.Status == ResourceMoveRecordStatus.Moving);
        var queued = records.Any(r => r.Status == ResourceMoveRecordStatus.Pending);
        var recovery = records.Any(r => r.Status == ResourceMoveRecordStatus.NeedsRecovery);
        var cancel = records.Any(r => r.CancelRequested);
        var status = recovery ? "needsRecovery" : running ? cancel ? "stopping" : "running" : counts.Waiting > 0 ? "waiting" : queued ? "queued" :
            counts.Succeeded == counts.Total ? "completed" : counts.Cancelled == counts.Total ? "cancelled" :
            counts.Failed == counts.Total ? "failed" : "partial";
        var task = taskManager.GetTaskViewModel(BuildTaskId(first.BatchId));
        return new()
        {
            BatchId = first.BatchId, TaskId = BuildTaskId(first.BatchId), Origin = first.Origin,
            SourceTabId = first.SourceTabId, SourceTabName = first.SourceTabName,
            DestDir = Path.GetDirectoryName(first.DestPath).StandardizePath()!, DestinationId = first.DestinationId,
            DestinationName = first.DestinationName, CreatedAt = first.CreatedAt,
            CompletedAt = records.Any(r => IsRetained(r.Status)) ? null : records.Max(r => r.CompletedAt),
            Status = status, ConflictPolicy = first.ConflictPolicy, CancelRequested = cancel,
            CanCancel = (running || queued || counts.Waiting > 0) && !cancel,
            CanRetry = !running && !queued && records.Any(r => r.ErrorCode != "legacySourcePlanMissing" &&
                r.Status is ResourceMoveRecordStatus.Failed or ResourceMoveRecordStatus.Interrupted or
                    ResourceMoveRecordStatus.Cancelled or ResourceMoveRecordStatus.NeedsRecovery),
            Percentage = running && task != null ? task.Percentage ?? 0 : (counts.Succeeded + counts.Skipped + counts.Cancelled +
                records.Count(r => r.Status == ResourceMoveRecordStatus.Failed)) * 100 / Math.Max(1, counts.Total),
            ResourceIds = records.Select(r => r.ResourceId).ToArray(),
            LockedResourceIds = guard.GetReservedResourceIds(first.BatchId), ReservedPaths = guard.GetReservedPaths(first.BatchId),
            Counts = counts, Records = records.Select(r => r with { MoveJournalJson = null, RequestFingerprint = null,
                ConflictDecisionsJson = null, ReservedResourceIdsJson = null, SourceResourcePathsJson = null, ExecutionPlanJson = null }).ToList()
        };
    }

    public async Task<BaseResponse> CancelBatch(string batchId)
    {
        await ControlGate.WaitAsync();
        try
        {
            if (!await Records.AnyAsync(r => r.BatchId == batchId)) return BaseResponseBuilder.NotFound;
            await Records.Where(r => r.BatchId == batchId).ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));
            await CancelUnstartedRecords(batchId);
            if (Executing.Contains(batchId)) await taskManager.MarkCancelling(BuildTaskId(batchId));
            else
            {
                var recovery = await Records.AsNoTracking().AnyAsync(r => r.BatchId == batchId && r.Status == ResourceMoveRecordStatus.NeedsRecovery);
                if (recovery) guard.Retain(batchId);
                else { guard.Release(batchId); await taskManager.MarkCancelled(BuildTaskId(batchId)); }
            }
            return BaseResponseBuilder.Ok;
        }
        finally { ControlGate.Release(); }
    }

    public Task<BaseResponse> RetryBatch(string batchId) => RetryRecords(batchId, null);

    public async Task<BaseResponse> Retry(int recordId)
    {
        var row = await Records.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recordId);
        return row == null ? BaseResponseBuilder.NotFound : await RetryRecords(row.BatchId, recordId);
    }

    private async Task<BaseResponse> RetryRecords(string batchId, int? recordId)
    {
        await ControlGate.WaitAsync();
        try
        {
            if (Executing.Contains(batchId)) return BaseResponseBuilder.BuildBadRequest(localizer.ResourceMove_RecordInProgress());
            var rows = await Records.Where(r => r.BatchId == batchId).ToListAsync();
            if (rows.Count == 0) return BaseResponseBuilder.NotFound;
            if (rows.Any(r => r.Status is ResourceMoveRecordStatus.Pending or ResourceMoveRecordStatus.Moving))
                return BaseResponseBuilder.BuildBadRequest(localizer.ResourceMove_RecordInProgress());
            var retry = rows.Where(r => (!recordId.HasValue || r.Id == recordId) && r.ErrorCode != "legacySourcePlanMissing" &&
                r.Status is ResourceMoveRecordStatus.Failed or ResourceMoveRecordStatus.Interrupted or
                    ResourceMoveRecordStatus.Cancelled or ResourceMoveRecordStatus.NeedsRecovery).ToList();
            if (retry.Count == 0) return BaseResponseBuilder.BuildBadRequest("No retryable move records");
            foreach (var r in retry)
            {
                try
                {
                    await ValidateSourceSnapshot(r);
                    if (!(r.PhysicalMoveStarted && r.MoveJournalJson == null))
                        await ValidateSourcePlan(r, await GetExecutionPlan(r));
                }
                catch (InvalidOperationException e) { return BaseResponseBuilder.Build(ResponseCode.Conflict, e.Message); }
            }
            var reservation = await EnsureReservation(batchId, rows.Where(r => IsRetained(r.Status) || retry.Contains(r)).ToList());
            if (reservation != null) return reservation;
            foreach (var r in rows) r.CancelRequested = false;
            foreach (var r in retry)
            {
                r.Status = ResourceMoveRecordStatus.Pending; r.Error = null; r.ErrorCode = null; r.CompletedAt = null;
            }
            await db.SaveChangesAsync();
        }
        finally { ControlGate.Release(); }
        await QueueExistingBatch(batchId);
        return BaseResponseBuilder.Ok;
    }

    private async Task<BaseResponse?> EnsureReservation(string batchId, List<ResourceMoveRecordDbModel> rows)
    {
        var paths = rows.SelectMany(r => new[] {r.SourcePath, r.DestPath}).Distinct().ToArray();
        var ids = await ComputeAffectedResourceIds(paths, rows.Select(r => r.ResourceId));
        foreach (var row in rows)
            if (row.SourceResourcePathsJson != null)
                ids.UnionWith(JsonConvert.DeserializeObject<Dictionary<int,string>>(row.SourceResourcePathsJson)!.Keys);
        if (!guard.TryExtendReservation(batchId, ids, paths, out var conflict))
            return BaseResponseBuilder.Build(ResponseCode.Conflict, localizer.ResourceMove_ResourcesAreBeingMoved(conflict!));
        foreach (var r in rows) r.ReservedResourceIdsJson = JsonConvert.SerializeObject(ids);
        return null;
    }

    private async Task QueueExistingBatch(string batchId)
    {
        if (taskManager.GetTaskViewModel(BuildTaskId(batchId)) != null)
            await taskManager.Requeue(BuildTaskId(batchId));
        else
        {
            var rows = await Records.AsNoTracking().Where(r => r.BatchId == batchId).ToListAsync();
            await EnqueueBatchTask(batchId, rows.Count, Path.GetDirectoryName(rows[0].DestPath).StandardizePath()!,
                guard.GetReservedResourceIds(batchId));
        }
    }

    public async Task<BaseResponse> ResolveConflict(int recordId, ResourceMoveConflictResolution resolution)
    {
        string batchId;
        var applyPanel = false;
        var queueBatch = true;
        await ControlGate.WaitAsync();
        try
        {
            var row = await Records.FirstOrDefaultAsync(r => r.Id == recordId);
            if (row == null) return BaseResponseBuilder.NotFound;
            batchId = row.BatchId;
            if (Executing.Contains(batchId)) return BaseResponseBuilder.BuildBadRequest("Move executor is finishing; try again shortly");
            var legacySourceRecovery = row.Status == ResourceMoveRecordStatus.NeedsRecovery && row.ConflictKind == "legacySourcePlanMissing";
            if ((!legacySourceRecovery && row.Status != ResourceMoveRecordStatus.WaitingForConflict) || row.ConflictVersion != resolution.ConflictVersion)
                return BaseResponseBuilder.Build(ResponseCode.Conflict, "Conflict has changed; refresh the task");
            if (resolution.Action is not ("overwrite" or "skip" or "restoreSource") || resolution.Scope is not ("once" or "batch" or "panel"))
                return BaseResponseBuilder.BuildBadRequest("Unknown conflict decision");
            if (resolution.Action == "restoreSource")
            {
                if ((!legacySourceRecovery && (row.ConflictKind != "legacyRecovery" || row.MoveJournalJson != null)) || resolution.Scope != "once")
                    return BaseResponseBuilder.BuildBadRequest("Manual source restoration only applies to legacy moves without a complete recovery plan");
                if (!Directory.Exists(row.SourcePath) && !File.Exists(row.SourcePath))
                    return BaseResponseBuilder.BuildBadRequest("Restore the complete resource at its original source path first");
                try { await ValidateSourceSnapshot(row, true); }
                catch (InvalidOperationException e) { return BaseResponseBuilder.Build(ResponseCode.Conflict, e.Message); }
                var restored = await BuildSourcePlan(row.ResourceId, row.SourcePath, row.SourcePath,
                    await GetSourceSnapshot(row), restoring: true);
                if (restored.ReasonCode != null) return BaseResponseBuilder.Build(ResponseCode.Conflict, restored.ReasonCode);
                // This is a separate, explicit human acknowledgement. Never infer it from
                // the overwrite policy and never touch or delete the old destination.
                row.PolicyAuditJson = JsonConvert.SerializeObject(new { action = "restoreSource", at = DateTime.UtcNow,
                    previousJournal = row.MoveJournalJson, previousExecutionPlan = row.ExecutionPlanJson });
                row.MoveJournalJson = null;
                row.ExecutionPlanJson = null;
                row.Status = ResourceMoveRecordStatus.Cancelled;
                row.PhysicalMoveStarted = false;
                row.CompletedAt = DateTime.Now;
                row.Error = null;
                row.ErrorCode = null;
                row.ConflictKind = null;
                row.CanOverwrite = false;
                row.ConflictDecisionsJson = null;
            }
            else if (resolution.Action == "skip")
            {
                if (row.PhysicalMoveStarted) return BaseResponseBuilder.BuildBadRequest("Recover this resource before skipping it");
                row.Status = ResourceMoveRecordStatus.Skipped;
                row.CompletedAt = DateTime.Now;
            }
            else
            {
                if (!row.CanOverwrite || row.ConflictPath == null)
                    return BaseResponseBuilder.BuildBadRequest("This conflict requires manual resolution; automatic resource merging is not supported");
                await CheckIdentityConflict(row);
                var current = ResourceMoveSafeFileSystem.Fingerprint(row.ConflictPath);
                if (current != row.ConflictFingerprint)
                {
                    row.ConflictFingerprint = current; row.ConflictVersion++;
                    await db.SaveChangesAsync();
                    return BaseResponseBuilder.Build(ResponseCode.Conflict, "Destination changed; review the new conflict");
                }
                var decisions = JsonConvert.DeserializeObject<Dictionary<string,string>>(row.ConflictDecisionsJson ?? "{}")!;
                decisions[row.ConflictPath] = current;
                row.ConflictDecisionsJson = JsonConvert.SerializeObject(decisions);
                row.PolicyAuditJson = JsonConvert.SerializeObject(new { action = resolution.Action, scope = resolution.Scope,
                    row.ConflictPath, current, at = DateTime.UtcNow });
                if (resolution.Scope == "batch")
                {
                    var batchRows = await Records.Where(r => r.BatchId == batchId).ToListAsync();
                    foreach (var item in batchRows) item.ConflictPolicy = "overwrite";
                }
                if (resolution.Scope == "panel")
                {
                    if (row.Origin != "move-panel") return BaseResponseBuilder.BuildBadRequest("Panel policy only applies to panel moves");
                    await panelSettings.SetAutoOverwrite(true);
                    applyPanel = true;
                }
                row.Status = ResourceMoveRecordStatus.Pending;
                row.CancelRequested = false;
                row.Error = null;
                row.ErrorCode = null;
            }
            await db.SaveChangesAsync();
            if (resolution.Action == "restoreSource" &&
                !await Records.AsNoTracking().AnyAsync(r => r.BatchId == batchId &&
                    (r.Status == ResourceMoveRecordStatus.Pending || r.Status == ResourceMoveRecordStatus.Moving ||
                     r.Status == ResourceMoveRecordStatus.WaitingForConflict || r.Status == ResourceMoveRecordStatus.NeedsRecovery)))
            {
                guard.Release(batchId);
                await taskManager.MarkCancelled(BuildTaskId(batchId));
                queueBatch = false;
            }
        }
        catch (ResourceMoveConflictException e) { return BaseResponseBuilder.Build(ResponseCode.Conflict, e.Message); }
        finally { ControlGate.Release(); }
        if (applyPanel) await ApplyPanelPolicy();
        if (queueBatch) await QueueExistingBatch(batchId);
        return BaseResponseBuilder.Ok;
    }

    public async Task ApplyPanelPolicy()
    {
        if (!panelSettings.AutoOverwrite) return;
        var batches = new HashSet<string>();
        await ControlGate.WaitAsync();
        try
        {
            var waiting = await Records.Where(r => r.Status == ResourceMoveRecordStatus.WaitingForConflict &&
                r.Origin == "move-panel" && r.ConflictPolicy == "inherit" && r.CanOverwrite && !r.CancelRequested).ToListAsync();
            foreach (var r in waiting)
            {
                r.Status = ResourceMoveRecordStatus.Pending; r.Error = null; r.ErrorCode = null;
                batches.Add(r.BatchId);
            }
            await db.SaveChangesAsync();
        }
        finally { ControlGate.Release(); }
        foreach (var id in batches) await QueueExistingBatch(id);
    }

    public async Task<BaseResponse> DeleteRecord(int recordId)
    {
        await ControlGate.WaitAsync();
        try
        {
            var r = await Records.FirstOrDefaultAsync(r => r.Id == recordId);
            if (r == null) return BaseResponseBuilder.NotFound;
            if (IsRetained(r.Status) || guard.HoldsReservation(r.BatchId))
                return BaseResponseBuilder.BuildBadRequest(localizer.ResourceMove_RecordInProgress());
            // Keep the request-key row so delayed network retries cannot recreate a finished move.
            if (r.IdempotencyKey != null) return BaseResponseBuilder.BuildBadRequest("Batch contains a durable submission receipt");
            Records.Remove(r); await db.SaveChangesAsync();
            return BaseResponseBuilder.Ok;
        }
        finally { ControlGate.Release(); }
    }

    public async Task<BaseResponse> DeleteInactiveRecords()
    {
        await ControlGate.WaitAsync();
        try
        {
            var rows = await Records.ToListAsync();
            var removable = rows.GroupBy(r => r.BatchId).Where(g => g.All(r => !IsRetained(r.Status)) &&
                !guard.HoldsReservation(g.Key) && g.All(r => r.IdempotencyKey == null)).SelectMany(g => g).ToList();
            Records.RemoveRange(removable); await db.SaveChangesAsync();
            return BaseResponseBuilder.Ok;
        }
        finally { ControlGate.Release(); }
    }

    public async Task MarkInterruptedOnStartup()
    {
        await ControlGate.WaitAsync();
        try
        {
            var rows = await Records.Where(r => r.Status == ResourceMoveRecordStatus.Pending || r.Status == ResourceMoveRecordStatus.Moving ||
                r.Status == ResourceMoveRecordStatus.WaitingForConflict || r.Status == ResourceMoveRecordStatus.NeedsRecovery ||
                r.PhysicalMoveStarted && (r.Status == ResourceMoveRecordStatus.Interrupted || r.Status == ResourceMoveRecordStatus.Cancelled || r.Status == ResourceMoveRecordStatus.Failed))
                .ToListAsync();
            foreach (var r in rows)
            {
                if (r.Status is ResourceMoveRecordStatus.Pending or ResourceMoveRecordStatus.Moving ||
                    r.PhysicalMoveStarted && r.Status is ResourceMoveRecordStatus.Interrupted or ResourceMoveRecordStatus.Cancelled or ResourceMoveRecordStatus.Failed)
                {
                    r.Status = r.PhysicalMoveStarted ? ResourceMoveRecordStatus.NeedsRecovery : ResourceMoveRecordStatus.Interrupted;
                    r.CompletedAt = DateTime.Now;
                    r.ErrorCode = r.PhysicalMoveStarted ? "recoveryRequired" : "interruptedBeforeStart";
                    r.Error = localizer.ResourceMove_InterruptedByRestart();
                }
            }
            await db.SaveChangesAsync();
            foreach (var batch in rows.GroupBy(r => r.BatchId))
            {
                var retained = batch.Where(r => IsRetained(r.Status)).ToList();
                if (retained.Count == 0) continue;
                var error = await EnsureReservation(batch.Key, retained);
                if (error != null) throw new InvalidOperationException($"Cannot restore move reservation: {batch.Key}");
                guard.Retain(batch.Key);
            }
        }
        finally { ControlGate.Release(); }
    }
}

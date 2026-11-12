using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionLocalRecoveryBatchService
{
    /// <summary>One bounded reconciliation. No HTTP request, tab, or in-memory queue is the
    /// authority for whether a previously approved task may run after restart.</summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        if (tasks?.IsShuttingDown == true || !await AcquisitionLocalRecoveryBatchPreviewStore.Gate.WaitAsync(0, ct)) return;
        try
        {
            var continuations = await db.AcquisitionTasks.AsNoTracking().Where(t => t.BuilderStateJson != null &&
                t.BuilderStateJson.Contains("\"continuationRequest\"")).ToArrayAsync(ct);
            if (continuations.Any(t => AcquisitionContinuationEntry.Read(t)?.Status == "checking")) return;
            var all = await QueueRowsAsync(ct);
            var open = all.Where(t => AcquisitionLocalRecoveryQueueEntry.Read(t) is {IsOpen: true}).ToArray();
            // Never launch a second batch task while any batch-owned execution is active,
            // even if another batch was created by a different browser during that run.
            var active = open.Where(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.Status is "starting" or "running")
                .OrderBy(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.CreatedAt).ThenBy(t => t.Id).ToArray();
            if (active.Length > 0)
            {
                foreach (var task in active) await ObserveAsync(task.Id, ct);
                return;
            }
            var batch = open.GroupBy(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.BatchId)
                .Where(g => g.Any(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.Status == "queued") &&
                    g.All(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.Status is not ("paused" or "spaceBlocked")))
                .OrderBy(g => g.Min(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.CreatedAt)).FirstOrDefault();
            var next = batch?.OrderBy(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.Order).FirstOrDefault(t =>
                AcquisitionLocalRecoveryQueueEntry.Read(t)!.Status == "queued");
            if (next != null) await DispatchAsync(next.Id, ct);
        }
        finally {AcquisitionLocalRecoveryBatchPreviewStore.Gate.Release();}
    }

    private async Task ObserveAsync(int id, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            var (task, run) = await CurrentAsync(id, ct);
            var entry = task == null ? null : AcquisitionLocalRecoveryQueueEntry.Read(task);
            if (task == null || entry == null || entry.Status is not ("starting" or "running")) return;
            if (task.DeletedAt != null || task.Status == AcquisitionStatus.Cancelled)
            {await SaveAsync(task, entry with {Status = "cancelled", ReasonCode = "RECOVERY_TASK_CANCELLED"}, ct); return;}
            // A local-files adoption may have committed a new run before its response was
            // persisted. Observe that exact next revision; never call adoption twice.
            if (entry.RecoveryScope == "localFiles" && task.WorkflowRunId is { } adopted && adopted != entry.WorkflowRunId &&
                await MatchesLocalAdoptionAsync(task, run, entry, ct))
            {
                entry = entry with {WorkflowRunId = adopted, PlanRevision = task.PlanRevision, RecoveryScope = "workflow"};
                await SaveAsync(task, entry, ct);
                task = (await CurrentAsync(id, ct)).Item1!;
            }
            if (task.WorkflowRunId != entry.WorkflowRunId || task.PlanRevision != entry.PlanRevision || run == null)
            {
                // A different UI action may have installed another run during a lost response.
                // Do not grant it this approval or overlap it with the next queued file operation.
                var original = entry.WorkflowRunId is { } old && old != task.WorkflowRunId
                    ? await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == old, ct) : null;
                var active = IsExecuting(run) || IsExecuting(original);
                await SaveAsync(task, entry with {Status = active ? "running" : "attention", ReasonCode = "LOCAL_BATCH_OUTCOME_UNKNOWN"}, ct);
                return;
            }
            if (run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running ||
                tasks?.IsPending("workflow.run." + run.Id) == true)
            {
                if (entry.Status != "running") await SaveAsync(task, entry with {Status = "running"}, ct);
                return;
            }
            if (run.Status == WorkflowRunStatus.Success)
            {await SaveAsync(task, entry with {Status = "completed", ReasonCode = null}, ct); return;}
            if (run.Status is WorkflowRunStatus.Interrupted or WorkflowRunStatus.Failed or WorkflowRunStatus.Cancelled)
            {
                // Startup can mark an accepted pending run Interrupted without clearing its
                // parked answer. A stale answer cannot keep the batch permanently active.
                var terminalStorage = StorageProblem(run);
                await SaveAsync(task, entry with {Status = terminalStorage == null ? "attention" : "spaceBlocked",
                    ReasonCode = terminalStorage ?? (run.Status == WorkflowRunStatus.Interrupted ? "LOCAL_BATCH_INTERRUPTED" :
                        run.Status == WorkflowRunStatus.Cancelled ? "RECOVERY_TASK_CANCELLED" : "LOCAL_BATCH_REVIEW_REQUIRED")}, ct);
                return;
            }
            if (run.Status == WorkflowRunStatus.Waiting && run.PendingSignalJson != null)
            {
                if (entry.Status != "running") await SaveAsync(task, entry with {Status = "running"}, ct);
                return;
            }
            if (run.Status == WorkflowRunStatus.Waiting && AcquisitionStorageOverrideService.HasReserveLowReason(run))
            {
                // A newly adopted run can reach its first writer before its run-bound grant
                // is installed. It simply waits under the normal reserve until explicit queue
                // permission is attached and revalidated; no work is duplicated.
                var installedGrant = AcquisitionStorageOverrideGrant.Read(task);
                if (entry.OverrideValid(DateTimeOffset.UtcNow) && !entry.OverrideResumeAttempted &&
                    (installedGrant?.Token != "batch:" + entry.BatchId ||
                     run.WaitingSince?.ToUniversalTime() <= installedGrant.GrantedAt.UtcDateTime))
                {
                    task = await GrantWhileLockedAsync(task, run, entry, ct);
                    entry = entry with {Status = "queued", RecoveryScope = "workflow", OverrideResumeAttempted = true,
                        Version = AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run), ReasonCode = null};
                    var check = await CheckAsync(task, run, new(entry.EstimatedBytes, "retained", entry.EstimateIncomplete), ct, mode: entry.Mode, restricted: entry.Restricted);
                    if (check.Evidence is { } e)
                        await SaveAsync(task, entry with {Signal = e.Signal, StorageStamp = e.StorageStamp,
                            WorkingDirectories = e.WorkingDirectories, DeliveryDirectories = e.DeliveryDirectories,
                            AuthorizationStamp = e.AuthorizationStamp, ActionCategory = e.ActionCategory, VerificationPending = e.VerificationPending}, ct);
                    else await SaveAsync(task, entry with {Status = "attention", ReasonCode = check.Row.ReasonCode}, ct);
                    return;
                }
                await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = StorageWriteGuard.ReserveLowCode}, ct);
                return;
            }
            if (StorageProblem(run) is { } storageProblem)
            {
                await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = storageProblem}, ct);
                return;
            }
            // Starting is persisted before enqueue. An unchanged waiting checkpoint after a
            // lost response is indeterminate, not permission to submit a second continuation.
            var code = entry.Status == "starting" && entry.AttemptVersion == AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run)
                ? "LOCAL_BATCH_OUTCOME_UNKNOWN" : run.Status == WorkflowRunStatus.Interrupted
                    ? "LOCAL_BATCH_INTERRUPTED" : "LOCAL_BATCH_REVIEW_REQUIRED";
            await SaveAsync(task, entry with {Status = "attention", ReasonCode = code}, ct);
        }
        finally {gate.Release();}
    }

    private bool IsExecuting(WorkflowRunDbModel? run) => run != null &&
        (run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running ||
         run.Status == WorkflowRunStatus.Waiting && run.PendingSignalJson != null || tasks?.IsPending("workflow.run." + run.Id) == true);

    private async Task<bool> MatchesLocalAdoptionAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        AcquisitionLocalRecoveryQueueEntry entry, CancellationToken ct)
    {
        try
        {
            var receipt = AcquisitionLocalFilesRecoveryReceipt.Read(task);
            var plan = AcquisitionLocalFilesRecoveryReceipt.Read<AcquisitionPlan>(task.PlanJson);
            if (receipt == null || plan == null || run == null || receipt.ResultingRevision != task.PlanRevision ||
                AcquisitionLocalFilesRecoveryReceipt.Hash(receipt.Storage) != entry.StorageStamp ||
                !entry.WorkingDirectories.SequenceEqual([receipt.Storage.Policy.ProcessingWorkspaceDirectory]) ||
                !entry.DeliveryDirectories.SequenceEqual(receipt.Preview.Parts.Select(p => p.DeliveryDirectory).Distinct(ArtifactManifestFiles.PathComparer)) ||
                plan.DeliveryLayout == null || !entry.DeliveryDirectories.ToHashSet(ArtifactManifestFiles.PathComparer)
                    .SetEquals(plan.DeliveryLayout.PartDirectories.Values) ||
                plan.StoragePolicy == null || AcquisitionLocalFilesRecoveryReceipt.Hash(plan.StoragePolicy) != AcquisitionLocalFilesRecoveryReceipt.Hash(receipt.Storage.Policy) ||
                plan.Parts.Count != receipt.Parts.Count || plan.Parts.Any(p => p.ContinueFrom == null ||
                    p.ContinueFrom.TaskId != task.Id || p.ContinueFrom.PreviousRunId != receipt.PreviousRunId ||
                    !ArtifactManifestFiles.PathComparer.Equals(ArtifactStoragePolicies.Resolve(plan.StoragePolicy, p.StoragePolicy).ProcessingWorkspaceDirectory,
                        receipt.Storage.Policy.ProcessingWorkspaceDirectory) ||
                    !receipt.Parts.Any(e => e.PartId == p.Id && e.ArtifactId == p.ContinueFrom.ArtifactId &&
                        e.OperationId == p.ContinueFrom.OperationId && e.Fingerprint == p.ContinueFrom.Fingerprint)) ||
                !IsLocalRemainder(run with {CurrentStepIndex = 0}, allowContinueFiles: true) ||
                WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes.Any(n => n.Kind == "download.awaitCompletion")) return false;
            var preview = receipt.Preview;
            IReadOnlyList<string>? downloadEvidence = null;
            if (entry.WorkflowRunId is { } previous)
            {
                if (receipt.PreviousRunId != previous || receipt.PreviousRevision != entry.PlanRevision ||
                    task.PlanRevision != entry.PlanRevision + 1) return false;
                var original = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == previous, ct);
                if (original == null || receipt.Checkpoint != AcquisitionLocalFilesRecoveryReceipt.Hash(original)) return false;
                preview = new(receipt.Preview.Parts.Select(p => p with {ArtifactId = null}).ToArray());
                downloadEvidence = receipt.Parts.Select(p => p.DownloadEvidence).ToArray();
            }
            else if (task.PlanRevision != entry.PlanRevision) return false;
            var settings = receipt.Storage;
            return AcquisitionLocalFilesRecoveryReceipt.Hash(new {settings, preview, downloadEvidence}) == entry.Signal;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException) {return false;}
    }

    internal static string? StorageProblem(WorkflowRunDbModel run)
    {
        var code = LocalStorageFailures.PersistedCode(run.ErrorMessage);
        if (code != null) return code;
        if (string.IsNullOrWhiteSpace(run.WaitPromptJson) || run.WaitPromptJson.Length > 1024 * 1024) return null;
        try
        {
            using var json = JsonDocument.Parse(run.WaitPromptJson);
            var prompt = json.RootElement;
            if (prompt.ValueKind != JsonValueKind.Object) return null;
            if (prompt.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String &&
                LocalStorageFailures.PersistedCode(message.GetString()) is { } persisted) return persisted;
            if (run.WaitReason == "cloud.fileProcessing" && prompt.TryGetProperty("reason", out var reason) &&
                reason.ValueKind == JsonValueKind.String &&
                Bakabase.Service.Components.Workflow.ExecutionProblemClassifier.IsSpaceInsufficient(reason.GetString()))
                return Bakabase.Service.Components.Workflow.ExecutionProblemClassifier.NormalizedReason(reason.GetString());
            return null;
        }
        catch (JsonException) {return null;}
    }

    private async Task DispatchAsync(int id, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); if (!await gate.WaitAsync(0, ct)) return;
        AcquisitionLocalRecoveryQueueEntry? dispatch = null;
        string? processingPlacementStamp = null;
        try
        {
            var (task, run) = await CurrentAsync(id, ct);
            var entry = task == null ? null : AcquisitionLocalRecoveryQueueEntry.Read(task);
            if (task == null || entry?.Status != "queued") return;
            if (task.DeletedAt != null || AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run) != entry.Version ||
                task.WorkflowRunId != entry.WorkflowRunId || task.PlanRevision != entry.PlanRevision)
            {await SaveAsync(task, entry with {Status = "skipped", ReasonCode = "ACQUISITION_TASK_CHANGED"}, ct); return;}
            // Capacity can reject the write before any expensive retained-content verification.
            var admissionMinimum = MinimumFor(entry);
            try
            {
                if (CapacityFor([new(entry.WorkingDirectories, entry.DeliveryDirectories, entry.EstimatedBytes, entry.EstimateIncomplete)], admissionMinimum)
                    .Any(v => v.ExceedsUsable))
                {await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = StorageWriteGuard.ReserveLowCode}, ct); return;}
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = "LOCAL_BATCH_CAPACITY_UNKNOWN"}, ct); return;}
            var check = await CheckAsync(task, run, new(entry.EstimatedBytes, "retained", entry.EstimateIncomplete), ct, mode: entry.Mode, restricted: entry.Restricted);
            if (check.Evidence is not { } e || e.Scope != entry.RecoveryScope || !entry.VerificationPending && e.Signal != entry.Signal || e.StorageStamp != entry.StorageStamp ||
                e.ActionCategory != entry.ActionCategory || e.ActionCategory != "localProcessing" && e.CanOverrideStorage != entry.CanOverrideStorage || e.AuthorizationStamp != entry.AuthorizationStamp ||
                !e.WorkingDirectories.SequenceEqual(entry.WorkingDirectories) || !e.DeliveryDirectories.SequenceEqual(entry.DeliveryDirectories))
            {await SaveAsync(task, entry with {Status = "attention", ReasonCode = check.Evidence == null ? check.Row.ReasonCode : "ACQUISITION_TASK_CHANGED"}, ct); return;}
            var minimum = MinimumFor(entry);
            IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> volumes;
            try {volumes = CapacityFor([Item(e, entry.EstimatedBytes)], minimum);}
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = "LOCAL_BATCH_CAPACITY_UNKNOWN"}, ct); return;}
            if (volumes.Any(v => v.ExceedsUsable))
            {await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = StorageWriteGuard.ReserveLowCode}, ct); return;}
            if (run != null && entry.RecoveryScope == "workflow")
                task = await GrantWhileLockedAsync(task, run, entry, ct);
            dispatch = entry with {Status = "starting", ReasonCode = null, Signal = e.Signal, VerificationPending = false,
                AttemptVersion = AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run)};
            processingPlacementStamp = check.ProcessingPlacementStamp;
            await SaveAsync(task, dispatch, ct);
        }
        finally {gate.Release();}
        if (dispatch == null) return;

        // Use the normal, freshly bound recovery protocol. Holding the batch gate prevents
        // duplicate queue dispatch; recovery itself retains task -> workflow lock ordering.
        try
        {
            var preview = await recovery.PreviewAsync(new([id], dispatch.RecoveryScope), ct);
            var row = preview.Rows.Single();
            var (before, beforeRun) = await CurrentAsync(id, ct);
            if (before == null || AcquisitionLocalRecoveryQueueEntry.Read(before)?.BatchId != dispatch.BatchId ||
                AcquisitionLocalRecoveryQueueEntry.ContentVersion(before, beforeRun) != dispatch.AttemptVersion ||
                !row.CanContinue || row.Action is not ("resume" or "retry" or "localFiles"))
            {await FinishDispatchAsync(id, dispatch, "attention", row.ReasonCode, ct); return;}
            var result = await recovery.ApplyBatchAsync(new(preview.PreviewToken, [id]), processingPlacementStamp, ct);
            var accepted = result.ContinuedTaskIds.Contains(id);
            var local = result.LocalFiles?.SingleOrDefault();
            var lockAgain = AcquisitionTaskGate.For(id); await lockAgain.WaitAsync(ct);
            try
            {
                var (current, currentRun) = await CurrentAsync(id, ct);
                var saved = current == null ? null : AcquisitionLocalRecoveryQueueEntry.Read(current);
                if (current == null || saved?.BatchId != dispatch.BatchId) return;
                if (accepted && currentRun != null)
                {
                    var matches = saved.RecoveryScope == "localFiles"
                        ? await MatchesLocalAdoptionAsync(current, currentRun, saved, ct)
                        : currentRun.Id == saved.WorkflowRunId && current.PlanRevision == saved.PlanRevision;
                    if (!matches)
                    {
                        await SaveAsync(current, saved with {Status = "running", ReasonCode = "LOCAL_BATCH_OUTCOME_UNKNOWN"}, ct);
                        return;
                    }
                    var next = saved with {Status = "running", WorkflowRunId = currentRun.Id, PlanRevision = current.PlanRevision,
                        RecoveryScope = "workflow", Version = AcquisitionLocalRecoveryQueueEntry.ContentVersion(current, currentRun)};
                    if (next.OverrideValid(DateTimeOffset.UtcNow)) current = await GrantWhileLockedAsync(current, currentRun, next, ct);
                    await SaveAsync(current, next, ct);
                }
                else
                {
                    var reason = local?.ReasonCode ?? result.Skipped.FirstOrDefault()?.ReasonCode ?? "LOCAL_BATCH_OUTCOME_UNKNOWN";
                    if (reason != "LOCAL_BATCH_OUTCOME_UNKNOWN") current = await RevokeBatchGrantWhileLockedAsync(current, saved, reason, ct);
                    var (_, observedRun) = await CurrentAsync(id, ct);
                    await SaveAsync(current, saved with {Status = IsExecuting(observedRun) ? "running" : "attention", ReasonCode = reason}, ct);
                }
            }
            finally {lockAgain.Release();}
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or OperationCanceledException or IOException)
        {
            // Do not repeat an accepted call after a lost response/process stop. The persisted
            // starting state is reconciled against its exact current run on the next tick.
            if (!ct.IsCancellationRequested) await ObserveAsync(id, ct);
        }
    }

    private async Task FinishDispatchAsync(int id, AcquisitionLocalRecoveryQueueEntry expected, string status, string reason, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var (task, _) = await CurrentAsync(id, ct);
            var entry = task == null ? null : AcquisitionLocalRecoveryQueueEntry.Read(task);
            if (task != null && entry?.BatchId == expected.BatchId && entry.Status == "starting")
            {
                task = await RevokeBatchGrantWhileLockedAsync(task, entry, reason, ct);
                var (_, observedRun) = await CurrentAsync(id, ct);
                await SaveAsync(task, entry with {Status = IsExecuting(observedRun) ? "running" : status, ReasonCode = reason}, ct);
            }
        }
        finally {gate.Release();}
    }

    private async Task<AcquisitionTaskDbModel> RevokeBatchGrantWhileLockedAsync(AcquisitionTaskDbModel task,
        AcquisitionLocalRecoveryQueueEntry entry, string reason, CancellationToken ct, bool runGateHeld = false)
    {
        var grant = AcquisitionStorageOverrideGrant.Read(task);
        if (grant == null || grant.Token != "batch:" + entry.BatchId || grant.WorkflowRunId != entry.WorkflowRunId ||
            grant.PlanRevision != entry.PlanRevision || task.WorkflowRunId != entry.WorkflowRunId) return task;
        var gate = WorkflowRunMutationGate.For(grant.WorkflowRunId); if (!runGateHeld) await gate.WaitAsync(ct);
        try
        {
            var (latest, run) = await CurrentAsync(task.Id, ct);
            var current = latest == null ? null : AcquisitionStorageOverrideGrant.Read(latest);
            if (latest == null || run == null || latest.WorkflowRunId != grant.WorkflowRunId || latest.PlanRevision != grant.PlanRevision ||
                current?.Token != grant.Token || run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running ||
                run.Status == WorkflowRunStatus.Waiting && run.PendingSignalJson != null || tasks?.IsPending("workflow.run." + run.Id) == true)
                return latest ?? task;
            var json = AcquisitionStorageOverrideGrant.WithState(latest.BuilderStateJson, current with {Status = "revoked", ReasonCode = reason});
            if (await db.AcquisitionTasks.Where(t => t.Id == latest.Id && t.BuilderStateJson == latest.BuilderStateJson &&
                    t.WorkflowRunId == latest.WorkflowRunId && t.PlanRevision == latest.PlanRevision)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json), ct) != 1)
                throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            AcquisitionStorageWritePolicyProvider.Invalidate();
            return latest with {BuilderStateJson = json};
        }
        finally {if (!runGateHeld) gate.Release();}
    }

    private long MinimumFor(AcquisitionLocalRecoveryQueueEntry entry) => AcquisitionSpaceReserve.Minimum(options.Value,
        entry.OverrideValid(DateTimeOffset.UtcNow) ? entry.ReserveMode ?? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Inherit);

    private async Task<AcquisitionTaskDbModel> GrantWhileLockedAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel run,
        AcquisitionLocalRecoveryQueueEntry entry, CancellationToken ct)
    {
        if (task.WorkflowRunId != run.Id || task.PlanRevision != entry.PlanRevision ||
            run.Status is WorkflowRunStatus.Success or WorkflowRunStatus.Cancelled || !(IsLocalRemainder(run) || entry.CanOverrideStorage && AcquisitionContinuationReserve.IsDownload(run))) return task;
        var gate = WorkflowRunMutationGate.For(run.Id); await gate.WaitAsync(ct);
        try
        {
            var (latest, latestRun) = await CurrentAsync(task.Id, ct);
            if (latest == null || latestRun == null || latest.WorkflowRunId != run.Id || latest.PlanRevision != entry.PlanRevision ||
                AcquisitionLocalRecoveryQueueEntry.Read(latest)?.BatchId != entry.BatchId) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            var existing = AcquisitionStorageOverrideGrant.Read(latest);
            var approved = entry.OverrideValid(DateTimeOffset.UtcNow);
            // An inherited selection must not silently retain a previous single-operation
            // emergency/configured choice. This marker follows the global policy; it grants
            // no new lowered-reserve permission and never renews the batch's approval.
            if (!approved && existing?.Status != "applied") return latest;
            var grant = new AcquisitionStorageOverrideGrant(run.Id, task.PlanRevision, "batch:" + entry.BatchId,
                DateTimeOffset.UtcNow, approved ? entry.OverrideExpiresAt!.Value : entry.CreatedAt.AddHours(24),
                ReserveMode: approved ? entry.ReserveMode ?? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Inherit);
            if (existing?.Token == grant.Token) return latest;
            var json = AcquisitionStorageOverrideGrant.WithState(latest.BuilderStateJson, grant);
            if (await db.AcquisitionTasks.Where(t => t.Id == latest.Id && t.BuilderStateJson == latest.BuilderStateJson &&
                    t.WorkflowRunId == latest.WorkflowRunId && t.PlanRevision == latest.PlanRevision)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json), ct) != 1)
                throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            AcquisitionStorageWritePolicyProvider.Invalidate();
            return latest with {BuilderStateJson = json};
        }
        finally {gate.Release();}
    }
}

public sealed class AcquisitionLocalRecoveryBatchWorker(IServiceScopeFactory scopes,
    ILogger<AcquisitionLocalRecoveryBatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                budget.CancelAfter(TimeSpan.FromSeconds(30));
                await scope.ServiceProvider.GetRequiredService<AcquisitionLocalRecoveryBatchService>().TickAsync(budget.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
            catch (OperationCanceledException) { }
            catch (Exception) {logger.LogWarning("Local acquisition recovery queue is temporarily unavailable; retained execution state will be checked again.");}
            try {await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);}
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
        }
    }
}

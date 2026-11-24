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
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionLocalRecoveryBatchService
{
    /// <summary>One bounded reconciliation. No HTTP request, tab, or in-memory queue is the
    /// authority for whether a previously approved task may run after restart.</summary>
    public Task TickAsync(CancellationToken ct = default) => TickAsync(null, null, null, ct);

    internal async Task TickAsync(IReadOnlySet<int>? inFlight, Action<int>? scheduleLocalFiles, Action<int>? scheduleWorkflow, CancellationToken ct)
    {
        if (tasks?.IsShuttingDown == true || !await AcquisitionLocalRecoveryBatchPreviewStore.Gate.WaitAsync(0, ct)) return;
        try
        {
            var all = await QueueRowsAsync(ct);
            var open = all.Where(t => AcquisitionLocalRecoveryQueueEntry.Read(t) is {IsOpen: true}).ToArray();
            // Reconcile each accepted execution independently. A workflow waiting for an AI
            // or download slot must not prevent another task from reaching its own node.
            var active = open.Where(t => inFlight?.Contains(t.Id) != true &&
                    AcquisitionLocalRecoveryQueueEntry.Read(t)!.Status is ("starting" or "running"))
                .OrderBy(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.CreatedAt).ThenBy(t => t.Id).ToArray();
            if (active.Length > 0)
            {
                foreach (var task in active) await ObserveAsync(task.Id, ct);
                // Observation may complete a row or pause its batch for a storage problem.
                open = (await QueueRowsAsync(ct)).Where(t => AcquisitionLocalRecoveryQueueEntry.Read(t) is {IsOpen: true}).ToArray();
            }
            bool Dispatchable(AcquisitionTaskDbModel task)
            {
                var state = AcquisitionLocalRecoveryQueueEntry.Read(task)!;
                return state.Status == "queued" && inFlight?.Contains(task.Id) != true &&
                    (inFlight == null || (NeedsFileWorker(state) ? scheduleLocalFiles : scheduleWorkflow) != null);
            }
            var batch = open.GroupBy(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.BatchId)
                .Where(g => g.Any(Dispatchable) &&
                    g.All(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.Status is not ("paused" or "spaceBlocked")))
                .OrderBy(g => g.Min(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.CreatedAt)).FirstOrDefault();
            var next = batch?.OrderBy(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.Order).FirstOrDefault(Dispatchable);
            if (next != null)
            {
                var schedule = NeedsFileWorker(AcquisitionLocalRecoveryQueueEntry.Read(next)!) ? scheduleLocalFiles : scheduleWorkflow;
                if (schedule != null) schedule(next.Id);
                else await DispatchAsync(next.Id, ct);
            }
        }
        finally {AcquisitionLocalRecoveryBatchPreviewStore.Gate.Release();}
    }

    private bool NeedsFileWorker(AcquisitionLocalRecoveryQueueEntry entry) => entry.RecoveryScope == "localFiles" ||
        _actions.GetValueOrDefault(entry.RecoveryScope) is IAcquisitionBatchFileActionProvider;

    private async Task ObserveAsync(int id, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            var (task, run) = await CurrentAsync(id, ct);
            var entry = task == null ? null : AcquisitionLocalRecoveryQueueEntry.Read(task);
            if (task == null || entry == null || entry.Status is not ("starting" or "running")) return;
            if (task.DeletedAt != null || task.Status == AcquisitionStatus.Cancelled &&
                run?.Status is not (WorkflowRunStatus.Pending or WorkflowRunStatus.Running))
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
                // Do not grant the unrelated run this task's approval.
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

    internal async Task DispatchAsync(int id, CancellationToken ct)
    {
        if (tasks?.IsShuttingDown == true) return;
        var gate = AcquisitionTaskGate.For(id); if (!await gate.WaitAsync(0, ct)) return;
        AcquisitionLocalRecoveryQueueEntry? dispatch = null;
        AcquisitionRecoveryRow? workflowRow = null;
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
                var initial = CapacityFor([new(entry.WorkingDirectories, entry.DeliveryDirectories, entry.EstimatedBytes,
                    entry.EstimateIncomplete, CheckpointWrite: entry.CheckpointWrite)], admissionMinimum);
                if (initial.Any(v => v.BlocksAdmission))
                {
                    // A previously approved queue may predate checkpoint budgets. Re-enter its existing
                    // diagnosis once; dispatch below still requires fresh matching evidence and admission.
                    var canRecheck = entry is {CheckpointWrite: null, RecoveryScope: "workflow", SelectedAction: ContinueAction} &&
                        run is {Status: WorkflowRunStatus.Waiting, WaitReason: "cloud.fileProcessing"} &&
                        StorageProblem(run) == StorageWriteGuard.ReserveLowCode;
                    if (!canRecheck || initial.Any(v => v.AvailableBytes <= v.MinimumFreeBytes))
                    {await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = StorageWriteGuard.ReserveLowCode}, ct); return;}
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = "LOCAL_BATCH_CAPACITY_UNKNOWN"}, ct); return;}
            var localAdoption = entry.RecoveryScope == "localFiles";
            var check = await CheckActionAsync(task, run, new(entry.EstimatedBytes, "retained", entry.EstimateIncomplete),
                entry.Mode, entry.SelectedAction, entry.MaxAdaptiveIterations, !localAdoption, entry.Restricted, null, ct);
            if (entry.SelectedAction == AcquisitionTransferReplayAction.Id && !entry.TransferRetryConfirmed)
            {
                await SaveAsync(task, entry with {Status = "attention", ReasonCode = AcquisitionRecoveryDiagnostics.TransferRetryConfirmationRequired}, ct);
                return;
            }
            if (check.Evidence is not { } e || e.Scope != entry.RecoveryScope || entry.CheckpointWrite != null && entry.CheckpointWrite != e.CheckpointWrite ||
                (localAdoption || !entry.VerificationPending) && e.Signal != entry.Signal || e.StorageStamp != entry.StorageStamp ||
                check.WorkflowRow?.ReasonCode == AcquisitionRecoveryDiagnostics.PausedOriginalReady && e.Signal != entry.Signal &&
                    !CloudSelectionSignal.IsPausedPreparedRetrySubset(e.Signal, entry.Signal) ||
                e.ActionCategory != entry.ActionCategory || e.ActionCategory != "localProcessing" && e.CanOverrideStorage != entry.CanOverrideStorage || e.AuthorizationStamp != entry.AuthorizationStamp ||
                !e.WorkingDirectories.SequenceEqual(entry.WorkingDirectories) || !e.DeliveryDirectories.SequenceEqual(entry.DeliveryDirectories))
            {await SaveAsync(task, entry with {Status = "attention", ReasonCode = check.Evidence == null ? check.Row.ReasonCode : "ACQUISITION_TASK_CHANGED"}, ct); return;}
            var minimum = MinimumFor(entry);
            IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> volumes;
            try {volumes = CapacityFor([Item(e, entry.EstimatedBytes)], minimum);}
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = "LOCAL_BATCH_CAPACITY_UNKNOWN"}, ct); return;}
            if (volumes.Any(v => v.BlocksAdmission))
            {await SaveAsync(task, entry with {Status = "spaceBlocked", ReasonCode = StorageWriteGuard.ReserveLowCode}, ct); return;}
            if (run != null && entry.RecoveryScope == "workflow")
                task = await GrantWhileLockedAsync(task, run, entry, ct,
                    pausedOriginal: check.WorkflowRow?.ReasonCode == AcquisitionRecoveryDiagnostics.PausedOriginalReady);
            dispatch = entry with {Status = "starting", ReasonCode = null, Signal = e.Signal, VerificationPending = false,
                CheckpointWrite = e.CheckpointWrite,
                AttemptVersion = AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run)};
            processingPlacementStamp = check.ProcessingPlacementStamp;
            workflowRow = check.WorkflowRow;
            await SaveAsync(task, dispatch, ct);
        }
        finally {gate.Release();}
        if (dispatch == null) return;

        // The persisted starting state prevents duplicate submission. Local adoption can hash
        // large receipts in its worker, under the same original run and exact approved evidence.
        try
        {
            AcquisitionRecoveryResult result;
            (int WorkflowRunId, int PlanRevision)? adaptiveRun = null;
            if (_actions.GetValueOrDefault(dispatch.RecoveryScope) is IAcquisitionBatchFileActionProvider fileAction)
            {
                try
                {
                    Task Guard(string? path) => string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : continuations!.Guard([path], true, ct);
                    adaptiveRun = await fileAction.ApplyAsync(id, dispatch.Signal, dispatch.MaxAdaptiveIterations, ct, dispatch.Restricted ? Guard : null);
                    result = new([id], []);
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    result = new([], [new(id, error.Message.StartsWith("ADAPTIVE_", StringComparison.Ordinal) ? error.Message : "ADAPTIVE_MIGRATION_FAILED")]);
                }
            }
            else if (dispatch.RecoveryScope == "localFiles") result = await ApplyLocalFilesDispatchAsync(id, dispatch, ct);
            else
            {
                var (before, beforeRun) = await CurrentAsync(id, ct);
                if (before == null || AcquisitionLocalRecoveryQueueEntry.Read(before)?.BatchId != dispatch.BatchId ||
                    AcquisitionLocalRecoveryQueueEntry.ContentVersion(before, beforeRun) != dispatch.AttemptVersion ||
                    workflowRow is not {CanContinue: true, Action: "resume" or "retry" or "start"} ||
                    (workflowRow.Action == "start") != (beforeRun == null))
                {await FinishDispatchAsync(id, dispatch, "attention", "ACQUISITION_TASK_CHANGED", ct); return;}
                result = await recovery.ApplyBatchAdmissionAsync(dispatch.RecoveryScope, workflowRow,
                    AcquisitionRecoveryService.Version(before, beforeRun), dispatch.Signal, processingPlacementStamp, dispatch.TransferRetryConfirmed, ct,
                    startingBatchId: workflowRow.Action == "start" ? dispatch.BatchId : null);
            }
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
                        : adaptiveRun is { } started ? currentRun.Id == started.WorkflowRunId && current.PlanRevision == started.PlanRevision
                        : currentRun.Id == saved.WorkflowRunId && current.PlanRevision == saved.PlanRevision;
                    if (!matches)
                    {
                        await SaveAsync(current, saved with {Status = "running", ReasonCode = "LOCAL_BATCH_OUTCOME_UNKNOWN"}, ct);
                        return;
                    }
                    var next = saved with {Status = "running", WorkflowRunId = currentRun.Id, PlanRevision = current.PlanRevision,
                        RecoveryScope = "workflow", SelectedAction = ContinueAction, TransferRetryConfirmed = false, Version = AcquisitionLocalRecoveryQueueEntry.ContentVersion(current, currentRun)};
                    if (next.UsesConfiguredReserve || next.OverrideValid(DateTimeOffset.UtcNow)) current = await GrantWhileLockedAsync(current, currentRun, next, ct);
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

    private async Task<AcquisitionRecoveryResult> ApplyLocalFilesDispatchAsync(int id, AcquisitionLocalRecoveryQueueEntry dispatch,
        CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id);
        await gate.WaitAsync(ct);
        try
        {
            var (task, run) = await CurrentAsync(id, ct);
            var saved = task == null ? null : AcquisitionLocalRecoveryQueueEntry.Read(task);
            if (tasks?.IsShuttingDown == true) return new([], [new(id, "LOCAL_BATCH_INTERRUPTED")]);
            if (task == null || task.DeletedAt != null || saved?.BatchId != dispatch.BatchId || saved.Status != "starting" ||
                saved.AttemptVersion != dispatch.AttemptVersion ||
                AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run) != dispatch.AttemptVersion)
                return new([], [new(id, "ACQUISITION_TASK_CHANGED")]);
            if (dispatch.Restricted && continuations == null) return new([], [new(id, "LOCAL_BATCH_PATH_FORBIDDEN")]);
            var outcome = await localFiles.ApplyWhileLockedAsync(id, AcquisitionRecoveryService.Version(task, run), dispatch.Signal,
                dispatch.Restricted ? GuardDirectory : null, ct);
            return new(outcome.Status == "started" ? [id] : [], [], LocalFiles: [outcome]);

            Task GuardDirectory(string? path) => string.IsNullOrWhiteSpace(path)
                ? Task.CompletedTask : continuations!.Guard([path], true, ct);
        }
        catch (UnauthorizedAccessException) {return new([], [new(id, "LOCAL_BATCH_PATH_FORBIDDEN")]);}
        catch (Exception e) when (e is ArgumentException or InvalidOperationException &&
            (e.Message.StartsWith("RECOVERY_", StringComparison.Ordinal) || e.Message.StartsWith("PLAN_", StringComparison.Ordinal)))
        {return new([], [new(id, e.Message)]);}
        finally {gate.Release();}
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
        entry.UsesConfiguredReserve ? AcquisitionSpaceReserve.Configured :
        entry.OverrideValid(DateTimeOffset.UtcNow) ? entry.ReserveMode ?? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Inherit);

    private async Task<AcquisitionTaskDbModel> GrantWhileLockedAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel run,
        AcquisitionLocalRecoveryQueueEntry entry, CancellationToken ct, bool pausedOriginal = false)
    {
        if (task.WorkflowRunId != run.Id || task.PlanRevision != entry.PlanRevision ||
            run.Status == WorkflowRunStatus.Success || run.Status == WorkflowRunStatus.Cancelled && !pausedOriginal ||
            !(entry.UsesConfiguredReserve || IsLocalRemainder(run) || entry.CanOverrideStorage && AcquisitionContinuationReserve.IsDownload(run))) return task;
        var gate = WorkflowRunMutationGate.For(run.Id); await gate.WaitAsync(ct);
        try
        {
            var (latest, latestRun) = await CurrentAsync(task.Id, ct);
            if (latest == null || latestRun == null || latest.WorkflowRunId != run.Id || latest.PlanRevision != entry.PlanRevision ||
                AcquisitionLocalRecoveryQueueEntry.Read(latest)?.BatchId != entry.BatchId) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            if (pausedOriginal && !AcquisitionDownloadContinuationService.IsPausedDownloadOwner(latest, latestRun))
                throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            var existing = AcquisitionStorageOverrideGrant.Read(latest);
            var approved = entry.UsesConfiguredReserve || entry.OverrideValid(DateTimeOffset.UtcNow);
            // An inherited selection must not silently retain a previous single-operation
            // emergency/configured choice. This marker follows the global policy; it grants
            // no new lowered-reserve permission and never renews the batch's approval.
            if (!approved && existing?.Status != "applied") return latest;
            var grant = new AcquisitionStorageOverrideGrant(run.Id, task.PlanRevision, "batch:" + entry.BatchId,
                DateTimeOffset.UtcNow, approved ? entry.OverrideExpiresAt!.Value : entry.CreatedAt.AddHours(24),
                ReserveMode: approved ? entry.ReserveMode ?? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Inherit);
            if (existing?.Token == grant.Token && (!entry.UsesConfiguredReserve ||
                existing.ReserveMode == grant.ReserveMode && existing.IsCurrentBinding(latest, latestRun, DateTimeOffset.UtcNow))) return latest;
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
    private const int LocalFilesParallelism = 2;
    private const int WorkflowAdmissionParallelism = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fileWorkers = new Dictionary<int, Task>();
        var workflowWorkers = new Dictionary<int, Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                RemoveFinished(fileWorkers);
                RemoveFinished(workflowWorkers);
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    budget.CancelAfter(TimeSpan.FromSeconds(30));
                    // Polling has a bounded time slice. Provider verification and workflow handoff
                    // can take longer, so approved admissions run in their own scope until shutdown.
                    await scope.ServiceProvider.GetRequiredService<AcquisitionLocalRecoveryBatchService>()
                        .TickAsync(fileWorkers.Keys.Concat(workflowWorkers.Keys).ToHashSet(),
                            Lane(fileWorkers, LocalFilesParallelism), Lane(workflowWorkers, WorkflowAdmissionParallelism), budget.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
                catch (OperationCanceledException) { }
                catch (Exception) {logger.LogWarning("Local acquisition recovery queue is temporarily unavailable; retained execution state will be checked again.");}
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally {await Task.WhenAll(fileWorkers.Values.Concat(workflowWorkers.Values));}

        Action<int>? Lane(Dictionary<int, Task> workers, int limit) => workers.Count >= limit ? null :
            id => workers.Add(id, Task.Run(() => DispatchApprovedAsync(id, stoppingToken)));
        static void RemoveFinished(Dictionary<int, Task> workers)
        {
            foreach (var done in workers.Where(p => p.Value.IsCompleted).Select(p => p.Key).ToArray())
                workers.Remove(done);
        }
    }

    private async Task DispatchApprovedAsync(int taskId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AcquisitionLocalRecoveryBatchService>().DispatchAsync(taskId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception) {logger.LogWarning("Acquisition batch admission did not finish; the saved dispatch state will be checked again.");}
    }
}

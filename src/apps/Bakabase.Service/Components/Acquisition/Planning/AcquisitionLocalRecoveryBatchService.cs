using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A bounded durable queue of explicit local recoveries. It never owns a downloader,
/// chooses a provider, or edits processing instructions; existing recovery supplies those checks.</summary>
public sealed partial class AcquisitionLocalRecoveryBatchService(BakabaseDbContext db,
    AcquisitionRecoveryService recovery, IAcquisitionRecoveryDiagnostics diagnostics,
    AcquisitionLocalFilesRecoveryService localFiles, AcquisitionPlanService plans,
    IBOptions<AcquisitionOptions> options, IUserStoragePolicy storage, IAcquisitionBatchCapacityReader capacity,
    AcquisitionLocalRecoveryBatchPreviewStore previews, BTaskManager? tasks = null,
    AcquisitionContinuationService? continuations = null)
{
    private sealed record Checked(AcquisitionLocalRecoveryBatchPreviewRow Row,
        AcquisitionLocalRecoveryBatchPreviewStore.Evidence? Evidence = null, string? ProcessingPlacementStamp = null);

    public async Task<AcquisitionLocalRecoveryBatchCapacityPreview> PreviewCapacityAsync(AcquisitionLocalRecoveryBatchCapacityInput input,
        CancellationToken ct = default, Func<string?, Task>? guardDirectory = null)
    {
        var ids = Ids(input.TaskIds).Order().ToArray();
        var entry = previews.Get(input.PreviewToken);
        if (ids.Any(id => !entry.Evidence.ContainsKey(id))) throw new ArgumentException("LOCAL_BATCH_SELECTION_INVALID");
        RequireCurrentReserve(entry);
        foreach (var path in ids.SelectMany(id => entry.Evidence[id].WorkingDirectories.Concat(entry.Evidence[id].DeliveryDirectories)).Distinct())
        {ct.ThrowIfCancellationRequested(); if (guardDirectory != null) await guardDirectory(path);}
        var volumes = SelectionCapacity(entry, ids, input.ReserveMode ?? AcquisitionSpaceReserve.Inherit);
        return new(input.PreviewToken, ids, volumes, volumes.Any(v => v.ExceedsUsable), volumes.Any(v => v.EstimateIncomplete));
    }

    public async Task<AcquisitionLocalRecoveryBatchPreview> PreviewAsync(AcquisitionLocalRecoveryBatchPreviewInput input,
        CancellationToken ct = default, Func<string?, Task>? guardDirectory = null, bool restricted = false)
    {
        var ids = Ids(input.TaskIds);
        var capacityOnly = input.Mode == "capacityOnly";
        if (capacityOnly && ids.Length != 1 || input.Mode is not ("localOnly" or "automatic" or "capacityOnly") ||
            input.Order is not ("sizeAsc" or "sizeDesc" or "selection"))
            throw new ArgumentException("LOCAL_BATCH_SELECTION_INVALID");
        var mode = capacityOnly ? "automatic" : input.Mode;
        var queueStatuses = capacityOnly ? null : (await QueueRowsAsync(ct)).GroupBy(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.BatchId)
            .ToDictionary(g => g.Key, g => Project(g.ToArray()).Status);
        var estimates = await new AcquisitionSizeEstimateProjection(db).ReadAsync(ids, ct);
        var rows = new List<AcquisitionLocalRecoveryBatchPreviewRow>();
        var evidence = new Dictionary<int, AcquisitionLocalRecoveryBatchPreviewStore.Evidence>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        foreach (var id in ids)
        {
            var estimate = estimates.GetValueOrDefault(id) ?? new(null, "unknown");
            try
            {
                using var item = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                item.CancelAfter(TimeSpan.FromSeconds(4));
                var (task, run) = await CurrentAsync(id, item.Token);
                if (task == null) {rows.Add(new(id, null, null, false, "ACQUISITION_TASK_NOT_FOUND", null, true, null, null, null)); continue;}
                var queue = queueStatuses == null ? null : await QueueReference(task, run, queueStatuses, item.Token);
                if (queue != null && (mode != "automatic" || !queue.CanRequeue))
                {rows.Add(new(id, task.Title, run?.Id, false, "LOCAL_BATCH_ALREADY_QUEUED", estimate.Bytes, estimate.Partial, null, null, null, Queue: queue)); continue;}
                if (!capacityOnly && !await CanReplaceQueue(task, run, item.Token))
                {rows.Add(new(id, task.Title, run?.Id, false, "LOCAL_BATCH_OUTCOME_UNKNOWN", estimate.Bytes, estimate.Partial, null, null, null, Queue: queue)); continue;}
                var checkedTask = capacityOnly && AcquisitionContinuationReserve.IsDownload(run)
                    ? await DownloadCapacityAsync(task, run!, estimate, item.Token, guardDirectory)
                    : await CheckAsync(task, run, estimate, item.Token, guardDirectory, mode,
                        verifyFiles: mode != "automatic", restricted: restricted);
                rows.Add(checkedTask.Row with {Queue = queue});
                if (checkedTask.Evidence is { } valid) evidence[id] = valid with {QueueStamp = QueueStamp(task)};
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {rows.Add(new(id, null, null, false, "RECOVERY_CHECK_INCOMPLETE", estimate.Bytes, true, null, null, null));}
        }
        var ordered = input.Order == "selection" ? rows.ToArray() : rows.OrderBy(r => r.EstimatedBytes == null)
            .ThenBy(r => input.Order == "sizeDesc" ? -r.EstimatedBytes : r.EstimatedBytes).ThenBy(r => r.TaskId).ToArray();
        var settings = options.Value;
        var minimum = AcquisitionSpaceReserve.Minimum(settings);
        IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> volumes;
        try
        {
            volumes = CapacityFor(ordered.Where(r => evidence.ContainsKey(r.TaskId)).Select(r =>
                Item(evidence[r.TaskId], r.EstimatedBytes)), minimum);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Missing/unmounted volumes have no override path. Do not report an unknown
            // capacity as zero free bytes or invite a reserve exemption for an I/O failure.
            ordered = ordered.Select(r => r.CanQueue ? r with {CanQueue = false, ReasonCode = "LOCAL_BATCH_CAPACITY_UNKNOWN"} : r).ToArray();
            evidence.Clear(); volumes = [];
        }
        if (capacityOnly)
        {
            // A live capacity read never admits a queue entry or consumes a confirmation token.
            return new("", DateTimeOffset.UtcNow, ordered.Select(r => r with {CanQueue = false}).ToArray(),
                volumes, volumes.Any(v => v.ExceedsUsable), volumes.Any(v => v.EstimateIncomplete),
                settings.UseEmergencySpaceReserve, settings.MinimumFreeSpaceBytes);
        }
        var preview = new AcquisitionLocalRecoveryBatchPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10),
            ordered, volumes, volumes.Any(v => v.ExceedsUsable), volumes.Any(v => v.EstimateIncomplete),
            settings.UseEmergencySpaceReserve, settings.MinimumFreeSpaceBytes);
        previews.Add(new(preview, evidence, settings.MinimumFreeSpaceBytes));
        return preview;
    }

    public async Task<AcquisitionLocalRecoveryBatch> ApplyAsync(AcquisitionLocalRecoveryBatchApplyInput input,
        CancellationToken ct = default, Func<string?, Task>? guardDirectory = null, bool restricted = false)
    {
        var selected = Ids(input.TaskIds).Order().ToArray();
        var mode = input.ReserveMode ?? (input.AllowStorageOverride ? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Inherit);
        if (input.AllowStorageOverride && mode != AcquisitionSpaceReserve.Emergency) throw new ArgumentException("LOCAL_BATCH_SELECTION_INVALID");
        await AcquisitionLocalRecoveryBatchPreviewStore.Gate.WaitAsync(ct);
        var locks = new List<SemaphoreSlim>();
        var runLocks = new List<SemaphoreSlim>();
        try
        {
            var entry = previews.Get(input.PreviewToken);
            if (selected.Any(id => !entry.Evidence.ContainsKey(id))) throw new ArgumentException("LOCAL_BATCH_SELECTION_INVALID");
            if (entry.BatchId != null)
            {
                if (entry.ReserveMode != mode || !selected.SequenceEqual(entry.Selected!))
                    throw new ArgumentException("LOCAL_BATCH_SELECTION_INVALID");
                return await GetAsync(entry.BatchId, ct);
            }
            foreach (var id in selected)
            {
                var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct); locks.Add(gate);
            }
            // Time and policy are rechecked after waiting for all owner gates.
            previews.Get(input.PreviewToken);
            RequireCurrentReserve(entry);
            if (mode == AcquisitionSpaceReserve.Emergency && entry.MinimumFreeBytes <= AcquisitionStorageOverrideGrant.EmergencyMinimumFreeBytes)
                throw new ArgumentException("STORAGE_OVERRIDE_NOT_NEEDED");
            if (mode != AcquisitionSpaceReserve.Inherit && selected.All(id => ModeFor(entry.Evidence[id], mode) == AcquisitionSpaceReserve.Inherit))
                throw new ArgumentException("STORAGE_OVERRIDE_NOT_NEEDED");
            var current = new Dictionary<int, AcquisitionTaskDbModel>();
            foreach (var id in selected)
            {
                var (task, run) = await CurrentAsync(id, ct);
                if (task == null || QueueStamp(task) != entry.Evidence[id].QueueStamp ||
                    !await CanReplaceQueue(task, run, ct) ||
                    AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run) != entry.Evidence[id].Version)
                    throw new ArgumentException("ACQUISITION_TASK_CHANGED");
                var e = entry.Evidence[id];
                if (e.AuthorizationStamp != null)
                {
                    if (continuations == null) throw new ArgumentException("ACQUISITION_TASK_CHANGED");
                    var paths = await continuations.CapturePaths(task, run, "{}", ct);
                    if (Hash(paths) != e.AuthorizationStamp) throw new ArgumentException("ACQUISITION_TASK_CHANGED");
                    await continuations.Guard(paths, restricted || e.Restricted, ct);
                    foreach (var path in paths) if (guardDirectory != null) await guardDirectory(path);
                }
                foreach (var path in e.WorkingDirectories.Concat(e.DeliveryDirectories))
                    if (guardDirectory != null) await guardDirectory(path);
                current[id] = task;
            }
            // Snapshot approval authorizes only the captured destinations, and only reserve
            // relaxation. Actual per-volume capacity and policy are still checked by the worker.
            SelectionCapacity(entry, selected, mode);
            var batchId = Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            // Preserve task -> run -> database ordering. A normal workflow resumer owns
            // its run gate while writing SQLite; never wait for that gate as a writer.
            var heldRunIds = current.Values.SelectMany(t => new[] {t.WorkflowRunId,
                AcquisitionLocalRecoveryQueueEntry.Read(t)?.WorkflowRunId, AcquisitionContinuationEntry.Read(t)?.WorkflowRunId})
                .Where(id => id != null).Select(id => id!.Value).Distinct().Order().ToArray();
            foreach (var runId in heldRunIds)
            {var gate = WorkflowRunMutationGate.For(runId); await gate.WaitAsync(ct); runLocks.Add(gate);}
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Recheck after obtaining SQLite's write transaction as well as the process owner
            // gates. A workflow resumed through its own run API cannot slip into a takeover.
            foreach (var id in selected)
            {
                var (fresh, run) = await CurrentAsync(id, ct);
                if (fresh == null || QueueStamp(fresh) != entry.Evidence[id].QueueStamp ||
                    AcquisitionLocalRecoveryQueueEntry.ContentVersion(fresh, run) != entry.Evidence[id].Version ||
                    !await CanReplaceQueue(fresh, run, ct)) throw new ArgumentException("ACQUISITION_TASK_CHANGED");
                current[id] = fresh;
            }
            var ordered = entry.Preview.Rows.Where(r => selected.Contains(r.TaskId)).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var row = ordered[index]; var e = entry.Evidence[row.TaskId]; var task = current[row.TaskId];
                if (e.Mode == "automatic") task = await RetireSelectedQueue(task, heldRunIds, ct);
                var rowMode = ModeFor(e, mode);
                var permit = rowMode != AcquisitionSpaceReserve.Inherit;
                var state = new AcquisitionLocalRecoveryQueueEntry {BatchId = batchId, Version = e.Version, Signal = e.Signal,
                    RecoveryScope = e.Scope, Mode = e.Mode, ActionCategory = e.ActionCategory, VerificationPending = e.VerificationPending,
                    AuthorizationStamp = e.AuthorizationStamp, Restricted = restricted || e.Restricted, StorageStamp = e.StorageStamp, WorkingDirectories = e.WorkingDirectories,
                    DeliveryDirectories = e.DeliveryDirectories, CreatedAt = now, UpdatedAt = now, Order = index,
                    WorkflowRunId = task.WorkflowRunId, PlanRevision = task.PlanRevision,
                    AllowStorageOverride = rowMode == AcquisitionSpaceReserve.Emergency, ReserveMode = rowMode, CanOverrideStorage = e.CanOverrideStorage,
                    OverrideExpiresAt = permit ? now.AddHours(24) : null, EstimatedBytes = row.EstimatedBytes,
                    EstimateIncomplete = e.EstimateIncomplete, MinimumFreeBytes = entry.MinimumFreeBytes};
                var (_, run) = await CurrentAsync(task.Id, ct);
                state = state with {Version = AcquisitionLocalRecoveryQueueEntry.ContentVersion(task with
                    {BuilderStateJson = AcquisitionLocalRecoveryQueueEntry.Write(task.BuilderStateJson, state)}, run)};
                await SaveAsync(task, state, ct);
            }
            await transaction.CommitAsync(ct);
            entry.Selected = selected; entry.ReserveMode = mode; entry.BatchId = batchId;
            return await GetAsync(batchId, ct);
        }
        finally
        {
            foreach (var gate in runLocks.AsEnumerable().Reverse()) gate.Release();
            foreach (var gate in locks.AsEnumerable().Reverse()) gate.Release();
            AcquisitionLocalRecoveryBatchPreviewStore.Gate.Release();
        }
    }

    public async Task<IReadOnlyList<AcquisitionLocalRecoveryBatch>> ListAsync(CancellationToken ct = default)
    {
        var rows = await QueueRowsAsync(ct);
        return WithHistory(rows).GroupBy(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.BatchId).Select(g => Project(g.ToArray()))
            .OrderByDescending(b => b.Status is "queued" or "running" or "paused" or "spaceBlocked")
            .ThenByDescending(b => b.CreatedAt).Take(30).ToArray();
    }
    public async Task<AcquisitionLocalRecoveryBatch> GetAsync(string batchId, CancellationToken ct = default)
    {
        RequireId(batchId);
        var rows = WithHistory(await QueueRowsAsync(ct)).Where(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.BatchId == batchId).ToArray();
        if (rows.Length == 0) throw new ArgumentException("LOCAL_BATCH_NOT_FOUND");
        return Project(rows);
    }
    public async Task<AcquisitionLocalRecoveryBatch> CommandAsync(string batchId, string action, CancellationToken ct = default,
        Func<string?, Task>? guardDirectory = null)
    {
        RequireId(batchId);
        if (action is not ("pause" or "resume" or "cancel")) throw new ArgumentException("LOCAL_BATCH_ACTION_INVALID");
        await AcquisitionLocalRecoveryBatchPreviewStore.Gate.WaitAsync(ct);
        try
        {
            var rows = (await QueueRowsAsync(ct)).Where(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!.BatchId == batchId).OrderBy(t => t.Id).ToArray();
            if (rows.Length == 0) throw new ArgumentException("LOCAL_BATCH_NOT_FOUND");
            // Authorization is an all-selection preflight, not a partially applied resume.
            // Current evidence is guarded again below before changing each parked item.
            if (action == "resume" && guardDirectory != null)
                foreach (var path in rows.Select(t => AcquisitionLocalRecoveryQueueEntry.Read(t)!)
                    .Where(e => e.Status is "paused" or "spaceBlocked")
                    .SelectMany(e => e.WorkingDirectories.Concat(e.DeliveryDirectories)).Distinct(ArtifactManifestFiles.PathComparer))
                    await guardDirectory(path);
            foreach (var original in rows)
            {
                var gate = AcquisitionTaskGate.For(original.Id); await gate.WaitAsync(ct);
                try
                {
                    var (task, run) = await CurrentAsync(original.Id, ct);
                    var state = task == null ? null : AcquisitionLocalRecoveryQueueEntry.Read(task);
                    if (state == null || state.BatchId != batchId || !state.IsOpen || state.Status is "running" or "starting") continue;
                    if (action == "cancel")
                    {
                        task = await RevokeBatchGrantWhileLockedAsync(task!, state, "LOCAL_BATCH_CANCELLED", ct);
                        await SaveAsync(task, state with {Status = "cancelled", ReasonCode = null}, ct);
                    }
                    else if (action == "pause") await SaveAsync(task!, state with {Status = "paused"}, ct);
                    else if (state.Status is "paused" or "spaceBlocked")
                    {
                        // A new explicit continue may refresh evidence for the same execution;
                        // it never renews an expired exemption or adopts another revision.
                        if (task!.WorkflowRunId != state.WorkflowRunId || task.PlanRevision != state.PlanRevision)
                        {await SaveAsync(task, state with {Status = "skipped", ReasonCode = "ACQUISITION_TASK_CHANGED"}, ct); continue;}
                        var check = await CheckAsync(task, run, new(state.EstimatedBytes, "retained", state.EstimateIncomplete), ct, guardDirectory, state.Mode, restricted: state.Restricted);
                        if (check.Evidence is not { } e)
                        {await SaveAsync(task, state with {Status = "attention", ReasonCode = check.Row.ReasonCode}, ct); continue;}
                        await SaveAsync(task, state with {Status = "queued", Version = e.Version, Signal = e.Signal,
                            RecoveryScope = e.Scope, ReasonCode = null, AttemptVersion = null, StorageStamp = e.StorageStamp,
                            WorkingDirectories = e.WorkingDirectories, DeliveryDirectories = e.DeliveryDirectories,
                            AuthorizationStamp = e.AuthorizationStamp, ActionCategory = e.ActionCategory, VerificationPending = e.VerificationPending}, ct);
                    }
                }
                finally {gate.Release();}
            }
            return await GetAsync(batchId, ct);
        }
        finally {AcquisitionLocalRecoveryBatchPreviewStore.Gate.Release();}
    }

    private async Task<Checked> CheckAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run, AcquisitionSizeEstimate estimate,
        CancellationToken ct, Func<string?, Task>? guardDirectory = null, string mode = "localOnly", bool verifyFiles = true, bool restricted = false)
    {
        Checked Block(string reason) => new(new(task.Id, task.Title, run?.Id, false, reason, estimate.Bytes, estimate.Partial, null, null, null));
        if (task.DeletedAt != null) return Block("ACQUISITION_TASK_DELETED");
        if (task.Status is AcquisitionStatus.Completed or AcquisitionStatus.Cancelled) return Block("RECOVERY_ALREADY_COMPLETED");
        if (run != null && (run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running ||
            run.PendingSignalJson != null || tasks?.IsPending("workflow.run." + run.Id) == true)) return Block("ACQUISITION_TASK_ACTIVE");
        try
        {
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "{}", WorkflowJson.Options);
            if (plan?.Parts is not {Count: > 0 and <= 100} || task.BuilderId == null) return Block("RECOVERY_LOCAL_FILES_UNAVAILABLE");
            AcquisitionRecoveryDiagnosis diagnosis;
            string scope;
            var category = "localProcessing";
            string? authorizationStamp = null;
            if (mode == "automatic" && continuations != null)
            {
                var directories = await continuations.CapturePaths(task, run, "{}", ct);
                await continuations.Guard(directories, restricted, ct);
                foreach (var directory in directories) if (guardDirectory != null) await guardDirectory(directory);
                authorizationStamp = Hash(directories);
            }
            var preferCurrentStorage = false;
            // Adoption starts a new revision from the original download. A later local cursor
            // must retain the current attempt, including the user's confirmed processing plan.
            if (plans != null && run != null && AcquisitionLocalFilesRecoveryReceipt.Awaiting(run))
            {
                var currentStorage = plans.CaptureLocalFilesStorage(task);
                preferCurrentStorage = plan.DeliveryLayout == null ||
                    !ArtifactManifestFiles.PathComparer.Equals(plan.DeliveryLayout.RootDirectory, currentStorage.Delivery.RootDirectory) ||
                    plan.Parts.Any(p => !ArtifactManifestFiles.PathComparer.Equals(
                        ArtifactStoragePolicies.Resolve(plan.StoragePolicy ?? new(), p.StoragePolicy).ProcessingWorkspaceDirectory,
                        currentStorage.Policy.ProcessingWorkspaceDirectory));
            }
            // A download cursor is a local remainder only when its existing files are complete.
            // Changed workspace defaults must not turn an original download retry into adoption
            // of files that do not exist yet. Metadata previews defer the provider recheck to dispatch.
            AcquisitionRecoveryDiagnosis? originalDownloadDiagnosis = null;
            if (mode == "automatic" && continuations != null && preferCurrentStorage &&
                run != null && AcquisitionLocalFilesRecoveryReceipt.Awaiting(run))
            {
                var candidate = await diagnostics.DiagnoseAsync(task, run, ct, verifyFiles);
                if (!candidate.Row.CanContinue && candidate.Row.ManualAction == "inspectSource" &&
                    candidate.Row.CurrentProblemCode != null)
                    return Block(candidate.Row.ReasonCode == "RECOVERY_SOURCE_STILL_BLOCKED"
                        ? candidate.Row.CurrentProblemCode : candidate.Row.ReasonCode);
                if (!verifyFiles && candidate.Row.ReasonCode == "RECOVERY_RECHECK_SOURCE" ||
                    candidate.Row.CanContinue && candidate.Row.Action == "resume" &&
                    AcquisitionDownloadOnlyRecoveryService.Targets(run, candidate.SignalJson) != null)
                    originalDownloadDiagnosis = candidate;
            }
            if (originalDownloadDiagnosis != null)
            {
                diagnosis = originalDownloadDiagnosis;
                scope = "workflow"; category = "workflow";
            }
            else if (mode == "automatic" && run != null && !IsLocalRemainder(run))
            {
                if (continuations == null) return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");
                diagnosis = await diagnostics.DiagnoseAsync(task, run, ct, verifyFiles);
                if (!(diagnosis.Row.CanContinue && diagnosis.Row.Action is "resume" or "retry") &&
                    !(mode == "automatic" && !verifyFiles && diagnosis.Row.ReasonCode is "RECOVERY_VERIFY_FILES" or "RECOVERY_RECHECK_SOURCE"))
                    return Block(diagnosis.Row.ReasonCode);
                scope = "workflow"; category = "workflow";
            }
            else if (IsLocalRemainder(run) && !preferCurrentStorage)
            {
                diagnosis = await diagnostics.DiagnoseAsync(task, run, ct, verifyFiles);
                var pending = mode == "automatic" && !verifyFiles && diagnosis.Row.ReasonCode is "RECOVERY_VERIFY_FILES" or "RECOVERY_RECHECK_SOURCE";
                if (!pending && (!diagnosis.Row.CanContinue || diagnosis.Row.Action is not ("resume" or "retry")))
                    return Block(diagnosis.Row.ReasonCode);
                if (pending && diagnosis.Row.ReasonCode == "RECOVERY_RECHECK_SOURCE" || !pending && !IsLocalSignal(run!, diagnosis.SignalJson))
                {
                    if (mode != "automatic" || continuations == null) return Block(diagnosis.Row.ReasonCode);
                    category = "workflow";
                }
                scope = "workflow";
            }
            else
            {
                diagnosis = await localFiles.DiagnoseAsync(task, run, verifyFiles, ct, guardDirectory);
                if ((!diagnosis.Row.CanContinue && !(mode == "automatic" && !verifyFiles && diagnosis.Row.ReasonCode == "RECOVERY_VERIFY_FILES" && diagnosis.Row.LocalFiles != null)) || diagnosis.Row.Action != "localFiles")
                    return Block(preferCurrentStorage ? "LOCAL_BATCH_STORAGE_REVIEW_REQUIRED" : diagnosis.Row.ReasonCode);
                scope = "localFiles";
            }
            string[] working, delivery;
            string storageStamp;
            if (originalDownloadDiagnosis != null)
            {
                // The currently authorized write is to the persisted download destination.
                // Future processing/publication retain their frozen plan and their own write guards.
                working = await DownloadDirectoriesAsync(run!, ct);
                delivery = [];
                storageStamp = Hash(new {OriginalDownloadDirectories = working});
            }
            else if (scope == "localFiles")
            {
                var settings = plans.CaptureLocalFilesStorage(task);
                working = [settings.Policy.ProcessingWorkspaceDirectory];
                delivery = diagnosis.Row.LocalFiles!.Parts.Select(p => p.DeliveryDirectory).Distinct(ArtifactManifestFiles.PathComparer).ToArray();
                storageStamp = AcquisitionLocalFilesRecoveryReceipt.Hash(settings);
            }
            else
            {
                var remainder = WorkflowRunPlans.Read(run!.PlanSnapshotJson)!.Nodes.Skip(run.CurrentStepIndex!.Value).ToArray();
                var processing = remainder.Any(n => n.Kind is AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind);
                working = processing ? plan.Parts.Select(p => ArtifactStoragePolicies.Resolve(plan.StoragePolicy ?? new(), p.StoragePolicy).ProcessingWorkspaceDirectory)
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(ArtifactManifestFiles.PathComparer).ToArray() : [];
                delivery = plan.DeliveryLayout?.PartDirectories.Values.Distinct(ArtifactManifestFiles.PathComparer).ToArray() ?? [];
                if (diagnosis.ProcessingStorageRoots.Count > 0)
                {
                    var placement = InspectCheckpointPlacement(run, remainder, diagnosis);
                    if (placement.Reason != null) return Block(placement.Reason);
                    (working, delivery) = (placement.Working, placement.Delivery);
                }
                storageStamp = diagnosis.ProcessingPlacementStamp ?? Hash(new {plan.StoragePolicy, plan.DeliveryLayout});
                if (processing && working.Length == 0) return Block("RECOVERY_LOCAL_STORAGE_REQUIRED");
            }
            if (category == "workflow" && AcquisitionContinuationReserve.IsDownload(run))
                working = working.Concat(await DownloadDirectoriesAsync(run!, ct))
                    .Distinct(ArtifactManifestFiles.PathComparer).ToArray();
            if (delivery.Length == 0 && originalDownloadDiagnosis == null) return Block("RECOVERY_LOCAL_STORAGE_REQUIRED");
            foreach (var path in working.Concat(delivery))
            {
                storage.EnsurePathAllowed(path); ArtifactManifestFiles.RejectLinks(path);
                if (guardDirectory != null) await guardDirectory(path);
            }
            var incomplete = estimate.Partial || estimate.Bytes == null || working.Length > 0;
            return new(new(task.Id, task.Title, run?.Id, true, "LOCAL_BATCH_READY", estimate.Bytes, incomplete, scope,
                working.FirstOrDefault(), delivery.FirstOrDefault(), category, CanOverrideStorage: category == "localProcessing" || AcquisitionContinuationReserve.IsDownload(run),
                CurrentStepKind: diagnosis.Row.CurrentStepKind, RecoveryAction: diagnosis.Row.Action ?? "verifyAndResume"),
                new(AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run), diagnosis.SignalJson, scope,
                    storageStamp, working, delivery, incomplete, mode, category, VerificationPending: !diagnosis.Row.CanContinue,
                    AuthorizationStamp: authorizationStamp, Restricted: restricted,
                    CanOverrideStorage: category == "localProcessing" || AcquisitionContinuationReserve.IsDownload(run)), diagnosis.ProcessingPlacementStamp);
        }
        catch (UnauthorizedAccessException) {return Block("LOCAL_BATCH_PATH_FORBIDDEN");}
        catch (Exception e) when (e is JsonException or ArgumentException or IOException or InvalidOperationException)
        {return Block("RECOVERY_LOCAL_FILES_UNAVAILABLE");}
    }

    // Use the same frozen publication and target overrides as the running activities.
    // The editable task plan and today's defaults do not own an existing attempt.
    private static (string[] Working, string[] Delivery, string? Reason) InspectCheckpointPlacement(
        WorkflowRunDbModel run, WorkflowRunPlanNode[] remainder, AcquisitionRecoveryDiagnosis diagnosis)
    {
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
        var publication = remainder.SingleOrDefault(n => n.Kind == "fs.publishArtifacts");
        var destination = publication == null ? null :
            JsonSerializer.Deserialize<FileArtifactPublicationConfig>(publication.ConfigJson, WorkflowJson.Options);
        if (workset == null || destination == null) return ([], [], "LOCAL_BATCH_STORAGE_REVIEW_REQUIRED");
        var targets = workset.Targets.Where(t => !t.Excluded).ToArray();
        if (targets.Any(t => t.PublishedDirectory == null && t.PublicationTargetDirectory == null &&
                string.IsNullOrWhiteSpace(destination.TargetDirectory) && destination.TargetDirectories?.ContainsKey(t.Id) != true))
            return ([], [], "LOCAL_BATCH_STORAGE_REVIEW_REQUIRED");
        var delivery = targets.Select(t => t.PublishedDirectory ?? t.PublicationTargetDirectory ??
            PublishFileArtifactsActivity.CloudTargetDirectory(workset, t, destination)).Distinct(ArtifactManifestFiles.PathComparer).ToArray();
        var roots = new HashSet<string>(diagnosis.ProcessingStorageRoots, ArtifactManifestFiles.PathComparer);
        foreach (var node in remainder.Where(n => n.Kind is AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind))
        {
            var config = node.Kind == AcquisitionPlanProcessingActivity.ActivityKind ?
                JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(node.ConfigJson, WorkflowJson.Options) : null;
            var targetId = config?.TargetId ?? JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(node.ConfigJson, WorkflowJson.Options)?.TargetId;
            var target = workset.Targets.SingleOrDefault(t => t.Id == targetId);
            if (target == null) return ([], [], "RECOVERY_CHECKPOINT_UNSUPPORTED");
            if (target.Excluded) continue;
            var path = (config?.StoragePolicy ?? target.StoragePolicy)?.ProcessingWorkspaceDirectory;
            if (!string.IsNullOrWhiteSpace(path)) roots.Add(path);
            else if (target.Id != diagnosis.ProcessingPartId) return ([], [], "RECOVERY_LOCAL_STORAGE_REQUIRED");
        }
        return (roots.Order(ArtifactManifestFiles.PathComparer).ToArray(), delivery, null);
    }

    internal static bool IsLocalRemainder(WorkflowRunDbModel? run, bool allowContinueFiles = false)
    {
        if (run?.CurrentStepIndex is not { } index || index < 0) return false;
        try
        {
            return IsLocalRemainder(WorkflowRunPlans.Read(run.PlanSnapshotJson), index, allowContinueFiles,
                allowDownloadWait: true);
        }
        catch (InvalidDataException) {return false;}
    }
    internal static bool IsLocalRemainder(WorkflowRunPlan? plan, int index, bool allowContinueFiles, bool allowDownloadWait)
    {
        if (plan == null || index < 0 || index >= plan.Nodes.Count) return false;
        return plan.Nodes.Skip(index).Select((node, i) => (node, i)).All(x => x.node.Kind is
            AcquisitionPlanProcessingActivity.ActivityKind or "fs.publishArtifacts" or AssessFileArtifactDeliveryActivity.ActivityKind ||
            allowContinueFiles && x.i == 0 && x.node.Kind == AcquisitionPlanContinueFilesActivity.ActivityKind ||
            x.i > 0 && x.node.Kind == "download.cleanupArtifacts" ||
            allowDownloadWait && x.i == 0 && x.node.Kind == "download.awaitCompletion");
    }

    private static bool IsLocalSignal(WorkflowRunDbModel run, string signal)
    {
        if (WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes[run.CurrentStepIndex!.Value].Kind != "download.awaitCompletion") return true;
        using var json = JsonDocument.Parse(signal);
        return json.RootElement.TryGetProperty("observeExistingOnly", out var observe) && observe.ValueKind == JsonValueKind.True &&
               json.RootElement.EnumerateObject().All(p => p.Name == "observeExistingOnly");
    }
    private IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> CapacityFor(IEnumerable<AcquisitionLocalRecoveryBatchCapacity.Item> items, long minimum) =>
        AcquisitionLocalRecoveryBatchCapacity.Inspect(items, minimum, capacity, storage);
    private void RequireCurrentReserve(AcquisitionLocalRecoveryBatchPreviewStore.Entry entry)
    {
        if (options.Value.MinimumFreeSpaceBytes != entry.MinimumFreeBytes ||
            options.Value.UseEmergencySpaceReserve != entry.Preview.UseEmergencySpaceReserve)
            throw new ArgumentException("RECOVERY_STORAGE_SETTINGS_CHANGED");
    }

    private static string ModeFor(AcquisitionLocalRecoveryBatchPreviewStore.Evidence evidence, string mode) =>
        evidence.ActionCategory == "localProcessing" || evidence.CanOverrideStorage ? mode : AcquisitionSpaceReserve.Inherit;

    private IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> SelectionCapacity(AcquisitionLocalRecoveryBatchPreviewStore.Entry entry,
        int[] ids, string mode)
    {
        var settings = options.Value;
        _ = AcquisitionSpaceReserve.Minimum(settings, mode);
        if (mode == AcquisitionSpaceReserve.Configured && settings.UseEmergencySpaceReserve &&
            settings.MinimumFreeSpaceBytes < AcquisitionStorageOverrideGrant.EmergencyMinimumFreeBytes)
            throw new ArgumentException("STORAGE_OVERRIDE_NOT_NEEDED");
        return CapacityFor(ids.Select(id =>
        {
            var evidence = entry.Evidence[id];
            return Item(evidence, entry.Preview.Rows.Single(row => row.TaskId == id).EstimatedBytes) with
                {MinimumFreeBytes = AcquisitionSpaceReserve.Minimum(settings, ModeFor(evidence, mode))};
        }), AcquisitionSpaceReserve.Minimum(settings));
    }

    private static AcquisitionLocalRecoveryBatchCapacity.Item Item(AcquisitionLocalRecoveryBatchPreviewStore.Evidence evidence, long? bytes) =>
        new(evidence.WorkingDirectories, evidence.DeliveryDirectories, bytes, evidence.EstimateIncomplete);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, WorkflowJson.Options))));
    private static int[] Ids(IReadOnlyList<int>? ids)
    {
        if (ids == null || ids.Count is 0 or > 100 || ids.Any(id => id < 1)) throw new ArgumentException("LOCAL_BATCH_SELECTION_INVALID");
        return ids.Distinct().ToArray();
    }
    private static void RequireId(string id)
    {if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("LOCAL_BATCH_NOT_FOUND");}
    private async Task<(AcquisitionTaskDbModel?, WorkflowRunDbModel?)> CurrentAsync(int id, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        var run = task?.WorkflowRunId is { } runId ? await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        return (task, run);
    }
    private async Task SaveAsync(AcquisitionTaskDbModel task, AcquisitionLocalRecoveryQueueEntry entry, CancellationToken ct)
    {
        var json = AcquisitionLocalRecoveryQueueEntry.Write(task.BuilderStateJson, entry with {UpdatedAt = DateTimeOffset.UtcNow});
        if (await db.AcquisitionTasks.Where(t => t.Id == task.Id && t.BuilderStateJson == task.BuilderStateJson &&
                t.PlanRevision == task.PlanRevision && t.WorkflowRunId == task.WorkflowRunId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json), ct) != 1)
            throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
    }
    private async Task<AcquisitionTaskDbModel[]> QueueRowsAsync(CancellationToken ct) =>
        (await db.AcquisitionTasks.AsNoTracking().Where(t => t.BuilderStateJson != null &&
            t.BuilderStateJson.Contains("\"localRecoveryQueue\""))
            .Select(t => new AcquisitionTaskDbModel {Id = t.Id, Title = t.Title, BuilderStateJson = t.BuilderStateJson,
                WorkflowRunId = t.WorkflowRunId, PlanRevision = t.PlanRevision, Status = t.Status, DeletedAt = t.DeletedAt})
            .ToArrayAsync(ct)).Where(t => AcquisitionLocalRecoveryQueueEntry.Read(t) != null).ToArray();
    private static IEnumerable<AcquisitionTaskDbModel> WithHistory(IEnumerable<AcquisitionTaskDbModel> rows)
    {
        foreach (var task in rows)
        {
            yield return task;
            var history = JsonNode.Parse(task.BuilderStateJson!)?["localRecoveryQueueHistory"] as JsonArray;
            if (history == null) continue;
            foreach (var previous in history)
                if (previous?.Deserialize<AcquisitionLocalRecoveryQueueEntry>(WorkflowJson.Options) is { } state)
                    yield return task with {BuilderStateJson = new JsonObject
                        {["localRecoveryQueue"] = JsonSerializer.SerializeToNode(state, WorkflowJson.Options)}.ToJsonString(WorkflowJson.Options)};
        }
    }
    private static AcquisitionLocalRecoveryBatch Project(AcquisitionTaskDbModel[] tasks)
    {
        var rows = tasks.Select(t => (Task: t, State: AcquisitionLocalRecoveryQueueEntry.Read(t)!)).OrderBy(t => t.State.Order).ToArray();
        var first = rows[0].State;
        var status = rows.Any(r => r.State.Status is "running" or "starting") ? "running" :
            rows.Any(r => r.State.Status == "spaceBlocked") ? "spaceBlocked" :
            rows.Any(r => r.State.Status == "paused") ? "paused" :
            rows.Any(r => r.State.Status == "queued") ? "queued" :
            rows.Any(r => r.State.Status == "cancelled") ? "cancelled" : "completed";
        return new(first.BatchId, first.CreatedAt, rows.Max(r => r.State.UpdatedAt), status,
            rows.Any(r => r.State.AllowStorageOverride), rows.Where(r => r.State.AllowStorageOverride).Select(r => r.State.OverrideExpiresAt).Min(),
            rows.Select(r => new AcquisitionLocalRecoveryBatchRow(r.Task.Id, r.Task.Title, r.State.Order, r.State.Status,
                r.State.WorkflowRunId, r.State.EstimatedBytes, r.State.ReasonCode)).ToArray(),
            rows.Where(r => r.State.Status is "running" or "starting").Select(r => (int?)r.Task.Id).FirstOrDefault());
    }
}

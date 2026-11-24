using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed class AcquisitionRecoveryPreviewStore
{
    internal sealed record Entry(AcquisitionRecoveryPreview Preview, IReadOnlyDictionary<int, string> Versions,
        IReadOnlyDictionary<int, string> Signals)
    {
        internal bool Claimed;
        internal int[]? Selected;
        internal bool? UseEmergencySpaceReserve;
        internal bool AnalyzeFilesFirst;
        internal int? MaxAdaptiveIterations;
        internal bool PrepareOnly;
        internal AcquisitionRecoveryResult? Result;
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    internal void Add(Entry entry)
    {
        foreach (var pair in _entries) if (pair.Value.Preview.ExpiresAt <= DateTimeOffset.UtcNow) _entries.TryRemove(pair.Key, out _);
        if (_entries.Count >= 50) throw new ArgumentException("RECOVERY_PREVIEW_LIMIT");
        _entries[entry.Preview.PreviewToken] = entry;
    }
    internal Entry Claim(string token, int[] selected, bool? useEmergencySpaceReserve = null,
        bool analyzeFilesFirst = false, int? maxAdaptiveIterations = null, bool prepareOnly = false)
    {
        if (!Guid.TryParseExact(token, "N", out _) || !_entries.TryGetValue(token, out var entry) || entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("RECOVERY_PREVIEW_EXPIRED");
        lock (entry)
        {
            if (prepareOnly && (entry.Preview.Scope != "localFiles" || analyzeFilesFirst ||
                                maxAdaptiveIterations != null || useEmergencySpaceReserve != null))
                throw new ArgumentException("RECOVERY_PREPARE_OPTIONS_INVALID");
            if (analyzeFilesFirst && entry.Preview.Scope != "localFiles" ||
                maxAdaptiveIterations != null && !analyzeFilesFirst || maxAdaptiveIterations is < 1 or > 5)
                throw new ArgumentException("RECOVERY_ANALYSIS_OPTIONS_INVALID");
            maxAdaptiveIterations = analyzeFilesFirst ? maxAdaptiveIterations ?? 5 : null;
            if (selected.Any(id => !entry.Versions.ContainsKey(id))) throw new ArgumentException("RECOVERY_SELECTION_INVALID");
            if (entry.Claimed && (entry.Result == null || !selected.SequenceEqual(entry.Selected!) ||
                useEmergencySpaceReserve != entry.UseEmergencySpaceReserve || analyzeFilesFirst != entry.AnalyzeFilesFirst ||
                maxAdaptiveIterations != entry.MaxAdaptiveIterations || prepareOnly != entry.PrepareOnly))
                throw new ArgumentException("RECOVERY_OUTCOME_UNKNOWN");
            entry.Claimed = true; entry.Selected = selected; entry.UseEmergencySpaceReserve = useEmergencySpaceReserve;
            entry.AnalyzeFilesFirst = analyzeFilesFirst; entry.MaxAdaptiveIterations = maxAdaptiveIterations;
            entry.PrepareOnly = prepareOnly;
            return entry;
        }
    }
}

public sealed class AcquisitionRecoveryService(BakabaseDbContext db, IAcquisitionRecoveryDiagnostics diagnostics,
    AcquisitionRecoveryPreviewStore previews, IWorkflowRunResumer resumer, AcquisitionPlanService? plans = null,
    IAcquisitionDownloadOnlyRecoveryService? downloadOnly = null, AcquisitionLocalFilesRecoveryService? localFiles = null,
    CloudOperationService? transfers = null)
{
    public async Task<AcquisitionRecoveryPreview> PreviewAsync(AcquisitionRecoveryPreviewInput input, CancellationToken ct = default,
        Func<string?, Task>? guardDirectory = null)
    {
        var ids = Ids(input.TaskIds);
        var scope = input.Scope ?? "workflow";
        if (scope is not ("workflow" or "downloads" or "localFiles" or "retryTransfer")) throw new ArgumentException("RECOVERY_SCOPE_INVALID");
        async Task<AcquisitionRecoveryDiagnosis> Diagnose(AcquisitionTaskDbModel task, WorkflowRunDbModel? run, bool verify, CancellationToken token)
        {
            if (scope == "retryTransfer") return await diagnostics.DiagnoseTransferRetryAsync(task, run, token);
            if (scope != "localFiles")
            {
                var current = await diagnostics.DiagnoseAsync(task, run, token, verifyFiles: verify,
                    includeStoppedDownloads: scope == "downloads", includeStoppedVerification: scope == "workflow",
                    deferCompletedDownloadVerification: true);
                if (guardDirectory != null)
                    foreach (var directory in current.InitialDownloadDirectories) await guardDirectory(directory);
                return ForScope(current, run, scope);
            }
            if (localFiles == null) throw new ArgumentException("RECOVERY_LOCAL_FILES_UNAVAILABLE");
            var diagnosis = await localFiles.PreviewAsync(task, run, token, guardDirectory);
            if (diagnosis.Row.CanContinue && diagnosis.Row.LocalFiles is { } files &&
                AcquisitionLocalFilesRecoveryService.CanPrepareWithoutFileOperations(task))
                return diagnosis with {Row = diagnosis.Row with {LocalFiles = files with {SupportsPrepareOnly = true}}};
            return diagnosis;
        }
        var rows = new List<AcquisitionRecoveryRow>(); var versions = new Dictionary<int, string>(); var signals = new Dictionary<int, string>();
        var verification = new List<(int Id, int Index, string Version, bool IsPausedOriginal)>();
        // Fetch private checkpoints in bounded batches, not one query per row or one unbounded
        // all-history response. Deep file verification has a separate per-row time budget.
        foreach (var batch in ids.Chunk(25))
        {
            var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => batch.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
            var runIds = tasks.Values.Where(t => t.WorkflowRunId != null).Select(t => t.WorkflowRunId!.Value).Distinct().ToArray();
            var runs = await db.WorkflowRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
            foreach (var id in batch)
            {
                if (!tasks.TryGetValue(id, out var task)) {rows.Add(new(id, null, null, false, null, "ACQUISITION_TASK_NOT_FOUND")); continue;}
                var run = task.WorkflowRunId is {} runId ? runs.GetValueOrDefault(runId) : null;
                var diagnosis = await Diagnose(task, run, false, ct);
                rows.Add(diagnosis.Row with {CanOverrideStorage = AcquisitionContinuationReserve.Supports(run)});
                if (diagnosis.Row.CanContinue) {versions[id] = Version(task, run!); signals[id] = diagnosis.SignalJson;}
                else if (diagnosis.Row.ReasonCode is "RECOVERY_VERIFY_FILES" or "RECOVERY_RECHECK_SOURCE")
                    verification.Add((id, rows.Count - 1, Version(task, run!),
                        diagnosis.PreparedDownloadReason == AcquisitionRecoveryDiagnostics.PausedOriginalReady));
            }
        }
        // Finish inexpensive diagnosis for every fixed task before spending the bounded budget
        // hashing retained files. Large files cannot starve unrelated scope-recovery checks.
        // When the user explicitly inspects one local file set, its hash may take longer on a large download or NAS.
        // The short per-row budget exists to keep a batch of independent tasks responsive.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        foreach (var pending in verification)
        {
            using var itemBudget = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            var inspectingPausedOriginal = ids.Length == 1 && scope == "workflow" && pending.IsPausedOriginal;
            if (!inspectingPausedOriginal)
                itemBudget.CancelAfter(TimeSpan.FromSeconds(rows[pending.Index].ReasonCode == "RECOVERY_RECHECK_SOURCE" ? 12 : 4));
            var inspectingOneFileSet = ids.Length == 1 && scope == "workflow" && IsLocalFileStep(rows[pending.Index].CurrentStepKind);
            var checkToken = inspectingOneFileSet ? ct : itemBudget.Token;
            try
            {
                checkToken.ThrowIfCancellationRequested();
                var (task, run) = await Current(pending.Id, checkToken);
                if (task == null || Version(task, run) != pending.Version)
                {rows[pending.Index] = rows[pending.Index] with {ReasonCode = "ACQUISITION_TASK_CHANGED"}; continue;}
                var result = await Diagnose(task, run, true, checkToken);
                rows[pending.Index] = result.Row with {CanOverrideStorage = AcquisitionContinuationReserve.Supports(run)};
                if (result.Row.CanContinue) {versions[pending.Id] = pending.Version; signals[pending.Id] = result.SignalJson;}
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                rows[pending.Index] = rows[pending.Index] with
                {
                    ReasonCode = pending.IsPausedOriginal ? AcquisitionRecoveryDiagnostics.PausedOriginalCheckTimedOut : "RECOVERY_CHECK_INCOMPLETE",
                    ManualAction = pending.IsPausedOriginal ? "inspectSource" : "inspectFiles"
                };
            }
        }
        var preview = new AcquisitionRecoveryPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10),
            rows.Count, versions.Count, rows.Count - versions.Count, rows, scope);
        ct.ThrowIfCancellationRequested();
        previews.Add(new(preview, versions, signals));
        return preview;
    }

    public async Task<AcquisitionRecoveryResult> ApplyAsync(AcquisitionRecoveryApplyInput input, CancellationToken ct = default,
        Func<string?, Task>? guardDirectory = null) => await ApplyCoreAsync(input, ct, guardDirectory);

    // The worker has already performed the full action diagnosis and checked its frozen
    // signal. Pin that exact read to the newly persisted starting state. A paused original
    // only repeats the local proof under the run gate; the downloader rechecks remote ownership.
    internal Task<AcquisitionRecoveryResult> ApplyBatchAdmissionAsync(string scope, AcquisitionRecoveryRow row,
        string version, string signal, string? placementStamp, bool transferRetryConfirmed, CancellationToken ct, string? startingBatchId = null)
    {
        var token = Guid.NewGuid().ToString("N");
        var preview = new AcquisitionRecoveryPreview(token, DateTimeOffset.UtcNow.AddMinutes(10), 1, 1, 0, [row], scope);
        previews.Add(new(preview, new Dictionary<int, string> {{row.TaskId, version}},
            new Dictionary<int, string> {{row.TaskId, signal}}));
        return ApplyCoreAsync(new(token, [row.TaskId]), ct, null, processingPlacementStamp: placementStamp, preserveReserve: true,
            transferRetryConfirmed: transferRetryConfirmed, expectedSignal: signal, startingBatchId: startingBatchId);
    }

    internal Task<AcquisitionRecoveryResult> ApplyBatchAsync(AcquisitionRecoveryApplyInput input,
        string? processingPlacementStamp, CancellationToken ct, bool transferRetryConfirmed = false, string? expectedSignal = null) =>
        ApplyCoreAsync(input, ct, null, processingPlacementStamp: processingPlacementStamp, preserveReserve: true,
            transferRetryConfirmed: transferRetryConfirmed, expectedSignal: expectedSignal);

    // The durable continuation request already binds its task/run/revision. Verify once at
    // dispatch, under the task gate, then keep the same exact-state guard inside the run gate.
    // This is intentionally not a public "skip verification" flag.
    internal async Task<AcquisitionRecoveryResult> ContinueDeferredAsync(int id, string scope, string expectedVersion,
        CancellationToken ct, Func<string?, Task> guardDirectory, string? expectedStorageJson = null, bool? useEmergencySpaceReserve = null)
    {
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var (task, run) = await Current(id, ct);
            if (task == null || AcquisitionContinuationEntry.ContentVersion(task, run) != expectedVersion)
                return new([], [new(id, "ACQUISITION_TASK_CHANGED")]);
            if (scope == "localFiles")
            {
                if (localFiles == null) return new([], [new(id, "RECOVERY_LOCAL_FILES_UNAVAILABLE")]);
                var outcome = await localFiles.ApplyWhileLockedAsync(id, Version(task, run), null, guardDirectory, ct, expectedStorageJson,
                    useEmergencySpaceReserve: useEmergencySpaceReserve);
                return new(outcome.Status == "started" ? [id] : [], [], LocalFiles: [outcome]);
            }
            var diagnosis = ForScope(await diagnostics.DiagnoseAsync(task, run, ct, verifyContent: true,
                includeStoppedDownloads: scope == "downloads", includeStoppedVerification: scope == "workflow",
                deferCompletedDownloadVerification: true), run, scope);
            if (!diagnosis.Row.CanContinue) return new([], [new(id, diagnosis.Row.ReasonCode)]);
            return await DispatchVerifiedWhileLockedAsync(task, run, scope, guardDirectory, diagnosis, ct, useEmergencySpaceReserve);
        }
        finally {gate.Release();}
    }

    internal static bool IsLocalFileStep(string? kind) =>
        kind is AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts";

    // A confirmed temporary reserve grant is already scoped to this task, saved run and
    // version. Share the queue's local validation path, so one large artifact is hashed
    // once under its task gate and the version is rechecked before the host resumes.
    internal async Task<AcquisitionRecoveryResult> ResumeGrantedFileStepAsync(int id, int runId, string version, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id);
        var held = false;
        var handingOff = false;
        try
        {
            await gate.WaitAsync(ct); held = true;
            var (task, run) = await Current(id, ct);
            if (task == null || run?.Id != runId || run.Status != WorkflowRunStatus.Waiting || Version(task, run) != version)
                return new([], [new(id, "ACQUISITION_TASK_CHANGED")]);
            var diagnosis = await diagnostics.DiagnoseAsync(task, run, ct, verifyContent: true);
            ct.ThrowIfCancellationRequested();
            if (!diagnosis.Row.CanContinue || diagnosis.Row.WorkflowRunId != runId || diagnosis.Row.Action != "resume")
                return new([], [new(id, diagnosis.Row.ReasonCode)]);
            handingOff = true;
            return await DispatchVerifiedWhileLockedAsync(task, run, "workflow", null, diagnosis, ct, preserveReserve: true);
        }
        catch (OperationCanceledException) when (!handingOff && ct.IsCancellationRequested)
        {
            // No workflow signal has been handed off, so the grant can be safely revoked.
            return new([], [new(id, "RECOVERY_CHECK_INCOMPLETE")]);
        }
        finally {if (held) gate.Release();}
    }

    private async Task<AcquisitionRecoveryResult> DispatchVerifiedWhileLockedAsync(AcquisitionTaskDbModel task,
        WorkflowRunDbModel? run, string scope, Func<string?, Task>? guardDirectory, AcquisitionRecoveryDiagnosis diagnosis,
        CancellationToken ct, bool? useEmergencySpaceReserve = null, bool preserveReserve = false)
    {
        var preview = new AcquisitionRecoveryPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10),
            1, 1, 0, [diagnosis.Row], scope);
        previews.Add(new(preview, new Dictionary<int, string> {{task.Id, Version(task, run)}},
            new Dictionary<int, string> {{task.Id, diagnosis.SignalJson}}));
        return await ApplyCoreAsync(new(preview.PreviewToken, [task.Id], useEmergencySpaceReserve), ct, guardDirectory, task.Id, diagnosis, preserveReserve: preserveReserve);
    }

    private async Task<AcquisitionRecoveryResult> ApplyCoreAsync(AcquisitionRecoveryApplyInput input, CancellationToken ct,
        Func<string?, Task>? guardDirectory, int? heldTask = null, AcquisitionRecoveryDiagnosis? verified = null,
        string? processingPlacementStamp = null, bool preserveReserve = false, bool transferRetryConfirmed = false, string? expectedSignal = null, string? startingBatchId = null)
    {
        var ids = Ids(input.TaskIds).Order().ToArray();
        var entry = previews.Claim(input.PreviewToken, ids, input.UseEmergencySpaceReserve, input.AnalyzeFilesFirst == true,
            input.MaxAdaptiveIterations, input.PrepareOnly == true);
        if (entry.Result is {} saved) return saved;
        var continued = new List<int>(); var skipped = new List<AcquisitionRecoverySkip>();
        var downloadOutcomes = new List<AcquisitionDownloadRecoveryOutcome>();
        var localOutcomes = new List<AcquisitionLocalFilesRecoveryOutcome>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var gate = AcquisitionTaskGate.For(id);
            if (heldTask != id && !await gate.WaitAsync(0, ct)) {skipped.Add(new(id, "ACQUISITION_TASK_ACTIVE")); continue;}
            try
            {
                var expected = entry.Preview.Rows.Single(r => r.TaskId == id);
                if (expectedSignal != null && entry.Signals[id] != expectedSignal)
                    throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                if (expected.ReasonCode == AcquisitionRecoveryDiagnostics.TransferRetryConfirmationRequired && !transferRetryConfirmed)
                    throw new RecoveryBlockedException(AcquisitionRecoveryDiagnostics.TransferRetryConfirmationRequired);
                await EnsureCurrentVersion(id, entry.Versions[id], ct);
                if (entry.Preview.Scope == "localFiles")
                {
                    if (localFiles == null || expected.Action != "localFiles")
                        throw new RecoveryBlockedException("RECOVERY_LOCAL_FILES_UNAVAILABLE");
                    var outcome = await localFiles.ApplyWhileLockedAsync(id, entry.Versions[id], entry.Signals[id], guardDirectory, ct,
                        analyzeFilesFirst: entry.AnalyzeFilesFirst, maxAdaptiveIterations: entry.MaxAdaptiveIterations,
                        useEmergencySpaceReserve: input.UseEmergencySpaceReserve, prepareOnly: entry.PrepareOnly);
                    localOutcomes.Add(outcome);
                    if (outcome.Status == "started") continued.Add(id);
                    continue;
                }
                if (entry.Preview.Scope == "downloads")
                {
                    if (downloadOnly == null || expected.Action != "downloads")
                        throw new RecoveryBlockedException("RECOVERY_DOWNLOAD_SCOPE_UNAVAILABLE");
                    var (task, run) = await Current(id, ct);
                    if (task == null || run == null) throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                    using var validationBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    validationBudget.CancelAfter(TimeSpan.FromSeconds(15));
                    var current = verified ?? ForScope(await diagnostics.DiagnoseAsync(task, run, validationBudget.Token, includeStoppedDownloads: true), run, "downloads");
                    if (!current.Row.CanContinue || current.SignalJson != entry.Signals[id])
                        throw new RecoveryBlockedException(current.Row.CanContinue ? "ACQUISITION_TASK_CHANGED" : current.Row.ReasonCode);
                    // Persist the requested boundary before starting a child. A lost response or
                    // process restart must not turn downloads-only into permission to process files.
                    var runGate = WorkflowRunMutationGate.For(run.Id); await runGate.WaitAsync(ct);
                    string version;
                    try
                    {
                        await EnsureCurrentVersion(id, entry.Versions[id], ct);
                        if (guardDirectory != null)
                            foreach (var directory in current.InitialDownloadDirectories) await guardDirectory(directory);
                        task = await AcquisitionContinuationReserve.SaveWhileLockedAsync(db, task, run, input.UseEmergencySpaceReserve,
                            "recovery:" + input.PreviewToken, ct, preserveReserve);
                        task = await new AcquisitionDownloadContinuationService(db).SaveWhileLockedAsync(task, run.Id,
                            AcquisitionDownloadContinuationService.DownloadsOnly, ct);
                        version = Version(task, run);
                    }
                    finally {runGate.Release();}
                    var outcome = await downloadOnly.StartAsync(id, run, entry.Signals[id],
                        token => EnsureCurrentVersion(id, version, token), ct);
                    downloadOutcomes.Add(outcome);
                    if (outcome.ReasonCode == null && outcome.IndeterminateDownloadTaskIds.Count == 0 &&
                        outcome.StartedDownloadTaskIds.Count + (outcome.VerifiedDownloadTaskIds?.Count ?? 0) ==
                        AcquisitionDownloadOnlyRecoveryService.Targets(run, entry.Signals[id],
                            verificationOnly: expected.ReasonCode == "RECOVERY_EXISTING_FILES_VERIFY_REQUIRED")!.Length)
                        continued.Add(id);
                    else skipped.Add(new(id, outcome.StartedDownloadTaskIds.Count > 0 ? "RECOVERY_DOWNLOAD_PARTIALLY_STARTED" :
                        outcome.ReasonCode ?? "RECOVERY_DOWNLOAD_OUTCOME_UNKNOWN"));
                    continue;
                }
                if (expected.Action == "start" && plans != null)
                {
                    var (task, run) = await Current(id, ct);
                    if (task == null || run != null) throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                    var current = verified ?? await diagnostics.DiagnoseAsync(task, null, ct);
                    if (!current.Row.CanContinue || current.Row.Action != "start")
                        throw new RecoveryBlockedException(current.Row.ReasonCode);
                    if (current.SignalJson != entry.Signals[id]) throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                    var started = await plans.RunWhileLockedAsync(id, task.PlanRevision, entry.Signals[id], ct, startingBatchId: startingBatchId);
                    if (started.Task.WorkflowRunId != null) continued.Add(id);
                    else skipped.Add(new(id, "RECOVERY_PREPARED_PLAN_BLOCKED"));
                    continue;
                }
                // The workflow host takes only its run gate, then invokes our check. This preserves
                // owner task -> run ordering used by deletion, without recursively taking either gate.
                await resumer.ContinueConditionallyAsync(expected.WorkflowRunId!.Value, expected.Action == "resume",
                    entry.Signals[id], async token =>
                    {
                        var (task, run) = await Current(id, token);
                        if (task == null || run == null || Version(task, run) != entry.Versions[id])
                            throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                        var pausedOriginal = expected.ReasonCode == AcquisitionRecoveryDiagnostics.PausedOriginalReady;
                        using var validationBudget = CancellationTokenSource.CreateLinkedTokenSource(token);
                        if (!pausedOriginal && (entry.Preview.TotalCount != 1 || !IsLocalFileStep(expected.CurrentStepKind)))
                            validationBudget.CancelAfter(preserveReserve && diagnostics.IsTransferStep(expected.CurrentStepKind)
                                ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(15));
                        var approvedSignal = entry.Signals[id];
                        AcquisitionRecoveryDiagnosis current;
                        try { current = entry.Preview.Scope == "retryTransfer"
                            ? await diagnostics.DiagnoseTransferRetryAsync(task, run, validationBudget.Token)
                            : verified ?? await diagnostics.DiagnoseAsync(task, run, validationBudget.Token,
                            verifyFiles: !pausedOriginal, verifyContent: !pausedOriginal,
                            includeStoppedVerification: true, deferCompletedDownloadVerification: true); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        { throw new RecoveryBlockedException("RECOVERY_CHECK_INCOMPLETE"); }
                        if (pausedOriginal)
                        {
                            if (current.PreparedDownloadReason != AcquisitionRecoveryDiagnostics.PausedOriginalReady)
                                throw new RecoveryBlockedException(current.Row.CanContinue ?
                                    "ACQUISITION_TASK_CHANGED" : current.Row.ReasonCode);
                            if (!CloudSelectionSignal.IsPausedPreparedRetrySubset(approvedSignal, current.SignalJson))
                                throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                            if (guardDirectory != null)
                                foreach (var directory in current.InitialDownloadDirectories) await guardDirectory(directory);
                        }
                        else
                        {
                            if (!current.Row.CanContinue) throw new RecoveryBlockedException(current.Row.ReasonCode);
                            if (current.SignalJson != approvedSignal) throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                        }
                        if (processingPlacementStamp != null && current.ProcessingPlacementStamp != processingPlacementStamp)
                            throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                        if (current.TransferRetryTargets.Count > 0)
                        {
                            if (transfers == null || expected.ReasonCode != current.Row.ReasonCode ||
                                current.Row.ReasonCode != AcquisitionRecoveryDiagnostics.TransferRecoveryReady)
                                throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                            try
                            {
                                foreach (var retry in current.TransferRetryTargets)
                                {
                                    var authorized = await transfers.AuthorizeTransferRecoveryAsync(run.Id, retry.Target,
                                        retry.AccountId, retry.RemoteDirectory, validationBudget.Token, retry.NextIsolatedOperationId);
                                    if (authorized.Disposition is not (CloudTransferRecoveryDisposition.Authorized or
                                        CloudTransferRecoveryDisposition.Reconcile or CloudTransferRecoveryDisposition.Retry))
                                        throw new RecoveryBlockedException("RECOVERY_TRANSFER_REVIEW_REQUIRED");
                                }
                            }
                            catch (OperationCanceledException) when (!token.IsCancellationRequested)
                            {throw new RecoveryBlockedException("RECOVERY_CHECK_INCOMPLETE");}
                        }
                        if (entry.Preview.Scope == "retryTransfer")
                        {
                            if (!transferRetryConfirmed || transfers == null || current.TransferReplays.Count == 0)
                                throw new RecoveryBlockedException(AcquisitionRecoveryDiagnostics.TransferRetryConfirmationRequired);
                            await using var preparation = await db.Database.BeginTransactionAsync(token);
                            foreach (var replay in current.TransferReplays)
                                if (!await transfers.PrepareTransferReplayAsync(run.Id, replay, validationBudget.Token))
                                    throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
                            await preparation.CommitAsync(token);
                        }
                        task = await AcquisitionContinuationReserve.SaveWhileLockedAsync(db, task, run, input.UseEmergencySpaceReserve,
                            "recovery:" + input.PreviewToken, token, preserveReserve, pausedOriginal);
                        // Enqueue may be consumed immediately. Commit the confirmed follow-up
                        // mode under the same task/run gates, before handing over the signal.
                        await new AcquisitionDownloadContinuationService(db).SaveWhileLockedAsync(task, run.Id,
                            pausedOriginal ? AcquisitionDownloadContinuationService.Paused : AcquisitionDownloadContinuationService.Automatic,
                            token, approvePausedSignal: pausedOriginal ? approvedSignal : null);
                    }, ct);
                continued.Add(id);
            }
            catch (RecoveryBlockedException error) {skipped.Add(new(id, error.Message));}
            catch (InvalidOperationException error) when (error.Message == "ACQUISITION_TASK_CHANGED")
            {skipped.Add(new(id, error.Message));}
            catch (Exception error) when (entry.Preview.Scope == "localFiles" && error is ArgumentException or InvalidOperationException &&
                (error.Message.StartsWith("RECOVERY_", StringComparison.Ordinal) || error.Message.StartsWith("PLAN_", StringComparison.Ordinal)))
            {skipped.Add(new(id, error.Message));}
            // Any other failure may follow a committed pending state or enqueue. Keep this token
            // indeterminate and reject retries; the caller must obtain a fresh preview.
            finally {if (heldTask != id) gate.Release();}
        }
        var result = new AcquisitionRecoveryResult(continued, skipped, entry.Preview.Scope == "downloads" ? downloadOutcomes : null,
            entry.Preview.Scope == "localFiles" ? localOutcomes : null);
        lock (entry) entry.Result = result;
        return result;
    }
    private static AcquisitionRecoveryDiagnosis ForScope(AcquisitionRecoveryDiagnosis diagnosis, WorkflowRunDbModel? run, string scope)
    {
        if (scope != "downloads" || !diagnosis.Row.CanContinue) return diagnosis;
        return diagnosis.Row.ReasonCode == AcquisitionRecoveryDiagnostics.PausedOriginalReady ||
            AcquisitionDownloadOnlyRecoveryService.Targets(run, diagnosis.SignalJson,
            verificationOnly: diagnosis.Row.ReasonCode == "RECOVERY_EXISTING_FILES_VERIFY_REQUIRED") == null
            ? diagnosis with {Row = diagnosis.Row with {CanContinue = false, Action = null, ReasonCode = "RECOVERY_DOWNLOAD_SCOPE_UNAVAILABLE"}}
            : diagnosis with {Row = diagnosis.Row with {Action = "downloads"}};
    }
    private async Task EnsureCurrentVersion(int id, string version, CancellationToken ct)
    {
        var (task, run) = await Current(id, ct);
        if (task == null || Version(task, run) != version) throw new RecoveryBlockedException("ACQUISITION_TASK_CHANGED");
    }
    private async Task<(AcquisitionTaskDbModel?, WorkflowRunDbModel?)> Current(int id, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        var run = task?.WorkflowRunId is {} runId ? await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        return (task, run);
    }
    private static int[] Ids(IReadOnlyList<int>? ids)
    {
        if (ids == null || ids.Count is 0 or > 1000 || ids.Any(id => id <= 0)) throw new ArgumentException("RECOVERY_SELECTION_INVALID");
        return ids.Distinct().ToArray();
    }
    internal static string Version(AcquisitionTaskDbModel task, WorkflowRunDbModel? run) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(task) + "\n" + JsonSerializer.Serialize(run))));
    private sealed class RecoveryBlockedException(string code) : InvalidOperationException(code);
}

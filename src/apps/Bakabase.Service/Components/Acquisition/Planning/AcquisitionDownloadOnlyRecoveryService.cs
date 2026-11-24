using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public interface IAcquisitionOriginalDownloadStarter
{
    Task<bool> StartAsync(int id, Func<CancellationToken, Task> validate,
        Func<CancellationToken, Task> validateUnderOwnerLease, CancellationToken ct, string? preparedRetryOperationId = null);
}

public sealed class AcquisitionOriginalDownloadStarter(DownloadTaskService downloads) : IAcquisitionOriginalDownloadStarter
{
    public async Task<bool> StartAsync(int id, Func<CancellationToken, Task> validate,
        Func<CancellationToken, Task> validateUnderOwnerLease, CancellationToken ct, string? preparedRetryOperationId = null) =>
        (await downloads.StartConditionally(id, validate, ct, validateUnderOwnerLease, preparedRetryOperationId)).Code == 0;
}

public interface IAcquisitionDownloadOnlyRecoveryService
{
    Task<AcquisitionDownloadRecoveryOutcome> StartAsync(int taskId, WorkflowRunDbModel run, string signalJson,
        Func<CancellationToken, Task> validateOwner, CancellationToken ct);
}

/// <summary>Re-enables only original failed site tasks. It never resumes a workflow or edits its
/// checkpoints, enqueues a new generation, purchases content, or invokes a transfer.</summary>
public sealed class AcquisitionDownloadOnlyRecoveryService(BakabaseDbContext db, CloudDownloadRecoveryService recovery,
    IAcquisitionOriginalDownloadStarter starter, DownloaderManager manager, IStorageWriteGuard writes,
    DownloadTaskService nativeTasks) : IAcquisitionDownloadOnlyRecoveryService
{
    internal static CloudDownloadTarget[]? Targets(WorkflowRunDbModel? run, string signalJson, bool verificationOnly = false)
    {
        if (run?.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex ?? -1)?.Kind != "download.awaitCompletion") return null;
        try
        {
            var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(signalJson, WorkflowJson.Options);
            var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
            var matchesMode = verificationOnly
                ? signal is {VerifyExistingFilesOnly: true, RevalidateExistingDownloads: not true}
                : signal is {RevalidateExistingDownloads: true, VerifyExistingFilesOnly: not true};
            if (signal?.ObserveExistingOnly != true || !matchesMode || !signal.HasValidPreparedRetrySelection() ||
                signal.ResumeTargetIds is not {Count: > 0 and <= 100} ids || ids.Distinct().Count() != ids.Count || workset == null ||
                signal.SourceIds.Count > 0 || signal.SourceMethods.Count > 0 || signal.SourceAccounts.Count > 0 ||
                signal.SourceCredentialCorrections.Count > 0 || signal.AccountId != null || signal.OutputDirectory != null) return null;
            var targets = workset.Targets.Where(t => ids.Contains(t.Id)).ToArray();
            return targets.Length == ids.Count && targets.All(t => !t.Excluded && t.SelectedSource != null &&
                t.DownloadTaskId != null && t.TransportTaskId == null && t.AcquisitionNonce == null) &&
                targets.Select(t => t.DownloadTaskId).Distinct().Count() == targets.Length ? targets : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return null; }
    }

    public async Task<AcquisitionDownloadRecoveryOutcome> StartAsync(int taskId, WorkflowRunDbModel run, string signalJson,
        Func<CancellationToken, Task> validateOwner, CancellationToken ct)
    {
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson)!;
        if (Targets(run, signalJson, verificationOnly: true) is { } verificationTargets)
            return await VerifyOnlyAsync(taskId, run.Id, workset.OutputDirectory, verificationTargets, validateOwner, ct);
        var targets = Targets(run, signalJson) ?? throw new InvalidOperationException("RECOVERY_DOWNLOAD_SCOPE_UNAVAILABLE");
        var confirmedPrepared = JsonSerializer.Deserialize<CloudSelectionSignal>(signalJson, WorkflowJson.Options)!.PreparedDownloadRetries;
        var started = new List<int>(); var unknown = new List<int>(); string? reason = null;
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            var grouped = workset.Targets.Count(t => t.DownloadTaskId == target.DownloadTaskId) > 1;
            try { await validateOwner(ct); }
            catch (InvalidOperationException error) {reason = SafeReason(error); break;}
            using var remoteBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            remoteBudget.CancelAfter(TimeSpan.FromSeconds(15));
            CloudDownloadRecoveryCheck check;
            try { check = await recovery.CheckAsync(run.Id, target, workset.OutputDirectory, true, remoteBudget.Token,
                includeStopped: true, requireEquivalentGroup: grouped); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { reason = "RECOVERY_CHECK_INCOMPLETE"; break; }
            if (!check.CanRetry || check.VerifyExistingOnly) {reason = check.ReasonCode; break;}
            if (confirmedPrepared?.GetValueOrDefault(target.Id) != check.PreparedRetry)
            {reason = "RECOVERY_DOWNLOAD_IDENTITY_CHANGED"; break;}
            try
            {
                var id = target.DownloadTaskId!.Value;
                // The owner gate is acquired here only for the initial local check. The native
                // starter then acquires it itself and calls ValidateOwnerLease twice under that lease.
                async Task Validate(CancellationToken token)
                {
                    var gate = WorkflowRunMutationGate.For(run.Id); await gate.WaitAsync(token);
                    try
                    {
                        await validateOwner(token);
                        var local = await recovery.CheckAsync(run.Id, target, workset.OutputDirectory, false, token,
                            includeStopped: true, requireEquivalentGroup: grouped);
                        if (!local.Candidate || local.VerifyExistingOnly) throw new InvalidOperationException(local.ReasonCode);
                        if (local.PreparedRetry != check.PreparedRetry)
                            throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
                        await CheckCapacity(id, target, workset, token);
                    }
                    finally {gate.Release();}
                }
                async Task ValidateOwnerLease(CancellationToken token)
                {
                    // Do not recursively take the run gate. No callback is retained by a worker.
                    await validateOwner(token);
                    if (check.PreparedRetry is { } prepared)
                        await recovery.ValidatePreparedAdmissionAsync(run.Id, target, workset.OutputDirectory, prepared, token);
                    if (grouped && (await recovery.ReadEquivalentGroupAsync(run.Id, target, workset.OutputDirectory, token))?.Owner.Id != target.Id)
                        throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
                    await CheckCapacity(id, target, workset, token);
                }
                bool accepted;
                try { accepted = await starter.StartAsync(id, Validate, ValidateOwnerLease, ct, check.PreparedRetry?.OperationId); }
                catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    var state = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, CancellationToken.None);
                    if (state?.Status is DownloadTaskDbModelStatus.InProgress or DownloadTaskDbModelStatus.Complete ||
                        manager[id]?.IsOccupyingDownloadTaskSource() == true) unknown.Add(id);
                    reason = unknown.Count > 0 ? "RECOVERY_DOWNLOAD_OUTCOME_UNKNOWN" : SafeReason(error);
                    break;
                }
                var current = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
                // Persistent InProgress alone is not proof of a started engine. This strict scope
                // never silently leaves an automatic queue entry behind a source conflict.
                if (accepted && (current?.Status == DownloadTaskDbModelStatus.Complete ||
                    manager[id]?.IsOccupyingDownloadTaskSource() == true)) started.Add(id);
                else
                {
                    if (current?.Status is DownloadTaskDbModelStatus.InProgress or DownloadTaskDbModelStatus.Complete ||
                        manager[id]?.IsOccupyingDownloadTaskSource() == true) unknown.Add(id);
                    reason = unknown.Count > 0 ? "RECOVERY_DOWNLOAD_OUTCOME_UNKNOWN" : "RECOVERY_DOWNLOAD_START_REJECTED";
                    break;
                }
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
            { reason = SafeReason(error); break; }
        }
        return new(taskId, started, unknown, reason);
    }

    private async Task<AcquisitionDownloadRecoveryOutcome> VerifyOnlyAsync(int taskId, int runId, string outputRoot,
        IReadOnlyList<CloudDownloadTarget> targets, Func<CancellationToken, Task> validateOwner, CancellationToken ct)
    {
        // The acquisition lock already fixes the user's selection. The native download action lock
        // protects the payload during verification. Fence the short receipt transaction against
        // independent workflow edits, without locking the workflow during a long network read.
        async Task GuardRegistration(Func<Task> register)
        {
            var gate = WorkflowRunMutationGate.For(runId);
            await gate.WaitAsync(ct);
            try { await validateOwner(ct); await register(); }
            finally { gate.Release(); }
        }
        var verified = new List<int>();
        foreach (var target in targets)
        {
            try
            {
                await validateOwner(ct);
                await recovery.VerifyExistingAsync(runId, target, outputRoot, nativeTasks, ct, GuardRegistration);
                verified.Add(target.DownloadTaskId!.Value);
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                var reason = AcquisitionRuntimeProblems.Code(error.Message) ??
                    SafeReason(error, "RECOVERY_EXISTING_FILES_UNVERIFIED");
                return new(taskId, [], [], reason, verified);
            }
        }
        return new(taskId, [], [], VerifiedDownloadTaskIds: verified);
    }

    private async Task CheckCapacity(int id, CloudDownloadTarget target, DownloadWorkset workset, CancellationToken ct)
    {
        var download = await db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == id, ct);
        var operationId = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId == id)
            .Select(o => o.Id).SingleAsync(ct);
        await writes.CheckAsync(download.DownloadPath, new("cloudOperation", operationId), ct);
    }

    private static string SafeReason(Exception error, string fallback = "RECOVERY_DOWNLOAD_START_REJECTED")
    {
        if (error is LocalStorageException local) return local.Code;
        if (LocalStorageFailures.PersistedCode(error.Message) is { } storageCode) return storageCode;
        if (error.Message == "DOWNLOAD_START_SOURCE_BUSY") return "RECOVERY_DOWNLOAD_SOURCE_BUSY";
        var code = error.Message;
        return code.Length <= 100 && code.All(c => c is >= 'A' and <= 'Z' or '_') &&
               (code.StartsWith("RECOVERY_", StringComparison.Ordinal) || code.StartsWith("ACQUISITION_", StringComparison.Ordinal))
            ? code : fallback;
    }
}

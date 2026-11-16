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
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public interface IAcquisitionOriginalDownloadStarter
{
    Task<bool> StartAsync(int id, Func<CancellationToken, Task> validate,
        Func<CancellationToken, Task> validateUnderOwnerLease, CancellationToken ct);
}

public sealed class AcquisitionOriginalDownloadStarter(DownloadTaskService downloads) : IAcquisitionOriginalDownloadStarter
{
    public async Task<bool> StartAsync(int id, Func<CancellationToken, Task> validate,
        Func<CancellationToken, Task> validateUnderOwnerLease, CancellationToken ct) =>
        (await downloads.StartConditionally(id, validate, ct, validateUnderOwnerLease)).Code == 0;
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
    IBOptions<AcquisitionOptions> options) : IAcquisitionDownloadOnlyRecoveryService
{
    private static readonly SemaphoreSlim Admission = new(1, 1);

    internal static CloudDownloadTarget[]? Targets(WorkflowRunDbModel? run, string signalJson)
    {
        if (run?.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex ?? -1)?.Kind != "download.awaitCompletion") return null;
        try
        {
            var signal = JsonSerializer.Deserialize<CloudSelectionSignal>(signalJson, WorkflowJson.Options);
            var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
            if (signal?.ObserveExistingOnly != true || signal.RevalidateExistingDownloads != true || signal.VerifyExistingFilesOnly == true ||
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
        var targets = Targets(run, signalJson) ?? throw new InvalidOperationException("RECOVERY_DOWNLOAD_SCOPE_UNAVAILABLE");
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson)!;
        var started = new List<int>(); var unknown = new List<int>(); string? reason = null;
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            try { await validateOwner(ct); }
            catch (InvalidOperationException error) {reason = SafeReason(error); break;}
            using var remoteBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            remoteBudget.CancelAfter(TimeSpan.FromSeconds(15));
            CloudDownloadRecoveryCheck check;
            try { check = await recovery.CheckAsync(run.Id, target, workset.OutputDirectory, true, remoteBudget.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { reason = "RECOVERY_CHECK_INCOMPLETE"; break; }
            if (!check.CanRetry || check.VerifyExistingOnly) {reason = check.ReasonCode; break;}
            await Admission.WaitAsync(ct);
            try
            {
                var id = target.DownloadTaskId!.Value;
                await EnsureAdmission(id, ct);
                // The owner gate is acquired here only for the initial local check. The native
                // starter then acquires it itself and calls ValidateOwnerLease twice under that lease.
                async Task Validate(CancellationToken token)
                {
                    var gate = WorkflowRunMutationGate.For(run.Id); await gate.WaitAsync(token);
                    try
                    {
                        await validateOwner(token);
                        var local = await recovery.CheckAsync(run.Id, target, workset.OutputDirectory, false, token);
                        if (!local.Candidate || local.VerifyExistingOnly) throw new InvalidOperationException(local.ReasonCode);
                        await EnsureAdmission(id, token); await CheckCapacity(id, target, workset, token);
                    }
                    finally {gate.Release();}
                }
                async Task ValidateOwnerLease(CancellationToken token)
                {
                    // Do not recursively take the run gate. No callback is retained by a worker.
                    await validateOwner(token); await EnsureAdmission(id, token);
                    await CheckCapacity(id, target, workset, token);
                }
                bool accepted;
                try { accepted = await starter.StartAsync(id, Validate, ValidateOwnerLease, ct); }
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
            finally {Admission.Release();}
        }
        return new(taskId, started, unknown, reason);
    }

    private async Task CheckCapacity(int id, CloudDownloadTarget target, DownloadWorkset workset, CancellationToken ct)
    {
        var download = await db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == id, ct);
        var operationId = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId == id)
            .Select(o => o.Id).SingleAsync(ct);
        await writes.CheckAsync(download.DownloadPath, new("cloudOperation", operationId), ct);
    }

    private async Task EnsureAdmission(int currentId, CancellationToken ct)
    {
        var taskRuns = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId != null).Select(t => t.WorkflowRunId!.Value).ToListAsync(ct);
        var sourceRuns = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" && s.EntityKind == "task")
            .Select(s => s.WorkflowRunId).ToListAsync(ct);
        var runs = taskRuns.Concat(sourceRuns).Distinct().ToArray();
        var cloud = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId != null && runs.Contains(o.WorkflowRunId))
            .Select(o => o.DownloadTaskId!.Value).ToListAsync(ct);
        var managed = await db.ManagedDownloadTasks.AsNoTracking().Where(o => o.SiteTaskId != null && o.WorkflowRunId != null && runs.Contains(o.WorkflowRunId.Value))
            .Select(o => o.SiteTaskId!.Value).ToListAsync(ct);
        var owned = await db.DownloadResultOwners.AsNoTracking().Where(o => o.AcquisitionTaskId > 0).Select(o => o.DownloadTaskId).ToListAsync(ct);
        var ids = cloud.Concat(managed).Concat(owned).Distinct().Where(id => id != currentId).ToArray();
        var states = await db.DownloadTasks.AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new {d.Id, d.Status}).ToListAsync(ct);
        var active = states.Count(d => d.Status == DownloadTaskDbModelStatus.InProgress || manager[d.Id]?.IsOccupyingDownloadTaskSource() == true);
        if (active >= Math.Clamp(options.Value.Concurrency, AcquisitionOptions.MinimumConcurrency, AcquisitionOptions.MaximumConcurrency))
            throw new InvalidOperationException("RECOVERY_DOWNLOAD_CONCURRENCY_LIMIT");
    }

    private static string SafeReason(Exception error)
    {
        if (error is LocalStorageException local) return local.Code;
        if (LocalStorageFailures.PersistedCode(error.Message) is { } storageCode) return storageCode;
        if (error.Message == "DOWNLOAD_START_SOURCE_BUSY") return "RECOVERY_DOWNLOAD_SOURCE_BUSY";
        var code = error.Message;
        return code.Length <= 100 && code.All(c => c is >= 'A' and <= 'Z' or '_') &&
               (code.StartsWith("RECOVERY_", StringComparison.Ordinal) || code.StartsWith("ACQUISITION_", StringComparison.Ordinal))
            ? code : "RECOVERY_DOWNLOAD_START_REJECTED";
    }
}

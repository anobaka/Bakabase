using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Observes completed original children; never starts a downloader or chooses a source.</summary>
public sealed class AcquisitionDownloadCompletionCoordinator(BakabaseDbContext db,
    IAcquisitionRecoveryDiagnostics diagnostics, IWorkflowRunResumer resumer,
    AcquisitionDownloadContinuationService intentions, BTaskManager tasks)
{
    internal const string Signal = "{\"observeExistingOnly\":true}";

    public async Task<int> ReconcileBatchAsync(int afterTaskId, CancellationToken ct)
    {
        if (tasks.IsShuttingDown) return afterTaskId;
        var ids = await (from task in db.AcquisitionTasks.AsNoTracking()
            join run in db.WorkflowRuns.AsNoTracking() on task.WorkflowRunId equals run.Id
            where task.Id > afterTaskId && task.DeletedAt == null && task.Status != AcquisitionStatus.Cancelled &&
                  run.Status == WorkflowRunStatus.Waiting && run.PendingSignalJson == null &&
                  run.WaitReason == "cloud.cloudAttention"
            orderby task.Id
            select task.Id).Take(32).ToArrayAsync(ct);
        // One round has a bounded total budget as well as a bounded candidate count. A large
        // manifest cannot monopolize startup or the completion notification thread.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        var cursor = afterTaskId;
        foreach (var id in ids)
        {
            if (tasks.IsShuttingDown) return cursor;
            cursor = id;
            try { await ReconcileAsync(id, budget.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return cursor; }
        }
        return ids.Length == 32 ? cursor : 0;
    }

    public async Task<bool> ReconcileAsync(int taskId, CancellationToken ct)
    {
        if (tasks.IsShuttingDown) return false;
        var gate = AcquisitionTaskGate.For(taskId);
        if (!await gate.WaitAsync(0, ct)) return false;
        try
        {
            var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
            if (task == null || task.DeletedAt != null || task.Status == AcquisitionStatus.Cancelled ||
                AcquisitionDownloadContinuationService.Read(task) is not {Mode: AcquisitionDownloadContinuationService.Automatic} intent ||
                intent.NextCheckAt > DateTimeOffset.UtcNow || tasks.IsPending("workflow.run." + intent.WorkflowRunId)) return false;
            var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == intent.WorkflowRunId, ct);
            if (run?.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
                run.WaitReason != "cloud.cloudAttention") return false;
            try
            {
                if (WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex ?? -1)?.Kind != "download.awaitCompletion") return false;
            }
            catch (System.IO.InvalidDataException) {return false;}
            var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
            var targets = workset?.Targets.Where(t => !t.Excluded).ToArray();
            if (targets is not {Length: > 0 and <= 100} || targets.Any(t => t.DownloadTaskId == null || t.TransportTaskId != null ||
                t.ProcessedArtifactId != null || t.PublishedArtifactId != null || t.ProcessingWaitReason != null ||
                t.PublicationWaitReason != null || t.DeliveryReview != null)) return false;
            var downloadIds = targets.Select(t => t.DownloadTaskId!.Value).Distinct().ToArray();
            if (await db.DownloadTasks.AsNoTracking().CountAsync(d => downloadIds.Contains(d.Id) &&
                d.Status == DownloadTaskDbModelStatus.Complete, ct) != downloadIds.Length) return false;

            var expected = AcquisitionRecoveryService.Version(task, run);
            var runGate = WorkflowRunMutationGate.For(run.Id); await runGate.WaitAsync(ct);
            try
            {
                var currentTask = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
                var currentRun = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id, ct);
                if (AcquisitionRecoveryService.Version(currentTask, currentRun) != expected) return false;
                // Persist backoff before expensive validation. A restart or repeated completion
                // event cannot repeatedly hash unchanged files after a failed/timeout check.
                task = await intentions.SaveWhileLockedAsync(task, run.Id, intent.Mode, ct,
                    DateTimeOffset.UtcNow.AddMinutes(5));
                expected = AcquisitionRecoveryService.Version(task, run);
            }
            finally {runGate.Release();}
            try
            {
                await resumer.ContinueConditionallyAsync(run.Id, true, Signal, async token =>
                {
                    var currentTask = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, token);
                    var currentRun = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id, token);
                    if (tasks.IsShuttingDown || tasks.IsPending("workflow.run." + run.Id) ||
                        AcquisitionRecoveryService.Version(currentTask, currentRun) != expected ||
                        AcquisitionDownloadContinuationService.Read(currentTask)?.Mode != AcquisitionDownloadContinuationService.Automatic)
                        throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
                    var diagnosis = await diagnostics.DiagnoseAsync(currentTask, currentRun, token, verifyFiles: true, verifyContent: true);
                    if (!diagnosis.Row.CanContinue || diagnosis.Row.ReasonCode != "RECOVERY_FILES_VERIFIED" || diagnosis.SignalJson != Signal)
                        throw new InvalidOperationException("RECOVERY_FILES_UNVERIFIED");
                }, ct);
                return true;
            }
            catch (InvalidOperationException) {return false;}
        }
        finally {gate.Release();}
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionLocalRecoveryBatchService
{
    private static string QueueStamp(AcquisitionTaskDbModel task) => Hash(new
        {Local = AcquisitionLocalRecoveryQueueEntry.Read(task), Continuation = AcquisitionContinuationEntry.Read(task)});

    private async Task<bool> CanReplaceQueue(AcquisitionTaskDbModel task, WorkflowRunDbModel? run, CancellationToken ct)
    {
        if (IsExecuting(run)) return false;
        var local = AcquisitionLocalRecoveryQueueEntry.Read(task);
        var deferred = AcquisitionContinuationEntry.Read(task);
        if (local is {IsOpen: true} && local.Status is not ("queued" or "paused" or "spaceBlocked") ||
            deferred is {IsOpen: true} && deferred.Status != "queued" ||
            local?.ReasonCode == "LOCAL_BATCH_OUTCOME_UNKNOWN" || deferred?.ReasonCode == "RECOVERY_OUTCOME_UNKNOWN") return false;
        foreach (var id in new[] {local?.WorkflowRunId, deferred?.WorkflowRunId}.Where(id => id != null && id != task.WorkflowRunId).Distinct())
            if (IsExecuting(await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct))) return false;
        return true;
    }

    private async Task<AcquisitionRecoveryQueueReference?> QueueReference(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        IReadOnlyDictionary<string, string> statuses, CancellationToken ct)
    {
        if (AcquisitionLocalRecoveryQueueEntry.Read(task) is {IsOpen: true} local)
            return new("localQueue", local.BatchId, statuses.GetValueOrDefault(local.BatchId, local.Status), local.Status,
                await CanReplaceQueue(task, run, ct));
        if (AcquisitionContinuationEntry.Read(task) is {IsOpen: true} deferred)
            return new("continuation", deferred.BatchId, deferred.Status, deferred.Status, await CanReplaceQueue(task, run, ct));
        return null;
    }

    private async Task<AcquisitionTaskDbModel> RetireSelectedQueue(AcquisitionTaskDbModel task, IReadOnlyCollection<int> heldRunIds, CancellationToken ct)
    {
        // The caller holds admission plus the owner gate, and has compared the exact preview stamp.
        // Only this selected row is withdrawn. Other members of the former batch are untouched.
        if (AcquisitionLocalRecoveryQueueEntry.Read(task) is {IsOpen: true} local)
        {
            task = await RevokeBatchGrantWhileLockedAsync(task, local, "LOCAL_BATCH_REQUEUED", ct,
                local.WorkflowRunId is { } run && heldRunIds.Contains(run));
            await SaveAsync(task, local with {Status = "cancelled", ReasonCode = "LOCAL_BATCH_REQUEUED"}, ct);
            task = (await CurrentAsync(task.Id, ct)).Item1!;
        }
        if (AcquisitionContinuationEntry.Read(task) is {Status: "queued"} deferred)
        {
            var updated = AcquisitionContinuationEntry.Write(task.BuilderStateJson,
                deferred with {Status = "cancelled", ReasonCode = "LOCAL_BATCH_REQUEUED", UpdatedAt = DateTimeOffset.UtcNow});
            if (await db.AcquisitionTasks.Where(t => t.Id == task.Id && t.BuilderStateJson == task.BuilderStateJson)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, updated), ct) != 1)
                throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            task = task with {BuilderStateJson = updated};
        }
        return task;
    }
}

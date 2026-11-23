using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionDeletionWriters(int[] RunIds, int[] SiteIds, string[] NativeIds)
{
    internal bool Covers(AcquisitionDeletionWriters now) => !now.RunIds.Except(RunIds).Any() &&
        !now.SiteIds.Except(SiteIds).Any() && !now.NativeIds.Except(NativeIds).Any();
}

internal interface IAcquisitionDeletionWriterControl
{
    Task StopWritersAsync(AcquisitionDeletionWriters writers, CancellationToken ct);
}

public sealed partial class AcquisitionDeletionActivityReader : IAcquisitionDeletionWriterControl
{
    private async Task<bool> ExclusivelyOwnedAsync(AcquisitionTaskDbModel task, int[] runs, int[] siteIds, string[] operations, CancellationToken ct)
    {
        var key = task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var runKeys = runs.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var foreign = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id != task.Id && t.DeletedAt == null)
            .Select(t => new {t.WorkflowRunId, t.PlanHistoryJson}).ToArrayAsync(ct);
        if (foreign.Any(t => t.WorkflowRunId is {} run && runs.Contains(run) || HistoryRuns(t.PlanHistoryJson).Intersect(runs).Any()))
            return false;
        if (await db.Set<WorkflowRunSourceDbModel>().AnyAsync(s => runs.Contains(s.WorkflowRunId) &&
                s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId != key, ct) ||
            await db.DownloadResultOwners.AnyAsync(o => runs.Contains(o.WorkflowRunId) && o.AcquisitionTaskId != task.Id, ct))
            return false;
        if (siteIds.Length > 0 && (await db.CloudDownloadOperations.AnyAsync(o => o.DownloadTaskId.HasValue &&
                siteIds.Contains(o.DownloadTaskId.Value) && !runs.Contains(o.WorkflowRunId), ct) ||
            await db.DownloadResultOwners.AnyAsync(o => siteIds.Contains(o.DownloadTaskId) && o.AcquisitionTaskId != task.Id, ct) ||
            await db.DownloadResultProcessing.AnyAsync(p => p.WorkflowRunId.HasValue && !runs.Contains(p.WorkflowRunId.Value) &&
                db.DownloadResults.Any(r => r.Id == p.DownloadResultId && siteIds.Contains(r.DownloadTaskId)), ct)))
            return false;
        return !await db.ArtifactOperationBindings.AnyAsync(b => operations.Contains(b.OperationId) &&
                (b.OwnerKind != "workflow" || !runKeys.Contains(b.OwnerId)), ct) &&
            !await db.ArtifactConsumers.AnyAsync(b => operations.Contains(b.OperationId) && b.ReleasedAt == null &&
                b.LeaseUntil > DateTime.UtcNow && (b.OwnerKind != "workflow" || !runKeys.Contains(b.OwnerId!)), ct);
    }

    // The caller holds the acquisition gate and checked the fixed plan and the ownership of
    // these exact writers. A preview alone must never cancel an operation or initialize a queue.
    async Task IAcquisitionDeletionWriterControl.StopWritersAsync(AcquisitionDeletionWriters writers, CancellationToken ct)
    {
        if (bTasks == null) throw new InvalidOperationException("ACQUISITION_TASK_ACTIVE");
        foreach (var run in writers.RunIds) await bTasks.Stop($"workflow.run.{run}");
        if (writers.SiteIds.Length > 0) await sites.Stop(t => writers.SiteIds.Contains(t.Id));
        foreach (var id in writers.NativeIds)
            if (id.StartsWith("bt:", StringComparison.Ordinal)) await bt.StopAsync(id[3..], ct);
            else if (id.StartsWith("http:", StringComparison.Ordinal)) await http.StopAsync(id[5..], ct);
        foreach (var run in writers.RunIds) await bTasks.WaitForExecutionAsync($"workflow.run.{run}", ct);
        // A queued run may not have acquired its BTask. Keep its checkpoint, outputs and node history.
        await db.WorkflowRuns.Where(r => writers.RunIds.Contains(r.Id) &&
                (r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Pending ||
                 r.Status == WorkflowRunStatus.Waiting && r.PendingSignalJson != null))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Cancelled)
                .SetProperty(r => r.PendingSignalJson, (string?)null)
                .SetProperty(r => r.CompletedAt, DateTime.Now), ct);
    }
}

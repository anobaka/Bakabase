using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>All site start paths use the same owner gate as task deletion, without taking a business-task gate.</summary>
public sealed class WorkflowOwnedDownloadStartGuard(BakabaseDbContext db, IEnumerable<IWorkflowRunMutationGuard> guards) : IDownloadTaskStartGuard
{
    public async Task<IAsyncDisposable> AcquireAsync(int downloadTaskId, CancellationToken ct = default)
    {
        var owners = await db.DownloadResultOwners.AsNoTracking().Where(o => o.DownloadTaskId == downloadTaskId)
            .Select(o => new {o.WorkflowRunId, o.AcquisitionTaskId}).ToArrayAsync(ct);
        var cloudRuns = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId == downloadTaskId)
            .Select(o => o.WorkflowRunId).ToArrayAsync(ct);
        var registryRuns = await db.ManagedDownloadTasks.AsNoTracking().Where(o => o.SiteTaskId == downloadTaskId && o.WorkflowRunId != null)
            .Select(o => o.WorkflowRunId!.Value).ToArrayAsync(ct);
        var held = new List<SemaphoreSlim>();
        try
        {
            foreach (var id in owners.Select(o => o.WorkflowRunId).Concat(cloudRuns).Concat(registryRuns).Where(id => id > 0).Distinct().Order())
            {
                var gate = WorkflowRunMutationGate.For(id); await gate.WaitAsync(ct); held.Add(gate);
                foreach (var guard in guards) await guard.EnsureCanMutateAsync(id, ct);
            }
            var taskIds = owners.Select(o => o.AcquisitionTaskId).ToArray();
            if (await db.AcquisitionTasks.AsNoTracking().AnyAsync(t => taskIds.Contains(t.Id) && t.DeletedAt != null, ct))
                throw new InvalidOperationException("ACQUISITION_TASK_DELETED: Continue from a retained task rather than restarting a deleted owner's download.");
            return new Lease(held);
        }
        catch {foreach (var gate in held) gate.Release(); throw;}
    }
    private sealed class Lease(List<SemaphoreSlim> gates) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        { for (var i = gates.Count - 1; i >= 0; i--) gates[i].Release(); return ValueTask.CompletedTask; }
    }
}

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
        var (runs, resultOwners) = await ReadOwnershipAsync([downloadTaskId], ct);
        var held = new List<SemaphoreSlim>();
        try
        {
            foreach (var id in runs[downloadTaskId].Distinct().Order())
            {
                var gate = WorkflowRunMutationGate.For(id); await gate.WaitAsync(ct); held.Add(gate);
                foreach (var guard in guards) await guard.EnsureCanMutateAsync(id, ct);
            }
            if ((await ReadDeletedOwnersAsync(resultOwners, ct)).Contains(downloadTaskId))
                throw new InvalidOperationException("ACQUISITION_TASK_DELETED: Continue from a retained task rather than restarting a deleted owner's download.");
            return new Lease(held);
        }
        catch {foreach (var gate in held) gate.Release(); throw;}
    }

    public async Task<IReadOnlySet<int>> ReadBlockedTaskIdsAsync(IReadOnlyCollection<int> downloadTaskIds,
        CancellationToken ct = default)
    {
        var (runs, resultOwners) = await ReadOwnershipAsync(downloadTaskIds, ct);
        var denied = await ReadDeletedOwnersAsync(resultOwners, ct);
        var runIds = runs.SelectMany(r => r).ToHashSet();
        if (runIds.Count == 0) return denied;
        var refused = new HashSet<int>();
        foreach (var guard in guards)
            refused.UnionWith((await guard.ReadMutationRefusalsAsync(runIds, ct)).Keys);
        denied.UnionWith(runs.Where(download => download.Any(refused.Contains)).Select(download => download.Key));
        return denied;
    }

    private async Task<(ILookup<int, int> Runs, IReadOnlyList<ResultOwner> ResultOwners)> ReadOwnershipAsync(
        IReadOnlyCollection<int> taskIds, CancellationToken ct)
    {
        var results = await db.DownloadResultOwners.AsNoTracking().Where(o => taskIds.Contains(o.DownloadTaskId))
            .Select(o => new ResultOwner(o.DownloadTaskId, o.WorkflowRunId, o.AcquisitionTaskId)).ToArrayAsync(ct);
        var cloud = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId != null &&
                taskIds.Contains(o.DownloadTaskId.Value))
            .Select(o => new {TaskId = o.DownloadTaskId!.Value, RunId = o.WorkflowRunId}).ToArrayAsync(ct);
        var registry = await db.ManagedDownloadTasks.AsNoTracking().Where(o => o.SiteTaskId != null &&
                taskIds.Contains(o.SiteTaskId.Value) && o.WorkflowRunId != null)
            .Select(o => new {TaskId = o.SiteTaskId!.Value, RunId = o.WorkflowRunId!.Value}).ToArrayAsync(ct);
        var runs = results.Select(o => (o.TaskId, o.RunId)).Concat(cloud.Select(o => (o.TaskId, o.RunId)))
            .Concat(registry.Select(o => (o.TaskId, o.RunId))).Where(o => o.RunId > 0)
            .ToLookup(o => o.TaskId, o => o.RunId);
        return (runs, results);
    }

    private async Task<HashSet<int>> ReadDeletedOwnersAsync(IReadOnlyList<ResultOwner> owners, CancellationToken ct)
    {
        if (owners.Count == 0) return [];
        var ids = owners.Select(o => o.AcquisitionId).Distinct().ToArray();
        var deleted = (await db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id) && t.DeletedAt != null)
            .Select(t => t.Id).ToArrayAsync(ct)).ToHashSet();
        return owners.Where(o => deleted.Contains(o.AcquisitionId)).Select(o => o.TaskId).ToHashSet();
    }

    private sealed record ResultOwner(int TaskId, int RunId, int AcquisitionId);

    private sealed class Lease(List<SemaphoreSlim> gates) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        { for (var i = gates.Count - 1; i >= 0; i--) gates[i].Release(); return ValueTask.CompletedTask; }
    }
}

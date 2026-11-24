using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionThroughputOwner(int TaskId, string? Title, bool MayHaveFileIo,
    IReadOnlyList<double?> Downloads, long? ReceivedBytes = null);
public sealed record AcquisitionThroughputOwners(IReadOnlyList<AcquisitionThroughputOwner> Tasks,
    IReadOnlyDictionary<(string OwnerKind, string OwnerId), int> FileOwners);

public interface IAcquisitionThroughputDownloadSource
{
    Task<AcquisitionThroughputOwners> ReadAsync(IReadOnlyList<FileIoObservationSample> io, CancellationToken ct);
}

public sealed class AcquisitionThroughputDownloadSource(BakabaseDbContext db, IAcquisitionRuntimeDownloadReader downloads,
    TimeProvider? time = null) : IAcquisitionThroughputDownloadSource
{
    private sealed record ManagedBinding(string Id, string? ParentId, int? WorkflowRunId, bool UserStopped);
    private sealed record RunOwner(int TaskId, int RunId, WorkflowRunStatus Status, string? NodeKind);

    public async Task<AcquisitionThroughputOwners> ReadAsync(IReadOnlyList<FileIoObservationSample> io, CancellationToken ct)
    {
        var measuredOwners = io.Select(s => (s.OwnerKind, s.OwnerId)).Distinct().ToArray();
        var siteIds = measuredOwners.Where(k => k.OwnerKind == "downloadTask").Select(k => PositiveId(k.OwnerId))
            .OfType<int>().ToHashSet();
        var transportIds = measuredOwners.Where(k => k.OwnerKind == "independentDownload")
            .SelectMany(k => NativeIds(k.OwnerId)).ToHashSet();
        var network = io.Where(s => s.NetworkObserved && s.NetworkDownloadId != null)
            .ToLookup(s => s.NetworkDownloadId!, StringComparer.Ordinal);
        foreach (var stream in network)
        {
            if (stream.Key.StartsWith("site:", StringComparison.Ordinal) && PositiveId(stream.Key[5..]) is {} site)
                siteIds.Add(site);
            else
                transportIds.UnionWith(NativeIds(stream.Key));
        }
        var snapshots = await downloads.ReadThroughputAsync(siteIds, transportIds, [], ct);
        var physical = snapshots.GroupBy(s => s.Id).Select(g => g.MaxBy(s => s.ObservedAt)!).ToArray();
        var observedJobs = physical.Select(j => j.Id).Concat(physical.Select(j => j.ParentId).OfType<string>())
            .Concat(transportIds).Concat(siteIds.Select(id => "site:" + id)).ToHashSet();

        var managed = await ReadManaged(observedJobs);
        var parentsFromBindings = managed.Select(m => m.ParentId).OfType<string>().Except(observedJobs).ToHashSet();
        if (parentsFromBindings.Count > 0)
        {
            observedJobs.UnionWith(parentsFromBindings);
            managed.AddRange(await ReadManaged(parentsFromBindings));
        }
        siteIds.UnionWith(observedJobs.Where(id => id.StartsWith("site:", StringComparison.Ordinal))
            .Select(id => PositiveId(id[5..])).OfType<int>());

        var cloudKeys = measuredOwners.Where(k => k.OwnerKind == "cloudOperation").Select(k => k.OwnerId).ToHashSet();
        var cloud = cloudKeys.Count == 0 && siteIds.Count == 0 ? [] : await db.CloudDownloadOperations.AsNoTracking()
            .Where(o => cloudKeys.Contains(o.Id) || o.Kind == "download" && o.DownloadTaskId.HasValue && siteIds.Contains(o.DownloadTaskId.Value))
            .Select(o => new {o.Id, o.WorkflowRunId, o.DownloadTaskId, o.Kind}).ToListAsync(ct);
        var owners = siteIds.Count == 0 ? [] : await (from owner in db.DownloadResultOwners.AsNoTracking()
            where siteIds.Contains(owner.DownloadTaskId)
            join task in db.AcquisitionTasks.AsNoTracking() on owner.AcquisitionTaskId equals task.Id
            select new {owner.WorkflowRunId, owner.AcquisitionTaskId, owner.DownloadTaskId, task.DeletedAt}).ToListAsync(ct);
        owners.RemoveAll(o => o.DeletedAt != null);

        var operationIds = measuredOwners.Where(k => k.OwnerKind == "artifactOperation").Select(k => k.OwnerId).ToHashSet();
        var bindings = operationIds.Count == 0 ? [] : await db.ArtifactOperationBindings.AsNoTracking().Where(b => operationIds.Contains(b.OperationId))
            .Select(b => new {b.OperationId, b.OwnerId, b.OwnerKind}).Distinct().ToListAsync(ct);
        var operationRuns = bindings.Where(b => b.OwnerKind == "workflow").GroupBy(b => b.OperationId)
            .Where(g => g.Count() == 1 && PositiveId(g.First().OwnerId).HasValue)
            .ToDictionary(g => g.Key, g => PositiveId(g.First().OwnerId)!.Value);
        var observedRuns = measuredOwners.Where(k => k.OwnerKind == "workflow").Select(k => PositiveId(k.OwnerId)).OfType<int>()
            .Concat(physical.Select(j => j.WorkflowRunId).OfType<int>()).Concat(managed.Select(m => m.WorkflowRunId).OfType<int>())
            .Concat(cloud.Select(o => o.WorkflowRunId)).Concat(owners.Select(o => o.WorkflowRunId)).Concat(operationRuns.Values).ToHashSet();

        // Separate the two indexed lookups. In a single join SQLite otherwise favors DeletedAt IS NULL
        // and may walk the entire acquisition history to discover that only one workflow is active.
        var runIds = (await db.WorkflowRuns.AsNoTracking().Where(r => r.Status == WorkflowRunStatus.Running ||
            r.Status == WorkflowRunStatus.Pending).Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        runIds.UnionWith(observedRuns);
        var runIndex = runIds.Count == 0 ? [] : await (from task in db.AcquisitionTasks.AsNoTracking()
            where task.WorkflowRunId.HasValue && runIds.Contains(task.WorkflowRunId.Value)
            join run in db.WorkflowRuns.AsNoTracking() on task.WorkflowRunId equals run.Id
            select new {task.DeletedAt, Owner = new RunOwner(task.Id, run.Id, run.Status, run.Status == WorkflowRunStatus.Running
                ? db.WorkflowNodeExecutions.Where(n => n.WorkflowRunId == run.Id && n.StepIndex == run.CurrentStepIndex)
                    .OrderByDescending(n => n.Attempt).ThenByDescending(n => n.Id).Select(n => n.Kind).FirstOrDefault()
                : null)}).ToListAsync(ct);
        var runs = runIndex.Where(x => x.DeletedAt == null).Select(x => x.Owner).ToArray();
        var byRun = runs.GroupBy(r => r.RunId).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().TaskId);
        var formerRuns = observedRuns.Except(runIndex.Select(r => r.Owner.RunId)).ToHashSet();
        if (formerRuns.Count > 0)
        {
            // A final event may arrive just after a replan detached the old run from its acquisition.
            var links = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => formerRuns.Contains(s.WorkflowRunId)
                && s.Module == "acquisition" && s.EntityKind == "task")
                .Select(s => new {s.WorkflowRunId, s.EntityId}).Distinct().ToListAsync(ct);
            foreach (var group in links.GroupBy(s => s.WorkflowRunId))
                if (group.Count() == 1 && PositiveId(group.First().EntityId) is {} task)
                    byRun[group.Key] = task;
        }

        var origins = new Dictionary<string, (int TaskId, int Priority)>(StringComparer.Ordinal);
        // Prefer an explicitly registered acquisition owner over inferred parentage. Shared consumers
        // without a primary owner use a stable task id, so each physical stream enters the total once.
        void Bind(string id, int task, int priority)
        {
            if (!origins.TryGetValue(id, out var existing) || (priority, task).CompareTo((existing.Priority, existing.TaskId)) < 0)
                origins[id] = (task, priority);
        }
        foreach (var op in cloud)
            if (op.Kind == "download" && op.DownloadTaskId.HasValue && byRun.TryGetValue(op.WorkflowRunId, out var owner))
                Bind("site:" + op.DownloadTaskId, owner, 2);
        foreach (var owner in owners)
            if (!byRun.TryGetValue(owner.WorkflowRunId, out var source) || source == owner.AcquisitionTaskId)
                Bind("site:" + owner.DownloadTaskId, owner.AcquisitionTaskId, 0);
        var stopped = managed.Where(m => m.UserStopped).Select(m => m.Id).ToHashSet();
        foreach (var job in managed)
            if (job.WorkflowRunId is {} run && byRun.TryGetValue(run, out var owner)) Bind(job.Id, owner, 1);
        foreach (var job in managed)
            if (job.WorkflowRunId == null && job.ParentId is {} parent && origins.TryGetValue(parent, out var source))
                Bind(job.Id, source.TaskId, 3);

        Task<List<ManagedBinding>> ReadManaged(HashSet<string> ids) => ids.Count == 0 ? Task.FromResult(new List<ManagedBinding>()) :
            db.ManagedDownloadTasks.AsNoTracking()
            .Where(m => ids.Contains(m.Id)).Select(m => new ManagedBinding(m.Id, m.ParentId, m.WorkflowRunId, m.UserStopped)).ToListAsync(ct);
        var parents = physical.Where(s => s.ParentId != null).Select(s => s.ParentId!).ToHashSet();
        var busy = runs.Where(r => r.Status == WorkflowRunStatus.Running && r.NodeKind != "download.awaitCompletion")
            .Select(r => r.TaskId).ToHashSet();
        var queued = runs.Where(r => r.Status == WorkflowRunStatus.Pending).Select(r => r.TaskId).ToHashSet();
        var rates = new Dictionary<string, (int TaskId, double? Speed)>(StringComparer.Ordinal);
        var now = (time ?? TimeProvider.System).GetUtcNow();
        foreach (var leaf in physical)
        {
            if (leaf.ParentId != null && origins.TryGetValue(leaf.ParentId, out var parent))
                Bind(leaf.Id, parent.TaskId, parent.Priority);
            else if (leaf.WorkflowRunId is {} run)
            {
                if (!byRun.TryGetValue(run, out var owner)) continue;
                Bind(leaf.Id, owner, 1);
            }
            if (!origins.TryGetValue(leaf.Id, out var origin) || parents.Contains(leaf.Id) || stopped.Contains(leaf.Id) ||
                leaf.ParentId != null && stopped.Contains(leaf.ParentId)) continue;
            if (leaf.Status == "queued") queued.Add(origin.TaskId);
            if (leaf.Status is not ("running" or "stopping")) continue;
            busy.Add(origin.TaskId);
            if (leaf.Status == "stopping" || leaf.Stage is "Verifying" or "VerifyingSource" or "VerifyingLocalFiles" or "Checking" or
                "Saving" or "Stopping" or "Stopped" or "Failed" or "Completed" or "TransferPaused" or "TransferCancelled" or
                "TransferRetrying" or "TransferFailed" or "TransferStateUnavailable")
                continue;
            var age = now - leaf.ObservedAt;
            rates[leaf.Id] = (origin.TaskId, leaf.ProblemCode == null && age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(15) &&
                leaf.SpeedBytesPerSecond is {} speed && double.IsFinite(speed) && speed >= 0 ? speed : null);
        }

        var cloudOwners = cloud.ToDictionary(o => o.Id, o => o.WorkflowRunId);
        var knownNative = managed.Select(m => m.Id).Concat(physical.Select(j => j.Id)).ToHashSet();
        var fileOwners = new Dictionary<(string OwnerKind, string OwnerId), int>();
        foreach (var key in measuredOwners)
        {
            int? task = key.OwnerKind switch
            {
                "workflow" when PositiveId(key.OwnerId) is {} run && byRun.TryGetValue(run, out var owner) => owner,
                "downloadTask" when PositiveId(key.OwnerId) is {} id && origins.TryGetValue("site:" + id, out var owner) => owner.TaskId,
                "cloudOperation" when cloudOwners.TryGetValue(key.OwnerId, out var run) && byRun.TryGetValue(run, out var owner) => owner,
                "artifactOperation" when operationRuns.TryGetValue(key.OwnerId, out var run) && byRun.TryGetValue(run, out var owner) => owner,
                "independentDownload" when NativeIds(key.OwnerId).Where(knownNative.Contains).ToArray() is [var native] &&
                    origins.TryGetValue(native, out var owner) => owner.TaskId,
                _ => null
            };
            if (task.HasValue) fileOwners[key] = task.Value;
        }
        // Each physical transfer has one measurement. A byte receipt survives completion, unlike
        // a provider speed, and takes precedence over the speed of that same transfer only.
        var received = new Dictionary<string, (int TaskId, long Bytes)>(StringComparer.Ordinal);
        foreach (var stream in network)
        {
            int taskId;
            if (origins.TryGetValue(stream.Key, out var primary)) taskId = primary.TaskId;
            else
            {
                var mapped = stream.Select(s => (s.OwnerKind, s.OwnerId)).Where(fileOwners.ContainsKey)
                    .Select(key => fileOwners[key]).Distinct().ToArray();
                if (mapped.Length != 1) continue;
                taskId = mapped[0];
            }
            received[stream.Key] = (taskId, stream.Sum(s => s.NetworkReceivedBytes));
        }
        var speedsByTask = rates.Where(r => !received.ContainsKey(r.Key))
            .ToLookup(r => r.Value.TaskId, r => r.Value.Speed);
        var bytesByTask = received.Values.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.Sum(r => r.Bytes));
        var visible = busy.Concat(queued).Concat(fileOwners.Values).Concat(bytesByTask.Keys).ToHashSet();
        var tasks = visible.Count == 0 ? [] : await db.AcquisitionTasks.AsNoTracking().Where(t => visible.Contains(t.Id))
            .OrderBy(t => t.Id).Select(t => new {t.Id, t.Title, t.DeletedAt}).ToListAsync(ct);
        tasks.RemoveAll(t => t.DeletedAt != null);
        visible = tasks.Select(t => t.Id).ToHashSet();
        return new(tasks.Select(t => new AcquisitionThroughputOwner(t.Id, t.Title, busy.Contains(t.Id),
            speedsByTask[t.Id].ToArray(), bytesByTask.TryGetValue(t.Id, out var bytes) ? bytes : null)).ToArray(),
            fileOwners.Where(o => visible.Contains(o.Value)).ToDictionary());
    }

    private static int? PositiveId(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    private static IEnumerable<string> NativeIds(string owner)
    {
        // Both native engines issue GUIDs. Their storage context can carry the unqualified id.
        if (Guid.TryParseExact(owner, "N", out _)) return ["http:" + owner, "bt:" + owner];
        if ((owner.StartsWith("http:", StringComparison.Ordinal) || owner.StartsWith("bt:", StringComparison.Ordinal)) &&
            Guid.TryParseExact(owner[(owner.IndexOf(':') + 1)..], "N", out _)) return [owner];
        return [];
    }
}

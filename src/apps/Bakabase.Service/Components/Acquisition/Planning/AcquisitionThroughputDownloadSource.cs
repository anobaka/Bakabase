using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
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
    private sealed record RunOwner(int TaskId, int RunId, WorkflowRunStatus Status, string? NodeKind);

    public async Task<AcquisitionThroughputOwners> ReadAsync(IReadOnlyList<FileIoObservationSample> io, CancellationToken ct)
    {
        // The current run index also covers native transfers that outlive their workflow's waiting or terminal state.
        // Recorded tasks without a current run have no active download to sample.
        var runs = await (from task in db.AcquisitionTasks.AsNoTracking()
            join run in db.WorkflowRuns.AsNoTracking() on task.WorkflowRunId equals run.Id
            where task.DeletedAt == null
            select new RunOwner(task.Id, run.Id, run.Status, run.Status == WorkflowRunStatus.Running
                ? db.WorkflowNodeExecutions.Where(n => n.WorkflowRunId == run.Id && n.StepIndex == run.CurrentStepIndex)
                    .OrderByDescending(n => n.Attempt).ThenByDescending(n => n.Id).Select(n => n.Kind).FirstOrDefault()
                : null)).ToListAsync(ct);
        var byRun = runs.GroupBy(r => r.RunId).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var runIds = byRun.Keys.ToHashSet();
        var measuredOwners = io.Select(s => (s.OwnerKind, s.OwnerId)).Distinct().ToArray();
        var sampledSites = measuredOwners.Where(k => k.OwnerKind == "downloadTask").Select(k => PositiveId(k.OwnerId))
            .OfType<int>().ToHashSet();
        var operationIds = measuredOwners.Where(k => k.OwnerKind == "artifactOperation").Select(k => k.OwnerId).ToHashSet();
        var origins = new Dictionary<string, (int TaskId, int Priority)>(StringComparer.Ordinal);

        // Prefer an explicitly registered acquisition owner over inferred parentage. Shared consumers
        // without a primary owner use a stable task id, so each physical stream enters the total once.
        void Bind(string id, int task, int priority)
        {
            if (!origins.TryGetValue(id, out var existing) || (priority, task).CompareTo((existing.Priority, existing.TaskId)) < 0)
                origins[id] = (task, priority);
        }
        var cloud = await db.CloudDownloadOperations.AsNoTracking()
            .Where(o => o.Kind == "download" && o.DownloadTaskId != null && runIds.Contains(o.WorkflowRunId))
            .Select(o => new {o.WorkflowRunId, o.DownloadTaskId}).Distinct().ToListAsync(ct);
        foreach (var op in cloud) Bind("site:" + op.DownloadTaskId, byRun[op.WorkflowRunId].TaskId, 2);
        var owners = await db.DownloadResultOwners.AsNoTracking().Where(o => runIds.Contains(o.WorkflowRunId))
            .Select(o => new {o.WorkflowRunId, o.AcquisitionTaskId, o.DownloadTaskId}).ToListAsync(ct);
        foreach (var owner in owners)
            if (byRun[owner.WorkflowRunId].TaskId == owner.AcquisitionTaskId)
                Bind("site:" + owner.DownloadTaskId, owner.AcquisitionTaskId, 0);

        var nativeSamples = measuredOwners.Where(k => k.OwnerKind == "independentDownload").SelectMany(k => NativeIds(k.OwnerId)).ToHashSet();
        var managed = await db.ManagedDownloadTasks.AsNoTracking()
            .Where(m => m.WorkflowRunId.HasValue && runIds.Contains(m.WorkflowRunId.Value) || nativeSamples.Contains(m.Id))
            .Select(m => new {m.Id, m.ParentId, m.WorkflowRunId, m.UserStopped}).ToListAsync(ct);
        var stopped = managed.Where(m => m.UserStopped).Select(m => m.Id).ToHashSet();
        foreach (var job in managed)
        {
            if (job.WorkflowRunId is {} run && byRun.TryGetValue(run, out var owner)) Bind(job.Id, owner.TaskId, 1);
            else if (job.WorkflowRunId == null && job.ParentId is {} parent && origins.TryGetValue(parent, out var source))
                Bind(job.Id, source.TaskId, 3);
        }

        var boundSites = origins.Keys.Where(id => id.StartsWith("site:", StringComparison.Ordinal))
            .Select(id => PositiveId(id[5..])).OfType<int>().ToHashSet();
        var workingTasks = runs.Where(r => r.Status == WorkflowRunStatus.Running).Select(r => r.TaskId).ToHashSet();
        // Persisted in-progress includes the provider queue. A completed site can still verify files for a running workflow.
        var siteIds = boundSites.Count == 0 ? [] : (await db.DownloadTasks.AsNoTracking().Where(t =>
            boundSites.Contains(t.Id) && t.Status == DownloadTaskDbModelStatus.InProgress).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        siteIds.UnionWith(boundSites.Where(id => sampledSites.Contains(id) || workingTasks.Contains(origins["site:" + id].TaskId)));
        // Linked children are selected through their site's latest result generation, never through a stale native binding.
        var directIds = managed.Where(m => m.ParentId == null && !m.Id.StartsWith("site:", StringComparison.Ordinal) &&
            origins.ContainsKey(m.Id) && !m.UserStopped).Select(m => m.Id).ToHashSet();
        var snapshots = await downloads.ReadThroughputAsync(siteIds, directIds, runIds, ct);
        var physical = snapshots.GroupBy(s => s.Id).Select(g => g.MaxBy(s => s.ObservedAt)!).ToArray();
        var parents = physical.Where(s => s.ParentId != null).Select(s => s.ParentId!).ToHashSet();
        var busy = runs.Where(r => r.Status == WorkflowRunStatus.Running && r.NodeKind != "download.awaitCompletion")
            .Select(r => r.TaskId).ToHashSet();
        var queued = runs.Where(r => r.Status == WorkflowRunStatus.Pending).Select(r => r.TaskId).ToHashSet();
        var rates = new Dictionary<int, List<double?>>();
        var now = (time ?? TimeProvider.System).GetUtcNow();
        foreach (var leaf in physical)
        {
            if (leaf.ParentId != null && origins.TryGetValue(leaf.ParentId, out var parent))
                Bind(leaf.Id, parent.TaskId, parent.Priority);
            else if (leaf.WorkflowRunId is {} run)
            {
                if (!byRun.TryGetValue(run, out var owner)) continue;
                Bind(leaf.Id, owner.TaskId, 1);
            }
            if (!origins.TryGetValue(leaf.Id, out var origin) || parents.Contains(leaf.Id) || stopped.Contains(leaf.Id) ||
                leaf.ParentId != null && stopped.Contains(leaf.ParentId)) continue;
            if (leaf.Status == "queued") queued.Add(origin.TaskId);
            if (leaf.Status is not ("running" or "stopping")) continue;
            busy.Add(origin.TaskId);
            if (leaf.Status == "stopping" || leaf.Stage is "Verifying" or "VerifyingSource" or "VerifyingLocalFiles" or "Checking" or "Saving")
                continue;
            if (!rates.TryGetValue(origin.TaskId, out var taskRates)) rates[origin.TaskId] = taskRates = [];
            var age = now - leaf.ObservedAt;
            taskRates.Add(leaf.ProblemCode == null && age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(15) &&
                leaf.SpeedBytesPerSecond is {} speed && double.IsFinite(speed) && speed >= 0 ? speed : null);
        }

        var operationOwners = new Dictionary<string, int>();
        if (operationIds.Count > 0)
        {
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
                operationIds.Contains(b.OperationId)).Select(b => new {b.OperationId, b.OwnerId}).Distinct().ToListAsync(ct);
            foreach (var group in bindings.GroupBy(b => b.OperationId))
            {
                var ids = group.Select(b => PositiveId(b.OwnerId)).Distinct().ToArray();
                if (ids is [{ } run] && byRun.TryGetValue(run, out var owner)) operationOwners[group.Key] = owner.TaskId;
            }
        }
        var cloudKeys = measuredOwners.Where(k => k.OwnerKind == "cloudOperation").Select(k => k.OwnerId).ToHashSet();
        var cloudOwners = cloudKeys.Count == 0 ? [] : await db.CloudDownloadOperations.AsNoTracking()
            .Where(o => cloudKeys.Contains(o.Id) && runIds.Contains(o.WorkflowRunId))
            .ToDictionaryAsync(o => o.Id, o => o.WorkflowRunId, ct);
        var fileOwners = new Dictionary<(string OwnerKind, string OwnerId), int>();
        foreach (var key in measuredOwners)
        {
            int? task = key.OwnerKind switch
            {
                "workflow" when PositiveId(key.OwnerId) is {} run && byRun.TryGetValue(run, out var owner) => owner.TaskId,
                "downloadTask" when PositiveId(key.OwnerId) is {} id && origins.TryGetValue("site:" + id, out var owner) => owner.TaskId,
                "cloudOperation" when cloudOwners.TryGetValue(key.OwnerId, out var run) => byRun[run].TaskId,
                "artifactOperation" when operationOwners.TryGetValue(key.OwnerId, out var id) => id,
                "independentDownload" => NativeIds(key.OwnerId).Where(origins.ContainsKey).Select(id => origins[id].TaskId)
                    .Distinct().ToArray() is [var owner] ? owner : null,
                _ => null
            };
            if (task.HasValue) fileOwners[key] = task.Value;
        }
        var visible = busy.Concat(queued).Concat(fileOwners.Values).ToHashSet();
        var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => t.DeletedAt == null && visible.Contains(t.Id))
            .OrderBy(t => t.Id).Select(t => new {t.Id, t.Title}).ToListAsync(ct);
        visible = tasks.Select(t => t.Id).ToHashSet();
        return new(tasks.Select(t => new AcquisitionThroughputOwner(t.Id, t.Title, busy.Contains(t.Id),
            rates.GetValueOrDefault(t.Id) ?? [])).ToArray(), fileOwners.Where(o => visible.Contains(o.Value)).ToDictionary());
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

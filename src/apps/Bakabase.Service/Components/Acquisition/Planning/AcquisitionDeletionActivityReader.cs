using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionDeletionRun(int Id, WorkflowRunStatus Status, int? CurrentStepIndex, string? WaitReason,
    DateTime? WaitingSince, string? PendingSignalJson, DateTime? CompletedAt);
public sealed record AcquisitionDeletionActivity(IReadOnlyList<int> RunIds, string Version, string? BlockReason)
{
    internal AcquisitionDeletionWriters? Writers { get; init; }
    internal IReadOnlyList<AcquisitionDeletionRun>? ObservedRuns { get; init; }
    internal bool CanAbandon(AcquisitionTaskDbModel task, bool reviewDeletedWaits) =>
        (task.DeletedAt != null) == reviewDeletedWaits && BlockReason == null && ObservedRuns is {Count: > 0} &&
        ObservedRuns.All(r => r.PendingSignalJson == null && r.Status is not (WorkflowRunStatus.Running or WorkflowRunStatus.Pending)) &&
        (task.WorkflowRunId == null || ObservedRuns.Any(r => r.Id == task.WorkflowRunId)) &&
        AcquisitionLocalRecoveryQueueEntry.Read(task) is not {IsOpen: true} && AcquisitionContinuationEntry.Read(task) is not {IsOpen: true};

    internal bool MatchesAfterCleanup(AcquisitionDeletionActivity now, IReadOnlyList<int>? closedRuns)
    {
        if (!RunIds.SequenceEqual(now.RunIds)) return false;
        if (Version == now.Version) return true;
        if (closedRuns is not { Count: > 0 } || ObservedRuns == null || now.ObservedRuns == null ||
            ObservedRuns.Count != now.ObservedRuns.Count) return false;
        var closed = closedRuns.ToHashSet();
        // Superseded-run cleanup changes only Status and CompletedAt for the receipted old waits.
        return ObservedRuns.Zip(now.ObservedRuns).All(pair => closed.Contains(pair.First.Id)
            ? pair.First.Status == WorkflowRunStatus.Waiting && pair.First.PendingSignalJson == null &&
              pair.Second.Status == WorkflowRunStatus.Cancelled && pair.Second.CompletedAt != null &&
              pair.First with { Status = pair.Second.Status, CompletedAt = pair.Second.CompletedAt } == pair.Second
            : pair.First == pair.Second);
    }
}
public interface IAcquisitionDeletionActivityReader
{
    Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(IReadOnlyList<AcquisitionTaskDbModel> tasks, CancellationToken ct);
}

/// <summary>Conservative ownership audit. Never initializes transports or changes download state.</summary>
public sealed partial class AcquisitionDeletionActivityReader(BakabaseDbContext db, DownloadTaskService sites,
    IBtDownloadManager bt, IHttpDownloadManager http, BTaskManager? bTasks = null) : IAcquisitionDeletionActivityReader
{
    public async Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(IReadOnlyList<AcquisitionTaskDbModel> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return new Dictionary<int, AcquisitionDeletionActivity>();
        var taskIds = tasks.Select(t => t.Id).ToArray();
        var taskKeys = taskIds.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var links = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && taskKeys.Contains(s.EntityId)).Select(s => new {s.EntityId, s.WorkflowRunId}).ToListAsync(ct);
        var directOwners = await db.DownloadResultOwners.AsNoTracking().Where(o => taskIds.Contains(o.AcquisitionTaskId))
            .Select(o => new {o.AcquisitionTaskId, o.WorkflowRunId}).ToListAsync(ct);
        var runsByTask = tasks.ToDictionary(t => t.Id, t => links.Where(l => l.EntityId == t.Id.ToString()).Select(l => l.WorkflowRunId)
            .Concat(directOwners.Where(o => o.AcquisitionTaskId == t.Id).Select(o => o.WorkflowRunId))
            .Concat(t.WorkflowRunId is {} run ? new[] {run} : []).Concat(HistoryRuns(t.PlanHistoryJson)).Distinct().Order().ToArray());
        var runIds = runsByTask.Values.SelectMany(ids => ids).Distinct().ToArray();
        var runKeys = runIds.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var runs = await db.WorkflowRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).Select(r =>
            new AcquisitionDeletionRun(r.Id, r.Status, r.CurrentStepIndex, r.WaitReason, r.WaitingSince,
                r.PendingSignalJson, r.CompletedAt)).ToListAsync(ct);
        var owners = await db.DownloadResultOwners.AsNoTracking().Where(o => taskIds.Contains(o.AcquisitionTaskId) || runIds.Contains(o.WorkflowRunId))
            .Select(o => new {o.AcquisitionTaskId, o.WorkflowRunId, o.DownloadTaskId}).ToListAsync(ct);
        var clouds = await db.CloudDownloadOperations.AsNoTracking().Where(o => runIds.Contains(o.WorkflowRunId) && o.DownloadTaskId != null)
            .Select(o => new {o.WorkflowRunId, o.DownloadTaskId}).ToListAsync(ct);
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" && runKeys.Contains(b.OwnerId))
            .Select(b => new {b.OwnerId, b.OperationId}).ToListAsync(ct);
        var consumers = await db.ArtifactConsumers.AsNoTracking().Where(b => b.OwnerKind == "workflow" && runKeys.Contains(b.OwnerId!))
            .Select(b => new {b.OwnerId, b.OperationId}).ToListAsync(ct);
        var operationIds = bindings.Select(b => b.OperationId).Concat(consumers.Select(b => b.OperationId)).Distinct().ToArray();
        // In-flight native downloads only have a lease until completion writes durable bindings.
        var operations = await db.ArtifactOperations.AsNoTracking().Where(o => operationIds.Contains(o.Id) ||
            o.LeaseOwner != null && o.LeaseOwner.StartsWith("workflow:"))
            .Select(o => new {o.Id, o.Kind, o.IntentKey, o.LeaseOwner, o.LeaseUntil, o.State}).ToListAsync(ct);
        var nodeOutputs = await db.WorkflowNodeExecutions.AsNoTracking().Where(n => runIds.Contains(n.WorkflowRunId) && n.OutputsJson != null)
            .Select(n => new {n.WorkflowRunId, n.OutputsJson}).ToListAsync(ct);
        var transportOutputs = nodeOutputs.SelectMany(n => ReadOutputIds(n.OutputsJson).Select(id => (n.WorkflowRunId, Id: id))).ToArray();
        var registry = await db.ManagedDownloadTasks.AsNoTracking().ToListAsync(ct);
        var btJobs = await bt.ReadSnapshotAsync(ct); var httpJobs = await http.ReadSnapshotAsync(ct);
        var native = btJobs.Select(j => new Native("bt:" + j.Id, (int)j.Status, j.WorkflowRunId, j.DownloadTaskId, j.DownloadResultId, j.SourceKind, j.SourceId))
            .Concat(httpJobs.Select(j => new Native("http:" + j.Id, (int)j.Status, j.WorkflowRunId, j.DownloadTaskId, j.DownloadResultId, j.SourceKind, j.SourceId))).ToArray();
        var candidateIntents = operations.Select(o => o.IntentKey).ToHashSet();
        var allSites = owners.Select(o => o.DownloadTaskId).Concat(clouds.Select(o => o.DownloadTaskId!.Value))
            .Concat(registry.Where(r => r.WorkflowRunId is {} run && runIds.Contains(run) ||
                r.OriginKind == "workflow" && r.OriginId != null && (candidateIntents.Contains(r.OriginId) || runKeys.Contains(r.OriginId)) ||
                r.OriginKind == "acquisition" && taskKeys.Contains(r.OriginId!)).Select(r => r.SiteTaskId ?? SiteParent(r.ParentId)))
            .Where(id => id > 0).Distinct().ToArray();
        var results = await db.DownloadResults.AsNoTracking().Where(r => allSites.Contains(r.DownloadTaskId))
            .Select(r => new {r.Id, r.DownloadTaskId}).ToListAsync(ct);
        var persistentSites = await db.DownloadTasks.AsNoTracking().Where(t => allSites.Contains(t.Id)).Select(t => new {t.Id, t.Status}).ToListAsync(ct);
        var liveSites = await sites.GetDtos(allSites);
        var output = new Dictionary<int, AcquisitionDeletionActivity>();
        foreach (var task in tasks)
        {
            var ownedRuns = runsByTask[task.Id]; var keys = ownedRuns.Select(id => id.ToString()).ToHashSet();
            var boundOperations = bindings.Where(b => keys.Contains(b.OwnerId)).Select(b => b.OperationId)
                .Concat(consumers.Where(c => c.OwnerId != null && keys.Contains(c.OwnerId)).Select(c => c.OperationId)).ToHashSet();
            var ownOperations = operations.Where(o => boundOperations.Contains(o.Id) ||
                WorkflowLeaseOwnerRun(o.LeaseOwner) is {} run && ownedRuns.Contains(run)).ToArray();
            var intents = ownOperations.Select(o => o.IntentKey).ToHashSet();
            var explicitIds = transportOutputs.Where(o => ownedRuns.Contains(o.WorkflowRunId)).Select(o => o.Id).ToHashSet();
            bool Origin(string kind, string? id) => kind == "workflow" && id != null && (keys.Contains(id) || intents.Contains(id)) ||
                kind == "acquisition" && id == task.Id.ToString();
            var ownRegistry = registry.Where(r => r.WorkflowRunId is {} run && ownedRuns.Contains(run) || Origin(r.OriginKind, r.OriginId)).ToArray();
            var siteIds = owners.Where(o => o.AcquisitionTaskId == task.Id || ownedRuns.Contains(o.WorkflowRunId)).Select(o => o.DownloadTaskId)
                .Concat(clouds.Where(o => ownedRuns.Contains(o.WorkflowRunId)).Select(o => o.DownloadTaskId!.Value))
                .Concat(ownRegistry.Select(r => r.SiteTaskId ?? SiteParent(r.ParentId))).Where(id => id > 0).ToHashSet();
            var resultIds = results.Where(r => siteIds.Contains(r.DownloadTaskId)).Select(r => r.Id).ToHashSet();
            bool Linked(int? run, int? site, int? result, string kind, string? source) =>
                run.HasValue && ownedRuns.Contains(run.Value) || site.HasValue && siteIds.Contains(site.Value) ||
                result.HasValue && resultIds.Contains(result.Value) || Origin(kind, source) ||
                kind == "download-result" && int.TryParse(source, out var resultId) && resultIds.Contains(resultId);
            var linkedNative = native.Where(n => explicitIds.Contains(n.Id) || Linked(n.RunId, n.SiteId, n.ResultId, n.Kind, n.SourceId)).ToArray();
            var linkedRegistry = registry.Where(r => (r.Category is "bt" or "http" || r.Id.StartsWith("bt:") || r.Id.StartsWith("http:")) &&
                (explicitIds.Contains(r.Id) || Linked(r.WorkflowRunId, r.SiteTaskId ?? (SiteParent(r.ParentId) is > 0 and var parent ? parent : null), r.DownloadResultId, r.OriginKind, r.OriginId))).ToArray();
            string? blocked = !ValidHistory(task.PlanHistoryJson) || ownedRuns.Any(id => !runs.Any(r => r.Id == id)) ? "ACQUISITION_EXECUTION_STATE_UNKNOWN" :
                ownedRuns.Any(id => bTasks?.IsPending("workflow.run." + id) == true) ||
                runs.Any(r => ownedRuns.Contains(r.Id) && (r.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running ||
                    r.Status == WorkflowRunStatus.Waiting && r.PendingSignalJson != null))
                    ? "ACQUISITION_TASK_ACTIVE" :
                persistentSites.Any(s => siteIds.Contains(s.Id) && s.Status == DownloadTaskDbModelStatus.InProgress) ||
                liveSites.Any(s => siteIds.Contains(s.Id) && (int)s.Status is >= 200 and <= 500) || linkedNative.Any(n => n.Status is 1 or 2 or 3)
                    ? "ACQUISITION_DOWNLOAD_ACTIVE" :
                ownOperations.Any(o => o.LeaseUntil > DateTime.UtcNow && o.State == ArtifactOperationState.Running)
                    ? "ACQUISITION_DOWNLOAD_ACTIVE" :
                linkedRegistry.Any(r => !native.Any(n => n.Id == r.Id)) || explicitIds.Any(id => !native.Any(n => n.Id == id)) ||
                // File processing/publication has no independent download transport. An expired
                // claim can remain after a suspended or abandoned attempt; the workflow/BTask
                // and live-lease checks above already guard its actual execution. Keep its
                // history intact instead of requiring a nonexistent native download snapshot.
                // Unknown and transport-backed operations must still fail closed.
                ownOperations.Any(o => o.State == ArtifactOperationState.Running &&
                    !(o.Kind is "files.process" or "files.publish" && o.LeaseUntil != null && o.LeaseUntil <= DateTime.UtcNow) &&
                    !linkedNative.Any(n => Origin(n.Kind, n.SourceId)))
                    ? "ACQUISITION_DOWNLOAD_STATE_UNKNOWN" : null;
            var observed = runs.Where(r => ownedRuns.Contains(r.Id)).OrderBy(r => r.Id).ToArray();
            AcquisitionDeletionWriters? writers = null;
            if (bTasks != null && blocked is ("ACQUISITION_TASK_ACTIVE" or "ACQUISITION_DOWNLOAD_ACTIVE"))
            {
                var writingRuns = observed.Where(r => bTasks.IsPending("workflow.run." + r.Id) ||
                    r.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running ||
                    r.Status == WorkflowRunStatus.Waiting && r.PendingSignalJson != null).Select(r => r.Id).ToArray();
                var writingSites = persistentSites.Where(s => siteIds.Contains(s.Id) && s.Status == DownloadTaskDbModelStatus.InProgress).Select(s => s.Id)
                    .Concat(liveSites.Where(s => siteIds.Contains(s.Id) && (int)s.Status is >= 200 and <= 500).Select(s => s.Id)).Distinct().ToArray();
                var writingNative = linkedNative.Where(n => n.Status is 1 or 2 or 3).ToArray();
                // A workflow may observe an independently owned download. Only stop transports
                // explicitly owned by this acquisition, its workflow, or its owned site download.
                bool OwnsSite(int id) => owners.Any(o => o.DownloadTaskId == id && o.AcquisitionTaskId == task.Id) ||
                    clouds.Any(o => o.DownloadTaskId == id && ownedRuns.Contains(o.WorkflowRunId));
                if (writingSites.All(OwnsSite) &&
                    writingNative.All(n => (n.RunId == null || ownedRuns.Contains(n.RunId.Value)) &&
                        (n.Kind != "acquisition" || Origin(n.Kind, n.SourceId)) &&
                        (Origin(n.Kind, n.SourceId) || n.RunId != null || n.SiteId is {} site &&
                            OwnsSite(site))) &&
                    (writingRuns.Length + writingSites.Length + writingNative.Length > 0) &&
                    await ExclusivelyOwnedAsync(task, ownedRuns, writingSites, ownOperations.Where(o => o.State == ArtifactOperationState.Running ||
                        writingNative.Any(n => n.Kind == "workflow" && n.SourceId == o.IntentKey)).Select(o => o.Id).ToArray(), ct))
                    writers = new(writingRuns, writingSites, writingNative.Select(n => n.Id).ToArray());
            }
            output[task.Id] = new(ownedRuns, JsonSerializer.Serialize(observed), blocked) { ObservedRuns = observed, Writers = writers };
        }
        return output;
    }
    private static int SiteParent(string? value) => value?.StartsWith("site:", StringComparison.Ordinal) == true &&
        int.TryParse(value[5..], out var id) ? id : 0;
    internal static int? WorkflowLeaseOwnerRun(string? value)
    {
        if (value?.StartsWith("workflow:", StringComparison.Ordinal) != true) return null;
        var end = value.IndexOf(':', 9);
        if (end <= 9 || end == value.Length - 1 || value.Any(char.IsControl)) return null;
        var key = value[9..end];
        return int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                   out var id) && id > 0 && key == id.ToString(System.Globalization.CultureInfo.InvariantCulture) ? id : null;
    }
    internal static bool ValidHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try { return JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(json, JsonSerializerOptions.Web) != null; }
        catch (JsonException) { return false; }
    }
    private static IEnumerable<string> ReadOutputIds(string? json)
    {
        try { return (JsonSerializer.Deserialize<List<WorkflowOutputBlock>>(json ?? "[]", JsonSerializerOptions.Web) ?? [])
            .Select(o => o.TaskId).Where(id => id != null && (id.StartsWith("bt:") || id.StartsWith("http:"))).Select(id => id!).ToArray(); }
        catch (JsonException) { return []; }
    }
    internal static IEnumerable<int> HistoryRuns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return (JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(json, JsonSerializerOptions.Web) ?? [])
            .Where(r => r.PreviousRunId.HasValue).Select(r => r.PreviousRunId!.Value).ToArray(); }
        catch (JsonException) { return []; }
    }
    private sealed record Native(string Id, int Status, int? RunId, int? SiteId, int? ResultId, string Kind, string? SourceId);
}

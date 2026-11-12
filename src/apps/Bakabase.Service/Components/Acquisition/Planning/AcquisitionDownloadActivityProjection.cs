using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionDownloadActivity(string Activity, IReadOnlySet<string> RecoveredPartIds);

/// <summary>Read-only activity of current-run downloads. JSON extraction stays in SQLite: global facets
/// never materialize checkpoint file lists, frozen plans, downloader options or historical outputs.</summary>
public sealed class AcquisitionDownloadActivityProjection(BakabaseDbContext db, Func<IAcquisitionRuntimeDownloadReader> readDownloads)
{
    public AcquisitionDownloadActivityProjection(BakabaseDbContext db, IAcquisitionRuntimeDownloadReader downloads)
        : this(db, () => downloads) { }
    public async Task<IReadOnlyDictionary<int, AcquisitionDownloadActivity>> ReadAsync(
        IReadOnlyCollection<int> taskIds, CancellationToken ct = default,
        IReadOnlyList<AcquisitionRuntimeDownload>? existingSnapshots = null)
    {
        var result = new Dictionary<int, AcquisitionDownloadActivity>();
        foreach (var batch in taskIds.Distinct().Chunk(500))
        {
            var facts = await db.Database.SqlQueryRaw<ActivityFact>(FactsSql, JsonSerializer.Serialize(batch),
                (int)WorkflowRunStatus.Running, (int)WorkflowRunStatus.Waiting).ToListAsync(ct);
            var eligible = facts.Where(f => IsDownloadNode(f.NodeKind) &&
                (f.RunStatus != (int)WorkflowRunStatus.Waiting || f.WaitReason is null or "cloud.cloudAttention"))
                .ToArray();
            var runIds = eligible.Select(f => f.RunId).Distinct().ToArray();
            if (runIds.Length == 0) continue;
            var operations = await db.CloudDownloadOperations.AsNoTracking()
                .Where(o => runIds.Contains(o.WorkflowRunId) && o.Kind == "download" && o.DownloadTaskId != null)
                .OrderByDescending(o => o.UpdatedAt).Select(o => new {o.WorkflowRunId, o.TargetId, o.DownloadTaskId}).ToListAsync(ct);
            var owners = await db.DownloadResultOwners.AsNoTracking().Where(o => runIds.Contains(o.WorkflowRunId))
                .Select(o => new {o.AcquisitionTaskId, o.WorkflowRunId, o.DownloadTaskId}).ToListAsync(ct);
            var managed = await db.ManagedDownloadTasks.AsNoTracking().Where(m => runIds.Contains(m.WorkflowRunId ?? 0))
                .Select(m => new {m.Id, m.ParentId, m.WorkflowRunId, m.UserStopped}).ToListAsync(ct);
            var bindings = new Dictionary<int, Dictionary<string, string?>>();
            foreach (var group in eligible.GroupBy(f => f.TaskId))
            {
                var first = group.First();
                var bound = new Dictionary<string, string?>(StringComparer.Ordinal);
                var hasTargets = group.Any(f => f.TargetId != null);
                foreach (var target in group.Where(f => f.TargetId != null && !f.Excluded))
                {
                    var id = target.TransportId ?? (target.SiteId is {} site ? "site:" + site : null) ??
                        operations.Where(o => o.WorkflowRunId == first.RunId && o.TargetId == target.TargetId)
                            .Select(o => "site:" + o.DownloadTaskId).FirstOrDefault();
                    if (id != null) bound[id] = target.TargetId;
                }
                // Legacy acquisition downloads have durable ownership but no cloud workset.
                if (!hasTargets)
                {
                    foreach (var owner in owners.Where(o => o.AcquisitionTaskId == first.TaskId && o.WorkflowRunId == first.RunId))
                        bound["site:" + owner.DownloadTaskId] = null;
                    foreach (var item in managed.Where(m => m.WorkflowRunId == first.RunId && !m.UserStopped))
                        bound[item.Id] = null;
                }
                foreach (var stopped in managed.Where(m => m.UserStopped)) bound.Remove(stopped.Id);
                bindings[first.TaskId] = bound;
            }
            var ids = bindings.Values.SelectMany(b => b.Keys).Distinct().ToArray();
            if (ids.Length == 0) continue;
            var siteIds = ids.Where(id => id.StartsWith("site:", StringComparison.Ordinal))
                .Select(id => int.TryParse(id[5..], out var number) ? number : 0).Where(id => id > 0).ToArray();
            var snapshots = existingSnapshots ?? await readDownloads().ReadAsync(siteIds,
                ids.Where(id => !id.StartsWith("site:", StringComparison.Ordinal)).ToArray(), [], ct);
            var stoppedIds = managed.Where(m => m.UserStopped).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var group in eligible.GroupBy(f => f.TaskId))
            {
                var first = group.First(); var bound = bindings[first.TaskId];
                var matching = snapshots.Where(d => !stoppedIds.Contains(d.Id) &&
                    (d.WorkflowRunId == null || d.WorkflowRunId == first.RunId) &&
                    (bound.ContainsKey(d.Id) || d.ParentId != null && bound.ContainsKey(d.ParentId))).ToArray();
                var parents = matching.Where(d => d.ParentId != null).Select(d => d.ParentId!).ToHashSet();
                var leaves = matching.Where(d => !parents.Contains(d.Id)).ToArray();
                var activity = Activity(leaves);
                if (activity == null) continue;
                var recovered = new HashSet<string>(StringComparer.Ordinal);
                foreach (var part in bound.Where(b => b.Value != null))
                {
                    var children = leaves.Where(d => d.Id == part.Key || d.ParentId == part.Key).ToArray();
                    if (Activity(children) != null && children.All(d => d.Status is "running" or "queued" or "completed" && d.ProblemCode == null) &&
                        children.All(d => first.WaitAt == null || d.ObservedAt >= AcquisitionRuntimeDownloadReader.Local(first.WaitAt.Value)))
                        recovered.Add(part.Value!);
                }
                result[first.TaskId] = new(activity, recovered);
            }
        }
        return result;
    }

    internal static string? Activity(IEnumerable<AcquisitionRuntimeDownload> values) =>
        values.Any(d => d.Status == "running") ? "running" : values.Any(d => d.Status == "queued") ? "queued" : null;
    internal static bool IsDownloadNode(string? kind) => kind is "download.awaitCompletion" or "download.enqueueSupported" or
        "cloud.download" or "acquisition.fetchSupported";

    public sealed class ActivityFact
    {
        public int TaskId { get; set; }
        public int RunId { get; set; }
        public int RunStatus { get; set; }
        public string? NodeKind { get; set; }
        public string? WaitReason { get; set; }
        public DateTime? WaitAt { get; set; }
        public string? TargetId { get; set; }
        public bool Excluded { get; set; }
        public int? SiteId { get; set; }
        public string? TransportId { get; set; }
    }

    // Filter owners before touching JSON. Malformed or unknown envelopes cannot invent a binding.
    private const string FactsSql = """
        WITH candidate AS (
            SELECT t.Id TaskId, r.Id RunId, r.Status RunStatus, r.WaitReason, r.WaitingSince WaitAt,
                r.CurrentStepIndex, r.WorkflowDefinitionId,
                CASE WHEN json_valid(r.PlanSnapshotJson) THEN r.PlanSnapshotJson ELSE '{{}}' END Plan,
                CASE WHEN json_valid(r.CurrentItemJson) THEN r.CurrentItemJson ELSE '{{}}' END Item,
                CASE WHEN json_valid(r.WaitPromptJson) THEN r.WaitPromptJson ELSE '{{}}' END Prompt
            FROM AcquisitionTasks t JOIN WorkflowRuns r ON r.Id = t.WorkflowRunId
            WHERE t.Id IN (SELECT value FROM json_each({0})) AND t.DeletedAt IS NULL
                AND r.Status IN ({1}, {2})
        ), envelope AS (
            SELECT *, CASE WHEN json_type(Item, '$.item') = 'object' THEN json_extract(Item, '$.item') ELSE Item END Body
            FROM candidate
        ), workset AS (
            SELECT *, CASE
                WHEN json_valid(json_extract(Body, '$.variables."download.workset"')) THEN json_extract(Body, '$.variables."download.workset"')
                WHEN json_type(Body, '$.targets') = 'array' THEN Body ELSE Prompt END Targets
            FROM envelope
        )
        SELECT w.TaskId, w.RunId, w.RunStatus, w.WaitReason, w.WaitAt,
            COALESCE(json_extract(w.Plan, '$.nodes[' || w.CurrentStepIndex || '].kind'),
                (SELECT n.Kind FROM WorkflowNodeExecutions n WHERE n.WorkflowRunId = w.RunId AND n.StepIndex = w.CurrentStepIndex
                    ORDER BY n.Attempt DESC, n.Id DESC LIMIT 1),
                (SELECT a.Kind FROM WorkflowActivities a WHERE a.WorkflowDefinitionId = w.WorkflowDefinitionId
                    AND (SELECT COUNT(*) FROM WorkflowActivities earlier WHERE earlier.WorkflowDefinitionId = a.WorkflowDefinitionId
                        AND earlier."Order" < a."Order") = w.CurrentStepIndex LIMIT 1)) NodeKind,
            json_extract(target.value, '$.id') TargetId,
            COALESCE(json_extract(target.value, '$.excluded'), 0) Excluded,
            json_extract(target.value, '$.downloadTaskId') SiteId,
            json_extract(target.value, '$.transportTaskId') TransportId
        FROM workset w LEFT JOIN json_each(CASE WHEN json_type(w.Targets, '$.targets') = 'array'
            THEN json_extract(w.Targets, '$.targets') ELSE '[]' END) target ON target.type = 'object'
        """;
}

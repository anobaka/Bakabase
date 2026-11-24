using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>History remains readable; deleted owners and superseded continuations cannot be restarted.</summary>
public sealed class AcquisitionWorkflowRunMutationGuard(BakabaseDbContext db) : IWorkflowRunMutationGuard
{
    public void ValidateAnswer(Bakabase.Modules.Workflow.Abstractions.Models.Domain.WorkflowActivity? activity,
        string? waitReason, string? prompt, string signalJson)
    {
        // Other workflows own their signal formats. Inspect only the two group-scoped actions.
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(signalJson); }
        catch (JsonException) { return; }
        using var json = parsed;
        if (json.RootElement.ValueKind != JsonValueKind.Object) return;
        var action = json.RootElement.EnumerateObject().LastOrDefault(p => p.Name.Equals("action", StringComparison.OrdinalIgnoreCase)).Value;
        if (action.ValueKind != JsonValueKind.String || action.GetString() is not
            (FileProcessing.FileProcessingResumeActions.InspectGroup or FileProcessing.FileProcessingResumeActions.RetainGroup)) return;
        if (activity?.Kind != AcquisitionPlanProcessingActivity.ActivityKind || waitReason != "cloud.fileProcessing")
            throw new InvalidOperationException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
        try
        {
            var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(activity.ConfigJson, JsonSerializerOptions.Web)!;
            var signal = json.RootElement.Deserialize<CloudDownloads.CloudFileProcessingSignal>(JsonSerializerOptions.Web)!;
            FileProcessing.FileProcessingResumeActions.ValidateGroup(prompt, config.TargetId, "MissingVolumes", signal, config.StopAfterStepId);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        { throw new InvalidOperationException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED", error); }
    }

    public async Task EnsureCanMutateAsync(int runId, CancellationToken ct = default)
    {
        if ((await ReadMutationRefusalsAsync([runId], ct)).TryGetValue(runId, out var reason))
            throw new InvalidOperationException(reason);
    }

    public async Task<IReadOnlyDictionary<int, string>> ReadMutationRefusalsAsync(IReadOnlyCollection<int> runIds,
        CancellationToken ct = default)
    {
        const string deleted = "ACQUISITION_TASK_DELETED: The acquisition task was deleted; its execution history is retained.";
        const string sourceChanged = "ACQUISITION_PLAN_SUPERSEDED: A newer plan selected another source. Continue the acquisition task; this historical run remains read-only.";
        const string continued = "ACQUISITION_PLAN_SUPERSEDED: A newer plan continues from this run's verified files. Continue the acquisition task; this historical run remains read-only.";
        var refused = new Dictionary<int, string>();
        if (runIds.Count == 0) return refused;

        var scope = runIds.ToHashSet();
        var sourceOwners = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking()
            .Where(s => scope.Contains(s.WorkflowRunId) && s.Module == "acquisition" && s.EntityKind == "task")
            .Select(s => new {s.WorkflowRunId, s.EntityId}).ToArrayAsync(ct);
        var resultOwners = await db.DownloadResultOwners.AsNoTracking().Where(o => scope.Contains(o.WorkflowRunId))
            .Select(o => new {o.WorkflowRunId, o.AcquisitionTaskId}).ToArrayAsync(ct);
        var owners = sourceOwners.Select(s => (s.WorkflowRunId, Id: int.TryParse(s.EntityId, out var id) ? id : 0))
            .Where(s => s.Id > 0).Concat(resultOwners.Select(o => (o.WorkflowRunId, Id: o.AcquisitionTaskId)))
            .ToLookup(o => o.Id, o => o.WorkflowRunId);
        var ownerIds = owners.Select(o => o.Key).ToArray();
        var acquisition = db.AcquisitionTasks.AsNoTracking();
        var deletedOwners = await acquisition.Where(t => t.DeletedAt != null &&
                (t.WorkflowRunId.HasValue && scope.Contains(t.WorkflowRunId.Value) || ownerIds.Contains(t.Id)))
            .Select(t => new {t.Id, t.WorkflowRunId}).ToArrayAsync(ct);
        foreach (var task in deletedOwners)
        {
            if (task.WorkflowRunId is { } current && scope.Contains(current)) refused.TryAdd(current, deleted);
            foreach (var run in owners[task.Id]) refused.TryAdd(run, deleted);
        }

        // Legacy plans have no indexed previous-run link. Select only candidate JSON in
        // SQLite so polling does not hydrate every discarded acquisition plan.
        if (refused.Count == scope.Count) return refused;
        var historyScope = JsonSerializer.Serialize(scope.Where(run => !refused.ContainsKey(run)));
        var historyCandidates = await db.Database.SqlQuery<HistoryCandidate>($$"""
            SELECT a.WorkflowRunId, a.PlanHistoryJson, a.DeletedAt IS NOT NULL AS IsDeleted
            FROM AcquisitionTasks a
            WHERE (a.DeletedAt IS NOT NULL OR instr(a.PlanHistoryJson, {{AcquisitionSourceRevisionService.RevisionReason}}) > 0)
              AND EXISTS (
                SELECT 1
                FROM json_each(CASE WHEN json_valid(a.PlanHistoryJson) THEN a.PlanHistoryJson ELSE '[]' END) r
                JOIN json_each(CASE WHEN r.type = 'object' THEN r.value ELSE '{}' END) p
                WHERE lower(p.key) = 'previousrunid' AND p.type IN ('integer', 'text')
                  AND CAST(p.value AS INTEGER) IN (SELECT value FROM json_each({{historyScope}}))
              )
            """).ToArrayAsync(ct);
        foreach (var prior in historyCandidates.Where(t => t.IsDeleted)
                     .SelectMany(t => AcquisitionDeletionActivityReader.HistoryRuns(t.PlanHistoryJson)))
            if (scope.Contains(prior)) refused.TryAdd(prior, deleted);
        if (refused.Count == scope.Count) return refused;

        foreach (var task in historyCandidates.Where(t => t.PlanHistoryJson.Contains(AcquisitionSourceRevisionService.RevisionReason)))
            foreach (var revision in ReadHistory(task.PlanHistoryJson))
                if (revision.PreviousRunId is { } prior && prior != task.WorkflowRunId && scope.Contains(prior) &&
                    revision.Reason == AcquisitionSourceRevisionService.RevisionReason && revision.SourceRevision != null)
                    refused.TryAdd(prior, sourceChanged);
        if (refused.Count == scope.Count) return refused;

        // Later local revisions can rebind their verified input to a newer run. The original
        // run must not become runnable again merely because it is no longer the direct parent.
        // Restrict this history lookup to the run's actual acquisition owner.
        static bool ContinuesFrom(AcquisitionPlan? plan, int ownerId, int previousRunId) =>
            plan?.Parts.Any(p => p.ContinueFrom is { } input && input.TaskId == ownerId &&
                input.PreviousRunId == previousRunId) == true;
        var ownerGenerations = await acquisition.Where(t => ownerIds.Contains(t.Id))
            .Select(t => new {t.Id, t.WorkflowRunId}).ToArrayAsync(ct);
        var historicalOwnerIds = ownerGenerations
            .Where(t => owners[t.Id].Any(run => run != t.WorkflowRunId && !refused.ContainsKey(run)))
            .Select(t => t.Id).ToArray();
        if (historicalOwnerIds.Length == 0) return refused;
        var plans = await acquisition.Where(t => historicalOwnerIds.Contains(t.Id) && (t.PlanJson != null || t.PlanHistoryJson != null))
            .Select(t => new {t.Id, t.WorkflowRunId, t.PlanJson, t.PlanHistoryJson}).ToArrayAsync(ct);
        foreach (var task in plans)
        {
            var older = owners[task.Id].Where(run => run != task.WorkflowRunId && !refused.ContainsKey(run)).Distinct().ToArray();
            if (older.Length == 0) continue;
            var plan = string.IsNullOrWhiteSpace(task.PlanJson) ? null :
                JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson, JsonSerializerOptions.Web);
            var history = ReadHistory(task.PlanHistoryJson);
            foreach (var prior in older)
                if (ContinuesFrom(plan, task.Id, prior) || history.Any(revision => ContinuesFrom(revision.Plan, task.Id, prior)))
                    refused.TryAdd(prior, continued);
        }
        return refused;
    }

    private static IReadOnlyList<AcquisitionPlanRevision> ReadHistory(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] :
            JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(json, JsonSerializerOptions.Web) ?? [];

    private sealed record HistoryCandidate(int? WorkflowRunId, string PlanHistoryJson, bool IsDeleted);
}

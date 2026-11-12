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
    public async Task EnsureCanMutateAsync(int runId, CancellationToken ct = default)
    {
        var ownerIds = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == runId &&
            s.Module == "acquisition" && s.EntityKind == "task").Select(s => s.EntityId).ToArrayAsync(ct);
        var ids = ownerIds.Select(id => int.TryParse(id, out var value) ? value : 0).Where(id => id > 0).ToArray();
        var deletedHistory = await db.AcquisitionTasks.AsNoTracking().Where(t => t.DeletedAt != null && t.PlanHistoryJson != null)
            .Select(t => t.PlanHistoryJson).ToArrayAsync(ct);
        var ownerTasks = await db.DownloadResultOwners.AsNoTracking().Where(o => o.WorkflowRunId == runId).Select(o => o.AcquisitionTaskId).ToArrayAsync(ct);
        if (deletedHistory.Any(json => AcquisitionDeletionActivityReader.HistoryRuns(json).Contains(runId)) ||
            await db.AcquisitionTasks.AsNoTracking().AnyAsync(t => t.DeletedAt != null &&
            (t.WorkflowRunId == runId || ids.Contains(t.Id) || ownerTasks.Contains(t.Id)), ct))
            throw new InvalidOperationException("ACQUISITION_TASK_DELETED: The acquisition task was deleted; its execution history is retained.");
        var plans = await db.AcquisitionTasks.AsNoTracking().Where(t => (ids.Contains(t.Id) || ownerTasks.Contains(t.Id)) &&
                t.WorkflowRunId != runId && (t.PlanJson != null || t.PlanHistoryJson != null))
            .Select(t => new {t.Id, t.PlanJson, t.PlanHistoryJson}).ToArrayAsync(ct);
        var revisedHistory = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId != runId && t.PlanHistoryJson != null &&
                t.PlanHistoryJson.Contains(AcquisitionSourceRevisionService.RevisionReason))
            .Select(t => t.PlanHistoryJson).ToArrayAsync(ct);
        if (revisedHistory.Any(json => JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(json!, JsonSerializerOptions.Web)?
                .Any(r => r.PreviousRunId == runId && r.Reason == AcquisitionSourceRevisionService.RevisionReason && r.SourceRevision != null) == true))
            throw new InvalidOperationException("ACQUISITION_PLAN_SUPERSEDED: A newer plan selected another source. Continue the acquisition task; this historical run remains read-only.");
        // Later local revisions can rebind their verified input to a newer run. The original
        // run must not become runnable again merely because it is no longer the direct parent.
        // Restrict this history lookup to the run's actual acquisition owner.
        static bool ContinuesFrom(AcquisitionPlan? plan, int ownerId, int previousRunId) =>
            plan?.Parts.Any(p => p.ContinueFrom is { } input && input.TaskId == ownerId &&
                input.PreviousRunId == previousRunId) == true;
        if (plans.Any(owner => !string.IsNullOrWhiteSpace(owner.PlanJson) &&
                ContinuesFrom(JsonSerializer.Deserialize<AcquisitionPlan>(owner.PlanJson, JsonSerializerOptions.Web), owner.Id, runId) ||
                !string.IsNullOrWhiteSpace(owner.PlanHistoryJson) &&
                JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(owner.PlanHistoryJson, JsonSerializerOptions.Web)?
                    .Any(revision => ContinuesFrom(revision.Plan, owner.Id, runId)) == true))
            throw new InvalidOperationException("ACQUISITION_PLAN_SUPERSEDED: A newer plan continues from this run's verified files. Continue the acquisition task; this historical run remains read-only.");
    }
}

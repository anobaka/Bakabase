using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A confirmed continuation changes only this run's reserve, never its source or file identity.</summary>
internal static class AcquisitionContinuationReserve
{
    internal static bool IsDownload(WorkflowRunDbModel? run) => Kind(run) is
        "download.selectSources" or "baidu.transfer" or "baidu.download" or "download.enqueueSupported" or "download.awaitCompletion";

    internal static bool Supports(WorkflowRunDbModel? run) => IsDownload(run) || AcquisitionRecoveryService.IsLocalFileStep(Kind(run));

    internal static string? Kind(WorkflowRunDbModel? run)
    {
        if (run?.CurrentStepIndex is not >= 0) return null;
        try {return WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex.Value)?.Kind;}
        catch (InvalidDataException) {return null;}
    }

    // Caller holds task -> run gates and has validated the frozen continuation. Omitted
    // options inherit the global default. Internal handoffs can retain a grant already confirmed upstream.
    internal static async Task<AcquisitionTaskDbModel> SaveWhileLockedAsync(BakabaseDbContext db,
        AcquisitionTaskDbModel task, WorkflowRunDbModel run, bool? useEmergency, string token, CancellationToken ct,
        bool preserveExisting = false, bool pausedOriginal = false)
    {
        if (!Supports(run) || useEmergency == null && (preserveExisting || AcquisitionStorageOverrideGrant.Read(task) == null)) return task;
        if (task.DeletedAt != null || task.PlanRevision < 1 || task.WorkflowRunId != run.Id ||
            task.Status == AcquisitionStatus.Completed || run.Status == WorkflowRunStatus.Success ||
            (task.Status == AcquisitionStatus.Cancelled || run.Status == WorkflowRunStatus.Cancelled) &&
            !(pausedOriginal && AcquisitionDownloadContinuationService.IsPausedDownloadOwner(task, run)))
            throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
        var now = DateTimeOffset.UtcNow;
        var grant = new AcquisitionStorageOverrideGrant(run.Id, task.PlanRevision, token, now, now.AddHours(24),
            ReserveMode: useEmergency == null ? AcquisitionSpaceReserve.Inherit : useEmergency.Value ? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Configured);
        var json = AcquisitionStorageOverrideGrant.WithState(task.BuilderStateJson, grant);
        if (await db.AcquisitionTasks.Where(t => t.Id == task.Id && t.DeletedAt == null && t.WorkflowRunId == run.Id &&
                t.PlanRevision == task.PlanRevision && t.BuilderStateJson == task.BuilderStateJson)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json), ct) != 1)
            throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
        AcquisitionStorageWritePolicyProvider.Invalidate();
        return task with {BuilderStateJson = json};
    }
}

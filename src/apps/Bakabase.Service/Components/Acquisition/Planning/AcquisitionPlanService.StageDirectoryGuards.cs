using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private async Task<int[]> StageDirectoryOwnersAsync(AcquisitionTaskDbModel row, CancellationToken ct)
    {
        var id = row.Id.ToString(CultureInfo.InvariantCulture);
        var owners = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking()
            .Where(s => s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == id)
            .Select(s => s.WorkflowRunId).Distinct().ToArrayAsync(ct);
        if (row.WorkflowRunId is {} current && !owners.Contains(current))
            throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN");
        if (await db.Set<WorkflowRunSourceDbModel>().AnyAsync(s => owners.Contains(s.WorkflowRunId) &&
            s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId != id, ct))
            throw new InvalidOperationException("STAGE_DIRECTORY_SHARED");
        return owners;
    }

    private async Task<string?> StageDirectoryActivityIssueAsync(AcquisitionTaskDbModel row, CancellationToken ct)
    {
        if (row.Status == AcquisitionStatus.Cancelled) return "STAGE_DIRECTORY_TASK_UNAVAILABLE";
        if (State(row).ProcessingStageCommitId != null || State(row).ProcessingStageInputId != null) return "STAGE_DIRECTORY_TASK_ACTIVE";
        if (AcquisitionLocalRecoveryQueueEntry.Read(row) is {IsOpen: true} ||
            AcquisitionContinuationEntry.Read(row) is {IsOpen: true}) return "STAGE_DIRECTORY_TASK_ACTIVE";
        var owners = await StageDirectoryOwnersAsync(row, ct);
        if (await db.WorkflowRuns.AnyAsync(r => owners.Contains(r.Id) &&
            (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.PendingSignalJson != null), ct))
            return "STAGE_DIRECTORY_TASK_ACTIVE";
        var ownerKeys = owners.Select(r => r.ToString(CultureInfo.InvariantCulture)).ToArray();
        var operationIds = db.ArtifactOperationBindings.Where(b => b.OwnerKind == "workflow" && ownerKeys.Contains(b.OwnerId))
            .Select(b => b.OperationId);
        if (await db.ArtifactOperations.AnyAsync(o => operationIds.Contains(o.Id) &&
            (o.State == ArtifactOperationState.Running || o.LeaseUntil > DateTime.UtcNow), ct))
            return "STAGE_DIRECTORY_TASK_ACTIVE";
        var ids = await db.CloudDownloadOperations.AsNoTracking().Where(o => owners.Contains(o.WorkflowRunId) && o.DownloadTaskId != null)
            .Select(o => o.DownloadTaskId!.Value).ToArrayAsync(ct);
        var resultIds = await db.DownloadResultOwners.AsNoTracking().Where(o => o.AcquisitionTaskId == row.Id)
            .Select(o => o.DownloadTaskId).ToArrayAsync(ct);
        ids = ids.Concat(resultIds).Distinct().ToArray();
        if (await db.DownloadTasks.AnyAsync(d => ids.Contains(d.Id) && d.Status == DownloadTaskDbModelStatus.InProgress, ct))
            return "STAGE_DIRECTORY_TASK_ACTIVE";
        if (downloadActivity != null && (await downloadActivity.ReadAsync([row.Id], ct)).ContainsKey(row.Id))
            return "STAGE_DIRECTORY_TASK_ACTIVE";
        return null;
    }
    private async Task RequireStageDirectoryInactiveAsync(AcquisitionTaskDbModel row, CancellationToken ct)
    {
        if (await StageDirectoryActivityIssueAsync(row, ct) is {} issue) throw new InvalidOperationException(issue);
    }

    private async Task RequireDownloadDirectoryMovableAsync(AcquisitionTaskDbModel row, int[] owners, string partId, CancellationToken ct)
    {
        var ids = await db.CloudDownloadOperations.AsNoTracking().Where(o => owners.Contains(o.WorkflowRunId) &&
            o.TargetId == partId && o.Kind == "download" && o.DownloadTaskId != null).Select(o => o.DownloadTaskId!.Value).ToArrayAsync(ct);
        if (await db.DownloadTasks.AnyAsync(d => ids.Contains(d.Id) && d.Status != DownloadTaskDbModelStatus.Complete, ct) ||
            await db.ManagedDownloadTasks.AnyAsync(d => d.WorkflowRunId != null && owners.Contains(d.WorkflowRunId.Value) &&
                d.SiteTaskId == null && d.DownloadResultId == null, ct))
            throw new InvalidOperationException("STAGE_DIRECTORY_DOWNLOAD_INCOMPLETE");
    }

    private Task StageDirectoryReferencesAsync(AcquisitionTaskDbModel row, int[] owners,
        string partId, string directory, CancellationToken ct) => artifacts == null
        ? throw new InvalidOperationException("STAGE_DIRECTORY_UNAVAILABLE")
        : new AcquisitionStageDirectoryReferenceGuard(db, artifacts, storage, workspaces)
            .RequireAsync(row, owners, partId, directory, ct);
}

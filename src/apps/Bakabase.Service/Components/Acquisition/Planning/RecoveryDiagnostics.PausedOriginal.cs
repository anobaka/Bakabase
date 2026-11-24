using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionRecoveryDiagnostics
{
    internal const string PausedOriginalReady = "RECOVERY_PAUSED_ORIGINAL_READY";

    private async Task<AcquisitionRecoveryDiagnosis?> InspectPausedOriginalAsync(AcquisitionTaskDbModel task,
        WorkflowRunDbModel run, DownloadWorkset workset, bool verifyRemote, CancellationToken ct)
    {
        var owner = task.Id.ToString(CultureInfo.InvariantCulture);
        var revision = task.PlanRevision.ToString(CultureInfo.InvariantCulture);
        if (workset.Id != $"acquisition:{owner}:{revision}" ||
            !await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().AnyAsync(source => source.WorkflowRunId == run.Id &&
                source.Module == "acquisition" && source.EntityKind == "task" && source.EntityId == owner &&
                source.Revision == revision, ct)) return null;

        var selected = new Dictionary<string, CloudPreparedDownloadRetry>(StringComparer.Ordinal);
        CloudDownloadRecoveryCheck? remoteBlock = null;
        foreach (var group in workset.Targets.Where(t => !t.Excluded && t.DownloadTaskId != null).GroupBy(t => t.DownloadTaskId))
        {
            if (group.Count() != 1) continue;
            var target = group.Single();
            var proof = await downloads!.CheckPausedPreparedAsync(run.Id, target, workset.OutputDirectory, verifyRemote, ct);
            if (proof.PreparedRetry is {WasPaused: true} retry && (verifyRemote ? proof.CanRetry : proof.Candidate))
                selected.Add(target.Id, retry);
            else if (proof.ReasonCode == "RECOVERY_SOURCE_STILL_BLOCKED") remoteBlock ??= proof;
        }
        const string node = "download.awaitCompletion";
        if (selected.Count == 0) return remoteBlock == null ? null : new(new(task.Id, task.Title, run.Id, false, null,
            remoteBlock.ReasonCode, "inspectSource", CurrentStepKind: node, CurrentProblemCode: remoteBlock.FailureCode));
        var signal = new CloudSelectionSignal {ObserveExistingOnly = true, RevalidateExistingDownloads = true,
            ResumeTargetIds = selected.Keys.ToList(), PreparedDownloadRetries = selected};
        var reason = verifyRemote ? PausedOriginalReady : "RECOVERY_RECHECK_SOURCE";
        var nativeIds = workset.Targets.Where(t => selected.ContainsKey(t.Id)).Select(t => t.DownloadTaskId!.Value).ToArray();
        var directories = await db.DownloadTasks.AsNoTracking().Where(t => nativeIds.Contains(t.Id))
            .Select(t => t.DownloadPath).Distinct().ToArrayAsync(ct);
        return new(new(task.Id, task.Title, run.Id, verifyRemote,
            verifyRemote ? run.Status == WorkflowRunStatus.Waiting ? "resume" : "retry" : null,
            reason, CurrentStepKind: node), JsonSerializer.Serialize(signal, WorkflowJson.Options))
        {PreparedDownloadReason = PausedOriginalReady, InitialDownloadDirectories = directories};
    }
}

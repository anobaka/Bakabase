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
    internal const string PausedOriginalCheckTimedOut = "RECOVERY_PAUSED_ORIGINAL_CHECK_TIMED_OUT";

    public bool CanInspectPausedOriginal(AcquisitionTaskDbModel task, WorkflowRunDbModel? run)
    {
        try
        {
            return downloads != null && run != null && AcquisitionDownloadContinuationService.IsPausedDownloadOwner(task, run) &&
                AcquisitionContinuationReserve.Kind(run) == "download.awaitCompletion" &&
                ReadWorkset(run.CurrentItemJson) is {} workset &&
                workset.Id == FormattableString.Invariant($"acquisition:{task.Id}:{task.PlanRevision}") &&
                workset.Targets is {Count: > 0 and <= 100} targets &&
                targets.Any(target => target is {Excluded: false, DownloadTaskId: not null, DownloadArtifactId: null, SelectedSource: {} source} &&
                    downloads.SupportsPausedPreparedDownload(source));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) {return false;}
    }

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
        CloudDownloadRecoveryCheck? block = null;
        foreach (var group in workset.Targets.Where(t => !t.Excluded && t.DownloadTaskId != null).GroupBy(t => t.DownloadTaskId))
        {
            if (group.Count() != 1) continue;
            var target = group.Single();
            var proof = await downloads!.CheckPausedPreparedAsync(run.Id, target, workset.OutputDirectory, verifyRemote, ct);
            if (proof.PreparedRetry is {WasPaused: true} retry && (verifyRemote ? proof.CanRetry : proof.Candidate))
                selected.Add(target.Id, retry);
            else if (proof.ReasonCode is "RECOVERY_SOURCE_STILL_BLOCKED" or CloudDownloadRecoveryService.PausedOriginalLocalReview) block ??= proof;
        }
        const string node = "download.awaitCompletion";
        if (selected.Count == 0) return block == null ? null : new(new(task.Id, task.Title, run.Id, false, null,
            block.ReasonCode, block.ReasonCode == CloudDownloadRecoveryService.PausedOriginalLocalReview ? "inspectFiles" : "inspectSource",
            CurrentStepKind: node, CurrentProblemCode: block.FailureCode));
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

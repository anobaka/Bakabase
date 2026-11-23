using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionTransferRetryTarget(CloudDownloadTarget Target, string AccountId, string RemoteDirectory, string? NextIsolatedOperationId);

public sealed partial class AcquisitionRecoveryDiagnostics
{
    public bool IsTransferStep(string? kind) => CloudWorkflow.TransferActivity(kind, activities) != null;
    internal const string TransferRecoveryReady = "RECOVERY_TRANSFER_RECOVERY_READY";
    internal const string TransferRetryConfirmationRequired = "RECOVERY_TRANSFER_RETRY_CONFIRMATION_REQUIRED";

    public async Task<AcquisitionRecoveryDiagnosis> DiagnoseTransferRetryAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        CancellationToken ct)
    {
        AcquisitionRecoveryDiagnosis Block(string reason) => new(new(task.Id, task.Title, run?.Id, false, null, reason));
        if (task.DeletedAt != null) return Block("ACQUISITION_TASK_DELETED");
        if (transfers == null || run == null || task.Status == AcquisitionStatus.Cancelled ||
            task.WorkflowRunId != run.Id || task.PlanRevision < 1 || run.CurrentStepIndex is not > 0)
            return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");
        if (tasks?.IsPending("workflow.run." + run.Id) == true || run.PendingSignalJson != null ||
            run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Failed))
            return Block("ACQUISITION_TASK_ACTIVE");
        try
        {
            var definition = WorkflowRunPlans.Read(run.PlanSnapshotJson);
            var index = run.CurrentStepIndex.Value;
            if (definition?.TriggerKind != CloudWorkflow.Trigger || definition.Nodes.ElementAtOrDefault(index)?.Kind != "download.enqueueSupported")
                return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");
            var prior = definition.Nodes.Take(index).LastOrDefault(n => IsTransferStep(n.Kind));
            if (prior == null) return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");
            var transfer = JsonSerializer.Deserialize<CloudWorkflowConfig>(prior.ConfigJson, WorkflowJson.Options);
            var enqueue = JsonSerializer.Deserialize<CloudWorkflowConfig>(definition.Nodes[index].ConfigJson, WorkflowJson.Options);
            var workset = ReadWorkset(run.CurrentItemJson);
            var owner = task.Id.ToString(CultureInfo.InvariantCulture);
            var revision = task.PlanRevision.ToString(CultureInfo.InvariantCulture);
            if (transfer == null || string.IsNullOrWhiteSpace(transfer.RemoteDirectory) || transfer.ReusePolicy == "force" ||
                enqueue == null || enqueue.ReusePolicy == "force" || workset == null || workset.Id != $"acquisition:{owner}:{revision}" ||
                !string.IsNullOrWhiteSpace(enqueue.OutputDirectory) && enqueue.OutputDirectory != workset.OutputDirectory ||
                !await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().AnyAsync(s => s.WorkflowRunId == run.Id &&
                    s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == owner && s.Revision == revision, ct))
                return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            var targets = workset.Targets.Where(t => !t.Excluded).ToArray();
            if (targets.Length is 0 or > 100 || targets.Select(t => t.Id).Distinct().Count() != targets.Length)
                return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");
            var replays = new List<CloudTransferReplay>();
            foreach (var target in targets)
            {
                if (target.DownloadTaskId != null || target.DownloadArtifactId != null || target.Files.Count > 0)
                    return Block("RECOVERY_DOWNLOAD_HAS_OUTPUT");
                if (target.SelectedSource == null || CloudWorkflow.TransferActivity(prior.Kind, activities)?.Transfers(target.SelectedSource) != true ||
                    (!string.IsNullOrWhiteSpace(transfer.AccountId) && transfer.AccountId != target.AccountId) ||
                    (!string.IsNullOrWhiteSpace(enqueue.AccountId) && enqueue.AccountId != target.AccountId) ||
                    await transfers.InspectTransferReplayAsync(run.Id, target, workset.OutputDirectory, transfer.RemoteDirectory, ct) is not {} replay)
                    return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
                replays.Add(replay);
            }
            var signal = new CloudSelectionSignal {TransferReplay = new(transfer.RemoteDirectory,
                replays.ToDictionary(r => r.Target.Id, r => r.Request.OperationId))};
            return new(new(task.Id, task.Title, run.Id, true, run.Status == WorkflowRunStatus.Waiting ? "resume" : "retry",
                TransferRetryConfirmationRequired, CurrentStepKind: definition.Nodes[index].Kind), JsonSerializer.Serialize(signal, WorkflowJson.Options))
                {TransferReplays = replays, InitialDownloadDirectories = targets.Select(t => CloudStageDirectoryPath.Download(t) ??
                    t.DownloadDirectoryScope?.Directory ?? workset.OutputDirectory).Distinct().ToArray()};
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {return Block("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");}
    }

    private async Task<AcquisitionRecoveryDiagnosis?> InspectTransferRecoveryAsync(AcquisitionTaskDbModel task,
        WorkflowRunDbModel run, WorkflowRunPlanNode node, DownloadWorkset workset, bool verifyRemote, CancellationToken ct)
    {
        if (task.WorkflowRunId != run.Id || task.PlanRevision < 1) return null;
        var owner = task.Id.ToString(CultureInfo.InvariantCulture);
        var revision = task.PlanRevision.ToString(CultureInfo.InvariantCulture);
        if (workset.Id != $"acquisition:{owner}:{revision}" ||
            !await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().AnyAsync(s => s.WorkflowRunId == run.Id &&
                s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == owner && s.Revision == revision, ct))
            return null;
        var config = JsonSerializer.Deserialize<CloudWorkflowConfig>(node.ConfigJson, WorkflowJson.Options);
        if (config == null || string.IsNullOrWhiteSpace(config.RemoteDirectory)) return null;
        var shares = workset.Targets.Where(t => !t.Excluded && t.SelectedSource is {} source && CloudWorkflow.TransferActivity(node.Kind, activities)?.Transfers(source) == true && transfers!.CanRecoverTransfer(source) &&
            t.RemoteEntry == null && t.DownloadArtifactId == null && t.ProcessedArtifactId == null && t.PublishedArtifactId == null).ToArray();
        if (shares.Length == 0 || shares.Any(t => string.IsNullOrWhiteSpace(t.AccountId) ||
            !string.IsNullOrWhiteSpace(config.AccountId) && config.AccountId != t.AccountId ||
            t.DownloadTaskId != null || t.TransportTaskId != null || t.Files.Count != 0)) return null;

        var retries = new List<AcquisitionTransferRetryTarget>();
        var hasRecordedTransfer = false;
        foreach (var share in shares)
        {
            var check = await transfers!.InspectTransferRecoveryAsync(run.Id, share, share.AccountId!, config.RemoteDirectory,
                verifyRemote, ct);
            if (check.Disposition == CloudTransferRecoveryDisposition.Candidate)
                return new(new(task.Id, task.Title, run.Id, false, null, "RECOVERY_RECHECK_SOURCE", "inspectSource",
                    CurrentStepKind: node.Kind));
            if (check.Disposition == CloudTransferRecoveryDisposition.Unavailable)
            {
                if (check.Problem is "RECOVERY_TRANSFER_TARGET_UNVERIFIED" or "RECOVERY_TRANSFER_EVIDENCE_CHANGED")
                    return new(new(task.Id, task.Title, run.Id, false, null, check.Problem, "inspectSource",
                        CurrentStepKind: node.Kind));
                if (check.Problem is {} problem && problem != "RECOVERY_TRANSFER_REVIEW_REQUIRED" &&
                    WorkflowSourceRunProblems.Extract(problem).Contains(problem, StringComparer.Ordinal))
                    return new(new(task.Id, task.Title, run.Id, false, null, "RECOVERY_TRANSFER_REVIEW_REQUIRED",
                        "inspectSource", CurrentStepKind: node.Kind, CurrentProblemCode: problem));
                return null;
            }
            if (check.Disposition == CloudTransferRecoveryDisposition.FirstAttempt) continue;
            hasRecordedTransfer = true;
            if (check.Disposition == CloudTransferRecoveryDisposition.RequiresConfirmation)
                retries.Add(new(share, share.AccountId!, config.RemoteDirectory, check.NextIsolatedOperationId));
        }
        if (!hasRecordedTransfer) return null;

        const string reason = TransferRecoveryReady;
        var action = run.Status == WorkflowRunStatus.Waiting ? "resume" : "retry";
        var isolatedAttempts = retries.Where(target => target.NextIsolatedOperationId != null)
            .OrderBy(target => target.Target.Id, StringComparer.Ordinal)
            .ToDictionary(target => target.Target.Id, target => target.NextIsolatedOperationId, StringComparer.Ordinal);
        var signal = isolatedAttempts.Count == 0 ? AcquisitionDownloadCompletionCoordinator.Signal :
            JsonSerializer.Serialize(new {observeExistingOnly = true, isolatedTransferAttempts = isolatedAttempts});
        return new(new(task.Id, task.Title, run.Id, true, action, reason, CurrentStepKind: node.Kind), signal) {TransferRetryTargets = retries};
    }
}

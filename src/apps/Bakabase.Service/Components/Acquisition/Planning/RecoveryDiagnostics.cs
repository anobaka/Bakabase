using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRecoveryDiagnosis(AcquisitionRecoveryRow Row, string SignalJson = "{}")
{
    // A suspended attempt can have pinned writes even without a workspace policy on its old plan.
    internal string? ProcessingPartId { get; init; }
    internal IReadOnlyList<string> ProcessingStorageRoots { get; init; } = [];
    internal string? ProcessingPlacementStamp { get; init; }
    internal IReadOnlyList<string> InitialDownloadDirectories { get; init; } = [];
    internal IReadOnlyList<string> InitialDownloadTargets { get; init; } = [];
    internal string? FirstDownloadDirectory => InitialDownloadDirectories.FirstOrDefault();
    internal IReadOnlyList<string> InitialDeliveryDirectories { get; init; } = [];
    internal string? FirstDeliveryDirectory => InitialDeliveryDirectories.FirstOrDefault();
}
public interface IAcquisitionRecoveryDiagnostics
{
    Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run, CancellationToken ct,
        bool verifyFiles = true, bool verifyContent = false, bool includeStoppedDownloads = false);
}

/// <summary>Explicit, read-only recovery capabilities. Supported provider failures receive a bounded
/// original-file recheck. Never creates receipts, mutates artifacts, purchases or initializes a download engine.</summary>
public sealed partial class AcquisitionRecoveryDiagnostics(BakabaseDbContext db, IUserStoragePolicy storage,
    IArtifactOperationStore artifacts, BTaskManager? tasks = null, FileProcessingWorkspaceResolver? workspaces = null,
    CloudDownloadRecoveryService? downloads = null, AcquisitionPlanService? plans = null,
    AcquisitionTransferredDownloadRecovery? transferredDownloads = null) : IAcquisitionRecoveryDiagnostics
{
    public async Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run, CancellationToken ct,
        bool verifyFiles = true, bool verifyContent = false, bool includeStoppedDownloads = false)
    {
        string? kind = null, problem = null;
        AcquisitionRecoveryDiagnosis Result(string reason, bool ready = false, string? manual = "reviewTask", string signal = "{}") =>
            new(new(task.Id, task.Title, run?.Id, ready, ready ? run?.Status == WorkflowRunStatus.Waiting ? "resume" : "retry" : null,
                reason, ready ? null : manual, run == null ? null : new DateTimeOffset((run.CompletedAt ?? run.WaitingSince ?? run.StartedAt).ToUniversalTime()),
                kind, problem), signal);
        if (task.DeletedAt != null) return Result("ACQUISITION_TASK_DELETED");
        if (task.Status == AcquisitionStatus.Cancelled) return Result("RECOVERY_TASK_CANCELLED");
        if (run == null)
        {
            if (plans == null || task.PlanJson == null) return Result("RECOVERY_NO_RUN");
            if (!verifyFiles) return Result("RECOVERY_RECHECK_SOURCE");
            try
            {
                var check = await plans.CheckUnstartedRecoveryAsync(task, ct);
                problem = check.Problem;
                return problem == null
                    ? new(new(task.Id, task.Title, null, true, "start", "RECOVERY_PREPARED_PLAN_READY"), check.Inputs!)
                    : Result("RECOVERY_PREPARED_PLAN_BLOCKED");
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
            { return Result("RECOVERY_PREPARED_PLAN_BLOCKED"); }
        }
        if (tasks?.IsPending("workflow.run." + run.Id) == true) return Result("ACQUISITION_TASK_ACTIVE");
        if (run.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Pending) return Result("ACQUISITION_TASK_ACTIVE");
        if (run.Status == WorkflowRunStatus.Success) return Result("RECOVERY_ALREADY_COMPLETED");
        if (run.Status is not (WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Waiting))
            return Result("RECOVERY_TASK_CANCELLED");
        if (run.PendingSignalJson != null) return Result("RECOVERY_PENDING_ANSWER");
        try
        {
            var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson);
            var node = plan?.Nodes.ElementAtOrDefault(run.CurrentStepIndex ?? -1);
            kind = node?.Kind;
            if (plan == null || node == null || plan.TriggerKind != CloudWorkflow.Trigger)
                return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
            if (plan.Nodes.Skip(run.CurrentStepIndex!.Value).Any(n => n.Kind is not
                ("baidu.transfer" or "download.enqueueSupported" or "download.awaitCompletion" or
                    "acquisition.plan.process" or AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts" or "download.cleanupArtifacts"))) return Result("RECOVERY_NODE_UNSUPPORTED");
            // Publication resumes with its own typed answer, not the cloud continuation signal.
            // Only a waiting publication at the end of the cloud portion is safe to resume here;
            // otherwise a later download would not inherit the persisted no-repeat constraint.
            if (kind == "fs.publishArtifacts" && (run.Status != WorkflowRunStatus.Waiting ||
                plan.Nodes.Skip(run.CurrentStepIndex.Value + 1).Any(n => n.Kind is
                    "baidu.transfer" or "download.enqueueSupported" or "download.awaitCompletion")))
                return Result("RECOVERY_NODE_UNSUPPORTED");
            foreach (var remaining in plan.Nodes.Skip(run.CurrentStepIndex.Value))
            {
                using var configDocument = JsonDocument.Parse(remaining.ConfigJson);
                if (configDocument.RootElement.TryGetProperty("reusePolicy", out var policy) && policy.GetString() == "force")
                    return Result("RECOVERY_MANUAL_REVIEW");
            }
            // Only a bounded, typed checkpoint is accepted. Never hydrate a CLR type from saved JSON.
            var workset = ReadWorkset(run.CurrentItemJson);
            if (workset == null || workset.Targets.Count is 0 or > 100 || workset.Targets.All(t => t.Excluded) ||
                workset.Targets.Any(t => !t.Excluded && (t.SelectedSource == null || t.AcquisitionNonce != null)))
                return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
            if (kind == "download.enqueueSupported" && run.Status is WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted &&
                transferredDownloads != null && workset.Targets.Any(t => !t.Excluded && t.RemoteEntry != null && t.DownloadTaskId == null))
            {
                var placement = await transferredDownloads.InspectAsync(task, run, workset, ct);
                if (placement != null)
                    return Result("RECOVERY_TRANSFERRED_FIRST_DOWNLOAD", true, signal: JsonSerializer.Serialize(
                        new CloudSelectionSignal {TransferredDownloadPlacement = placement.Proof}, WorkflowJson.Options))
                        with {InitialDownloadDirectories = placement.Directories,
                            InitialDownloadTargets = placement.Workset.Targets.Where(t => !t.Excluded)
                                .Select(t => t.DownloadDirectoryScope!.Directory).Distinct(ArtifactManifestFiles.PathComparer).ToArray(),
                            InitialDeliveryDirectories = placement.Workset.Targets.Where(t => !t.Excluded && t.PublicationTargetDirectory != null)
                                .Select(t => t.PublicationTargetDirectory!).Distinct(ArtifactManifestFiles.PathComparer).ToArray()};
            }
            if (kind == AcquisitionPlanProcessingActivity.ActivityKind)
            {
                if (run.Status == WorkflowRunStatus.Failed && run.ErrorMessage == "STAGE_COMMIT_REFERENCED" &&
                    task.WorkflowRunId == run.Id && workspaces != null && AcquisitionLocalRecoveryBatchService.IsLocalRemainder(run))
                {
                    problem = run.ErrorMessage;
                    var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(task.BuilderStateJson ?? "{}", WorkflowJson.Options);
                    var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(node.ConfigJson, WorkflowJson.Options);
                    var target = workset.Targets.SingleOrDefault(t => !t.Excluded && t.Id == config?.TargetId);
                    if (config?.ContinueFrom is {} continuation && target != null)
                    {
                        if (!verifyFiles) return Result("RECOVERY_VERIFY_FILES");
                        if (continuation.TaskId != task.Id || !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, run.Id, task.Id, ct))
                            return Result("RECOVERY_FILES_UNVERIFIED", manual: "inspectFiles");
                        var input = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, task.Id, target.Id, continuation, ct);
                        var following = plan.Nodes.Skip(run.CurrentStepIndex.Value + 1)
                            .Select(n => new WorkflowPlannedActivity(n.Kind, n.ConfigJson)).ToArray();
                        if (!AcquisitionPlanProcessingActivity.KeepsPublishedInput(config, input) ||
                            !await AcquisitionPlanProcessingActivity.CanForwardPublishedInputAsync(db, run.Id, workset, target, input, following, ct))
                            return Result("RECOVERY_PROCESSING_FILES_REFERENCED", manual: "inspectFiles");
                        if (!await ValidArtifact(input.Id, ct)) return Result("RECOVERY_FILES_UNVERIFIED", manual: "inspectFiles");
                        return Result("RECOVERY_STAGE_INPUT_READY", true, signal: JsonSerializer.Serialize(
                            new CloudFileProcessingSignal {TargetId = target.Id}, WorkflowJson.Options));
                    }
                    if (state?.ProcessingStageInputId is not { } pending || target == null || config?.ContinueFrom != null)
                        return Result("RECOVERY_MANUAL_REVIEW", manual: "inspectFiles");
                    if (!verifyFiles) return Result("RECOVERY_VERIFY_FILES");
                    try
                    {
                        if (!await AcquisitionProcessingStageInput.CanRetrySnapshotAsync(db, artifacts, storage, workspaces, task.Id, run.Id, target, pending, ct))
                            return Result("RECOVERY_FILES_UNVERIFIED", manual: "inspectFiles");
                    }
                    catch (InvalidOperationException error) when (error.Message is "STAGE_COMMIT_REFERENCED" or "STAGE_COMMIT_SHARED")
                    { return Result("RECOVERY_PROCESSING_FILES_REFERENCED", manual: "inspectFiles"); }
                    return Result("RECOVERY_STAGE_INPUT_READY", true, signal: JsonSerializer.Serialize(
                        new CloudFileProcessingSignal {TargetId = target.Id}, WorkflowJson.Options));
                }
                return await DiagnoseProcessingStorageAsync(task, run, plan, node, workset, verifyFiles, verifyContent, ct);
            }
            if (kind == AssessFileArtifactDeliveryActivity.ActivityKind)
                return await DiagnoseDeliveryStorageAsync(task, run, plan, node, workset, verifyFiles, ct);
            string? promptMessage = null;
            if (kind == "fs.publishArtifacts" && run.WaitPromptJson != null)
            {
                using var prompt = JsonDocument.Parse(run.WaitPromptJson);
                if (prompt.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    promptMessage = message.GetString();
            }
            var messages = new[] {run.ErrorMessage, promptMessage}.Concat(workset.Targets.Where(t => !t.Excluded).Select(t => t.Problem))
                .Where(m => !string.IsNullOrWhiteSpace(m)).Cast<string>().ToArray();
            // A typed publication I/O failure can be retried against the same verified input
            // and publication generation. It must never authorize another download or transfer.
            string? RecoverableProblem(string? message) => LegacyProblem(message) ??
                (kind == "fs.publishArtifacts" && run.WaitReason == "artifacts.publication" &&
                 (LocalStorageFailures.PersistedCode(message) != null ||
                  message?.StartsWith("PUBLICATION_IO_ERROR:", StringComparison.Ordinal) == true)
                    ? "RECOVERY_STORAGE_UNAVAILABLE" : null);
            problem = messages.Select(RecoverableProblem).FirstOrDefault(p => p != null) ??
                messages.Select(AcquisitionRuntimeProblems.Code).FirstOrDefault(p => p != null);
            if (kind == "download.awaitCompletion" && run.Status == WorkflowRunStatus.Waiting && downloads != null)
            {
                // A provider capability certifies one original failed task, not all targets in this run.
                // Unselected siblings retain their checkpoints and can still hold the workflow for review.
                var checkedTargets = new System.Collections.Generic.List<(string Id, CloudDownloadRecoveryCheck Check)>();
                foreach (var target in workset.Targets.Where(t => !t.Excluded))
                    checkedTargets.Add((target.Id, await downloads.CheckAsync(run.Id, target, workset.OutputDirectory, verifyFiles, ct, includeStoppedDownloads)));
                if (!verifyFiles && checkedTargets.Any(t => t.Check.Candidate)) return Result("RECOVERY_RECHECK_SOURCE");
                var verifyOnly = checkedTargets.Where(t => t.Check.CanRetry && t.Check.VerifyExistingOnly).Select(t => t.Id).ToList();
                if (verifyOnly.Count > 0)
                    return Result("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", true, signal: JsonSerializer.Serialize(new CloudSelectionSignal
                        {ObserveExistingOnly = true, ResumeTargetIds = verifyOnly, VerifyExistingFilesOnly = true}, WorkflowJson.Options));
                var ready = checkedTargets.Where(t => t.Check.CanRetry && !t.Check.VerifyExistingOnly).Select(t => t.Id).ToList();
                if (ready.Count > 0)
                    return Result("RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED", true, signal: JsonSerializer.Serialize(new CloudSelectionSignal
                        {ObserveExistingOnly = true, ResumeTargetIds = ready, RevalidateExistingDownloads = true}, WorkflowJson.Options));
                var checkedFailure = checkedTargets.FirstOrDefault(t => t.Check.FailureCode != null).Check;
                if (checkedFailure != null)
                {
                    problem = checkedFailure.FailureCode;
                    return Result(checkedFailure.ReasonCode, manual: "inspectSource");
                }
            }
            if (kind is not ("baidu.transfer" or "download.enqueueSupported" or "download.awaitCompletion" or "fs.publishArtifacts"))
                return Result("RECOVERY_NODE_UNSUPPORTED");
            foreach (var remaining in plan.Nodes.Skip(run.CurrentStepIndex.Value).Where(n => n.Kind is "baidu.transfer" or "download.enqueueSupported" or "download.awaitCompletion"))
            {
                var laterConfig = JsonSerializer.Deserialize<CloudWorkflowConfig>(remaining.ConfigJson, WorkflowJson.Options) ?? new();
                if (!string.IsNullOrWhiteSpace(laterConfig.OutputDirectory) &&
                    !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(laterConfig.OutputDirectory), Path.GetFullPath(workset.OutputDirectory)) ||
                    !string.IsNullOrWhiteSpace(laterConfig.AccountId) && workset.Targets.Any(t => !t.Excluded &&
                        t.SelectedSource?.ProviderId == "baidu" && t.AccountId != laterConfig.AccountId))
                    return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
            }
            // Download-only recovery can finish while this waiting workset retains its old provider
            // error. Observe verified completed originals; never turn that stale text into a retry.
            if (kind == "download.awaitCompletion" && run.Status == WorkflowRunStatus.Waiting &&
                await CompletedOriginalDownloads(run.Id, workset, verifyFiles, ct) is { } completed)
                return Result(completed, completed == "RECOVERY_FILES_VERIFIED",
                    signal: "{\"observeExistingOnly\":true}");
            if (messages.Any(m => RecoverableProblem(m) == null))
                return Result("RECOVERY_MANUAL_REVIEW");
            if (problem is not ("RECOVERY_SERVICE_SCOPE_DISPOSED" or "RECOVERY_STORAGE_UNAVAILABLE" or "RECOVERY_MANIFEST_CHANGED"))
                return Result("RECOVERY_MANUAL_REVIEW", manual: "reviewTask");
            foreach (var target in workset.Targets.Where(t => !t.Excluded))
            {
                // Publication consumes an already registered local artifact. Historical download
                // roots, provider transfer receipts and accounts are no longer dependencies of it.
                if (kind == "fs.publishArtifacts")
                {
                    if (problem is not ("RECOVERY_MANIFEST_CHANGED" or "RECOVERY_STORAGE_UNAVAILABLE"))
                        return Result("RECOVERY_MANUAL_REVIEW", manual: "inspectFiles");
                    var publication = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(node.ConfigJson, WorkflowJson.Options) ?? new();
                    if (publication.ReusePolicy == "force" || publication.Source is not ("download" or "processed"))
                        return Result("RECOVERY_MANUAL_REVIEW");
                    var artifactId = publication.Source == "download" ? target.DownloadArtifactId : target.ProcessedArtifactId;
                    var artifact = artifactId == null ? null : await artifacts.GetArtifactAsync(artifactId, ct);
                    if (artifact == null || !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, run.Id, task.Id, ct) ||
                        !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
                            b.OwnerId == run.Id.ToString() && b.OperationId == artifact.OperationId &&
                            (publication.Source == "download" ? b.Slot == target.Id + ":artifact.download" || b.Slot == target.Id + ":artifact.continuation" :
                                b.Slot.StartsWith(target.Id + ":process:") || b.Slot == target.Id + ":artifact.continuation"), ct))
                        return Result("RECOVERY_FILES_UNVERIFIED", manual: "inspectFiles");
                    var operation = await artifacts.GetOperationAsync(artifact.OperationId, ct);
                    // Older adaptive/publication checkpoints can contain the processed
                    // artifact and its bound operation while omitting the derived intent
                    // fields on the target.  The publication activity already treats the
                    // completed operation as the source of truth in that case.  Keep the
                    // same rule here so recovery can republish the verified artifact
                    // without reacquiring it.  A non-null, conflicting value still blocks
                    // recovery.
                    var processingOperationMatches = publication.Source != "processed" ||
                        target.ProcessingOperationId == null || operation?.Id == target.ProcessingOperationId;
                    var processingIntentMatches = publication.Source != "processed" ||
                        string.IsNullOrWhiteSpace(target.ProcessingIntentKey) || operation?.IntentKey == target.ProcessingIntentKey;
                    var borrowed = publication.Source == "processed" && operation?.Kind == "files.publish" &&
                        await AcquisitionPlanArtifactInputGuard.IsBorrowedPublicationInputAsync(db, task.Id, run, target, artifact, ct);
                    if (operation?.State != ArtifactOperationState.Completed || operation.OutputArtifactId != artifact.Id ||
                        !borrowed && operation.Kind != (publication.Source == "download" ? "download" : "files.process") ||
                        (publication.Source == "download" && (operation.Id != target.DownloadOperationId ||
                            operation.InputFingerprint != DownloadArtifactCoordinator.Source(target) ||
                            operation.IntentKey != DownloadArtifactCoordinator.ArtifactIntent(target, workset.OutputDirectory, workspaces))) ||
                        (publication.Source == "processed" && !borrowed && (!processingOperationMatches || !processingIntentMatches)))
                        return Result("RECOVERY_FILES_UNVERIFIED", manual: "inspectFiles");
                    if (!verifyFiles) return Result("RECOVERY_VERIFY_FILES");
                    if (!await ValidArtifact(artifactId, ct)) return Result("RECOVERY_FILES_UNVERIFIED", manual: "inspectFiles");
                    var directory = target.PublishedDirectory ?? target.PublicationTargetDirectory ??
                        PublishFileArtifactsActivity.CloudTargetDirectory(workset, target, publication);
                    storage.EnsurePathAllowed(directory);
                    ArtifactManifestFiles.RejectLinks(directory);
                    continue;
                }
                var intent = DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory, workspaces);
                // The workset keeps its semantic input after a reviewed destination revision.
                // Only an exclusively bound, matching persisted request may replace that physical root.
                var executionRoot = await DownloadExecutionRoot(run.Id, target, workset.OutputDirectory, ct);
                storage.EnsurePathAllowed(executionRoot);
                ArtifactManifestFiles.RejectLinks(executionRoot);
                if (!Directory.Exists(executionRoot)) return Result("RECOVERY_STORAGE_UNAVAILABLE", manual: "checkStorage");
                var config = JsonSerializer.Deserialize<CloudWorkflowConfig>(node!.ConfigJson, WorkflowJson.Options) ?? new();
                if (config.ReusePolicy == "force") return Result("RECOVERY_MANUAL_REVIEW");
                if (!string.IsNullOrWhiteSpace(config.OutputDirectory) &&
                    !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(config.OutputDirectory), Path.GetFullPath(workset.OutputDirectory)) ||
                    target.SelectedSource?.ProviderId == "baidu" && !string.IsNullOrWhiteSpace(config.AccountId) && config.AccountId != target.AccountId)
                    return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
                var transferId = CloudOperationService.Key("transfer.v2", DownloadArtifactCoordinator.Source(target), config.RemoteDirectory);
                var slot = CloudOperationService.OperationSlot(target, "transfer");
                var bound = await db.ArtifactOperationBindings.AsNoTracking().SingleOrDefaultAsync(b => b.OwnerKind == "workflow" &&
                    b.OwnerId == run.Id.ToString() && b.Slot == slot, ct);
                if (bound != null && bound.OperationId != transferId) return Result("RECOVERY_TRANSFER_UNKNOWN", manual: "inspectSource");
                var transfer = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == transferId, ct);
                if (transfer?.State is "running" or "unknown") return Result("RECOVERY_TRANSFER_UNKNOWN", manual: "inspectSource");
                if (kind == "baidu.transfer")
                {
                    if (problem != "RECOVERY_SERVICE_SCOPE_DISPOSED") return Result("RECOVERY_MANUAL_REVIEW");
                    // A transfer cursor can coexist with a shared download left by another run.
                    // Never present a missing/failed existing generation as a fresh first download.
                    var download = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent, ct);
                    if (download != null)
                    {
                        var reason = await CheckDownload(download.DownloadTaskId, executionRoot, verifyFiles, ct);
                        if (reason != null) return Result(reason, manual: "inspectDownload");
                    }
                    else if (await artifacts.GetOperationAsync(intent, ct) is {} known)
                    {
                        if (known.State != ArtifactOperationState.Completed || known.OutputArtifactId == null)
                            return Result("RECOVERY_DOWNLOAD_REVIEW_REQUIRED", manual: "inspectDownload");
                        if (!verifyFiles) return Result("RECOVERY_VERIFY_FILES");
                        if (!await ValidArtifact(known.OutputArtifactId, ct)) return Result("RECOVERY_FILES_UNVERIFIED", manual: "inspectFiles");
                    }
                    else if (target.DownloadTaskId != null || target.TransportTaskId != null || target.DownloadArtifactId != null)
                        return Result("RECOVERY_DOWNLOAD_REVIEW_REQUIRED", manual: "inspectDownload");
                    if (target.SelectedSource!.ProviderId != "baidu" || target.SelectedSource.Kind == "remote") continue;
                    if (string.IsNullOrWhiteSpace(target.AccountId)) return Result("RECOVERY_TRANSFER_REVIEW_REQUIRED", manual: "inspectSource");
                    if (target.RemoteEntry != null) continue;
                    // The transfer writer durably records its intent before calling the provider.
                    // Absent operation is therefore a first attempt, not an uncertain repeat.
                    if (transfer == null) continue;
                    if (transfer.State != "succeeded" || string.IsNullOrWhiteSpace(transfer.ReceiptJson))
                        return Result("RECOVERY_TRANSFER_REVIEW_REQUIRED", manual: "inspectSource");
                    var receipt = JsonSerializer.Deserialize<CloudTransferReceipt>(transfer.ReceiptJson, WorkflowJson.Options);
                    if (receipt?.OperationId != transferId || receipt.Entry.AccountId != target.AccountId)
                        return Result("RECOVERY_TRANSFER_UNKNOWN", manual: "inspectSource");
                    continue;
                }
                var siteId = target.DownloadTaskId;
                if (kind == "download.enqueueSupported")
                {
                    var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent, ct);
                    siteId = operation?.DownloadTaskId;
                }
                if (target.TransportTaskId != null || siteId == null)
                    return Result("RECOVERY_DOWNLOAD_REVIEW_REQUIRED", manual: "inspectDownload");
                var downloadReason = await CheckDownload(siteId, executionRoot, verifyFiles, ct);
                if (downloadReason != null) return Result(downloadReason, manual: "inspectFiles");
            }
            var signal = "{\"observeExistingOnly\":true}";
            if (kind == "fs.publishArtifacts" && run.Status == WorkflowRunStatus.Waiting)
            {
                using var prompt = JsonDocument.Parse(run.WaitPromptJson ?? "{}");
                var waiting = workset.Targets.Where(t => !t.Excluded && t.PublicationWaitReason != null).ToArray();
                if (!prompt.RootElement.TryGetProperty("targetId", out var selected) ||
                    waiting.Length != 1 || waiting[0].Id != selected.GetString()) return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
                signal = JsonSerializer.Serialize(new FileArtifactPublicationSignal(selected.GetString()), WorkflowJson.Options);
            }
            return Result(kind == "baidu.transfer" ? "RECOVERY_RUNTIME_SCOPE_FIXED" : "RECOVERY_FILES_VERIFIED", true, signal: signal);
        }
        catch (UserStoragePathException) {return Result("RECOVERY_STORAGE_UNAVAILABLE", manual: "checkStorage");}
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        {return Result("RECOVERY_CHECK_INCOMPLETE", manual: "inspectFiles");}
    }

    private async Task<string?> CompletedOriginalDownloads(int runId, DownloadWorkset workset, bool verifyFiles, CancellationToken ct)
    {
        var selected = workset.Targets.Where(t => !t.Excluded).ToArray();
        // A downstream/manual checkpoint is not a stale downloader error and must keep its own review.
        if (selected.Any(t => t.DownloadTaskId == null || t.TransportTaskId != null || t.ProcessedArtifactId != null ||
                t.PublishedArtifactId != null || t.ProcessingWaitReason != null || t.PublicationWaitReason != null ||
                t.DeliveryReview != null || t.ProcessedFiles.Count > 0 || t.PublishedFiles.Count > 0)) return null;
        foreach (var target in selected)
            if (!await db.DownloadTasks.AsNoTracking().AnyAsync(d => d.Id == target.DownloadTaskId &&
                    d.Status == DownloadTaskDbModelStatus.Complete, ct)) return null;
        try
        {
            foreach (var target in selected)
            {
                var root = await DownloadExecutionRoot(runId, target, workset.OutputDirectory, ct, requireBinding: true);
                storage.EnsurePathAllowed(root); ArtifactManifestFiles.RejectLinks(root);
                if (!Directory.Exists(root)) return null;
                var reason = await CheckDownload(target.DownloadTaskId, root, verifyFiles, ct);
                if (reason != null && !(reason == "RECOVERY_VERIFY_FILES" && !verifyFiles)) return null;
            }
            return verifyFiles ? "RECOVERY_FILES_VERIFIED" : "RECOVERY_VERIFY_FILES";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        { return null; }
    }

    internal async Task<string> DownloadExecutionRoot(int runId, CloudDownloadTarget target, string semanticRoot, CancellationToken ct,
        bool requireBinding = false, bool allowRegisteredArtifact = false)
    {
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, semanticRoot, workspaces);
        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent, ct);
        var id = target.DownloadTaskId ?? operation?.DownloadTaskId;
        var download = id == null ? null : await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        // Existing non-cloud/legacy task options need not deserialize as a cloud request.
        static CloudDownloadRequest? Revised(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("destinationRevisions", out var revisions) && revisions.ValueKind != JsonValueKind.Null
                ? document.RootElement.Deserialize<CloudDownloadRequest>(WorkflowJson.Options) : null;
        }
        var request = Revised(operation?.RequestJson);
        var configured = Revised(download?.Options);
        if (request == null && configured == null)
        {
            if (!requireBinding) return semanticRoot;
            request = string.IsNullOrWhiteSpace(operation?.RequestJson) ? null :
                JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, WorkflowJson.Options);
            configured = string.IsNullOrWhiteSpace(download?.Options) ? null :
                JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options, WorkflowJson.Options);
        }
        static string Json<T>(T value) => JsonSerializer.Serialize(value, WorkflowJson.Options);
        static CloudRemoteEntry? Payload(CloudRemoteEntry? entry) => entry == null ? null : entry with {OwnedContainer = null};
        void Require(bool value)
        {
            if (!value) throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        }
        Require(request != null && configured != null && download != null &&
                operation is {Kind: "download", State: "started" or "starting" or "queued" or "succeeded"} &&
                operation.WorkflowRunId == runId && operation.TargetId == target.Id && operation.DownloadTaskId == download.Id &&
                request.OperationId == intent && (target.DownloadOperationId == null || target.DownloadOperationId == intent) &&
                Json(request) == Json(configured) && Json(request.Source) == Json(target.SelectedSource) &&
                request.AccountId == (target.AccountId ?? target.SelectedSource?.AccountId) &&
                Json(Payload(request.RemoteEntry)) == Json(Payload(target.RemoteEntry)));
        CloudDownloadDestinations.Validate(request!);
        FileProcessingFiles.Within(request!.OutputDirectory, semanticRoot);
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == intent).ToListAsync(ct);
        // Registration of these same completed bytes adds this exact artifact slot. It does
        // not constitute another downloader consumer or relax the exclusive cloud binding.
        if (allowRegisteredArtifact) bindings.RemoveAll(b => b.OwnerKind == "workflow" && b.OwnerId == runId.ToString() &&
                                b.Slot == target.Id + ":artifact.download");
        Require(bindings.Count == 1 && bindings[0].OwnerKind == "workflow" && bindings[0].OwnerId == runId.ToString() &&
                bindings[0].Slot == CloudOperationService.OperationSlot(target, "download"));
        var directory = CloudDownloadDestinations.ExecutionDirectory(request);
        var root = CloudDownloadDestinations.ExecutionRoot(request, semanticRoot);
        Require(ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(directory), Path.GetFullPath(download!.DownloadPath)));
        FileProcessingFiles.Within(directory, root);
        ArtifactManifestFiles.RejectLinks(directory);
        return root;
    }

    internal async Task<string?> CheckDownload(int? id, string outputRoot, bool verifyFiles, CancellationToken ct)
    {
        if (id == null) return "RECOVERY_DOWNLOAD_REVIEW_REQUIRED";
        var download = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (download?.Status != DownloadTaskDbModelStatus.Complete) return "RECOVERY_DOWNLOAD_REVIEW_REQUIRED";
        var result = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == id).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        if (result == null || string.IsNullOrWhiteSpace(result.Fingerprint)) return "RECOVERY_FILES_UNVERIFIED";
        var files = JsonSerializer.Deserialize<string[]>(result.FilesJson, WorkflowJson.Options) ?? [];
        if (files.Length is 0 or > 100000) return "RECOVERY_FILES_UNVERIFIED";
        if (!CloudDownloadPayloadRoot.TryValidate(download.DownloadPath, result.DownloadDirectory, files, out _))
            return "RECOVERY_FILES_UNVERIFIED";
        FileProcessingFiles.Within(result.DownloadDirectory, outputRoot);
        storage.EnsurePathAllowed(result.DownloadDirectory);
        foreach (var file in files) storage.EnsurePathAllowed(file);
        if (!verifyFiles) return "RECOVERY_VERIFY_FILES";
        var manifest = await ArtifactManifestFiles.CaptureAsync(result.DownloadDirectory, files, ct);
        return manifest.Fingerprint == result.Fingerprint ? null : "RECOVERY_FILES_UNVERIFIED";
    }

    private async Task<bool> ValidArtifact(string? id, CancellationToken ct)
    {
        if (id == null) return false;
        var artifact = await artifacts.GetArtifactAsync(id, ct); // ResolveById writes state; preview must not.
        if (artifact?.State != ArtifactLocationState.Available) return false;
        var location = await db.Set<ArtifactDbModel>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (location == null) return false;
        if (!location.RootIsAppData) storage.EnsurePathAllowed(artifact.RootDirectory);
        var operation = await artifacts.GetOperationAsync(artifact.OperationId, ct);
        if (operation?.State != ArtifactOperationState.Completed) return false;
        return (await ArtifactManifestFiles.ValidateAsync(artifact.RootDirectory, artifact.Manifest, ct)).Available;
    }
    internal static string? LegacyProblem(string? text)
    {
        if (text == null) return null;
        if (text.Contains("Cannot access a disposed object.", StringComparison.Ordinal) && text.Contains("IServiceProvider", StringComparison.Ordinal)) return "RECOVERY_SERVICE_SCOPE_DISPOSED";
        if (text.Contains("Choose a folder inside a mounted storage location.", StringComparison.Ordinal)) return "RECOVERY_STORAGE_UNAVAILABLE";
        if (text.Contains("An artifact file changed while its manifest was being recorded.", StringComparison.Ordinal)) return "RECOVERY_MANIFEST_CHANGED";
        return null;
    }
    internal static DownloadWorkset? ReadWorkset(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8 * 1024 * 1024) return null;
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement;
        if (item.TryGetProperty("item", out var wrapped))
        {
            var expected = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name;
            if (!item.TryGetProperty("type", out var type) || type.GetString() != expected) return null;
            item = wrapped;
        }
        else return null;
        if (!item.TryGetProperty("targets", out _)) return null;
        return item.Deserialize<DownloadWorkset>(WorkflowJson.Options);
    }
}

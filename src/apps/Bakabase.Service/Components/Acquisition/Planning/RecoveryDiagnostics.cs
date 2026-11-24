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
using Bakabase.Service.Components.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRecoveryDiagnosis(AcquisitionRecoveryRow Row, string SignalJson = "{}")
{
    internal string? PreparedDownloadReason { get; init; }
    internal DownloadWorkset? PreparedWorkset { get; init; }
    // A suspended attempt can have pinned writes even without a workspace policy on its old plan.
    internal IReadOnlyDictionary<int, CompletedDownloadPlacement> CompletedDownloads { get; init; } = new Dictionary<int, CompletedDownloadPlacement>();
    internal string? ProcessingPartId { get; init; }
    internal IReadOnlyList<string> ProcessingStorageRoots { get; init; } = [];
    internal string? ProcessingPlacementStamp { get; init; }
    internal AcquisitionCheckpointWrite? CheckpointWrite { get; init; }
    internal IReadOnlyList<string> InitialDownloadDirectories { get; init; } = [];
    internal IReadOnlyList<string> InitialDownloadTargets { get; init; } = [];
    internal string? FirstDownloadDirectory => InitialDownloadDirectories.FirstOrDefault();
    internal IReadOnlyList<string> InitialDeliveryDirectories { get; init; } = [];
    internal string? FirstDeliveryDirectory => InitialDeliveryDirectories.FirstOrDefault();
    internal IReadOnlyList<CloudTransferReplay> TransferReplays { get; init; } = [];
    internal IReadOnlyList<AcquisitionTransferRetryTarget> TransferRetryTargets { get; init; } = [];
}
public interface IAcquisitionRecoveryDiagnostics
{
    Task<AcquisitionRecoveryDiagnosis> WithCompletedDownloadPlacementAsync(WorkflowRunDbModel run,
        AcquisitionRecoveryDiagnosis diagnosis, CancellationToken ct) => Task.FromResult(diagnosis);
    Task<IReadOnlyList<string>?> InspectUpcomingProcessingRootsAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel run,
        DownloadWorkset workset, CloudDownloadTarget target, AcquisitionPlanProcessingConfig config, AcquisitionRecoveryDiagnosis diagnosis, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>?>(null);
    bool IsTransferStep(string? kind) => CloudWorkflow.TransferActivity(kind) != null;
    Task<AcquisitionRecoveryDiagnosis> DiagnoseTransferRetryAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        CancellationToken ct) => Task.FromResult(new AcquisitionRecoveryDiagnosis(new(task.Id, task.Title, run?.Id,
            false, null, "RECOVERY_CHECKPOINT_UNSUPPORTED")));
    Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run, CancellationToken ct,
        bool verifyFiles = true, bool verifyContent = false, bool includeStoppedDownloads = false,
        bool includeStoppedVerification = false, bool deferCompletedDownloadVerification = false);
}

/// <summary>Explicit, read-only recovery capabilities. Supported provider failures receive a bounded
/// original-file recheck. Never creates receipts, mutates artifacts, purchases or initializes a download engine.</summary>
public sealed partial class AcquisitionRecoveryDiagnostics(BakabaseDbContext db, IUserStoragePolicy storage,
    IArtifactOperationStore artifacts, BTaskManager? tasks = null, FileProcessingWorkspaceResolver? workspaces = null,
    CloudDownloadRecoveryService? downloads = null, AcquisitionPlanService? plans = null,
    AcquisitionTransferredDownloadRecovery? transferredDownloads = null,
    CloudManualDownloadResume? manualDownloads = null, CloudOperationService? transfers = null, IWorkflowActivityRegistry? activities = null) : IAcquisitionRecoveryDiagnostics
{
    public async Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run, CancellationToken ct,
        bool verifyFiles = true, bool verifyContent = false, bool includeStoppedDownloads = false,
        bool includeStoppedVerification = false, bool deferCompletedDownloadVerification = false)
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
                    ? new(new(task.Id, task.Title, null, true, "start", "RECOVERY_PREPARED_PLAN_READY"), check.Inputs!) {PreparedWorkset = check.Workset}
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
            if (plan.Nodes.Skip(run.CurrentStepIndex!.Value).Any(n => !IsTransferStep(n.Kind) && n.Kind is not
                ("download.enqueueSupported" or "download.awaitCompletion" or
                    "acquisition.plan.process" or AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts" or "download.cleanupArtifacts"))) return Result("RECOVERY_NODE_UNSUPPORTED");
            // Publication resumes with its own typed answer, not the cloud continuation signal.
            // Only a waiting publication at the end of the cloud portion is safe to resume here;
            // otherwise a later download would not inherit the persisted no-repeat constraint.
            if (kind == "fs.publishArtifacts" && (run.Status != WorkflowRunStatus.Waiting ||
                plan.Nodes.Skip(run.CurrentStepIndex.Value + 1).Any(n => IsTransferStep(n.Kind) || n.Kind is
                    "download.enqueueSupported" or "download.awaitCompletion")))
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
            if (IsTransferStep(kind) && transfers != null &&
                await InspectTransferRecoveryAsync(task, run, node, workset, verifyFiles, ct) is {} transferRecovery)
                return transferRecovery;
            if (IsTransferStep(kind) && messages.SelectMany(WorkflowSourceRunProblems.Extract)
                    .FirstOrDefault(ExecutionProblemClassifier.RequiresTransferTargetReview) is { } transferProblem)
            {
                problem = transferProblem;
                return Result("RECOVERY_TRANSFER_REVIEW_REQUIRED", manual: "inspectSource");
            }
            // A typed publication I/O failure can be retried against the same verified input
            // and publication generation. It must never authorize another download or transfer.
            string? RecoverableProblem(string? message) => LegacyProblem(message) ??
                (kind == "fs.publishArtifacts" && run.WaitReason == "artifacts.publication" &&
                 (LocalStorageFailures.PersistedCode(message) != null ||
                  message?.StartsWith("PUBLICATION_IO_ERROR:", StringComparison.Ordinal) == true)
                    ? "RECOVERY_STORAGE_UNAVAILABLE" : null);
            problem = messages.Select(RecoverableProblem).FirstOrDefault(p => p != null) ??
                messages.Select(AcquisitionRuntimeProblems.Code).FirstOrDefault(p => p != null);
            if (kind == "download.awaitCompletion" && run.Status == WorkflowRunStatus.Waiting &&
                includeStoppedVerification && !includeStoppedDownloads && manualDownloads != null &&
                workset.Targets is [var legacyTarget] &&
                await manualDownloads.InspectLegacyBitTorrentAsync(run.Id, legacyTarget, workset.OutputDirectory, ct) is {} legacy &&
                (legacy.Job.CanResume || legacy.Job.CanRetry))
            {
                problem = AcquisitionRuntimeProblems.Code(legacy.Job.Error) ?? problem;
                return Result("RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED", true, signal: JsonSerializer.Serialize(
                    new CloudSelectionSignal {ObserveExistingOnly = true, ResumeTargetIds = [legacyTarget.Id]}, WorkflowJson.Options))
                    with {InitialDownloadDirectories = [legacy.Native.OutputDirectory]};
            }
            if (kind == "download.awaitCompletion" && run.Status == WorkflowRunStatus.Waiting && downloads != null)
            {
                // A provider capability certifies one original failed task, not all targets in this run.
                // Unselected siblings retain their checkpoints and can still hold the workflow for review.
                var checkedTargets = new System.Collections.Generic.List<(string Id, CloudDownloadRecoveryCheck Check)>();
                var originalGroups = workset.Targets.Where(t => t.DownloadTaskId != null)
                    .GroupBy(t => t.DownloadTaskId!.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                var checkedGroups = new System.Collections.Generic.HashSet<int>();
                foreach (var target in workset.Targets.Where(t => !t.Excluded))
                {
                    var originalTarget = target;
                    var grouped = target.DownloadTaskId is { } native && originalGroups.Contains(native);
                    if (grouped)
                    {
                        if (!checkedGroups.Add(target.DownloadTaskId!.Value)) continue;
                        originalTarget = (await downloads.ReadEquivalentGroupAsync(run.Id, target, workset.OutputDirectory, ct))?.Owner ?? target;
                    }
                    checkedTargets.Add((originalTarget.Id, await downloads.CheckAsync(run.Id, originalTarget, workset.OutputDirectory, verifyFiles, ct,
                        includeStoppedDownloads, includeStoppedForVerification: includeStoppedVerification && task.WorkflowRunId == run.Id,
                        requireEquivalentGroup: grouped)));
                }
                AcquisitionRecoveryDiagnosis OriginalRetry(List<(string Id, CloudDownloadRecoveryCheck Check)> targets, bool checkedRemote)
                {
                    problem = targets[0].Check.FailureCode ?? problem;
                    var prepared = targets.Where(t => t.Check.PreparedRetry != null)
                        .ToDictionary(t => t.Id, t => t.Check.PreparedRetry!, StringComparer.Ordinal);
                    if (!checkedRemote && prepared.Count == 0) return Result("RECOVERY_RECHECK_SOURCE");
                    var preparedReason = prepared.Count == 0 ? null : prepared.Count == targets.Count
                        ? "RECOVERY_PREPARED_DOWNLOAD_REVALIDATED" : "RECOVERY_MIXED_DOWNLOADS_REVALIDATED";
                    var signal = new CloudSelectionSignal {ObserveExistingOnly = true, ResumeTargetIds = targets.Select(t => t.Id).ToList(),
                        RevalidateExistingDownloads = true, PreparedDownloadRetries = prepared.Count > 0 ? prepared : null};
                    return Result(checkedRemote ? preparedReason ?? "RECOVERY_ORIGINAL_DOWNLOAD_REVALIDATED" : "RECOVERY_RECHECK_SOURCE",
                        checkedRemote, signal: JsonSerializer.Serialize(signal, WorkflowJson.Options)) with {PreparedDownloadReason = preparedReason};
                }
                var candidates = checkedTargets.Where(t => t.Check.Candidate).ToList();
                if (!verifyFiles && candidates.Count > 0) return OriginalRetry(candidates, false);
                // The latest workflow message can describe a failed recovery probe.
                // Report the native failure on which this recovery action depends.
                var verifyOnly = checkedTargets.Where(t => t.Check.CanRetry && t.Check.VerifyExistingOnly).ToList();
                if (verifyOnly.Count > 0)
                {
                    problem = verifyOnly[0].Check.FailureCode ?? problem;
                    var targetIds = verifyOnly.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                    var siteIds = workset.Targets.Where(t => targetIds.Contains(t.Id))
                        .Select(t => t.DownloadTaskId!.Value).ToArray();
                    var directories = await db.DownloadTasks.AsNoTracking().Where(d => siteIds.Contains(d.Id))
                        .Select(d => d.DownloadPath).Distinct().ToArrayAsync(ct);
                    return Result("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", true, signal: JsonSerializer.Serialize(new CloudSelectionSignal
                        {ObserveExistingOnly = true, ResumeTargetIds = verifyOnly.Select(t => t.Id).ToList(), VerifyExistingFilesOnly = true}, WorkflowJson.Options))
                        with {InitialDownloadDirectories = directories};
                }
                var ready = checkedTargets.Where(t => t.Check.CanRetry && !t.Check.VerifyExistingOnly).ToList();
                if (ready.Count > 0) return OriginalRetry(ready, true);
                var checkedFailure = checkedTargets.FirstOrDefault(t => t.Check.FailureCode != null).Check;
                if (checkedFailure != null)
                {
                    problem = checkedFailure.FailureCode;
                    return Result(checkedFailure.ReasonCode, manual: "inspectSource");
                }
            }
            if (!IsTransferStep(kind) && kind is not ("download.enqueueSupported" or "download.awaitCompletion" or "fs.publishArtifacts"))
                return Result("RECOVERY_NODE_UNSUPPORTED");
            foreach (var remaining in plan.Nodes.Skip(run.CurrentStepIndex.Value).Where(n => IsTransferStep(n.Kind) || n.Kind is "download.enqueueSupported" or "download.awaitCompletion"))
            {
                var laterConfig = JsonSerializer.Deserialize<CloudWorkflowConfig>(remaining.ConfigJson, WorkflowJson.Options) ?? new();
                if (!string.IsNullOrWhiteSpace(laterConfig.OutputDirectory) &&
                    !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(laterConfig.OutputDirectory), Path.GetFullPath(workset.OutputDirectory)) ||
                    !string.IsNullOrWhiteSpace(laterConfig.AccountId) && workset.Targets.Any(t => !t.Excluded &&
                        t.SelectedSource?.ProviderId == "baidu" && t.AccountId != laterConfig.AccountId))
                    return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
            }
            // Download-only recovery can finish while this waiting workset retains its old provider
            // error. A continuation can pin the original receipts here and verify bytes in the workflow.
            if (kind == "download.awaitCompletion" && run.Status == WorkflowRunStatus.Waiting &&
                await CompletedOriginalDownloads(run.Id, workset, verifyFiles, ct, deferCompletedDownloadVerification) is { } completed)
                return Result(completed.Reason, completed.Reason != "RECOVERY_VERIFY_FILES", signal: completed.Signal) with
                    {CompletedDownloads = completed.Receipts};
            if (kind == "download.awaitCompletion" && !includeStoppedDownloads && HistoricalFailedMountRoot(run) is { } failedMount)
            {
                problem = "RECOVERY_STORAGE_UNAVAILABLE";
                // This exact old mount guard could escape before the wait node returned a
                // typed suspension. Re-enter only its owned cursor, without repeating source work.
                if (plan.Nodes.Skip(run.CurrentStepIndex.Value + 1).Any(n => n.Kind is not
                    (AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or
                        "fs.publishArtifacts" or "download.cleanupArtifacts")))
                    return Result("RECOVERY_NODE_UNSUPPORTED");
                var owner = task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var revision = task.PlanRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (task.WorkflowRunId != run.Id || task.PlanRevision < 1 ||
                    workset.Id != $"acquisition:{owner}:{revision}" ||
                    workset.SourceRefs?.Any(s => s.Module == "acquisition" && s.EntityKind == "task" &&
                        s.EntityId == owner && s.Revision == revision) != true ||
                    !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, run.Id, task.Id, ct) ||
                    !await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().AnyAsync(s => s.WorkflowRunId == run.Id &&
                        s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == owner && s.Revision == revision, ct))
                    return Result("ACQUISITION_TASK_CHANGED");
                var permittedDirectories = workset.Targets.Where(t => !t.Excluded)
                    .Select(t => CloudStageDirectoryPath.Download(t) ?? workset.OutputDirectory)
                    .Distinct(ArtifactManifestFiles.PathComparer).ToArray();
                if (!permittedDirectories.Any(path => HistoricalMountContains(failedMount, path)))
                    return Result("RECOVERY_MANUAL_REVIEW");
                if (storage.IsRestricted && !storage.GetRoots().Any(root =>
                        ArtifactManifestFiles.PathComparer.Equals(root.Path, failedMount)))
                    return Result("RECOVERY_STORAGE_UNAVAILABLE", manual: "checkStorage");
                foreach (var path in permittedDirectories) storage.EnsurePathAllowed(path);
                var original = await CompletedOriginalDownloads(run.Id, workset, verifyFiles, ct);
                return Result(original?.Reason ?? "RECOVERY_FILES_UNVERIFIED", original?.Reason == "RECOVERY_FILES_VERIFIED",
                    manual: "inspectFiles", signal: original?.Signal ?? "{}");
            }
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
                var transferId = target.ReviewedTransferId ?? CloudOperationService.Key("transfer.v2", DownloadArtifactCoordinator.Source(target), config.RemoteDirectory);
                var slot = CloudOperationService.OperationSlot(target, "transfer");
                var bound = await db.ArtifactOperationBindings.AsNoTracking().SingleOrDefaultAsync(b => b.OwnerKind == "workflow" &&
                    b.OwnerId == run.Id.ToString() && b.Slot == slot, ct);
                if (bound != null && bound.OperationId != transferId) return Result("RECOVERY_TRANSFER_UNKNOWN", manual: "inspectSource");
                var transfer = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == transferId, ct);
                if (transfer?.State is "running" or "unknown") return Result("RECOVERY_TRANSFER_UNKNOWN", manual: "inspectSource");
                if (CloudWorkflow.TransferActivity(kind, activities) is {} transferActivity)
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
                    if (!transferActivity.Transfers(target.SelectedSource!)) continue;
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
            return Result(IsTransferStep(kind) ? "RECOVERY_RUNTIME_SCOPE_FIXED" : "RECOVERY_FILES_VERIFIED", true, signal: signal);
        }
        catch (UserStoragePathException) {return Result("RECOVERY_STORAGE_UNAVAILABLE", manual: "checkStorage");}
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        {return Result("RECOVERY_CHECK_INCOMPLETE", manual: "inspectFiles");}
    }

    private static string? HistoricalFailedMountRoot(WorkflowRunDbModel run)
    {
        const string prefix = "Reconnect the mounted storage location. ";
        if (run.Status != WorkflowRunStatus.Failed || run.CompletedAt == null || run.PendingSignalJson != null ||
            run.WaitReason != null || run.WaitPromptJson != null ||
            run.ErrorMessage is not {Length: <= 16384} error || !error.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var path = error[prefix.Length..];
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl)) return null;
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return ArtifactManifestFiles.PathComparer.Equals(path, normalized) &&
            !ArtifactManifestFiles.PathComparer.Equals(normalized, Path.GetPathRoot(normalized)) ? normalized : null;
    }

    private static bool HistoricalMountContains(string mountRoot, string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl)) return false;
        var relative = Path.GetRelativePath(mountRoot, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public async Task<AcquisitionRecoveryDiagnosis> WithCompletedDownloadPlacementAsync(WorkflowRunDbModel run,
        AcquisitionRecoveryDiagnosis diagnosis, CancellationToken ct)
    {
        if (diagnosis.CompletedDownloads.Count != 0 || diagnosis.Row.CurrentStepKind != "download.awaitCompletion" ||
            diagnosis.Row.ReasonCode is not ("RECOVERY_VERIFY_FILES" or "RECOVERY_FILES_VERIFIED") ||
            ReadWorkset(run.CurrentItemJson) is not {} workset) return diagnosis;
        // A failed download cursor still requires its original byte verification at dispatch.
        // Obtain the exact completed registrations only to determine where processing will write.
        var observed = await CompletedOriginalDownloads(run.Id, workset, false, ct, deferVerification: true);
        return observed == null ? diagnosis : diagnosis with {CompletedDownloads = observed.Value.Receipts};
    }

    private async Task<(string Reason, string Signal, IReadOnlyDictionary<int, CompletedDownloadPlacement> Receipts)?> CompletedOriginalDownloads(int runId, DownloadWorkset workset,
        bool verifyFiles, CancellationToken ct, bool deferVerification = false)
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
            var receipts = new Dictionary<int, CompletedDownloadPlacement>();
            foreach (var group in selected.GroupBy(t => t.DownloadTaskId))
            {
                var target = group.First();
                CloudDownloadRecoveryService.EquivalentGroup? equivalent = null;
                if (group.Count() > 1)
                {
                    equivalent = downloads == null ? null : await downloads.ReadEquivalentGroupAsync(runId, target, workset.OutputDirectory, ct);
                    if (equivalent == null) return null;
                    target = equivalent.Owner;
                }
                // A selected retry can register one sibling before the other completed
                // downloads are observed. Allow that sibling's own registration slot only
                // when its checkpoint names the actual completed download artifact.
                var registered = equivalent == null && target.DownloadArtifactId != null;
                if (registered && (target.DownloadOperationId == null ||
                    await artifacts.GetOperationAsync(target.DownloadOperationId, ct) is not
                        {Kind: "download", State: ArtifactOperationState.Completed} operation ||
                    operation.OutputArtifactId != target.DownloadArtifactId ||
                    !await db.Artifacts.AsNoTracking().AnyAsync(a => a.Id == target.DownloadArtifactId &&
                        a.OperationId == operation.Id && a.Kind == "download" && a.State == ArtifactLocationState.Available, ct))) return null;
                var root = await DownloadExecutionRoot(runId, target, workset.OutputDirectory, ct, requireBinding: true,
                    allowRegisteredArtifact: registered, equivalentGroup: equivalent);
                storage.EnsurePathAllowed(root); ArtifactManifestFiles.RejectLinks(root);
                if (!Directory.Exists(root)) return null;
                if (deferVerification)
                {
                    var (_, receipt) = await InspectCompletedDownload(target.DownloadTaskId, root, ct);
                    if (receipt == null) return null;
                    receipts.Add(receipt.Result.DownloadTaskId, receipt.Placement);
                }
                else
                {
                    var reason = await CheckDownload(target.DownloadTaskId, root, verifyFiles, ct);
                    if (reason != null && !(reason == "RECOVERY_VERIFY_FILES" && !verifyFiles)) return null;
                }
            }
            return deferVerification
                ? ("RECOVERY_COMPLETED_DOWNLOAD_READY", JsonSerializer.Serialize(new
                    {ObserveExistingOnly = true, CompletedDownloadReceipts = receipts.ToDictionary(e => e.Key, e => e.Value.Key)}, WorkflowJson.Options), receipts)
                : (verifyFiles ? "RECOVERY_FILES_VERIFIED" : "RECOVERY_VERIFY_FILES", "{\"observeExistingOnly\":true}", receipts);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        { return null; }
    }

    internal async Task<string> DownloadExecutionRoot(int runId, CloudDownloadTarget target, string semanticRoot, CancellationToken ct,
        bool requireBinding = false, bool allowRegisteredArtifact = false,
        CloudDownloadRecoveryService.EquivalentGroup? equivalentGroup = null)
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
        var expectedSlots = (equivalentGroup?.Members ?? [target]).Select(t => CloudOperationService.OperationSlot(t, "download"))
            .ToHashSet(StringComparer.Ordinal);
        Require((equivalentGroup == null || equivalentGroup.OperationId == intent && equivalentGroup.Owner.Id == target.Id) &&
            bindings.Count == expectedSlots.Count && bindings.All(b => b.OwnerKind == "workflow" && b.OwnerId == runId.ToString() && expectedSlots.Contains(b.Slot)));
        var directory = CloudDownloadDestinations.ExecutionDirectory(request);
        var root = CloudDownloadDestinations.ExecutionRoot(request, semanticRoot);
        Require(ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(directory), Path.GetFullPath(download!.DownloadPath)));
        FileProcessingFiles.Within(directory, root);
        ArtifactManifestFiles.RejectLinks(directory);
        return root;
    }

    internal async Task<string?> CheckDownload(int? id, string outputRoot, bool verifyFiles, CancellationToken ct)
    {
        var (problem, receipt) = await InspectCompletedDownload(id, outputRoot, ct);
        if (receipt == null) return problem;
        if (!verifyFiles) return "RECOVERY_VERIFY_FILES";
        return await VerifyCompletedDownloadAsync(receipt, ct);
    }

    internal static async Task<string?> VerifyCompletedDownloadAsync(CompletedCloudDownloadReceipt receipt, CancellationToken ct,
        Action<ArtifactManifest>? onVerified = null)
    {
        var manifest = await ArtifactManifestFiles.CaptureAsync(receipt.Result.DownloadDirectory, receipt.Files, ct);
        if (manifest.Fingerprint != receipt.Result.Fingerprint) return "RECOVERY_FILES_UNVERIFIED";
        onVerified?.Invoke(manifest);
        return null;
    }

    internal async Task<(string? Problem, CompletedCloudDownloadReceipt? Receipt)> InspectCompletedDownload(int? id, string outputRoot, CancellationToken ct)
    {
        if (id == null) return ("RECOVERY_DOWNLOAD_REVIEW_REQUIRED", null);
        var download = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (download?.Status != DownloadTaskDbModelStatus.Complete) return ("RECOVERY_DOWNLOAD_REVIEW_REQUIRED", null);
        var result = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == id).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        if (result == null || string.IsNullOrWhiteSpace(result.Fingerprint)) return ("RECOVERY_FILES_UNVERIFIED", null);
        var files = JsonSerializer.Deserialize<string[]>(result.FilesJson, WorkflowJson.Options) ?? [];
        if (files.Length is 0 or > 100000) return ("RECOVERY_FILES_UNVERIFIED", null);
        if (!CloudDownloadPayloadRoot.TryValidate(download.DownloadPath, result.DownloadDirectory, files, out _))
            return ("RECOVERY_FILES_UNVERIFIED", null);
        FileProcessingFiles.Within(result.DownloadDirectory, outputRoot);
        storage.EnsurePathAllowed(result.DownloadDirectory);
        foreach (var file in files) storage.EnsurePathAllowed(file);
        return (null, new(result, download.DownloadPath, files));
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

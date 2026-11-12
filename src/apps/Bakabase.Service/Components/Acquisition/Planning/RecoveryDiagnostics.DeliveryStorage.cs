using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionRecoveryDiagnostics
{
    private async Task<AcquisitionRecoveryDiagnosis> DiagnoseDeliveryStorageAsync(AcquisitionTaskDbModel task,
        WorkflowRunDbModel run, WorkflowRunPlan plan, WorkflowRunPlanNode node, DownloadWorkset workset,
        bool verifyFiles, CancellationToken ct)
    {
        AcquisitionRecoveryDiagnosis Block(string code) => new(new(task.Id, task.Title, run.Id, false, null,
            code, "inspectFiles", CurrentStepKind: node.Kind));
        if (task.Status != AcquisitionStatus.Waiting || task.WorkflowRunId != run.Id || run.Status != WorkflowRunStatus.Waiting ||
            run.WaitReason != "cloud.fileProcessing" || !string.IsNullOrEmpty(run.ErrorMessage) ||
            string.IsNullOrWhiteSpace(run.WaitPromptJson) || run.WaitPromptJson.Length > 1024 * 1024 ||
            !AcquisitionLocalRecoveryBatchService.IsLocalRemainder(run)) return Block("RECOVERY_MANUAL_REVIEW");
        using var promptJson = JsonDocument.Parse(run.WaitPromptJson);
        var prompt = promptJson.RootElement;
        string? Read(string name) => prompt.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        var config = JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(node.ConfigJson, WorkflowJson.Options);
        var target = workset.Targets.SingleOrDefault(t => t.Id == config?.TargetId && !t.Excluded);
        var review = target?.DeliveryReview;
        var reserveLow = prompt.ValueKind == JsonValueKind.Object &&
            LocalStorageFailures.PersistedCode(Read("message")) == StorageWriteGuard.ReserveLowCode;
        var storageWait = review?.WaitReason == "DiskSpaceLimit" ||
            review?.WaitReason == "StorageUnavailable" && reserveLow;
        if (target == null || review is not {Accepted: false} || !storageWait ||
            review.ArtifactId != target.ProcessedArtifactId || target.ProcessingWaitReason != review.WaitReason ||
            target.Problem != review.WaitReason || string.IsNullOrWhiteSpace(review.PlanJson) || string.IsNullOrWhiteSpace(review.Intent))
            return Block("RECOVERY_MANUAL_REVIEW");
        var processingPlan = JsonSerializer.Deserialize<FileProcessingPlan>(review.PlanJson, WorkflowJson.Options);
        FileProcessingPlanValidator.Validate(processingPlan);
        if (processingPlan == null || processingPlan.Requirement == "unknown" ||
            processingPlan.Requirement == "required" && processingPlan.Steps.Count == 0)
            return Block("RECOVERY_MANUAL_REVIEW");
        if (review.AutomaticOuter && review.Limits is { } limits) RecursiveArchiveExecutor.ValidateOptions(limits);
        var token = AssessFileArtifactDeliveryActivity.ReviewToken(target.Id, review);
        if (prompt.ValueKind != JsonValueKind.Object || Read("targetId") != target.Id || Read("reason") != review.WaitReason ||
            Read("artifactId") != review.ArtifactId || Read("fingerprint") != review.Fingerprint || Read("reviewToken") != token ||
            !prompt.TryGetProperty("deliveryReview", out var marker) || marker.ValueKind != JsonValueKind.True)
            return Block("ACQUISITION_TASK_CHANGED");
        var owner = task.Id.ToString(CultureInfo.InvariantCulture);
        var revision = task.PlanRevision.ToString(CultureInfo.InvariantCulture);
        if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, run.Id, task.Id, ct) ||
            !await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().AnyAsync(s => s.WorkflowRunId == run.Id &&
                s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == owner && s.Revision == revision, ct) ||
            !workset.SourceRefs.Any(s => s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == owner && s.Revision == revision))
            return Block("ACQUISITION_TASK_CHANGED");
        var artifact = await artifacts.GetArtifactAsync(review.ArtifactId, ct);
        if (artifact == null || artifact.State != ArtifactLocationState.Available || artifact.Kind is not ("processedFiles" or "publishedFiles" or AcquisitionCurrentDirectoryFiles.Kind) ||
            artifact.Fingerprint != review.Fingerprint || artifact.Manifest.Fingerprint != review.Fingerprint || artifact.Manifest.Entries.Count == 0 ||
            !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
                b.OwnerId == run.Id.ToString() && b.OperationId == artifact.OperationId &&
                (b.Slot == target.Id + ":artifact.continuation" || b.Slot.StartsWith(target.Id + ":process:") ||
                 b.Slot == target.Id + ":adaptive:input:" + artifact.Id ||
                 artifact.Kind == "publishedFiles" && b.Slot == target.Id + ":artifact.publish"), ct))
            return Block("RECOVERY_FILES_UNVERIFIED");
        if (artifact.Kind == AcquisitionCurrentDirectoryFiles.Kind &&
            !await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, artifacts, artifact, task.Id, run.Id, target.Id, ct))
            return Block("RECOVERY_FILES_UNVERIFIED");
        var operation = await artifacts.GetOperationAsync(artifact.OperationId, ct);
        if (operation?.State != ArtifactOperationState.Completed || operation.OutputArtifactId != artifact.Id ||
            operation.Kind != (artifact.Kind == "publishedFiles" ? "files.publish" : artifact.Kind == AcquisitionCurrentDirectoryFiles.Kind
                ? AcquisitionCurrentDirectoryFiles.OperationKind : "files.process"))
            return Block("RECOVERY_FILES_UNVERIFIED");
        var processingConfig = new FileArtifactProcessingConfig {ArchivePolicy = review.AutomaticOuter ? "autoDetect" : "instructions",
            Recursive = review.Limits ?? new(), StoragePolicy = target.StoragePolicy, StorageTitle = target.Title,
            ExtractionLayout = review.ExtractionLayout, WorkspaceDirectoryScope = target.ProcessingDirectoryScope};
        FileArtifactProcessor.Validate(processingConfig);
        var key = FileArtifactProcessor.OperationKey(review.Intent, artifact,
            FileArtifactProcessor.WithDefaultPasswords(processingPlan, target.SelectedSource?.ArchivePassword),
            target.SelectedSource?.ArchivePassword, processingConfig);
        var processing = await artifacts.GetOperationAsync(key, ct);
        var adaptivePlan = FileArtifactDeliveryStorageContinuation.IsAdaptivePlan(processingPlan);
        var adaptiveCheckpoint = adaptivePlan ? await FileArtifactDeliveryStorageContinuation.ReadAsync(db, artifacts, workspaces,
            run.Id, target, artifact, review, config!, processingPlan, processingConfig, ct) : null;
        if (adaptivePlan ? adaptiveCheckpoint == null : processing is not {State: ArtifactOperationState.Running, Restartable: true, Attempt: > 0, OutputArtifactId: null} ||
            processing.Kind != "files.process" || processing.IntentKey != review.Intent || processing.InputFingerprint != artifact.Fingerprint ||
            processing.LeaseOwner != $"workflow:{run.Id}:delivery-review:{target.Id}" ||
            !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" && b.OwnerId == run.Id.ToString() &&
                b.OperationId == artifact.OperationId && b.Slot == target.Id + ":process:delivery-input:" + artifact.Id, ct))
            return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");
        if (!verifyFiles) return Block("RECOVERY_VERIFY_FILES");
        if (artifact.Kind == AcquisitionCurrentDirectoryFiles.Kind)
        {
            try { await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(artifact, workspaces, ct); }
            catch (Exception error) when (error is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException or JsonException)
            { return Block("RECOVERY_FILES_UNVERIFIED"); }
        }
        if (!await ValidArtifact(artifact.Id, ct)) return Block("RECOVERY_FILES_UNVERIFIED");
        // The typed answer changes no plan, password, recursion budget, storage policy or generation.
        // The activity validates the same review token again and re-enters its retained operation.
        var signal = new FileArtifactDeliverySignal(artifact.Id, artifact.Fingerprint, token,
            new CloudFileProcessingSignal {TargetId = target.Id});
        return new(new(task.Id, task.Title, run.Id, true, "resume", "RECOVERY_DELIVERY_STORAGE_READY",
            CurrentStepKind: node.Kind, CurrentProblemCode: "LOCAL_STORAGE_FULL"),
            JsonSerializer.Serialize(signal, WorkflowJson.Options));
    }
}

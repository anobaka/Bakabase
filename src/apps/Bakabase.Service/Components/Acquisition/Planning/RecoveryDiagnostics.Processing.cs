using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
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
    private sealed record ProcessingStoragePrompt(string? TargetId, string? Reason, string? Message,
        string? ExtractionPlanJson, string? KnownStepId);

    private async Task<AcquisitionRecoveryDiagnosis> DiagnoseProcessingStorageAsync(AcquisitionTaskDbModel task,
        WorkflowRunDbModel run, WorkflowRunPlan plan, WorkflowRunPlanNode node, DownloadWorkset workset,
        bool verifyFiles, bool verifyContent, CancellationToken ct)
    {
        string? promptTarget = null;
        string? problemCode = null;
        FileArtifactProcessingRecovery? expectedProcessing = null;
        string[] workingRoots = [];
        string? placementStamp = null;
        AcquisitionRecoveryDiagnosis Result(string reason, bool ready = false, string manual = "inspectFiles") =>
            new(new(task.Id, task.Title, run.Id, ready, ready ? "resume" : null, reason, ready ? null : manual,
                new DateTimeOffset((run.WaitingSince ?? run.StartedAt).ToUniversalTime()), node.Kind,
                problemCode), ready
                ? JsonSerializer.Serialize(new CloudFileProcessingSignal {TargetId = promptTarget!, StorageRecovery = expectedProcessing}, WorkflowJson.Options) : "{}")
                {ProcessingPartId = promptTarget, ProcessingStorageRoots = workingRoots, ProcessingPlacementStamp = placementStamp};
        // This is a local, typed continuation, never a retry of a failed run or a new generation.
        if (task.Status != AcquisitionStatus.Waiting || task.WorkflowRunId != run.Id || task.PlanRevision < 1 ||
            run.Status != WorkflowRunStatus.Waiting || run.WaitReason != "cloud.fileProcessing" ||
            !string.IsNullOrWhiteSpace(run.ErrorMessage) || string.IsNullOrWhiteSpace(run.WaitPromptJson) ||
            run.WaitPromptJson.Length > 1024 * 1024)
            return Result("RECOVERY_MANUAL_REVIEW", manual: "reviewTask");
        if (plan.Nodes.Skip(run.CurrentStepIndex!.Value).Any(n =>
                n.Kind is not (AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts" or "download.cleanupArtifacts")))
            return Result("RECOVERY_NODE_UNSUPPORTED", manual: "reviewTask");
        var prompt = JsonSerializer.Deserialize<ProcessingStoragePrompt>(run.WaitPromptJson, WorkflowJson.Options);
        var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(node.ConfigJson, WorkflowJson.Options);
        if (prompt?.Reason != "StorageUnavailable" || config == null || string.IsNullOrWhiteSpace(config.TargetId) ||
            prompt.TargetId != config.TargetId || prompt.KnownStepId != config.StopAfterStepId)
            return Result("RECOVERY_MANUAL_REVIEW", manual: "reviewTask");
        problemCode = "RECOVERY_STORAGE_UNAVAILABLE";
        promptTarget = config.TargetId;
        var targets = workset.Targets.Where(t => t.Id == config.TargetId).ToArray();
        if (targets.Length != 1 || targets[0].Excluded || targets[0].ProcessingWaitReason != "StorageUnavailable" ||
            targets[0].Problem != "StorageUnavailable" || targets[0].PublishedArtifactId != null)
            return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
        var target = targets[0];
        var effective = AcquisitionPlanProcessingActivity.EffectivePlan(config, target);
        FileProcessingPlanValidator.Validate(effective);
        if (effective.Requirement == "unknown" || effective.Requirement == "required" && effective.Steps.Count == 0 ||
            config.StopAfterStepId != null && !effective.Steps.Any(s => s.Id == config.StopAfterStepId) ||
            string.IsNullOrWhiteSpace(prompt.ExtractionPlanJson) ||
            JsonSerializer.Serialize(JsonSerializer.Deserialize<FileProcessingPlan>(prompt.ExtractionPlanJson, WorkflowJson.Options), WorkflowJson.Options) !=
            JsonSerializer.Serialize(effective, WorkflowJson.Options))
            return Result("RECOVERY_MANUAL_REVIEW", manual: "reviewTask");
        var owner = task.Id.ToString(CultureInfo.InvariantCulture);
        var revision = task.PlanRevision.ToString(CultureInfo.InvariantCulture);
        if (!await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().AnyAsync(s => s.WorkflowRunId == run.Id &&
                s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == owner && s.Revision == revision, ct) ||
            !workset.SourceRefs.Any(s => s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == owner && s.Revision == revision))
            return Result("ACQUISITION_TASK_CHANGED", manual: "reviewTask");

        ArtifactRecord? input;
        string inputSlot;
        if (config.ContinueFrom is { } continuation)
        {
            if (continuation.TaskId != task.Id) return Result("RECOVERY_FILES_UNVERIFIED");
            input = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, task.Id, target.Id, continuation, ct);
            inputSlot = target.Id + ":artifact.continuation";
        }
        else
        {
            input = target.DownloadArtifactId == null ? null : await artifacts.GetArtifactAsync(target.DownloadArtifactId, ct);
            inputSlot = target.Id + ":artifact.download";
            var download = input == null ? null : await artifacts.GetOperationAsync(input.OperationId, ct);
            if (input?.Kind != "download" || target.DownloadOperationId != input.OperationId ||
                download?.Kind != "download" || download.IntentKey != DownloadArtifactCoordinator.DownloadIntent(target, workset.OutputDirectory, workspaces) ||
                download.InputFingerprint != DownloadArtifactCoordinator.Source(target) ||
                target.DownloadDirectory == null || !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(target.DownloadDirectory), input.RootDirectory) ||
                !target.Files.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(input.Files))
                return Result("RECOVERY_FILES_UNVERIFIED");
        }
        if (input == null || input.State != ArtifactLocationState.Available || input.Manifest.Entries.Count is 0 or > 100000 ||
            input.Fingerprint != input.Manifest.Fingerprint || !await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().AnyAsync(b =>
                b.OwnerKind == "workflow" && b.OwnerId == run.Id.ToString() && b.Slot == inputSlot && b.OperationId == input.OperationId, ct))
            return Result("RECOVERY_FILES_UNVERIFIED");
        var completedInput = await artifacts.GetOperationAsync(input.OperationId, ct);
        if (completedInput?.State != ArtifactOperationState.Completed || completedInput.OutputArtifactId != input.Id)
            return Result("RECOVERY_FILES_UNVERIFIED");
        var intent = AcquisitionPlanProcessingActivity.Intent(workset, target, effective,
            config.StopAfterStepId ?? effective.Steps.LastOrDefault()?.Id, workspaces, config.ContinueFrom);
        var resolved = FileArtifactProcessor.WithDefaultPasswords(effective, target.SelectedSource?.ArchivePassword);
        var processingConfig = new FileArtifactProcessingConfig
            {StoragePolicy = config.StoragePolicy ?? target.StoragePolicy, StorageTitle = target.Title,
                WorkspaceDirectoryScope = target.ProcessingDirectoryScope};
        var operationId = FileArtifactProcessor.OperationKey(intent, input, resolved, target.SelectedSource?.ArchivePassword, processingConfig);
        var operation = await artifacts.GetOperationAsync(operationId, ct);
        // A generic retry would increment Failed/Completed generations. Only the exact suspended
        // restartable attempt owned by this run may be re-entered by this capability.
        if (operation is not {State: ArtifactOperationState.Running, Restartable: true, Attempt: > 0, OutputArtifactId: null} ||
            operation.Kind != "files.process" || operation.InputFingerprint != input.Fingerprint || operation.IntentKey != intent ||
            operation.LeaseOwner != $"workflow:{run.Id}:{target.Id}" || workspaces == null)
            return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
        expectedProcessing = new(operation.Id, operation.Attempt);
        var location = await workspaces.InspectExistingAsync("operations", operationId, operation.Attempt, ct);
        if (location == null) return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
        var checkpointLocation = config.StopAfterStepId == null ?
            new FileProcessingWorkspaceResolver.CheckpointInspection(Path.Combine(location.Directory, "plan"), location.StorageRoot) :
            await workspaces.InspectCheckpointAsync(FileArtifactProcessor.CheckpointKey(input, resolved), operationId, operation.Attempt, input, resolved,
                processingConfig.WorkspaceDirectoryScope?.Directory ?? processingConfig.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
        if (checkpointLocation == null) return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
        workingRoots = new[] {location.StorageRoot, checkpointLocation.StorageRoot}.Distinct(ArtifactManifestFiles.PathComparer).ToArray();
        placementStamp = AcquisitionLocalFilesRecoveryReceipt.Hash(new {operation.Id, operation.Attempt, location, checkpointLocation});
        if (!verifyFiles) return Result("RECOVERY_VERIFY_FILES");

        storage.EnsurePathAllowed(input.RootDirectory);
        if (!Directory.Exists(input.RootDirectory))
            return Result("RECOVERY_STORAGE_UNAVAILABLE", manual: "checkStorage");
        if (config.ContinueFrom == null)
        {
            storage.EnsurePathAllowed(workset.OutputDirectory);
            if (!Directory.Exists(workset.OutputDirectory))
                return Result("RECOVERY_STORAGE_UNAVAILABLE", manual: "checkStorage");
        }
        else if (!ValidateContinuationProcessingPaths(plan, run.CurrentStepIndex.Value, workset, target, processingConfig, workspaces))
            return Result("RECOVERY_STORAGE_UNAVAILABLE", manual: "checkStorage");
        var files = ArtifactManifestFiles.ResolveFiles(input.RootDirectory, input.Manifest);
        for (var index = 0; index < files.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            storage.EnsurePathAllowed(files[index]);
            ArtifactManifestFiles.RejectLinks(files[index]);
            // No payload reads in a preview. Open handles expose uncached SMB metadata and
            // sharing violations; compare length and mtime while the handle remains open.
            using var stream = new FileStream(files[index], FileMode.Open, FileAccess.Read, FileShare.Read);
            var length = RandomAccess.GetLength(stream.SafeFileHandle);
            var written = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
            var current = new FileInfo(files[index]);
            if (length != input.Manifest.Entries[index].Length || current.Length != length || current.LastWriteTimeUtc != written ||
                RandomAccess.GetLength(stream.SafeFileHandle) != length || File.GetLastWriteTimeUtc(stream.SafeFileHandle) != written)
                return Result("RECOVERY_FILES_UNVERIFIED");
        }
        // Apply calls this again under the owner-task/run gates with a bounded full validation.
        // The resumed processor also retains its existing ResolveById content validation.
        if (verifyContent && !(await ArtifactManifestFiles.ValidateAsync(input.RootDirectory, input.Manifest, ct)).Available)
            return Result("RECOVERY_FILES_UNVERIFIED");
        var checkpointRoot = checkpointLocation.Directory;
        var normalized = files.OrderBy(p => p, ArtifactManifestFiles.PathComparer).ToArray();
        var checkpointIdentity = FileProcessingPlanExecutor.CheckpointIdentity(resolved, normalized, workspaces.IdentityPath);
        var directories = ArtifactManifestFiles.ResolveDirectories(input.RootDirectory, input.Manifest)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (directories.Length > 0)
            checkpointIdentity = ArtifactManifestFiles.Hash(checkpointIdentity + "\ndirectories-v1\n" + JsonSerializer.Serialize(
                directories.Select(p => Path.GetRelativePath(input.RootDirectory, p).Replace('\\', '/')).ToArray()));
        var checkpoint = Path.Combine(checkpointRoot, checkpointIdentity);
        ArtifactManifestFiles.RejectLinks(checkpoint);
        var stateFile = Path.Combine(checkpoint, "state.json");
        ArtifactManifestFiles.RejectLinks(stateFile);
        if (!File.Exists(stateFile) && LocalStorageFailures.PersistedCode(prompt.Message) == StorageWriteGuard.ReserveLowCode &&
            (config.StopAfterStepId == null || resolved.Steps.FirstOrDefault()?.Id == config.StopAfterStepId) &&
            target.ProcessedArtifactId == null && target.ProcessingOperationId == null && target.ProcessingIntentKey == null &&
            target.ProcessedDirectory == null && target.ProcessedFiles.Count == 0 && target.ProcessingStepArtifacts.Count == 0 &&
            await workspaces.IsUnstartedCheckpointAsync(FileArtifactProcessor.CheckpointKey(input, resolved), operationId,
                operation.Attempt, input, resolved, processingConfig.WorkspaceDirectoryScope?.Directory ?? processingConfig.StoragePolicy?.ProcessingWorkspaceDirectory,
                config.StopAfterStepId == null, ct))
        {
            // The operation and owner were checked above; apply additionally hashed the input.
            // Resume that same attempt so admission runs again without creating a new download.
            return Result("RECOVERY_FILES_VERIFIED", true);
        }
        if (!Directory.Exists(checkpoint) || !File.Exists(stateFile) || new FileInfo(stateFile).Length > 8 * 1024 * 1024)
            return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile, ct), WorkflowJson.Options);
        if (state?.Outputs == null || state.Active == null || state.Operations == null || state.PendingOperation != null)
            return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
        if (new DriveInfo(checkpointLocation!.StorageRoot).AvailableFreeSpace <= 0)
            return Result("RECOVERY_STORAGE_UNAVAILABLE", manual: "checkStorage");
        if (prompt.Message?.Contains("being used by another process", StringComparison.Ordinal) == true)
        {
            // Compatibility is deliberately restricted to the old .NET sharing-violation
            // sentence. Never open a path borrowed from an arbitrary provider error.
            const string prefix = "The process cannot access the file '";
            const string suffix = "' because it is being used by another process.";
            if (!prompt.Message.StartsWith(prefix, StringComparison.Ordinal) || !prompt.Message.EndsWith(suffix, StringComparison.Ordinal) ||
                prompt.Message.Length <= prefix.Length + suffix.Length || prompt.Message.Length > 16 * 1024)
                return Result("RECOVERY_CHECKPOINT_UNSUPPORTED");
            // Keep the historical error intact, but resolve its file through the same audited
            // relocation map as the checkpoint. The current checkpoint boundary still applies.
            var blocked = FileProcessingFiles.Within(
                workspaces.Relocate(prompt.Message[prefix.Length..^suffix.Length]), checkpoint);
            ArtifactManifestFiles.RejectLinks(blocked);
            using var exclusive = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        return Result("RECOVERY_FILES_VERIFIED", true);
    }

    private bool ValidateContinuationProcessingPaths(WorkflowRunPlan plan, int currentStep, DownloadWorkset workset,
        CloudDownloadTarget target, FileArtifactProcessingConfig config, FileProcessingWorkspaceResolver workspaces)
    {
        // ContinueFrom retains the download root as semantic history. Its verified artifact,
        // pinned workspace locators, and frozen delivery config own the actual file locations.
        if ((config.WorkspaceDirectoryScope?.Directory ?? config.StoragePolicy?.ProcessingWorkspaceDirectory) is {Length: > 0} configuredRoot)
        {
            var processingRoot = workspaces.Relocate(configuredRoot);
            storage.EnsurePathAllowed(processingRoot);
            ArtifactManifestFiles.RejectLinks(processingRoot);
            if (!Directory.Exists(processingRoot)) return false;
        }
        foreach (var node in plan.Nodes.Skip(currentStep + 1).Where(n => n.Kind == "fs.publishArtifacts"))
        {
            var publication = JsonSerializer.Deserialize<FileArtifactPublicationConfig>(node.ConfigJson, WorkflowJson.Options)
                ?? throw new InvalidOperationException("RECOVERY_CHECKPOINT_UNSUPPORTED");
            if (string.IsNullOrWhiteSpace(publication.TargetDirectory) && publication.TargetDirectories == null)
            {
                // Legacy publication really will use this root; it is not just source history.
                storage.EnsurePathAllowed(workset.OutputDirectory);
                if (!Directory.Exists(workset.OutputDirectory)) return false;
            }
            var destination = PublishFileArtifactsActivity.CloudTargetDirectory(workset, target, publication);
            storage.EnsurePathAllowed(destination);
            ArtifactManifestFiles.RejectLinks(destination);
            // A new delivery leaf is normal. Its existing ancestor must still be available
            // user storage; never make a missing mount look like an empty destination.
            var parent = new DirectoryInfo(destination);
            while (!parent.Exists)
            {
                if (File.Exists(parent.FullName) || parent.Parent == null) return false;
                parent = parent.Parent;
            }
            storage.EnsurePathAllowed(parent.FullName);
            ArtifactManifestFiles.RejectLinks(parent.FullName);
        }
        return true;
    }

}

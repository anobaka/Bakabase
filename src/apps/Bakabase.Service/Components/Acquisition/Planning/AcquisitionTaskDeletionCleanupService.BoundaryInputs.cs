using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    private sealed record RecordedContinuation(ArtifactRecord Input, string Evidence);
    private sealed record AbandonedBoundary(FileProcessingWorkspaceResolver.Location Location, string Directory, bool Shared,
        RecordedContinuation? Historical = null);

    // Boundary storage is keyed by input and recipe, separately from an operation's
    // private workspace. Only the exact suspended step and an exclusive input may release it.
    private async Task<AbandonedBoundary?> ReadAbandonedBoundaryAsync(ArtifactOperationDbModel operation,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        if (!authorization.Consent.AbandonUnfinishedFiles && !authorization.Consent.WholeTaskFiles || workspaces == null ||
            AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(operation.LeaseOwner) is not { } owner ||
            !authorization.Consent.OwnerRunIds.Contains(owner)) return null;
        var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == owner, ct);
        if (run?.CurrentStepIndex is not >= 0 || run.PendingSignalJson != null || run.WaitPromptJson?.Length > 1048576) return null;
        var nodes = WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes;
        var node = nodes?.ElementAtOrDefault(run.CurrentStepIndex.Value);
        if (node?.Kind != AcquisitionPlanProcessingActivity.ActivityKind) return null;
        var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(node.ConfigJson, WorkflowJson.Options);
        if (config?.StopAfterStepId == null) return null;
        var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson);
        var target = workset?.Targets.SingleOrDefault(t => t.Id == config.TargetId);
        if (target == null) return null;
        ArtifactRecord? input = null;
        RecordedContinuation? historical = null;
        if (run.WaitPromptJson is {Length: > 0} wait)
        {
            if (run.WaitReason != "cloud.fileProcessing") return null;
            using var prompt = JsonDocument.Parse(wait);
            if (!prompt.RootElement.TryGetProperty("targetId", out var targetId) || targetId.GetString() != config.TargetId ||
                !prompt.RootElement.TryGetProperty("knownStepId", out var stepId) || stepId.GetString() != config.StopAfterStepId) return null;
            if (prompt.RootElement.TryGetProperty("artifactId", out var id) && id.ValueKind != JsonValueKind.Null)
            {
                if (id.ValueKind != JsonValueKind.String) return null;
                input = await artifacts.GetArtifactAsync(id.GetString()!, ct);
                if (input == null) return null;
            }
        }
        if (input == null)
        {
            if (run.Status is not (WorkflowRunStatus.Success or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Cancelled) ||
                run.CurrentStepIndex == 0 || run.WaitReason != null && string.IsNullOrEmpty(run.WaitPromptJson) ||
                !await authorization.IsCurrentAsync(ct) || workset == null ||
                !await db.AcquisitionTasks.AsNoTracking().AnyAsync(t => t.Id == authorization.Consent.TaskId && t.DeletedAt != null, ct)) return null;
            var prior = nodes![run.CurrentStepIndex.Value - 1];
            historical = await ReadRecordedContinuationAsync(run, prior, config, authorization.Consent, ct);
            input = historical?.Input;
            if (input == null || AcquisitionPlanProcessingActivity.Intent(workset, target,
                AcquisitionPlanProcessingActivity.EffectivePlan(config, target), config.StopAfterStepId, workspaces, config.ContinueFrom) != operation.IntentKey) return null;
        }
        if (input.Fingerprint != operation.InputFingerprint) return null;
        var password = target.SelectedSource?.ArchivePassword;
        var processing = new FileArtifactProcessingConfig {StoragePolicy = config.StoragePolicy ?? target.StoragePolicy,
            StorageTitle = target.Title, WorkspaceDirectoryScope = target.ProcessingDirectoryScope};
        var resolved = FileExtractionLayouts.Apply(FileArtifactProcessor.WithDefaultPasswords(
            AcquisitionPlanProcessingActivity.EffectivePlan(config, target), password), processing.ExtractionLayout);
        if (FileArtifactProcessor.OperationKey(operation.IntentKey, input, resolved, password, processing) != operation.Id) return null;

        var key = FileArtifactProcessor.CheckpointKey(input, resolved);
        var configured = processing.WorkspaceDirectoryScope?.Directory ?? processing.StoragePolicy?.ProcessingWorkspaceDirectory;
        var location = historical != null ? await workspaces.InspectOwnedCheckpointForStageCleanupAsync(key, operation.Id, operation.Attempt, configured, ct)
            : await workspaces.InspectCompletedCheckpointAsync(key, operation.Id, operation.Attempt, configured, ct);
        if (location == null || !await workspaces.HasOwnedLocationAsync(location, ct)) return null;
        var directory = FileProcessingPlanExecutor.CheckpointInputDirectory(location.Directory, resolved, input, workspaces.IdentityPath);
        ArtifactManifestFiles.RejectLinks(directory);
        if (!Directory.Exists(directory)) return null;

        // Two different file-processing operations can use the same boundary locator.
        // Other attempts can point to it only with the same input fingerprint/attempt.
        var siblings = await db.ArtifactOperations.AsNoTracking().Where(o => o.Id != operation.Id && o.Kind == "files.process" &&
            o.InputFingerprint == operation.InputFingerprint && o.Attempt == operation.Attempt).ToListAsync(ct);
        var owners = authorization.Consent.OwnerRunIds.Select(r => r.ToString(CultureInfo.InvariantCulture)).ToArray();
        foreach (var sibling in siblings)
        {
            if (historical != null && await db.ArtifactConsumers.AsNoTracking().AnyAsync(c => c.OperationId == sibling.Id &&
                c.ReleasedAt == null && (c.OwnerKind != "workflow" || !owners.Contains(c.OwnerId)), ct))
                return new(location, directory, true);
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == sibling.Id).ToListAsync(ct);
            var claim = AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(sibling.LeaseOwner);
            if (bindings.Any(b => b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId)) ||
                (claim != null && !authorization.Consent.OwnerRunIds.Contains(claim.Value)) || bindings.Count == 0 && claim == null ||
                sibling.LeaseUntil > DateTime.UtcNow || await authorization.HasActiveConsumersAsync(sibling.Id, ct)) return new(location, directory, true);
        }
        return new(location, directory, false, historical);
    }

    private async Task<RecordedContinuation?> ReadRecordedContinuationAsync(WorkflowRunDbModel run, WorkflowRunPlanNode predecessor,
        AcquisitionPlanProcessingConfig config, ArtifactDeliveryCleanupConsent consent, CancellationToken ct)
    {
        var selected = config.ContinueFrom;
        if (selected == null || selected.TaskId != consent.TaskId || !consent.OwnerRunIds.Contains(selected.PreviousRunId) ||
            predecessor.Kind != AcquisitionPlanContinueFilesActivity.ActivityKind || selected.PreviousRunId == run.Id ||
            !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, run.Id, consent.TaskId, ct) ||
            !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, selected.PreviousRunId, consent.TaskId, ct)) return null;
        var initial = JsonSerializer.Deserialize<AcquisitionPlanContinueFilesConfig>(predecessor.ConfigJson, WorkflowJson.Options);
        if (initial?.Targets?.SingleOrDefault(t => t.Target.Id == config.TargetId)?.Input != selected) return null;
        var invocations = await db.WorkflowNodeExecutions.AsNoTracking().Where(n => n.WorkflowRunId == run.Id &&
            n.StepIndex == run.CurrentStepIndex - 1 && n.NodeId == predecessor.NodeId && n.Kind == predecessor.Kind)
            .Select(n => new {n.Id, n.Attempt, n.Status, n.CompletedAt, n.OutputsTruncated, n.OutputsJson}).Take(2).ToListAsync(ct);
        if (invocations.Count != 1 || invocations[0] is not {Status: "succeeded", CompletedAt: not null, OutputsTruncated: false,
            OutputsJson.Length: > 0 and <= 1048576} receipt) return null;
        var blocks = JsonSerializer.Deserialize<WorkflowOutputBlock[]>(receipt.OutputsJson, WorkflowJson.Options);
        var output = blocks?.SingleOrDefault(v => v.Key == config.TargetId + ":continuation");
        if (output is not {Kind: "artifact", State: "reused"} || output.ArtifactId != selected.ArtifactId ||
            output.OperationId != selected.OperationId || string.IsNullOrWhiteSpace(output.Directory) || !Path.IsPathFullyQualified(output.Directory)) return null;
        var record = await artifacts.GetArtifactAsync(selected.ArtifactId, ct);
        if (record == null || record.OperationId != selected.OperationId || record.Fingerprint != selected.Fingerprint ||
            record.Manifest.Entries.Count != output.FileCount || record.Manifest.Entries.Sum(x => x.Length) != output.TotalBytes ||
            !await AcquisitionPlanArtifactInputGuard.HasContinuationBindingAsync(db, run.Id, config.TargetId, record.OperationId, ct)) return null;
        var previousOwner = selected.PreviousRunId.ToString(CultureInfo.InvariantCulture);
        if (!await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == record.OperationId &&
            b.OwnerKind == "workflow" && b.OwnerId == previousOwner && b.Slot.StartsWith(config.TargetId + ":"), ct)) return null;
        var origin = record.State == ArtifactLocationState.Retired ? await RetiredStagedInputRootAsync(record, ct) :
            record.State == ArtifactLocationState.Available ? record.RootDirectory : null;
        if (origin == null || !SamePath(workspaces!.Relocate(output.Directory), workspaces.Relocate(origin))) return null;
        // The old path is immutable operation-key evidence. Cleanup never enumerates this directory.
        var historical = record with {RootDirectory = output.Directory, Files = ArtifactManifestFiles.ResolveFiles(output.Directory, record.Manifest)};
        var evidence = ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {run.Id, run.CurrentStepIndex, run.WaitReason,
            run.WaitPromptJson, predecessor.NodeId, predecessor.ConfigJson, config, receipt, origin}));
        return new(historical, evidence);
    }
}

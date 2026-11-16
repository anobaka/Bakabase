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
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    private sealed record AbandonedBoundary(FileProcessingWorkspaceResolver.Location Location, string Directory, bool Shared);

    // Boundary storage is keyed by input and recipe, separately from an operation's
    // private workspace. Only the exact suspended step and an exclusive input may release it.
    private async Task<AbandonedBoundary?> ReadAbandonedBoundaryAsync(ArtifactOperationDbModel operation,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        if (!authorization.Consent.AbandonUnfinishedFiles && !authorization.Consent.WholeTaskFiles || workspaces == null ||
            AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(operation.LeaseOwner) is not { } owner ||
            !authorization.Consent.OwnerRunIds.Contains(owner)) return null;
        var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == owner, ct);
        if (run?.WaitReason != "cloud.fileProcessing" || run.CurrentStepIndex is not >= 0 ||
            run.WaitPromptJson is not {Length: > 0 and <= 1048576} || run.PendingSignalJson != null) return null;
        var node = WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex.Value);
        if (node?.Kind != AcquisitionPlanProcessingActivity.ActivityKind) return null;
        var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(node.ConfigJson, WorkflowJson.Options);
        if (config?.StopAfterStepId == null) return null;
        using var prompt = JsonDocument.Parse(run.WaitPromptJson);
        if (!prompt.RootElement.TryGetProperty("artifactId", out var id) || id.ValueKind != JsonValueKind.String ||
            !prompt.RootElement.TryGetProperty("targetId", out var targetId) || targetId.GetString() != config.TargetId ||
            !prompt.RootElement.TryGetProperty("knownStepId", out var stepId) || stepId.GetString() != config.StopAfterStepId) return null;
        var input = await artifacts.GetArtifactAsync(id.GetString()!, ct);
        var target = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)?.Targets.SingleOrDefault(t => t.Id == config.TargetId);
        if (input == null || input.Fingerprint != operation.InputFingerprint || target == null) return null;
        var password = target.SelectedSource?.ArchivePassword;
        var processing = new FileArtifactProcessingConfig {StoragePolicy = config.StoragePolicy ?? target.StoragePolicy,
            StorageTitle = target.Title, WorkspaceDirectoryScope = target.ProcessingDirectoryScope};
        var resolved = FileExtractionLayouts.Apply(FileArtifactProcessor.WithDefaultPasswords(
            AcquisitionPlanProcessingActivity.EffectivePlan(config, target), password), processing.ExtractionLayout);
        if (FileArtifactProcessor.OperationKey(operation.IntentKey, input, resolved, password, processing) != operation.Id) return null;

        var location = await workspaces.InspectCompletedCheckpointAsync(FileArtifactProcessor.CheckpointKey(input, resolved),
            operation.Id, operation.Attempt, processing.WorkspaceDirectoryScope?.Directory ?? processing.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
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
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == sibling.Id).ToListAsync(ct);
            var claim = AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(sibling.LeaseOwner);
            if (bindings.Any(b => b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId)) ||
                (claim != null && !authorization.Consent.OwnerRunIds.Contains(claim.Value)) || bindings.Count == 0 && claim == null ||
                sibling.LeaseUntil > DateTime.UtcNow || await authorization.HasActiveConsumersAsync(sibling.Id, ct)) return new(location, directory, true);
        }
        return new(location, directory, false);
    }
}

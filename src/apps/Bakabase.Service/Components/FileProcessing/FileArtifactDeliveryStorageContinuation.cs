using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Re-enter a saved adaptive plan without asking the model again or allocating another attempt.</summary>
internal static class FileArtifactDeliveryStorageContinuation
{
    internal sealed record Checkpoint(string OperationId, int Attempt, string Consumer, string Directory, string StorageRoot, int Iteration);

    internal static bool IsAdaptivePlan(FileProcessingPlan plan) => plan.Evidence?.Contains("adaptive-file-processing") == true;

    internal static async Task<Checkpoint?> ReadAsync(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingWorkspaceResolver? locations, long runId, CloudDownloadTarget target, ArtifactRecord input,
        FileArtifactDeliveryReview review, FileArtifactDeliveryAssessmentConfig config, FileProcessingPlan plan,
        FileArtifactProcessingConfig processingConfig, CancellationToken ct)
    {
        if (!config.EnableAdaptiveAssessment || locations == null || !IsAdaptivePlan(plan) ||
            review.AutomaticOuter || review.ArtifactId != input.Id || review.Fingerprint != input.Fingerprint ||
            string.IsNullOrWhiteSpace(review.PlanJson) || string.IsNullOrWhiteSpace(review.Intent) ||
            plan.Evidence.Count != 2 || string.IsNullOrWhiteSpace(plan.Evidence[1])) return null;
        var expectedIntent = FileArtifactProcessor.Intent(
            "adaptive-file-processing-v1:" + input.OperationId + ":" + input.Fingerprint + ":" + plan.Evidence[1],
            review.PlanJson, target.SelectedSource?.ArchivePassword, processingConfig);
        if (review.Intent != expectedIntent) return null;
        var resolved = FileExtractionLayouts.Apply(FileArtifactProcessor.WithDefaultPasswords(plan,
            target.SelectedSource?.ArchivePassword), processingConfig.ExtractionLayout);
        var key = FileArtifactProcessor.OperationKey(review.Intent, input, resolved, target.SelectedSource?.ArchivePassword, processingConfig);
        var operation = await store.GetOperationAsync(key, ct);
        if (operation is not {State: ArtifactOperationState.Running, Restartable: true, Attempt: > 0, OutputArtifactId: null} ||
            operation.Kind != "files.process" || operation.IntentKey != review.Intent || operation.InputFingerprint != input.Fingerprint)
            return null;
        var prefix = $"workflow:{runId}:adaptive:{target.Id}:";
        var owner = operation.LeaseOwner;
        if (owner == null || !owner.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(owner[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var iteration) ||
            iteration < 0 || iteration >= Math.Clamp(config.MaxAdaptiveIterations, 1, AdaptiveFileProcessingAssessmentService.MaxCallsPerTask) ||
            owner != prefix + iteration.ToString(CultureInfo.InvariantCulture) ||
            !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" && b.OwnerId == runId.ToString() &&
                b.OperationId == input.OperationId && b.Slot == target.Id + ":adaptive:input:" + input.Id, ct)) return null;
        var location = await locations.InspectExistingAsync("operations", key, operation.Attempt, ct);
        if (location == null) return null;
        var files = ArtifactManifestFiles.ResolveFiles(input.RootDirectory, input.Manifest).OrderBy(p => p, FileProcessingFiles.PathComparer).ToArray();
        var identity = FileProcessingPlanExecutor.CheckpointIdentity(resolved, files, locations.IdentityPath);
        var directories = ArtifactManifestFiles.ResolveDirectories(input.RootDirectory, input.Manifest).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (directories.Length > 0)
            identity = ArtifactManifestFiles.Hash(identity + "\ndirectories-v1\n" + JsonSerializer.Serialize(
                directories.Select(p => Path.GetRelativePath(input.RootDirectory, p).Replace('\\', '/')).ToArray()));
        var directory = Path.Combine(location.Directory, "plan", identity);
        var statePath = Path.Combine(directory, "state.json");
        ArtifactManifestFiles.RejectLinks(statePath);
        if (!File.Exists(statePath) || new FileInfo(statePath).Length > 8 * 1024 * 1024) return null;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, ct));
        if (!document.RootElement.TryGetProperty("inputManifest", out var manifest) ||
            manifest.Deserialize<ArtifactManifest>(WorkflowJson.Options) is not { } captured ||
            captured.Fingerprint != input.Fingerprint || !captured.Entries.SequenceEqual(input.Manifest.Entries)) return null;
        return new(key, operation.Attempt, owner, directory, location.StorageRoot, iteration);
    }
}

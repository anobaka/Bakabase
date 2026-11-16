using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class ArtifactWorkspaceMaintenance
{
    private sealed record RetiredWorkspaceAbsence(string OriginalRoot, string Fingerprint,
        ArtifactRecord? Original = null, IReadOnlyList<string>? PrivateFiles = null);

    // Retirement moves the registered root to its now-empty quarantine location. It says
    // nothing by itself about private inputs next to the original result. A purged receipt,
    // the installation-owned locator and the executor's completed cleanup state together
    // let deletion acknowledge an empty workspace without resurrecting or deleting files.
    private async Task<RetiredWorkspaceAbsence?> InspectRetiredWorkspaceAbsenceAsync(ArtifactRecord artifact,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct, bool includePrivateFiles = false)
    {
        if (artifact is not {State: ArtifactLocationState.Retired, Managed: true, Kind: "processedFiles"} ||
            artifact.OperationId.Length != 64 || !artifact.OperationId.All(Uri.IsHexDigit) ||
            !await authorization.CoversAsync(artifact, ct) ||
            await authorization.HasActiveConsumersAsync(artifact.OperationId, ct)) return null;
        var owners = authorization.Consent.OwnerRunIds.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
        if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId))) return null;
        var operation = await store.GetOperationAsync(artifact.OperationId, ct);
        if (operation is not {State: ArtifactOperationState.Completed, Attempt: > 0, Kind: "files.process"} ||
            operation.OutputArtifactId != artifact.Id) return null;
        var registered = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == artifact.Id, ct);
        if (registered == null || registered.State != artifact.State || registered.Fingerprint != artifact.Fingerprint) return null;
        var original = await workspaces.ReadOriginalArtifactDirectoryAsync(registered, ct);
        if (original == null) return null;
        var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(artifact.MetadataJson ?? "null", Json);
        FileProcessingWorkspaceResolver.Location? location;
        var recordedPlanIdentity = false;
        if (metadata?.CompletedThroughStepId != null)
        {
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, Json);
            if (plan == null || !plan.Steps.Any(step => step.Id == metadata.CompletedThroughStepId)) return null;
            location = await workspaces.InspectCompletedCheckpointAsync(FileArtifactProcessor.CheckpointKey(operation.InputFingerprint, plan),
                operation.Id, operation.Attempt, metadata.WorkspaceDirectoryScope?.Directory ?? metadata.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
            if (location == null)
            {
                // New optional plan fields can change today's serialization of an old
                // completed recipe. Its immutable recorded JSON may still identify the
                // original locator. This fallback proves only an empty-workspace no-op;
                // surviving private payload always keeps the existing review requirement.
                location = await workspaces.InspectCompletedCheckpointAsync(
                    ArtifactManifestFiles.Hash(operation.InputFingerprint + "\n" + metadata.PlanJson),
                    operation.Id, operation.Attempt, metadata.WorkspaceDirectoryScope?.Directory ?? metadata.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
                recordedPlanIdentity = location != null;
            }
        }
        else location = await workspaces.InspectExistingAsync("operations", operation.Id, operation.Attempt, ct);
        if (location == null || !ArtifactCleanupPathReferences.StrictlyUnder(original, location.Directory) ||
            !await workspaces.HasOwnedLocationAsync(location, ct)) return null;
        var proof = await ReadRetiredCheckpointAsync(original, artifact, metadata, location.Directory, ct);
        if (proof == null) return null;
        var (stateFile, stateIdentity, hasPendingOperation) = proof.Value;
        // Check the whole exact operation/checkpoint, not a guessed parent and not just
        // the files a previous manifest happened to list. Any unknown payload or extra
        // journal remains visible for review. Empty author directories are left intact.
        var pending = new Stack<string>(); pending.Push(location.Directory);
        var directories = new List<string>(); var otherFiles = new List<string>(); var count = 0; var foundState = false;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > 10000) return null;
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
                if (attributes.HasFlag(FileAttributes.Directory))
                { directories.Add(Path.GetRelativePath(location.Directory, entry)); pending.Push(entry); }
                else if (ArtifactManifestFiles.PathComparer.Equals(entry, stateFile)) foundState = true;
                else if (!recordedPlanIdentity && includePrivateFiles && authorization.Consent.SkipFileVerification && authorization.Consent.DeliveryAccepted)
                    otherFiles.Add(entry);
                else return null;
            }
        }
        if (!foundState || ReadStateIdentity(stateFile) != stateIdentity) return null;
        // A stopped run can leave a stale pending marker after its proven successful
        // boundary was purged. It permits only an explicitly accepted empty-workspace
        // no-op, never deletion of surviving private files or the executor's history.
        if (hasPendingOperation && (otherFiles.Count > 0 ||
                !authorization.Consent.DeliveryAccepted || !authorization.Consent.SkipFileVerification)) return null;
        ArtifactRecord? originalView = null;
        if (otherFiles.Count > 0)
        {
            originalView = artifact with {State = ArtifactLocationState.Available, RootDirectory = original,
                Files = ArtifactManifestFiles.ResolveFiles(original, artifact.Manifest)};
            var reclaimable = await Paths(originalView, ct, overrideRetention: true, previewOnly: true);
            // Only executor-recorded private files with complete replacement coverage are
            // eligible. Unknown additions, surviving registered outputs and outside readers
            // still keep this workspace for review even after its main result was purged.
            if (!otherFiles.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(reclaimable) ||
                await authorization.HasOtherRegisteredFilesAsync(otherFiles, artifact.Id, ct) ||
                await references.HasReferencesAsync(originalView, authorization.Consent.OwnerRunIds, ct, otherFiles)) return null;
        }
        var selection = CaptureSelection(otherFiles, ct);
        return new(original, ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {artifact.Id, artifact.OperationId,
            artifact.Fingerprint, OriginalRoot = original, location, State = stateIdentity,
            Selection = selection.Fingerprint,
            Directories = directories.OrderBy(p => p, ArtifactManifestFiles.PathComparer)}, Json)), originalView, otherFiles);
    }

    private sealed record RetiredStateIdentity(long Length, DateTime CreationTimeUtc, DateTime LastWriteTimeUtc);
    private static RetiredStateIdentity ReadStateIdentity(string file)
    {
        ArtifactManifestFiles.RejectLinks(file);
        var info = new FileInfo(file); info.Refresh();
        return new(info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc);
    }

    // Large histories repeat per-file transitions and origins. Read only the committed
    // result identity needed for an absence proof, skipping unrelated values as a stream.
    // These state files remain history and are never selected for deletion.
    private sealed class RetiredStateProjection
    {
        public bool Completed { get; init; }
        public int LineageVersion { get; init; }
        public string? PendingOperation { get; init; }
        public ArtifactManifest? FinalManifest { get; init; }
        public List<string> FinalFiles { get; init; } = [];
        public Dictionary<string, RetiredBoundaryProjection> BoundaryResults { get; init; } = [];
    }
    private sealed class RetiredBoundaryProjection
    {
        public string Directory { get; init; } = "";
        public ArtifactManifest? Manifest { get; init; }
        public List<RetiredFileProjection> Files { get; init; } = [];
    }
    private sealed class RetiredFileProjection
    {
        public string Path { get; init; } = "";
    }

    private async Task<(string StateFile, RetiredStateIdentity Identity, bool HasPendingOperation)?> ReadRetiredCheckpointAsync(string original,
        ArtifactRecord artifact, FileArtifactProcessingMetadata? metadata, string ownedRoot, CancellationToken ct)
    {
        // A step output can sit below named directories inside a shared checkpoint. Its
        // immutable boundary record identifies the state, not a folder-name convention.
        // Stay inside the proven locator; never discover an unrelated parent's state.
        var expected = ArtifactManifestFiles.ResolveFiles(original, artifact.Manifest)
            .ToHashSet(ArtifactManifestFiles.PathComparer);
        var directory = Path.GetDirectoryName(original);
        for (var depth = 0; directory != null && depth < 16 &&
             (ArtifactManifestFiles.PathComparer.Equals(directory, ownedRoot) ||
              ArtifactCleanupPathReferences.StrictlyUnder(directory, ownedRoot));
             directory = Path.GetDirectoryName(directory), depth++)
        {
            ct.ThrowIfCancellationRequested();
            var stateFile = Path.Combine(directory, "state.json");
            ArtifactManifestFiles.RejectLinks(stateFile);
            if (!File.Exists(stateFile)) continue;
            var identity = ReadStateIdentity(stateFile);
            if (identity.Length > 256 * 1024 * 1024) return null;
            await using var stream = new FileStream(stateFile, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<RetiredStateProjection>(stream, Json, ct);
            if (ReadStateIdentity(stateFile) != identity) return null;
            if (state is not {LineageVersion: 1}) return null;
            var final = state.Completed && state.FinalManifest != null && state.FinalFiles.Count > 0 &&
                FileDispositionProofs.SameManifest(state.FinalManifest, artifact.Manifest) &&
                state.FinalFiles.Select(workspaces.Relocate).ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(expected);
            var boundary = metadata?.CompletedThroughStepId is { } step &&
                state.BoundaryResults.TryGetValue(step, out var recorded) &&
                ArtifactManifestFiles.PathComparer.Equals(workspaces.Relocate(recorded.Directory), original) &&
                recorded.Manifest != null && FileDispositionProofs.SameManifest(recorded.Manifest, artifact.Manifest) &&
                recorded.Files.Select(file => workspaces.Relocate(file.Path))
                    .ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(expected);
            // IntermediatesReclaimedAt can be absent when moves/final assembly consumed
            // every private input. The positive, whole-workspace absence scan above is
            // the evidence for this no-op; a timestamp cannot substitute for that scan.
            return final || boundary ? (stateFile, identity, state.PendingOperation != null) : null;
        }
        return null;
    }
}

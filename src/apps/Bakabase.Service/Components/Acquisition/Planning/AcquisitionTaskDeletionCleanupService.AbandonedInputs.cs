using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    // Failed attempts have no output artifact, so artifact-lineage maintenance cannot discover
    // their private input copies. Only the fixed, explicitly accepted task deletion can retire
    // these copies. The failed operation and its executor journal remain unchanged.
    internal sealed record AbandonedInputCandidate(string Id, string OperationId, int Attempt,
        string InputArtifactId, string CheckpointDirectory, string StateFingerprint, string ClaimIdentity,
        IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Files, IReadOnlyDictionary<string, string> FileIdentities,
        AcquisitionTaskDeleteCleanupGroup Group)
    {
        public IReadOnlyList<string> InputDirectories { get; init; } = [];
        internal IReadOnlyDictionary<string, DeferredEmptyDirectoryTree.PrivateFile>? PrivateExtractionFiles { get; init; }
        internal IReadOnlyDictionary<string, ArtifactManifestEntry>? ExtractionEntries { get; init; }
    }

    private async Task<IReadOnlyList<AbandonedInputCandidate>> PreviewAbandonedInputsAsync(
        IReadOnlyList<string> operationIds, ArtifactDeliveryCleanupAuthorization authorization,
        ArtifactCleanupReferenceGuard references, CancellationToken ct, Func<string, Task>? guardDirectory = null,
        IReadOnlyList<string>? stageDirectories = null)
    {
        if (workspaces == null || !authorization.Consent.DeliveryAccepted && !authorization.Consent.AbandonUnfinishedFiles && !authorization.Consent.WholeTaskFiles) return [];
        var now = DateTime.UtcNow;
        var operations = await db.ArtifactOperations.AsNoTracking().Where(o =>
            o.Kind == "files.process" && o.OutputArtifactId == null && o.Attempt > 0 &&
            (operationIds.Contains(o.Id) && (o.State == ArtifactOperationState.Failed || o.State == ArtifactOperationState.Unknown) ||
                o.LeaseOwner != null && o.LeaseOwner.StartsWith("workflow:") &&
                (o.LeaseUntil == null || o.LeaseUntil <= now) &&
                (o.State == ArtifactOperationState.Failed || o.State == ArtifactOperationState.Unknown ||
                 o.State == ArtifactOperationState.Running && o.LeaseUntil != null))).ToListAsync(ct);
        var result = new List<AbandonedInputCandidate>();
        foreach (var operation in operations)
        {
            ct.ThrowIfCancellationRequested();
            if ((operation.State == ArtifactOperationState.Running || !operationIds.Contains(operation.Id)) &&
                (AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(operation.LeaseOwner) is not {} ownerRun ||
                 !authorization.Consent.OwnerRunIds.Contains(ownerRun))) continue;
            if (!HexKey(operation.Id) || await InactiveAbandonedOperationAsync(operation.Id, operation.Attempt, authorization, ct) == null)
                continue;
            FileProcessingWorkspaceResolver.Location? owned = null;
            try
            {
                var location = await workspaces.InspectExistingAsync("operations", operation.Id, operation.Attempt, ct);
                if (location == null || !await workspaces.HasOwnedLocationAsync(location, ct)) continue;
                owned = location;
                var checkpoints = new List<string>();
                var plans = Path.Combine(location.Directory, "plan");
                ArtifactManifestFiles.RejectLinks(plans);
                if (!OverlapsStageCleanupSelection(location.Directory, stageDirectories) && Directory.Exists(plans))
                {
                    await EnsureDirectoryAuthorizedAsync(location.Directory, guardDirectory);
                    var children = Directory.EnumerateDirectories(plans).Take(257).ToArray();
                    if (children.Length <= 256) checkpoints.AddRange(children.Where(path => HexKey(Path.GetFileName(path))));
                }
                var boundary = await ReadAbandonedBoundaryAsync(operation, authorization, ct);
                if (boundary?.Shared == true && !OverlapsStageCleanupSelection(boundary.Directory, stageDirectories))
                {
                    await EnsureDirectoryAuthorizedAsync(boundary.Directory, guardDirectory);
                    result.Add(BlockedAbandonedInput(operation.Id, operation.Attempt, boundary.Directory, "ReferencedPath", boundary: true));
                }
                else if (boundary != null) checkpoints.Add(boundary.Directory);
                foreach (var checkpoint in checkpoints.Distinct(ArtifactManifestFiles.PathComparer))
                {
                    if (OverlapsStageCleanupSelection(checkpoint, stageDirectories)) continue;
                    await EnsureDirectoryAuthorizedAsync(checkpoint, guardDirectory);
                    var candidate = await InspectAbandonedInputAsync(operation.Id, operation.Attempt, checkpoint,
                        authorization, references, ct);
                    if (candidate != null && (candidate.Files.Count > 0 || candidate.InputDirectories.Count > 0)) result.Add(candidate);
                    else if (candidate == null && HasAbandonedCheckpointPayload(checkpoint, ct))
                        result.Add(BlockedAbandonedInput(operation.Id, operation.Attempt, checkpoint, "CLEANUP_FILES_RETAINED"));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
            {
                logger?.LogWarning(error, "Unable to inspect failed input workspace {OperationId}", operation.Id);
                if (owned != null) result.Add(BlockedAbandonedInput(operation.Id, operation.Attempt, owned.Directory, "WorkspaceUnavailable"));
            }
        }
        return result;
    }

    private static bool OverlapsStageCleanupSelection(string checkpoint, IReadOnlyList<string>? directories) =>
        directories?.Any(directory => StageCleanupOverlap(directory, checkpoint)) == true;

    private async Task<ArtifactOperationDbModel?> InactiveAbandonedOperationAsync(string id, int attempt,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        if (authorization.Consent.AbandonUnfinishedFiles || authorization.Consent.WholeTaskFiles)
        {
            if (!await authorization.IsCurrentAsync(ct)) return null;
        }
        else if (!authorization.Consent.DeliveryAccepted || authorization.Consent.Publications.Count == 0) return null;
        // Matches rechecks the task version, completed delivery and all fixed owner runs.
        foreach (var expected in authorization.Consent.Publications)
            if (await artifacts.GetArtifactAsync(expected.ArtifactId, ct) is not { } publication ||
                !await authorization.MatchesAsync(publication, ct)) return null;
        var operation = await artifacts.GetOperationAsync(id, ct);
        if (operation is not {Kind: "files.process", OutputArtifactId: null} || operation.Attempt != attempt ||
            operation.LeaseUntil > DateTime.UtcNow || await authorization.HasActiveConsumersAsync(id, ct)) return null;
        var owners = authorization.Consent.OwnerRunIds.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == id).ToListAsync(ct);
        if (bindings.Count > 0)
            return operation.State is ArtifactOperationState.Failed or ArtifactOperationState.Unknown &&
                bindings.All(b => b.OwnerKind == "workflow" && owners.Contains(b.OwnerId)) ? operation : null;
        // A process can fail after staging inputs but before writing its durable binding.
        // Only its exact, inactive workflow claim supplies ownership in this narrow case.
        // A waiting run or even an inactive consumer may still intend to resume the claim.
        if (operation.State is not (ArtifactOperationState.Running or ArtifactOperationState.Failed or ArtifactOperationState.Unknown) ||
            operation.State == ArtifactOperationState.Running && operation.LeaseUntil == null ||
            AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(operation.LeaseOwner) is not {} owner ||
            !authorization.Consent.OwnerRunIds.Contains(owner) ||
            await db.ArtifactConsumers.AsNoTracking().AnyAsync(c => c.OperationId == id, ct)) return null;
        var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == owner, ct);
        return run != null && run.PendingSignalJson == null && (run.Status is WorkflowRunStatus.Success or
            WorkflowRunStatus.Failed or WorkflowRunStatus.Cancelled or WorkflowRunStatus.Interrupted ||
            authorization.Consent.AbandonUnfinishedFiles && run.Status == WorkflowRunStatus.Waiting && authorization.PreviewSupersededRuns.Contains(owner)) ? operation : null;
    }

    private async Task<AbandonedInputCandidate?> InspectAbandonedInputAsync(string operationId, int attempt,
        string checkpoint, ArtifactDeliveryCleanupAuthorization authorization, ArtifactCleanupReferenceGuard references,
        CancellationToken ct)
    {
        var operation = await InactiveAbandonedOperationAsync(operationId, attempt, authorization, ct);
        if (operation == null || workspaces == null) return null;
        var location = await workspaces.InspectExistingAsync("operations", operationId, attempt, ct);
        if (location == null || !await workspaces.HasOwnedLocationAsync(location, ct) || !HexKey(Path.GetFileName(checkpoint))) return null;
        var boundary = !SamePath(Path.GetDirectoryName(checkpoint)!, Path.Combine(location.Directory, "plan"));
        RecordedContinuation? historical = null;
        if (boundary)
        {
            var recorded = await ReadAbandonedBoundaryAsync(operation, authorization, ct);
            if (recorded == null || recorded.Shared || !SamePath(checkpoint, recorded.Directory)) return null;
            location = recorded.Location;
            historical = recorded.Historical;
        }
        var stateFile = Path.Combine(checkpoint, "state.json");
        ArtifactManifestFiles.RejectLinks(stateFile);
        if (!File.Exists(stateFile) || new FileInfo(stateFile).Length > 16 * 1024 * 1024) return null;
        var stateJson = await File.ReadAllTextAsync(stateFile, ct);
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(stateJson, JsonSerializerOptions.Web);
        if (state is {Completed: false} && (historical != null || state.Operations.Values.Any(o => o.Completed && o.OutputManifest != null)))
            return await InspectAbandonedExtractionAsync(operation, checkpoint, stateFile, stateJson, state, authorization, ct, historical);
        if (state is not {LineageVersion: 1, InputManifest: {Entries.Count: > 0}} ||
            state.InputManifest.Fingerprint != operation.InputFingerprint ||
            state.BoundaryResults.Count != 0 || state.Outputs.Count != 1 || !state.Outputs.TryGetValue("download", out var staged) ||
            staged.Count != state.InputManifest.Entries.Count || staged.Count > 100000) return null;
        // The executor can finish a no-transform plan before its operation commits an output.
        // Its result is only another private copy when every final byte/path still matches the
        // registered input, with no processing or boundary evidence. Do not generalize this to
        // completed extraction/rename plans or trust an editable checkpoint as its own proof.
        var completedCopy = state.Completed && state.FinalManifest != null &&
            FileDispositionProofs.SameManifest(state.InputManifest, state.FinalManifest) &&
            state.Operations.Count == 0 && state.FileTransitions.Count == 0 && state.PendingOperation == null &&
            state.StagedFiles.Count == 0 && state.FinalExtractedFiles.Count == 0 && state.RetainedFiles.Count == 0 &&
            state.FinalAssembly == null && state.IntermediatesReclaimedAt == null &&
            state.Active.Count == staged.Count && state.Active.All(a => !a.Extracted && a.StepId == null) &&
            state.FinalFiles.Count == staged.Count;
        if (!completedCopy && (state.Completed || state.FinalManifest != null || state.FinalFiles.Count != 0)) return null;
        var untouchedStaging = !state.Completed && state.Operations.Count == 0 && state.FileTransitions.Count == 0 &&
            state.PendingOperation == null && state.StagedFiles.Count == 0 && state.FinalExtractedFiles.Count == 0 &&
            state.RetainedFiles.Count == 0 && state.FinalAssembly == null && state.IntermediatesReclaimedAt == null &&
            state.Active.Count == staged.Count && state.Active.All(a => !a.Extracted && a.StepId == null);

        // The input manifest must be an actual, exclusively owned registered input, not
        // content asserted only by an editable checkpoint. PendingOperation is intentionally
        // allowed: failure commonly happens after staging and before the first extraction.
        var ownerKeys = authorization.Consent.OwnerRunIds.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
        var inputIds = await db.Artifacts.AsNoTracking().Where(a =>
                (a.Managed && (a.Kind == "download" || a.Kind == "processedFiles") || !a.Managed && a.Kind == "currentFiles") &&
                a.Fingerprint == operation.InputFingerprint &&
                db.ArtifactOperationBindings.Any(b => b.OperationId == a.OperationId && b.OwnerKind == "workflow" && ownerKeys.Contains(b.OwnerId)))
            .Select(a => a.Id).Take(101).ToListAsync(ct);
        if (inputIds.Count > 100) return null;
        ArtifactRecord? input = null;
        string? retiredInputRoot = null;
        foreach (var id in inputIds)
        {
            var possible = await artifacts.GetArtifactAsync(id, ct);
            if (possible == null || possible.State == ArtifactLocationState.Quarantined ||
                !FileDispositionProofs.SameManifest(possible.Manifest, state.InputManifest) ||
                await authorization.HasActiveConsumersAsync(possible.OperationId, ct)) continue;
            var producer = await artifacts.GetOperationAsync(possible.OperationId, ct);
            if (producer is not {State: ArtifactOperationState.Completed} || producer.OutputArtifactId != possible.Id) continue;
            var inputBindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == possible.OperationId).ToListAsync(ct);
            if (inputBindings.Count == 0 || inputBindings.Any(b => b.OwnerKind != "workflow" || !ownerKeys.Contains(b.OwnerId))) continue;
            string? originalRoot = null;
            if (possible.State == ArtifactLocationState.Retired && !completedCopy)
            {
                // A successful parent can be retired earlier in the same accepted
                // cleanup. Its immutable manifest remains evidence for a failed
                // attempt's untouched private copy, not authority over the old source.
                if (!untouchedStaging ||
                    (originalRoot = await RetiredStagedInputRootAsync(possible, ct)) == null) continue;
            }
            if (possible.Kind == "currentFiles")
            {
                // A captured directory stays user-owned. Its completed capture may prove
                // only the executor's private staged copies, never retirement of the source.
                var claimOwner = AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(operation.LeaseOwner);
                var bindingOwners = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == operation.Id &&
                    b.OwnerKind == "workflow").Select(b => b.OwnerId).ToListAsync(ct);
                if (claimOwner is {} run) bindingOwners.Add(run.ToString(CultureInfo.InvariantCulture));
                var slot = "adaptive:input:" + possible.Id;
                if (possible.Managed || producer.Kind != "files.captureCurrentDirectory" ||
                    !inputBindings.Any(b => bindingOwners.Contains(b.OwnerId) &&
                        (b.Slot == slot || b.Slot.EndsWith(":" + slot, StringComparison.Ordinal)))) continue;
            }
            input = possible;
            retiredInputRoot = originalRoot;
            if (SamePath(possible.RootDirectory, location.StorageRoot)) break;
        }
        if (input == null) return null;
        var inputs = Path.Combine(checkpoint, "inputs");
        var expected = ArtifactManifestFiles.ResolveFiles(inputs, state.InputManifest);
        if (retiredInputRoot != null &&
            (!state.Active.Select(a => workspaces.Relocate(a.Path)).ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(expected) ||
             ArtifactManifestFiles.ResolveFiles(retiredInputRoot, input.Manifest).Intersect(expected, ArtifactManifestFiles.PathComparer).Any())) return null;
        if (input.Kind == "currentFiles" && ArtifactManifestFiles.ResolveFiles(input.RootDirectory, input.Manifest)
                .Intersect(expected, ArtifactManifestFiles.PathComparer).Any()) return null;
        var stagedPaths = staged.Select(a => workspaces.Relocate(a.Path)).ToHashSet(ArtifactManifestFiles.PathComparer);
        if (!stagedPaths.SetEquals(expected)) return null;
        for (var index = 0; index < expected.Count; index++)
        {
            var relative = ArtifactManifestFiles.RelativeFile(checkpoint, expected[index]);
            if (!state.FileContents.TryGetValue(relative, out var saved) || saved != state.InputManifest.Entries[index]) return null;
        }
        var byPath = expected.Select((path, index) => (path, entry: state.InputManifest.Entries[index]))
            .ToDictionary(x => x.path, x => x.entry, ArtifactManifestFiles.PathComparer);
        if (completedCopy)
        {
            var activePaths = state.Active.Select(a => workspaces.Relocate(a.Path)).ToHashSet(ArtifactManifestFiles.PathComparer);
            if (!activePaths.SetEquals(expected)) return null;
            var results = ArtifactManifestFiles.ResolveFiles(Path.Combine(checkpoint, "result"), state.FinalManifest!);
            if (!state.FinalFiles.Select(workspaces.Relocate).ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(results) ||
                state.FileContents.Count != expected.Count + results.Count) return null;
            for (var index = 0; index < results.Count; index++)
            {
                var relative = ArtifactManifestFiles.RelativeFile(checkpoint, results[index]);
                var entry = state.FinalManifest!.Entries[index];
                if (!state.FileContents.TryGetValue(relative, out var saved) || saved != entry) return null;
                byPath.Add(results[index], entry);
            }
        }
        var allowed = byPath.Keys.Append(stateFile).ToHashSet(ArtifactManifestFiles.PathComparer);
        var actual = new List<string>();
        var inputDirectories = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        if (authorization.Consent.AbandonUnfinishedFiles || authorization.Consent.WholeTaskFiles)
        {
            foreach (var start in expected.Select(Path.GetDirectoryName).Concat(ArtifactManifestFiles.ResolveDirectories(inputs, state.InputManifest)))
                for (var path = start; path != null && (SamePath(path, inputs) || ArtifactCleanupPathReferences.StrictlyUnder(path, inputs));
                     path = Path.GetDirectoryName(path))
                    if (!inputDirectories.Add(path)) break;
        }
        var observedDirectories = new List<string>();
        var pending = new Stack<string>(); pending.Push(checkpoint); var entries = 0;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(directory);
            if (inputDirectories.Contains(directory)) observedDirectories.Add(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > 200000) return null;
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(path);
                else if (!allowed.Contains(path)) return null; // Unknown files remain for review.
                else if (!SamePath(path, stateFile)) actual.Add(path);
            }
        }
        var files = new List<AcquisitionTaskDeleteCleanupFile>();
        var identities = new Dictionary<string, string>(ArtifactManifestFiles.PathComparer);
        foreach (var path in actual.OrderBy(p => p, ArtifactManifestFiles.PathComparer))
        {
            storage.EnsurePathAllowed(path);
            var info = new FileInfo(path); info.Refresh();
            if (info.Length != byPath[path].Length) return null;
            files.Add(new(path, info.Length));
            identities.Add(path, JsonSerializer.Serialize(new {info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc}));
        }
        var reason = await artifacts.HasRegisteredFilesAsync(actual, ct) ? "ReferencedArtifact" : null;
        // When the owned workspace is inside the real completed download, inspect that
        // entire input root conservatively. The existing exact transport receipt exemption
        // then excludes only its own container; private registered snapshots are still
        // protected by HasRegisteredFilesAsync above. No forged output artifact is needed.
        if (reason == null && await references.HasReferencesAsync(input, authorization.Consent.OwnerRunIds, ct,
                privatePaths: SamePath(input.RootDirectory, location.StorageRoot) ? null : actual,
                previewSupersededRuns: authorization.PreviewSupersededRuns)) reason = "ReferencedPath";
        if (reason == null && observedDirectories.Count > 0)
        {
            // An external task or resource may own an empty directory even when it
            // owns no staged file. Its directory cannot be released by this consent.
            var ownerTask = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == authorization.Consent.TaskId, ct);
            try
            {
                await new AcquisitionStageDirectoryReferenceGuard(db, artifacts, storage, workspaces)
                    .RequireAsync(ownerTask, authorization.Consent.OwnerRunIds.ToArray(), "", inputs, ct,
                        explicitTaskCleanup: true, abandonUnfinishedFiles: true);
            }
            catch (InvalidOperationException error) when (error.Message is "STAGE_DIRECTORY_SHARED" or
                "STAGE_DIRECTORY_REFERENCED" or "STAGE_DIRECTORY_OWNERSHIP_UNKNOWN" or "STAGE_DIRECTORY_TASK_ACTIVE")
            { observedDirectories.Clear(); }
        }
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == operationId).ToListAsync(ct);
        var nodes = bindings.Select(b => new AcquisitionTaskDeleteCleanupNode(int.Parse(b.OwnerId, CultureInfo.InvariantCulture),
            NodeId(b.Slot), "processingWorkspace")).Distinct().ToArray();
        if (nodes.Length == 0 && AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(operation.LeaseOwner) is {} owner)
            nodes = [new(owner, "", "processingWorkspace")];
        var identity = (boundary ? "abandoned-boundary:" : "abandoned-input:") + operationId + ":" + Path.GetFileName(checkpoint);
        return new(identity, operationId, attempt, input.Id, checkpoint, ArtifactManifestFiles.Hash(stateJson), ClaimIdentity(operation), files, identities,
            new(identity, "intermediate", completedCopy ? checkpoint : inputs, reason == null, reason ?? (files.Count == 0 && observedDirectories.Count == 0 ? "AlreadyAbsent" : null),
                nodes, files, files.Count, files.Sum(f => f.Bytes), Shared: reason is "ReferencedArtifact" or "ReferencedPath"))
            {InputDirectories = observedDirectories};
    }

    private async Task<ArtifactWorkspaceCleanupResult> DeleteAbandonedInputsAsync(AbandonedInputCandidate expected,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        ArtifactWorkspaceCleanupJournal.Receipt? receipt = null;
        var removed = 0; long bytes = 0;
        try
        {
            await using var fence = await artifacts.AcquireMaintenanceAsync(ct);
            var current = await InspectAbandonedInputAsync(expected.OperationId, expected.Attempt, expected.CheckpointDirectory,
                authorization, new ArtifactCleanupReferenceGuard(db, workspaces), ct);
            if (current == null || current.Id != expected.Id ||
                expected.PrivateExtractionFiles != null && current.FileIdentities.Count != expected.FileIdentities.Count ||
                current.ClaimIdentity != expected.ClaimIdentity || current.StateFingerprint != expected.StateFingerprint || current.InputArtifactId != expected.InputArtifactId ||
                current.FileIdentities.Any(p => !expected.FileIdentities.TryGetValue(p.Key, out var before) || before != p.Value))
                return new(expected.Id, false, "SelectionChanged");
            if (!current.Group.Eligible) return new(expected.Id, false, current.Group.ReasonCode);
            if (current.Files.Count == 0 && current.InputDirectories.Count == 0) return new(expected.Id, true, "AlreadyAbsent");
            var snapshot = new ArtifactCleanupFileSnapshot();
            foreach (var file in current.Files) snapshot.Observe(file.Path, file.Bytes, ct);
            var input = await artifacts.GetArtifactAsync(current.InputArtifactId, ct);
            if (input == null) return new(expected.Id, false, "SelectionChanged");
            if (current.ExtractionEntries != null && !authorization.Consent.SkipFileVerification)
            {
                var observed = await ArtifactManifestFiles.CaptureAsync(current.CheckpointDirectory, current.Files.Select(f => f.Path).ToArray(), ct);
                if (observed.Entries.Any(e => !current.ExtractionEntries.TryGetValue(e.RelativePath, out var recorded) ||
                    recorded.Length != e.Length || recorded.Sha256 != e.Sha256)) return new(expected.Id, false, "SelectionChanged");
            }
            else if (!authorization.Consent.SkipFileVerification)
            {
                var entries = input.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
                foreach (var copy in current.Files.GroupBy(f =>
                             ArtifactManifestFiles.RelativeFile(current.CheckpointDirectory, f.Path).Split('/')[0]))
                {
                    if (copy.Key is not ("inputs" or "result")) return new(expected.Id, false, "SelectionChanged");
                    var root = Path.Combine(current.CheckpointDirectory, copy.Key);
                    var captured = await ArtifactManifestFiles.CaptureAsync(root, copy.Select(f => f.Path).ToArray(), ct);
                    if (captured.Entries.Any(e => !entries.TryGetValue(e.RelativePath, out var original) || e != original))
                        return new(expected.Id, false, "SelectionChanged");
                }
            }
            var refreshed = await InspectAbandonedInputAsync(expected.OperationId, expected.Attempt, expected.CheckpointDirectory,
                authorization, new ArtifactCleanupReferenceGuard(db, workspaces), ct);
            if (refreshed == null || !refreshed.Group.Eligible || refreshed.ClaimIdentity != current.ClaimIdentity || refreshed.StateFingerprint != current.StateFingerprint ||
                refreshed.FileIdentities.Count != current.FileIdentities.Count || refreshed.FileIdentities.Any(p =>
                    !current.FileIdentities.TryGetValue(p.Key, out var before) || before != p.Value))
                return new(expected.Id, false, "SelectionChanged");
            receipt = await maintenance.BeginAbandonedInputCleanupAsync(input, current.OperationId, current.CheckpointDirectory,
                current.Files.ToDictionary(f => f.Path, f => f.Bytes), authorization.Consent, ct);
            foreach (var file in current.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (!snapshot.Matches(file.Path, file.Bytes, ct)) throw new IOException("The failed input snapshot changed.");
                if (current.PrivateExtractionFiles is {} privateFiles)
                {
                    if (DeferredEmptyDirectoryTree.InspectPrivateFile(file.Path, privateFiles[file.Path]) == null)
                        throw new IOException("The private extraction file changed.");
                }
                else File.Delete(file.Path);
                removed++; bytes += file.Bytes;
            }
            var confirmedDirectories = refreshed.InputDirectories.Intersect(expected.InputDirectories, ArtifactManifestFiles.PathComparer);
            RemoveConfirmedEmptyDirectories(confirmedDirectories, ct);
            await maintenance.FinishDeliveredCleanupAsync(receipt, "Completed", ct);
            return new(expected.Id, true, null, removed, bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            if (receipt != null)
                try { await maintenance.FinishDeliveredCleanupAsync(receipt, error is OperationCanceledException ? "Interrupted" : "Failed", CancellationToken.None); }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            if (error is OperationCanceledException) throw;
            return new(expected.Id, false, "CLEANUP_FILES_RETAINED", removed, bytes);
        }
    }

    private static bool HexKey(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static string ClaimIdentity(ArtifactOperationDbModel operation) => ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new
        {operation.Id, operation.Attempt, operation.State, operation.LeaseOwner, operation.LeaseUntil, operation.LeaseToken}));
    private static bool SamePath(string left, string right) => ArtifactManifestFiles.PathComparer.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)));

    private static AbandonedInputCandidate BlockedAbandonedInput(string operation, int attempt, string directory, string reason, bool boundary = false)
    {
        var id = (boundary ? "abandoned-boundary:" : "abandoned-input:") + operation + ":" + Path.GetFileName(directory);
        return new(id, operation, attempt, "", directory, "", "", [], new Dictionary<string, string>(),
            new(id, "intermediate", directory, false, reason, [], [], 0, 0, Shared: reason == "ReferencedPath", SizeUnknown: true));
    }

    private static bool HasAbandonedCheckpointPayload(string checkpoint, CancellationToken ct)
    {
        var pending = new Stack<string>(); pending.Push(checkpoint); var count = 0;
        var stateFile = Path.Combine(checkpoint, "state.json");
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 10000) return true; // Unproven, never silently reported as cleaned.
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(path);
                else if (!SamePath(path, stateFile)) return true;
            }
        }
        return false;
    }
}

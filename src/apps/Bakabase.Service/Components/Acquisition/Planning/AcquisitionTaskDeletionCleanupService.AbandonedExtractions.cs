using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    // The caller has already verified the operation, attempt, installation locator and (when present)
    // independent boundary locator. The registered input establishes provenance for the journal only.
    private async Task<AbandonedInputCandidate?> InspectAbandonedExtractionAsync(ArtifactOperationDbModel operation,
        string checkpoint, string stateFile, string stateJson, FileProcessingPlanExecutor.State state,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct, RecordedContinuation? historical = null)
    {
        var consent = authorization.Consent;
        if (workspaces == null || !(consent.WholeTaskFiles || consent.AbandonUnfinishedFiles || consent.DeliveryAccepted) || !await authorization.IsCurrentAsync(ct) ||
            operation.OutputArtifactId != null || operation.LeaseUntil > DateTime.UtcNow ||
            AcquisitionDeletionActivityReader.WorkflowLeaseOwnerRun(operation.LeaseOwner) is not {} owner ||
            !consent.OwnerRunIds.Contains(owner) || state.InputManifest?.Fingerprint != operation.InputFingerprint ||
            state.LineageVersion != 1 || state.BoundaryResults.Count != 0 || state.Completed || state.FinalManifest != null ||
            state.FinalAssembly != null || state.FinalFiles.Count != 0 || state.RetainedFiles.Count != 0 ||
            state.StagedFiles.Count != 0 || state.IntermediatesReclaimedAt != null || state.ReadOnlyVolumeArtifactId != null ||
            state.SnapshotSourceArtifactId != null) return null;
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == consent.TaskId, ct);
        var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == owner, ct);
        if (task?.DeletedAt == null || run == null || run.PendingSignalJson != null ||
            run.Status is not (WorkflowRunStatus.Success or WorkflowRunStatus.Failed or WorkflowRunStatus.Cancelled or WorkflowRunStatus.Interrupted) ||
            await db.ArtifactConsumers.AsNoTracking().AnyAsync(c => c.OperationId == operation.Id && c.ReleasedAt == null, ct)) return null;
        var proved = ProveExtractionManifest(checkpoint, state, historical != null);
        if (proved == null) return null;
        var input = await RegisteredExtractionInputAsync(owner, checkpoint, state.InputManifest!, authorization, ct, historical?.Input.Id);
        if (input == null) return null;
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web);
        if (plan == null) return null;
        var selected = await StageCleanupSelectionsAsync(task, plan, consent.OwnerRunIds, true, false, ct);
        if (selected.ProtectedRoots.Concat(selected.PublicationRoots).Any(root => StageCleanupOverlap(root, checkpoint)) ||
            selected.ConfiguredRoots.Any(root => SamePath(root, checkpoint) || ArtifactCleanupPathReferences.StrictlyUnder(root, checkpoint))) return null;
        var scope = new AcquisitionStageDirectoryReferenceGuard(db, artifacts, storage, workspaces);
        try
        {
            await scope.RequireAsync(task, consent.OwnerRunIds.ToArray(), "", checkpoint, ct, explicitTaskCleanup: true,
                abandonUnfinishedFiles: true, hasActiveConsumers: authorization.HasActiveConsumersAsync, privateReceiptDirectory: checkpoint);
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("STAGE_DIRECTORY_", StringComparison.Ordinal)) { return null; }
        var marker = DeferredEmptyDirectoryTree.InspectPrivateFile(stateFile);
        if (marker == null) return null;
        var physical = new Dictionary<string, DeferredEmptyDirectoryTree.PrivateFile>(ArtifactManifestFiles.PathComparer);
        var pending = new Stack<string>(); pending.Push(checkpoint);
        var count = 0;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 200000) return null;
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
                if (SamePath(path, stateFile)) continue;
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!proved.Value.Directories.Contains(path)) return null;
                    pending.Push(path); continue;
                }
                var relative = ArtifactManifestFiles.RelativeFile(checkpoint, path);
                if (!proved.Value.Files.TryGetValue(relative, out var entry) ||
                    DeferredEmptyDirectoryTree.InspectPrivateFile(path) is not {} identity ||
                    identity.Length != entry.Length || identity.Device != marker.Device || identity.Mount != marker.Mount) return null;
                physical.Add(path, identity);
            }
        }
        if (physical.Count == 0) return null;
        var files = physical.Select(x => new AcquisitionTaskDeleteCleanupFile(x.Key, x.Value.Length))
            .OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        var id = (historical == null ? "abandoned-extraction:" : "abandoned-boundary:") + operation.Id + ":" + Path.GetFileName(checkpoint);
        var preview = new AcquisitionTaskDeleteCleanupGroup(id, "intermediate", checkpoint, true, null,
            [new(owner, "", "processingWorkspace")], files, files.Length, files.Sum(f => f.Bytes));
        var identities = physical.ToDictionary(x => x.Key, x => JsonSerializer.Serialize(x.Value), ArtifactManifestFiles.PathComparer);
        return new(id, operation.Id, operation.Attempt, input.Id, checkpoint,
            ArtifactManifestFiles.Hash(stateJson + "\n" + JsonSerializer.Serialize(marker) + historical?.Evidence), ClaimIdentity(operation), files, identities, preview)
            {PrivateExtractionFiles = physical, ExtractionEntries = proved.Value.Files};
    }

    private async Task<ArtifactRecord?> RegisteredExtractionInputAsync(int owner, string checkpoint, ArtifactManifest manifest,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct, string? requiredInputId)
    {
        var ownerKey = owner.ToString(CultureInfo.InvariantCulture);
        var ids = await db.Artifacts.AsNoTracking().Where(a => (requiredInputId == null || a.Id == requiredInputId) && a.Fingerprint == manifest.Fingerprint &&
            (a.Managed && (a.Kind == "download" || a.Kind == "processedFiles") || !a.Managed && a.Kind == "currentFiles") &&
            db.ArtifactOperationBindings.Any(b => b.OperationId == a.OperationId && b.OwnerKind == "workflow" && b.OwnerId == ownerKey))
            .OrderBy(a => a.Id).Select(a => a.Id).Take(101).ToListAsync(ct);
        if (ids.Count > 100) return null;
        var owners = authorization.Consent.OwnerRunIds.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        foreach (var id in ids)
        {
            var candidate = await artifacts.GetArtifactAsync(id, ct);
            if (candidate == null || candidate.State == ArtifactLocationState.Quarantined ||
                await authorization.HasActiveConsumersAsync(candidate.OperationId, ct) || !FileDispositionProofs.SameManifest(manifest, candidate.Manifest) ||
                await artifacts.GetOperationAsync(candidate.OperationId, ct) is not {State: ArtifactOperationState.Completed} producer ||
                producer.OutputArtifactId != id || await db.ArtifactOperationBindings.AsNoTracking()
                    .AnyAsync(b => b.OperationId == candidate.OperationId && (b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId)), ct)) continue;
            if (candidate.Kind == "currentFiles")
            {
                var slot = "adaptive:input:" + id;
                if (producer.Kind != "files.captureCurrentDirectory" || !await db.ArtifactOperationBindings.AsNoTracking()
                    .AnyAsync(b => b.OperationId == candidate.OperationId && b.OwnerKind == "workflow" && b.OwnerId == ownerKey &&
                                  (b.Slot == slot || b.Slot.EndsWith(":" + slot)), ct)) continue;
            }
            var original = candidate.State == ArtifactLocationState.Retired
                ? await RetiredStagedInputRootAsync(candidate, ct) : candidate.RootDirectory;
            if (original == null || SamePath(original, checkpoint) || ArtifactCleanupPathReferences.StrictlyUnder(original, checkpoint) ||
                ArtifactCleanupPathReferences.StrictlyUnder(checkpoint, original) && ArtifactManifestFiles.ResolveFiles(original, manifest)
                    .Concat(ArtifactManifestFiles.ResolveDirectories(original, manifest)).Any(p => StageCleanupOverlap(p, checkpoint))) continue;
            return candidate;
        }
        return null;
    }

    private (Dictionary<string, ArtifactManifestEntry> Files, HashSet<string> Directories)? ProveExtractionManifest(
        string checkpoint, FileProcessingPlanExecutor.State state, bool historicalBoundary)
    {
        var entries = new Dictionary<string, ArtifactManifestEntry>(ArtifactManifestFiles.PathComparer);
        var directories = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var inputs = Path.Combine(checkpoint, "inputs"); var outputs = Path.Combine(checkpoint, "outputs");
        bool Add(string directory, ArtifactManifest manifest)
        {
            if (manifest.Entries.Count is 0 or > 100000 || manifest.Fingerprint != ArtifactManifestFiles.Fingerprint(manifest.Entries, manifest.Directories)) return false;
            directories.Add(directory);
            var files = ArtifactManifestFiles.ResolveFiles(directory, manifest);
            foreach (var path in files.Concat(ArtifactManifestFiles.ResolveDirectories(directory, manifest)))
                for (var p = Path.GetDirectoryName(path); p != null && FileProcessingPlanExecutor.IsInside(checkpoint, p); p = Path.GetDirectoryName(p))
                    directories.Add(p);
            directories.UnionWith(ArtifactManifestFiles.ResolveDirectories(directory, manifest));
            for (var i = 0; i < files.Count; i++)
            {
                var rel = ArtifactManifestFiles.RelativeFile(checkpoint, files[i]);
                if (!entries.TryAdd(rel, manifest.Entries[i]) || !state.FileContents.TryGetValue(rel, out var recorded) || recorded != manifest.Entries[i]) return false;
            }
            return true;
        }

        if (state.InputManifest == null || !Add(inputs, state.InputManifest) || !state.Outputs.TryGetValue("download", out var staged) ||
            !ArtifactManifestFiles.ResolveFiles(inputs, state.InputManifest).ToHashSet(ArtifactManifestFiles.PathComparer)
                .SetEquals(staged.Select(item => workspaces!.Relocate(item.Path)))) return null;
        var groupRoots = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var unfinished = state.Operations.Where(pair => !pair.Value.Completed).Select(pair => pair.Key).ToArray();
        var untouchedBoundary = historicalBoundary && state.Operations.Count == 0 && state.PendingOperation == null && state.FileTransitions.Count == 0;
        if (!untouchedBoundary && (unfinished.Length != 1 || state.PendingOperation is {} pending && pending != unfinished[0])) return null;
        foreach (var group in state.Operations.Values)
        {
            if (group.ArchiveInputs is not {Count: > 0} || group.ArchiveInputs.Any(name => !state.FileContents.ContainsKey(name)) ||
                group.OutputDirectory == null || group.RenameFiles != null || group.ProjectedDirectories != null) return null;
            var root = workspaces!.Relocate(group.OutputDirectory);
            if (!FileProcessingPlanExecutor.IsInside(outputs, root) || !groupRoots.Add(root) ||
                groupRoots.Any(previous => !SamePath(previous, root) && StageCleanupOverlap(previous, root))) return null;
            if (!group.Completed)
            {
                if (group.OutputManifest != null) return null;
                continue;
            }
            if (group.OutputManifest == null || !Add(root, group.OutputManifest)) return null;
        }
        if (entries.Count != state.FileContents.Count || entries.Count > 100000) return null;
        foreach (var item in state.Active.Concat(state.Outputs.Values.SelectMany(x => x)))
        {
            var p = workspaces!.Relocate(item.Path);
            if (!FileProcessingPlanExecutor.IsInside(checkpoint, p) || !entries.ContainsKey(ArtifactManifestFiles.RelativeFile(checkpoint, p))) return null;
        }
        directories.Add(outputs);
        return (entries, directories);
    }
}

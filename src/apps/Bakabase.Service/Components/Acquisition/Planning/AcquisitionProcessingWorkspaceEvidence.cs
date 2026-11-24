using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionProcessingWorkspaceFiles(IReadOnlyList<ArtifactManifestEntry> Files,
    IReadOnlyList<string> StageArtifactIds)
{
    internal IReadOnlyList<AcquisitionRetainedDownloadFile> RetainedFiles { get; init; } = [];
    internal IReadOnlyList<string> PurgedStageIds { get; init; } = [];
    internal IReadOnlyList<string> RetainedDownloadIds { get; init; } = [];
    internal IReadOnlyList<string> CheckpointProofIds { get; init; } = [];
}

/// <summary>Reconstructs owned, delivered payload from immutable registrations and writer receipts.
/// Files under the allocation that have no matching producer remain outside the selection.</summary>
internal static partial class AcquisitionProcessingWorkspaceEvidence
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    internal static async Task<AcquisitionProcessingWorkspaceFiles?> FindAsync(BakabaseDbContext db,
        IArtifactOperationStore store, FileProcessingWorkspaceResolver workspaces, AcquisitionTaskDbModel task,
        AcquisitionPlan plan, string partId, AcquisitionStageDirectory stage, IReadOnlyList<int> ownerRuns,
        IReadOnlyList<ArtifactRecord> verifiedPublications, CancellationToken ct, string? previousWorkspaceReceipts = null)
    {
        if (stage is not {Ownership: "exclusive", Evidence: "taskAllocation", Allocation: {} allocation} ||
            allocation.OwnerKind != "acquisition" || allocation.OwnerId != task.Id.ToString(CultureInfo.InvariantCulture) ||
            allocation.Role != "processing-" + partId || !Same(stage.Directory, allocation.Directory) ||
            plan.StageDirectories?.Parts.GetValueOrDefault(partId)?.Processing != stage ||
            !Guid.TryParseExact(stage.BindingId, "N", out _) || verifiedPublications.Count == 0 ||
            !await workspaces.HasScopedDirectoryAsync(allocation, ct)) return null;
        var part = plan.Parts.Single(p => p.Id == partId);
        if (plan.StoragePolicy == null) return null;
        var sourceCleanup = ArtifactStoragePolicies.Resolve(plan.StoragePolicy, part.StoragePolicy).SourceCleanup;
        var checkpointOnly = previousWorkspaceReceipts != null;
        if (checkpointOnly && sourceCleanup != "keep") return null;
        if (sourceCleanup == "keep")
        {
            if (part.Processing?.Steps.Any(s => s.StoragePolicy is {CleanupIntermediates: false} or {SourceCleanup: "keep"}) == true)
                return null;
            verifiedPublications = await RetainedSourcePublicationsAsync(db, store, task.WorkflowRunId, partId, verifiedPublications, ct);
            if (verifiedPublications.Count == 0) return null;
        }
        var retainedDownloads = new Dictionary<string, AcquisitionRetainedDownload>(StringComparer.Ordinal);
        var owners = ownerRuns.Select(r => r.ToString(CultureInfo.InvariantCulture)).ToArray();
        var prefix = partId + ":process:stage:";
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
            owners.Contains(b.OwnerId) && b.Slot.StartsWith(prefix)).Select(b => new {b.OperationId, b.OwnerId, b.Slot}).ToListAsync(ct);
        var publications = verifiedPublications.Select(p => p.Id).ToArray();
        var selected = new Dictionary<string, ArtifactManifestEntry>(FileProcessingFiles.PathComparer);
        var committedFiles = new Dictionary<string, IReadOnlyList<ArtifactManifestEntry>>(StringComparer.Ordinal);
        var purgedStages = new HashSet<string>(StringComparer.Ordinal);
        var checkpointProofIds = new HashSet<string>(StringComparer.Ordinal);
        var removedInputs = new Dictionary<string, HashSet<ArtifactManifestEntry>>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            var committed = await store.GetArtifactAsync(binding.Slot[prefix.Length..], ct);
            if (committed is not {Kind: "processedFiles", Managed: true} || binding.OperationId != committed.OperationId ||
                committed.State is not (ArtifactLocationState.Available or ArtifactLocationState.Missing or ArtifactLocationState.Retired)) continue;
            var previouslyPurged = !Same(committed.RootDirectory, stage.Directory);
            if (checkpointOnly)
            {
                // Older stage cleanup retired this canonical download copy in place.
                // The completed receipt and readable absence prove exactly which copy was removed.
                if (previouslyPurged || committed.State != ArtifactLocationState.Retired ||
                    !await AcquisitionProcessingWorkspaceRemoval.CoversCompletedSelectionAsync(previousWorkspaceReceipts!, stage.BindingId!,
                        stage.Directory, committed.Manifest, ct) ||
                    !ArtifactCleanupAbsence.AllConfirmedAbsent(stage.Directory, committed.Files, ct)) continue;
                previouslyPurged = true;
            }
            else if (previouslyPurged)
            {
                // Earlier cleanup versions may have purged the visible working copy while leaving its
                // private checkpoints. The purged receipt proves the original directory, never its new occupants.
                if (sourceCleanup != "keep" || committed.State != ArtifactLocationState.Retired) continue;
                var record = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == committed.Id, ct);
                var originalRoot = await workspaces.ReadOriginalArtifactDirectoryAsync(record, ct);
                if (originalRoot == null || !Same(originalRoot, stage.Directory)) continue;
                committed = committed with {RootDirectory = stage.Directory, Files = ArtifactManifestFiles.ResolveFiles(stage.Directory, committed.Manifest)};
            }
            var metadata = Read(committed);
            if (metadata?.StageCommit is not {Version: 1} evidence || !AllowsCleanup(metadata, sourceCleanup) ||
                evidence.RunId.ToString(CultureInfo.InvariantCulture) != binding.OwnerId || evidence.TaskId != task.Id ||
                evidence.PartId != partId || evidence.BindingId != stage.BindingId || !Same(evidence.Directory, stage.Directory) ||
                evidence.Id != committed.OperationId || evidence.Id != ArtifactManifestFiles.Hash($"processing-stage-v1:{task.Id}:{partId}:{stage.BindingId}:{evidence.SourceArtifactId}") ||
                !committed.ParentArtifactIds.SequenceEqual([evidence.SourceArtifactId])) continue;
            var source = await store.GetArtifactAsync(evidence.SourceArtifactId, ct);
            if (source is not {Kind: "processedFiles", Managed: true}) continue;
            var parentMetadata = Read(source);
            if (parentMetadata == null || !AllowsCleanup(parentMetadata, sourceCleanup) ||
                evidence.SourceFingerprint != source.Fingerprint || metadata.InputArtifactId != source.Id ||
                !FileDispositionProofs.SameManifest(committed.Manifest, source.Manifest) ||
                !FileDispositionProofs.ValidFor(metadata.FileDisposition, source, committed)) continue;
            if (await store.GetOperationAsync(committed.OperationId, ct) is not {Kind: "files.process", State: ArtifactOperationState.Completed} operation ||
                operation.OutputArtifactId != committed.Id || operation.InputFingerprint != source.Fingerprint ||
                await store.GetOperationAsync(source.OperationId, ct) is not {Kind: "files.process", State: ArtifactOperationState.Completed} producer ||
                producer.OutputArtifactId != source.Id) continue;
            var custody = await AcquisitionTaskCleanupViews.OwnedClosuresAsync(db, [committed.Id, source.Id], owners, ct);
            if (!custody.Contains(committed.Id) || !custody.Contains(source.Id) ||
                !await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == source.OperationId &&
                    b.OwnerKind == "workflow" && b.OwnerId == binding.OwnerId && b.Slot.StartsWith(partId + ":process:"), ct)) continue;
            var path = Path.Combine(workspaces.StageCommitMetadataDirectory, evidence.Id + ".json");
            ArtifactManifestFiles.RejectLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024 * 1024) continue;
            var journal = JsonSerializer.Deserialize<AcquisitionProcessingStageCommitReceipt>(await File.ReadAllTextAsync(path, ct), Json);
            if (!OwnsCommittedFiles(committed, evidence, journal)) continue;
            var registeredSource = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == source.Id, ct);
            if (registeredSource == null) continue;
            var original = await workspaces.ReadOriginalArtifactDirectoryAsync(registeredSource, ct);
            if (original == null && checkpointOnly && source.State == ArtifactLocationState.Retired &&
                Same(source.RootDirectory, evidence.SourceDirectory) &&
                ArtifactCleanupPathReferences.StrictlyUnder(source.RootDirectory, stage.Directory) &&
                ArtifactCleanupAbsence.AllConfirmedAbsent(stage.Directory, source.Files, ct)) original = source.RootDirectory;
            if (original == null || !Same(evidence.SourceDirectory, original)) continue;
            var input = parentMetadata.InputArtifactId == null ? null : await store.GetArtifactAsync(parentMetadata.InputArtifactId, ct);
            if (input == null || !source.ParentArtifactIds.Contains(input.Id) ||
                !FileDispositionProofs.ValidFor(parentMetadata.FileDisposition, input, source)) continue;
            var delivered = (await ArtifactFileCoverage.EvaluateAsync(store, committed, publications, ct)).Complete;
            if (sourceCleanup == "keep")
            {
                if (!delivered || !FileDispositionProofs.SameManifest(source.Manifest, input.Manifest) ||
                    !await AcquisitionProcessingStageInput.WasCommittedSnapshotAsync(workspaces, task.Id, evidence.RunId,
                        partId, stage, input, source with {RootDirectory = original}, committed, ct, sourceCleanup)) continue;
                var external = await VerifiedRetainedDownloadAsync(db, store, plan.StageDirectories.Parts[partId].Download,
                    stage, task.Id, evidence.RunId, partId, input, ct);
                if (external == null) continue;
                retainedDownloads[external.ArtifactId] = external;
                var privateFiles = new Dictionary<string, ArtifactManifestEntry>(FileProcessingFiles.PathComparer);
                try
                {
                    await AddDeliveredCheckpointCopiesAsync(db, store, workspaces, stage, binding.OwnerId, partId, committed,
                        verifiedPublications, privateFiles, ct, checkpointOnly ? task.Id : null, checkpointProofIds);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                    InvalidDataException or ArgumentException or InvalidOperationException)
                { privateFiles.Clear(); }
                if (!AddEntries(privateFiles.Values.ToArray(), stage.Directory, stage.Directory, selected)) return null;
            }
            else if (!delivered)
            {
                // A failed earlier run can leave an independently produced snapshot of the
                // same original download a later verified publication actually consumed.
                if (input.Kind != "download" || !FileDispositionProofs.SameManifest(source.Manifest, input.Manifest) ||
                    !(await ArtifactFileCoverage.EvaluateAsync(store, input, publications, ct)).Complete ||
                    !await AcquisitionProcessingStageInput.WasCommittedSnapshotAsync(workspaces, task.Id, evidence.RunId,
                        partId, stage, input, source, committed, ct)) continue;
            }
            if (previouslyPurged) purgedStages.Add(committed.Id);
            committedFiles[committed.Id] = previouslyPurged ? [] : committed.Manifest.Entries;
            if (checkpointOnly) continue;
            var removed = ReplacedStageInputs(input, source, committed, parentMetadata.FileDisposition!, journal!);
            if (removed.Count > 0)
            {
                if (!removedInputs.TryGetValue(input.Id, out var members)) removedInputs[input.Id] = members = [];
                members.UnionWith(removed);
            }
            if (!delivered || parentMetadata.CompletedThroughStepId is not {} step ||
                parentMetadata.WorkspaceDirectoryScope != stage.Allocation) continue;
            // The visible stage has its own verified handoff. A missing, oversized or
            // damaged private checkpoint only withholds that checkpoint's input files.
            FileProcessingCheckpointInput? snapshot;
            try
            {
                var recipe = JsonSerializer.Deserialize<FileProcessingPlan>(parentMetadata.PlanJson, Json);
                if (recipe == null) continue;
                var locator = await workspaces.InspectOwnedCheckpointForStageCleanupAsync(FileArtifactProcessor.CheckpointKey(input, recipe),
                    source.OperationId, producer.Attempt, stage.Directory, ct);
                if (locator == null || !ArtifactCleanupPathReferences.StrictlyUnder(locator.Directory, stage.Directory) ||
                    !await workspaces.HasOwnedLocationAsync(locator, ct)) continue;
                var previous = source with {RootDirectory = evidence.SourceDirectory};
                snapshot = await FileProcessingPlanExecutor.ReadReclaimableInputAsync(locator.Directory, recipe, input,
                    previous, step, workspaces.IdentityPath, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or ArgumentException or InvalidOperationException)
            { continue; }
            if (snapshot != null && !AddEntries(snapshot.InputManifest.Entries, Path.Combine(snapshot.Directory, "inputs"),
                    stage.Directory, selected)) return null;
        }
        foreach (var (artifactId, entries) in committedFiles)
        {
            // A later successful stage commit can replace and unlink files in the same visible
            // allocation. Its durable receipt withdraws only those leaves of that exact input.
            // A newly created file at an old name is outside this cleanup confirmation.
            var remaining = removedInputs.TryGetValue(artifactId, out var removed)
                ? entries.Where(e => !removed.Contains(e)).ToArray() : entries;
            if (!AddEntries(remaining, stage.Directory, stage.Directory, selected)) return null;
        }
        return selected.Count == 0 ? null : new(selected.Values.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray(),
            committedFiles.Keys.Order(StringComparer.Ordinal).ToArray()) {RetainedDownloadIds = retainedDownloads.Keys.Order(StringComparer.Ordinal).ToArray(),
                RetainedFiles = retainedDownloads.Values.SelectMany(d => d.Files).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray(),
                PurgedStageIds = purgedStages.Order(StringComparer.Ordinal).ToArray(),
                CheckpointProofIds = checkpointProofIds.Order(StringComparer.Ordinal).ToArray()};
    }

    internal static IReadOnlyList<ArtifactManifestEntry> ReplacedStageInputs(ArtifactRecord input, ArtifactRecord source,
        ArtifactRecord committed, FileDispositionProof disposition, AcquisitionProcessingStageCommitReceipt journal)
    {
        if (journal.ReplacedInputsRemoved is not {Count: > 0} removed || input is not {Managed: true, Kind: "processedFiles"} ||
            !Same(input.RootDirectory, committed.RootDirectory) || journal.ArtifactId != committed.Id ||
            journal.SourceArtifactId != source.Id || !source.ParentArtifactIds.Contains(input.Id) ||
            !FileDispositionProofs.ValidFor(disposition, input, source)) return [];
        var recorded = removed.ToHashSet(StringComparer.Ordinal);
        var output = committed.Manifest.Entries.Select(e => e.RelativePath).ToHashSet(FileProcessingFiles.PathComparer);
        var replaced = disposition.Entries.Where(e => e.Disposition == "replaced" && recorded.Contains(e.Source.RelativePath) &&
            !output.Contains(e.Source.RelativePath)).Select(e => e.Source.RelativePath).ToHashSet(StringComparer.Ordinal);
        return input.Manifest.Entries.Where(e => replaced.Contains(e.RelativePath)).ToArray();
    }

    private static FileArtifactProcessingMetadata? Read(ArtifactRecord a) => a.MetadataJson == null ? null :
        JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(a.MetadataJson, Json);
    private static bool AllowsCleanup(FileArtifactProcessingMetadata? m, string sourceCleanup) =>
        m?.StoragePolicy is {CleanupIntermediates: true} policy && policy.SourceCleanup == sourceCleanup &&
        JsonSerializer.Deserialize<FileProcessingPlan>(m.PlanJson, Json) is {Steps: {} steps} &&
        !steps.Any(s => s.StoragePolicy is {CleanupIntermediates: false} or {SourceCleanup: "keep"});
    private static bool Same(string a, string b) => FileProcessingFiles.PathComparer.Equals(Path.GetFullPath(a), Path.GetFullPath(b));

    internal static bool OwnsCommittedFiles(ArtifactRecord committed, AcquisitionProcessingStageCommitReceipt evidence,
        AcquisitionProcessingStageCommitReceipt? journal)
    {
        if (journal == null || journal.Version != evidence.Version || journal.Id != evidence.Id || journal.TaskId != evidence.TaskId ||
            journal.RunId != evidence.RunId || journal.BindingId != evidence.BindingId || journal.PartId != evidence.PartId ||
            journal.SourceArtifactId != evidence.SourceArtifactId || journal.SourceFingerprint != evidence.SourceFingerprint ||
            journal.Intent != evidence.Intent || !Same(journal.SourceDirectory, evidence.SourceDirectory) ||
            !Same(journal.Directory, committed.RootDirectory) || journal.ArtifactId != committed.Id || journal.Fingerprint != committed.Fingerprint ||
            journal.OwnedArtifactId != null || evidence.OwnedArtifactId != null ||
            journal.ReusedFiles is not {Count: 0} || evidence.ReusedFiles is not {Count: 0} ||
            journal.Move is not {MoveFiles: true} move || evidence.Move is not {Completed: true, MoveFiles: true} prior ||
            move.SourceDirectory == null || move.PartId != journal.PartId || move.Stage != "processing" ||
            prior.SourceDirectory == null || prior.Stage != move.Stage || prior.PartId != move.PartId ||
            !Same(prior.Directory, move.Directory) || !Same(prior.SourceDirectory, move.SourceDirectory) ||
            !Same(move.Directory, journal.Directory) || !Same(move.SourceDirectory, journal.SourceDirectory) ||
            !move.Files.SequenceEqual(prior.Files) || move.Files.Count != committed.Manifest.Entries.Count ||
            move.Files.Any(f => !f.Completed)) return false;
        var expected = committed.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        return move.Files.All(f => expected.Remove(f.RelativePath.Replace('\\', '/'), out var e) && f.Length == e.Length &&
            (f.CopiedHash == null || string.Equals(f.CopiedHash, e.Sha256, StringComparison.OrdinalIgnoreCase))) && expected.Count == 0;
    }

    private static bool AddEntries(IReadOnlyList<ArtifactManifestEntry> entries, string directory, string stage,
        Dictionary<string, ArtifactManifestEntry> selected)
    {
        foreach (var entry in entries)
        {
            var path = FileProcessingFiles.Within(Path.Combine(directory, entry.RelativePath), stage);
            var relative = Path.GetRelativePath(stage, path).Replace('\\', '/');
            var mapped = entry with {RelativePath = relative};
            if (selected.TryGetValue(relative, out var old) && !FileDispositionProofs.SameEntry(old, mapped)) return false;
            selected[relative] = mapped;
        }
        return true;
    }
}

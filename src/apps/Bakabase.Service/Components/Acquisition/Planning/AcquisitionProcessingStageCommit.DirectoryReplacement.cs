using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal static partial class AcquisitionProcessingStageCommit
{
    // An extensionless archive can be replaced by an extracted directory with the same
    // name. The executor's measured disposition identifies its outputs. The frozen
    // cleanup policy decides whether to retain the archive or retire that working input.
    private sealed record DirectoryReplacement(ArtifactRecord Input, FileArtifactProcessingMetadata Metadata, bool RetainInput)
    {
        internal FileDispositionProof Proof => Metadata.FileDisposition!;
    }

    private static async Task<DirectoryReplacement?> ReplacementProofAsync(IArtifactOperationStore store,
        AcquisitionStageDirectory binding, ArtifactRecord output, CancellationToken ct)
    {
        var metadata = ReadMetadata(output);
        if (metadata?.StoragePolicy == null || metadata.InputArtifactId == null || metadata.FileDisposition == null) return null;
        var input = await store.GetArtifactAsync(metadata.InputArtifactId, ct);
        if (input is not {Managed: true, Kind: "processedFiles"} ||
            !Same(input.RootDirectory, binding.Directory) || !FileDispositionProofs.ValidFor(metadata.FileDisposition, input, output))
            return null;
        var keep = metadata.StoragePolicy.SourceCleanup == "keep" || ReadMetadata(input)?.StoragePolicy?.SourceCleanup == "keep";
        return keep || metadata.StoragePolicy.CleanupIntermediates ? new(input, metadata, keep) : null;
    }

    private static IReadOnlyList<string> FindDirectoryBlockers(AcquisitionStageDirectory binding, AcquisitionStageDirectoryMove move)
    {
        var blockers = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var relative in move.Files.Select(f => Path.GetDirectoryName(f.RelativePath)).Concat(move.EmptyDirectories))
        {
            if (relative == null) continue;
            for (var path = Path.Combine(binding.Directory, relative); path != null && !Same(path, binding.Directory); path = Path.GetDirectoryName(path))
                if (File.Exists(path)) blockers.Add(ArtifactManifestFiles.RelativeFile(binding.Directory, path));
        }
        return blockers.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    private static async Task<bool> RetainBlockingInputAsync(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingWorkspaceResolver workspaces, int taskId, int runId, string partId, AcquisitionStageDirectory binding,
        ArtifactRecord source, IReadOnlyList<string> blockers, CancellationToken ct)
    {
        var evidence = await ReplacementProofAsync(store, binding, source, ct);
        var replacements = evidence?.Proof.Entries.Where(e => e.Disposition == "replaced")
            .ToDictionary(e => e.Source.RelativePath, ArtifactManifestFiles.PathComparer);
        if (evidence == null || replacements == null || blockers.Any(p => !replacements.ContainsKey(p)))
            throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
        if (!evidence.RetainInput) return false;

        // The archived input and the executor's final result cannot both occupy the same visible name.
        // Keep both registrations and their original paths. Publication consumes the validated result manifest.
        var metadata = evidence.Metadata;
        var plan = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, Json);
        if (binding.Allocation == null || metadata.WorkspaceDirectoryScope != binding.Allocation ||
            !source.ParentArtifactIds.Contains(evidence.Input.Id) || metadata.CompletedThroughStepId == null ||
            plan?.Steps.LastOrDefault()?.Id != metadata.CompletedThroughStepId ||
            await store.GetOperationAsync(source.OperationId, ct) is not {State: ArtifactOperationState.Completed} operation)
            throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
        var location = await workspaces.InspectOwnedCheckpointForStageCleanupAsync(FileArtifactProcessor.CheckpointKey(evidence.Input, plan),
            source.OperationId, operation.Attempt, binding.Directory, ct);
        if (location == null || !ArtifactCleanupPathReferences.StrictlyUnder(location.Directory, binding.Directory) ||
            !await workspaces.HasOwnedLocationAsync(location, ct) ||
            !Same(source.RootDirectory, Path.Combine(FileProcessingPlanExecutor.CheckpointInputDirectory(
                location.Directory, plan, evidence.Input, workspaces.IdentityPath), "result")))
            throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
        // ValidFor already checks each mapped output's exact path, length and SHA. Check that
        // every file beneath a retained name was produced by the corresponding input.
        var mappedPaths = blockers.ToDictionary(p => p, p => replacements[p].Outputs.Select(f => f.RelativePath)
            .ToHashSet(StringComparer.Ordinal), ArtifactManifestFiles.PathComparer);
        var matched = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var file in source.Manifest.Entries)
        {
            for (var parent = Path.GetDirectoryName(file.RelativePath); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            {
                var relative = parent.Replace('\\', '/');
                if (!mappedPaths.TryGetValue(relative, out var outputs)) continue;
                if (!outputs.Contains(file.RelativePath)) throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
                matched.Add(relative);
                break;
            }
        }
        if (matched.Count != blockers.Count) throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
        var entries = blockers.Select(p => replacements[p].Source).ToArray();
        if (!await ReplacedInputsUnreferencedAsync(db, store, workspaces, taskId, runId, partId, binding, evidence.Input, entries, ct))
            throw new InvalidOperationException("STAGE_COMMIT_REFERENCED");
        foreach (var entry in entries)
            if (!await MatchesAsync(Path.Combine(binding.Directory, entry.RelativePath), entry, ct))
                throw new IOException("STAGE_COMMIT_TARGET_CHANGED");
        if ((await ArtifactManifestFiles.ValidateAsync(source.RootDirectory, source.Manifest, ct)).State != ArtifactLocationState.Available)
            throw new IOException("STAGE_COMMIT_SOURCE_CHANGED");
        return true;
    }

    // Called only under the artifact maintenance fence, after the durable move intent
    // has been written. A retry can finish the same commit without extracting again.
    private static async Task RemoveDirectoryBlockersAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver workspaces, int taskId, int runId, string partId,
        AcquisitionStageDirectory binding, ArtifactRecord source, AcquisitionProcessingStageCommitReceipt journal, CancellationToken ct)
    {
        if (journal.BlockingInputsToRemove is not {Count: > 0} blockers) return;
        var evidence = await ReplacementProofAsync(store, binding, source, ct);
        if (evidence == null || evidence.RetainInput) throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
        var entries = blockers.Select(relative => evidence.Proof.Entries.SingleOrDefault(e =>
            ArtifactManifestFiles.PathComparer.Equals(e.Source.RelativePath, relative) && e.Disposition == "replaced")).ToArray();
        if (entries.Any(e => e == null)) throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
        if (!await ReplacedInputsUnreferencedAsync(db, store, workspaces, taskId, runId, partId, binding,
                evidence.Input, entries.Select(e => e!.Source).ToArray(), ct))
            throw new InvalidOperationException("STAGE_COMMIT_REFERENCED");
        // Validate every affected replacement before removing any input. Once a move
        // has completed, its exact output bytes are verified at the stage location.
        foreach (var output in entries.SelectMany(e => e!.Outputs).DistinctBy(e => e.RelativePath))
        {
            var original = Path.Combine(source.RootDirectory, output.RelativePath);
            var current = File.Exists(original) ? original : Path.Combine(binding.Directory, output.RelativePath);
            if (!await MatchesAsync(current, output, ct)) throw new IOException("STAGE_COMMIT_SOURCE_CHANGED");
        }
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(binding.Directory, entry!.Source.RelativePath);
            storage.EnsureTreeMutationAllowed(path); ArtifactManifestFiles.RejectLinks(path);
            if (!File.Exists(path)) continue; // This journal may already have retired the exact input.
            var before = new FileInfo(path); var ticks = before.LastWriteTimeUtc.Ticks;
            if (!await MatchesAsync(path, entry.Source, ct)) throw new IOException("STAGE_COMMIT_TARGET_CHANGED");
            var after = new FileInfo(path);
            if (!after.Exists || after.Length != entry.Source.Length || after.LastWriteTimeUtc.Ticks != ticks)
                throw new IOException("STAGE_COMMIT_TARGET_CHANGED");
            File.Delete(path);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // name. Only a measured executor proof and the existing cleanup policy authorize
    // retiring that exact working input; unknown files and keep policies still conflict.
    private static async Task<(ArtifactRecord Input, FileDispositionProof Proof)?> ReplacementProofAsync(
        IArtifactOperationStore store, AcquisitionStageDirectory binding, ArtifactRecord output, CancellationToken ct)
    {
        var metadata = ReadMetadata(output);
        if (metadata?.StoragePolicy?.CleanupIntermediates != true || metadata.StoragePolicy.SourceCleanup == "keep" ||
            metadata.InputArtifactId == null || metadata.FileDisposition == null) return null;
        var input = await store.GetArtifactAsync(metadata.InputArtifactId, ct);
        if (input is not {Managed: true, Kind: "processedFiles"} ||
            ReadMetadata(input)?.StoragePolicy?.SourceCleanup == "keep" || !Same(input.RootDirectory, binding.Directory) ||
            !FileDispositionProofs.ValidFor(metadata.FileDisposition, input, output)) return null;
        return (input, metadata.FileDisposition);
    }

    private static async Task<IReadOnlyList<string>> FindReplaceableDirectoryBlockersAsync(IArtifactOperationStore store,
        AcquisitionStageDirectory binding, ArtifactRecord source, AcquisitionStageDirectoryMove move, CancellationToken ct)
    {
        var blockers = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var relative in move.Files.Select(f => Path.GetDirectoryName(f.RelativePath)).Concat(move.EmptyDirectories))
        {
            if (relative == null) continue;
            for (var path = Path.Combine(binding.Directory, relative); path != null && !Same(path, binding.Directory); path = Path.GetDirectoryName(path))
                if (File.Exists(path)) blockers.Add(Path.GetRelativePath(binding.Directory, path));
        }
        if (blockers.Count == 0) return [];
        var evidence = await ReplacementProofAsync(store, binding, source, ct);
        if (evidence == null || blockers.Any(relative => !evidence.Value.Proof.Entries.Any(e =>
                ArtifactManifestFiles.PathComparer.Equals(e.Source.RelativePath, relative) && e.Disposition == "replaced")))
            throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
        return blockers.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    // Called only under the artifact maintenance fence, after the durable move intent
    // has been written. A retry can finish the same commit without extracting again.
    private static async Task RemoveDirectoryBlockersAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver workspaces, int taskId, int runId, string partId,
        AcquisitionStageDirectory binding, ArtifactRecord source, AcquisitionProcessingStageCommitReceipt journal, CancellationToken ct)
    {
        if (journal.BlockingInputsToRemove is not {Count: > 0} blockers) return;
        var evidence = await ReplacementProofAsync(store, binding, source, ct)
            ?? throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
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

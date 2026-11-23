using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>A constraint to an existing execution snapshot, never permission to recreate it.</summary>
public sealed record FileProcessingCheckpointInput(string Directory, ArtifactManifest InputManifest,
    string PreviousStepId, string PreviousDirectory, string PreviousFingerprint)
{
    internal IReadOnlyList<FileProcessingMovedInput>? MovedInputs { get; init; }
}

internal sealed record FileProcessingMovedInput(string InputRelativePath, string CurrentPath);

public sealed partial class FileProcessingPlanExecutor
{
    internal static string CheckpointInputDirectory(string checkpointRoot, FileProcessingPlan plan, ArtifactRecord input,
        Func<string, string>? identityPath)
    {
        var files = input.Files.OrderBy(p => p, FileProcessingFiles.PathComparer).ToArray();
        var identity = CheckpointIdentity(plan, files, identityPath);
        var directories = ArtifactManifestFiles.ResolveDirectories(input.RootDirectory, input.Manifest)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (directories.Length > 0)
            identity = ArtifactManifestFiles.Hash(identity + "\ndirectories-v1\n" + JsonSerializer.Serialize(
                directories.Select(p => Path.GetRelativePath(input.RootDirectory, p).Replace('\\', '/')).ToArray()));
        return Path.Combine(checkpointRoot, identity);
    }

    internal static async Task<FileProcessingCheckpointInput?> ReadPinnedInputAsync(string checkpointRoot,
        FileProcessingPlan plan, ArtifactRecord input, ArtifactRecord previous, string previousStep,
        Func<string, string>? identityPath, CancellationToken ct)
    {
        var directory = CheckpointInputDirectory(checkpointRoot, plan, input, identityPath);
        var state = await ReadPinnedStateAsync(directory, ct);
        // Read-only archive volumes still belong to their registered source. The normal continuation
        // re-pins that input until the operation finishes; the private snapshot cannot replace it.
        if (state.ReadOnlyVolumeArtifactId != null || state.LineageVersion != 1 || state.InputManifest == null ||
            !state.BoundaryResults.TryGetValue(previousStep, out var boundary) || boundary.Manifest == null)
            return null;
        if (!SameManifestContents(state.InputManifest, input.Manifest) ||
            boundary.Manifest.Fingerprint != previous.Fingerprint ||
            !FileProcessingFiles.PathComparer.Equals(boundary.Directory, previous.RootDirectory))
            throw new InvalidDataException("The existing checkpoint belongs to different input or output evidence.");
        var inputs = Path.Combine(directory, "inputs");
        IReadOnlyList<FileProcessingMovedInput>? moved = null;
        if (state.Operations.Values.SelectMany(o => o.RenameFiles ?? []).Any(r => r.Move && IsInside(inputs, r.Source)))
        {
            moved = MapCommittedInputMoves(state, plan, directory, previousStep);
            if (moved == null) return null;
        }
        return new(directory, input.Manifest, previousStep, previous.RootDirectory, previous.Fingerprint) {MovedInputs = moved};
    }

    internal static async Task<FileProcessingCheckpointInput?> ReadReclaimableInputAsync(string root,
        FileProcessingPlan plan, ArtifactRecord input, ArtifactRecord produced, string step,
        Func<string, string>? identityPath, CancellationToken ct, string sourceCleanup = "delete")
    {
        var directory = CheckpointInputDirectory(root, plan, input, identityPath);
        var state = await ReadCleanupEvidenceAsync(directory, step, ct);
        // An input path in the journal can refer to a borrowed volume or an earlier move. Only an
        // unchanged, privately materialized input set from this completed checkpoint is eligible.
        if (state.LineageVersion != 1 || !state.Completed || state.PendingOperation != null ||
            state.ReadOnlyVolumeArtifactId != null || state.ReadOnlyVolumeMembers != null ||
            state.StoragePolicy is not {CleanupIntermediates: true} || state.StoragePolicy.SourceCleanup != sourceCleanup ||
            state.StepPolicies.Values.Any(p => !p.CleanupIntermediates || (sourceCleanup == "keep" ? p.SourceCleanup != "keep" : p.SourceCleanup == "keep")) ||
            plan.Steps.Any(s => s.StoragePolicy is {CleanupIntermediates: false} or {SourceCleanup: "keep"}) ||
            state.Operations.Values.SelectMany(o => o.RenameFiles ?? []).Any(f => f.Move) ||
            !FileDispositionProofs.SameManifest(state.InputManifest, input.Manifest) ||
            !state.BoundaryResults.TryGetValue(step, out var boundary) ||
            !FileProcessingFiles.PathComparer.Equals(boundary.Directory, produced.RootDirectory) ||
            !FileDispositionProofs.SameManifest(boundary.Manifest, produced.Manifest) ||
            !state.Outputs.TryGetValue("download", out var files)) return null;
        var inputs = Path.Combine(directory, "inputs");
        var paths = ArtifactManifestFiles.ResolveFiles(inputs, input.Manifest).ToHashSet(FileProcessingFiles.PathComparer);
        if (files.Count != paths.Count || !paths.SetEquals(files.Select(f => f.Path)) ||
            state.RetainedFiles.Any(paths.Contains)) return null;
        return new(directory, input.Manifest, step, produced.RootDirectory, produced.Fingerprint);
    }

    private static async Task<State> ReadPinnedStateAsync(string directory, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(directory);
        var path = Path.Combine(directory, "state.json");
        ArtifactManifestFiles.RejectLinks(path);
        if (!Directory.Exists(directory) || !File.Exists(path))
            throw new InvalidDataException("The pinned processing checkpoint is missing; it was not recreated.");
        if (new FileInfo(path).Length > 64 * 1024 * 1024)
            throw new InvalidDataException("The pinned processing checkpoint exceeds the inspection limit.");
        return JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(path, ct), Json)
            ?? throw new InvalidDataException("The pinned processing checkpoint is invalid.");
    }

    private static async Task ValidatePinnedInputAsync(string directory, FileProcessingPlan plan,
        FileProcessingCheckpointInput proof, CancellationToken ct)
    {
        var state = await ReadPinnedStateAsync(directory, ct);
        if (state.LineageVersion != 1 || state.InputManifest == null ||
            !SameManifestContents(state.InputManifest, proof.InputManifest) ||
            !state.BoundaryResults.TryGetValue(proof.PreviousStepId, out var boundary) || boundary.Manifest == null ||
            boundary.Manifest.Fingerprint != proof.PreviousFingerprint ||
            !FileProcessingFiles.PathComparer.Equals(boundary.Directory, proof.PreviousDirectory))
            throw new InvalidDataException("The pinned checkpoint evidence changed before continuation.");
        var inputs = Path.Combine(directory, "inputs");
        // Full SHA and declared-directory verification runs on every continuation, under the
        // executor gate. Never use mtime/length as a content cache, or refill missing inputs.
        if (!Directory.Exists(inputs))
            throw new InvalidDataException("The complete private input snapshot is unavailable.");
        if (proof.MovedInputs == null) await VerifyOutputManifest(inputs, proof.InputManifest, ct);
        else await VerifyCommittedInputMovesAsync(state, plan, directory, proof, ct);
        await VerifyOutputManifest(boundary.Directory, boundary.Manifest, ct);
    }
}

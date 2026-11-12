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
    internal static async Task<FileProcessingCheckpointInput?> ReadPinnedInputAsync(string checkpointRoot,
        FileProcessingPlan plan, ArtifactRecord input, ArtifactRecord previous, string previousStep,
        Func<string, string>? identityPath, CancellationToken ct)
    {
        var files = input.Files.OrderBy(p => p, FileProcessingFiles.PathComparer).ToArray();
        var identity = CheckpointIdentity(plan, files, identityPath);
        var directories = ArtifactManifestFiles.ResolveDirectories(input.RootDirectory, input.Manifest)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (directories.Length > 0)
            identity = ArtifactManifestFiles.Hash(identity + "\ndirectories-v1\n" + JsonSerializer.Serialize(
                directories.Select(p => Path.GetRelativePath(input.RootDirectory, p).Replace('\\', '/')).ToArray()));
        var directory = Path.Combine(checkpointRoot, identity);
        var state = await ReadPinnedStateAsync(directory, ct);
        // Old states do not acquire new trust from current files or instructions.
        if (state.LineageVersion != 1 || state.InputManifest == null ||
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

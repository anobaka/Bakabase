using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    // This is a location proof for every original byte, not extraction lineage. It
    // cannot substitute an archive's children for a missing original archive.
    private static IReadOnlyList<FileProcessingMovedInput>? MapCommittedInputMoves(State state,
        FileProcessingPlan plan, string directory, string previousStep)
    {
        if (state.LineageVersion != 1 || state.MaterializationVersion != 2 || state.InputManifest == null ||
            !state.Outputs.TryGetValue("download", out var original)) return null;
        var boundaryIndex = plan.Steps.FindIndex(s => s.Id == previousStep);
        if (boundaryIndex < 0) return null;
        var inputs = Path.Combine(directory, "inputs");
        ArtifactManifestFiles.RejectLinks(inputs);
        if (!Directory.Exists(inputs)) return null;
        var expectedPaths = ArtifactManifestFiles.ResolveFiles(inputs, state.InputManifest);
        if (original.Count != expectedPaths.Count || !original.Select(a => a.Path)
                .ToHashSet(FileProcessingFiles.PathComparer).SetEquals(expectedPaths)) return null;
        var steps = plan.Steps.Take(boundaryIndex + 1)
            .Where(s => s.Op is "renameExtension" or "renameFile" or "renameLiteral" or "moveFile" or "rebaseDirectory")
            .ToDictionary(s => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.Id)))[..16] + "-rename",
                StringComparer.Ordinal);
        var moves = new Dictionary<string, string>(FileProcessingFiles.PathComparer);
        string Key(string path) => ArtifactManifestFiles.RelativeFile(directory, path);
        foreach (var (key, operation) in state.Operations.Where(pair => pair.Value.RenameFiles != null))
        {
            // A journal saved before its step/output commit cannot grant continuation.
            if (!steps.TryGetValue(key, out var step) || !operation.Completed || operation.OutputDirectory == null ||
                !state.Outputs.TryGetValue(step.Id, out var output)) return null;
            var destination = FileProcessingFiles.Within(operation.OutputDirectory, Path.Combine(directory, "outputs"));
            var entries = operation.RenameFiles!;
            var targets = entries.Select(e => FileProcessingFiles.Within(Path.Combine(destination, e.RelativePath), destination)).ToArray();
            if (targets.Distinct(FileProcessingFiles.PathComparer).Count() != targets.Length ||
                output.Count != targets.Length || !output.Select(a => a.Path).ToHashSet(FileProcessingFiles.PathComparer).SetEquals(targets)) return null;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; var target = targets[i];
                if (!IsInside(inputs, entry.Source) && !IsInside(Path.Combine(directory, "outputs"), entry.Source)) return null;
                var sourceKey = Key(entry.Source); var targetKey = Key(target);
                if (!FileProcessingFiles.PathComparer.Equals(entry.Content.RelativePath, sourceKey) ||
                    !state.FileContents.TryGetValue(sourceKey, out var sourceContent) || !SameContent(sourceContent, entry.Content) ||
                    !state.FileContents.TryGetValue(targetKey, out var targetContent) || !SameContent(targetContent, entry.Content) ||
                    !state.FileTransitions.TryGetValue(sourceKey, out var transitions) ||
                    !transitions.Contains(targetKey, FileProcessingFiles.PathComparer)) return null;
                if (entry.Move && (transitions.Count != 1 || !moves.TryAdd(entry.Source, target))) return null;
            }
        }
        var mapped = new List<FileProcessingMovedInput>();
        var leaves = new HashSet<string>(FileProcessingFiles.PathComparer);
        foreach (var file in state.InputManifest.Entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            var current = FileProcessingFiles.Within(Path.Combine(inputs, file.RelativePath), inputs);
            var seen = new HashSet<string>(FileProcessingFiles.PathComparer);
            while (true)
            {
                if (!seen.Add(current) || seen.Count > plan.Steps.Count + 1 ||
                    !state.FileContents.TryGetValue(Key(current), out var content) || !SameContent(file, content)) return null;
                ArtifactManifestFiles.RejectLinks(current);
                if (ExistingPrivateFile(current)) break;
                if (!moves.TryGetValue(current, out var next)) return null;
                current = next;
            }
            // Different originals cannot collapse to one file merely because their bytes match.
            if (!leaves.Add(current)) return null;
            mapped.Add(new(file.RelativePath, current));
        }
        return mapped;
    }

    private static bool ExistingPrivateFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("A private input file was replaced by another filesystem entry.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static async Task VerifyCommittedInputMovesAsync(State state, FileProcessingPlan plan, string directory,
        FileProcessingCheckpointInput proof, CancellationToken ct)
    {
        var mapped = MapCommittedInputMoves(state, plan, directory, proof.PreviousStepId);
        if (mapped == null || mapped.Count != proof.MovedInputs!.Count || mapped.Where((entry, i) =>
                entry.InputRelativePath != proof.MovedInputs[i].InputRelativePath ||
                !FileProcessingFiles.PathComparer.Equals(entry.CurrentPath, proof.MovedInputs[i].CurrentPath)).Any())
            throw new InvalidDataException("The complete private input move receipt changed before continuation.");
        var inputs = Path.Combine(directory, "inputs");
        var originalPaths = ArtifactManifestFiles.ResolveFiles(inputs, proof.InputManifest).ToHashSet(FileProcessingFiles.PathComparer);
        if (FileProcessingFiles.Enumerate(inputs).Any(path => !originalPaths.Contains(path)))
            throw new InvalidDataException("The private input snapshot contains unrecorded files.");
        try { ArtifactManifestFiles.VerifyDirectories(inputs, proof.InputManifest); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException("A recorded private input directory is missing; it was not recreated.", error);
        }
        // Always hash all original bytes at their proven current paths. The previous
        // boundary is independently verified by the caller; it may contain only a subset.
        var actual = await ArtifactManifestFiles.CaptureAsync(directory, mapped.Select(e => e.CurrentPath).ToArray(), ct);
        var contents = actual.Entries.ToDictionary(e => FileProcessingFiles.Within(Path.Combine(directory, e.RelativePath), directory),
            FileProcessingFiles.PathComparer);
        var expected = proof.InputManifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        if (mapped.Any(e => !SameContent(expected[e.InputRelativePath], contents[e.CurrentPath])))
            throw new InvalidDataException("A moved private input differs from the complete original content manifest.");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed class FileProcessingCheckpointPlacementException() : InvalidOperationException(
    "The previous processing checkpoint contains progress or unrecognized files. Keep it and continue from a verified output, or restore its original storage before proceeding.");

public sealed partial class FileProcessingPlanExecutor
{
    internal static async Task<T> WithUnstartedCheckpointAsync<T>(string root, FileProcessingPlan plan,
        ArtifactRecord input, Func<string, string> identityPath, Func<Task<T>> create, CancellationToken ct,
        Func<Task>? validateEmptyOwner = null)
    {
        var normalized = input.Files.OrderBy(p => p, FileProcessingFiles.PathComparer).ToArray();
        var identity = CheckpointIdentity(plan, normalized, identityPath);
        if (input.Manifest.Directories is {Count: > 0} directories)
            identity = ArtifactManifestFiles.Hash(identity + "\ndirectories-v1\n" + JsonSerializer.Serialize(
                directories.OrderBy(p => FileProcessingFiles.Within(Path.Combine(input.RootDirectory, p), input.RootDirectory), StringComparer.Ordinal)
                    .Select(p => p.Replace('\\', '/')).ToArray()));
        var directory = Path.Combine(root, identity);
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            ArtifactManifestFiles.RejectLinks(root); ArtifactManifestFiles.RejectLinks(directory);
            // Known-size admission can stop before the first state file. Only the resolver
            // can prove this still-existing empty directory belongs to this installation
            // and exact checkpoint locator; an arbitrary empty folder is not evidence.
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
            {
                if (validateEmptyOwner == null) throw new FileProcessingCheckpointPlacementException();
                await validateEmptyOwner();
                ArtifactManifestFiles.RejectLinks(root);
                if (!Directory.Exists(root) || Directory.EnumerateFileSystemEntries(root).Any())
                    throw new FileProcessingCheckpointPlacementException();
                return await create();
            }
            // Older writers created the input staging parent before their first capacity check.
            // Those exact empty manifest-derived directory prefixes contain no progress;
            // any staged file, even zero bytes, or an unknown directory remains protected.
            if (!Directory.Exists(root) || !Directory.Exists(directory) ||
                !Directory.EnumerateFileSystemEntries(root).SequenceEqual([directory]) ||
                !MetadataOnlyCheckpointEntries(directory, input.Manifest))
                throw new FileProcessingCheckpointPlacementException();
            var stateFile = Path.Combine(directory, "state.json"); ArtifactManifestFiles.RejectLinks(stateFile);
            if (new FileInfo(stateFile).Length is <= 0 or > 8 * 1024 * 1024)
                throw new FileProcessingCheckpointPlacementException();
            var bytes = await File.ReadAllBytesAsync(stateFile, ct);
            State? state;
            try
            {
                using var document = JsonDocument.Parse(bytes);
                var fields = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
                if (fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length ||
                    new[] {"completed", "materializationVersion", "lineageVersion", "directoryVersion", "generatedDirectoryVersion",
                        "inputManifest", "finalManifest", "pendingOperation", "outputs", "operations", "active", "activeDirectories",
                        "directoryOutputs", "fileContents", "fileTransitions", "boundaryResults", "passwords", "retainedFiles",
                        "stagedFiles", "finalFiles", "finalExtractedFiles", "intermediatesReclaimedAt"}.Any(f => !fields.Contains(f)))
                    throw new FileProcessingCheckpointPlacementException();
                state = JsonSerializer.Deserialize<State>(bytes, new JsonSerializerOptions(Json)
                    {UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow});
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException && error is not FileProcessingCheckpointPlacementException)
            { throw new FileProcessingCheckpointPlacementException(); }
            if (state is not {Completed: false, LineageVersion: 1, MaterializationVersion: 2, DirectoryVersion: 1,
                    GeneratedDirectoryVersion: 1, PendingOperation: null,
                    FinalManifest: null, IntermediatesReclaimedAt: null} || state.InputManifest == null ||
                !SameManifestContents(state.InputManifest, input.Manifest) ||
                state.Outputs is not {Count: 0} || state.Operations is not {Count: 0} || state.Active is not {Count: 0} ||
                state.ActiveDirectories is not {Count: 0} || state.DirectoryOutputs is not {Count: 0} ||
                state.FileContents is not {Count: 0} || state.FileTransitions is not {Count: 0} ||
                state.BoundaryResults is not {Count: 0} || state.Passwords is not {Count: 0} ||
                state.RetainedFiles is not {Count: 0} || state.StagedFiles is not {Count: 0} ||
                state.FinalFiles is not {Count: 0} || state.FinalExtractedFiles is not {Count: 0})
                throw new FileProcessingCheckpointPlacementException();
            // The old state and locator are never rewritten or removed; creating the new
            // location under the same executor lock prevents racing an old active step.
            return await create();
        }
        catch (InvalidDataException) {throw new FileProcessingCheckpointPlacementException();}
        finally {gate.Release();}
    }

    private static bool MetadataOnlyCheckpointEntries(string directory, ArtifactManifest input)
    {
        var stateFile = Path.Combine(directory, "state.json");
        var staging = Path.Combine(directory, "inputs.pending");
        var entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
        foreach (var entry in entries)
            if (!FileProcessingFiles.PathComparer.Equals(entry, stateFile) && !FileProcessingFiles.PathComparer.Equals(entry, staging))
                return false;
        if (!File.Exists(stateFile)) return false;
        if (!entries.Contains(staging, FileProcessingFiles.PathComparer)) return true;
        ArtifactManifestFiles.RejectLinks(staging);
        if (!Directory.Exists(staging)) return false;
        var allowed = new HashSet<string>(FileProcessingFiles.PathComparer) {staging};
        // Resolve the validated manifest lexically; filesystem links are rejected by
        // the guarded directory walk, before any linked target could be inspected.
        foreach (var target in ArtifactManifestFiles.ResolveFiles(staging, input))
            for (var parent = Path.GetDirectoryName(target); parent != null && !FileProcessingFiles.PathComparer.Equals(parent, staging);
                 parent = Path.GetDirectoryName(parent)) allowed.Add(parent);
        var pending = new Stack<string>(); pending.Push(staging);
        while (pending.TryPop(out var current))
        {
            ArtifactManifestFiles.RejectLinks(current);
            foreach (var child in Directory.EnumerateFileSystemEntries(current))
            {
                if (!allowed.Contains(child) || !Directory.Exists(child)) return false;
                pending.Push(child);
            }
        }
        return true;
    }
}

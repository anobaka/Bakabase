using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    // Resolve companions from this step's registered inputs, using logical relative paths
    // so separate upstream nodes can supply different physical directories. Never enumerate
    // a parent directory: filename membership alone does not authorize unrelated disk files.
    private static List<Artifact> ExpandAuthorizedArchiveSelection(IReadOnlyList<Artifact> supplied,
        IReadOnlyList<Artifact> selected)
    {
        var wanted = selected.Select(file => file.RelativePath).ToHashSet(FileProcessingFiles.PathComparer);
        var paths = supplied.Select(file => file.RelativePath).ToArray();
        var sfx = CompressedFileHelper.DetectSfxMultipartCandidates(paths);
        var sfxPaths = sfx.SelectMany(candidate => candidate.Files).ToHashSet(FileProcessingFiles.PathComparer);
        foreach (var candidate in sfx.Where(candidate => candidate.Files.Any(wanted.Contains)))
            wanted.UnionWith(candidate.Files);
        foreach (var group in CompressedFileHelper.DetectCompressedFileGroups(
                     paths.Where(path => !sfxPaths.Contains(path)).ToArray(), inspectFileSizes: false))
            if (group.Files.Count > 1 && group.Files.Any(wanted.Contains)) wanted.UnionWith(group.Files);
        return supplied.Where(file => wanted.Contains(file.RelativePath)).ToList();
    }

    private static FileProcessingPlanResult ArchiveGroupChanged(State state, string stepId) =>
        new(false, state.Active.Select(a => a.Path).ToArray(), null,
            "The archive group's saved input identity cannot be reconciled with this step. Files and completed work were retained. Revise the processing plan from a verified artifact before continuing.",
            stepId, FailureCode: "ArchiveGroupInputChanged");

    private static bool HasUnboundArchiveResponse(State state, string stepKey) =>
        state.Passwords.Keys.Concat(state.PendingOperation == null ? [] : new[] {state.PendingOperation})
            .Where(key => key.StartsWith(stepKey + "-", StringComparison.Ordinal))
            .Any(key => !state.Operations.TryGetValue(key, out var operation) || operation.ArchiveInputs == null);

    private static bool BindArchiveGroups(State state, string stepKey, IReadOnlyList<DetectedArchiveGroup> groups,
        IReadOnlyDictionary<string, string> originalPaths, out bool changed)
    {
        changed = false;
        var proposed = groups.Select(group => group.Files.Select(path => ContentKey(state, originalPaths[path])).ToList()).ToArray();
        var additions = new List<(Operation Operation, List<string> Inputs)>();
        // Validate the whole step before adopting even its first cached group. A later changed
        // group must not lend an index-based output or saved password to different inputs.
        foreach (var (key, operation) in state.Operations.Where(pair => pair.Key.StartsWith(stepKey + "-", StringComparison.Ordinal)))
        {
            if (!int.TryParse(key[(stepKey.Length + 1)..], out var index) || index < 0 || index >= proposed.Length)
                return false;
            var inputs = proposed[index];
            if (operation.ArchiveInputs != null)
            {
                if (!operation.ArchiveInputs.SequenceEqual(inputs, FileProcessingFiles.PathComparer)) return false;
            }
            else
            {
                if (!LegacyArchiveMembersMatch(state, operation, inputs)) return false;
                additions.Add((operation, inputs));
            }
        }
        foreach (var (operation, inputs) in additions) operation.ArchiveInputs = inputs;
        changed = additions.Count > 0;
        return true;
    }

    private static bool LegacyArchiveMembersMatch(State state, Operation operation, IReadOnlyList<string> inputs)
    {
        if (state.LineageVersion != 1 || !operation.Completed || operation.OutputDirectory == null ||
            operation.OutputManifest is not {Entries.Count: > 0} manifest ||
            manifest.Fingerprint != ArtifactManifestFiles.Fingerprint(manifest.Entries, manifest.Directories)) return false;
        try
        {
            var outputRoot = FileProcessingFiles.Within(operation.OutputDirectory, state.ExecutionRoot!);
            var outputs = manifest.Entries.Select(entry =>
            {
                var path = FileProcessingFiles.Within(Path.Combine(outputRoot, entry.RelativePath), outputRoot);
                return (Key: ContentKey(state, path), Entry: entry);
            }).ToArray();
            var outputKeys = outputs.Select(o => o.Key).ToHashSet(FileProcessingFiles.PathComparer);
            if (outputKeys.Count != outputs.Length || outputs.Any(o =>
                    !state.FileContents.TryGetValue(o.Key, out var recorded) || !SameContent(recorded, o.Entry))) return false;
            // Outputs owned by another operation cannot prove which old index consumed them.
            if (state.Operations.Values.Any(other => !ReferenceEquals(other, operation) && other.OutputDirectory != null &&
                    (FileProcessingFiles.PathComparer.Equals(outputRoot, Path.GetFullPath(other.OutputDirectory)) ||
                     IsInside(outputRoot, other.OutputDirectory) || IsInside(other.OutputDirectory, outputRoot)))) return false;
            var incoming = state.FileTransitions.Where(pair => pair.Value.Any(outputKeys.Contains)).ToArray();
            if (incoming.Length != inputs.Count || !incoming.Select(p => p.Key).ToHashSet(FileProcessingFiles.PathComparer).SetEquals(inputs))
                return false;
            // An input can feed other steps too, but its edge to this operation must cover every
            // committed output, and its own path/content receipt must still exist.
            return incoming.All(pair => state.FileContents.ContainsKey(pair.Key) && outputKeys.IsSubsetOf(pair.Value));
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException)
        {
            return false;
        }
    }
}

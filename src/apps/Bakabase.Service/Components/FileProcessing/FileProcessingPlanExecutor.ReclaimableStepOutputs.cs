using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    // The caller verifies the task's exclusive allocation, the owners of each supplied boundary
    // and the final publication. Private operation outputs have no artifact registration of their
    // own. Bind their writer, exact bytes and replacement chain back to those registrations.
    internal static async Task<IReadOnlyList<ArtifactManifestEntry>> ReadReclaimableStepOutputsAsync(
        FileProcessingCheckpointInput checkpoint, FileProcessingPlan plan, ArtifactRecord input, ArtifactRecord produced,
        IReadOnlyDictionary<string, ArtifactRecord> priorBoundaries, CancellationToken ct)
    {
        if (priorBoundaries.Count == 0 || plan.Steps.Count < 2 || plan.Steps[^1].Id != checkpoint.PreviousStepId ||
            !produced.Managed || produced.Kind != "processedFiles" || !produced.ParentArtifactIds.SequenceEqual([input.Id]) ||
            produced.Fingerprint != checkpoint.PreviousFingerprint ||
            !FileProcessingFiles.PathComparer.Equals(produced.RootDirectory, checkpoint.PreviousDirectory) ||
            !FileDispositionProofs.SameManifest(checkpoint.InputManifest, input.Manifest)) return [];
        var root = checkpoint.Directory;
        var state = await ReadPinnedStateAsync(root, ct);
        if (!state.Completed || state.PendingOperation != null || state.LineageVersion != 1 ||
            state.ReadOnlyVolumeArtifactId != null || state.ReadOnlyVolumeMembers != null ||
            state.StoragePolicy?.CleanupIntermediates != true ||
            state.StepPolicies == null || state.FinalFiles == null || state.Active == null || state.RetainedFiles == null ||
            state.Outputs == null || state.Operations == null || state.FileContents == null || state.FileTransitions == null ||
            state.BoundaryResults == null || state.BoundaryResults.Values.Any(b => b?.Files == null) ||
            plan.Steps.Any(s => s.ArchiveMemberSelector != null || s.StoragePolicy is {CleanupIntermediates: false} or {SourceCleanup: "keep"}) ||
            !OperationDirectoriesAreDisjoint(state) ||
            !FileDispositionProofs.SameManifest(state.InputManifest, input.Manifest) ||
            !FileDispositionProofs.SameManifest(state.FinalManifest, produced.Manifest) ||
            !MatchesRegisteredBoundary(state, checkpoint.PreviousStepId, produced) ||
            !state.FinalFiles.ToHashSet(FileProcessingFiles.PathComparer)
                .SetEquals(ArtifactManifestFiles.ResolveFiles(produced.RootDirectory, produced.Manifest))) return [];
        var resolved = NewState(plan, state.StoragePolicy, null, state.ExtractionContentsAsRoot, null).StepPolicies;
        if (resolved.Count != state.StepPolicies.Count || resolved.Any(p =>
                !state.StepPolicies.TryGetValue(p.Key, out var saved) || !saved.CleanupIntermediates ||
                saved.SourceCleanup != p.Value.SourceCleanup || saved.CleanupIntermediates != p.Value.CleanupIntermediates)) return [];
        state.ExecutionRoot = root;
        var protectedPaths = state.BoundaryResults.Values.SelectMany(b => b.Files).Concat(state.Active)
            .Select(f => f.Path).Concat(state.FinalFiles).Concat(state.RetainedFiles)
            .ToHashSet(FileProcessingFiles.PathComparer);
        var coverage = new ReplacementCoverage(state, ct);
        var result = new Dictionary<string, ArtifactManifestEntry>(FileProcessingFiles.PathComparer);
        foreach (var step in plan.Steps.SkipLast(1))
        {
            ct.ThrowIfCancellationRequested();
            if (step.Op != "extractArchive" || !priorBoundaries.TryGetValue(step.Id, out var prior) ||
                prior.Id == produced.Id || !prior.Managed || prior.Kind != "processedFiles" ||
                !prior.ParentArtifactIds.SequenceEqual([input.Id]) || !MatchesRegisteredBoundary(state, step.Id, prior) ||
                !state.Outputs.TryGetValue(step.Id, out var outputs)) continue;
            var written = RegisteredExtractionOutputs(state, root, step.Id, ct);
            if (written == null) continue;
            var entries = prior.Manifest.Entries.ToDictionary(f => f.RelativePath, StringComparer.Ordinal);
            var boundary = state.BoundaryResults[step.Id].Files.ToDictionary(f => f.RelativePath.Replace('\\', '/'), FileProcessingFiles.PathComparer);
            foreach (var file in outputs)
            {
                ct.ThrowIfCancellationRequested();
                var logicalPath = file.RelativePath.Replace('\\', '/');
                if (!file.Extracted || file.StepId != step.Id || file.Origins is not {Count: > 0} ||
                    protectedPaths.Contains(file.Path) || !written.TryGetValue(file.Path, out var actual) ||
                    !entries.TryGetValue(logicalPath, out var registered) || !SameContent(actual, registered) ||
                    !boundary.TryGetValue(logicalPath, out var copy) || copy.StepId != step.Id ||
                    copy.Origins == null || !copy.Origins.SequenceEqual(file.Origins) ||
                    !FileProcessingFiles.PathComparer.Equals(copy.Path, Path.Combine(prior.RootDirectory, registered.RelativePath)) ||
                    !state.FileContents.TryGetValue(ContentKey(state, copy.Path), out var copyContent) || !SameContent(copyContent, registered) ||
                    !state.FileContents.TryGetValue(ContentKey(state, file.Path), out var privateContent) || !SameContent(privateContent, registered) ||
                    !coverage.IsComplete(file.Path)) continue;
                var relative = ArtifactManifestFiles.RelativeFile(root, file.Path);
                if (!result.TryAdd(relative, registered with {RelativePath = relative})) return [];
            }
        }
        return result.Values.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static bool MatchesRegisteredBoundary(State state, string step, ArtifactRecord registration) =>
        state.BoundaryResults.TryGetValue(step, out var boundary) && registration.Manifest.Fingerprint == registration.Fingerprint &&
        FileProcessingFiles.PathComparer.Equals(boundary.Directory, registration.RootDirectory) &&
        FileDispositionProofs.SameManifest(boundary.Manifest, registration.Manifest) &&
        boundary.Files.Count == registration.Manifest.Entries.Count && boundary.Files.Select(f => f.Path)
            .ToHashSet(FileProcessingFiles.PathComparer).SetEquals(ArtifactManifestFiles.ResolveFiles(registration.RootDirectory, registration.Manifest));

    private static bool OperationDirectoriesAreDisjoint(State state)
    {
        var roots = new HashSet<string>(FileProcessingFiles.PathComparer);
        foreach (var op in state.Operations.Values)
        {
            if (op == null) return false;
            if (op.OutputDirectory != null && !roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(op.OutputDirectory)))) return false;
        }
        foreach (var root in roots)
            for (var parent = Path.GetDirectoryName(root); parent != null; parent = Path.GetDirectoryName(parent))
                if (roots.Contains(parent)) return false;
        return true;
    }

    private static Dictionary<string, ArtifactManifestEntry>? RegisteredExtractionOutputs(State state, string root,
        string step, CancellationToken ct, bool promoting = false)
    {
        var result = new Dictionary<string, ArtifactManifestEntry>(FileProcessingFiles.PathComparer);
        var prefix = StepKey(step) + "-";
        var outputRoot = Path.Combine(root, "outputs");
        foreach (var (key, op) in state.Operations.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            ct.ThrowIfCancellationRequested();
            if (!int.TryParse(key.AsSpan(prefix.Length), out var index) || key != prefix + index || index < 0)
                return null;
            // A retained group wrote no outputs. It grants no ownership. Other independently
            // committed writers can still promote their own files into this mixed boundary.
            // Reclamation callers continue to require complete coverage of the entire step.
            if (promoting && op is {Completed: false, OutputManifest: null, ExtractionStagingId: null,
                RetainedGroupToken.Length: 64, ArchiveInputs.Count: > 0, OutputDirectory: {} empty} &&
                IsInside(outputRoot, empty) && !Path.Exists(FileProcessingFiles.Within(empty, outputRoot)) &&
                !Path.Exists(ExtractionStagingDirectory(empty, op, root))) continue;
            if (!op.Completed || op.ArchiveInputs is not {Count: > 0} || op.RenameFiles != null ||
                op.OutputDirectory == null || !IsInside(outputRoot, op.OutputDirectory) ||
                op.OutputManifest is not {Entries.Count: > 0} manifest ||
                manifest.Fingerprint != ArtifactManifestFiles.Fingerprint(manifest.Entries, manifest.Directories)) return null;
            var paths = ArtifactManifestFiles.ResolveFiles(op.OutputDirectory, manifest);
            var outputKeys = paths.Select(p => ContentKey(state, p)).ToHashSet(FileProcessingFiles.PathComparer);
            if (op.ArchiveInputs.Any(input => !state.FileContents.ContainsKey(input) ||
                    !state.FileTransitions.TryGetValue(input, out var children) || !outputKeys.IsSubsetOf(children))) return null;
            for (var i = 0; i < paths.Count; i++)
                if (!result.TryAdd(paths[i], manifest.Entries[i])) return null;
        }
        return result;
    }
}

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

public sealed partial class FileProcessingPlanExecutor
{
    private sealed record BoundaryCleanupState(string Directory, State State, State Projection);

    internal static async Task<bool> IsRegisteredBoundaryAsync(string root, ArtifactManifest manifest, CancellationToken ct) =>
        await ReadBoundaryCleanupState(root, manifest, ct) != null;

    // Boundary roots may be inside a named output directory, so inspect a bounded ancestor
    // chain. A matching directory name is never evidence: the committed boundary manifest,
    // exact file set and per-file execution ledger must all agree with the registered output.
    private static async Task<BoundaryCleanupState?> ReadBoundaryCleanupState(string root, ArtifactManifest manifest, CancellationToken ct)
    {
        if (Path.GetFileName(root) == "result") return null;
        var directory = Path.GetDirectoryName(root);
        for (var depth = 0; directory != null && depth < 12;
             directory = Path.GetDirectoryName(directory), depth++)
        {
            var path = Path.Combine(directory, "state.json"); ArtifactManifestFiles.RejectLinks(path);
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length > 64L * 1024 * 1024) return null;
            var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(path, ct), Json);
            if (state is not {LineageVersion: 1} || state.InputManifest == null || state.PendingOperation != null) return null;
            var boundary = state.BoundaryResults.Values.SingleOrDefault(b => FileProcessingFiles.PathComparer.Equals(
                Path.GetFullPath(b.Directory), Path.GetFullPath(root)));
            if (boundary?.Manifest == null || !SameManifestContents(boundary.Manifest, manifest) ||
                !boundary.Files.Select(f => f.Path).ToHashSet(FileProcessingFiles.PathComparer)
                    .SetEquals(ArtifactManifestFiles.ResolveFiles(root, manifest))) return null;
            state.ExecutionRoot = directory;
            // This is a proof projection only; never mutate the unfinished plan's Completed,
            // Active or FinalManifest fields to make a boundary look like a completed plan.
            // Materialized boundary copies do not add a rename/extract transition: the
            // executor proves them by the same relative-path/content manifest as its active
            // outputs. Recover only those ledger outputs whose exact bytes and relative name
            // are present in this immutable boundary, even if the plan later changed Active.
            var entries = manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
            var retained = boundary.Files.Concat(state.Outputs.Values.SelectMany(files => files))
                .Where(file => entries.TryGetValue(file.RelativePath.Replace('\\', '/'), out var entry) &&
                    state.FileContents.TryGetValue(ContentKey(state, file.Path), out var content) && SameContent(content, entry))
                .DistinctBy(file => file.Path, FileProcessingFiles.PathComparer).ToList();
            var proof = new State {ExecutionRoot = directory, LineageVersion = 1, FinalManifest = manifest,
                Active = retained, FileContents = state.FileContents, FileTransitions = state.FileTransitions};
            return new(directory, state, proof);
        }
        return null;
    }

    internal static async Task<IReadOnlyList<string>> GetReclaimableBoundaryFilesAsync(string root,
        ArtifactManifest manifest, CancellationToken ct, bool overrideRetention = false, bool verifyContents = true)
    {
        var checkpoint = await ReadBoundaryCleanupState(root, manifest, ct);
        if (checkpoint == null) return [];
        return await BoundaryCandidates(checkpoint, ct, overrideRetention, verifyContents);
    }

    private static async Task<IReadOnlyList<string>> BoundaryCandidates(BoundaryCleanupState checkpoint, CancellationToken ct, bool overrideRetention, bool verifyContents = true)
    {
        var state = checkpoint.State;
        var protectedFiles = state.BoundaryResults.Values.SelectMany(b => b.Files).Select(f => f.Path)
            .Concat(state.FinalFiles).Concat(overrideRetention ? [] : state.RetainedFiles).ToHashSet(FileProcessingFiles.PathComparer);
        var allowed = new[] {"inputs", "outputs", "merged-inputs"}.Select(n => Path.Combine(checkpoint.Directory, n)).ToArray();
        var candidates = state.Outputs.Values.SelectMany(v => v).Select(v => v.Path).Concat(state.StagedFiles)
            .Select(Path.GetFullPath).Distinct(FileProcessingFiles.PathComparer)
            .Where(p => allowed.Any(r => IsInside(r, p)) && !protectedFiles.Contains(p) && File.Exists(p)).ToArray();
        var result = new List<string>();
        var coverage = new ReplacementCoverage(checkpoint.Projection, ct);
        foreach (var path in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (coverage.IsComplete(path) && (verifyContents ? await ContentsStillMatch(state, path, ct)
                    : state.FileContents.TryGetValue(ContentKey(state, path), out var expected) &&
                      ArtifactCleanupPreviewFiles.MatchesLength(path, expected.Length))) result.Add(path);
        }
        return result;
    }

    internal async Task ReclaimBoundaryIntermediatesAsync(string root, ArtifactManifest manifest,
        IReadOnlySet<string> approvedFiles, CancellationToken ct, bool overrideRetention = false, ArtifactCleanupFileSnapshot? snapshot = null)
    {
        var before = await ReadBoundaryCleanupState(root, manifest, ct);
        if (before == null) return;
        var gate = Gates.GetOrAdd(before.Directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var current = await ReadBoundaryCleanupState(root, manifest, ct);
            if (current == null || current.Directory != before.Directory) return;
            var files = await BoundaryCandidates(current, ct, overrideRetention, verifyContents: snapshot == null);
            if (files.Any(p => !approvedFiles.Contains(p)) || snapshot == null && !(await ArtifactManifestFiles.ValidateAsync(root, manifest, ct)).Available) return;
            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                ArtifactManifestFiles.RejectLinks(path);
                if (File.Exists(path) && (snapshot == null ? await ContentsStillMatch(current.State, path, ct)
                        : current.State.FileContents.TryGetValue(ContentKey(current.State, path), out var recorded) &&
                          snapshot.Matches(path, recorded.Length, ct))) File.Delete(path);
            }
            current.State.IntermediatesReclaimedAt = DateTime.UtcNow;
            var stateFile = Path.Combine(current.Directory, "state.json");
            var temporary = stateFile + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(current.State, Json), ct);
            File.Move(temporary, stateFile, true);
        }
        finally { gate.Release(); }
    }
}

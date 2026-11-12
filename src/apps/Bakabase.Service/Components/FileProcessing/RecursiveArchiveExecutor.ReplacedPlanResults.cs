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

public sealed partial class RecursiveArchiveExecutor
{
    internal sealed record ReplacedPlanResult(string Root, ArtifactManifest Manifest,
        string EvidenceFingerprint, IReadOnlyList<string> Files);

    // The path convention locates checkpoints only; three exact manifests and measured
    // per-file transitions authorize a result. External/borrowed inputs never qualify.
    internal static async Task<ReplacedPlanResult?> ReplacedPlanResultAsync(string resultRoot,
        ArtifactManifest expected, CancellationToken ct, bool verifyContents = true)
    {
        var directory = Path.GetDirectoryName(resultRoot)!;
        if (Path.GetFileName(resultRoot) != "result" || Path.GetFileName(directory) != "recursive") return null;
        var stateFile = Path.Combine(directory, "recursive-state.json");
        ArtifactManifestFiles.RejectLinks(stateFile);
        if (!File.Exists(stateFile)) return null;
        var json = await File.ReadAllTextAsync(stateFile, ct);
        var state = JsonSerializer.Deserialize<State>(json, Json);
        if (state is not {Completed: true, LineageVersion: 1, StoragePolicy.CleanupIntermediates: true} ||
            state.PendingArchiveId != null || state.Inputs.Count == 0 ||
            state.Inputs.Any(f => f.InState || f.StoragePolicy?.CleanupIntermediates != true || !HasCompleteReplacement(state, f)) ||
            state.Nodes.Any(n => !n.Completed || n.StoragePolicy?.CleanupIntermediates != true) ||
            !SameManifest(Manifest(state.FinalFiles, f => f.LogicalPath,
                state.FinalDirectories.Select(d => d.LogicalPath).ToArray()), expected)) return null;
        var input = Manifest(state.Inputs, f => f.Path, state.InputDirectories.Select(d => d.Path).ToArray());
        var matches = new List<(string Root, FileProcessingPlanExecutor.State State, string Json)>();
        var plans = Path.Combine(Path.GetDirectoryName(directory)!, "plan");
        ArtifactManifestFiles.RejectLinks(plans);
        if (!Directory.Exists(plans)) return null;
        foreach (var checkpoint in Directory.EnumerateDirectories(plans))
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(checkpoint);
            var name = Path.GetFileName(checkpoint);
            if (name.Length != 64 || !name.All(Uri.IsHexDigit)) continue;
            var file = Path.Combine(checkpoint, "state.json");
            ArtifactManifestFiles.RejectLinks(file);
            if (!File.Exists(file)) continue;
            var text = await File.ReadAllTextAsync(file, ct);
            var plan = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(text, Json);
            if (plan is not {Completed: true, LineageVersion: 1, FinalManifest: not null} ||
                !SameManifest(plan.FinalManifest, input)) continue;
            matches.Add((Path.Combine(checkpoint, "result"), plan, text));
        }
        if (matches.Count != 1) return null;
        var match = matches[0];
        if (!FileProcessingPlanExecutor.ResultMayBeReplaced(match.State, match.Root, input)) return null;
        var files = new List<string>();
        var expectedPaths = ArtifactManifestFiles.ResolveFiles(match.Root, input).ToHashSet(FileProcessingFiles.PathComparer);
        if (!Directory.Exists(match.Root)) return null;
        // Unknown siblings cannot silently become deletion candidates.
        if (FileProcessingFiles.Enumerate(match.Root).Any(p => !expectedPaths.Contains(p))) return null;
        foreach (var fact in input.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var path = FileProcessingFiles.Within(Path.Combine(match.Root, fact.RelativePath), match.Root);
            ArtifactManifestFiles.RejectLinks(path);
            if (!File.Exists(path)) continue; // Exact partial cleanup is idempotent.
            if (!(verifyContents ? await StillMatches(path, fact.Length, fact.Sha256, ct)
                    : ArtifactCleanupPreviewFiles.MatchesLength(path, fact.Length))) return null;
            files.Add(path);
        }
        return files.Count == 0 ? null : new(match.Root, input,
            ArtifactManifestFiles.Hash(json + "\n" + match.Json), files);
    }

    // Caller holds the artifact maintenance fence and has verified either the registered
    // recursive result or a fully covered final publication. Neither proof comes from paths.
    internal async Task ReclaimReplacedPlanResultAsync(string resultRoot, ArtifactManifest expected,
        IReadOnlySet<string> approved, CancellationToken ct, ArtifactCleanupFileSnapshot? snapshot = null)
    {
        var directory = Path.GetDirectoryName(resultRoot)!;
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var proof = await ReplacedPlanResultAsync(resultRoot, expected, ct, verifyContents: snapshot == null);
            if (proof == null || proof.Files.Any(p => !approved.Contains(p))) return;
            await FileProcessingPlanExecutor.ReclaimReplacedResultAsync(proof.Root, proof.Manifest, approved, ct, snapshot);
        }
        finally { gate.Release(); }
    }
}

public sealed partial class FileProcessingPlanExecutor
{
    internal static bool ResultMayBeReplaced(State state, string root, ArtifactManifest expected) =>
        state is {Completed: true, LineageVersion: 1, StoragePolicy.CleanupIntermediates: true, PendingOperation: null} &&
        state.RetainedFiles.Count == 0 && state.BoundaryResults.Count == 0 &&
        state.StepPolicies.Values.All(p => p.CleanupIntermediates) &&
        FileDispositionProofs.SameManifest(state.FinalManifest, expected) &&
        ArtifactManifestFiles.ResolveFiles(root, expected).ToHashSet(FileProcessingFiles.PathComparer).SetEquals(state.FinalFiles);

    internal static async Task ReclaimReplacedResultAsync(string root, ArtifactManifest expected,
        IReadOnlySet<string> approved, CancellationToken ct, ArtifactCleanupFileSnapshot? snapshot = null)
    {
        var directory = Path.GetDirectoryName(root)!;
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var stateFile = Path.Combine(directory, "state.json");
            ArtifactManifestFiles.RejectLinks(stateFile);
            var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(stateFile, ct), Json);
            if (state == null || !ResultMayBeReplaced(state, root, expected)) return;
            var paths = ArtifactManifestFiles.ResolveFiles(root, expected);
            if (FileProcessingFiles.Enumerate(root).Any(p => !paths.Contains(p, FileProcessingFiles.PathComparer))) return;
            foreach (var entry in expected.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var path = FileProcessingFiles.Within(Path.Combine(root, entry.RelativePath), root);
                ArtifactManifestFiles.RejectLinks(path);
                if (!File.Exists(path)) continue;
                if (!approved.Contains(path)) return;
                if (snapshot != null)
                {
                    if (!snapshot.Matches(path, entry.Length, ct)) return;
                }
                else
                {
                    var actual = await ArtifactManifestFiles.CaptureAsync(root, [path], ct);
                    if (actual.Entries.Count != 1 || !FileDispositionProofs.SameEntry(entry, actual.Entries[0])) return;
                }
                File.Delete(path);
            }
            // Keep the checkpoint receipt and all unknown directories for audit/recovery.
            RemoveEmptyDirectories(root, ct);
        }
        finally { gate.Release(); }
    }
}

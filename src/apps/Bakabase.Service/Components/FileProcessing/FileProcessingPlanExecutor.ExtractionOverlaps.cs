using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    // A current-directory capture may include both an archive and its previously extracted
    // files. Only exact, verified copies can share a final path. No overwrite, filename-only
    // deduplication, or adoption of an unrecorded file is permitted by this reconciliation.
    private static async Task CoalesceVerifiedExtractionOverlapsAsync(FileProcessingStep step, State state,
        List<Artifact> output, HashSet<string> consumed,
        IReadOnlyDictionary<string, FileProcessingPathProjectionEntry> projection,
        IReadOnlyDictionary<string, ArtifactManifestEntry> extractionReceipts, CancellationToken ct)
    {
        if (state.LineageVersion != 1 || output.Count == 0) return;
        var retained = state.Active.Where(a => !consumed.Contains(a.Path))
            .GroupBy(a => a.RelativePath.Replace('\\', '/'), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var overlaps = new List<(int Index, Artifact Retained, Artifact Extracted)>();
        for (var index = 0; index < output.Count; index++)
        {
            var extracted = output[index];
            if (!retained.TryGetValue(extracted.RelativePath.Replace('\\', '/'), out var matches)) continue;
            // Keep ambiguous inputs and absent/changed receipts on the normal conflict path.
            if (matches.Length != 1 || matches[0].Origins == null || extracted.Origins == null ||
                !state.FileContents.TryGetValue(ContentKey(state, matches[0].Path), out var original) ||
                !state.FileContents.TryGetValue(ContentKey(state, extracted.Path), out var replacement) ||
                !extractionReceipts.TryGetValue(extracted.Path, out var committed) ||
                string.IsNullOrEmpty(original.Sha256) || !SameContent(original, replacement) ||
                !SameContent(replacement, committed)) return;
            overlaps.Add((index, matches[0], extracted));
        }
        if (overlaps.Count == 0) return;

        // Verify every collision before changing any lineage. Extraction can be slow, so
        // the earlier input check cannot alone establish that retained bytes still match.
        await VerifyContents(state, overlaps.SelectMany(pair => new[] {pair.Retained.Path, pair.Extracted.Path}), ct);
        var reused = overlaps.Select(pair => pair.Retained.Path).ToHashSet(FileProcessingFiles.PathComparer);
        CheckProjection(step.Id, state.Active.Where(a => !consumed.Contains(a.Path) && !reused.Contains(a.Path)).Select(PathEntry)
            .Concat(output.Select(a => projection.GetValueOrDefault(a.Path) ?? PathEntry(a)))
            .Concat(state.ActiveDirectories.Select(PathEntry)), portable: step.ExtractionLayout != null);

        foreach (var (index, original, extracted) in overlaps)
        {
            output[index] = extracted with
            {
                // The archive is replaced; the already present file survives unchanged.
                Origins = extracted.Origins!.Concat(original.Origins!).Distinct().ToArray(),
                // A directory is generated only if both sources establish that fact.
                GeneratedDirectories = (extracted.GeneratedDirectories ?? [])
                    .Intersect(original.GeneratedDirectories ?? [], StringComparer.Ordinal).ToArray()
            };
            consumed.Add(original.Path);
            AddTransition(state, original.Path, [extracted.Path]);
        }
    }
}

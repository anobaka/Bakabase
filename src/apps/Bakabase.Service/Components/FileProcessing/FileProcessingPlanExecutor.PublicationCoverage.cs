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
    // A registered boundary is a copy of an execution output, not an artifact ancestor
    // of later boundaries. Prove that copy through the executor's actual transition ledger;
    // never manufacture a parent edge or infer replacement from equal hashes alone.
    internal static async Task<string?> BoundaryPublicationProofAsync(IArtifactOperationStore store,
        ArtifactRecord source, ArtifactRecord publication, IReadOnlyDictionary<string, ArtifactRecord> ancestors,
        IReadOnlyList<ArchiveQuarantineReceipt> receipts, CancellationToken ct, bool reviewedProcessingBoundary = false)
    {
        try
        {
            if (source.Kind != "processedFiles" || !source.Managed || publication.Kind != (reviewedProcessingBoundary ? "processedFiles" : "publishedFiles") ||
                source.MetadataJson == null || source.Manifest.Entries.Count == 0) return null;
            var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(source.MetadataJson, Json);
            if (metadata?.InputArtifactId == null || metadata.CompletedThroughStepId == null ||
                !source.ParentArtifactIds.SequenceEqual([metadata.InputArtifactId])) return null;
            var operation = await store.GetOperationAsync(source.OperationId, ct);
            if (operation is not {State: ArtifactOperationState.Completed, Kind: "files.process"} ||
                operation.OutputArtifactId != source.Id) return null;
            var input = await store.GetArtifactAsync(metadata.InputArtifactId, ct);
            if (input == null || operation.InputFingerprint != input.Fingerprint) return null;
            string OriginalRoot(ArtifactRecord artifact)
            {
                if (artifact.State is not (ArtifactLocationState.Quarantined or ArtifactLocationState.Retired)) return artifact.RootDirectory;
                var receipt = receipts.SingleOrDefault(r => r.ArtifactId == artifact.Id &&
                    r.Status is "Moving" or "Quarantined" or "Deleting" or "Purged");
                return receipt != null && FileProcessingFiles.PathComparer.Equals(receipt.QuarantineRoot, artifact.RootDirectory)
                    ? receipt.OriginalRoot : artifact.RootDirectory;
            }
            var sourceRoot = OriginalRoot(source);
            var directory = Path.GetDirectoryName(sourceRoot);
            State? state = null;
            for (var depth = 0; directory != null && depth < 12; directory = Path.GetDirectoryName(directory), depth++)
            {
                var path = Path.Combine(directory, "state.json"); ArtifactManifestFiles.RejectLinks(path);
                if (!File.Exists(path)) continue;
                state = await ReadPinnedStateAsync(directory, ct);
                break;
            }
            if (directory == null || state is not {Completed: true, LineageVersion: 1, PendingOperation: null} ||
                state.StepPolicies == null || !reviewedProcessingBoundary && (state.StoragePolicy?.CleanupIntermediates != true ||
                state.StepPolicies.Values.Any(p => p == null || !p.CleanupIntermediates)) ||
                state.BoundaryResults == null || state.Outputs == null || state.FileContents == null || state.FileTransitions == null ||
                state.Active == null || state.FinalFiles == null || state.RetainedFiles == null ||
                state.InputManifest == null || state.FinalManifest == null || !SameManifestContents(state.InputManifest, input.Manifest) ||
                !state.BoundaryResults.TryGetValue(metadata.CompletedThroughStepId, out var boundary) || boundary.Manifest == null ||
                !FileProcessingFiles.PathComparer.Equals(boundary.Directory, sourceRoot) ||
                !SameManifestContents(boundary.Manifest, source.Manifest) ||
                !boundary.Files.Select(f => f.Path).ToHashSet(FileProcessingFiles.PathComparer)
                    .SetEquals(ArtifactManifestFiles.ResolveFiles(sourceRoot, source.Manifest))) return null;
            state.ExecutionRoot = directory;
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, Json);
            if (plan?.Steps == null || !reviewedProcessingBoundary && (!state.StepPolicies.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(plan.Steps.Select(s => s.Id)) ||
                plan.Steps.Any(s => s.StoragePolicy?.SourceCleanup is not (null or "delete")))) return null;
            // Keeping the original download is independent of reclaiming processing copies.
            // Explicit per-step retention still wins, and frozen resolved policies must agree
            // with their recorded recipe instead of silently inheriting today's defaults.
            var resolved = NewState(plan, state.StoragePolicy, null, state.ExtractionContentsAsRoot, null).StepPolicies;
            if (!reviewedProcessingBoundary && resolved.Any(p => p.Value.SourceCleanup != state.StepPolicies[p.Key].SourceCleanup ||
                p.Value.CleanupIntermediates != state.StepPolicies[p.Key].CleanupIntermediates)) return null;
            var order = new[] {"download"}.Concat(plan.Steps.Select(s => s.Id)).ToArray();
            var boundaryIndex = Array.IndexOf(order, metadata.CompletedThroughStepId);
            if (boundaryIndex < 1 || reviewedProcessingBoundary && boundaryIndex == order.Length - 1 || order.Distinct(StringComparer.Ordinal).Count() != order.Length) return null;
            // The anchor must be an actual publication ancestor committed by this exact
            // completed execution. A retired anchor can still prove lineage via its durable
            // quarantine receipt and immutable manifest; its payload need not be recreated.
            var anchors = new List<ArtifactRecord>();
            foreach (var anchor in ancestors.Values.Where(a => a.Kind == "processedFiles" && a.MetadataJson != null))
            {
                var info = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(anchor.MetadataJson!, Json);
                var anchorPlan = info == null ? null : JsonSerializer.Deserialize<FileProcessingPlan>(info.PlanJson, Json);
                if (info?.InputArtifactId != input.Id || !anchor.ParentArtifactIds.SequenceEqual([input.Id]) ||
                    anchorPlan == null || IdentityJson(anchorPlan) != IdentityJson(plan) ||
                    !SameManifestContents(anchor.Manifest, state.FinalManifest) ||
                    !state.FinalFiles.ToHashSet(FileProcessingFiles.PathComparer)
                        .SetEquals(ArtifactManifestFiles.ResolveFiles(OriginalRoot(anchor), anchor.Manifest))) continue;
                var committed = await store.GetOperationAsync(anchor.OperationId, ct);
                if (committed is {State: ArtifactOperationState.Completed, Kind: "files.process"} && committed.OutputArtifactId == anchor.Id &&
                    committed.InputFingerprint == input.Fingerprint)
                    anchors.Add(anchor);
            }
            if (anchors.Count != 1 || !(reviewedProcessingBoundary && anchors[0].Id == publication.Id) && !(await ArtifactFileCoverage.EvaluateAsync(store, anchors[0], [publication.Id], ct)).Complete) return null;
            var entries = source.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
            if (boundary.Files.Count != entries.Count) return null;
            var coverage = new ReplacementCoverage(state, ct);
            foreach (var file in boundary.Files)
            {
                var relative = file.RelativePath.Replace('\\', '/');
                var step = file.StepId ?? "download";
                var index = Array.IndexOf(order, step);
                if (index < 0 || index > boundaryIndex || file.Origins == null || file.Origins.Count == 0 ||
                    !entries.TryGetValue(relative, out var entry) ||
                    !FileProcessingFiles.PathComparer.Equals(file.Path, Path.Combine(sourceRoot, relative)) ||
                    !state.FileContents.TryGetValue(ContentKey(state, file.Path), out var copied) || !SameContent(copied, entry) ||
                    !state.Outputs.TryGetValue(step, out var outputs)) return null;
                var original = outputs.Where(f => f.StepId == file.StepId && f.RelativePath.Replace('\\', '/') == relative &&
                    f.Origins != null && f.Origins.SequenceEqual(file.Origins) &&
                    state.FileContents.TryGetValue(ContentKey(state, f.Path), out var content) && SameContent(content, entry)).ToArray();
                if (original.Length != 1 || state.RetainedFiles.Contains(original[0].Path, FileProcessingFiles.PathComparer) ||
                    state.RetainedFiles.Contains(file.Path, FileProcessingFiles.PathComparer) ||
                    !coverage.IsComplete(original[0].Path)) return null;
            }
            // Cleanup timestamps are not execution evidence and may change when private
            // files are reclaimed before their registered boundary. Everything else is pinned.
            state.IntermediatesReclaimedAt = null;
            return ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new
                {State = state, directory, SourceId = source.Id, AnchorId = anchors[0].Id, Anchor = anchors[0].Manifest,
                    PublicationId = publication.Id, publication.Fingerprint}, Json));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return null; }
    }
}

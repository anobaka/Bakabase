using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed record ReviewedProcessingBoundary(string ArtifactId, string OperationId, string Fingerprint, string Root, string DownloadId, string ResultId, int RunId,
    string Proof, int FileCount, long Bytes);
internal sealed record ReviewedBoundaryApproval(ReviewedProcessingBoundary Source, Func<CancellationToken, Task<bool>> Verify);

public sealed partial class PublishedArtifactAncestorCleanup
{
    internal async Task<IReadOnlyList<ReviewedProcessingBoundary>> ReviewedBoundariesAsync(ReviewedCheckpointWait wait,
        Func<string, Task> guard, CancellationToken ct, string? selectedId = null)
    {
        try { return await ReadReviewedBoundariesAsync(wait, guard, ct, selectedId); }
        catch (Exception ex) when (UnavailableReviewEvidence(ex)) { return []; }
    }

    internal static bool UnavailableReviewEvidence(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException;

    private async Task<IReadOnlyList<ReviewedProcessingBoundary>> ReadReviewedBoundariesAsync(ReviewedCheckpointWait wait,
        Func<string, Task> guard, CancellationToken ct, string? selectedId)
    {
        var owner = wait.RunId.ToString(CultureInfo.InvariantCulture);
        var receipts = await quarantine.GetReceiptsAsync(ct);
        var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner).ToListAsync(ct);
        var result = new List<ReviewedProcessingBoundary>();
        var referenceGuard = new ArtifactCleanupReferenceGuard(db, workspaces, cachePreviewReads: selectedId == null);
        foreach (var part in wait.Parts)
        {
            var raw = await store.GetArtifactAsync(part.DownloadId, ct);
            var output = await store.GetArtifactAsync(part.ResultId, ct);
            if (raw is not {Kind: "download", State: ArtifactLocationState.Available} || raw.OperationId != part.DownloadOperationId ||
                output is not {Kind: "processedFiles", Managed: true, State: ArtifactLocationState.Available} ||
                output.OperationId != part.ResultOperationId || output.Fingerprint != part.ResultFingerprint ||
                !bindings.Any(b => b.OperationId == raw.OperationId && b.Slot == part.TargetId + ":artifact.download") ||
                !bindings.Any(b => b.OperationId == output.OperationId && b.Slot.StartsWith(part.TargetId + ":process:", StringComparison.Ordinal)))
                return [];
            if (raw.ParentArtifactIds.Count != 0 || await db.Set<ArtifactConsumerDbModel>().AnyAsync(c => c.OperationId == raw.OperationId &&
                c.ReleasedAt == null && (c.OwnerKind != "workflow" || c.OwnerId != owner), ct)) continue;
            await guard(raw.RootDirectory);
            await guard(output.RootDirectory);
            if (!HasLayout(raw, ct) || !HasLayout(output, ct)) return [];
            var graph = await AncestorsAsync(output, ct);
            if (!graph.ContainsKey(raw.Id) || (await store.GetOperationAsync(output.OperationId, ct)) is not
                {State: ArtifactOperationState.Completed, Kind: "files.process", OutputArtifactId: {} produced} || produced != output.Id) return [];
            var candidateIds = selectedId == null ? await BoundaryCandidateIdsAsync([raw.Id], ct) : new List<string>{selectedId};
            foreach (var id in candidateIds.Where(id => !graph.ContainsKey(id)))
            {
                var candidate = await store.GetArtifactAsync(id, ct);
                if (candidate is not {Managed: true, Kind: "processedFiles", MetadataJson: not null} ||
                    !candidate.ParentArtifactIds.SequenceEqual([raw.Id]) ||
                    candidate.State is not (ArtifactLocationState.Available or ArtifactLocationState.Quarantined or ArtifactLocationState.Retired)) continue;
                var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(candidate.MetadataJson, Json);
                if (string.IsNullOrWhiteSpace(metadata?.CompletedThroughStepId) ||
                    !bindings.Any(b => b.OperationId == candidate.OperationId && b.Slot == part.TargetId + ":process:" + metadata.CompletedThroughStepId) ||
                    await db.Set<ArtifactOperationBindingDbModel>().AnyAsync(b => b.OperationId == candidate.OperationId &&
                        (b.OwnerKind != "workflow" || b.OwnerId != owner || b.Slot != part.TargetId + ":process:" + metadata.CompletedThroughStepId), ct) ||
                    await db.Set<ArtifactLineageDbModel>().AnyAsync(l => l.ParentArtifactId == id, ct) ||
                    await db.Set<ArtifactConsumerDbModel>().AnyAsync(c => c.OperationId == candidate.OperationId && c.ReleasedAt == null, ct)) continue;
                var receipt = receipts.SingleOrDefault(r => r.ArtifactId == id && r.Status is "Quarantined" or "Moving" or "Deleting" or "Purged");
                if (receipt != null && (receipt.Status == "Purged" || !receipt.OutputArtifactIds.SequenceEqual([output.Id])) ||
                    candidate.State != ArtifactLocationState.Available && receipt == null) continue;
                var original = receipt?.OriginalRoot ?? candidate.RootDirectory;
                await guard(original);
                var files = ArtifactManifestFiles.ResolveFiles(original, candidate.Manifest);
                if (receipt == null && (!HasLayout(candidate, ct) || files.Any(path => DeferredEmptyDirectoryTree.InspectPrivateFile(path) == null)) ||
                    await store.HasOtherRegisteredFilesAsync(files, id, [], ct) ||
                    await referenceGuard.HasReferencesAsync(candidate with {RootDirectory = original, Files = files}, [wait.RunId], ct,
                        reviewedBoundary: new(wait.RunId, part.TargetId, metadata.CompletedThroughStepId, id, raw.Id, output.Id, raw.RootDirectory))) continue;
                var proof = await FileProcessingPlanExecutor.BoundaryPublicationProofAsync(store, candidate, output, graph, receipts, ct,
                    reviewedProcessingBoundary: true);
                if (proof == null) continue;
                var identity = ArtifactManifestFiles.Hash(wait.Identity + ":" + proof);
                if (receipt != null && !await quarantine.MatchesReviewedBoundaryAsync(id, identity, ct)) continue;
                result.Add(new(id, candidate.OperationId, candidate.Fingerprint, original, raw.Id, output.Id, wait.RunId, identity, files.Count,
                    candidate.Manifest.Entries.Sum(e => e.Length)));
            }
        }
        return result.OrderBy(c => c.ArtifactId, StringComparer.Ordinal).ToArray();
    }

    private static bool HasLayout(ArtifactRecord artifact, CancellationToken ct)
    {
        if (artifact.Manifest.Entries.Count == 0 || artifact.Fingerprint != artifact.Manifest.Fingerprint ||
            ArtifactManifestFiles.Fingerprint(artifact.Manifest.Entries, artifact.Manifest.Directories) != artifact.Fingerprint) return false;
        ArtifactManifestFiles.VerifyDirectories(artifact.RootDirectory, artifact.Manifest);
        var files = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
        if (!files.SequenceEqual(artifact.Files, ArtifactManifestFiles.PathComparer)) return false;
        for (var i = 0; i < files.Count; ++i)
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(files[i]);
            if (new FileInfo(files[i]) is not {Exists: true} info || info.Length != artifact.Manifest.Entries[i].Length) return false;
        }
        return true;
    }

    internal async Task<ArchiveQuarantineResult> ReclaimReviewedBoundaryAsync(ReviewedCheckpointSelection selected,
        ReviewedProcessingBoundary boundary, Func<string, Task> guard, Func<CancellationToken, Task<bool>> current,
        ReviewedResultValidation results, CancellationToken ct)
    {
        async Task<bool> StillSelected(ArtifactRecord source, CancellationToken token) =>
            source.Id == boundary.ArtifactId && await current(token) &&
            (await ReviewedBoundariesAsync(selected.Wait, guard, token, boundary.ArtifactId)).SequenceEqual([boundary]);
        return await quarantine.DeleteReviewedBoundaryAsync(new(boundary, results.VerifyAsync), StillSelected, ct);
    }
}

// The user confirms a complete processed result for each part. A change counter lets
// later safety checks reuse that request's hash only while the same named bytes remain.
internal sealed class ReviewedResultValidation(IArtifactOperationStore store, ReviewedCheckpointWait wait, Func<string,Task> guard)
{
    private readonly Dictionary<string, string?[]> generations = new(StringComparer.Ordinal);
    internal async Task<bool> VerifyAsync(CancellationToken ct)
    {
        try { return await VerifyResultsAsync(ct); }
        catch (Exception error) when (PublishedArtifactAncestorCleanup.UnavailableReviewEvidence(error)) { return false; }
    }

    private async Task<bool> VerifyResultsAsync(CancellationToken ct)
    {
        foreach (var part in wait.Parts)
        {
            var result = await store.GetArtifactAsync(part.ResultId, ct);
            if (result is not {State: ArtifactLocationState.Available, Kind: "processedFiles"} ||
                result.OperationId != part.ResultOperationId || result.Fingerprint != part.ResultFingerprint) return false;
            await guard(result.RootDirectory);
            string?[] Generation() => result.Files.Select(ArtifactFileIdentity.ReadContentGeneration).ToArray();
            ArtifactManifestFiles.VerifyDirectories(result.RootDirectory, result.Manifest);
            var before = Generation();
            if (generations.TryGetValue(result.Id, out var previous) && previous.All(g => g != null))
            {
                if (!before.SequenceEqual(previous)) return false;
                continue;
            }
            if ((await ArtifactManifestFiles.ValidateAsync(result.RootDirectory, result.Manifest, ct)).State != ArtifactLocationState.Available) return false;
            var after = Generation();
            if (!before.SequenceEqual(after)) return false;
            generations[result.Id] = after;
        }
        return true;
    }
}

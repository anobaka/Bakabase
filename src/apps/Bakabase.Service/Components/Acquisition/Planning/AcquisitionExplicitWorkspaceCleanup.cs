using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private async Task<HashSet<string>> RegisteredCleanupIntermediatesAsync(
        IReadOnlyList<ArtifactWorkspaceCleanupPreview> candidates, CancellationToken ct)
    {
        var ids = candidates.Select(c => c.ArtifactId).Distinct().ToArray();
        var intermediates = await db.Set<ArtifactDbModel>().AsNoTracking()
            .Where(a => ids.Contains(a.Id) && a.Kind == "processedFiles").Select(a => a.Id).ToListAsync(ct);
        return intermediates.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<ArtifactDeliveryCleanupAuthorization?> ExplicitCleanupAuthorizationAsync(
        AcquisitionTaskDbModel row, string version, IReadOnlyList<int> owners, CancellationToken ct,
        PublishedArtifactAncestorCleanup ancestors, ArtifactDeliveryCleanupConsent? expected = null)
    {
        if (artifacts == null) return null;
        var outputs = await AcquisitionVerifiedDeliveryRunRetirement.RequiredPublicationsAsync(db, artifacts, row, null, ct);
        if (outputs is not {Count: > 0}) return null;
        var publications = outputs.Select(a => new ArtifactAcceptedPublication(a.Id, a.OperationId, a.Fingerprint, a.RootDirectory))
            .OrderBy(a => a.ArtifactId, StringComparer.Ordinal).ToArray();
        if (expected != null && (expected.DeliveryAccepted || expected.TaskId != row.Id || expected.PlanRevision != row.PlanRevision ||
            expected.CompletedRunId != row.WorkflowRunId || !expected.OwnerRunIds.Order().SequenceEqual(owners.Order()) ||
            !expected.Publications.OrderBy(a => a.ArtifactId, StringComparer.Ordinal).SequenceEqual(publications))) return null;
        // This consent overrides retention only. It never asserts that missing delivery
        // files were accepted/moved, and never authorizes removing the acquisition task.
        var consent = expected ?? new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            row.Id, row.PlanRevision, row.WorkflowRunId!.Value, false, owners, publications);
        async Task<bool> Current(CancellationToken token)
        {
            var current = await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == row.Id, token);
            if (current == null || StorageVersion(current) != version) return false;
            var latest = await AcquisitionVerifiedDeliveryRunRetirement.RequiredPublicationsAsync(db, artifacts, current, null, token);
            return latest != null && latest.Select(a => new ArtifactAcceptedPublication(a.Id, a.OperationId, a.Fingerprint, a.RootDirectory))
                .OrderBy(a => a.ArtifactId, StringComparer.Ordinal).SequenceEqual(publications);
        }
        return new(consent, Current, artifacts, boundaryCoverage: ancestors.HasBoundaryCoverageAsync);
    }

    private async Task<AcquisitionWorkspaceCleanupPreview> PreviewExplicitWorkspaceCleanupAsync(AcquisitionStoragePolicyPreviewInput input,
        AcquisitionStoragePolicyPreviewStore previews, ArtifactWorkspaceMaintenance maintenance, Func<string, Task> guard,
        CancellationToken ct, PublishedArtifactAncestorCleanup? ancestors)
    {
        var ids = BatchIds(input.TaskIds);
        var rows = await Tasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var result = new List<AcquisitionWorkspaceCleanupRow>();
        var versions = new Dictionary<int, string>();
        var captured = new Dictionary<int, IReadOnlyList<ArtifactWorkspaceCleanupPreview>>();
        var ownerRuns = new Dictionary<int, IReadOnlyList<int>>();
        var consents = new Dictionary<int, ArtifactDeliveryCleanupConsent>();
        var dependencies = new Dictionary<int, IReadOnlyDictionary<string, IReadOnlyList<ArtifactCleanupRetirementDependency>>>();
        var reviewed = new Dictionary<int, ReviewedCheckpointSelection>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        var includedPaths = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var id in ids)
        {
            var row = rows.SingleOrDefault(t => t.Id == id);
            var (reason, artifactIds, _, owners) = await CleanupArtifacts(row, ct);
            var candidates = new List<ArtifactWorkspaceCleanupPreview>();
            var version = row == null ? "" : StorageVersion(row);
            if (reason == "not_completed" && ancestors != null && await ReadCheckpointReviewAsync(row, ct) is {} wait)
            {
                IReadOnlyList<ReviewedProcessingBoundary> sources;
                try
                {
                    await GuardStoragePathsAsync(row!, guard);
                    sources = await ancestors.ReviewedBoundariesAsync(wait, guard, ct);
                }
                catch (Exception error) when (PublishedArtifactAncestorCleanup.UnavailableReviewEvidence(error)) { sources = []; }
                if (sources.Count == 0 || sources.Any(s => included.Contains(s.ArtifactId)))
                    result.Add(new(id, row!.Title, false, "processed_checkpoint_not_reclaimable", [], 0, 0));
                else
                {
                    var intermediateIds = sources.Select(s => s.ArtifactId).ToArray();
                    reviewed[id] = new(wait, sources);
                    versions[id] = version;
                    ownerRuns[id] = [wait.RunId];
                    included.UnionWith(intermediateIds);
                    result.Add(new(id, row!.Title, true, null, intermediateIds, sources.Sum(s => s.FileCount), sources.Sum(s => s.Bytes))
                    {
                        ProcessedCheckpointCleanup = new(wait.Parts.Select(p => p.ResultId).ToArray(),
                            wait.Parts.Select(p => p.DownloadId).ToArray(), intermediateIds)
                    });
                }
                continue;
            }
            var authorization = reason == null && ancestors != null
                ? await ExplicitCleanupAuthorizationAsync(row!, version, owners, ct, ancestors) : null;
            if (reason == null && authorization == null) reason = "CLEANUP_DELIVERY_PROOF_MISSING";
            if (reason == null)
            {
                await GuardStoragePathsAsync(row!, guard);
                foreach (var publication in authorization!.Consent.Publications) await guard(publication.RootDirectory);
                foreach (var chunk in artifactIds.Chunk(100))
                    candidates.AddRange(await maintenance.PreviewForDeletionAsync(chunk, authorization, ct));
                candidates.AddRange(await ancestors!.PreviewForDeletionAsync(
                    authorization.Consent.Publications.Select(p => p.ArtifactId).ToArray(), owners, authorization, ct,
                    includeRegisteredFiles: true));
                candidates = candidates.Where(c => c.Eligible && !included.Contains(c.ArtifactId)).ToList();
                if (input.PreserveOriginalDownloads && candidates.Count > 0)
                {
                    var intermediates = await RegisteredCleanupIntermediatesAsync(candidates, ct);
                    candidates.RemoveAll(c => !intermediates.Contains(c.ArtifactId));
                }
                foreach (var candidate in candidates) await guard(candidate.ResultDirectory);
                var selection = SelectExplicitWorkspaceCleanup(candidates, includedPaths);
                candidates = selection ?? [];
                if (selection == null) reason = "cleanup_overlap_requires_review";
                else if (candidates.Count == 0) reason = "nothing_to_reclaim";
                else
                {
                    versions[id] = version; ownerRuns[id] = owners;
                    captured[id] = candidates.Select(c => c.RegisteredPreviewFiles == null ? c : c with {RegisteredPreviewFiles = null}).ToArray();
                    consents[id] = authorization.Consent;
                    dependencies[id] = authorization.RetirementDependencies.ToDictionary(p => p.Key, p => p.Value);
                    included.UnionWith(candidates.Select(c => c.ArtifactId));
                }
            }
            result.Add(new(id, row?.Title, reason == null, reason, candidates.Select(c => c.ArtifactId).Distinct(StringComparer.Ordinal).ToArray(),
                candidates.Sum(c => c.FileCount), candidates.Sum(c => c.Bytes)));
        }
        var preview = new AcquisitionWorkspaceCleanupPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10), result, versions.Count);
        previews.Add(preview.PreviewId, new("cleanup", preview.ExpiresAt, versions)
            {Artifacts = captured, CleanupOwnerRuns = ownerRuns, CleanupConsents = consents, CleanupDependencies = dependencies,
                PreserveOriginalDownloads = input.PreserveOriginalDownloads, ReviewedCheckpoints = reviewed});
        return preview;
    }

    internal static List<ArtifactWorkspaceCleanupPreview>? SelectExplicitWorkspaceCleanup(
        IReadOnlyList<ArtifactWorkspaceCleanupPreview> candidates, HashSet<string> includedPaths)
    {
        var selected = new bool[candidates.Count];
        var selectedPaths = new HashSet<string>(includedPaths.Comparer);
        var overlappingPrivatePaths = new HashSet<string>(includedPaths.Comparer);
        // Keep registered retirement proofs ahead of overlapping private selections. Among
        // private selections prefer a complete larger proof. Each proof is indivisible:
        // editing its file list would invalidate its fingerprint on apply.
        foreach (var index in Enumerable.Range(0, candidates.Count)
                     .OrderByDescending(i => candidates[i].RegisteredPreviewFiles != null)
                     .ThenByDescending(i => candidates[i].PreviewFiles?.Count ?? 0))
        {
            var candidate = candidates[index];
            var files = candidate.RegisteredPreviewFiles ?? candidate.PreviewFiles;
            if (files == null || files.Count == 0 && candidate.RegisteredPreviewFiles == null) continue;
            var paths = files.Select(file => Path.GetFullPath(file.Path)).ToArray();
            // Confirmation can execute just this row. Another task's selected files
            // cannot be used to prove this row's cleanup is complete.
            if (paths.Any(includedPaths.Contains)) return null;
            if (paths.Any(selectedPaths.Contains))
            {
                // Retirement is a distinct operation and may be a prerequisite for a
                // different artifact. An overlap alone cannot replace that proof.
                if (candidate.RegisteredPreviewFiles != null) return null;
                overlappingPrivatePaths.UnionWith(paths);
                continue;
            }
            selectedPaths.UnionWith(paths);
            selected[index] = true;
        }
        if (overlappingPrivatePaths.Any(path => !selectedPaths.Contains(path))) return null;
        includedPaths.UnionWith(selectedPaths);
        // Apply still uses the original order and the original candidate fingerprints.
        return candidates.Where((_, index) => selected[index]).ToList();
    }

    private async Task<AcquisitionWorkspaceCleanupResult> ReclaimExplicitWorkspacesAsync(int[] ids,
        AcquisitionStoragePolicyPreviewStore.Entry entry, ArtifactWorkspaceMaintenance maintenance, Func<string, Task> guard,
        CancellationToken ct, PublishedArtifactAncestorCleanup? ancestors)
    {
        var completed = new List<int>(); var skipped = new List<AcquisitionStoragePolicySkip>();
        var removedFiles = 0; long removedBytes = 0;
        foreach (var id in ids)
        {
            using var cleanupLease = AcquisitionTaskGate.TryEnterIntermediateCleanup(id, ct);
            var runGates = new List<SemaphoreSlim>();
            if (cleanupLease == null) {skipped.Add(new(id, "task_busy")); continue;}
            try
            {
                var busy = false;
                foreach (var owner in entry.CleanupOwnerRuns[id].Distinct().Order())
                {
                    var gate = WorkflowRunMutationGate.For(owner);
                    if (!await gate.WaitAsync(0, ct)) {busy = true; break;}
                    runGates.Add(gate);
                }
                if (busy) {skipped.Add(new(id, "task_busy")); continue;}
                var row = await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
                if (entry.ReviewedCheckpoints.TryGetValue(id, out var reviewed))
                {
                    async Task<bool> Current(CancellationToken token)
                    {
                        var current = await Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, token);
                        return current != null && StorageVersion(current) == entry.Versions[id] &&
                            (await ReadCheckpointReviewAsync(current, token))?.Identity == reviewed.Wait.Identity;
                    }
                    if (ancestors == null || artifacts == null || !await Current(ct))
                    { skipped.Add(new(id, "revision_changed")); continue; }
                    await GuardStoragePathsAsync(row!, guard);
                    var sources = await ancestors.ReviewedBoundariesAsync(reviewed.Wait, guard, ct);
                    if (!sources.SequenceEqual(reviewed.Sources)) { skipped.Add(new(id, "revision_changed")); continue; }
                    var results = new ReviewedResultValidation(artifacts, reviewed.Wait, guard);
                    if (!await results.VerifyAsync(ct)) { skipped.Add(new(id, "processed_checkpoint_not_reclaimable")); continue; }
                    var done = true;
                    foreach (var source in sources)
                    {
                        var outcome = await ancestors.ReclaimReviewedBoundaryAsync(reviewed, source, guard, Current, results, ct);
                        removedFiles += outcome.RemovedFiles;
                        removedBytes += outcome.RemovedBytes;
                        if (!outcome.Completed) { skipped.Add(new(id, "processed_checkpoint_not_reclaimable")); done = false; break; }
                    }
                    if (done) completed.Add(id);
                    continue;
                }
                var (reason, _, _, owners) = await CleanupArtifacts(row, ct);
                if (reason == null && StorageVersion(row!) != entry.Versions[id]) reason = "revision_changed";
                var authorization = reason == null && ancestors != null
                    ? await ExplicitCleanupAuthorizationAsync(row!, entry.Versions[id], owners, ct, ancestors, entry.CleanupConsents[id]) : null;
                if (reason == null && authorization == null) reason = "CLEANUP_DELIVERY_PROOF_MISSING";
                if (reason != null) {skipped.Add(new(id, reason)); continue;}
                if (entry.PreserveOriginalDownloads)
                {
                    var intermediates = await RegisteredCleanupIntermediatesAsync(entry.Artifacts[id], ct);
                    if (entry.Artifacts[id].Any(c => !intermediates.Contains(c.ArtifactId)))
                    {skipped.Add(new(id, "files_changed")); continue;}
                }
                authorization!.RestoreRetirementDependencies(entry.CleanupDependencies[id]);
                await GuardStoragePathsAsync(row!, guard);
                var verified = true;
                // Verify EVERY required part now, even if only one part has cleanup
                // candidates. The normal mutation path independently verifies again.
                await using (var fence = await artifacts!.AcquireMaintenanceAsync(ct))
                    foreach (var expected in authorization.Consent.Publications)
                    {
                        await guard(expected.RootDirectory);
                        var output = await artifacts.ResolveByIdAsync(expected.ArtifactId, ct);
                        if (output == null || !await authorization.MatchesAsync(output, ct)) {verified = false; break;}
                    }
                if (!verified) {skipped.Add(new(id, "CLEANUP_DELIVERY_PROOF_MISSING")); continue;}
                var succeeded = true;
                foreach (var candidate in entry.Artifacts[id])
                {
                    await guard(candidate.ResultDirectory);
                    var outcome = candidate.PublishedArtifactId == null
                        ? (await maintenance.ReclaimForDeletionAsync([candidate.ArtifactId], authorization,
                            new Dictionary<string, string> {{candidate.ArtifactId, candidate.SelectionFingerprint!}}, ct)).Single()
                        : await ancestors!.ReclaimForDeletionAsync(candidate, owners, authorization, ct);
                    removedFiles += outcome.RemovedFiles; removedBytes += outcome.RemovedBytes;
                    if (!outcome.Completed) {skipped.Add(new(id, outcome.Reason ?? "artifact_busy")); succeeded = false; break;}
                }
                if (succeeded) completed.Add(id);
            }
            catch (Exception error) when (entry.ReviewedCheckpoints.ContainsKey(id) && PublishedArtifactAncestorCleanup.UnavailableReviewEvidence(error))
            { skipped.Add(new(id, "processed_checkpoint_not_reclaimable")); }
            finally {foreach (var gate in runGates) gate.Release();}
        }
        var result = new AcquisitionWorkspaceCleanupResult(completed, removedFiles, removedBytes, skipped);
        lock (entry) entry.Result = result;
        return result;
    }
}

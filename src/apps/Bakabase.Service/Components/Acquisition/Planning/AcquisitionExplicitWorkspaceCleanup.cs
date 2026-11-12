using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
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
        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var row = rows.SingleOrDefault(t => t.Id == id);
            var (reason, artifactIds, _, owners) = await CleanupArtifacts(row, ct);
            var candidates = new List<ArtifactWorkspaceCleanupPreview>();
            var version = row == null ? "" : StorageVersion(row);
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
                    authorization.Consent.Publications.Select(p => p.ArtifactId).ToArray(), owners, authorization, ct));
                candidates = candidates.Where(c => c.Eligible && !included.Contains(c.ArtifactId)).ToList();
                foreach (var candidate in candidates) await guard(candidate.ResultDirectory);
                if (candidates.Count == 0) reason = "nothing_to_reclaim";
                else
                {
                    versions[id] = version; captured[id] = candidates; ownerRuns[id] = owners;
                    consents[id] = authorization.Consent;
                    dependencies[id] = authorization.RetirementDependencies.ToDictionary(p => p.Key, p => p.Value);
                    included.UnionWith(candidates.Select(c => c.ArtifactId));
                }
            }
            result.Add(new(id, row?.Title, reason == null, reason, candidates.Select(c => c.ArtifactId).ToArray(),
                candidates.Sum(c => c.FileCount), candidates.Sum(c => c.Bytes)));
        }
        var preview = new AcquisitionWorkspaceCleanupPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10), result, versions.Count);
        previews.Add(preview.PreviewId, new("cleanup", preview.ExpiresAt, versions)
            {Artifacts = captured, CleanupOwnerRuns = ownerRuns, CleanupConsents = consents, CleanupDependencies = dependencies});
        return preview;
    }

    private async Task<AcquisitionWorkspaceCleanupResult> ReclaimExplicitWorkspacesAsync(int[] ids,
        AcquisitionStoragePolicyPreviewStore.Entry entry, ArtifactWorkspaceMaintenance maintenance, Func<string, Task> guard,
        CancellationToken ct, PublishedArtifactAncestorCleanup? ancestors)
    {
        var completed = new List<int>(); var skipped = new List<AcquisitionStoragePolicySkip>();
        var removedFiles = 0; long removedBytes = 0;
        foreach (var id in ids)
        {
            var taskGate = AcquisitionTaskGate.For(id); var runGates = new List<SemaphoreSlim>();
            if (!await taskGate.WaitAsync(0, ct)) {skipped.Add(new(id, "task_busy")); continue;}
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
                var (reason, _, _, owners) = await CleanupArtifacts(row, ct);
                if (reason == null && StorageVersion(row!) != entry.Versions[id]) reason = "revision_changed";
                var authorization = reason == null && ancestors != null
                    ? await ExplicitCleanupAuthorizationAsync(row!, entry.Versions[id], owners, ct, ancestors, entry.CleanupConsents[id]) : null;
                if (reason == null && authorization == null) reason = "CLEANUP_DELIVERY_PROOF_MISSING";
                if (reason != null) {skipped.Add(new(id, reason)); continue;}
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
            finally {foreach (var gate in runGates) gate.Release(); taskGate.Release();}
        }
        var result = new AcquisitionWorkspaceCleanupResult(completed, removedFiles, removedBytes, skipped);
        lock (entry) entry.Result = result;
        return result;
    }
}

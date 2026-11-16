using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Caller-owned execution history authorization; file cleanup never guesses ownership from paths.</summary>
public interface IArtifactCleanupOwnerScopeProvider
{
    Task<IReadOnlyList<int>?> ExpandAsync(ArtifactRecord publication, IReadOnlyList<int> directRunIds, CancellationToken ct);
}

/// <summary>A caller may durably finish obsolete execution ownership after verifying its
/// complete delivery. Read-only cleanup previews never invoke this mutation hook.</summary>
public interface IArtifactCleanupOwnerPreparation
{
    Task<ArtifactCleanupOwnerPreparationResult?> PrepareAsync(string publicationId, CancellationToken ct);
}
public sealed record ArtifactCleanupOwnerPreparationResult(bool Ready, string? Reason = null, bool Retry = false);

/// <summary>Retires registered inputs only after their complete per-file lineage reaches a
/// still-verifiable publication. Manifests, operation history and durable cleanup receipts remain.
/// The caller's owner set is an internal authorization boundary, never a client supplied list.</summary>
public sealed partial class PublishedArtifactAncestorCleanup(BakabaseDbContext db, IArtifactOperationStore store,
    ArchiveQuarantineService quarantine, IEnumerable<IArtifactCleanupOwnerScopeProvider>? ownerScopes = null,
    FileProcessingWorkspaceResolver? workspaces = null, ArtifactWorkspaceMaintenance? maintenance = null,
    IEnumerable<IArtifactCleanupOwnerPreparation>? ownerPreparations = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal Task<List<string>> GetRequestedIdsAsync(CancellationToken ct) => db.Set<ArtifactDbModel>().AsNoTracking()
        .Where(a => a.Kind == "publishedFiles" && a.MetadataJson != null &&
            a.MetadataJson.Contains("\"publicationCleanupPending\":true"))
        .OrderBy(a => a.UpdatedAt).ThenBy(a => a.Id).Select(a => a.Id).Take(1).ToListAsync(ct);

    internal async Task ReclaimRequestedAsync(string publicationId, CancellationToken ct)
    {
        try { await ReclaimRequestedCoreAsync(publicationId, ct); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // An unavailable old input must not permanently occupy the front of the queue.
            // Keep a bounded, non-secret diagnostic; a manual preview can reevaluate later.
            var row = await db.Set<ArtifactDbModel>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == publicationId, ct);
            if (row?.MetadataJson == null) return;
            var metadata = JsonNode.Parse(row.MetadataJson)!.AsObject();
            metadata[PublishedArtifactCleanupCompletionObserver.PendingKey] = false;
            metadata[PublishedArtifactCleanupCompletionObserver.ProgressKey] = new JsonObject
                { ["status"] = "verificationUnavailable", ["checkedAt"] = DateTime.UtcNow.ToString("O"), ["failureType"] = error.GetType().Name };
            var updated = metadata.ToJsonString();
            await db.Set<ArtifactDbModel>().Where(a => a.Id == publicationId && a.MetadataJson == row.MetadataJson)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.MetadataJson, updated).SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
        }
    }

    private async Task ReclaimRequestedCoreAsync(string publicationId, CancellationToken ct)
    {
        var row = await db.Set<ArtifactDbModel>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == publicationId, ct);
        if (row?.MetadataJson == null) return;
        var original = row.MetadataJson;
        var metadata = JsonNode.Parse(original)!.AsObject();
        if (metadata[PublishedArtifactCleanupCompletionObserver.PendingKey]?.GetValue<bool>() != true) return;
        var requestedRun = metadata[PublishedArtifactCleanupCompletionObserver.ProgressKey]?["runId"]?.GetValue<int>();
        if (requestedRun == null || !await db.WorkflowRuns.AnyAsync(r => r.Id == requestedRun && r.Status == WorkflowRunStatus.Success && r.FailedItemCount == 0, ct))
        {
            // A user can restart an old run after its completion intent was committed.
            // Never reclaim while it is active, or leave that stale intent ahead of new
            // deliveries forever. Its next successful completion stages a fresh intent.
            metadata[PublishedArtifactCleanupCompletionObserver.PendingKey] = false;
            metadata[PublishedArtifactCleanupCompletionObserver.ProgressKey] = new JsonObject
                { ["runId"] = requestedRun, ["status"] = "ownerNotCompleted", ["checkedAt"] = DateTime.UtcNow.ToString("O") };
            var deferred = metadata.ToJsonString();
            await db.Set<ArtifactDbModel>().Where(a => a.Id == publicationId && a.MetadataJson == original)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.MetadataJson, deferred).SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
            return;
        }
        var preparation = await PreparePublicationOwnersAsync(publicationId, ct);
        if (!preparation.Ready)
        {
            metadata[PublishedArtifactCleanupCompletionObserver.PendingKey] = preparation.Retry;
            var blocked = metadata[PublishedArtifactCleanupCompletionObserver.ProgressKey]!.AsObject();
            blocked["status"] = preparation.Retry ? "waitingOwners" : "retained";
            blocked["checkedAt"] = DateTime.UtcNow.ToString("O");
            blocked["reasons"] = JsonSerializer.SerializeToNode(new[] {preparation.Reason ?? "ActiveOwner"}, Json);
            var deferred = metadata.ToJsonString();
            await db.Set<ArtifactDbModel>().Where(a => a.Id == publicationId && a.MetadataJson == original)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.MetadataJson, deferred).SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
            return;
        }
        var owners = await PublicationOwnersAsync(publicationId, ct);
        var privateResults = await PrepareAutomaticPrivateCleanupAsync(publicationId, owners, ct);
        var candidates = await PreviewAsync([publicationId], owners, ct);
        var eligible = candidates.Where(c => c.Eligible).ToArray();
        var outcomes = new List<ArtifactWorkspaceCleanupResult>(privateResults);
        var retiredInput = false;
        foreach (var candidate in eligible.Take(2))
        {
            var beforeState = await db.Artifacts.AsNoTracking().Where(a => a.Id == candidate.ArtifactId)
                .Select(a => (ArtifactLocationState?)a.State).SingleOrDefaultAsync(ct);
            var outcome = await ReclaimAutomaticPreparedAsync(candidate, owners, privateResults, ct);
            outcomes.Add(outcome);
            // A completed stage move can leave no bytes at the old registered path.
            // Retiring that registration still releases ancestors, unlike an already
            // retired/no-op result. Count only a freshly observed state transition.
            if (outcome.Completed && beforeState.HasValue && beforeState != ArtifactLocationState.Retired &&
                await db.Artifacts.AsNoTracking().AnyAsync(a => a.Id == candidate.ArtifactId &&
                    a.State == ArtifactLocationState.Retired, ct)) retiredInput = true;
        }
        // Retiring a nearer snapshot may release a previously blocked ancestor. Reevaluate
        // once after actual progress; AlreadyRetired/no-op results cannot perpetuate the queue.
        var pending = retiredInput || outcomes.Any(o => o.Completed && o.RemovedFiles > 0) || eligible.Length > Math.Min(eligible.Length, 2) ||
            candidates.Any(c => c.Reason is "ActiveOwner" or "ActiveConsumer" or "UnboundArtifact") ||
            outcomes.Any(r => !r.Completed && r.Reason is "ActiveOwner" or "ActiveConsumer");
        var reasons = candidates.Where(c => !c.Eligible && c.Reason != "AlreadyRetired").Select(c => c.Reason)
            .Concat(outcomes.Where(r => !r.Completed).Select(r => r.Reason)).Distinct().ToArray();
        metadata[PublishedArtifactCleanupCompletionObserver.PendingKey] = pending;
        var progress = metadata[PublishedArtifactCleanupCompletionObserver.ProgressKey]!.AsObject();
        progress["status"] = pending ? "pending" : row.State != ArtifactLocationState.Available ? "deliveryUnavailable" : reasons.Length > 0 ? "retained" : "completed";
        progress["checkedAt"] = DateTime.UtcNow.ToString("O");
        progress["removedFiles"] = (progress["removedFiles"]?.GetValue<int>() ?? 0) + outcomes.Sum(o => o.RemovedFiles);
        progress["removedBytes"] = (progress["removedBytes"]?.GetValue<long>() ?? 0) + outcomes.Sum(o => o.RemovedBytes);
        progress["reasons"] = JsonSerializer.SerializeToNode(reasons, Json);
        var updated = metadata.ToJsonString();
        await db.Set<ArtifactDbModel>().Where(a => a.Id == publicationId && a.MetadataJson == original)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.MetadataJson, updated).SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
    }

    internal async Task<IReadOnlyList<string>> GetCandidateIdsAsync(string? afterId, int limit, CancellationToken ct)
    {
        var query = db.Set<ArtifactDbModel>().AsNoTracking().Where(a => a.Kind == "publishedFiles" &&
            a.State == ArtifactLocationState.Available && db.Set<ArtifactOperationDbModel>().Any(o =>
                o.Id == a.OperationId && o.State == ArtifactOperationState.Completed && o.OutputArtifactId == a.Id));
        if (afterId != null) query = query.Where(a => string.Compare(a.Id, afterId) > 0);
        return await query.OrderBy(a => a.Id).Select(a => a.Id).Take(limit).ToListAsync(ct);
    }

    internal async Task<IReadOnlyList<int>> PublicationOwnersAsync(string publicationId, CancellationToken ct)
    {
        var artifact = await store.GetArtifactAsync(publicationId, ct);
        if (artifact == null) return [];
        var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
        if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out _))) return [];
        var direct = bindings.Select(b => int.Parse(b.OwnerId, CultureInfo.InvariantCulture)).Distinct().ToArray();
        IReadOnlyList<int>? expanded = null;
        foreach (var provider in ownerScopes ?? [])
        {
            var scope = await provider.ExpandAsync(artifact, direct, ct);
            if (scope == null) continue;
            if (scope.Count == 0) return [];
            if (expanded != null || direct.Any(r => !scope.Contains(r))) return [];
            expanded = scope;
        }
        return expanded ?? (direct.Length == 1 ? direct : []);
    }

    internal async Task<ArtifactCleanupOwnerPreparationResult> PreparePublicationOwnersAsync(string publicationId, CancellationToken ct)
    {
        foreach (var preparation in ownerPreparations ?? [])
            if (await preparation.PrepareAsync(publicationId, ct) is {Ready: false} blocked) return blocked;
        return new(true);
    }

    public Task<IReadOnlyList<ArtifactWorkspaceCleanupPreview>> PreviewAsync(IReadOnlyList<string> publications,
        IReadOnlyList<int> ownerRunIds, CancellationToken ct) => PreviewCoreAsync(publications, ownerRunIds, ct, null);

    internal Task<IReadOnlyList<ArtifactWorkspaceCleanupPreview>> PreviewForDeletionAsync(IReadOnlyList<string> publications,
        IReadOnlyList<int> ownerRunIds, ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct,
        ArtifactCleanupReferenceGuard? previewReferences = null) =>
        PreviewCoreAsync(publications, ownerRunIds, ct, authorization, previewReferences);

    private async Task<IReadOnlyList<ArtifactWorkspaceCleanupPreview>> PreviewCoreAsync(IReadOnlyList<string> publications,
        IReadOnlyList<int> ownerRunIds, CancellationToken ct, ArtifactDeliveryCleanupAuthorization? authorization,
        ArtifactCleanupReferenceGuard? previewReferences = null)
    {
        previewReferences ??= new(db, workspaces, cachePreviewReads: true);
        var rows = new List<ArtifactWorkspaceCleanupPreview>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        var approved = new Dictionary<string, ArtifactCleanupRetirementDependency>(StringComparer.Ordinal);
        foreach (var publicationId in publications.Distinct(StringComparer.Ordinal))
        {
            var output = await store.GetArtifactAsync(publicationId, ct);
            if (output?.Kind != "publishedFiles" || output.State != ArtifactLocationState.Available &&
                (authorization?.Consent.DeliveryAccepted != true || !await authorization.MatchesAsync(output, ct))) continue;
            var graph = await AncestorsAsync(output, ct);
            var siblingIds = await BoundaryCandidateIdsAsync(graph.Keys.ToArray(), ct);
            var siblings = new List<ArtifactRecord>();
            foreach (var id in siblingIds)
                if (!graph.ContainsKey(id) && await store.GetArtifactAsync(id, ct) is {MetadataJson: not null} sibling)
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(sibling.MetadataJson, Json)?.CompletedThroughStepId != null)
                            siblings.Add(sibling);
                    }
                    catch (JsonException) { /* Unknown metadata grants no additional cleanup candidate. */ }
                }
            // Explicit deletion includes completed branches superseded by a newer plan.
            // Every candidate still passes ownership, consumer and exact-reference guards.
            if (authorization?.IncludesHistoricalProcessing == true)
            {
                var ownerKeys = ownerRunIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
                var historicalIds = await db.Artifacts.AsNoTracking().Where(a => a.Managed && a.Kind == "processedFiles" &&
                        a.State != ArtifactLocationState.Retired && db.ArtifactOperationBindings.Any(b =>
                            b.OperationId == a.OperationId && b.OwnerKind == "workflow" && ownerKeys.Contains(b.OwnerId)))
                    .OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => a.Id).Take(10001).ToListAsync(ct);
                if (historicalIds.Count > 10000) throw new InvalidDataException("CLEANUP_LINEAGE_TOO_LARGE");
                foreach (var id in historicalIds)
                    if (!graph.ContainsKey(id) && siblings.All(s => s.Id != id) &&
                        await store.GetArtifactAsync(id, ct) is { } historical)
                        siblings.Add(historical);
            }
            // Nearer descendants first. Once retired, their immutable manifests still prove
            // coverage for older ancestors; no input has to be reacquired to prove the graph.
            // Siblings are a separate candidate list, never fabricated ancestry. In particular
            // a raw parent remains protected by any still-retained sibling child.
            foreach (var registered in DescendantsFirst(siblings.Concat(graph.Values).Where(a => a.Id != output.Id && a.Managed &&
                         a.Kind is "processedFiles" or "download")))
            {
                if (!included.Add(registered.Id)) continue;
                var source = registered;
                if (source.State == ArtifactLocationState.Retired && maintenance != null &&
                    await PreviewRetiredPlanResultAsync(source, output, graph, ownerRunIds, ct, authorization) is {} replaced)
                { rows.Add(replaced); continue; }
                if (source.State == ArtifactLocationState.Quarantined)
                {
                    var receipt = (await quarantine.GetReceiptsAsync(ct)).SingleOrDefault(r => r.ArtifactId == source.Id &&
                        r.Status is "Moving" or "Quarantined" or "Deleting");
                    if (receipt == null || !receipt.OutputArtifactIds.SequenceEqual(new[] {output.Id})) continue;
                    source = source with {RootDirectory = receipt.OriginalRoot,
                        Files = ArtifactManifestFiles.ResolveFiles(receipt.OriginalRoot, source.Manifest)};
                }
                // Only an explicitly confirmed task-deletion plan can stage prerequisites.
                // They must already be independently eligible earlier in this exact plan.
                // Ordinary background cleanup retains its one-pass conservative behaviour.
                var prerequisites = authorization == null ? [] :
                    await PlannedPrerequisitesAsync(source, output, graph, approved, ct, authorization);
                var reason = await IneligibleAsync(source, output, graph, ownerRunIds, ct, authorization,
                    prerequisites.Select(p => p.ArtifactId).ToArray(), previewReferences);
                var proof = reason == null && !graph.ContainsKey(source.Id) ? await BoundaryProofAsync(source, output, graph, ct) : null;
                var fingerprint = reason == null ? Fingerprint(source, output, ownerRunIds, proof, prerequisites) : null;
                if (reason == null && authorization != null)
                {
                    authorization.RecordRetirementDependencies(source.Id, prerequisites);
                    approved.Add(source.Id, new(source.Id, source.OperationId, source.Fingerprint,
                        source.RootDirectory, fingerprint!, output.Id));
                }
                rows.Add(new(source.Id, source.OperationId, source.RootDirectory, reason == null, reason,
                    reason == null ? source.Manifest.Entries.Count : 0,
                    reason == null ? source.Manifest.Entries.Sum(e => e.Length) : 0, fingerprint, output.Id));
            }
        }
        return rows;
    }

    private static IReadOnlyList<ArtifactRecord> DescendantsFirst(IEnumerable<ArtifactRecord> records)
    {
        var unique = records.GroupBy(a => a.Id, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        var children = unique.SelectMany(a => a.ParentArtifactIds.Select(parent => (Parent: parent, Child: a)))
            .ToLookup(edge => edge.Parent, edge => edge.Child, StringComparer.Ordinal);
        var recordsById = unique.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var remaining = unique.ToDictionary(a => a.Id, a => children[a.Id].Select(c => c.Id).Distinct().Count(), StringComparer.Ordinal);
        var ready = new Queue<ArtifactRecord>(unique.Where(a => remaining[a.Id] == 0));
        var ordered = new List<ArtifactRecord>();
        while (ready.TryDequeue(out var record))
        {
            ordered.Add(record);
            foreach (var parent in record.ParentArtifactIds.Distinct(StringComparer.Ordinal))
                if (remaining.TryGetValue(parent, out var count) && (remaining[parent] = count - 1) == 0)
                    ready.Enqueue(recordsById[parent]);
        }
        if (ordered.Count != unique.Length) throw new InvalidDataException("CLEANUP_LINEAGE_INVALID");
        return ordered;
    }

    public Task<ArtifactWorkspaceCleanupResult> ReclaimAsync(ArtifactWorkspaceCleanupPreview expected,
        IReadOnlyList<int> ownerRunIds, CancellationToken ct) => ReclaimCoreAsync(expected, ownerRunIds, ct, null);

    internal Task<ArtifactWorkspaceCleanupResult> ReclaimForDeletionAsync(ArtifactWorkspaceCleanupPreview expected,
        IReadOnlyList<int> ownerRunIds, ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct) =>
        ReclaimCoreAsync(expected, ownerRunIds, ct, authorization);

    private async Task<ArtifactWorkspaceCleanupResult> ReclaimCoreAsync(ArtifactWorkspaceCleanupPreview expected,
        IReadOnlyList<int> ownerRunIds, CancellationToken ct, ArtifactDeliveryCleanupAuthorization? authorization,
        IReadOnlyList<int>? heldRunGates = null)
    {
        if (expected.PublishedArtifactId == null || !expected.Eligible || expected.SelectionFingerprint == null)
            throw new ArgumentException("A registered-input cleanup needs its exact publication preview.");
        var output = await store.GetArtifactAsync(expected.PublishedArtifactId, ct);
        if (output?.Kind != "publishedFiles" || output.State != ArtifactLocationState.Available &&
            (authorization?.Consent.DeliveryAccepted != true || !await authorization.MatchesAsync(output, ct)))
            return new(expected.ArtifactId, false, "ResultNotVerified");
        var graph = await AncestorsAsync(output, ct);
        var source = graph.GetValueOrDefault(expected.ArtifactId) ?? await store.GetArtifactAsync(expected.ArtifactId, ct);
        if (source == null) return new(expected.ArtifactId, false, "UnrelatedArtifact");
        if (source.State == ArtifactLocationState.Retired)
            return await ReclaimRetiredPlanResultAsync(expected, ownerRunIds,
                authorization != null ? ownerRunIds : heldRunGates ?? [], ct, authorization);
        var prerequisites = authorization?.DependenciesFor(source.Id) ?? [];
        if (!await PrerequisitesRetiredAsync(prerequisites, ct))
            return new(source.Id, false, "ReferencedDescendant");
        var proof = !graph.ContainsKey(source.Id) ? await BoundaryProofAsync(source, output, graph, ct) : null;
        if (source.State == ArtifactLocationState.Available && Fingerprint(source, output, ownerRunIds, proof, prerequisites) != expected.SelectionFingerprint)
            return new(source.Id, false, "SelectionChanged");
        var referenceSource = source with {RootDirectory = expected.ResultDirectory,
            Files = ArtifactManifestFiles.ResolveFiles(expected.ResultDirectory, source.Manifest)};
        var reason = await IneligibleAsync(referenceSource, output, graph, ownerRunIds, ct, authorization);
        if (reason != null) return new(source.Id, false, reason);
        var result = await quarantine.DeleteManagedAncestorAfterDeliveryAsync(source.Id, [output.Id], async (current, token) =>
        {
            var latestOutput = await store.GetArtifactAsync(output.Id, token);
            if (latestOutput?.Fingerprint != output.Fingerprint || latestOutput.RootDirectory != output.RootDirectory) return false;
            var latestGraph = await AncestorsAsync(latestOutput, token);
            // Quarantine changes the registered location. Also preserve checks against the
            // original name used by plans and resource paths before the final unlink.
            var original = current with {RootDirectory = expected.ResultDirectory,
                Files = ArtifactManifestFiles.ResolveFiles(expected.ResultDirectory, current.Manifest)};
            var latestProof = !latestGraph.ContainsKey(original.Id) ? await BoundaryProofAsync(original, latestOutput, latestGraph, token) : null;
            return await PrerequisitesRetiredAsync(prerequisites, token) &&
                Fingerprint(original, latestOutput, ownerRunIds, latestProof, prerequisites) == expected.SelectionFingerprint &&
                await IneligibleAsync(original, latestOutput, latestGraph, ownerRunIds, token, authorization) == null;
        }, ct, authorization, async (original, token) => workspaces == null ? new HashSet<string>(StringComparer.Ordinal) :
            (await Bakabase.Service.Components.Acquisition.Planning.AcquisitionProcessingStageCommit.ReadMovedEntriesAsync(
                db, store, workspaces, original, token))?.RelativePaths.ToHashSet(StringComparer.Ordinal) ?? [],
            async token => await store.GetArtifactAsync(output.Id, token) is {} latest
                ? await ReadReleasedStageViewsAsync(latest, await AncestorsAsync(latest, token), ownerRunIds, token) : []);
        return new(source.Id, result.Completed, result.Completed ? null : result.Message ?? result.Status,
            result.RemovedFiles, result.RemovedBytes);
    }

    internal async Task<ArtifactWorkspaceCleanupResult> ReclaimAutomaticAsync(ArtifactWorkspaceCleanupPreview expected,
        IReadOnlyList<int> owners, CancellationToken ct)
    {
        if (expected.PublishedArtifactId == null || !expected.Eligible || expected.SelectionFingerprint == null)
            throw new ArgumentException("A registered-input cleanup needs its exact publication preview.");
        var prepared = await PrepareAutomaticPrivateCleanupAsync(expected.PublishedArtifactId, owners, ct);
        var result = await ReclaimAutomaticPreparedAsync(expected, owners, prepared, ct);
        return result with {RemovedFiles = result.RemovedFiles + prepared.Sum(p => p.RemovedFiles),
            RemovedBytes = result.RemovedBytes + prepared.Sum(p => p.RemovedBytes)};
    }

    private async Task<string?> IneligibleAsync(ArtifactRecord source, ArtifactRecord output,
        IReadOnlyDictionary<string, ArtifactRecord> graph, IReadOnlyList<int> owners, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization? authorization = null, IReadOnlyList<string>? previewRetirements = null,
        ArtifactCleanupReferenceGuard? previewReferences = null)
    {
        if (owners.Count == 0 || owners.Any(i => i <= 0)) return "UnboundArtifact";
        var allowed = owners.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (!source.Managed || source.Kind is not ("processedFiles" or "download")) return "NotManagedInput";
        if (source.State == ArtifactLocationState.Retired) return "AlreadyRetired";
        if (source.State is not (ArtifactLocationState.Available or ArtifactLocationState.Quarantined)) return "InputUnavailable";
        if (authorization != null && !await authorization.MatchesAsync(output, ct)) return "DeliveryAcceptanceChanged";
        if (authorization == null && !PolicyAllows(source, graph)) return "RetentionPolicyKeep";
        var previewInactive = authorization?.PreviewSupersededRuns ?? [];
        if (await db.Set<WorkflowRunDbModel>().CountAsync(r => owners.Contains(r.Id) &&
                (r.Status != WorkflowRunStatus.Pending && r.Status != WorkflowRunStatus.Running && r.Status != WorkflowRunStatus.Waiting ||
                 r.Status == WorkflowRunStatus.Waiting && r.PendingSignalJson == null && previewInactive.Contains(r.Id)), ct) != owners.Distinct().Count())
            return "ActiveOwner";
        var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().Where(b =>
            b.OperationId == source.OperationId || b.OperationId == output.OperationId).ToListAsync(ct);
        if (!bindings.Any(b => b.OperationId == source.OperationId) || !bindings.Any(b => b.OperationId == output.OperationId) ||
            bindings.Any(b => b.OwnerKind != "workflow" || !allowed.Contains(b.OwnerId))) return "SharedOwner";
        var operation = await store.GetOperationAsync(source.OperationId, ct);
        var publication = await store.GetOperationAsync(output.OperationId, ct);
        if (operation?.State != ArtifactOperationState.Completed || operation.OutputArtifactId != source.Id ||
            operation.Kind != (source.Kind == "download" ? "download" : "files.process") ||
            publication?.State != ArtifactOperationState.Completed || publication.Kind != "files.publish" ||
            publication.OutputArtifactId != output.Id) return "OperationNotCompleted";
        if (await (authorization?.HasActiveConsumersAsync(source.OperationId, ct) ?? store.HasActiveConsumersAsync(source.OperationId, ct))) return "ActiveConsumer";
        var releasedViews = authorization == null ? await ReadReleasedStageViewsAsync(output, graph, owners, ct) : [];
        if (source.Files.Intersect(output.Files, ArtifactManifestFiles.PathComparer).Any() ||
            await (authorization?.HasOtherRegisteredFilesAsync(source.Files, source.Id, ct) ??
                store.HasOtherRegisteredFilesAsync(source.Files, source.Id, releasedViews, ct))) return "ReferencedArtifact";
        var graphIds = graph.Keys.ToArray();
        var planned = (previewRetirements ?? []).Concat(authorization == null ? [] :
            await authorization.ReleasableViewIdsAsync(ct)).Distinct(StringComparer.Ordinal).ToArray();
        if (await db.Set<ArtifactLineageDbModel>().AnyAsync(edge => edge.ParentArtifactId == source.Id &&
                !graphIds.Contains(edge.ChildArtifactId) && !planned.Contains(edge.ChildArtifactId) && db.Set<ArtifactDbModel>().Any(a =>
                    a.Id == edge.ChildArtifactId && a.State != ArtifactLocationState.Retired), ct)) return "ReferencedDescendant";
        if (await (previewReferences ?? new(db, workspaces)).HasReferencesAsync(source, owners, ct,
                previewSupersededRuns: authorization?.PreviewSupersededRuns)) return "ReferencedPath";
        if (authorization != null && await authorization.SkipsReplacementProofAsync(source, [output.Id], ct))
            return null;
        var coverage = await ArtifactFileCoverage.EvaluateAsync(store, source, [output.Id], ct);
        return coverage.Complete || await BoundaryProofAsync(source, output, graph, ct) != null
            ? null : ArtifactFileCoverage.IncompleteCode;
    }

    private Task<List<string>> BoundaryCandidateIdsAsync(string[] ancestorIds, CancellationToken ct) =>
        db.Set<ArtifactDbModel>().AsNoTracking().Where(a => a.Kind == "processedFiles" && a.Managed &&
            a.MetadataJson != null && a.MetadataJson.Contains("completedThroughStepId") &&
            db.Set<ArtifactLineageDbModel>().Any(e => e.ChildArtifactId == a.Id && ancestorIds.Contains(e.ParentArtifactId)))
            .OrderBy(a => a.Id).Select(a => a.Id).Take(10000).ToListAsync(ct);

    private async Task<string?> BoundaryProofAsync(ArtifactRecord source, ArtifactRecord publication,
        IReadOnlyDictionary<string, ArtifactRecord> ancestors, CancellationToken ct) =>
        await FileProcessingPlanExecutor.BoundaryPublicationProofAsync(store, source, publication, ancestors,
            await quarantine.GetReceiptsAsync(ct), ct);

    internal async Task<bool> HasBoundaryCoverageAsync(ArtifactRecord source, ArtifactRecord publication, CancellationToken ct) =>
        await BoundaryProofAsync(source, publication, await AncestorsAsync(publication, ct), ct) != null;

    private async Task<IReadOnlyList<ArtifactCleanupRetirementDependency>> PlannedPrerequisitesAsync(ArtifactRecord source,
        ArtifactRecord publication, IReadOnlyDictionary<string, ArtifactRecord> graph,
        IReadOnlyDictionary<string, ArtifactCleanupRetirementDependency> approved, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization authorization)
    {
        var ids = graph.Keys.Concat(await authorization.ReleasableViewIdsAsync(ct)).Distinct(StringComparer.Ordinal).ToArray();
        var pending = await db.Set<ArtifactLineageDbModel>().AsNoTracking().Where(e => e.ParentArtifactId == source.Id &&
            !ids.Contains(e.ChildArtifactId) && db.Set<ArtifactDbModel>().Any(a => a.Id == e.ChildArtifactId &&
                a.State != ArtifactLocationState.Retired)).Select(e => e.ChildArtifactId).Distinct().ToListAsync(ct);
        var result = new List<ArtifactCleanupRetirementDependency>();
        foreach (var id in pending.Order(StringComparer.Ordinal))
        {
            if (!approved.TryGetValue(id, out var expected) || expected.PublicationId != publication.Id) return [];
            var child = await store.GetArtifactAsync(id, ct);
            if (child?.State != ArtifactLocationState.Available || child.OperationId != expected.OperationId ||
                child.Fingerprint != expected.Fingerprint || child.RootDirectory != expected.OriginalRoot) return [];
            result.Add(expected);
        }
        return result;
    }

    private async Task<bool> PrerequisitesRetiredAsync(IReadOnlyList<ArtifactCleanupRetirementDependency> prerequisites, CancellationToken ct)
    {
        if (prerequisites.Count == 0) return true;
        var receipts = await quarantine.GetReceiptsAsync(ct);
        foreach (var expected in prerequisites)
        {
            var artifact = await store.GetArtifactAsync(expected.ArtifactId, ct);
            var proof = receipts.Where(r => r.ArtifactId == expected.ArtifactId && r.Status == "Purged" &&
                r.OriginalRoot == expected.OriginalRoot && r.OutputArtifactIds.SequenceEqual([expected.PublicationId])).ToArray();
            if (artifact?.State != ArtifactLocationState.Retired || artifact.OperationId != expected.OperationId ||
                artifact.Fingerprint != expected.Fingerprint || proof.Length != 1 ||
                artifact.RootDirectory != proof[0].QuarantineRoot) return false;
            var operation = await store.GetOperationAsync(artifact.OperationId, ct);
            if (operation is not {State: ArtifactOperationState.Completed} || operation.OutputArtifactId != artifact.Id) return false;
        }
        return true;
    }

    private static bool PolicyAllows(ArtifactRecord source, IReadOnlyDictionary<string, ArtifactRecord> graph)
    {
        static FileArtifactProcessingMetadata? Metadata(ArtifactRecord artifact) => artifact.MetadataJson == null
            ? null : JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(artifact.MetadataJson, Json);
        static bool StepsAllow(FileArtifactProcessingMetadata metadata, bool intermediates)
        {
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, Json);
            // A per-file coverage receipt proves replacement, not retention authorization.
            // Whole-artifact retirement conservatively retains a selected subset too when
            // any step explicitly keeps/quarantines inputs; never infer that selector's scope.
            return plan != null && plan.Steps.All(s => (!intermediates || s.StoragePolicy?.CleanupIntermediates != false) &&
                s.StoragePolicy?.SourceCleanup is null or "delete");
        }
        var children = graph.Values.Where(a => a.ParentArtifactIds.Contains(source.Id)).ToArray();
        if (source.Kind == "processedFiles")
        {
            // Legacy missing policy is not a new deletion authorization. A step-level keep
            // also wins over the enclosing default when retiring the entire registered set.
            var metadata = Metadata(source);
            return metadata?.StoragePolicy is {CleanupIntermediates: true} && StepsAllow(metadata, true) &&
                children.Where(a => a.Kind == "processedFiles").All(a => Metadata(a) is {StoragePolicy.CleanupIntermediates: true} next &&
                    StepsAllow(next, true));
        }
        // A raw input is retained unless every direct processing consumer explicitly opted
        // into deletion. Unknown modules and publications never authorize source deletion.
        // Nested overrides are retained too: coverage alone cannot cancel their explicit keep.
        return children.Length > 0 && children.All(a => a.Kind == "processedFiles" &&
            Metadata(a)?.StoragePolicy?.SourceCleanup == "delete") &&
            graph.Values.Where(a => a.Kind == "processedFiles").All(a => Metadata(a) is {} metadata && StepsAllow(metadata, false));
    }

    private async Task<Dictionary<string, ArtifactRecord>> AncestorsAsync(ArtifactRecord publication, CancellationToken ct)
    {
        var records = new Dictionary<string, ArtifactRecord>(StringComparer.Ordinal) {{publication.Id, publication}};
        var queue = new Queue<string>(publication.ParentArtifactIds);
        while (queue.TryDequeue(out var id))
        {
            ct.ThrowIfCancellationRequested();
            if (records.ContainsKey(id)) continue;
            if (records.Count >= 10000) throw new InvalidDataException("The artifact lineage exceeds the cleanup limit.");
            var record = await store.GetArtifactAsync(id, ct) ?? throw new InvalidDataException("An artifact lineage record is missing.");
            records.Add(id, record);
            foreach (var parent in record.ParentArtifactIds) queue.Enqueue(parent);
        }
        return records;
    }

    private static string Fingerprint(ArtifactRecord source, ArtifactRecord output, IReadOnlyList<int> owners,
        string? boundaryProof = null, IReadOnlyList<ArtifactCleanupRetirementDependency>? prerequisites = null)
    {
        var original = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Version = 1, source.Id, source.OperationId, source.RootDirectory, source.Fingerprint, source.MetadataJson,
            source.Manifest, PublicationId = output.Id, PublicationRoot = output.RootDirectory,
            PublicationFingerprint = output.Fingerprint, Owners = owners.Distinct().Order().ToArray(), BoundaryProof = boundaryProof
        }, Json)))).ToLowerInvariant();
        return prerequisites is not {Count: > 0} ? original : ArtifactManifestFiles.Hash(JsonSerializer.Serialize(
            new {Original = original, Prerequisites = prerequisites}, Json));
    }

}

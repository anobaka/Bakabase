using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>
/// A current-directory capture is a read-only view, not another owned copy. A fixed deletion
/// consent may release only views proven to belong to this task's stopped runs. This never
/// adopts user-added files: deletion still uses the managed source's exact file manifest.
/// Completed stage views can also release their read references after their durable commit
/// receipt and complete descendant ownership are verified; borrowed files stay borrowed.
/// Explicit final-file deletion also releases the references held by its fixed, exclusively
/// owned publication versions. Their exact files are independently selected for final cleanup.
/// </summary>
internal static class AcquisitionTaskCleanupViews
{
    internal static async Task<IReadOnlyList<string>> ReadAsync(BakabaseDbContext db, IArtifactOperationStore store,
        ArtifactDeliveryCleanupConsent consent, CancellationToken ct, FileProcessingWorkspaceResolver? workspaces = null)
    {
        var owners = consent.OwnerRunIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
        var candidates = await db.Artifacts.AsNoTracking().Where(a => (a.Kind == AcquisitionCurrentDirectoryFiles.Kind || a.Kind == "processedFiles") &&
            !a.Managed && db.ArtifactOperationBindings.Any(b => b.OperationId == a.OperationId &&
                b.OwnerKind == "workflow" && owners.Contains(b.OwnerId))).Select(a => a.Id).ToListAsync(ct);
        var closureRoots = candidates.Concat(consent.DeleteDeliveredFiles
            ? consent.Publications.Select(p => p.ArtifactId) : []).Distinct(StringComparer.Ordinal).ToArray();
        var ownedClosures = await OwnedClosuresAsync(db, closureRoots, owners, ct);
        var result = new List<string>();
        foreach (var id in candidates)
        {
            var view = await store.GetArtifactAsync(id, ct);
            if (view?.MetadataJson == null || view.State is ArtifactLocationState.Quarantined or ArtifactLocationState.StorageUnavailable)
                continue;
            if (view.Kind == "processedFiles")
            {
                if (ownedClosures.Contains(view.Id) && await AcquisitionProcessingStageCommit.IsOwnedViewAsync(
                        db, store, workspaces, view, consent.TaskId, consent.OwnerRunIds, ct))
                    result.Add(view.Id);
                continue;
            }
            AcquisitionCurrentDirectoryReceipt? receipt;
            try { receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(view.MetadataJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { continue; }
            if (receipt == null || !consent.OwnerRunIds.Contains(receipt.PreviousRunId) ||
                !await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, store, view, consent.TaskId,
                    receipt.PreviousRunId, receipt.PartId, ct)) continue;
            // A view reused by another workflow, including an inactive one, is not ours to
            // release. Walk descendants too: an outside owner must retain its recoverable input.
            if (ownedClosures.Contains(view.Id)) result.Add(view.Id);
        }
        if (consent.DeleteDeliveredFiles)
            foreach (var expected in consent.Publications)
            {
                var publication = await store.GetArtifactAsync(expected.ArtifactId, ct);
                if (publication is not {Kind: "publishedFiles"} || publication.OperationId != expected.OperationId ||
                    publication.Fingerprint != expected.Fingerprint || publication.Manifest.Fingerprint != expected.Fingerprint ||
                    publication.RootDirectory != expected.RootDirectory ||
                    publication.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired) continue;
                var operation = await store.GetOperationAsync(publication.OperationId, ct);
                if (operation is not {Kind: "files.publish", State: ArtifactOperationState.Completed} ||
                    operation.OutputArtifactId != publication.Id || !ownedClosures.Contains(publication.Id)) continue;
                result.Add(publication.Id);
            }
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static async Task<HashSet<string>> OwnedClosuresAsync(BakabaseDbContext db, IReadOnlyList<string> starts,
        string[] owners, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (starts.Count == 0) return result;
        // This snapshot lives only for this reference check. Never reuse it across the
        // preview/confirmation boundary or the next mutation safety phase. Project the
        // graph and ownership facts in batches; loading a full artifact for every edge
        // repeatedly parses large manifests and issues N+1 lineage/consumer queries.
        var edges = await db.ArtifactLineage.AsNoTracking()
            .Select(e => new {e.ParentArtifactId, e.ChildArtifactId}).ToListAsync(ct);
        var children = edges.ToLookup(e => e.ParentArtifactId, e => e.ChildArtifactId, StringComparer.Ordinal);
        var closures = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in starts)
        {
            var pending = new Queue<string>([start]);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryDequeue(out var artifactId))
            {
                ct.ThrowIfCancellationRequested();
                if (!visited.Add(artifactId)) continue;
                if (visited.Count > 10000) break;
                foreach (var child in children[artifactId]) pending.Enqueue(child);
            }
            if (visited.Count > 10000) continue;
            closures.Add(start, visited);
            all.UnionWith(visited);
        }
        var ids = all.ToArray();
        var artifacts = await db.Artifacts.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Select(a => new {a.Id, a.OperationId}).ToListAsync(ct);
        var operations = artifacts.Select(a => a.OperationId).Distinct(StringComparer.Ordinal).ToArray();
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => operations.Contains(b.OperationId))
            .Select(b => new {b.OperationId, b.OwnerKind, b.OwnerId}).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var active = (await db.ArtifactConsumers.AsNoTracking().Where(c => operations.Contains(c.OperationId) && c.ReleasedAt == null &&
                (c.OwnerKind == "workflow" ? db.WorkflowRuns.Any(r => r.Id.ToString() == c.OwnerId &&
                    (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Waiting))
                    : c.LeaseUntil > now))
            .Select(c => c.OperationId).Distinct().ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var byOperation = bindings.ToLookup(b => b.OperationId, StringComparer.Ordinal);
        var allowedOwners = owners.ToHashSet(StringComparer.Ordinal);
        var owned = artifacts.Where(a => !active.Contains(a.OperationId) && byOperation[a.OperationId].Any() &&
                byOperation[a.OperationId].All(b => b.OwnerKind == "workflow" && allowedOwners.Contains(b.OwnerId)))
            .Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in closures)
            if (pair.Value.All(owned.Contains)) result.Add(pair.Key);
        return result;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>
/// A current-directory capture is a read-only view, not another owned copy. A fixed deletion
/// consent may release only views proven to belong to this task's stopped runs. This never
/// adopts user-added files: deletion still uses the managed source's exact file manifest.
/// Explicit final-file deletion also releases the references held by its fixed, exclusively
/// owned publication versions. Their exact files are independently selected for final cleanup.
/// </summary>
internal static class AcquisitionTaskCleanupViews
{
    internal static async Task<IReadOnlyList<string>> ReadAsync(BakabaseDbContext db, IArtifactOperationStore store,
        ArtifactDeliveryCleanupConsent consent, CancellationToken ct)
    {
        var owners = consent.OwnerRunIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
        var candidates = await db.Artifacts.AsNoTracking().Where(a => a.Kind == AcquisitionCurrentDirectoryFiles.Kind &&
            !a.Managed && db.ArtifactOperationBindings.Any(b => b.OperationId == a.OperationId &&
                b.OwnerKind == "workflow" && owners.Contains(b.OwnerId))).Select(a => a.Id).ToListAsync(ct);
        var result = new List<string>();
        foreach (var id in candidates)
        {
            var view = await store.GetArtifactAsync(id, ct);
            if (view?.MetadataJson == null || view.State is ArtifactLocationState.Quarantined or ArtifactLocationState.StorageUnavailable)
                continue;
            AcquisitionCurrentDirectoryReceipt? receipt;
            try { receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(view.MetadataJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { continue; }
            if (receipt == null || !consent.OwnerRunIds.Contains(receipt.PreviousRunId) ||
                !await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, store, view, consent.TaskId,
                    receipt.PreviousRunId, receipt.PartId, ct)) continue;
            // A view reused by another workflow, including an inactive one, is not ours to
            // release. Walk descendants too: an outside owner must retain its recoverable input.
            if (await OwnedClosureAsync(view.Id)) result.Add(view.Id);
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
                    operation.OutputArtifactId != publication.Id || !await OwnedClosureAsync(publication.Id)) continue;
                result.Add(publication.Id);
            }
        return result.Distinct(StringComparer.Ordinal).ToArray();

        async Task<bool> OwnedClosureAsync(string start)
        {
            var pending = new Queue<string>([start]);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryDequeue(out var artifactId))
            {
                if (!visited.Add(artifactId)) continue;
                if (visited.Count > 10000) return false;
                var artifact = await store.GetArtifactAsync(artifactId, ct);
                if (artifact == null) return false;
                var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
                if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !owners.Contains(b.OwnerId)) ||
                    await store.HasActiveConsumersAsync(artifact.OperationId, ct)) return false;
                foreach (var child in await db.ArtifactLineage.AsNoTracking().Where(e => e.ParentArtifactId == artifactId)
                    .Select(e => e.ChildArtifactId).ToListAsync(ct)) pending.Enqueue(child);
            }
            return true;
        }
    }
}

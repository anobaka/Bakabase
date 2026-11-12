using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class PublishedArtifactAncestorCleanup
{
    internal Task<ArtifactWorkspaceCleanupResult> ReclaimWithHeldRunGatesAsync(ArtifactWorkspaceCleanupPreview expected,
        IReadOnlyList<int> owners, IReadOnlyList<int> heldRunGates, CancellationToken ct) =>
        ReclaimCoreAsync(expected, owners, ct, null, heldRunGates);

    private async Task<ArtifactRecord?> RetiredOriginalAsync(ArtifactRecord source, ArtifactRecord output, CancellationToken ct)
    {
        if (source is not {State: ArtifactLocationState.Retired, Managed: true, Kind: "processedFiles"}) return null;
        var receipts = (await quarantine.GetReceiptsAsync(ct)).Where(r => r.ArtifactId == source.Id && r.Status == "Purged" &&
            r.QuarantineRoot == source.RootDirectory && r.OutputArtifactIds.SequenceEqual([output.Id])).ToArray();
        if (receipts.Length != 1) return null;
        // A read-only view of the exact retired location, never a registration/state revival.
        return source with {State = ArtifactLocationState.Available, RootDirectory = receipts[0].OriginalRoot,
            Files = ArtifactManifestFiles.ResolveFiles(receipts[0].OriginalRoot, source.Manifest)};
    }

    private async Task<ArtifactWorkspaceCleanupPreview?> PreviewRetiredPlanResultAsync(ArtifactRecord registered,
        ArtifactRecord output, IReadOnlyDictionary<string, ArtifactRecord> graph, IReadOnlyList<int> owners, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization? authorization = null)
    {
        var source = await RetiredOriginalAsync(registered, output, ct);
        if (source == null || await IneligibleAsync(source, output, graph, owners, ct, authorization) != null ||
            authorization != null && !await authorization.CoversAsync(source, ct)) return null;
        var proof = await RecursiveArchiveExecutor.ReplacedPlanResultAsync(source.RootDirectory, source.Manifest, ct, false);
        if (proof == null) return null;
        var references = new ArtifactCleanupReferenceGuard(db, workspaces);
        if (await store.HasRegisteredFilesAsync(proof.Files, ct) ||
            await references.HasReferencesAsync(source, owners, ct, proof.Files, authorization?.PreviewSupersededRuns)) return null;
        var key = ArtifactManifestFiles.Hash("replaced-plan-result-v1\n" + Fingerprint(source, output, owners) + "\n" +
            proof.EvidenceFingerprint + "\n" + JsonSerializer.Serialize(proof.Files, Json));
        var sizes = proof.Files.Select(path => (Path: path, Bytes: new FileInfo(path).Length)).ToArray();
        return new(source.Id, source.OperationId, source.RootDirectory, true, null, sizes.Length,
            sizes.Sum(f => f.Bytes), key, output.Id) {PreviewFiles = sizes};
    }

    private async Task<ArtifactWorkspaceCleanupResult> ReclaimRetiredPlanResultAsync(ArtifactWorkspaceCleanupPreview expected,
        IReadOnlyList<int> owners, IReadOnlyList<int> heldRunGates, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization? authorization)
    {
        if (maintenance == null) return new(expected.ArtifactId, true, "AlreadyRetired");
        var gates = new List<SemaphoreSlim>();
        try
        {
            foreach (var owner in owners.Except(heldRunGates).Distinct().Order())
            {
                var gate = WorkflowRunMutationGate.For(owner);
                if (!await gate.WaitAsync(0, ct)) return new(expected.ArtifactId, false, "ActiveOwner");
                gates.Add(gate);
            }
            await using var fence = await store.AcquireMaintenanceAsync(ct);
            var output = await store.GetArtifactAsync(expected.PublishedArtifactId!, ct);
            var registered = await store.GetArtifactAsync(expected.ArtifactId, ct);
            if (output == null || registered == null ||
                output.State != ArtifactLocationState.Available && authorization?.Consent.DeliveryAccepted != true ||
                authorization != null && !await authorization.MatchesAsync(output, ct))
                return new(expected.ArtifactId, false, "ResultNotVerified");
            var current = await PreviewRetiredPlanResultAsync(registered, output, await AncestorsAsync(output, ct), owners, ct, authorization);
            if (current == null || current.SelectionFingerprint != expected.SelectionFingerprint)
                return new(expected.ArtifactId, false, "SelectionChanged");
            var verified = authorization?.Consent.DeliveryAccepted == true
                ? output : await store.ResolveByIdAsync(output.Id, ct);
            if (verified == null || verified.RootDirectory != output.RootDirectory || verified.Fingerprint != output.Fingerprint)
                return new(expected.ArtifactId, false, "ResultNotVerified");
            // Full hashing may be slow. Recheck the receipt, owner state, policies, complete
            // lineage and every path reference under the fence before writing cleanup intent.
            registered = await store.GetArtifactAsync(expected.ArtifactId, ct);
            if (registered == null || (await PreviewRetiredPlanResultAsync(registered, verified,
                    await AncestorsAsync(verified, ct), owners, ct, authorization))?.SelectionFingerprint != expected.SelectionFingerprint)
                return new(expected.ArtifactId, false, "SelectionChanged");
            var source = await RetiredOriginalAsync(registered, verified, ct);
            return source == null ? new(expected.ArtifactId, false, "SelectionChanged") :
                await maintenance.ReclaimReplacedPlanAfterDeliveryAsync(source, verified, owners, ct, authorization);
        }
        finally { foreach (var gate in gates) gate.Release(); }
    }
}

public sealed partial class ArtifactWorkspaceMaintenance
{
    // Invoked only while the publication cleaner holds all run gates and the maintenance
    // fence, after full final-output verification and the immutable replacement proof.
    internal async Task<ArtifactWorkspaceCleanupResult> ReclaimReplacedPlanAfterDeliveryAsync(ArtifactRecord source,
        ArtifactRecord publication, IReadOnlyList<int> owners, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization? authorization = null)
    {
        var snapshot = ArtifactCleanupFileSnapshot.For(authorization);
        var proof = await RecursiveArchiveExecutor.ReplacedPlanResultAsync(source.RootDirectory, source.Manifest, ct, verifyContents: snapshot == null);
        if (proof == null) return new(source.Id, false, "SelectionChanged");
        if (authorization != null && (!await authorization.MatchesAsync(publication, ct) ||
            !await authorization.CoversAsync(source, ct))) return new(source.Id, false, "DeliveryAcceptanceChanged");
        if (await store.HasRegisteredFilesAsync(proof.Files, ct) ||
            await references.HasReferencesAsync(source, owners, ct, proof.Files)) return new(source.Id, false, "ReferencedPath");
        var files = proof.Files.ToDictionary(path => path, path => new FileInfo(path).Length, FileProcessingFiles.PathComparer);
        foreach (var file in files) snapshot?.Observe(file.Key, file.Value, ct);
        var receipt = await journal.BeginAsync(publication, files, ct, authorization?.Consent, relatedArtifactId: source.Id,
            checkpointDirectory: authorization?.Consent.DeliveryAccepted == true ? Path.GetDirectoryName(proof.Root) : null);
        try
        {
            await recursive.ReclaimReplacedPlanResultAsync(source.RootDirectory, source.Manifest,
                files.Keys.ToHashSet(FileProcessingFiles.PathComparer), ct, snapshot);
            var outcome = await journal.FinishAsync(receipt, "Completed", ct);
            return new(source.Id, outcome.Status == "Completed", outcome.Status == "Completed" ? null : "WorkspaceChanged",
                outcome.RemovedFiles, outcome.RemovedBytes);
        }
        catch
        {
            await journal.FinishAsync(receipt, "Interrupted", CancellationToken.None);
            throw;
        }
    }
}

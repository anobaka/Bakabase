using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionProcessingWorkspaceCandidate(ProcessingWorkspaceSelection Selection,
    IReadOnlyList<string> StageArtifactIds, IReadOnlyList<string> MovedInputIds)
{
    internal IReadOnlyList<AcquisitionRetainedDownloadFile> RetainedFiles { get; init; } = [];
    internal IReadOnlyList<string> RetainedDownloadIds { get; init; } = [];
    internal IReadOnlyList<string> PublicationIds { get; init; } = [];
    internal IReadOnlyList<string> RetireMovedInputs { get; init; } = [];
}

public sealed partial class AcquisitionPlanService
{
    private string WorkspaceReclaimReceipts => Path.Combine(workspaces!.StageCleanupMetadataDirectory, "confirmed-processing-files");

    // A retained download can have a regular processedFiles registration for its working copy.
    // Only the stage selection checks that its independently retained source still exists. Prevent
    // the older ancestor path from simultaneously offering that same copy on weaker evidence.
    private async Task RequireRetainedInputSelectionAsync(AcquisitionTaskDbModel row,
        List<ArtifactWorkspaceCleanupPreview> candidates, CancellationToken ct)
    {
        if (row.PlanJson == null || candidates.Count == 0) return;
        var plan = Plan(row);
        if (plan.StoragePolicy == null || plan.StageDirectories == null) return;
        var roots = plan.Parts.Where(p => ArtifactStoragePolicies.Resolve(plan.StoragePolicy, p.StoragePolicy).SourceCleanup == "keep")
            .Select(p => plan.StageDirectories.Parts.GetValueOrDefault(p.Id)?.Processing)
            .Where(s => s is {Ownership: "exclusive", Evidence: "taskAllocation", Allocation: not null})
            .Select(s => s!.Directory).ToHashSet(FileProcessingFiles.PathComparer);
        var possiblyClaimed = candidates.Where(c => roots.Contains(c.ResultDirectory)).Select(c => c.OperationId).ToArray();
        if (possiblyClaimed.Length == 0) return;
        var inputOperations = await db.ArtifactOperations.AsNoTracking().Where(o => possiblyClaimed.Contains(o.Id) &&
            o.Kind == "files.process" && o.IntentKey.StartsWith("acquisition.stage-input:")).Select(o => o.Id).ToListAsync(ct);
        candidates.RemoveAll(c => roots.Contains(c.ResultDirectory) && inputOperations.Contains(c.OperationId));
    }

    // Production receipts attest to the writer. A complete, separately verified official publication and
    // the acquisition's current retention, isolation and reference policies authorize this reclamation.
    private async Task<List<AcquisitionProcessingWorkspaceCandidate>> ProcessingWorkspacesAsync(AcquisitionTaskDbModel row,
        IReadOnlyList<int> owners, Func<string, Task> guard, PublishedArtifactAncestorCleanup? ancestors, CancellationToken ct,
        AcquisitionStageDirectoryReferenceGuard? references = null)
    {
        var candidates = new List<AcquisitionProcessingWorkspaceCandidate>();
        if (workspaces == null || artifacts == null || ancestors == null || row.PlanJson == null) return candidates;
        try
        {
            var plan = Plan(row);
            if (plan.StageDirectories is not {} stages || stages.TaskId != row.Id || plan.StoragePolicy == null ||
                !stages.Parts.Values.Any(p => p.Processing is {Ownership: "exclusive", Allocation: not null})) return candidates;
            AcquisitionStageDirectoryResolver.Validate(stages, plan);
            var version = StorageVersion(row);
            var authorization = await ExplicitCleanupAuthorizationAsync(row, version, owners, ct, ancestors);
            if (authorization == null) return candidates;
            await AcquisitionTaskDeletionCleanupService.RequireStageCleanupInactiveAsync(db, row, owners, authorization, ct);
            var publications = await AcquisitionVerifiedDeliveryRunRetirement.RequiredPublicationsAsync(db, artifacts, row, null, ct);
            if (publications is not {Count: > 0}) return candidates;
            var protectedRoots = stages.Parts.Values.SelectMany(p => new[] {p.Download?.Directory, p.Delivery?.Directory})
                .OfType<string>().Concat(publications.Select(p => p.RootDirectory)).ToArray();
            references ??= new AcquisitionStageDirectoryReferenceGuard(db, artifacts, storage, workspaces);
            var ownerIds = owners.ToArray();
            var verified = false;
            foreach (var part in plan.Parts)
            {
                var stage = stages.Parts[part.Id].Processing;
                if (stage is not {Ownership: "exclusive", BindingId: not null, Allocation: not null} ||
                    ArtifactStoragePolicies.Resolve(plan.StoragePolicy, part.StoragePolicy) is not {CleanupIntermediates: true, SourceCleanup: "delete" or "keep"} ||
                    protectedRoots.Any(root => StageDirectoryOverlap(stage.Directory, root))) continue;
                await guard(stage.Directory);
                try
                {
                    var evidence = await AcquisitionProcessingWorkspaceEvidence.FindAsync(db, artifacts, workspaces, row, plan,
                        part.Id, stage, owners, publications, ct);
                    if (evidence == null) continue;
                    var generation = ArtifactManifestFiles.Hash(Serialize(new {version, stage, owners = owners.Order().ToArray(),
                        publications = authorization.Consent.Publications, artifacts = evidence.StageArtifactIds.Order().ToArray()}));
                    if (evidence.RetainedDownloadIds.Count > 0)
                        generation = ArtifactManifestFiles.Hash(generation + "\n" + string.Join("\n", evidence.RetainedDownloadIds));
                    var selected = await AcquisitionProcessingWorkspaceRemoval.InspectAsync(WorkspaceReclaimReceipts, part.Id,
                        stage.BindingId, stage.Directory, generation, evidence.Files, ct, held => references.RequireAsync(
                            row, ownerIds, part.Id, stage.Directory, ct, explicitTaskCleanup: true, privateReceiptDirectory: held));
                    if (selected == null || selected.Phase == "new" && await db.Artifacts.AsNoTracking().AnyAsync(a =>
                        evidence.StageArtifactIds.Contains(a.Id) && !evidence.PurgedStageIds.Contains(a.Id) && a.State == ArtifactLocationState.Retired, ct)) continue;
                    if (!verified)
                    {
                        foreach (var output in publications)
                        {
                            await guard(output.RootDirectory);
                            if (!(await ArtifactManifestFiles.ValidateAsync(output.RootDirectory, output.Manifest, ct)).Available) return [];
                        }
                        verified = true;
                    }
                    var movedInputs = new List<string>();
                    foreach (var artifactId in evidence.StageArtifactIds)
                    {
                        if (await artifacts.GetArtifactAsync(artifactId, ct) is not {ParentArtifactIds.Count: 1} committed ||
                            await artifacts.GetArtifactAsync(committed.ParentArtifactIds[0], ct) is not {} original) continue;
                        if (ArtifactCleanupAbsence.AllConfirmedAbsent(original.RootDirectory, original.Files, ct))
                            movedInputs.Add(original.Id);
                    }
                    candidates.Add(new(selected, evidence.StageArtifactIds, movedInputs) {PublicationIds = publications.Select(p => p.Id).ToArray(), RetainedDownloadIds = evidence.RetainedDownloadIds, RetainedFiles = evidence.RetainedFiles});
                }
                catch (Exception e) when (WorkspaceEvidenceUnavailable(e)) { }
            }
        }
        catch (Exception e) when (WorkspaceEvidenceUnavailable(e)) { }
        return candidates;
    }

    private static bool WorkspaceEvidenceUnavailable(Exception e) => e is IOException or UnauthorizedAccessException or
        InvalidDataException or JsonException or ArgumentException or InvalidOperationException;

    private async Task<ArtifactWorkspaceCleanupResult> RemoveProcessingWorkspacesAsync(AcquisitionTaskDbModel row,
        IReadOnlyList<int> owners, IReadOnlyList<AcquisitionProcessingWorkspaceCandidate> selected, Func<string, Task> guard,
        PublishedArtifactAncestorCleanup? ancestors, CancellationToken ct)
    {
        var files = 0;
        long bytes = 0;
        await using var fence = await artifacts!.AcquireMaintenanceAsync(ct);
        var references = new AcquisitionStageDirectoryReferenceGuard(db, artifacts, storage, workspaces, reuseManifestReads: true);
        var current = await ProcessingWorkspacesAsync(row, owners, guard, ancestors, ct, references);
        foreach (var old in selected)
        {
            var match = current.SingleOrDefault(c => c.Selection.PartId == old.Selection.PartId);
            if (match == null || !old.StageArtifactIds.SequenceEqual(match.StageArtifactIds) ||
                !old.RetainedFiles.SequenceEqual(match.RetainedFiles) ||
                !old.RetireMovedInputs.All(match.MovedInputIds.Contains) ||
                !AcquisitionProcessingWorkspaceRemoval.Same(old.Selection, match.Selection))
                return new("", false, "files_changed");
        }
        var ownerIds = owners.ToArray();
        var remaining = selected.ToList();
        var protectedIds = current.SelectMany(c => c.StageArtifactIds.Concat(c.PublicationIds).Concat(c.MovedInputIds).Concat(c.RetainedDownloadIds)).Distinct().ToArray();
        var handoff = new AcquisitionProcessingWorkspaceHandoff(db, fence, protectedIds, async token =>
        {
            foreach (var pending in remaining)
            {
                AcquisitionRetainedDownloadFile.RequireUnchanged(pending.RetainedFiles, token);
                await guard(pending.Selection.Root);
                await references.RequireAsync(row, ownerIds, pending.Selection.PartId, pending.Selection.Root, token,
                    explicitTaskCleanup: true, privateReceiptDirectory: pending.Selection.QuarantineDirectory);
            }
        });
        await handoff.CaptureAsync(ct);
        foreach (var item in selected)
        {
            var checkOriginalBeforePurge = true;
            var result = await AcquisitionProcessingWorkspaceRemoval.RemoveAsync(WorkspaceReclaimReceipts, item.Selection, ct, async token =>
            {
                // Final reference proof and retirement stay under the same fence as the slot-absence check.
                await handoff.RequireBeforeRetirementAsync(token);
                // Independently reconstructed ids, exclusive roots and hash manifests remain protected here.
                // One SQL write retires the complete selection. Existing operation bindings and lineage remain as history.
                var retire = item.StageArtifactIds.ToList();
                foreach (var id in item.RetireMovedInputs)
                {
                    // The old producer already moved its payload. Retire its empty registration only. No operation
                    // ever unlinks its original paths, including a new file recreated since confirmation.
                    var original = await artifacts.GetArtifactAsync(id, token);
                    if (original is not {Kind: "processedFiles", Managed: true} ||
                        !ArtifactCleanupAbsence.AllConfirmedAbsent(original.RootDirectory, original.Files, token) ||
                        await artifacts.HasActiveConsumersAsync(original.OperationId, token) ||
                        await new ArtifactCleanupReferenceGuard(db, workspaces).HasReferencesAsync(original, owners, token, original.Files)) continue;
                    retire.Add(id);
                }
                await db.Artifacts.Where(a => retire.Contains(a.Id) && a.Managed &&
                    (a.State == ArtifactLocationState.Available || a.State == ArtifactLocationState.Missing))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired)
                        .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), token);
            }, async token =>
            {
                if (checkOriginalBeforePurge)
                {
                    AcquisitionRetainedDownloadFile.RequireUnchanged(item.RetainedFiles, token);
                    checkOriginalBeforePurge = false;
                }
                await handoff.YieldAsync(token);
            });
            files += result.RemovedFiles;
            bytes += result.RemovedBytes;
            if (!result.Completed) return result with {RemovedFiles = files, RemovedBytes = bytes};
            remaining.Remove(item);
            if (remaining.Count > 0) await handoff.CaptureAsync(ct);
        }
        return new("", true, null, files, bytes);
    }
}

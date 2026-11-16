using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    private async Task<Plan> PreviewWholeTaskFilesAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners,
        AcquisitionSupersededRunCleanup.Plan? stopped, bool intermediates, bool deliveries, bool skipVerification,
        bool retryRetainedFiles, CancellationToken ct, Func<string, Task>? guard, string? borrowedOperation,
        bool directDirectoryDeletion = false)
    {
        var ownerKeys = owners.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        // A continuation binding confers read access. Never offer the borrowed producer's
        // source even if its original binding is later removed during history maintenance.
        var operations = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
                ownerKeys.Contains(b.OwnerId) && b.OperationId != borrowedOperation)
            .Select(b => b.OperationId).Distinct().ToArrayAsync(ct);
        var publications = new List<ArtifactAcceptedPublication>();
        if (deliveries)
        {
            var outputIds = await db.Artifacts.AsNoTracking().Where(a => operations.Contains(a.OperationId) &&
                    a.Kind == "publishedFiles" && a.State != ArtifactLocationState.Retired && a.State != ArtifactLocationState.Quarantined)
                .Select(a => a.Id).ToArrayAsync(ct);
            foreach (var id in outputIds)
                if (await artifacts.GetArtifactAsync(id, ct) is {} publication)
                    publications.Add(new(id, publication.OperationId, publication.Fingerprint, publication.RootDirectory));
        }
        var consent = new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, task.Id,
            task.PlanRevision, task.WorkflowRunId ?? 0, false, owners.Order().ToArray(), publications, skipVerification, deliveries,
            AbandonUnfinishedFiles: task.Status != AcquisitionStatus.Completed, WholeTaskFiles: true);
        var authorization = Authorization(task, consent, stopped, retryRetainedFiles);
        var roots = await PreviewStageDirectoriesForDeletionAsync(task, owners, authorization, intermediates, deliveries, ct, guard,
            directDirectoryDeletion);
        var protectedOrSelectedRoots = roots.Select(r => r.RootDirectory).ToArray();
        var references = new ArtifactCleanupReferenceGuard(db, workspaces, cachePreviewReads: true);
        var inputs = intermediates && !directDirectoryDeletion ? await PreviewAbandonedInputsAsync(operations, authorization, references,
            ct, guard, protectedOrSelectedRoots) : [];
        var candidates = roots.ToList();
        var ids = await db.Artifacts.AsNoTracking().Where(a => operations.Contains(a.OperationId) &&
                a.State != ArtifactLocationState.Retired && (deliveries && a.Kind == "publishedFiles" ||
                intermediates && a.Managed && (a.Kind == "download" || a.Kind == "processedFiles")))
            .Select(a => a.Id).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            var registered = await WholeTaskArtifactAsync(task, authorization, id, intermediates, protectedOrSelectedRoots, references, ct, guard,
                directDirectoryDeletion);
            if (registered != null) candidates.Add(registered);
        }
        // Old attempts may leave registered additions in a retired, privately allocated
        // result outside the current stage tree. Its existing capture receipt proves only those leaves.
        var captured = intermediates && !directDirectoryDeletion ? (await PreviewCapturedInputsAsync(authorization, references, ct, guard))
            .Where(c => !protectedOrSelectedRoots.Any(r => SamePath(r, c.RootDirectory) ||
                ArtifactCleanupPathReferences.StrictlyUnder(c.RootDirectory, r))).ToArray() : [];
        var groups = candidates.Select(c => c.Group).Concat(inputs.Select(c => c.Group))
            .Concat(captured.Select(c => c.Group)).ToArray();
        var eligible = Unique(groups.Where(g => g.Eligible).SelectMany(g => g.Files));
        var identified = Unique(groups.SelectMany(g => g.Files));
        return new(new(eligible.Count, eligible.Sum(f => f.Bytes), groups, IdentifiedFileCount: identified.Count,
            IdentifiedBytes: identified.Sum(f => f.Bytes), SizeIncomplete: groups.Any(g => g.SizeUnknown)),
            Version(task), consent, [], stopped, RetryRetainedFiles: retryRetainedFiles, StageDirectories: candidates, AbandonedInputs: inputs, CapturedInputs: captured);
    }

    // A registration in a borrowed/shared container proves individual leaves only.
    // Never adopt the container or unregistered siblings as task-owned.
    private async Task<StageDirectoryCandidate?> WholeTaskArtifactAsync(AcquisitionTaskDbModel task,
        ArtifactDeliveryCleanupAuthorization authorization, string id, bool intermediates, IReadOnlyList<string> coveredRoots,
        ArtifactCleanupReferenceGuard references, CancellationToken ct, Func<string, Task>? guard, bool directDirectoryDeletion = false)
    {
        var consent = authorization.Consent;
        var artifact = await artifacts.GetArtifactAsync(id, ct);
        if (artifact == null || !consent.WholeTaskFiles || artifact.State == ArtifactLocationState.Retired) return null;
        var delivery = artifact.Kind == "publishedFiles";
        if (delivery ? !consent.DeleteDeliveredFiles : !intermediates || !artifact.Managed ||
            artifact.Kind is not ("download" or "processedFiles")) return null;
        var root = artifact.RootDirectory;
        bool Covered(string path) => coveredRoots.Any(r => SamePath(r, path) || ArtifactCleanupPathReferences.StrictlyUnder(path, r));
        if (Covered(root)) return null;
        var paths = ArtifactManifestFiles.ResolveFiles(root, artifact.Manifest).Where(p => !Covered(p)).ToArray();
        if (paths.Length == 0) return null;
        var proof = "task-artifact:" + id;
        var stage = delivery ? "delivery" : artifact.Kind == "download" ? "download" : "processing";
        var files = new List<AcquisitionTaskDeleteCleanupFile>();
        var identities = new Dictionary<string, string>(ArtifactManifestFiles.PathComparer);
        StageDirectoryCandidate Result(string? reason) => new(proof, task.Id, task.PlanRevision, "", stage, root, artifact.Fingerprint,
            files, [], identities, new(proof, artifact.Kind, root, reason is null or "AlreadyAbsent", reason, [], files,
                files.Count, files.Sum(f => f.Bytes), Shared: reason is "ReferencedPath" or "ReferencedArtifact" or "SharedOwner",
                SizeUnknown: reason != null && reason != "AlreadyAbsent"), proof, intermediates, consent.DeleteDeliveredFiles)
            {ManifestArtifactId = id, CoveredRoots = coveredRoots, DirectDeletion = directDirectoryDeletion};
        await EnsureDirectoryAuthorizedAsync(root, guard);
        ArtifactManifestFiles.RejectLinks(root);
        if (!await authorization.IsCurrentAsync(ct)) return Result("SelectionChanged");
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToArrayAsync(ct);
        var keys = consent.OwnerRunIds.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (bindings.Length == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !keys.Contains(b.OwnerId)) ||
            await authorization.HasActiveConsumersAsync(artifact.OperationId, ct)) return Result("SharedOwner");
        var op = await artifacts.GetOperationAsync(artifact.OperationId, ct);
        if (artifact.State == ArtifactLocationState.Quarantined || op is not {State: ArtifactOperationState.Completed} ||
            op.OutputArtifactId != artifact.Id) return Result("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
        if (delivery && !(await authorization.ReleasableViewIdsAsync(ct)).Contains(id, StringComparer.Ordinal) ||
            !delivery && !(await AcquisitionTaskCleanupViews.OwnedClosuresAsync(db, [id], keys, ct)).Contains(id))
            return Result("ReferencedDescendant");
        if (await authorization.HasOtherRegisteredFilesAsync(paths, id, ct)) return Result("ReferencedArtifact");
        if (await references.HasReferencesAsync(artifact, consent.OwnerRunIds, ct, privatePaths: paths,
            previewSupersededRuns: authorization.PreviewSupersededRuns)) return Result("ReferencedPath");
        var expected = artifact.Manifest.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            if (directDirectoryDeletion) storage.EnsureTreeMutationAllowed(root);
            return Result((delivery || directDirectoryDeletion) && Path.GetDirectoryName(root) is {} parent &&
                ArtifactCleanupAbsence.Probe(parent, root, ct) == ArtifactCleanupPathPresence.ConfirmedAbsent
                    ? "AlreadyAbsent" : "WorkspaceUnavailable");
        }
        if (directDirectoryDeletion)
        {
            files.AddRange(paths.Select(path => new AcquisitionTaskDeleteCleanupFile(path,
                expected[ArtifactManifestFiles.RelativeFile(root, path)].Length)));
            return Result(null);
        }
        foreach (var path in paths)
        {
            await EnsureDirectoryAuthorizedAsync(path, guard);
            storage.EnsureTreeMutationAllowed(path);
            var presence = ArtifactCleanupAbsence.Probe(root, path, ct);
            if (presence == ArtifactCleanupPathPresence.ConfirmedAbsent) continue;
            if (presence != ArtifactCleanupPathPresence.Present) return Result("WorkspaceUnavailable");
            var info = new FileInfo(path); info.Refresh();
            if (!info.Exists || info.Length != expected[ArtifactManifestFiles.RelativeFile(root, path)].Length) return Result("SelectionChanged");
            files.Add(new(path, info.Length));
            identities.Add(path, JsonSerializer.Serialize(new {info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc}));
        }
        return Result(files.Count > 0 ? null : "AlreadyAbsent");
    }
}

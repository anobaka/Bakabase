using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Shared directory-level ownership and reference guard. Same-task historical registrations
/// remain records, while outside owners, resources, configurations and live consumers keep their files.</summary>
internal sealed class AcquisitionStageDirectoryReferenceGuard(BakabaseDbContext db, IArtifactOperationStore artifacts,
    IUserStoragePolicy storage, FileProcessingWorkspaceResolver? workspaces, bool reuseManifestReads = false)
{
    // For one in-flight cleanup only. Hold immutable path projections across handoffs; ownership,
    // bindings, consumers, task/run state, public references and actual locations are read afresh.
    private sealed record ManifestRead(string Root, string? OriginalRoot, string Fingerprint, DateTime Updated, ArtifactRecord Artifact,
        bool HasLeafUnderDirectory, bool ClaimsPrivateDirectory);
    private readonly Dictionary<(string Directory, string? PrivateDirectory, string Id), ManifestRead>? manifestReads = reuseManifestReads ? new() : null;
    internal async Task RequireAsync(AcquisitionTaskDbModel row, int[] owners,
        string partId, string directory, CancellationToken ct, int? ownedDownloadTaskId = null,
        bool explicitTaskCleanup = false, bool abandonUnfinishedFiles = false, int? processingCaptureRun = null,
        Func<string, CancellationToken, Task<bool>>? hasActiveConsumers = null,
        Action<string>? referenceFound = null, string? privateReceiptDirectory = null)
    {
        int? ForeignRun(string? ownerKind, string? ownerId) =>
            ownerKind == "workflow" && int.TryParse(ownerId, out var runId) && !owners.Contains(runId) ? runId : null;
        int? ForeignBindingRun(List<ArtifactOperationBindingDbModel> bindings) =>
            bindings.Select(b => ForeignRun(b.OwnerKind, b.OwnerId)).FirstOrDefault(id => id.HasValue);
        InvalidOperationException SharedReference(string category, int? foreignRun = null)
        {
            referenceFound?.Invoke(foreignRun is { } id ? $"{category}:run:{id}" : category);
            return new("STAGE_DIRECTORY_SHARED");
        }

        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        storage.EnsurePathAllowed(directory); ArtifactManifestFiles.RejectLinks(directory);
        
        var records = await db.Artifacts.AsNoTracking().Where(a => a.State != ArtifactLocationState.Retired)
            .Select(a => new {a.Id, a.RootPath, a.RootIsAppData, a.QuarantineSourceRootPath, a.QuarantineSourceRootIsAppData, a.Fingerprint, a.UpdatedAt}).ToListAsync(ct);
        var mappings = workspaces?.ReadRelocations() ?? [];
        var pathReferences = new ArtifactCleanupPathReferences([directory], mappings, ct);
        ArtifactRecord? representative = null;
        var checkedOperations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!record.RootIsAppData)
            {
                var recordedRoot = pathReferences.Relocate(Path.GetFullPath(record.RootPath));
                if (!Overlap(directory, recordedRoot) && record.QuarantineSourceRootPath == null) continue;
            }
            var root = workspaces?.ResolveStoredArtifactRoot(record.RootPath, record.RootIsAppData, mappings) ??
                pathReferences.Relocate(!record.RootIsAppData ? record.RootPath :
                    await artifacts.GetArtifactDirectoryAsync(record.Id, ct) ?? throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN"));
            var quarantineSource = record.QuarantineSourceRootPath is { } original
                ? workspaces?.ResolveStoredArtifactRoot(original, record.QuarantineSourceRootIsAppData, mappings) ??
                    (record.QuarantineSourceRootIsAppData
                        ? throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN") : Path.GetFullPath(original)) : null;
            if (!Overlap(directory, root) && (quarantineSource == null || !Overlap(directory, quarantineSource))) continue;
            var key = (directory, privateReceiptDirectory, record.Id);
            ManifestRead? read = null;
            if (manifestReads?.TryGetValue(key, out read) != true || read!.Root != root || read.OriginalRoot != quarantineSource ||
                read.Fingerprint != record.Fingerprint || read.Updated != record.UpdatedAt)
            {
                var full = await artifacts.GetArtifactAsync(record.Id, ct)
                    ?? throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN");
                if (!ArtifactManifestFiles.PathComparer.Equals(pathReferences.Relocate(full.RootDirectory), root) ||
                    full.Fingerprint != record.Fingerprint) throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN");
                var ancestor = ArtifactCleanupPathReferences.StrictlyUnder(directory, root);
                bool HasLeaf(string from, string target) => ArtifactManifestFiles.ResolveFiles(from, full.Manifest)
                    .Concat(ArtifactManifestFiles.ResolveDirectories(from, full.Manifest)).Any(path => Overlap(path, target));
                // Receipt slots are never normal producer artifacts. A saved registration of a held
                // payload protects it even if someone attached that registration to this same run.
                bool Claims(string from, string held) => Overlap(from, held) &&
                    (!ArtifactCleanupPathReferences.StrictlyUnder(held, from) || HasLeaf(from, held));
                var hasLeaf = !ancestor || HasLeaf(root, directory) ||
                    quarantineSource != null && Claims(quarantineSource, directory);
                var claimsPrivate = privateReceiptDirectory is {} held &&
                    (Claims(root, held) || quarantineSource != null && Claims(quarantineSource, held));
                // Only a representative for this exact directory can need the full artifact for the path guard.
                var projected = AcquisitionStageDirectoryFileMove.Same(directory, root) ? full
                    : full with {Files = [], Manifest = new([], full.Fingerprint), MetadataJson = null, ParentArtifactIds = []};
                read = new(root, quarantineSource, record.Fingerprint, record.UpdatedAt, projected, hasLeaf, claimsPrivate);
                if (manifestReads != null) manifestReads[key] = read;
            }
            if (read!.ClaimsPrivateDirectory) throw SharedReference("registered-reclaim-file");
            var artifact = read.Artifact;
            // Explicit task deletion selects a current directory, including files changed
            // manually. An older part/attempt or failed producer is not an outside owner.
            // Directory changes and runtime moves retain their narrower part-level rules.
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
            var sameTask = bindings.Count > 0 && bindings.All(b => b.OwnerKind == "workflow" &&
                int.TryParse(b.OwnerId, out var owner) && owners.Contains(owner));
            if (!sameTask && bindings.Count == 0 && AcquisitionStageDirectoryFileMove.Same(directory, root))
                sameTask = await AcquisitionCurrentDirectoryFiles.IsLocalDownloadDirectoryArtifactAsync(
                    artifacts, row, partId, directory, artifact, ct);
            if (ArtifactCleanupPathReferences.StrictlyUnder(directory, root))
            {
                // Manifest roots can be shared containers. Only their exact registered
                // leaves and intentional directories claim descendants.
                var producer = await artifacts.GetOperationAsync(artifact.OperationId, ct);
                if ((!explicitTaskCleanup || !sameTask) &&
                    (producer is not {State: ArtifactOperationState.Completed} || producer.OutputArtifactId != artifact.Id))
                    throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN");
                if (!read.HasLeafUnderDirectory) continue;
            }
            if ((!explicitTaskCleanup || !sameTask) &&
                (artifact.State is ArtifactLocationState.Quarantined || record.QuarantineSourceRootPath != null))
                throw SharedReference("quarantined-artifact", ForeignBindingRun(bindings));
            if (checkedOperations.Add(artifact.OperationId))
            {
                if (!sameTask)
                    throw SharedReference("shared-artifact-owner", ForeignBindingRun(bindings));
                if (!explicitTaskCleanup && bindings.Any(b => !b.Slot.StartsWith(partId + ":", StringComparison.Ordinal)))
                    throw SharedReference("different-artifact-part");
                // Called only after this artifact's concrete root/manifest overlaps the
                // selected directory. Durable workflow pins outlive their lease clock;
                // use the store/cleanup authorization's lifecycle-aware reader, including
                // the narrowly proven superseded-own-run exception during a preview.
                if (explicitTaskCleanup && await (hasActiveConsumers?.Invoke(artifact.OperationId, ct) ??
                    artifacts.HasActiveConsumersAsync(artifact.OperationId, ct)))
                    throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
                var consumers = await db.ArtifactConsumers.AsNoTracking().Where(c => c.OperationId == artifact.OperationId && c.ReleasedAt == null).ToListAsync(ct);
                // Pinned workflow consumers follow their durable run, not their old
                // lease timestamp. The complete task scope is checked for live runs,
                // pending answers and queues by the deletion activity fence; a proven
                // superseded wait is closed before execution. Outside leases still block.
                var foreignConsumer = consumers.FirstOrDefault(c => c.LeaseUntil > DateTime.UtcNow &&
                    (c.OwnerKind != "workflow" || !int.TryParse(c.OwnerId, out var owner) || !owners.Contains(owner)));
                if (foreignConsumer != null)
                    throw SharedReference("foreign-artifact-consumer", ForeignRun(foreignConsumer.OwnerKind, foreignConsumer.OwnerId));
                if (explicitTaskCleanup && await db.ArtifactOperations.AnyAsync(o => o.Id == artifact.OperationId &&
                    (o.State == ArtifactOperationState.Running || o.LeaseUntil > DateTime.UtcNow), ct))
                    throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
            }
            if (AcquisitionStageDirectoryFileMove.Same(directory, root) &&
                (representative == null || artifact.Kind == "download")) representative = artifact;
        }
        representative ??= new("stage-directory:" + row.Id, "", "stageDirectory", directory, [],
            new([], ""), "", ArtifactLocationState.Available, false, [], null);
        var references = new ArtifactCleanupReferenceGuard(db, workspaces, referenceFound: referenceFound);
        if (await references.HasReferencesAsync(representative, owners, ct, previewSupersededRuns: owners,
                directoryOwnerTaskId: row.Id, deletedDirectoryOwner: row.DeletedAt != null,
                directoryDownloadTaskId: ownedDownloadTaskId, explicitTaskDirectoryCleanup: explicitTaskCleanup,
                abandonUnfinishedFiles: abandonUnfinishedFiles, directoryOwnerRunId: processingCaptureRun,
                processingPartId: processingCaptureRun.HasValue ? partId : null))
            throw new InvalidOperationException("STAGE_DIRECTORY_REFERENCED");
    }
    private static bool Overlap(string a, string b) => ArtifactManifestFiles.PathComparer.Equals(a, b) ||
        ArtifactCleanupPathReferences.StrictlyUnder(a, b) || ArtifactCleanupPathReferences.StrictlyUnder(b, a);
}

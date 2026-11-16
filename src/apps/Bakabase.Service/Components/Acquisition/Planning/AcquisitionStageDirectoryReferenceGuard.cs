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
    IUserStoragePolicy storage, FileProcessingWorkspaceResolver? workspaces)
{
    internal async Task RequireAsync(AcquisitionTaskDbModel row, int[] owners,
        string partId, string directory, CancellationToken ct, int? ownedDownloadTaskId = null)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        storage.EnsurePathAllowed(directory); ArtifactManifestFiles.RejectLinks(directory);
        
        var records = await db.Artifacts.AsNoTracking().Where(a => a.State != ArtifactLocationState.Retired)
            .Select(a => new {a.Id, a.RootPath, a.RootIsAppData, a.QuarantineSourceRootPath}).ToListAsync(ct);
        ArtifactRecord? representative = null;
        var checkedOperations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!record.RootIsAppData)
            {
                var recordedRoot = workspaces?.Relocate(Path.GetFullPath(record.RootPath)) ?? Path.GetFullPath(record.RootPath);
                if (!Overlap(directory, recordedRoot) && record.QuarantineSourceRootPath == null) continue;
            }
            var artifact = await artifacts.GetArtifactAsync(record.Id, ct)
                ?? throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN");
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifact.RootDirectory));
            if (!Overlap(directory, root)) continue;
            if (ArtifactCleanupPathReferences.StrictlyUnder(directory, root))
            {
                // Manifest roots can be shared containers. Only their exact registered
                // leaves and intentional directories claim descendants.
                var producer = await artifacts.GetOperationAsync(artifact.OperationId, ct);
                if (producer is not {State: ArtifactOperationState.Completed} || producer.OutputArtifactId != artifact.Id)
                    throw new InvalidOperationException("STAGE_DIRECTORY_OWNERSHIP_UNKNOWN");
                if (!ArtifactManifestFiles.ResolveFiles(root, artifact.Manifest)
                    .Concat(ArtifactManifestFiles.ResolveDirectories(root, artifact.Manifest))
                    .Any(path => Overlap(path, directory))) continue;
            }
            if (artifact.State is ArtifactLocationState.Quarantined || record.QuarantineSourceRootPath != null)
                throw new InvalidOperationException("STAGE_DIRECTORY_SHARED");
            if (checkedOperations.Add(artifact.OperationId))
            {
                var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
                if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" ||
                    !int.TryParse(b.OwnerId, out var owner) || !owners.Contains(owner) ||
                    !b.Slot.StartsWith(partId + ":", StringComparison.Ordinal)))
                    throw new InvalidOperationException("STAGE_DIRECTORY_SHARED");
                var consumers = await db.ArtifactConsumers.AsNoTracking().Where(c => c.OperationId == artifact.OperationId && c.ReleasedAt == null).ToListAsync(ct);
                if (consumers.Any(c => c.LeaseUntil > DateTime.UtcNow &&
                    (c.OwnerKind != "workflow" || !int.TryParse(c.OwnerId, out var owner) || !owners.Contains(owner))))
                    throw new InvalidOperationException("STAGE_DIRECTORY_SHARED");
            }
            if (AcquisitionStageDirectoryFileMove.Same(directory, root) &&
                (representative == null || artifact.Kind == "download")) representative = artifact;
        }
        representative ??= new("stage-directory:" + row.Id, "", "stageDirectory", directory, [],
            new([], ""), "", ArtifactLocationState.Available, false, [], null);
        var references = new ArtifactCleanupReferenceGuard(db, workspaces);
        if (await references.HasReferencesAsync(representative, owners, ct, previewSupersededRuns: owners,
                directoryOwnerTaskId: row.Id, deletedDirectoryOwner: row.DeletedAt != null,
                directoryDownloadTaskId: ownedDownloadTaskId))
            throw new InvalidOperationException("STAGE_DIRECTORY_REFERENCED");
    }
    private static bool Overlap(string a, string b) => ArtifactManifestFiles.PathComparer.Equals(a, b) ||
        ArtifactCleanupPathReferences.StrictlyUnder(a, b) || ArtifactCleanupPathReferences.StrictlyUnder(b, a);
}

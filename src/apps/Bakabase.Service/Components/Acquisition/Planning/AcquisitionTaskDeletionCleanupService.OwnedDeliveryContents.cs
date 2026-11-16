using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    /// <summary>Explicit removal of a producer-proven, exclusively allocated delivery root.
    /// This is never used by automatic cleanup, borrowed locations or ordinary verification.
    /// The returned fixed selection is compared again under the maintenance fence.</summary>
    private async Task<DeliveredCandidate> PreviewOwnedDeliveryContentsAsync(ArtifactRecord artifact,
        ArtifactDeliveryCleanupAuthorization authorization, CancellationToken ct)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifact.RootDirectory));
        var files = new List<AcquisitionTaskDeleteCleanupFile>(); var directories = new List<string>();
        var identities = new Dictionary<string, string>(ArtifactManifestFiles.PathComparer);
        var pending = new Stack<(string Directory, int Depth)>(); pending.Push((root, 0)); var count = 0;
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(current.Directory);
            if (current.Depth > 128 || ++count > 100000) return Block("CLEANUP_FILES_RETAINED");
            foreach (var path in Directory.EnumerateFileSystemEntries(current.Directory).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(path); storage.EnsurePathAllowed(path);
                if (Directory.Exists(path))
                {
                    // Internal execution and quarantine journals are not user delivery
                    // payloads. Their established receipt cleanup remains independent.
                    if (Path.GetFileName(path) is ".bakabase-work" or ".bakabase-quarantine") continue;
                    directories.Add(path); pending.Push((path, current.Depth + 1)); continue;
                }
                if (++count > 100000) return Block("CLEANUP_FILES_RETAINED");
                var info = new FileInfo(path); info.Refresh();
                if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory)) return Block("SelectionChanged");
                files.Add(new(path, info.Length));
                identities.Add(path, JsonSerializer.Serialize(new {info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc}));
            }
        }
        if (await authorization.HasOtherRegisteredFilesAsync(files.Select(f => f.Path).ToArray(), artifact.Id, ct))
            return Block("ReferencedArtifact");
        // Root ownership was checked immediately before enumeration. Revalidate the full
        // directory reference, not only obsolete manifest paths after manual reorganization.
        if (!await OwnsDeliveryDirectoryAsync(artifact, authorization, ct) ||
            await new ArtifactCleanupReferenceGuard(db, workspaces).HasReferencesAsync(artifact,
                authorization.Consent.OwnerRunIds, ct, previewSupersededRuns: authorization.PreviewSupersededRuns))
            return Block("ReferencedPath");
        return new(artifact.Id, artifact.OperationId, root, artifact.Fingerprint,
            ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {artifact.Id, artifact.Fingerprint, Root = root,
                Files = identities.OrderBy(p => p.Key, StringComparer.Ordinal), Directories = directories.Order(StringComparer.Ordinal),
                CurrentOwnedDirectory = true})), files, FileIdentities: identities, RemoveOwnedRoot: true,
            CurrentDirectories: directories);

        DeliveredCandidate Block(string reason) => new(artifact.Id, artifact.OperationId, root,
            artifact.Fingerprint, "", files, reason);
    }
}

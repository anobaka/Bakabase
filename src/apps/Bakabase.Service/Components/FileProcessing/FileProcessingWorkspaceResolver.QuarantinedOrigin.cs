using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingWorkspaceResolver
{
    // Read-only reference ownership for an already registered managed selection. This does
    // not authorize file removal, restore bytes, or extend the normal retired-root resolver.
    internal async Task<string?> ReadQuarantinedArtifactOriginAsync(ArtifactDbModel artifact, CancellationToken ct)
    {
        if (!artifact.Managed || artifact.State is not (ArtifactLocationState.Quarantined or ArtifactLocationState.Retired)) return null;
        try
        {
            string Stored(string path, bool relative)
            {
                if (!relative) return Relocate(Path.GetFullPath(path));
                if (Path.IsPathRooted(path) || path.Replace('\\', '/').Split('/').Any(p => p == ".."))
                    throw new InvalidDataException("Invalid artifact storage path.");
                return Relocate(FileProcessingFiles.Within(Path.Combine(appData, path), appData));
            }
            var current = Stored(artifact.RootPath, artifact.RootIsAppData);
            var id = Path.GetFileName(current);
            if (!Guid.TryParseExact(id, "N", out _) || Path.GetFileName(Path.GetDirectoryName(current)) != ".bakabase-quarantine") return null;
            var file = Path.Combine(appData, "file-processing", "quarantine-receipts", id + ".json");
            ArtifactManifestFiles.RejectLinks(file);
            if (!File.Exists(file) || new FileInfo(file).Length > 64L * 1024 * 1024) return null;
            var receipt = JsonSerializer.Deserialize<ArchiveQuarantineService.Receipt>(await File.ReadAllTextAsync(file, ct), Json);
            var manifest = JsonSerializer.Deserialize<ArtifactManifest>(artifact.ManifestJson, Json);
            if (receipt is not {Managed: true} || receipt.Id != id || receipt.ArtifactId != artifact.Id || manifest == null ||
                manifest.Fingerprint != artifact.Fingerprint || !FileDispositionProofs.SameManifest(receipt.Manifest, manifest) ||
                manifest.Fingerprint != ArtifactManifestFiles.Fingerprint(manifest.Entries, manifest.Directories) ||
                (artifact.State == ArtifactLocationState.Quarantined
                    ? receipt.Status is not ("Moving" or "Quarantined" or "Deleting")
                    : receipt.Status is not ("Deleting" or "Purged"))) return null;
            var original = ArchiveQuarantineService.DecodeLocation(receipt.OriginalRoot, appData, this);
            if (!ArtifactManifestFiles.PathComparer.Equals(current, ArchiveQuarantineService.DecodeLocation(receipt.QuarantineRoot, appData, this)) ||
                !ArtifactManifestFiles.PathComparer.Equals(current, Path.Combine(original, ".bakabase-quarantine", id))) return null;
            if (artifact.State == ArtifactLocationState.Quarantined && (artifact.QuarantineSourceRootPath == null ||
                !ArtifactManifestFiles.PathComparer.Equals(original, Stored(artifact.QuarantineSourceRootPath, artifact.QuarantineSourceRootIsAppData)))) return null;
            return original;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {return null;}
    }
}

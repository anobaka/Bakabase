using System;
using System.Collections.Generic;
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
    internal string ResolveStoredArtifactRoot(string path, bool relative, IReadOnlyList<Relocation>? mappings = null)
    {
        if (!relative) return Map(Path.GetFullPath(path), false, mappings);
        if (Path.IsPathRooted(path) || path.Replace('\\', '/').Split('/').Any(p => p == ".."))
            throw new InvalidDataException("The artifact path escapes application data.");
        return Map(FileProcessingFiles.Within(Path.Combine(appData, path), appData), false, mappings);
    }
    // This only identifies an existing input container. It neither revives a retired artifact
    // nor establishes that its removed bytes remain available for processing or reuse.
    internal async Task<string?> ReadOriginalArtifactDirectoryAsync(ArtifactDbModel artifact, CancellationToken ct)
    {
        var current = ResolveStoredArtifactRoot(artifact.RootPath, artifact.RootIsAppData);
        if (artifact.State == ArtifactLocationState.Quarantined) return null;
        if (artifact.State != ArtifactLocationState.Retired) return current;
        var id = Path.GetFileName(current);
        if (!Guid.TryParseExact(id, "N", out _) || Path.GetFileName(Path.GetDirectoryName(current)) != ".bakabase-quarantine") return null;
        // The path identifies which receipt to read, but the complete immutable receipt
        // proves the original location. Folder names alone never grant this exception.
        var file = Path.Combine(appData, "file-processing", "quarantine-receipts", id + ".json");
        ArtifactManifestFiles.RejectLinks(file);
        if (!File.Exists(file) || new FileInfo(file).Length > 64 * 1024 * 1024) return null;
        var receipt = JsonSerializer.Deserialize<ArchiveQuarantineService.Receipt>(await File.ReadAllTextAsync(file, ct), Json);
        var manifest = JsonSerializer.Deserialize<ArtifactManifest>(artifact.ManifestJson, Json);
        if (receipt is not {Status: "Purged", Managed: true} || receipt.Id != id || receipt.ArtifactId != artifact.Id ||
            !artifact.Managed || manifest?.Fingerprint != artifact.Fingerprint ||
            !FileDispositionProofs.SameManifest(receipt.Manifest, manifest) ||
            manifest.Fingerprint != ArtifactManifestFiles.Fingerprint(manifest.Entries, manifest.Directories)) return null;
        var original = ArchiveQuarantineService.DecodeLocation(receipt.OriginalRoot, appData, this);
        if (!ArtifactManifestFiles.PathComparer.Equals(current,
                ArchiveQuarantineService.DecodeLocation(receipt.QuarantineRoot, appData, this)) ||
            !ArtifactManifestFiles.PathComparer.Equals(current, Path.Combine(original, ".bakabase-quarantine", id))) return null;
        return original;
    }
}

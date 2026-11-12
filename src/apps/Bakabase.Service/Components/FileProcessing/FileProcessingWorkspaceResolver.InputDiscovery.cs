using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed record OwnedWorkspaceDirectory(string Directory, string Kind, string Key, int Attempt);

public sealed partial class FileProcessingWorkspaceResolver
{
    internal async Task<IReadOnlyList<OwnedWorkspaceDirectory>> OwnedDirectoriesUnderAsync(string root, CancellationToken ct)
    {
        var result = new Dictionary<string, OwnedWorkspaceDirectory>(ArtifactManifestFiles.PathComparer);
        var locations = Path.Combine(MetadataRoot, "locations");
        ArtifactManifestFiles.RejectLinks(locations);
        if (!Directory.Exists(locations)) return [];
        foreach (var path in Directory.EnumerateFiles(locations, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(path);
            if (new FileInfo(path).Length > 64 * 1024) continue;
            Location? location;
            try { location = JsonSerializer.Deserialize<Location>(await File.ReadAllTextAsync(path, ct), Json); }
            catch (JsonException) { continue; }
            if (location == null || location.Version != 1 || location.Kind is not ("operations" or "checkpoints") ||
                !Path.IsPathFullyQualified(location.StorageRoot)) continue;
            var ownerFile = Path.Combine(MetadataRoot, "owner.json");
            ArtifactManifestFiles.RejectLinks(ownerFile);
            if (!File.Exists(ownerFile) || new FileInfo(ownerFile).Length > 128) continue;
            var owner = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(ownerFile, ct), Json);
            if (!Guid.TryParseExact(owner, "N", out _)) continue;
            var directory = Path.Combine(Relocate(location.StorageRoot), ".bakabase-work", owner!);
            if (!ArtifactCleanupPathReferences.StrictlyUnder(directory, root)) continue;
            var proof = new OwnedWorkspaceDirectory(directory, location.Kind, location.Key, location.Attempt);
            if (await MatchesOwnedDirectoryAsync(proof, ct)) result.TryAdd(directory, proof);
        }
        return [.. result.Values];
    }

    internal async Task<bool> MatchesOwnedDirectoryAsync(OwnedWorkspaceDirectory proof, CancellationToken ct)
    {
        var location = await ReadLocationAsync(proof.Kind, proof.Key, proof.Attempt, ct);
        if (location == null) return false;
        var ownerFile = Path.Combine(MetadataRoot, "owner.json");
        ArtifactManifestFiles.RejectLinks(ownerFile);
        if (!File.Exists(ownerFile) || new FileInfo(ownerFile).Length > 128) return false;
        var owner = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(ownerFile, ct), Json);
        if (!Guid.TryParseExact(owner, "N", out _)) return false;
        var directory = Path.Combine(Relocate(location.StorageRoot), ".bakabase-work", owner!);
        if (!ArtifactManifestFiles.PathComparer.Equals(proof.Directory, directory) ||
            !ArtifactCleanupPathReferences.StrictlyUnder(Relocate(location.Directory), directory)) return false;
        var marker = Path.Combine(directory, "workspace-owner.json");
        ArtifactManifestFiles.RejectLinks(marker);
        return File.Exists(marker) && new FileInfo(marker).Length == 32 && await File.ReadAllTextAsync(marker, ct) == owner;
    }
}

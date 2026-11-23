using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingWorkspaceResolver
{
    internal async Task<bool> HasOwnedLocationAsync(Location location, CancellationToken ct)
    {
        // This proof checks the exact locator, readable storage and installation marker;
        // the empty-checkpoint caller separately checks emptiness before using it.
        await ValidateEmptyCheckpointOwnerAsync(location.Key, location.Attempt, location.Directory, ct, location.Kind);
        return true;
    }

    internal async Task<Location?> InspectOwnedLocationForStageCleanupAsync(string kind, string key, int attempt,
        CancellationToken ct)
    {
        var stored = await ReadLocationAsync(kind, key, attempt, ct);
        if (stored == null) return null;
        var location = stored with {Directory = Relocate(stored.Directory), StorageRoot = Relocate(stored.StorageRoot)};
        await RequireWorkspaceOwnerAsync(location.StorageRoot, location.Directory, kind, ct, requireExistingDirectory: false);
        if (Directory.Exists(location.Directory)) return location;

        // The exact attempt can prove an absent tree only from its readable parent.
        return Path.GetDirectoryName(location.Directory) is { } parent &&
            ArtifactCleanupAbsence.Probe(parent, location.Directory, ct) == ArtifactCleanupPathPresence.ConfirmedAbsent
                ? location : null;
    }
}

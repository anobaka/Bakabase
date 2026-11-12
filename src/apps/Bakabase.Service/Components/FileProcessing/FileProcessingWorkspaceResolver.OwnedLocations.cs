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
}

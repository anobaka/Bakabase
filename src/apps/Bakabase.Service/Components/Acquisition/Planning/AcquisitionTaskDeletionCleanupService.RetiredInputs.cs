using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    // Used only after the completed producer, same-task exclusive bindings, exact
    // manifest and inactive consumers were checked. A Retired flag by itself cannot
    // explain disappearance: the existing full purged receipt must still agree.
    private async Task<string?> RetiredStagedInputRootAsync(ArtifactRecord input, CancellationToken ct)
    {
        if (workspaces == null || !input.Managed || input.Kind is not ("download" or "processedFiles")) return null;
        var registered = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == input.Id, ct);
        if (registered == null || registered.State != ArtifactLocationState.Retired ||
            registered.OperationId != input.OperationId || registered.Fingerprint != input.Fingerprint) return null;
        // This validates the complete receipt, original/quarantine locations, managed
        // identity, manifest SHA/lengths and Purged state. It never revives the input.
        return await workspaces.ReadOriginalArtifactDirectoryAsync(registered, ct);
    }
}

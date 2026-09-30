using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.Federation.Identity;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// This install's identity in data sync (spec §2.11): the federation node — its id, its library epoch and its name —
/// so an actor is derived from the same identity peers pair with (§5.6).
/// </summary>
public sealed class FederationDataSyncDeviceIdentity(INodeIdentityProvider nodes) : IDataSyncDeviceIdentity
{
    public async Task<DataSyncDevice> GetAsync(CancellationToken ct)
    {
        var node = await nodes.GetAsync(ct);
        return new DataSyncDevice(node.NodeId, node.LibraryEpoch, node.Name);
    }
}

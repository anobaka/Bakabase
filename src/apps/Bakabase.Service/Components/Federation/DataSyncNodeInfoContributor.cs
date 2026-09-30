using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Says in this node's <c>/info</c> and handshake what data sync it speaks (§7.4): its contract and the lowest one a
/// reader must speak, and whether devices it approved may read its definitions right now. Readers use it to say "too
/// old" or "not shared" before asking; nothing is decided on it here.
/// </summary>
public sealed class DataSyncNodeInfoContributor(FederationStateStore store) : INodeInfoContributor
{
    public async ValueTask<NodeInfo> ContributeAsync(NodeInfo info, CancellationToken ct) => info with
    {
        DataSyncContractVersion = DataSyncContract.Version,
        DataSyncMinimumPeerContract = DataSyncContract.MinimumPeerVersion,
        SharesDefinitions = await store.IsDataSyncSharingEnabledAsync(ct)
    };
}

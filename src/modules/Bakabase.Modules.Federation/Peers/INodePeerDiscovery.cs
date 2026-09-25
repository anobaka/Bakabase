using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.Federation.Peers;

/// <param name="Kind">What kind of install its node info says it is, when it says: a hint, like the rest.</param>
/// <param name="Platform">What its node info says it runs on, when it says.</param>
/// <param name="DataSyncContractVersion">
/// The data sync contract its node info says it speaks (<see cref="NodeInfo.DataSyncContractVersion"/>); null on a
/// node without data sync.
/// </param>
/// <param name="SharesDefinitions">
/// Whether its node info says devices it approved may read its definitions (<see cref="NodeInfo.SharesDefinitions"/>).
/// </param>
public sealed record NodeDiscoveryCandidate(string NodeId, string Name, string Address,
    ServerKind? Kind = null, RemoteDevicePlatform? Platform = null, int? DataSyncContractVersion = null,
    bool? SharesDefinitions = null);

public interface INodePeerDiscovery
{
    Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default);
}

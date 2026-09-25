using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.Federation.Peers;

/// <param name="Kind">What kind of install its node info says it is, when it says: a hint, like the rest.</param>
/// <param name="Platform">What its node info says it runs on, when it says.</param>
public sealed record NodeDiscoveryCandidate(string NodeId, string Name, string Address,
    ServerKind? Kind = null, RemoteDevicePlatform? Platform = null);

public interface INodePeerDiscovery
{
    Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default);
}

namespace Bakabase.Modules.Federation.Peers;

public sealed record NodeDiscoveryCandidate(string NodeId, string Name, string Address);

public interface INodePeerDiscovery
{
    Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default);
}

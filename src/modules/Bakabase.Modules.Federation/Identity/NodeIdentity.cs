namespace Bakabase.Modules.Federation.Identity;

/// <summary>The host supplies its resolved AppData/federation directory.</summary>
public interface IFederationDataDirectory
{
    string Path { get; }
    string Ensure();
}

/// <summary>Bridges the existing persistent server identity without depending on a host.</summary>
public interface INodeIdSource
{
    Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default);
}

public sealed record NodeIdentity(string NodeId, string LibraryEpoch, string Name);

public interface INodeIdentityProvider
{
    Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class NodeIdentityProvider(FederationStateStore store) : INodeIdentityProvider
{
    public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
        store.GetIdentityAsync(cancellationToken);
}

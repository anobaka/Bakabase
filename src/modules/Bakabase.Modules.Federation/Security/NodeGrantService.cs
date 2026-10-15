using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.Federation.Security;

/// <param name="Scope">What the grant lets the caller read (<see cref="FederationScopes"/>); never <see cref="FederationScopes.Any"/>.</param>
public sealed record NodePrincipal(string GrantId, string SubjectNodeId, string AudienceNodeId,
    string LibraryEpoch, long Revision, string Scope = FederationScopes.DataSyncRead);

public interface INodeGrantService
{
    Task<NodePrincipal> ValidateAsync(string grantId, string expectedLibraryEpoch,
        CancellationToken cancellationToken = default);
}

public sealed class NodeGrantService(FederationStateStore store, INodeIdentityProvider identity,
    GrantLeaseRegistry leases, TimeProvider timeProvider, IServerSelfDescription? self = null,
    INodeInfoContributor? contributor = null) : INodeGrantService
{
    public async Task<NodePrincipal> ValidateAsync(string grantId, string expectedLibraryEpoch,
        CancellationToken cancellationToken = default)
    {
        var (credentials, scope) = await GetCredentialsAsync(grantId, cancellationToken);
        if (credentials.LibraryEpoch != expectedLibraryEpoch)
            throw new FederationAccessException("LibraryEpochChanged", 409, "The source library was replaced. Refresh its identity and pair again.");
        return new NodePrincipal(credentials.GrantId, credentials.SubjectNodeId, credentials.AudienceNodeId,
            credentials.LibraryEpoch, credentials.Revision, scope);
    }

    /// <summary>
    /// Finds a live grant of either scope and checks that scope's own switch: a grant is only as good as the
    /// sharing it was issued under. Grant ids are random and never collide across the two collections.
    /// </summary>
    internal async Task<(NodeCredentials Credentials, string Scope)> GetCredentialsAsync(string grantId,
        CancellationToken ct)
    {
        var local = await identity.GetAsync(ct);
        var state = await store.ReadAsync(ct);
        if (!state.DataSyncSharingEnabled) throw DataSyncSharingDisabled();
        if (!state.InboundDataSyncGrants.TryGetValue(grantId, out var grant))
            throw GrantRevoked();
        const string scope = FederationScopes.DataSyncRead;
        if (grant.Revoked || grant.Credentials.AudienceNodeId != local.NodeId ||
            grant.Credentials.LibraryEpoch != local.LibraryEpoch ||
            leases.GetCancellationToken(grantId).IsCancellationRequested)
            throw GrantRevoked();
        return (grant.Credentials, scope);
    }

    /// <summary>Answers a handshake made with a grant of either scope.</summary>
    public async Task<NodeHandshakeResponse> CreateHandshakeAsync(string grantId, string challenge,
        CancellationToken ct = default)
    {
        if (!NodeRequestSignature.IsIdentifier(challenge) || challenge.Length < 16)
            throw new FederationAccessException("InvalidChallenge", 400, "The identity challenge is invalid.");
        var (credentials, _) = await GetCredentialsAsync(grantId, ct);
        var local = await identity.GetAsync(ct);
        var info = new NodeInfo(local.NodeId, local.LibraryEpoch, local.Name, 1, timeProvider.GetUtcNow())
            .DescribedBy(self);
        if (contributor != null) info = await contributor.ContributeAsync(info, ct);
        return new NodeHandshakeResponse(info, challenge,
            NodeRequestSignature.HandshakeProof(credentials.Key, info, challenge));
    }

    public static FederationAccessException ScopeNotGranted() =>
        new("ScopeNotGranted", 403, "This node's permission does not cover this request.");

    public static FederationAccessException DataSyncSharingDisabled() =>
        new("DataSyncSharingDisabled", 403, "Definitions sharing is disabled on this node.");

    private static FederationAccessException GrantRevoked() => new("GrantRevoked", 401,
        "This node no longer has permission to read the source's definitions.");
}

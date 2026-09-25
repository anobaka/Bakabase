using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.Federation.Security;

/// <param name="Scope">What the grant lets the caller read (<see cref="FederationScopes"/>); never <see cref="FederationScopes.Any"/>.</param>
public sealed record NodePrincipal(string GrantId, string SubjectNodeId, string AudienceNodeId,
    string LibraryEpoch, long Revision, string Scope = FederationScopes.LibraryRead);

public interface INodeGrantService
{
    Task<NodePrincipal> ValidateAsync(string grantId, string expectedLibraryEpoch,
        CancellationToken cancellationToken = default);
}

public sealed class NodeGrantService(FederationStateStore store, INodeIdentityProvider identity,
    GrantLeaseRegistry leases, TimeProvider timeProvider, IServerSelfDescription? self = null,
    INodeInfoContributor? contributor = null) : INodeGrantService
{
    /// <summary>
    /// For the library's export services only: a <c>datasync.read</c> grant is refused here too
    /// (<c>ScopeNotGranted</c>), behind the gate that already keeps it off library routes.
    /// </summary>
    public async Task<NodePrincipal> ValidateAsync(string grantId, string expectedLibraryEpoch,
        CancellationToken cancellationToken = default)
    {
        var (credentials, scope) = await GetCredentialsAsync(grantId, cancellationToken);
        if (scope != FederationScopes.LibraryRead)
            throw ScopeNotGranted();
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
        StoredGrant? grant;
        string scope;
        if (state.InboundGrants.TryGetValue(grantId, out grant))
        {
            scope = FederationScopes.LibraryRead;
            if (!state.SharingEnabled)
                throw new FederationAccessException("SharingDisabled", 403, "Resource sharing is disabled on this node.");
        }
        else if (state.InboundDataSyncGrants.TryGetValue(grantId, out grant))
        {
            scope = FederationScopes.DataSyncRead;
            if (!state.DataSyncSharingEnabled) throw DataSyncSharingDisabled();
        }
        else if (!state.SharingEnabled && !state.DataSyncSharingEnabled)
            // Nothing is shared: say so, as before there were two kinds of grant.
            throw new FederationAccessException("SharingDisabled", 403, "Resource sharing is disabled on this node.");
        else throw GrantRevoked(FederationScopes.LibraryRead);
        if (grant.Revoked || grant.Credentials.AudienceNodeId != local.NodeId ||
            grant.Credentials.LibraryEpoch != local.LibraryEpoch ||
            leases.GetCancellationToken(grantId).IsCancellationRequested)
            throw GrantRevoked(scope);
        return (grant.Credentials, scope);
    }

    /// <summary>Answers a handshake made with a grant of either scope, and says which one it was.</summary>
    public async Task<NodeHandshakeResponse> CreateHandshakeAsync(string grantId, string challenge,
        CancellationToken ct = default)
    {
        if (!NodeRequestSignature.IsIdentifier(challenge) || challenge.Length < 16)
            throw new FederationAccessException("InvalidChallenge", 400, "The identity challenge is invalid.");
        var (credentials, scope) = await GetCredentialsAsync(grantId, ct);
        var local = await identity.GetAsync(ct);
        var info = new NodeInfo(local.NodeId, local.LibraryEpoch, local.Name, 1, timeProvider.GetUtcNow())
            .DescribedBy(self);
        if (contributor != null) info = await contributor.ContributeAsync(info, ct);
        return new NodeHandshakeResponse(info, challenge,
            NodeRequestSignature.HandshakeProof(credentials.Key, info, challenge), scope);
    }

    public static FederationAccessException ScopeNotGranted() =>
        new("ScopeNotGranted", 403, "This node's permission does not cover this request.");

    public static FederationAccessException DataSyncSharingDisabled() =>
        new("DataSyncSharingDisabled", 403, "Definitions sharing is disabled on this node.");

    private static FederationAccessException GrantRevoked(string scope) => new("GrantRevoked", 401,
        scope == FederationScopes.DataSyncRead
            ? "This node no longer has permission to read the source's definitions."
            : "This node no longer has permission to read the source library.");
}

using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.Federation.Security;

public sealed record NodePrincipal(string GrantId, string SubjectNodeId, string AudienceNodeId,
    string LibraryEpoch, long Revision);

public interface INodeGrantService
{
    Task<NodePrincipal> ValidateAsync(string grantId, string expectedLibraryEpoch,
        CancellationToken cancellationToken = default);
}

public sealed class NodeGrantService(FederationStateStore store, INodeIdentityProvider identity,
    GrantLeaseRegistry leases, TimeProvider timeProvider, IServerSelfDescription? self = null) : INodeGrantService
{
    public async Task<NodePrincipal> ValidateAsync(string grantId, string expectedLibraryEpoch,
        CancellationToken cancellationToken = default)
    {
        var credentials = await GetCredentialsAsync(grantId, cancellationToken);
        if (credentials.LibraryEpoch != expectedLibraryEpoch)
            throw new FederationAccessException("LibraryEpochChanged", 409, "The source library was replaced. Refresh its identity and pair again.");
        return new NodePrincipal(credentials.GrantId, credentials.SubjectNodeId, credentials.AudienceNodeId,
            credentials.LibraryEpoch, credentials.Revision);
    }

    internal async Task<NodeCredentials> GetCredentialsAsync(string grantId, CancellationToken ct)
    {
        var local = await identity.GetAsync(ct);
        var state = await store.ReadAsync(ct);
        if (!state.SharingEnabled)
            throw new FederationAccessException("SharingDisabled", 403, "Resource sharing is disabled on this node.");
        if (!state.InboundGrants.TryGetValue(grantId, out var grant) || grant.Revoked ||
            grant.Credentials.AudienceNodeId != local.NodeId || grant.Credentials.LibraryEpoch != local.LibraryEpoch ||
            leases.GetCancellationToken(grantId).IsCancellationRequested)
            throw new FederationAccessException("GrantRevoked", 401, "This node no longer has permission to read the source library.");
        return grant.Credentials;
    }

    public async Task<NodeHandshakeResponse> CreateHandshakeAsync(string grantId, string challenge,
        CancellationToken ct = default)
    {
        if (!NodeRequestSignature.IsIdentifier(challenge) || challenge.Length < 16)
            throw new FederationAccessException("InvalidChallenge", 400, "The identity challenge is invalid.");
        var credentials = await GetCredentialsAsync(grantId, ct);
        var local = await identity.GetAsync(ct);
        var info = new NodeInfo(local.NodeId, local.LibraryEpoch, local.Name, 1, timeProvider.GetUtcNow())
            .DescribedBy(self);
        return new NodeHandshakeResponse(info, challenge, NodeRequestSignature.HandshakeProof(credentials.Key, info, challenge));
    }
}

using System.Globalization;
using System.Security.Cryptography;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Peers;

/// <summary>Owns local sharing decisions. A peer grant never calls these management methods.</summary>
public sealed class FederationPeerService(FederationStateStore store, INodeIdentityProvider identity,
    GrantLeaseRegistry leases, TimeProvider timeProvider)
{
    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(10);
    private const int MaxRequests = 64;

    public async Task<FederationPeerStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var local = await identity.GetAsync(ct);
        var state = await store.ReadAsync(ct);
        var peers = state.Peers.Values.Select(peer =>
        {
            state.OutboundGrants.TryGetValue(peer.NodeId, out var outgoing);
            var incoming = state.InboundGrants.Values.FirstOrDefault(g => !g.Revoked &&
                g.Credentials.SubjectNodeId == peer.NodeId && g.Credentials.LibraryEpoch == local.LibraryEpoch);
            return new FederationPeerView(peer.NodeId, peer.Label, peer.Address, peer.Enabled,
                peer.Enabled ? "Unknown" : "Disabled",
                outgoing == null ? null : new NodeGrantSummary(outgoing.GrantId, outgoing.Revision),
                incoming == null ? null : new NodeGrantSummary(incoming.Credentials.GrantId, incoming.Credentials.Revision),
                peer.PathMappings);
        }).OrderBy(p => p.Label, StringComparer.Ordinal).ToArray();
        var now = timeProvider.GetUtcNow();
        var requests = state.IncomingRequests.Where(r => r.ExpiresAt > now).Select(r =>
                new NodePairingRequestView(r.RequestId, r.NodeId, r.NodeName, "incoming", r.Status, r.ExpiresAt))
            .Concat(state.OutgoingRequests.Where(r => r.ExpiresAt > now).Select(r =>
                new NodePairingRequestView(r.RequestId, r.NodeId, r.NodeName, "outgoing", r.Status, r.ExpiresAt)))
            .ToArray();
        return new FederationPeerStatus(local, state.SharingEnabled, peers, requests);
    }

    public async Task SetSharingAsync(bool enabled, CancellationToken ct = default)
    {
        await identity.GetAsync(ct);
        await store.MutateAsync(state =>
        {
            state.SharingEnabled = enabled;
            return true;
        }, ct);
        if (!enabled) leases.CancelInbound();
        else leases.Resume();
    }

    public async Task<NodeInvitation> IssueInvitationAsync(CancellationToken ct = default)
    {
        await identity.GetAsync(ct);
        var code = RandomNumberGenerator.GetInt32(100_000_000).ToString("D8", CultureInfo.InvariantCulture);
        var expiresAt = timeProvider.GetUtcNow() + PairingLifetime;
        await store.MutateAsync(state =>
        {
            RequireSharing(state);
            state.Invitation = new StoredInvitation
                { CodeHash = NodeRequestSignature.HashSecret(code), ExpiresAt = expiresAt };
            return true;
        }, ct);
        return new NodeInvitation(code, expiresAt);
    }

    public async Task<NodePairExchange> ExchangeCodeAsync(NodePairCodeRequest request,
        CancellationToken ct = default)
    {
        ValidatePairRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret);
        var local = await identity.GetAsync(ct);
        if (request.NodeId == local.NodeId) throw BadRequest("A node cannot pair with itself.");
        var now = timeProvider.GetUtcNow();
        var revoked = new List<string>();
        var result = await store.MutateAsync(state =>
        {
            RequireSharing(state);
            Prune(state, now);
            var existing = FindTransaction(state, request.TransactionId, request.NodeId, request.ClaimSecret);
            // Retrying a pending request with an invitation is a new approval path
            // for the same claimant, not merely a status poll. Completed/rejected
            // transactions remain idempotent and must never be revived by a code.
            if (existing != null && existing.Status != "awaitingApproval") return Exchange(state, existing);
            if (state.Invitation is not { } invitation || invitation.ExpiresAt <= now ||
                invitation.FailedAttempts >= 5)
                return new NodePairExchange("rejected", request.TransactionId, now);
            if (!NodeRequestSignature.FixedEquals(invitation.CodeHash,
                    NodeRequestSignature.HashSecret(request.Code?.Trim())))
            {
                // Return rather than throw: this failed attempt has to be committed.
                invitation.FailedAttempts++;
                return new NodePairExchange("rejected", request.TransactionId, now);
            }
            if (existing == null && state.IncomingRequests.Count >= MaxRequests)
                throw new FederationAccessException("PairingBusy", 429, "Too many pairing requests are pending. Try again later.");
            state.Invitation = null;
            var pending = existing ?? NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret, now);
            IssueGrant(state, pending, local, now, revoked);
            if (existing == null) state.IncomingRequests.Add(pending);
            return Exchange(state, pending);
        }, ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
        return result;
    }

    public async Task<NodePairExchange> RequestPairingAsync(NodePairRequest request, CancellationToken ct = default)
    {
        ValidatePairRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret);
        var local = await identity.GetAsync(ct);
        if (request.NodeId == local.NodeId) throw BadRequest("A node cannot pair with itself.");
        var now = timeProvider.GetUtcNow();
        return await store.MutateAsync(state =>
        {
            RequireSharing(state);
            Prune(state, now);
            var existing = FindTransaction(state, request.TransactionId, request.NodeId, request.ClaimSecret);
            if (existing != null) return Exchange(state, existing);
            if (state.IncomingRequests.Count >= MaxRequests)
                throw new FederationAccessException("PairingBusy", 429, "Too many pairing requests are pending. Try again later.");
            var pending = NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret, now);
            state.IncomingRequests.Add(pending);
            return Exchange(state, pending);
        }, ct);
    }

    public async Task<NodePairExchange> ClaimPairingAsync(NodePairClaimRequest request, CancellationToken ct = default)
    {
        var state = await store.ReadAsync(ct);
        RequireSharing(state);
        var pending = state.IncomingRequests.FirstOrDefault(r => r.RequestId == request.RequestId &&
            r.NodeId == request.NodeId && r.ExpiresAt > timeProvider.GetUtcNow());
        if (pending == null || !NodeRequestSignature.FixedEquals(pending.ClaimSecretHash,
                NodeRequestSignature.HashSecret(request.ClaimSecret)))
            throw new FederationAccessException("PairingRejected", 403, "The pairing request has expired or cannot be claimed.");
        // Re-delivery to the same secret is bounded by expiry and makes crash recovery safe.
        return Exchange(state, pending);
    }

    public async Task ApproveAsync(string requestId, CancellationToken ct = default)
    {
        var local = await identity.GetAsync(ct);
        var revoked = new List<string>();
        await store.MutateAsync(state =>
        {
            RequireSharing(state);
            var request = state.IncomingRequests.FirstOrDefault(r => r.RequestId == requestId &&
                r.ExpiresAt > timeProvider.GetUtcNow()) ?? throw BadRequest("This pairing request has expired.");
            if (request.Status == "granted") return true;
            if (request.Status != "awaitingApproval") throw BadRequest("This pairing request was rejected.");
            IssueGrant(state, request, local, timeProvider.GetUtcNow(), revoked);
            return true;
        }, ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
    }

    public async Task RejectAsync(string requestId, CancellationToken ct = default)
    {
        await store.MutateAsync(state =>
        {
            var request = state.IncomingRequests.FirstOrDefault(r => r.RequestId == requestId);
            if (request is { Status: "awaitingApproval" }) request.Status = "rejected";
            return true;
        }, ct);
    }

    public async Task RevokeAsync(string grantId, CancellationToken ct = default)
    {
        await store.MutateAsync(state =>
        {
            if (state.InboundGrants.TryGetValue(grantId, out var grant)) grant.Revoked = true;
            return true;
        }, ct);
        leases.Revoke(grantId);
    }

    public async Task ForgetOutboundAsync(string nodeId, CancellationToken ct = default)
    {
        var grantId = await store.MutateAsync(state =>
        {
            var grantId = state.OutboundGrants.GetValueOrDefault(nodeId)?.GrantId;
            state.OutboundGrants.Remove(nodeId);
            state.OutgoingRequests.RemoveAll(r => r.NodeId == nodeId);
            if (state.Peers.TryGetValue(nodeId, out var peer)) peer.Enabled = false;
            return grantId;
        }, ct);
        if (grantId != null) leases.Revoke(GrantLeaseRegistry.OutboundKey(grantId));
    }

    public async Task SetEnabledAsync(string nodeId, bool enabled, CancellationToken ct = default)
    {
        var grantId = await store.MutateAsync(state =>
        {
            var peer = state.Peers.GetValueOrDefault(nodeId) ?? throw BadRequest("This node is not known here.");
            peer.Enabled = enabled;
            return state.OutboundGrants.GetValueOrDefault(nodeId)?.GrantId;
        }, ct);
        if (grantId != null)
        {
            if (enabled) leases.Resume(GrantLeaseRegistry.OutboundKey(grantId));
            else leases.Revoke(GrantLeaseRegistry.OutboundKey(grantId));
        }
    }

    public async Task SetPathMappingsAsync(string nodeId, IReadOnlyList<NodePathMapping> mappings,
        CancellationToken ct = default, IReadOnlyList<NodePathMapping>? expectedMappings = null)
    {
        if (mappings.Count > 128 || expectedMappings is { Count: > 128 } ||
            mappings.Any(m => m == null || !NodeRequestSignature.IsIdentifier(m.SourceRootId) ||
                string.IsNullOrWhiteSpace(m.LocalPath) || m.LocalPath.Length > 4096 || !Path.IsPathFullyQualified(m.LocalPath)) ||
            mappings.Select(m => m.SourceRootId).Distinct(StringComparer.Ordinal).Count() != mappings.Count)
            throw BadRequest("Mappings need distinct source root identifiers and absolute paths on this machine.");
        await store.MutateAsync(state =>
        {
            var peer = state.Peers.GetValueOrDefault(nodeId) ?? throw BadRequest("This node is not known here.");
            if (expectedMappings != null && (expectedMappings.Any(m => m == null) ||
                !peer.PathMappings.OrderBy(m => m.SourceRootId, StringComparer.Ordinal).SequenceEqual(
                    expectedMappings.OrderBy(m => m.SourceRootId, StringComparer.Ordinal))))
                throw new FederationAccessException("PathMappingsChanged", 409,
                    "Mappings changed on this device. Review the current values before replacing them.");
            peer.PathMappings = mappings.ToList();
            return true;
        }, ct);
    }

    public async Task<IReadOnlyList<NodePathMapping>> GetPathMappingsAsync(string nodeId, CancellationToken ct = default) =>
        (await store.ReadAsync(ct)).Peers.GetValueOrDefault(nodeId)?.PathMappings ?? [];

    /// <summary>Call from supported database replacement/restore operations before exposing the new library.</summary>
    public async Task<NodeIdentity> RotateLibraryEpochAsync(CancellationToken ct = default)
    {
        var local = await identity.GetAsync(ct);
        var result = await store.MutateAsync(state =>
        {
            state.LibraryEpoch = Guid.NewGuid().ToString("N");
            state.SharingEnabled = false;
            state.BrowsingEnabled = false;
            foreach (var grant in state.InboundGrants.Values) grant.Revoked = true;
            state.IncomingRequests.Clear();
            state.Invitation = null;
            return new NodeIdentity(local.NodeId, state.LibraryEpoch, local.Name);
        }, ct);
        leases.CancelInbound();
        return result;
    }

    /// <summary>Explicit clone operation, never called by startup or ordinary options import.</summary>
    public async Task<NodeIdentity> ResetAsNewNodeAsync(CancellationToken ct = default)
    {
        var result = await store.ResetAsNewNodeAsync(ct);
        leases.CancelAll();
        return result;
    }

    private static void ValidatePairRequest(string nodeId, string name, string transactionId, string secret)
    {
        if (!NodeRequestSignature.IsIdentifier(nodeId) || !NodeRequestSignature.IsIdentifier(transactionId) ||
            !NodeRequestSignature.IsIdentifier(secret) || secret.Length < 32 || string.IsNullOrWhiteSpace(name) ||
            name.Length > 128 || name.Any(char.IsControl))
            throw BadRequest("The pairing identity or claim secret is invalid.");
    }

    private static StoredPairRequest? FindTransaction(FederationState state, string transactionId, string nodeId,
        string claimSecret)
    {
        var request = state.IncomingRequests.FirstOrDefault(r => r.TransactionId == transactionId);
        if (request != null && (request.NodeId != nodeId || !NodeRequestSignature.FixedEquals(request.ClaimSecretHash,
                NodeRequestSignature.HashSecret(claimSecret))))
            throw new FederationAccessException("PairingRejected", 403, "The pairing transaction belongs to another claimant.");
        return request;
    }

    private static StoredPairRequest NewRequest(string nodeId, string name, string transactionId, string secret,
        DateTimeOffset now) => new()
    {
        RequestId = transactionId,
        TransactionId = transactionId,
        NodeId = nodeId,
        NodeName = name.Trim(),
        ClaimSecretHash = NodeRequestSignature.HashSecret(secret),
        ExpiresAt = now + PairingLifetime
    };

    private static NodePairExchange Exchange(FederationState state, StoredPairRequest pending)
    {
        NodeCredentials? credentials = null;
        if (pending.Status == "granted" && pending.GrantId != null &&
            state.InboundGrants.TryGetValue(pending.GrantId, out var grant) && !grant.Revoked)
            credentials = grant.Credentials;
        var status = pending.Status == "granted" && credentials == null ? "rejected" : pending.Status;
        return new NodePairExchange(status, pending.RequestId, pending.ExpiresAt, credentials);
    }

    private static void IssueGrant(FederationState state, StoredPairRequest request, NodeIdentity local,
        DateTimeOffset now, List<string> revoked)
    {
        foreach (var old in state.InboundGrants.Values.Where(g =>
                     !g.Revoked && g.Credentials.SubjectNodeId == request.NodeId))
        {
            old.Revoked = true;
            revoked.Add(old.Credentials.GrantId);
        }
        var credentials = new NodeCredentials(NodeRequestSignature.RandomToken(18), request.NodeId, local.NodeId,
            local.LibraryEpoch, NodeRequestSignature.RandomToken(), 1);
        state.InboundGrants.Add(credentials.GrantId, new StoredGrant { Credentials = credentials, CreatedAt = now });
        request.GrantId = credentials.GrantId;
        request.Status = "granted";
        if (!state.Peers.TryGetValue(request.NodeId, out var peer))
            state.Peers.Add(request.NodeId, peer = new StoredPeer { NodeId = request.NodeId });
        peer.Label = request.NodeName;
    }

    private static void Prune(FederationState state, DateTimeOffset now)
    {
        state.IncomingRequests.RemoveAll(r => r.ExpiresAt <= now);
        state.OutgoingRequests.RemoveAll(r => r.ExpiresAt <= now);
    }

    private static void RequireSharing(FederationState state)
    {
        if (!state.SharingEnabled)
            throw new FederationAccessException("SharingDisabled", 403, "Enable resource sharing before pairing another node.");
    }

    private static FederationAccessException BadRequest(string message) => new("InvalidPairingRequest", 400, message);
}

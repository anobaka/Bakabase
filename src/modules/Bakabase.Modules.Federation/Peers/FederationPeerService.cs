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
    private const int MaxPendingRequests = 64;
    private const int MaxPendingRequestsPerAddress = 4;
    private const int MaxStoredRequests = 256;

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
                peer.PathMappings, peer.Kind, peer.Platform);
        }).OrderBy(p => p.Label, StringComparer.Ordinal).ToArray();
        var now = timeProvider.GetUtcNow();
        var requests = state.IncomingRequests.Where(r => r.ExpiresAt > now).Select(r =>
                new NodePairingRequestView(r.RequestId, r.NodeId, r.NodeName, "incoming", r.Status, r.ExpiresAt,
                    r.RemoteAddress, r.Status == "awaitingApproval" && state.InboundGrants.Values.Any(g =>
                        !g.Revoked && g.Credentials.SubjectNodeId == r.NodeId), r.Reciprocal != null))
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
        string? remoteAddress = null, CancellationToken ct = default)
    {
        ValidatePairRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret);
        var local = await identity.GetAsync(ct);
        if (request.NodeId == local.NodeId) throw BadRequest("A node cannot pair with itself.");
        var now = timeProvider.GetUtcNow();
        var revoked = new List<string>();
        var result = await store.MutateAsync<NodePairExchange?>(state =>
        {
            RequireSharing(state);
            Prune(state, now);
            var existing = FindTransaction(state, request.TransactionId, request.NodeId, request.ClaimSecret);
            // Retrying a pending request with an invitation is a new approval path
            // for the same claimant, not merely a status poll. Completed/rejected
            // transactions remain idempotent and must never be revived by a code.
            if (existing != null && existing.Status != "awaitingApproval") return Exchange(state, existing);
            // A wrong code says nothing about a pending transaction, which stays approvable on
            // both sides. Return rather than throw: a failed attempt has to be committed.
            var codeHash = NodeRequestSignature.HashSecret(request.Code?.Trim());
            var reciprocal = state.ReciprocalInvitations.FirstOrDefault(i => i.ExpiresAt > now &&
                i.AudienceNodeId == request.NodeId && NodeRequestSignature.FixedEquals(i.CodeHash, codeHash));
            if (reciprocal != null) state.ReciprocalInvitations.Remove(reciprocal);
            else if (state.Invitation is not { } invitation || invitation.ExpiresAt <= now ||
                     invitation.FailedAttempts >= 5)
                return null;
            else if (!NodeRequestSignature.FixedEquals(invitation.CodeHash, codeHash))
            {
                invitation.FailedAttempts++;
                return null;
            }
            else state.Invitation = null;
            var pending = existing;
            if (pending == null)
            {
                // A valid code is the owner's approval, so pending-request limits do not apply.
                AdmitIncoming(state, remoteAddress, authorized: true);
                pending = NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret,
                    remoteAddress, now);
                state.IncomingRequests.Add(pending);
            }
            pending.Reciprocal = ValidOffer(request.Reciprocal) ?? pending.Reciprocal;
            IssueGrant(state, pending, local, now, revoked);
            return Exchange(state, pending);
        }, ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
        return result ?? throw new FederationAccessException("InvalidPairingCode", 403,
            "The sharing code is wrong or has expired. Create a new code on the sharing device.");
    }

    public async Task<NodePairExchange> RequestPairingAsync(NodePairRequest request, string? remoteAddress = null,
        CancellationToken ct = default) => (await SubmitPairingRequestAsync(request, remoteAddress, ct)).Exchange;

    /// <summary><see cref="RequestPairingAsync"/>, also telling whether this call created the request.</summary>
    public async Task<(NodePairExchange Exchange, bool Created)> SubmitPairingRequestAsync(NodePairRequest request,
        string? remoteAddress = null, CancellationToken ct = default)
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
            if (existing != null)
            {
                // A retry mints a new offer and the requester keeps only the newest code.
                if (existing.Status == "awaitingApproval" && ValidOffer(request.Reciprocal) is { } retried)
                    existing.Reciprocal = retried;
                return (Exchange(state, existing), false);
            }
            AdmitIncoming(state, remoteAddress, authorized: false);
            var pending = NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret,
                remoteAddress, now);
            pending.Reciprocal = ValidOffer(request.Reciprocal);
            state.IncomingRequests.Add(pending);
            return (Exchange(state, pending), true);
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

    /// <returns>The approved requester's claimed NodeId.</returns>
    public async Task<string> ApproveAsync(string requestId, CancellationToken ct = default)
    {
        var local = await identity.GetAsync(ct);
        var revoked = new List<string>();
        var nodeId = await store.MutateAsync(state =>
        {
            RequireSharing(state);
            var request = state.IncomingRequests.FirstOrDefault(r => r.RequestId == requestId &&
                r.ExpiresAt > timeProvider.GetUtcNow()) ?? throw BadRequest("This pairing request has expired.");
            if (request.Status == "granted") return request.NodeId;
            if (request.Status != "awaitingApproval") throw BadRequest("This pairing request was rejected.");
            IssueGrant(state, request, local, timeProvider.GetUtcNow(), revoked);
            return request.NodeId;
        }, ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
        return nodeId;
    }

    /// <summary>
    /// Takes (once) the offer to read back a requester this node just granted. The caller connects
    /// back with it; the offer is useless to anyone else because its code is bound to this NodeId.
    /// </summary>
    public async Task<NodeReciprocalOffer?> TakeReciprocalOfferAsync(string nodeId, CancellationToken ct = default)
    {
        return await store.MutateAsync(state =>
        {
            var request = state.IncomingRequests.FirstOrDefault(r => r.NodeId == nodeId && r.Status == "granted" &&
                r.Reciprocal != null);
            var offer = request?.Reciprocal;
            if (request != null) request.Reciprocal = null;
            return offer;
        }, ct);
    }

    /// <summary>A single-use code that lets exactly <paramref name="audienceNodeId"/> read this library.</summary>
    public async Task<string> CreateReciprocalInvitationAsync(string audienceNodeId, CancellationToken ct = default)
    {
        if (!NodeRequestSignature.IsIdentifier(audienceNodeId)) throw BadRequest("The reciprocal device is invalid.");
        await identity.GetAsync(ct);
        var code = NodeRequestSignature.RandomToken();
        var now = timeProvider.GetUtcNow();
        await store.MutateAsync(state =>
        {
            RequireSharing(state);
            state.ReciprocalInvitations.RemoveAll(i => i.ExpiresAt <= now || i.AudienceNodeId == audienceNodeId);
            if (state.ReciprocalInvitations.Count >= MaxPendingRequests)
                throw new FederationAccessException("PairingBusy", 429, "Too many pairing requests are pending. Try again later.");
            state.ReciprocalInvitations.Add(new StoredReciprocalInvitation
            {
                CodeHash = NodeRequestSignature.HashSecret(code),
                AudienceNodeId = audienceNodeId,
                // The other device may approve at the very end of its request's lifetime.
                ExpiresAt = now + PairingLifetime + PairingLifetime
            });
            return true;
        }, ct);
        return code;
    }

    /// <summary>Forgets a device in both directions: no access to it, no access from it, no record.</summary>
    public async Task RemovePeerAsync(string nodeId, CancellationToken ct = default)
    {
        var (outbound, inbound) = await store.MutateAsync(state =>
        {
            var outbound = state.OutboundGrants.GetValueOrDefault(nodeId)?.GrantId;
            state.OutboundGrants.Remove(nodeId);
            var inbound = state.InboundGrants.Values.Where(g => !g.Revoked && g.Credentials.SubjectNodeId == nodeId)
                .Select(g => g.Credentials.GrantId).ToArray();
            foreach (var grantId in inbound) state.InboundGrants[grantId].Revoked = true;
            state.Peers.Remove(nodeId);
            state.IncomingRequests.RemoveAll(r => r.NodeId == nodeId);
            state.OutgoingRequests.RemoveAll(r => r.NodeId == nodeId);
            state.ReciprocalInvitations.RemoveAll(i => i.AudienceNodeId == nodeId);
            return (outbound, inbound);
        }, ct);
        if (outbound != null) leases.Revoke(GrantLeaseRegistry.OutboundKey(outbound));
        foreach (var grantId in inbound) leases.Revoke(grantId);
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

    /// <summary>Stops waiting for an outgoing request; the other device lets its copy expire.</summary>
    public async Task CancelOutgoingAsync(string requestId, CancellationToken ct = default)
    {
        await store.MutateAsync(state => state.OutgoingRequests.RemoveAll(r =>
            r.RequestId == requestId && r.Status == "awaitingApproval"), ct);
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

    private static NodeReciprocalOffer? ValidOffer(NodeReciprocalOffer? offer)
    {
        if (offer == null) return null;
        if (offer.Addresses is not { Length: > 0 and <= 8 } || !NodeRequestSignature.IsIdentifier(offer.Code) ||
            offer.Code.Length < 32)
            throw BadRequest("The reciprocal access offer is invalid.");
        var addresses = new List<string>();
        foreach (var address in offer.Addresses)
        {
            if (address is not { Length: > 0 and <= 256 }) throw BadRequest("The reciprocal access offer is invalid.");
            addresses.Add(Transport.FederationHttpClient.NormalizeAddress(address));
        }
        return new NodeReciprocalOffer(addresses.Distinct(StringComparer.Ordinal).ToArray(), offer.Code);
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
        string? remoteAddress, DateTimeOffset now) => new()
    {
        RequestId = transactionId,
        TransactionId = transactionId,
        NodeId = nodeId,
        NodeName = name.Trim(),
        ClaimSecretHash = NodeRequestSignature.HashSecret(secret),
        RemoteAddress = remoteAddress is { Length: > 0 and <= 64 } ? remoteAddress : null,
        ExpiresAt = now + PairingLifetime
    };

    /// <summary>
    /// Anyone can ask, so the pending list is bounded overall and per address, and decided
    /// requests (kept only for claim re-delivery) never block new ones.
    /// </summary>
    private static void AdmitIncoming(FederationState state, string? remoteAddress, bool authorized)
    {
        while (state.IncomingRequests.Count >= MaxStoredRequests &&
               state.IncomingRequests.Where(r => r.Status != "awaitingApproval").MinBy(r => r.ExpiresAt) is { } decided)
            state.IncomingRequests.Remove(decided);
        if (authorized) return;
        var pending = state.IncomingRequests.Where(r => r.Status == "awaitingApproval").ToArray();
        if (pending.Length >= MaxPendingRequests || state.IncomingRequests.Count >= MaxStoredRequests ||
            remoteAddress != null && pending.Count(r => r.RemoteAddress == remoteAddress) >= MaxPendingRequestsPerAddress)
            throw new FederationAccessException("PairingBusy", 429, "Too many pairing requests are pending. Try again later.");
    }

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
        // Older requests from the same node are superseded. Approving one later would
        // otherwise revoke this grant for a transaction nobody claims.
        foreach (var stale in state.IncomingRequests.Where(r => r != request && r.NodeId == request.NodeId &&
                     r.Status == "awaitingApproval"))
            stale.Status = "rejected";
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

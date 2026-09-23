using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;

namespace Bakabase.Modules.Federation.Peers;

/// <summary>Pairs in one direction. The reciprocal direction is a separate local operator action.</summary>
public sealed class NodePairingClient(FederationStateStore store, INodeIdentityProvider identity,
    FederationHttpClient http, TimeProvider timeProvider, GrantLeaseRegistry leases,
    FederationPeerService? peers = null)
{
    /// <param name="shareBackAddresses">
    /// When set, this node (which must be sharing) offers the target read access back through a
    /// code bound to the target's NodeId, reachable at these addresses; one approval pairs both ways.
    /// </param>
    /// <param name="expectedNodeId">Refuse to send a code to an address that is not this node.</param>
    public async Task<NodePairingOutcome> ConnectAsync(string address, string? code, CancellationToken ct = default,
        IReadOnlyList<string>? shareBackAddresses = null, string? expectedNodeId = null)
    {
        var normalized = FederationHttpClient.NormalizeAddress(address);
        var local = await identity.GetAsync(ct);
        var info = await http.PublicAsync<NodeInfo>(normalized, HttpMethod.Get, "/federation/v1/info", null, ct);
        PeerSessionFactory.ValidateInfo(info, expectedNodeId);
        if (info.NodeId == local.NodeId)
            throw new FederationAccessException("SelfAddress", 400, "This address belongs to this node.");
        NodeReciprocalOffer? offer = null;
        if (shareBackAddresses is { Count: > 0 })
        {
            if (peers == null) throw new InvalidOperationException("Sharing back needs the local peer service.");
            offer = new NodeReciprocalOffer(shareBackAddresses.Take(8).ToArray(),
                await peers.CreateReciprocalInvitationAsync(info.NodeId, ct));
        }

        // Persist the claim secret BEFORE dispatch. If the response is lost or this
        // process exits, the peer can safely re-deliver the same grant to this claimant.
        var created = false;
        var pending = await store.MutateAsync(state =>
        {
            var now = timeProvider.GetUtcNow();
            state.OutgoingRequests.RemoveAll(r => r.ExpiresAt <= now);
            var existing = state.OutgoingRequests.FirstOrDefault(r => r.NodeId == info.NodeId &&
                r.Address == normalized && r.LibraryEpoch == info.LibraryEpoch && r.Status == "awaitingApproval");
            if (existing != null) return existing;
            created = true;
            if (state.OutgoingRequests.Count >= 64)
                throw new FederationAccessException("PairingBusy", 429, "Too many pairing transactions are pending.");
            var request = new StoredOutgoingRequest
            {
                RequestId = NodeRequestSignature.RandomToken(18),
                Address = normalized,
                NodeId = info.NodeId,
                NodeName = info.Name,
                LibraryEpoch = info.LibraryEpoch,
                ClaimSecret = NodeRequestSignature.RandomToken(),
                ExpiresAt = now + FederationPeerService.PairingLifetime
            };
            state.OutgoingRequests.Add(request);
            return request;
        }, ct);

        NodePairExchange exchange;
        try
        {
            exchange = string.IsNullOrWhiteSpace(code)
                ? await http.PublicAsync<NodePairExchange>(normalized, HttpMethod.Post, "/federation/v1/pair/request",
                    new NodePairRequest(local.NodeId, local.Name, pending.RequestId, pending.ClaimSecret, offer), ct)
                : await http.PublicAsync<NodePairExchange>(normalized, HttpMethod.Post, "/federation/v1/pair/code",
                    new NodePairCodeRequest(local.NodeId, local.Name, code, pending.RequestId, pending.ClaimSecret,
                        offer), ct);
        }
        catch (FederationAccessException e) when (e.ErrorCode == "InvalidPairingCode")
        {
            // The source kept nothing for a new transaction; an existing one stays pending on both sides.
            if (created)
                await store.MutateAsync(state => state.OutgoingRequests.RemoveAll(r => r.RequestId == pending.RequestId), ct);
            throw;
        }
        return await SaveExchangeAsync(local, pending, exchange, ct);
    }

    /// <summary>Claims every outgoing request still waiting, so grants land without the UI polling.</summary>
    /// <returns>Whether any request was granted.</returns>
    public async Task<bool> ClaimPendingAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var pending = (await store.ReadAsync(ct)).OutgoingRequests
            .Where(r => r.Status == "awaitingApproval" && r.ExpiresAt > now).Select(r => r.RequestId).ToArray();
        var granted = false;
        foreach (var requestId in pending)
        {
            try { granted |= (await ClaimAsync(requestId, ct)).Outcome == "granted"; }
            catch (FederationAccessException) { /* Offline or rejected; the next round retries until expiry. */ }
        }
        return granted;
    }

    public async Task<NodePairingOutcome> ClaimAsync(string requestId, CancellationToken ct = default)
    {
        var state = await store.ReadAsync(ct);
        var pending = state.OutgoingRequests.FirstOrDefault(r => r.RequestId == requestId &&
            r.ExpiresAt > timeProvider.GetUtcNow()) ?? throw new FederationAccessException("PairingExpired", 410,
            "The local pairing transaction expired. Start a new connection.");
        var local = await identity.GetAsync(ct);
        var info = await http.PublicAsync<NodeInfo>(pending.Address, HttpMethod.Get, "/federation/v1/info", null, ct);
        PeerSessionFactory.ValidateInfo(info, pending.NodeId, pending.LibraryEpoch);
        var exchange = await http.PublicAsync<NodePairExchange>(pending.Address, HttpMethod.Post,
            "/federation/v1/pair/claim", new NodePairClaimRequest(requestId, local.NodeId, pending.ClaimSecret), ct);
        return await SaveExchangeAsync(local, pending, exchange, ct);
    }

    private async Task<NodePairingOutcome> SaveExchangeAsync(NodeIdentity local, StoredOutgoingRequest pending,
        NodePairExchange exchange, CancellationToken ct)
    {
        if (exchange.RequestId != pending.RequestId ||
            exchange.Outcome is not ("awaitingApproval" or "granted" or "rejected"))
            throw new FederationAccessException("InvalidNodeResponse", 502, "The pairing response belongs to a different transaction.");
        if (exchange.Outcome == "granted")
        {
            var credentials = exchange.Credentials;
            if (credentials == null || credentials.SubjectNodeId != local.NodeId ||
                credentials.AudienceNodeId != pending.NodeId || credentials.LibraryEpoch != pending.LibraryEpoch ||
                !NodeRequestSignature.IsIdentifier(credentials.GrantId) || credentials.Revision < 1)
                throw new FederationAccessException("InvalidNodeResponse", 502, "The node returned a grant for another identity or library.");
            try
            {
                if (NodeRequestSignature.Decode(credentials.Key).Length != 32) throw new FormatException();
            }
            catch (FormatException)
            {
                throw new FederationAccessException("InvalidNodeResponse", 502, "The node returned an invalid signing key.");
            }
        }
        var replacedGrantId = await store.MutateAsync(state =>
        {
            if (state.NodeId != local.NodeId)
                throw new FederationAccessException("IdentityConflict", 409, "The local node identity changed during pairing.");
            var request = state.OutgoingRequests.FirstOrDefault(r => r.RequestId == pending.RequestId);
            if (request == null)
                throw new FederationAccessException("PairingExpired", 410, "The pairing transaction was removed before it finished.");
            request.Status = exchange.Outcome;
            // Never extend a local transaction based on an untrusted wall clock.
            string? replacedGrantId = null;
            if (exchange.Outcome == "granted")
            {
                var old = state.OutboundGrants.GetValueOrDefault(pending.NodeId);
                var oldAddress = state.Peers.GetValueOrDefault(pending.NodeId)?.Address;
                if (old != null && (old != exchange.Credentials || oldAddress != pending.Address))
                    replacedGrantId = old.GrantId;
                state.OutboundGrants[pending.NodeId] = exchange.Credentials!;
                if (!state.Peers.TryGetValue(pending.NodeId, out var peer))
                    state.Peers[pending.NodeId] = peer = new StoredPeer { NodeId = pending.NodeId };
                peer.Label = pending.NodeName;
                peer.Address = pending.Address;
                peer.LibraryEpoch = pending.LibraryEpoch;
                peer.Enabled = true;
            }
            return replacedGrantId;
        }, ct);
        if (replacedGrantId != null) leases.Revoke(GrantLeaseRegistry.OutboundKey(replacedGrantId));
        if (exchange.Outcome == "granted")
            leases.Resume(GrantLeaseRegistry.OutboundKey(exchange.Credentials!.GrantId));
        return new NodePairingOutcome(exchange.Outcome, exchange.RequestId, pending.NodeId);
    }
}

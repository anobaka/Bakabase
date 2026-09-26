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
        ValidateExchange(local, pending, exchange);
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

    // ---- Definitions access (datasync.read, §7.2.2) --------------------------------------------------------------

    /// <summary>
    /// Asks the device at <paramref name="address"/> for <c>datasync.read</c>, by request or by a datasync code, on the
    /// datasync pairing routes (never the library's). Refuses before sending anything when the peer's <c>/info</c>
    /// says it cannot take part: <c>PeerTooOld</c>, <c>ThisTooOld</c> or <c>PeerSharingOff</c>. This device's own
    /// refusals on the way carry codes no peer answers: <c>LocalPairingBusy</c> (too many of its own requests or
    /// codes waiting) and <c>LocalDataSyncSharingDisabled</c> (two-way, with its own sharing off).
    /// </summary>
    /// <param name="intent"><see cref="NodeDataSyncIntents.Follow"/> or <see cref="NodeDataSyncIntents.TwoWay"/>.</param>
    /// <param name="contract">This build's data sync contract, which the peer's must meet and accept.</param>
    /// <param name="reciprocalAddresses">
    /// Two-way only: where the peer can reach this device, offered with a single-use datasync code bound to the
    /// peer's NodeId, so its approval can read this device back. This device must be sharing its definitions.
    /// </param>
    /// <param name="expectedNodeId">Refuse to send a code to an address that is not this node.</param>
    public async Task<NodeDataSyncPairingOutcome> ConnectDataSyncAsync(string address, string? code, string intent,
        NodeDataSyncContract contract, IReadOnlyList<string>? reciprocalAddresses = null,
        string? expectedNodeId = null, CancellationToken ct = default)
    {
        if (!NodeDataSyncIntents.IsValid(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown data sync intent.");
        var normalized = FederationHttpClient.NormalizeAddress(address);
        var local = await identity.GetAsync(ct);
        var info = await http.PublicAsync<NodeInfo>(normalized, HttpMethod.Get, "/federation/v1/info", null, ct);
        PeerSessionFactory.ValidateInfo(info, expectedNodeId);
        if (info.NodeId == local.NodeId)
            throw new FederationAccessException("SelfAddress", 400, "This address belongs to this node.");
        RequireDataSyncCapability(info, contract);
        NodeReciprocalOffer? offer = null;
        if (reciprocalAddresses is { Count: > 0 })
        {
            if (peers == null) throw new InvalidOperationException("Reading back needs the local peer service.");
            string reciprocalCode;
            try
            {
                reciprocalCode = await peers.CreateDataSyncReciprocalInvitationAsync(info.NodeId, ct);
            }
            catch (FederationAccessException e) when (e.ErrorCode is "DataSyncSharingDisabled" or "PairingBusy")
            {
                // This device's own refusal, under a code no peer answers, so it is never read as the peer's.
                throw new FederationAccessException("Local" + e.ErrorCode, e.StatusCode, e.ErrorCode == "PairingBusy"
                    ? "This device has too many two-way requests waiting. Cancel one or try again later."
                    : "Definitions sharing is off on this device, so no other device could read it back.");
            }
            offer = new NodeReciprocalOffer(reciprocalAddresses.Take(8).ToArray(), reciprocalCode);
        }

        // Persist the claim secret BEFORE dispatch, as for library access.
        var created = false;
        var pending = await store.MutateAsync(state =>
        {
            var now = timeProvider.GetUtcNow();
            state.OutgoingDataSyncRequests.RemoveAll(r => r.ExpiresAt <= now);
            var existing = state.OutgoingDataSyncRequests.FirstOrDefault(r => r.NodeId == info.NodeId &&
                r.Address == normalized && r.LibraryEpoch == info.LibraryEpoch && r.Status == "awaitingApproval" &&
                r.Intent == intent);
            if (existing != null) return existing;
            created = true;
            // This device's own bound, under a code no peer answers, so it is never read as the peer being busy.
            if (state.OutgoingDataSyncRequests.Count >= 64)
                throw new FederationAccessException("LocalPairingBusy", 429,
                    "This device has too many definitions requests waiting. Cancel one or try again later.");
            var request = new StoredOutgoingRequest
            {
                RequestId = NodeRequestSignature.RandomToken(18),
                Address = normalized,
                NodeId = info.NodeId,
                NodeName = info.Name,
                LibraryEpoch = info.LibraryEpoch,
                ClaimSecret = NodeRequestSignature.RandomToken(),
                ExpiresAt = now + FederationPeerService.PairingLifetime,
                Intent = intent
            };
            state.OutgoingDataSyncRequests.Add(request);
            return request;
        }, ct);

        NodePairExchange exchange;
        try
        {
            exchange = string.IsNullOrWhiteSpace(code)
                ? await http.PublicAsync<NodePairExchange>(normalized, HttpMethod.Post,
                    "/federation/v1/pair/datasync/request",
                    new NodeDataSyncPairRequest(local.NodeId, local.Name, pending.RequestId, pending.ClaimSecret,
                        intent, offer), ct)
                : await http.PublicAsync<NodePairExchange>(normalized, HttpMethod.Post,
                    "/federation/v1/pair/datasync/code",
                    new NodeDataSyncPairCodeRequest(local.NodeId, local.Name, code, pending.RequestId,
                        pending.ClaimSecret, intent, offer), ct);
        }
        catch (FederationAccessException e) when (e.ErrorCode == "InvalidPairingCode")
        {
            if (created)
                await store.MutateAsync(state =>
                    state.OutgoingDataSyncRequests.RemoveAll(r => r.RequestId == pending.RequestId), ct);
            throw;
        }
        return WithDataSyncReadBack(await SaveDataSyncExchangeAsync(local, pending, exchange, ct), exchange);
    }

    /// <summary>
    /// Claims every datasync request still waiting, next to <see cref="ClaimPendingAsync"/>, so grants land without
    /// the UI polling.
    /// </summary>
    /// <returns>The node ids whose grant this call obtained.</returns>
    public async Task<IReadOnlyList<string>> ClaimPendingDataSyncAsync(CancellationToken ct = default) =>
        (await ClaimPendingDataSyncOutcomesAsync(ct)).Select(o => o.PeerNodeId).ToArray();

    /// <summary>
    /// <see cref="ClaimPendingDataSyncAsync"/> with each grant's outcome as the exchange gave it, including whether the
    /// device reads this one back (<see cref="NodeDataSyncPairingOutcome.ReadBack"/>: a two-way request approved
    /// without reading back says <see cref="NodeDataSyncReadBack.Declined"/>).
    /// </summary>
    /// <returns>The outcomes of the grants this call obtained.</returns>
    public async Task<IReadOnlyList<NodeDataSyncPairingOutcome>> ClaimPendingDataSyncOutcomesAsync(
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var pending = (await store.ReadAsync(ct)).OutgoingDataSyncRequests
            .Where(r => r.Status == "awaitingApproval" && r.ExpiresAt > now).Select(r => r.RequestId).ToArray();
        var granted = new List<NodeDataSyncPairingOutcome>();
        foreach (var requestId in pending)
        {
            try
            {
                var outcome = await ClaimDataSyncAsync(requestId, ct);
                if (outcome.Outcome == "granted") granted.Add(outcome);
            }
            catch (FederationAccessException) { /* Offline or rejected; the next round retries until expiry. */ }
            // A device too slow to answer (the client's own deadline) is only offline this round: the rest are claimed.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        return granted;
    }

    public async Task<NodeDataSyncPairingOutcome> ClaimDataSyncAsync(string requestId, CancellationToken ct = default)
    {
        var state = await store.ReadAsync(ct);
        var pending = state.OutgoingDataSyncRequests.FirstOrDefault(r => r.RequestId == requestId &&
            r.ExpiresAt > timeProvider.GetUtcNow()) ?? throw new FederationAccessException("PairingExpired", 410,
            "The local pairing transaction expired. Start a new connection.");
        var local = await identity.GetAsync(ct);
        var info = await http.PublicAsync<NodeInfo>(pending.Address, HttpMethod.Get, "/federation/v1/info", null, ct);
        PeerSessionFactory.ValidateInfo(info, pending.NodeId, pending.LibraryEpoch);
        var exchange = await http.PublicAsync<NodePairExchange>(pending.Address, HttpMethod.Post,
            "/federation/v1/pair/datasync/claim", new NodePairClaimRequest(requestId, local.NodeId, pending.ClaimSecret),
            ct);
        return WithDataSyncReadBack(await SaveDataSyncExchangeAsync(local, pending, exchange, ct), exchange);
    }

    /// <summary>What the exchange said about reading this device back, in a word this build knows.</summary>
    private static NodeDataSyncPairingOutcome WithDataSyncReadBack(NodeDataSyncPairingOutcome outcome,
        NodePairExchange exchange) =>
        outcome with
        {
            ReadBack = exchange.ReadBack is NodeDataSyncReadBack.Started or NodeDataSyncReadBack.Declined
                ? exchange.ReadBack
                : null
        };

    /// <summary>What a peer's <c>/info</c> must say before this device asks it for definitions (§7.2.2).</summary>
    public static void RequireDataSyncCapability(NodeInfo info, NodeDataSyncContract contract)
    {
        if (info.DataSyncContractVersion is not { } version || version < contract.MinimumPeerVersion)
            throw new FederationAccessException("PeerTooOld", 409,
                "The other device does not support data sync, or its version is too old. Update it first.");
        if (info.DataSyncMinimumPeerContract is { } minimum && contract.Version < minimum)
            throw new FederationAccessException("ThisTooOld", 409,
                "The other device needs a newer version of data sync. Update this device first.");
        if (info.SharesDefinitions == false)
            throw new FederationAccessException("PeerSharingOff", 403,
                "The other device does not let devices read its definitions.");
    }

    /// <summary>
    /// <see cref="SaveExchangeAsync"/> for definitions access: the same checks, then <c>OutboundDataSyncGrants</c>
    /// and the peer's data sync address. It follows the routing rule of §7.1.1: a peer with library access in either
    /// direction keeps its label, address, library epoch, kind, platform and browsing switch; one without gets empty
    /// ones filled so the device map can show it. Nothing here ever changes <c>Enabled</c>.
    /// </summary>
    private async Task<NodeDataSyncPairingOutcome> SaveDataSyncExchangeAsync(NodeIdentity local,
        StoredOutgoingRequest pending, NodePairExchange exchange, CancellationToken ct)
    {
        ValidateExchange(local, pending, exchange);
        var replacedGrantId = await store.MutateAsync(state =>
        {
            if (state.NodeId != local.NodeId)
                throw new FederationAccessException("IdentityConflict", 409, "The local node identity changed during pairing.");
            var request = state.OutgoingDataSyncRequests.FirstOrDefault(r => r.RequestId == pending.RequestId);
            if (request == null)
                throw new FederationAccessException("PairingExpired", 410, "The pairing transaction was removed before it finished.");
            request.Status = exchange.Outcome;
            string? replacedGrantId = null;
            if (exchange.Outcome == "granted")
            {
                var old = state.OutboundDataSyncGrants.GetValueOrDefault(pending.NodeId);
                var peer = state.Peers.GetValueOrDefault(pending.NodeId);
                if (old != null && (old != exchange.Credentials ||
                                    (peer?.DataSyncAddress ?? peer?.Address) != pending.Address))
                    replacedGrantId = old.GrantId;
                state.OutboundDataSyncGrants[pending.NodeId] = exchange.Credentials!;
                if (peer == null) state.Peers[pending.NodeId] = peer = new StoredPeer { NodeId = pending.NodeId };
                peer.DataSyncAddress = pending.Address;
                // A verified address replaces what the peer merely offered.
                peer.DataSyncOfferedAddresses = null;
                if (!state.HasLibraryGrant(pending.NodeId))
                {
                    if (peer.Label.Length == 0) peer.Label = pending.NodeName;
                    peer.Address ??= pending.Address;
                    peer.LibraryEpoch ??= pending.LibraryEpoch;
                }
            }
            return replacedGrantId;
        }, ct);
        if (replacedGrantId != null) leases.Revoke(GrantLeaseRegistry.OutboundKey(replacedGrantId));
        if (exchange.Outcome == "granted")
            leases.Resume(GrantLeaseRegistry.OutboundKey(exchange.Credentials!.GrantId));
        return new NodeDataSyncPairingOutcome(exchange.Outcome, exchange.RequestId, pending.NodeId, pending.NodeName);
    }

    /// <summary>What a peer's pairing answer must be before anything is stored, for either kind of access.</summary>
    private static void ValidateExchange(NodeIdentity local, StoredOutgoingRequest pending, NodePairExchange exchange)
    {
        if (exchange.RequestId != pending.RequestId ||
            exchange.Outcome is not ("awaitingApproval" or "granted" or "rejected"))
            throw new FederationAccessException("InvalidNodeResponse", 502, "The pairing response belongs to a different transaction.");
        if (exchange.Outcome != "granted") return;
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
}

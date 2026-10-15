using System.Globalization;
using System.Security.Cryptography;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Peers;

/// <summary>Owns explicit definitions-sharing decisions and the device's identity lifecycle.</summary>
public sealed class FederationPeerService(FederationStateStore store, INodeIdentityProvider identity,
    GrantLeaseRegistry leases, TimeProvider timeProvider)
{
    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(10);
    private const int MaxPendingRequests = 64;
    private const int MaxPendingRequestsPerAddress = 4;
    private const int MaxStoredRequests = 256;

    /// <summary>Call from supported database replacement/restore operations before exposing the new library.</summary>
    /// <remarks>
    /// Ends definitions access too: every datasync grant is revoked, definitions sharing is turned off, and the
    /// incoming datasync requests, the datasync code and the reciprocal datasync codes are dropped.
    /// </remarks>
    public async Task<NodeIdentity> RotateLibraryEpochAsync(CancellationToken ct = default)
    {
        var local = await identity.GetAsync(ct);
        var result = await store.MutateAsync(state =>
        {
            state.LibraryEpoch = Guid.NewGuid().ToString("N");
            state.DataSyncSharingEnabled = false;
            foreach (var grant in state.InboundDataSyncGrants.Values) grant.Revoked = true;
            state.IncomingDataSyncRequests.Clear();
            state.DataSyncInvitation = null;
            state.DataSyncReciprocalInvitations.Clear();
            return new NodeIdentity(local.NodeId, state.LibraryEpoch, local.Name);
        }, ct);
        leases.CancelInbound();
        return result;
    }

    /// <summary>Explicit clone operation, never called by startup or ordinary options import.</summary>
    /// <param name="inheritHostIdentity">
    /// The new node takes the host's identity, which the caller has just replaced: a copied
    /// installation. Otherwise it gets an id of its own.
    /// </param>
    /// <param name="ct">Cancels the reset.</param>
    public async Task<NodeIdentity> ResetAsNewNodeAsync(bool inheritHostIdentity = false, CancellationToken ct = default)
    {
        var result = await store.ResetAsNewNodeAsync(inheritHostIdentity, ct);
        leases.CancelAll();
        return result;
    }

    /// <summary>The name this device knows a peer by, if it knows it.</summary>
    public async Task<string?> GetPeerNameAsync(string nodeId, CancellationToken ct = default) =>
        (await store.ReadAsync(ct)).Peers.GetValueOrDefault(nodeId)?.Label is { Length: > 0 } label ? label : null;

    // ---- Definitions access (datasync.read, §7.1-§7.2) ------------------------------------------------------------

    /// <summary>
    /// Everything about definitions access this device's own management plane shows: the peers, the requests
    /// both ways and who may read this device's definitions. Never a key, a code or a claim secret.
    /// </summary>
    public async Task<FederationDataSyncStatus> GetDataSyncStatusAsync(CancellationToken ct = default)
    {
        var local = await identity.GetAsync(ct);
        var state = await store.ReadAsync(ct);
        var now = timeProvider.GetUtcNow();
        // The address book may retain addresses used before definitions sync. Only definitions evidence
        // makes a peer a sync candidate; keep the saved fallback addresses for existing link recovery.
        var definitionsPeers = state.OutboundDataSyncGrants.Keys
            .Concat(state.InboundDataSyncGrants.Values.Select(g => g.Credentials.SubjectNodeId))
            .Concat(state.IncomingDataSyncRequests.Select(r => r.NodeId))
            .Concat(state.OutgoingDataSyncRequests.Select(r => r.NodeId))
            .Concat(state.DataSyncReciprocalInvitations.Select(i => i.AudienceNodeId))
            .ToHashSet(StringComparer.Ordinal);
        var peers = state.Peers.Values
            .Where(peer => peer.DataSyncAddress != null || peer.DataSyncOfferedAddresses is { Length: > 0 } ||
                           definitionsPeers.Contains(peer.NodeId))
            .Select(peer => new NodeDataSyncPeerView(peer.NodeId, peer.Label,
                peer.DataSyncAddress ?? peer.Address, state.OutboundDataSyncGrants.ContainsKey(peer.NodeId),
                LiveGrant(state.InboundDataSyncGrants, peer.NodeId, local.LibraryEpoch) != null))
            .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        var requests = state.IncomingDataSyncRequests.Where(r => r.ExpiresAt > now).Select(r =>
            {
                var known = state.Peers.GetValueOrDefault(r.NodeId);
                return new NodeDataSyncRequestView(r.RequestId, "incoming", r.NodeId, r.NodeName,
                    r.Intent ?? NodeDataSyncIntents.Follow, r.Status, r.ExpiresAt, r.RemoteAddress,
                    known?.DataSyncAddress ?? known?.Address,
                    r.Status == "awaitingApproval" &&
                    LiveGrant(state.InboundDataSyncGrants, r.NodeId, local.LibraryEpoch) != null);
            })
            .Concat(state.OutgoingDataSyncRequests.Where(r => r.ExpiresAt > now).Select(r =>
                new NodeDataSyncRequestView(r.RequestId, "outgoing", r.NodeId, r.NodeName,
                    r.Intent ?? NodeDataSyncIntents.Follow, r.Status, r.ExpiresAt, r.Address, null, false)))
            .ToArray();
        var grants = state.InboundDataSyncGrants.Values
            .Where(g => !g.Revoked && g.Credentials.LibraryEpoch == local.LibraryEpoch)
            .Select(g => new NodeDataSyncGrantView(g.Credentials.SubjectNodeId,
                state.Peers.GetValueOrDefault(g.Credentials.SubjectNodeId)?.Label is { Length: > 0 } label
                    ? label
                    : g.Credentials.SubjectNodeId,
                g.Credentials.GrantId, g.CreatedAt))
            .OrderBy(g => g.Name, StringComparer.Ordinal).ToArray();
        return new FederationDataSyncStatus(state.DataSyncSharingEnabled, peers, requests, grants);
    }

    /// <summary>
    /// The definitions sharing switch. Off cancels every reader's lease; on resumes them.
    /// It never changes remote access: the host couples that (§7.1.3).
    /// </summary>
    public async Task SetDataSyncSharingAsync(bool enabled, CancellationToken ct = default)
    {
        await identity.GetAsync(ct);
        var grants = await store.MutateAsync(state =>
        {
            state.DataSyncSharingEnabled = enabled;
            return LiveGrantIds(state.InboundDataSyncGrants);
        }, ct);
        SetLeases(grants, enabled);
    }

    /// <summary>
    /// An 8-digit, one-time, 10-minute code redeemed only on <c>pair/datasync/code</c> (§7.2.3). It grants one way:
    /// whoever redeems it reads this device, and is never read back.
    /// </summary>
    public async Task<NodeInvitation> IssueDataSyncInvitationAsync(CancellationToken ct = default)
    {
        await identity.GetAsync(ct);
        var code = RandomNumberGenerator.GetInt32(100_000_000).ToString("D8", CultureInfo.InvariantCulture);
        var expiresAt = timeProvider.GetUtcNow() + PairingLifetime;
        await store.MutateAsync(state =>
        {
            RequireDataSyncSharing(state);
            state.DataSyncInvitation = new StoredInvitation
                { CodeHash = NodeRequestSignature.HashSecret(code), ExpiresAt = expiresAt };
            return true;
        }, ct);
        return new NodeInvitation(code, expiresAt);
    }

    /// <summary><c>pair/datasync/request</c>: files a request for a person here to approve.</summary>
    /// <returns>The exchange, and whether this call created the request (a retry does not).</returns>
    public async Task<(NodePairExchange Exchange, bool Created)> SubmitDataSyncRequestAsync(
        NodeDataSyncPairRequest request, string? remoteAddress = null, CancellationToken ct = default)
    {
        ValidatePairRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret);
        var intent = ValidIntent(request.Intent);
        // Only a two-way request is read back; an offer is still validated, so a malformed one is refused.
        var offer = ValidOffer(request.Reciprocal);
        if (intent != NodeDataSyncIntents.TwoWay) offer = null;
        var local = await identity.GetAsync(ct);
        if (request.NodeId == local.NodeId) throw BadRequest("A node cannot pair with itself.");
        var now = timeProvider.GetUtcNow();
        return await store.MutateAsync(state =>
        {
            RequireDataSyncSharing(state);
            PruneDataSync(state, now);
            var existing = FindTransaction(state.IncomingDataSyncRequests, request.TransactionId, request.NodeId,
                request.ClaimSecret);
            if (existing != null)
            {
                if (existing.Status == "awaitingApproval" && offer != null) existing.Reciprocal = offer;
                return (Exchange(state.InboundDataSyncGrants, existing), false);
            }
            AdmitIncoming(state.IncomingDataSyncRequests, remoteAddress, authorized: false);
            var pending = NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret,
                remoteAddress, now);
            pending.Intent = intent;
            pending.Reciprocal = offer;
            state.IncomingDataSyncRequests.Add(pending);
            return (Exchange(state.InboundDataSyncGrants, pending), true);
        }, ct);
    }

    /// <summary>
    /// <c>pair/datasync/code</c>: a definitions code grants at once, one way: a two-way redemption
    /// of an ordinary code is never read back, and the exchange says <see cref="NodeDataSyncReadBack.Declined"/> (two-way
    /// consent is given only when approving a request). A reciprocal code (this device's own read-back of a device that
    /// approved it) grants and is never read back again.
    /// </summary>
    /// <returns>The exchange, and whether this call issued the grant (a re-delivery does not).</returns>
    public async Task<(NodePairExchange Exchange, bool Issued)> ExchangeDataSyncCodeAsync(
        NodeDataSyncPairCodeRequest request, string? remoteAddress = null, CancellationToken ct = default)
    {
        ValidatePairRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret);
        var intent = ValidIntent(request.Intent);
        var local = await identity.GetAsync(ct);
        if (request.NodeId == local.NodeId) throw BadRequest("A node cannot pair with itself.");
        var now = timeProvider.GetUtcNow();
        var revoked = new List<string>();
        var result = await store.MutateAsync<(NodePairExchange, bool)?>(state =>
        {
            RequireDataSyncSharing(state);
            PruneDataSync(state, now);
            var existing = FindTransaction(state.IncomingDataSyncRequests, request.TransactionId, request.NodeId,
                request.ClaimSecret);
            if (existing != null && existing.Status != "awaitingApproval")
                return (Exchange(state.InboundDataSyncGrants, existing), false);
            var codeHash = NodeRequestSignature.HashSecret(request.Code?.Trim());
            var reciprocal = state.DataSyncReciprocalInvitations.FirstOrDefault(i => i.ExpiresAt > now &&
                i.AudienceNodeId == request.NodeId && NodeRequestSignature.FixedEquals(i.CodeHash, codeHash));
            if (reciprocal != null) state.DataSyncReciprocalInvitations.Remove(reciprocal);
            else if (state.DataSyncInvitation is not { } invitation || invitation.ExpiresAt <= now ||
                     invitation.FailedAttempts >= 5)
                return null;
            else if (!NodeRequestSignature.FixedEquals(invitation.CodeHash, codeHash))
            {
                invitation.FailedAttempts++;
                return null;
            }
            else state.DataSyncInvitation = null;
            var pending = existing;
            if (pending == null)
            {
                AdmitIncoming(state.IncomingDataSyncRequests, remoteAddress, authorized: true);
                pending = NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret,
                    remoteAddress, now);
                state.IncomingDataSyncRequests.Add(pending);
            }
            pending.Intent = intent;
            pending.Reciprocal = null;
            pending.ReadBack = reciprocal == null && intent == NodeDataSyncIntents.TwoWay
                ? NodeDataSyncReadBack.Declined
                : null;
            IssueDataSyncGrant(state, pending, local, now, revoked);
            return (Exchange(state.InboundDataSyncGrants, pending), true);
        }, ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
        return result ?? throw new FederationAccessException("InvalidPairingCode", 403,
            "The code is wrong or has expired. Create a new code on the device that shares its definitions.");
    }

    /// <summary><c>pair/datasync/claim</c>: re-delivers a decided datasync request to its claimant.</summary>
    public async Task<NodePairExchange> ClaimDataSyncAsync(NodePairClaimRequest request, CancellationToken ct = default)
    {
        var state = await store.ReadAsync(ct);
        RequireDataSyncSharing(state);
        return Claim(state.IncomingDataSyncRequests, state.InboundDataSyncGrants, request);
    }

    /// <summary>Issues the requester a <c>datasync.read</c> grant.</summary>
    public Task<NodeDataSyncApproval> ApproveDataSyncAsync(string requestId, CancellationToken ct = default) =>
        ApproveDataSyncAsync(requestId, null, ct);

    /// <summary>
    /// <see cref="ApproveDataSyncAsync(string, CancellationToken)"/>, saying in the same write whether this device
    /// reads a two-way requester back: the claim then carries it to the requester
    /// (<see cref="NodePairExchange.ReadBack"/>), whose link shows that it is not read back when approved without
    /// (§7.2.4 step 7). Null says nothing.
    /// </summary>
    public async Task<NodeDataSyncApproval> ApproveDataSyncAsync(string requestId, bool? readBack,
        CancellationToken ct = default)
    {
        var local = await identity.GetAsync(ct);
        var revoked = new List<string>();
        var approval = await store.MutateAsync(state =>
        {
            RequireDataSyncSharing(state);
            var now = timeProvider.GetUtcNow();
            var request = state.IncomingDataSyncRequests.FirstOrDefault(r => r.RequestId == requestId &&
                r.ExpiresAt > now) ?? throw RequestNotFound("This request has expired or does not exist.");
            if (request.Status == "rejected") throw RequestNotFound("This request was rejected.");
            if (request.Status == "awaitingApproval") IssueDataSyncGrant(state, request, local, now, revoked);
            var intent = request.Intent ?? NodeDataSyncIntents.Follow;
            if (intent == NodeDataSyncIntents.TwoWay && readBack is { } receiveBack)
                request.ReadBack = receiveBack ? NodeDataSyncReadBack.Started : NodeDataSyncReadBack.Declined;
            return new NodeDataSyncApproval(request.NodeId, request.NodeName, intent,
                intent == NodeDataSyncIntents.TwoWay && request.Reciprocal != null);
        }, ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
        return approval;
    }

    /// <returns>Whether the request exists.</returns>
    public async Task<bool> RejectDataSyncAsync(string requestId, CancellationToken ct = default) =>
        await store.MutateAsync(state => Reject(state.IncomingDataSyncRequests, requestId), ct);

    /// <summary>
    /// Withdraws a waiting outgoing request. A two-way request's offer to be read back goes with it: the device's code
    /// for this one is dropped, unless another two-way request to it still stands (§7.2.4).
    /// </summary>
    /// <returns>Whether a waiting outgoing request was cancelled.</returns>
    public async Task<bool> CancelOutgoingDataSyncAsync(string requestId, CancellationToken ct = default) =>
        await store.MutateAsync(state =>
        {
            var request = state.OutgoingDataSyncRequests.FirstOrDefault(r =>
                r.RequestId == requestId && r.Status == "awaitingApproval");
            if (request == null) return false;
            state.OutgoingDataSyncRequests.Remove(request);
            var now = timeProvider.GetUtcNow();
            if (!state.OutgoingDataSyncRequests.Any(r => r.NodeId == request.NodeId && r.ExpiresAt > now &&
                    r.Intent == NodeDataSyncIntents.TwoWay && r.Status is "awaitingApproval" or "granted"))
                WithdrawDataSyncReadBack(state, request.NodeId);
            return true;
        }, ct);

    /// <summary>A single-use code that lets exactly <paramref name="audienceNodeId"/> read this device's definitions.</summary>
    public async Task<string> CreateDataSyncReciprocalInvitationAsync(string audienceNodeId,
        CancellationToken ct = default)
    {
        if (!NodeRequestSignature.IsIdentifier(audienceNodeId)) throw BadRequest("The reciprocal device is invalid.");
        await identity.GetAsync(ct);
        var code = NodeRequestSignature.RandomToken();
        var now = timeProvider.GetUtcNow();
        await store.MutateAsync(state =>
        {
            RequireDataSyncSharing(state);
            AddReciprocal(state.DataSyncReciprocalInvitations, audienceNodeId, code, now);
            return true;
        }, ct);
        return code;
    }

    /// <summary>
    /// Takes (once) a granted two-way requester's offer to be read back. The code goes with it; where the requester
    /// said it could be read stays with the peer (<c>DataSyncOfferedAddresses</c>), so asking it again after a failed
    /// read-back has somewhere to go (<see cref="GetDataSyncAddressesAsync"/>).
    /// </summary>
    public async Task<NodeReciprocalOffer?> TakeDataSyncReciprocalOfferAsync(string nodeId,
        CancellationToken ct = default) =>
        await store.MutateAsync(state =>
        {
            var offer = TakeOffer(state.IncomingDataSyncRequests, nodeId);
            if (offer != null && state.Peers.TryGetValue(nodeId, out var peer))
                peer.DataSyncOfferedAddresses = offer.Addresses;
            return offer;
        }, ct);

    /// <summary>
    /// Where to ask a device for its definitions when no address is given: where data sync reaches it
    /// (<c>DataSyncAddress ?? Address</c>), else, only when this device knows no address for it at all, every address
    /// it offered with its last two-way request, in its order. Those are unverified: the caller tries each with the
    /// device's NodeId expected.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetDataSyncAddressesAsync(string nodeId, CancellationToken ct = default)
    {
        var peer = (await store.ReadAsync(ct)).Peers.GetValueOrDefault(nodeId);
        return (peer?.DataSyncAddress ?? peer?.Address) is { } known ? [known] : peer?.DataSyncOfferedAddresses ?? [];
    }

    /// <summary>Whether this device holds <c>datasync.read</c> credentials for the device: whether it may read it.</summary>
    public async Task<bool> HasOutboundDataSyncGrantAsync(string nodeId, CancellationToken ct = default) =>
        (await store.ReadAsync(ct)).OutboundDataSyncGrants.ContainsKey(nodeId);

    /// <summary>
    /// Stops a device reading this device's definitions: its live datasync grants and their leases, and any code this
    /// device offered it to read it back with, which a request the device still holds would otherwise redeem.
    /// </summary>
    public async Task RevokeDataSyncAsync(string nodeId, CancellationToken ct = default)
    {
        var revoked = await store.MutateAsync(state =>
        {
            WithdrawDataSyncReadBack(state, nodeId);
            return RevokeSubject(state.InboundDataSyncGrants, nodeId);
        }, ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
    }

    /// <summary>
    /// "Done — stop reading X": drops this device's own datasync credentials for the peer and its datasync requests
    /// to it, with the offer to be read back that a two-way request made.
    /// </summary>
    public async Task ForgetOutboundDataSyncAsync(string nodeId, CancellationToken ct = default)
    {
        var grantId = await store.MutateAsync(state =>
        {
            var grantId = state.OutboundDataSyncGrants.GetValueOrDefault(nodeId)?.GrantId;
            state.OutboundDataSyncGrants.Remove(nodeId);
            state.OutgoingDataSyncRequests.RemoveAll(r => r.NodeId == nodeId);
            WithdrawDataSyncReadBack(state, nodeId);
            return grantId;
        }, ct);
        if (grantId != null) leases.Revoke(GrantLeaseRegistry.OutboundKey(grantId));
    }

    /// <summary>
    /// Consent withdrawn: the codes this device made for <paramref name="nodeId"/> to read it back with (§7.2.4). The
    /// device may still hold the request that carried one; approving it later then reads nothing back.
    /// </summary>
    private static void WithdrawDataSyncReadBack(FederationState state, string nodeId) =>
        state.DataSyncReciprocalInvitations.RemoveAll(i => i.AudienceNodeId == nodeId);

    // ---- Shared helpers ------------------------------------------------------------------------------------------

    private static void ValidatePairRequest(string nodeId, string name, string transactionId, string secret)
    {
        if (!NodeRequestSignature.IsIdentifier(nodeId) || !NodeRequestSignature.IsIdentifier(transactionId) ||
            !NodeRequestSignature.IsIdentifier(secret) || secret.Length < 32 || string.IsNullOrWhiteSpace(name) ||
            name.Length > 128 || name.Any(char.IsControl))
            throw BadRequest("The pairing identity or claim secret is invalid.");
    }

    private static string ValidIntent(string? intent) => NodeDataSyncIntents.IsValid(intent)
        ? intent!
        : throw BadRequest("A definitions request asks either to follow or to keep in step both ways.");

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

    private static StoredPairRequest? FindTransaction(List<StoredPairRequest> requests, string transactionId,
        string nodeId, string claimSecret)
    {
        var request = requests.FirstOrDefault(r => r.TransactionId == transactionId);
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
    private static void AdmitIncoming(List<StoredPairRequest> requests, string? remoteAddress, bool authorized)
    {
        while (requests.Count >= MaxStoredRequests &&
               requests.Where(r => r.Status != "awaitingApproval").MinBy(r => r.ExpiresAt) is { } decided)
            requests.Remove(decided);
        if (authorized) return;
        var pending = requests.Where(r => r.Status == "awaitingApproval").ToArray();
        if (pending.Length >= MaxPendingRequests || requests.Count >= MaxStoredRequests ||
            remoteAddress != null && pending.Count(r => r.RemoteAddress == remoteAddress) >= MaxPendingRequestsPerAddress)
            throw new FederationAccessException("PairingBusy", 429, "Too many pairing requests are pending. Try again later.");
    }

    private static NodePairExchange Exchange(Dictionary<string, StoredGrant> grants, StoredPairRequest pending)
    {
        NodeCredentials? credentials = null;
        if (pending.Status == "granted" && pending.GrantId != null &&
            grants.TryGetValue(pending.GrantId, out var grant) && !grant.Revoked)
            credentials = grant.Credentials;
        var status = pending.Status == "granted" && credentials == null ? "rejected" : pending.Status;
        return new NodePairExchange(status, pending.RequestId, pending.ExpiresAt, credentials, pending.ReadBack);
    }

    private NodePairExchange Claim(List<StoredPairRequest> requests, Dictionary<string, StoredGrant> grants,
        NodePairClaimRequest request)
    {
        var pending = requests.FirstOrDefault(r => r.RequestId == request.RequestId &&
            r.NodeId == request.NodeId && r.ExpiresAt > timeProvider.GetUtcNow());
        if (pending == null || !NodeRequestSignature.FixedEquals(pending.ClaimSecretHash,
                NodeRequestSignature.HashSecret(request.ClaimSecret)))
            throw new FederationAccessException("PairingRejected", 403, "The pairing request has expired or cannot be claimed.");
        // Re-delivery to the same secret is bounded by expiry and makes crash recovery safe.
        return Exchange(grants, pending);
    }

    private static bool Reject(List<StoredPairRequest> requests, string requestId)
    {
        var request = requests.FirstOrDefault(r => r.RequestId == requestId);
        if (request is { Status: "awaitingApproval" }) request.Status = "rejected";
        return request != null;
    }

    private static NodeReciprocalOffer? TakeOffer(List<StoredPairRequest> requests, string nodeId)
    {
        var request = requests.FirstOrDefault(r => r.NodeId == nodeId && r.Status == "granted" &&
            r.Reciprocal != null);
        var offer = request?.Reciprocal;
        if (request != null) request.Reciprocal = null;
        return offer;
    }

    private static void AddReciprocal(List<StoredReciprocalInvitation> invitations, string audienceNodeId, string code,
        DateTimeOffset now)
    {
        invitations.RemoveAll(i => i.ExpiresAt <= now || i.AudienceNodeId == audienceNodeId);
        if (invitations.Count >= MaxPendingRequests)
            throw new FederationAccessException("PairingBusy", 429, "Too many pairing requests are pending. Try again later.");
        invitations.Add(new StoredReciprocalInvitation
        {
            CodeHash = NodeRequestSignature.HashSecret(code),
            AudienceNodeId = audienceNodeId,
            // The other device may approve at the very end of its request's lifetime.
            ExpiresAt = now + PairingLifetime + PairingLifetime
        });
    }

    private static void IssueDataSyncGrant(FederationState state, StoredPairRequest request, NodeIdentity local,
        DateTimeOffset now, List<string> revoked)
    {
        revoked.AddRange(RevokeSubject(state.InboundDataSyncGrants, request.NodeId));
        var credentials = new NodeCredentials(NodeRequestSignature.RandomToken(18), request.NodeId, local.NodeId,
            local.LibraryEpoch, NodeRequestSignature.RandomToken(), 1);
        state.InboundDataSyncGrants.Add(credentials.GrantId,
            new StoredGrant { Credentials = credentials, CreatedAt = now });
        request.GrantId = credentials.GrantId;
        request.Status = "granted";
        foreach (var stale in state.IncomingDataSyncRequests.Where(r => r != request && r.NodeId == request.NodeId &&
                     r.Status == "awaitingApproval"))
            stale.Status = "rejected";
        if (!state.Peers.TryGetValue(request.NodeId, out var peer))
            state.Peers.Add(request.NodeId, peer = new StoredPeer { NodeId = request.NodeId });
        if (peer.Label.Length == 0) peer.Label = request.NodeName;
    }

    /// <returns>The grant ids this revoked.</returns>
    private static List<string> RevokeSubject(Dictionary<string, StoredGrant> grants, string nodeId)
    {
        var revoked = new List<string>();
        foreach (var grant in grants.Values.Where(g => !g.Revoked && g.Credentials.SubjectNodeId == nodeId))
        {
            grant.Revoked = true;
            revoked.Add(grant.Credentials.GrantId);
        }
        return revoked;
    }

    private static StoredGrant? LiveGrant(Dictionary<string, StoredGrant> grants, string nodeId, string libraryEpoch) =>
        grants.Values.FirstOrDefault(g => !g.Revoked && g.Credentials.SubjectNodeId == nodeId &&
            g.Credentials.LibraryEpoch == libraryEpoch);

    private static List<string> LiveGrantIds(Dictionary<string, StoredGrant> grants) =>
        grants.Values.Where(g => !g.Revoked).Select(g => g.Credentials.GrantId).ToList();

    private void SetLeases(IEnumerable<string> grantIds, bool enabled)
    {
        foreach (var grantId in grantIds)
            if (enabled) leases.Resume(grantId);
            else leases.Revoke(grantId);
    }

    private static void PruneDataSync(FederationState state, DateTimeOffset now)
    {
        state.IncomingDataSyncRequests.RemoveAll(r => r.ExpiresAt <= now);
        state.OutgoingDataSyncRequests.RemoveAll(r => r.ExpiresAt <= now);
    }

    private static void RequireDataSyncSharing(FederationState state)
    {
        if (!state.DataSyncSharingEnabled) throw NodeGrantService.DataSyncSharingDisabled();
    }

    private static FederationAccessException RequestNotFound(string message) => new("RequestNotFound", 404, message);

    private static FederationAccessException BadRequest(string message) => new("InvalidPairingRequest", 400, message);
}

using System.Globalization;
using System.Security.Cryptography;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Peers;

/// <summary>Owns local sharing decisions. A peer grant never calls these management methods.</summary>
/// <remarks>
/// Library access (<c>library.read</c>) and definitions access (<c>datasync.read</c>) are separate grants in separate
/// collections, each with its own switch, requests, codes and leases (§7.1). The methods named <c>DataSync</c> work on
/// definitions access only; every other method is about library access, except <see cref="RevokeAsync"/> (a grant
/// id of either kind), <see cref="RemovePeerAsync"/>, <see cref="RotateLibraryEpochAsync"/> and
/// <see cref="ResetAsNewNodeAsync"/>, which end both.
/// </remarks>
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
            state.OutboundDataSyncGrants.TryGetValue(peer.NodeId, out var dataSyncOutgoing);
            var dataSyncIncoming = LiveGrant(state.InboundDataSyncGrants, peer.NodeId, local.LibraryEpoch);
            return new FederationPeerView(peer.NodeId, peer.Label, peer.Address, peer.Enabled,
                peer.Enabled ? "Unknown" : "Disabled",
                outgoing == null ? null : new NodeGrantSummary(outgoing.GrantId, outgoing.Revision),
                incoming == null ? null : new NodeGrantSummary(incoming.Credentials.GrantId, incoming.Credentials.Revision),
                peer.PathMappings, peer.Kind, peer.Platform,
                dataSyncIncoming == null ? null
                    : new NodeGrantSummary(dataSyncIncoming.Credentials.GrantId, dataSyncIncoming.Credentials.Revision),
                dataSyncOutgoing == null ? null : new NodeGrantSummary(dataSyncOutgoing.GrantId, dataSyncOutgoing.Revision));
        }).OrderBy(p => p.Label, StringComparer.Ordinal).ToArray();
        var now = timeProvider.GetUtcNow();
        var requests = state.IncomingRequests.Where(r => r.ExpiresAt > now).Select(r =>
                new NodePairingRequestView(r.RequestId, r.NodeId, r.NodeName, "incoming", r.Status, r.ExpiresAt,
                    r.RemoteAddress, r.Status == "awaitingApproval" && state.InboundGrants.Values.Any(g =>
                        !g.Revoked && g.Credentials.SubjectNodeId == r.NodeId), r.Reciprocal != null))
            .Concat(state.OutgoingRequests.Where(r => r.ExpiresAt > now).Select(r =>
                new NodePairingRequestView(r.RequestId, r.NodeId, r.NodeName, "outgoing", r.Status, r.ExpiresAt)))
            .ToArray();
        return new FederationPeerStatus(local, state.SharingEnabled, peers, requests, state.DataSyncSharingEnabled);
    }

    public async Task SetSharingAsync(bool enabled, CancellationToken ct = default)
    {
        await identity.GetAsync(ct);
        var grants = await store.MutateAsync(state =>
        {
            state.SharingEnabled = enabled;
            return LiveGrantIds(state.InboundGrants);
        }, ct);
        // Per grant: library sharing never touches a definitions reader's lease, nor the reverse.
        SetLeases(grants, enabled);
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
            var existing = FindTransaction(state.IncomingRequests, request.TransactionId, request.NodeId,
                request.ClaimSecret);
            // Retrying a pending request with an invitation is a new approval path
            // for the same claimant, not merely a status poll. Completed/rejected
            // transactions remain idempotent and must never be revived by a code.
            if (existing != null && existing.Status != "awaitingApproval")
                return Exchange(state.InboundGrants, existing);
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
                AdmitIncoming(state.IncomingRequests, remoteAddress, authorized: true);
                pending = NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret,
                    remoteAddress, now);
                state.IncomingRequests.Add(pending);
            }
            pending.Reciprocal = ValidOffer(request.Reciprocal) ?? pending.Reciprocal;
            IssueGrant(state, pending, local, now, revoked);
            return Exchange(state.InboundGrants, pending);
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
            var existing = FindTransaction(state.IncomingRequests, request.TransactionId, request.NodeId,
                request.ClaimSecret);
            if (existing != null)
            {
                // A retry mints a new offer and the requester keeps only the newest code.
                if (existing.Status == "awaitingApproval" && ValidOffer(request.Reciprocal) is { } retried)
                    existing.Reciprocal = retried;
                return (Exchange(state.InboundGrants, existing), false);
            }
            AdmitIncoming(state.IncomingRequests, remoteAddress, authorized: false);
            var pending = NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret,
                remoteAddress, now);
            pending.Reciprocal = ValidOffer(request.Reciprocal);
            state.IncomingRequests.Add(pending);
            return (Exchange(state.InboundGrants, pending), true);
        }, ct);
    }

    public async Task<NodePairExchange> ClaimPairingAsync(NodePairClaimRequest request, CancellationToken ct = default)
    {
        var state = await store.ReadAsync(ct);
        RequireSharing(state);
        return Claim(state.IncomingRequests, state.InboundGrants, request);
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
    public async Task<NodeReciprocalOffer?> TakeReciprocalOfferAsync(string nodeId, CancellationToken ct = default) =>
        await store.MutateAsync(state => TakeOffer(state.IncomingRequests, nodeId), ct);

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
            AddReciprocal(state.ReciprocalInvitations, audienceNodeId, code, now);
            return true;
        }, ct);
        return code;
    }

    /// <summary>
    /// Forgets a device in both directions: no access to it, no access from it, no record — library access and
    /// definitions access alike, with every request and code that concerns it.
    /// </summary>
    public async Task RemovePeerAsync(string nodeId, CancellationToken ct = default)
    {
        var leaseKeys = await store.MutateAsync(state =>
        {
            var keys = new List<string>();
            if (state.OutboundGrants.GetValueOrDefault(nodeId)?.GrantId is { } outbound)
                keys.Add(GrantLeaseRegistry.OutboundKey(outbound));
            if (state.OutboundDataSyncGrants.GetValueOrDefault(nodeId)?.GrantId is { } outboundDataSync)
                keys.Add(GrantLeaseRegistry.OutboundKey(outboundDataSync));
            state.OutboundGrants.Remove(nodeId);
            state.OutboundDataSyncGrants.Remove(nodeId);
            keys.AddRange(RevokeSubject(state.InboundGrants, nodeId));
            keys.AddRange(RevokeSubject(state.InboundDataSyncGrants, nodeId));
            state.Peers.Remove(nodeId);
            state.IncomingRequests.RemoveAll(r => r.NodeId == nodeId);
            state.OutgoingRequests.RemoveAll(r => r.NodeId == nodeId);
            state.ReciprocalInvitations.RemoveAll(i => i.AudienceNodeId == nodeId);
            state.IncomingDataSyncRequests.RemoveAll(r => r.NodeId == nodeId);
            state.OutgoingDataSyncRequests.RemoveAll(r => r.NodeId == nodeId);
            state.DataSyncReciprocalInvitations.RemoveAll(i => i.AudienceNodeId == nodeId);
            return keys;
        }, ct);
        foreach (var key in leaseKeys) leases.Revoke(key);
    }

    public async Task RejectAsync(string requestId, CancellationToken ct = default)
    {
        await store.MutateAsync(state => Reject(state.IncomingRequests, requestId), ct);
    }

    /// <summary>Stops waiting for an outgoing request; the other device lets its copy expire.</summary>
    public async Task CancelOutgoingAsync(string requestId, CancellationToken ct = default)
    {
        await store.MutateAsync(state => state.OutgoingRequests.RemoveAll(r =>
            r.RequestId == requestId && r.Status == "awaitingApproval"), ct);
    }

    /// <summary>Revokes one grant this device issued, of either kind: grant ids never collide.</summary>
    public async Task RevokeAsync(string grantId, CancellationToken ct = default)
    {
        await store.MutateAsync(state =>
        {
            if (state.InboundGrants.TryGetValue(grantId, out var grant)) grant.Revoked = true;
            if (state.InboundDataSyncGrants.TryGetValue(grantId, out var dataSync)) dataSync.Revoked = true;
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
            state.SharingEnabled = false;
            state.BrowsingEnabled = false;
            foreach (var grant in state.InboundGrants.Values) grant.Revoked = true;
            state.IncomingRequests.Clear();
            state.Invitation = null;
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
    public async Task<NodeIdentity> ResetAsNewNodeAsync(CancellationToken ct = default)
    {
        var result = await store.ResetAsNewNodeAsync(ct);
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
        var peers = state.Peers.Values.Select(peer => new NodeDataSyncPeerView(peer.NodeId, peer.Label,
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
    /// The definitions sharing switch. Off cancels every datasync reader's lease, on resumes them; library leases are
    /// never touched. It never changes remote access: the host couples that (§7.1.3).
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

    /// <summary>An 8-digit, one-time, 10-minute code redeemed only on <c>pair/datasync/code</c> (§7.2.3).</summary>
    /// <param name="allowTwoWay">Whoever redeems it may also be read back: the two-way consent, given now.</param>
    public async Task<NodeInvitation> IssueDataSyncInvitationAsync(bool allowTwoWay, CancellationToken ct = default)
    {
        await identity.GetAsync(ct);
        var code = RandomNumberGenerator.GetInt32(100_000_000).ToString("D8", CultureInfo.InvariantCulture);
        var expiresAt = timeProvider.GetUtcNow() + PairingLifetime;
        await store.MutateAsync(state =>
        {
            RequireDataSyncSharing(state);
            state.DataSyncInvitation = new StoredInvitation
                { CodeHash = NodeRequestSignature.HashSecret(code), ExpiresAt = expiresAt, AllowTwoWay = allowTwoWay };
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
    /// <c>pair/datasync/code</c>: a datasync code (never a library one) grants at once. A two-way redemption is read
    /// back only when the code was made with two-way consent and the redeemer offered it; otherwise the exchange says
    /// <see cref="NodeDataSyncReadBack.Declined"/>. A reciprocal code (this device's own read-back of a device that
    /// approved it) grants and is never read back again.
    /// </summary>
    /// <returns>The exchange; whether this call issued the grant (a re-delivery does not); the request's intent.</returns>
    public async Task<(NodePairExchange Exchange, bool Issued, string Intent)> ExchangeDataSyncCodeAsync(
        NodeDataSyncPairCodeRequest request, string? remoteAddress = null, CancellationToken ct = default)
    {
        ValidatePairRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret);
        var intent = ValidIntent(request.Intent);
        var offer = ValidOffer(request.Reciprocal);
        var local = await identity.GetAsync(ct);
        if (request.NodeId == local.NodeId) throw BadRequest("A node cannot pair with itself.");
        var now = timeProvider.GetUtcNow();
        var revoked = new List<string>();
        var result = await store.MutateAsync<(NodePairExchange, bool, string)?>(state =>
        {
            RequireDataSyncSharing(state);
            PruneDataSync(state, now);
            var existing = FindTransaction(state.IncomingDataSyncRequests, request.TransactionId, request.NodeId,
                request.ClaimSecret);
            if (existing != null && existing.Status != "awaitingApproval")
                return (Exchange(state.InboundDataSyncGrants, existing), false,
                    existing.Intent ?? NodeDataSyncIntents.Follow);
            var codeHash = NodeRequestSignature.HashSecret(request.Code?.Trim());
            var reciprocal = state.DataSyncReciprocalInvitations.FirstOrDefault(i => i.ExpiresAt > now &&
                i.AudienceNodeId == request.NodeId && NodeRequestSignature.FixedEquals(i.CodeHash, codeHash));
            var allowTwoWay = false;
            if (reciprocal != null) state.DataSyncReciprocalInvitations.Remove(reciprocal);
            else if (state.DataSyncInvitation is not { } invitation || invitation.ExpiresAt <= now ||
                     invitation.FailedAttempts >= 5)
                return null;
            else if (!NodeRequestSignature.FixedEquals(invitation.CodeHash, codeHash))
            {
                invitation.FailedAttempts++;
                return null;
            }
            else
            {
                allowTwoWay = invitation.AllowTwoWay;
                state.DataSyncInvitation = null;
            }
            var pending = existing;
            if (pending == null)
            {
                AdmitIncoming(state.IncomingDataSyncRequests, remoteAddress, authorized: true);
                pending = NewRequest(request.NodeId, request.NodeName, request.TransactionId, request.ClaimSecret,
                    remoteAddress, now);
                state.IncomingDataSyncRequests.Add(pending);
            }
            pending.Intent = intent;
            offer ??= pending.Reciprocal;
            if (reciprocal != null || intent != NodeDataSyncIntents.TwoWay)
            {
                pending.Reciprocal = null;
                pending.ReadBack = null;
            }
            else if (allowTwoWay && offer != null)
            {
                pending.Reciprocal = offer;
                pending.ReadBack = NodeDataSyncReadBack.Started;
            }
            else
            {
                // Two-way consent is given when a code is made, never by whoever redeems it (§7.2.3).
                pending.Reciprocal = null;
                pending.ReadBack = NodeDataSyncReadBack.Declined;
            }
            IssueDataSyncGrant(state, pending, local, now, revoked);
            return (Exchange(state.InboundDataSyncGrants, pending), true, intent);
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

    /// <summary>Issues the requester a <c>datasync.read</c> grant; library access is never created or revoked.</summary>
    public async Task<NodeDataSyncApproval> ApproveDataSyncAsync(string requestId, CancellationToken ct = default)
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
            return new NodeDataSyncApproval(request.NodeId, request.NodeName, intent,
                intent == NodeDataSyncIntents.TwoWay && request.Reciprocal != null);
        }, ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
        return approval;
    }

    /// <returns>Whether the request exists.</returns>
    public async Task<bool> RejectDataSyncAsync(string requestId, CancellationToken ct = default) =>
        await store.MutateAsync(state => Reject(state.IncomingDataSyncRequests, requestId), ct);

    /// <returns>Whether a waiting outgoing request was cancelled.</returns>
    public async Task<bool> CancelOutgoingDataSyncAsync(string requestId, CancellationToken ct = default) =>
        await store.MutateAsync(state => state.OutgoingDataSyncRequests.RemoveAll(r =>
            r.RequestId == requestId && r.Status == "awaitingApproval") > 0, ct);

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

    /// <summary>Takes (once) a granted two-way requester's offer to be read back.</summary>
    public async Task<NodeReciprocalOffer?> TakeDataSyncReciprocalOfferAsync(string nodeId,
        CancellationToken ct = default) =>
        await store.MutateAsync(state => TakeOffer(state.IncomingDataSyncRequests, nodeId), ct);

    /// <summary>Stops a device reading this device's definitions: its live datasync grants and their leases.</summary>
    public async Task RevokeDataSyncAsync(string nodeId, CancellationToken ct = default)
    {
        var revoked = await store.MutateAsync(state => RevokeSubject(state.InboundDataSyncGrants, nodeId), ct);
        foreach (var grantId in revoked) leases.Revoke(grantId);
    }

    /// <summary>
    /// "Done — stop reading X": drops this device's own datasync credentials for the peer and its datasync requests
    /// to it. Library access and the peer's browsing switch are untouched.
    /// </summary>
    public async Task ForgetOutboundDataSyncAsync(string nodeId, CancellationToken ct = default)
    {
        var grantId = await store.MutateAsync(state =>
        {
            var grantId = state.OutboundDataSyncGrants.GetValueOrDefault(nodeId)?.GrantId;
            state.OutboundDataSyncGrants.Remove(nodeId);
            state.OutgoingDataSyncRequests.RemoveAll(r => r.NodeId == nodeId);
            return grantId;
        }, ct);
        if (grantId != null) leases.Revoke(GrantLeaseRegistry.OutboundKey(grantId));
    }

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

    private static void IssueGrant(FederationState state, StoredPairRequest request, NodeIdentity local,
        DateTimeOffset now, List<string> revoked)
    {
        revoked.AddRange(RevokeSubject(state.InboundGrants, request.NodeId));
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

    /// <summary>
    /// <see cref="IssueGrant"/> for definitions: it replaces only the subject's datasync grants, and adds the peer
    /// if it is new, so every datasync peer is listed; but the name an unsigned request claims never replaces the
    /// name of a device this one already knows (§7.1.1).
    /// </summary>
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

    private static void Prune(FederationState state, DateTimeOffset now)
    {
        state.IncomingRequests.RemoveAll(r => r.ExpiresAt <= now);
        state.OutgoingRequests.RemoveAll(r => r.ExpiresAt <= now);
    }

    private static void PruneDataSync(FederationState state, DateTimeOffset now)
    {
        state.IncomingDataSyncRequests.RemoveAll(r => r.ExpiresAt <= now);
        state.OutgoingDataSyncRequests.RemoveAll(r => r.ExpiresAt <= now);
    }

    private static void RequireSharing(FederationState state)
    {
        if (!state.SharingEnabled)
            throw new FederationAccessException("SharingDisabled", 403, "Enable resource sharing before pairing another node.");
    }

    private static void RequireDataSyncSharing(FederationState state)
    {
        if (!state.DataSyncSharingEnabled) throw NodeGrantService.DataSyncSharingDisabled();
    }

    private static FederationAccessException RequestNotFound(string message) => new("RequestNotFound", 404, message);

    private static FederationAccessException BadRequest(string message) => new("InvalidPairingRequest", 400, message);
}

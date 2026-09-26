using System.Collections.Concurrent;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.Federation.Transport;

/// <param name="Scope">
/// Which of this device's grants for the peer the session uses (<see cref="FederationScopes"/>): library browsing
/// or reading the peer's definitions.
/// </param>
public sealed record PeerSessionSnapshot(string NodeId, string BaseAddress, NodeCredentials Credentials,
    TimeSpan ClockOffset, NodeInfo Info, DateTimeOffset VerifiedAt, string Scope = FederationScopes.LibraryRead)
{
    public string LibraryEpoch => Credentials.LibraryEpoch;
    public string GrantId => Credentials.GrantId;
    public long GrantVersion => Credentials.Revision;
}

public interface IPeerSessionFactory
{
    Task<PeerSessionSnapshot> GetAsync(string nodeId, CancellationToken cancellationToken = default);
}

/// <remarks>
/// A peer has up to two sessions, one per grant this device holds for it: library browsing
/// (<see cref="FederationScopes.LibraryRead"/>, the default) and reading its definitions
/// (<see cref="FederationScopes.DataSyncRead"/>). They share nothing: each has its own verified cache, gate,
/// connection state and relocation throttle (the datasync ones keyed <c>datasync:</c>), and a datasync session
/// never writes what library browsing routes by (§7.1.1).
/// </remarks>
public sealed class PeerSessionFactory(FederationStateStore store, INodeIdentityProvider identity,
    FederationHttpClient http, TimeProvider timeProvider, INodePeerDiscovery? discovery = null) : IPeerSessionFactory
{
    /// <summary>A peer that stays offline is not rediscovered on every query.</summary>
    private static readonly TimeSpan RelocationInterval = TimeSpan.FromSeconds(30);
    private const string DataSyncKeyPrefix = "datasync:";
    private readonly ConcurrentDictionary<string, PeerSessionSnapshot> _verified = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _relocationAttempts = new(StringComparer.Ordinal);

    public string GetConnectionState(string nodeId) => _statuses.GetValueOrDefault(nodeId, "Unknown");

    /// <summary>The connection state of one of a peer's sessions.</summary>
    public string GetConnectionState(string nodeId, string scope) => GetConnectionState(Key(nodeId, scope));

    public Task<PeerSessionSnapshot> GetAsync(string nodeId, CancellationToken cancellationToken = default) =>
        GetAsync(nodeId, FederationScopes.LibraryRead, cancellationToken);

    /// <summary>
    /// A verified session with the grant of <paramref name="scope"/>. A datasync session needs the peer, an address
    /// (<c>DataSyncAddress ?? Address</c>) and <c>OutboundDataSyncGrants[nodeId]</c>, but not <c>peer.Enabled</c>, which
    /// is the library browsing switch.
    /// </summary>
    public async Task<PeerSessionSnapshot> GetAsync(string nodeId, string scope,
        CancellationToken cancellationToken = default)
    {
        var dataSync = IsDataSync(scope);
        var key = Key(nodeId, scope);
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var local = await identity.GetAsync(cancellationToken);
            var state = await store.ReadAsync(cancellationToken);
            if (!TryGetRoute(state, nodeId, dataSync, out var peer, out var address, out var credentials))
                throw new FederationAccessException("NodeNotAuthorized", 403, "This node is not enabled with a direct read authorization.");
            if (nodeId == local.NodeId || credentials.SubjectNodeId != local.NodeId || credentials.AudienceNodeId != nodeId)
                throw new FederationAccessException("IdentityConflict", 409, "The saved node authorization has the wrong direction.");
            var now = timeProvider.GetUtcNow();
            if (_verified.TryGetValue(key, out var cached) && cached.BaseAddress == address &&
                cached.Credentials == credentials && now - cached.VerifiedAt < TimeSpan.FromMinutes(1)) return cached;

            PeerSessionSnapshot snapshot;
            try
            {
                snapshot = await VerifyAsync(nodeId, address, credentials, scope, cancellationToken);
            }
            catch (FederationAccessException e) when (e.ErrorCode is "NodeUnreachable" or "IdentityConflict" &&
                                                      TryStartRelocation(key, now))
            {
                snapshot = await RelocateAsync(nodeId, address, credentials, scope, cancellationToken) ?? throw e;
            }
            _verified[key] = snapshot;
            _statuses[key] = "Online";
            var kind = ServerSelfDescriptionWords.KindOf(snapshot.Info.Kind);
            var platform = ServerSelfDescriptionWords.PlatformOf(snapshot.Info.Platform);
            if (!dataSync)
            {
                if (snapshot.Info.Name != peer.Label || kind != peer.Kind || platform != peer.Platform)
                {
                    // The name came with the authenticated handshake; follow the peer's renames — and
                    // what it says it is, which rides on the same verified answer.
                    await store.MutateAsync(current =>
                    {
                        if (current.Peers.TryGetValue(nodeId, out var stored))
                        {
                            stored.Label = snapshot.Info.Name;
                            stored.Kind = kind;
                            stored.Platform = platform;
                        }
                        return true;
                    }, cancellationToken);
                }
            }
            else if (!state.HasLibraryGrant(nodeId) &&
                     (snapshot.Info.Name != peer.Label || kind != peer.Kind || platform != peer.Platform))
                await FollowDataSyncPeerAsync(nodeId, snapshot, kind, platform, cancellationToken);
            return snapshot;
        }
        catch (FederationAccessException e)
        {
            _statuses[key] = e.ErrorCode switch
            {
                "IdentityConflict" or "LibraryEpochChanged" => "IdentityConflict",
                "ProtocolUnsupported" => "Incompatible",
                _ when e.StatusCode is 401 or 403 => "Unauthorized",
                _ => "Offline"
            };
            _verified.TryRemove(key, out _);
            throw;
        }
        catch (OperationCanceledException)
        {
            _statuses[key] = "Offline";
            _verified.TryRemove(key, out _);
            throw;
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Forgets <paramref name="session"/> if it is still the verified session of its peer and scope, so the next
    /// <see cref="GetAsync(string, string, CancellationToken)"/> asks the peer's info and handshake again instead of
    /// reusing it for the rest of its minute. For a request the peer refused with a session verified before the
    /// request's own call: what the peer did since — revoked the grant, or replaced its library, which revokes the
    /// grant and whose new epoch only its info shows — is then read from the peer itself. A newer session another
    /// caller verified meanwhile is kept.
    /// </summary>
    /// <returns>Whether the session was forgotten.</returns>
    public bool Invalidate(PeerSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _verified.TryRemove(KeyValuePair.Create(Key(session.NodeId, session.Scope), session));
    }

    private static bool IsDataSync(string scope) => scope switch
    {
        FederationScopes.LibraryRead => false,
        FederationScopes.DataSyncRead => true,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "A session uses a library or a datasync grant.")
    };

    /// <summary>Library keys are the bare node id, as they always were; datasync keys are prefixed.</summary>
    private static string Key(string nodeId, string scope) => IsDataSync(scope) ? DataSyncKeyPrefix + nodeId : nodeId;

    /// <summary>Where a session of this scope goes and with which grant; false when this device has none.</summary>
    private static bool TryGetRoute(FederationState state, string nodeId, bool dataSync, out StoredPeer peer,
        out string address, out NodeCredentials credentials)
    {
        address = null!;
        credentials = null!;
        if (!state.Peers.TryGetValue(nodeId, out peer!)) return false;
        if (dataSync)
        {
            if ((peer.DataSyncAddress ?? peer.Address) is not { } dataSyncAddress ||
                !state.OutboundDataSyncGrants.TryGetValue(nodeId, out var dataSyncCredentials)) return false;
            (address, credentials) = (dataSyncAddress, dataSyncCredentials);
            return true;
        }
        if (!peer.Enabled || peer.Address == null || !state.OutboundGrants.TryGetValue(nodeId, out var libraryCredentials))
            return false;
        (address, credentials) = (peer.Address, libraryCredentials);
        return true;
    }

    /// <summary>
    /// A datasync handshake's name, kind and platform, for a peer only data sync knows. Checked again inside the
    /// write, so a library grant that arrived meanwhile keeps its routing (§7.1.1).
    /// </summary>
    private Task FollowDataSyncPeerAsync(string nodeId, PeerSessionSnapshot snapshot, ServerKind? kind,
        RemoteDevicePlatform? platform, CancellationToken cancellationToken) =>
        store.MutateAsync(current =>
        {
            if (!current.HasLibraryGrant(nodeId) && current.Peers.TryGetValue(nodeId, out var stored) &&
                current.OutboundDataSyncGrants.GetValueOrDefault(nodeId) == snapshot.Credentials)
            {
                stored.Label = snapshot.Info.Name;
                stored.Kind = kind;
                stored.Platform = platform;
            }
            return true;
        }, cancellationToken);

    /// <summary>Proves that <paramref name="address"/> holds the paired node's grant key.</summary>
    private async Task<PeerSessionSnapshot> VerifyAsync(string nodeId, string address, NodeCredentials credentials,
        string scope, CancellationToken cancellationToken)
    {
        var sentAt = timeProvider.GetUtcNow();
        var info = await http.PublicAsync<NodeInfo>(address, HttpMethod.Get, "/federation/v1/info", null,
            cancellationToken);
        ValidateInfo(info, nodeId, credentials.LibraryEpoch);
        var offset = info.ServerTimeUtc - (sentAt + (timeProvider.GetUtcNow() - sentAt) / 2);
        var challenge = NodeRequestSignature.RandomToken();
        using var request = FederationHttpClient.CreateRequest(address, HttpMethod.Post,
            "/federation/v1/export/handshake", new NodeHandshakeRequest(challenge));
        await FederationHttpClient.SignAsync(request, credentials, timeProvider.GetUtcNow() + offset, cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(http.PublicDeadline);
        var handshakeSentAt = timeProvider.GetUtcNow();
        using var response = await http.SendAsync(request, deadline.Token);
        var proof = await FederationHttpClient.ReadEnvelopeAsync<NodeHandshakeResponse>(response, deadline.Token);
        ValidateInfo(proof.Info, nodeId, credentials.LibraryEpoch);
        if (proof.Challenge != challenge || !NodeRequestSignature.FixedEquals(proof.Proof,
                NodeRequestSignature.HandshakeProof(credentials.Key, proof.Info, challenge)))
            throw new FederationAccessException("IdentityConflict", 409, "The address could not prove the paired node's identity.");

        // The unsigned info clock only bootstraps this challenge. Retain the
        // authenticated server clock for query signatures and asset lifetimes.
        var verifiedOffset = proof.Info.ServerTimeUtc -
                             (handshakeSentAt + (timeProvider.GetUtcNow() - handshakeSentAt) / 2);
        return new PeerSessionSnapshot(nodeId, address, credentials, verifiedOffset, proof.Info,
            timeProvider.GetUtcNow(), scope);
    }

    private bool TryStartRelocation(string key, DateTimeOffset now)
    {
        if (discovery == null) return false;
        var last = _relocationAttempts.GetValueOrDefault(key);
        if (now - last < RelocationInterval) return false;
        _relocationAttempts[key] = now;
        return true;
    }

    /// <summary>
    /// Addresses change (DHCP, another network). Discovery only nominates candidates for the same
    /// NodeId; an address is adopted only after it proves possession of the existing grant's key.
    /// </summary>
    private async Task<PeerSessionSnapshot?> RelocateAsync(string nodeId, string oldAddress, NodeCredentials credentials,
        string scope, CancellationToken cancellationToken)
    {
        IReadOnlyList<NodeDiscoveryCandidate> candidates;
        try { candidates = await discovery!.DiscoverAsync(cancellationToken); }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        foreach (var candidate in candidates.Where(c => c.NodeId == nodeId))
        {
            string address;
            try { address = FederationHttpClient.NormalizeAddress(candidate.Address); }
            catch (FederationAccessException) { continue; }
            if (address == oldAddress) continue;
            PeerSessionSnapshot snapshot;
            try { snapshot = await VerifyAsync(nodeId, address, credentials, scope, cancellationToken); }
            catch (FederationAccessException) { continue; }
            await store.MutateAsync(state =>
            {
                if (IsDataSync(scope))
                {
                    // Only while the datasync grant is the one proven here. The library's address moves along
                    // only for a peer library browsing does not route to (§7.1.1).
                    if (state.Peers.TryGetValue(nodeId, out var dataSyncPeer) &&
                        (dataSyncPeer.DataSyncAddress ?? dataSyncPeer.Address) == oldAddress &&
                        state.OutboundDataSyncGrants.GetValueOrDefault(nodeId) == credentials)
                    {
                        dataSyncPeer.DataSyncAddress = address;
                        if (!state.HasLibraryGrant(nodeId)) dataSyncPeer.Address = address;
                    }
                    return true;
                }
                // Keep a concurrent re-pairing's newer address and grant.
                if (state.Peers.TryGetValue(nodeId, out var peer) && peer.Address == oldAddress &&
                    state.OutboundGrants.GetValueOrDefault(nodeId) == credentials)
                    peer.Address = address;
                return true;
            }, cancellationToken);
            return snapshot;
        }
        return null;
    }

    internal static void ValidateInfo(NodeInfo info, string? nodeId = null, string? libraryEpoch = null)
    {
        if (!NodeRequestSignature.IsIdentifier(info.NodeId) || !NodeRequestSignature.IsIdentifier(info.LibraryEpoch))
            throw new FederationAccessException("InvalidNodeResponse", 502, "The node did not return a valid identity.");
        if (string.IsNullOrWhiteSpace(info.Name) || info.Name.Length > 128 || info.Name.Any(char.IsControl) ||
            !ValidCapabilities(info.SupportedFilters) || !ValidCapabilities(info.SupportedSorts) ||
            !ValidCapabilities(info.SupportedAssetKinds) || info.MaxBatchSize is < 1 or > 1000 ||
            !ValidWord(info.Kind) || !ValidWord(info.Platform) ||
            (info.DataSyncKinds != null && !ValidCapabilities(info.DataSyncKinds)) ||
            info.DataSyncContractVersion is < 0 or > MaxDataSyncContractVersion ||
            info.DataSyncMinimumPeerContract is < 0 or > MaxDataSyncContractVersion)
            throw new FederationAccessException("InvalidNodeResponse", 502, "The node identity exceeds the protocol metadata budget.");
        if (info.ProtocolVersion != 1)
            throw new FederationAccessException("ProtocolUnsupported", 409, "This node does not support the same federation protocol.");
        if (nodeId != null && nodeId != info.NodeId)
            throw new FederationAccessException("IdentityConflict", 409, "This address belongs to a different node.");
        if (libraryEpoch != null && libraryEpoch != info.LibraryEpoch)
            throw new FederationAccessException("LibraryEpochChanged", 409, "The source library was replaced. Pair with its current library again.");
    }

    /// <summary>The data sync contract versions a node may state (§7.4): absent, or 0..1,000,000.</summary>
    private const int MaxDataSyncContractVersion = 1_000_000;

    private static bool ValidCapabilities(string[]? values) => values is { Length: <= 64 } &&
        values.All(value => value is { Length: > 0 and <= 128 } && !value.Any(char.IsControl));

    /// <summary>
    /// An optional word a node says about itself: absent, or short and printable. One this build
    /// does not know is fine — a later build may say more — but not one outside the budget.
    /// </summary>
    private static bool ValidWord(string? value) => value == null ||
        value.Length <= ServerSelfDescriptionWords.MaxLength && !value.Any(char.IsControl);
}

using System.Collections.Concurrent;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Transport;

public sealed record PeerSessionSnapshot(string NodeId, string BaseAddress, NodeCredentials Credentials,
    TimeSpan ClockOffset, NodeInfo Info, DateTimeOffset VerifiedAt)
{
    public string LibraryEpoch => Credentials.LibraryEpoch;
    public string GrantId => Credentials.GrantId;
    public long GrantVersion => Credentials.Revision;
}

public interface IPeerSessionFactory
{
    Task<PeerSessionSnapshot> GetAsync(string nodeId, CancellationToken cancellationToken = default);
}

public sealed class PeerSessionFactory(FederationStateStore store, INodeIdentityProvider identity,
    FederationHttpClient http, TimeProvider timeProvider, INodePeerDiscovery? discovery = null) : IPeerSessionFactory
{
    /// <summary>A peer that stays offline is not rediscovered on every query.</summary>
    private static readonly TimeSpan RelocationInterval = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, PeerSessionSnapshot> _verified = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _relocationAttempts = new(StringComparer.Ordinal);

    public string GetConnectionState(string nodeId) => _statuses.GetValueOrDefault(nodeId, "Unknown");

    public async Task<PeerSessionSnapshot> GetAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(nodeId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var local = await identity.GetAsync(cancellationToken);
            var state = await store.ReadAsync(cancellationToken);
            if (!state.Peers.TryGetValue(nodeId, out var peer) || !peer.Enabled || peer.Address == null ||
                !state.OutboundGrants.TryGetValue(nodeId, out var credentials))
                throw new FederationAccessException("NodeNotAuthorized", 403, "This node is not enabled with a direct read authorization.");
            if (nodeId == local.NodeId || credentials.SubjectNodeId != local.NodeId || credentials.AudienceNodeId != nodeId)
                throw new FederationAccessException("IdentityConflict", 409, "The saved node authorization has the wrong direction.");
            var now = timeProvider.GetUtcNow();
            if (_verified.TryGetValue(nodeId, out var cached) && cached.BaseAddress == peer.Address &&
                cached.Credentials == credentials && now - cached.VerifiedAt < TimeSpan.FromMinutes(1)) return cached;

            PeerSessionSnapshot snapshot;
            try
            {
                snapshot = await VerifyAsync(nodeId, peer.Address, credentials, cancellationToken);
            }
            catch (FederationAccessException e) when (e.ErrorCode is "NodeUnreachable" or "IdentityConflict" &&
                                                      TryStartRelocation(nodeId, now))
            {
                snapshot = await RelocateAsync(nodeId, peer.Address, credentials, cancellationToken) ?? throw e;
            }
            _verified[nodeId] = snapshot;
            _statuses[nodeId] = "Online";
            if (snapshot.Info.Name != peer.Label)
            {
                // The name came with the authenticated handshake; follow the peer's renames.
                await store.MutateAsync(current =>
                {
                    if (current.Peers.TryGetValue(nodeId, out var stored)) stored.Label = snapshot.Info.Name;
                    return true;
                }, cancellationToken);
            }
            return snapshot;
        }
        catch (FederationAccessException e)
        {
            _statuses[nodeId] = e.ErrorCode switch
            {
                "IdentityConflict" or "LibraryEpochChanged" => "IdentityConflict",
                "ProtocolUnsupported" => "Incompatible",
                _ when e.StatusCode is 401 or 403 => "Unauthorized",
                _ => "Offline"
            };
            _verified.TryRemove(nodeId, out _);
            throw;
        }
        catch (OperationCanceledException)
        {
            _statuses[nodeId] = "Offline";
            _verified.TryRemove(nodeId, out _);
            throw;
        }
        finally { gate.Release(); }
    }

    /// <summary>Proves that <paramref name="address"/> holds the paired node's grant key.</summary>
    private async Task<PeerSessionSnapshot> VerifyAsync(string nodeId, string address, NodeCredentials credentials,
        CancellationToken cancellationToken)
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
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
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
            timeProvider.GetUtcNow());
    }

    private bool TryStartRelocation(string nodeId, DateTimeOffset now)
    {
        if (discovery == null) return false;
        var last = _relocationAttempts.GetValueOrDefault(nodeId);
        if (now - last < RelocationInterval) return false;
        _relocationAttempts[nodeId] = now;
        return true;
    }

    /// <summary>
    /// Addresses change (DHCP, another network). Discovery only nominates candidates for the same
    /// NodeId; an address is adopted only after it proves possession of the existing grant's key.
    /// </summary>
    private async Task<PeerSessionSnapshot?> RelocateAsync(string nodeId, string oldAddress, NodeCredentials credentials,
        CancellationToken cancellationToken)
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
            try { snapshot = await VerifyAsync(nodeId, address, credentials, cancellationToken); }
            catch (FederationAccessException) { continue; }
            await store.MutateAsync(state =>
            {
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
            !ValidCapabilities(info.SupportedAssetKinds) || info.MaxBatchSize is < 1 or > 1000)
            throw new FederationAccessException("InvalidNodeResponse", 502, "The node identity exceeds the protocol metadata budget.");
        if (info.ProtocolVersion != 1)
            throw new FederationAccessException("ProtocolUnsupported", 409, "This node does not support the same federation protocol.");
        if (nodeId != null && nodeId != info.NodeId)
            throw new FederationAccessException("IdentityConflict", 409, "This address belongs to a different node.");
        if (libraryEpoch != null && libraryEpoch != info.LibraryEpoch)
            throw new FederationAccessException("LibraryEpochChanged", 409, "The source library was replaced. Pair with its current library again.");
    }

    private static bool ValidCapabilities(string[]? values) => values is { Length: <= 64 } &&
        values.All(value => value is { Length: > 0 and <= 128 } && !value.Any(char.IsControl));
}

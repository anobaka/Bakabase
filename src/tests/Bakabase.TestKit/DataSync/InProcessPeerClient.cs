using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.TestKit.DataSync;

/// <summary>One call an <see cref="InProcessPeerClient"/> made: <c>probe</c>, <c>head</c>, <c>manifest</c> or <c>page</c>.</summary>
public sealed record InProcessPeerCall(string Operation, string PeerNodeId, DataSyncFeedQuery? Query,
    string? SnapshotId = null, string? Kind = null, long? SinceSeq = null, string? Cursor = null);

/// <summary>
/// The receiver side of the feed between two TestKit providers of one process (spec §13.7): it reads another
/// provider's real <see cref="IDataSyncFeedSource"/> as a peer holding a <c>datasync.read</c> grant would over
/// federation, and hands back <b>the real page bytes</b>, which the receiver parses with <c>DataSyncWireReader</c>.
/// </summary>
/// <remarks>
/// <para>
/// Like the transport: heads and manifests travel as federation JSON (<see cref="FederationJson.Options"/>, depth 32,
/// serialized and parsed again), pages as raw bytes bounded by <see cref="FederationHttpClient.MaxControlResponseBytes"/>,
/// and the source's refusals become <see cref="DataSyncPeerException"/>s with the codes and retry advice of the
/// federation peer client (§7.6). The source sees this device as a reader named by its identity, under one grant id
/// per pair, so the source keeps one snapshot for it and rate-limits its manifests.
/// </para>
/// <para>
/// Register one per provider as its <see cref="IDataSyncPeerClient"/> (it has no dependencies), build both providers,
/// then call <see cref="WireAsync"/>. A test can take a peer offline, revoke this device's access or inject any
/// refusal (<see cref="Fault"/>), and read back every call. One fetch per peer at a time (§7.6):
/// <see cref="AcquireFetchAsync"/> holds the peer's lock as the federation peer client does.
/// </para>
/// </remarks>
public sealed class InProcessPeerClient : IDataSyncPeerClient
{
    private readonly ConcurrentDictionary<string, Peer> _peers = new(StringComparer.Ordinal);
    private readonly List<InProcessPeerCall> _calls = [];

    /// <summary>This device as the peers' feeds see it; set by <see cref="ConnectAsync"/>.</summary>
    public DataSyncDevice? Self { get; set; }

    /// <summary>A refusal to answer instead of the peer, per operation and peer; null lets the call through.</summary>
    public Func<string, string, DataSyncPeerException?>? Fault { get; set; }

    public IReadOnlyList<InProcessPeerCall> Calls
    {
        get
        {
            lock (_calls) return _calls.ToArray();
        }
    }

    /// <summary>Connects each provider's <see cref="InProcessPeerClient"/> to the other's feed.</summary>
    public static async Task WireAsync(IServiceProvider a, IServiceProvider b, CancellationToken ct = default)
    {
        await ClientOf(a).ConnectAsync(a, b, ct);
        await ClientOf(b).ConnectAsync(b, a, ct);
    }

    /// <summary>The <see cref="InProcessPeerClient"/> a provider registered as its peer client.</summary>
    public static InProcessPeerClient ClientOf(IServiceProvider services) =>
        services.GetRequiredService<IDataSyncPeerClient>() as InProcessPeerClient ??
        throw new InvalidOperationException($"The provider's {nameof(IDataSyncPeerClient)} is not an {nameof(InProcessPeerClient)}.");

    /// <summary>Lets <paramref name="self"/> read <paramref name="peer"/>'s feed, both named by their device identities.</summary>
    public async Task ConnectAsync(IServiceProvider self, IServiceProvider peer, CancellationToken ct = default)
    {
        Self = await self.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(ct);
        var peerDevice = await peer.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(ct);
        Connect(peerDevice.NodeId, peerDevice.Name, peer);
    }

    /// <summary>Lets this device read the feed of <paramref name="peerServices"/> as <paramref name="peerNodeId"/>.</summary>
    public void Connect(string peerNodeId, string peerName, IServiceProvider peerServices, string? grantId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(peerNodeId);
        ArgumentNullException.ThrowIfNull(peerServices);
        _peers[peerNodeId] = new Peer(peerNodeId, peerName, peerServices, grantId);
    }

    /// <summary>Forgets the peer: this device holds no grant for it any more (<c>AccessMissing</c>).</summary>
    public void Disconnect(string peerNodeId) => _peers.TryRemove(peerNodeId, out _);

    /// <summary>An unreachable peer answers nothing (<c>Unreachable</c>).</summary>
    public void SetReachable(string peerNodeId, bool reachable) => Require(peerNodeId).Reachable = reachable;

    /// <summary>The peer revoked this device's grant (<c>AccessRevoked</c>).</summary>
    public void Revoke(string peerNodeId) => Require(peerNodeId).Revoked = true;

    public Task<DataSyncPeerProbe> ProbeAsync(string peerNodeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Record(new InProcessPeerCall("probe", peerNodeId, null));
        if (!_peers.TryGetValue(peerNodeId, out var peer))
            return Task.FromResult(new DataSyncPeerProbe(peerNodeId, peerNodeId, null, false, null, null, null));
        return Task.FromResult(new DataSyncPeerProbe(peer.NodeId, peer.Name, Address(peer), !peer.Revoked,
            peer.Reachable ? DataSyncContract.Version : null, peer.Reachable ? true : null,
            peer.Reachable ? "online" : "offline"));
    }

    public Task<DataSyncFeedHead> GetHeadAsync(string peerNodeId, DataSyncFeedQuery query, CancellationToken ct) =>
        CallAsync("head", peerNodeId, query, async (source, reader) =>
            RoundTrip(await source.GetHeadAsync(reader, query, ct)), ct);

    public Task<DataSyncFeedManifest> GetManifestAsync(string peerNodeId, DataSyncFeedQuery query,
        CancellationToken ct) =>
        CallAsync("manifest", peerNodeId, query, async (source, reader) =>
            RoundTrip(await source.CreateSnapshotAsync(reader, query, ct)), ct);

    public Task<ReadOnlyMemory<byte>> GetPageAsync(string peerNodeId, string snapshotId, string kind, long sinceSeq,
        string? cursor, CancellationToken ct) =>
        CallAsync("page", peerNodeId, null, async (source, reader) =>
        {
            var page = await source.GetPageAsync(reader, snapshotId, kind, sinceSeq, cursor, ct);
            if (page.Length > FederationHttpClient.MaxControlResponseBytes)
                throw new DataSyncPeerException(DataSyncPeerErrorCode.TooLarge, "NodeResponseTooLarge");
            // A copy, as over the wire: the receiver never shares the source's precomputed page.
            return new ReadOnlyMemory<byte>(page.AsSpan().ToArray());
        }, ct, new InProcessPeerCall("page", peerNodeId, null, snapshotId, kind, sinceSeq, cursor));

    /// <summary>
    /// The federation peer client's mapping of a source's refusal (§7.6): <c>Busy</c> and <c>TooManySnapshots</c>
    /// become Busy with their retry advice, and so on; anything else by its status.
    /// </summary>
    public static DataSyncPeerException Map(DataSyncFeedException e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var code = e.Code switch
        {
            "Busy" or "TooManySnapshots" => DataSyncPeerErrorCode.Busy,
            "SnapshotTooLarge" => DataSyncPeerErrorCode.TooLarge,
            "SnapshotExpired" or "SnapshotMismatch" => DataSyncPeerErrorCode.SnapshotExpired,
            "CursorSuperseded" => DataSyncPeerErrorCode.CursorSuperseded,
            "SourceRestorePending" => DataSyncPeerErrorCode.PeerRestorePending,
            "DataSyncSharingDisabled" or "SharingDisabled" => DataSyncPeerErrorCode.PeerSharingOff,
            "RemoteAccessDisabled" => DataSyncPeerErrorCode.PeerRemoteAccessOff,
            "LibraryEpochChanged" or "IdentityConflict" => DataSyncPeerErrorCode.PeerReset,
            _ => e.Status switch
            {
                401 => DataSyncPeerErrorCode.AccessRevoked,
                403 => DataSyncPeerErrorCode.AccessMissing,
                413 => DataSyncPeerErrorCode.TooLarge,
                503 => DataSyncPeerErrorCode.Unreachable,
                _ => DataSyncPeerErrorCode.InvalidResponse,
            },
        };
        return new DataSyncPeerException(code, e.Code, e.RetryAfterSeconds);
    }

    /// <summary>How long a fetch, or a call outside one, waits for another fetch of the same peer (§7.6).</summary>
    public TimeSpan FetchWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The peer's fetch lock (§7.6), as the federation peer client holds it: from the head to the last page, the
    /// calls made in the holder's own flow go through, every other call and fetch of the peer waits for it, up to
    /// <see cref="FetchWait"/>, then gets <c>Busy</c>. Taken again in the same flow, it holds nothing more.
    /// </summary>
    /// <remarks>Not an async method: the hold is recorded in the caller's flow before this returns.</remarks>
    public Task<IAsyncDisposable> AcquireFetchAsync(string peerNodeId, CancellationToken ct)
    {
        var held = _held.Value ?? ImmutableDictionary.Create<string, FetchHold>(StringComparer.Ordinal);
        if (held.TryGetValue(peerNodeId, out var outer) && outer.IsHeld)
            return Task.FromResult<IAsyncDisposable>(FetchHold.Nested);
        var hold = new FetchHold(PeerLock(peerNodeId));
        _held.Value = held.RemoveRange(held.Where(p => p.Value.IsOver).Select(p => p.Key)).SetItem(peerNodeId, hold);
        return hold.AcquireAsync(FetchWait, ct);
    }

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _peerLocks = new(StringComparer.Ordinal);
    private readonly AsyncLocal<ImmutableDictionary<string, FetchHold>?> _held = new();

    private SemaphoreSlim PeerLock(string peerNodeId) => _peerLocks.GetOrAdd(peerNodeId, _ => new SemaphoreSlim(1, 1));

    private bool HoldsFetch(string peerNodeId) =>
        _held.Value is { } held && held.TryGetValue(peerNodeId, out var hold) && hold.IsHeld;

    private async Task<T> CallAsync<T>(string operation, string peerNodeId, DataSyncFeedQuery? query,
        Func<IDataSyncFeedSource, DataSyncReader, Task<T>> call, CancellationToken ct, InProcessPeerCall? recorded = null)
    {
        ct.ThrowIfCancellationRequested();
        // A call outside a fetch of the peer takes its lock for its own exchange, as over federation (§7.6).
        var gate = HoldsFetch(peerNodeId) ? null : PeerLock(peerNodeId);
        if (gate is not null && !await gate.WaitAsync(FetchWait, ct))
            throw new DataSyncPeerException(DataSyncPeerErrorCode.Busy, "fetchInProgress");
        try
        {
            return await ExchangeAsync(operation, peerNodeId, query, call, recorded);
        }
        finally
        {
            gate?.Release();
        }
    }

    private async Task<T> ExchangeAsync<T>(string operation, string peerNodeId, DataSyncFeedQuery? query,
        Func<IDataSyncFeedSource, DataSyncReader, Task<T>> call, InProcessPeerCall? recorded)
    {
        Record(recorded ?? new InProcessPeerCall(operation, peerNodeId, query));
        if (Fault?.Invoke(operation, peerNodeId) is { } fault) throw fault;
        if (!_peers.TryGetValue(peerNodeId, out var peer))
            throw new DataSyncPeerException(DataSyncPeerErrorCode.AccessMissing, "NodeNotAuthorized");
        if (!peer.Reachable) throw new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable, "NodeUnreachable");
        if (peer.Revoked) throw new DataSyncPeerException(DataSyncPeerErrorCode.AccessRevoked, "GrantRevoked");
        var self = Self ?? throw new InvalidOperationException($"{nameof(InProcessPeerClient)} has no {nameof(Self)}.");

        var source = peer.Services.GetRequiredService<IDataSyncFeedSource>();
        var reader = new DataSyncReader(self.NodeId, peer.GrantId ?? "inproc-" + self.NodeId, self.Name);
        try
        {
            return await call(source, reader);
        }
        catch (DataSyncFeedException e)
        {
            throw Map(e);
        }
    }

    /// <summary>What <c>FederationResult</c> and <c>ReadEnvelopeAsync</c> do to a head or manifest.</summary>
    private static T RoundTrip<T>(T value)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, FederationJson.Options),
                       FederationJson.Options) ??
                   throw new DataSyncPeerException(DataSyncPeerErrorCode.InvalidResponse, "empty");
        }
        catch (JsonException e)
        {
            throw new DataSyncPeerException(DataSyncPeerErrorCode.InvalidResponse, e.Message);
        }
    }

    private void Record(InProcessPeerCall call)
    {
        lock (_calls) _calls.Add(call);
    }

    private Peer Require(string peerNodeId) =>
        _peers.TryGetValue(peerNodeId, out var peer)
            ? peer
            : throw new InvalidOperationException($"{peerNodeId} is not connected.");

    private static string Address(Peer peer) => "inproc://" + peer.NodeId;

    /// <summary>One fetch's hold on a peer's lock: pending until taken, then held until released.</summary>
    private sealed class FetchHold(SemaphoreSlim peerLock) : IAsyncDisposable
    {
        private const int Pending = 0, Held = 1, Over = 2;

        /// <summary>Taken again by a flow that holds the peer: its outer hold covers it.</summary>
        public static readonly IAsyncDisposable Nested = new FetchHold(new SemaphoreSlim(0, 1)) { _state = Over };

        private int _state = Pending;

        public bool IsHeld => Volatile.Read(ref _state) == Held;
        public bool IsOver => Volatile.Read(ref _state) == Over;

        public async Task<IAsyncDisposable> AcquireAsync(TimeSpan wait, CancellationToken ct)
        {
            bool taken;
            try
            {
                taken = await peerLock.WaitAsync(wait, ct);
            }
            catch
            {
                Volatile.Write(ref _state, Over);
                throw;
            }

            if (!taken)
            {
                Volatile.Write(ref _state, Over);
                throw new DataSyncPeerException(DataSyncPeerErrorCode.Busy, "fetchInProgress");
            }

            Volatile.Write(ref _state, Held);
            return this;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _state, Over) == Held) peerLock.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Peer(string nodeId, string name, IServiceProvider services, string? grantId)
    {
        public string NodeId { get; } = nodeId;
        public string Name { get; } = name;
        public IServiceProvider Services { get; } = services;
        public string? GrantId { get; } = grantId;
        public volatile bool Reachable = true;
        public volatile bool Revoked;
    }
}

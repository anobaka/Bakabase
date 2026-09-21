using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Queries;

namespace Bakabase.Modules.Federation.Tests.Queries;

internal sealed class ManualClock : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
    public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
}

internal sealed class MemoryReader(string node, params string?[] titles) : ILocalLibraryReader
{
    public string Node { get; } = node;
    public string Epoch { get; set; } = "epoch-" + node;
    public List<LocalResourceProjection> Rows { get; set; } = titles.Select((title, i) =>
        new LocalResourceProjection(i + 1, title, null, false, [])).ToList();

    public Task<LocalLibraryCapture> CaptureAsync(CaptureBudget budget, CancellationToken cancellationToken)
    {
        foreach (var row in Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            budget.Charge(128);
            budget.ChargeString(row.EffectiveName);
        }
        return Task.FromResult(new LocalLibraryCapture(Node, Epoch, Node, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, Rows.ToArray()));
    }
}

internal sealed class MemoryAccess(MemoryReader reader) : IFederationQueryAccess
{
    public bool Revoked { get; set; }
    public long Version { get; set; } = 1;
    public Task<QueryAccess> ValidateAsync(string grantId, string expectedLibraryEpoch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Revoked) throw new FederationQueryException("GrantRevoked", 401);
        if (expectedLibraryEpoch != reader.Epoch) throw new FederationQueryException("LibraryEpochChanged", 409);
        return Task.FromResult(new QueryAccess(reader.Node, reader.Epoch, grantId, Version));
    }
}

internal sealed class TestPeer : IDisposable
{
    public readonly MemoryReader Reader;
    public readonly MemoryAccess Access;
    public readonly LocalSearchSnapshotService Store;
    public readonly FaultClient Client;
    public TestPeer(string node, FederationQueryLimits limits, TimeProvider? clock, params string?[] titles)
    {
        Reader = new(node, titles);
        Access = new(Reader);
        Store = new(Reader, Access, limits, clock);
        Client = new(new LocalPeerSearchClient(Store, "grant"));
    }
    public PeerSearchTarget Target => new(Reader.Node, Reader.Epoch, Client);
    public void Dispose() => Store.Dispose();
}

internal sealed class FaultClient(IPeerSearchClient inner) : IPeerSearchClient
{
    public int ReadCalls;
    public int ReleaseCalls;
    public int? FailReadCall;
    public bool FailCreate;
    public TimeSpan CreationDelay;
    public Task? CreationGate;
    public int? BlockSizeOverride;
    public bool IgnoreCreateCancellation;
    public Action? AfterCreate;
    public Action? AfterRelease;
    public async Task<NodeQueryBlock> CreateAsync(NodeExportQuery query, CancellationToken cancellationToken)
    {
        if (IgnoreCreateCancellation) cancellationToken = CancellationToken.None;
        if (CreationGate != null) await CreationGate.WaitAsync(cancellationToken);
        if (CreationDelay > TimeSpan.Zero) await Task.Delay(CreationDelay, cancellationToken);
        if (FailCreate) throw new HttpRequestException("offline");
        var block = await inner.CreateAsync(BlockSizeOverride.HasValue ? query with { BlockSize = BlockSizeOverride.Value } : query, cancellationToken);
        AfterCreate?.Invoke();
        return block;
    }
    public Task<NodeQueryBlock> ReadAsync(string snapshotId, string cursor, CancellationToken cancellationToken)
    {
        if (++ReadCalls == FailReadCall) throw new HttpRequestException("lost block");
        return inner.ReadAsync(snapshotId, cursor, cancellationToken);
    }
    public Task ValidateAsync(string snapshotId, CancellationToken cancellationToken) => inner.ValidateAsync(snapshotId, cancellationToken);
    public async Task ReleaseAsync(string snapshotId, CancellationToken cancellationToken)
    {
        ReleaseCalls++;
        await inner.ReleaseAsync(snapshotId, cancellationToken);
        AfterRelease?.Invoke();
    }
}

internal sealed class FixedTargets(params TestPeer[] peers) : IPeerSearchTargetResolver
{
    public Task<PeerSearchTarget> ResolveAsync(string nodeId, CancellationToken cancellationToken) =>
        Task.FromResult(peers.Single(p => p.Reader.Node == nodeId).Target);
}

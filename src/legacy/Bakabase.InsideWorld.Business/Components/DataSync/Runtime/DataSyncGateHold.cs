using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// A request's hold on the DataSyncGate (§10.1). A request waits for the gate at most
/// <see cref="RequestTimeout"/> and otherwise answers <c>Busy</c> before it changes anything. Network calls to peers
/// are never made while holding the gate, so <see cref="OutsideAsync{T}"/> releases it around one and then waits for it
/// again, without a limit: what the peer answered is written under the gate, and an answer already given is never
/// turned into <c>Busy</c>.
/// </summary>
public sealed class DataSyncGateHold : IAsyncDisposable
{
    /// <summary>How long an HTTP caller waits for the gate (§10.1).</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly IDataSyncGateEntry _entry;
    private DataSyncGateLease? _lease;

    private DataSyncGateHold(IDataSyncGateEntry entry, DataSyncGateLease lease)
    {
        _entry = entry;
        _lease = lease;
    }

    /// <summary>The lease while the gate is held; the actor check and Refresh take it (§5.6, §6.6).</summary>
    public DataSyncGateLease Lease => _lease ?? throw new InvalidOperationException("The gate is not held.");

    /// <summary>A hold, or null when the gate was not free within <paramref name="timeout"/> (null: no limit).</summary>
    public static async Task<DataSyncGateHold?> TryEnterAsync(IDataSyncGateEntry entry, TimeSpan? timeout,
        CancellationToken ct)
    {
        var lease = await entry.TryEnterAsync(timeout, ct);
        return lease is null ? null : new DataSyncGateHold(entry, lease);
    }

    /// <summary>Runs a network call with the gate released, and holds it again before returning.</summary>
    public async Task<T> OutsideAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        _lease?.Dispose();
        _lease = null;
        try
        {
            return await call();
        }
        finally
        {
            _lease = await _entry.TryEnterAsync(null, ct);
        }
    }

    public ValueTask DisposeAsync()
    {
        _lease?.Dispose();
        _lease = null;
        return ValueTask.CompletedTask;
    }
}

public static class DataSyncGateHoldExtensions
{
    /// <summary>A network call made through a hold releases the gate around it; without one it just runs.</summary>
    public static Task<T> OutsideGateAsync<T>(this DataSyncGateHold? hold, Func<Task<T>> call, CancellationToken ct) =>
        hold is null ? call() : hold.OutsideAsync(call, ct);
}

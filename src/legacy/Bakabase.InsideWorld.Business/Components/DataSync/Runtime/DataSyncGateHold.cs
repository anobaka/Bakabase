using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// A request's hold on the DataSyncGate (§10.1). A request waits for the gate at most
/// <see cref="RequestTimeout"/> and otherwise answers <c>Busy</c> before it changes anything. Network calls to peers
/// are never made while holding the gate: the actions that make them are not gated.
/// </summary>
public sealed class DataSyncGateHold : IAsyncDisposable
{
    /// <summary>How long an HTTP caller waits for the gate (§10.1).</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private DataSyncGateLease? _lease;

    private DataSyncGateHold(DataSyncGateLease lease)
    {
        _lease = lease;
    }

    /// <summary>The lease while the gate is held; the actor check and Refresh take it (§5.6, §6.6).</summary>
    public DataSyncGateLease Lease => _lease ?? throw new InvalidOperationException("The gate is not held.");

    /// <summary>A hold, or null when the gate was not free within <paramref name="timeout"/> (null: no limit).</summary>
    public static async Task<DataSyncGateHold?> TryEnterAsync(IDataSyncGateEntry entry, TimeSpan? timeout,
        CancellationToken ct)
    {
        var lease = await entry.TryEnterAsync(timeout, ct);
        return lease is null ? null : new DataSyncGateHold(lease);
    }

    public ValueTask DisposeAsync()
    {
        _lease?.Dispose();
        _lease = null;
        return ValueTask.CompletedTask;
    }
}

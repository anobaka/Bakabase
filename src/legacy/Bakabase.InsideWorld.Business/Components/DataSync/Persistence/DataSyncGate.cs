using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// The process-wide gate every data sync reader and writer of local state holds (v3.1 §2.7, spec §2.9): Refresh,
/// the feed's snapshots, every apply, undo, resolution and restore, and the entity settings. A singleton
/// <see cref="SemaphoreSlim"/>(1, 1), so it is <b>not</b> reentrant: code that already holds a lease passes it on
/// (Refresh takes the caller's lease and never enters the gate itself).
/// </summary>
/// <remarks>
/// It is also the runtime's <see cref="IDataSyncGateEntry"/>, so the facade, the apply runner and the feed share this
/// one gate (§10.1).
/// </remarks>
public sealed class DataSyncGate : IDataSyncGateEntry
{
    /// <summary>How long an HTTP caller or a feed request waits before it answers Busy (§7.5.1, §10.1).</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>True while any caller holds a lease (diagnostics and tests; never a reason to skip waiting).</summary>
    public bool IsHeld => _semaphore.CurrentCount == 0;

    /// <summary>
    /// Waits for the gate. <paramref name="timeout"/>: HTTP callers and the feed pass <see cref="RequestTimeout"/>
    /// and map <see cref="DataSyncGateTimeoutException"/> to Busy; BTask bodies pass null and wait without a limit.
    /// A cancelled wait throws <see cref="OperationCanceledException"/> and holds nothing.
    /// </summary>
    public async Task<DataSyncGateLease> EnterAsync(TimeSpan? timeout, CancellationToken ct)
    {
        if (timeout is { } limit)
        {
            if (!await _semaphore.WaitAsync(limit, ct)) throw new DataSyncGateTimeoutException(limit);
        }
        else
        {
            await _semaphore.WaitAsync(ct);
        }

        return new Lease(this);
    }

    /// <summary>A lease, or null when the gate was not free within <paramref name="timeout"/> (null: no limit).</summary>
    public async Task<DataSyncGateLease?> TryEnterAsync(TimeSpan? timeout, CancellationToken ct)
    {
        try
        {
            return await EnterAsync(timeout, ct);
        }
        catch (DataSyncGateTimeoutException)
        {
            return null;
        }
    }

    private sealed class Lease(DataSyncGate gate) : DataSyncGateLease
    {
        private int _released;

        public override bool IsHeld => Volatile.Read(ref _released) == 0;

        /// <summary>Releases the gate once; disposing again does nothing.</summary>
        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate._semaphore.Release();
        }
    }
}

/// <summary>The gate was not free within the caller's limit: the caller answers Busy (retryable).</summary>
public sealed class DataSyncGateTimeoutException(TimeSpan waited)
    : TimeoutException($"Data sync is busy: the gate was not free within {waited.TotalSeconds:0.#} s.")
{
    public TimeSpan Waited { get; } = waited;
}

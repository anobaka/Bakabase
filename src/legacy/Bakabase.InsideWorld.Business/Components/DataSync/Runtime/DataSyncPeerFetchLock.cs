using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// One fetch per peer at a time (§7.6): the fetch cycle, review staging, copy once and "Fetch again" hold it from
/// the head to the last page, so a second fetch can never make the source discard the snapshot the first one is
/// reading (one snapshot per reader grant). A second caller waits up to <see cref="Wait"/> and then gets
/// <see cref="DataSyncPeerErrorCode.Busy"/>.
/// </summary>
public sealed class DataSyncPeerFetchLock
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string peerNodeId, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(peerNodeId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(Wait, ct)) throw new DataSyncPeerException(DataSyncPeerErrorCode.Busy, "fetchInProgress");
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}

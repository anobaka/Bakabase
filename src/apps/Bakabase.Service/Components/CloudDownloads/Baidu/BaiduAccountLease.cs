using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

/// <summary>FIFO account leases let transfers and downloads read the profile while keeping profile changes exclusive.</summary>
internal sealed class BaiduAccountLease
{
    private readonly object _sync = new();
    private readonly LinkedList<Waiter> _waiting = new();
    private int _readers;
    private bool _writer;

    public async Task<IDisposable> AcquireAsync(bool exclusive, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Waiter waiter;
        lock (_sync)
        {
            if (!_writer && _waiting.Count == 0 && (!exclusive || _readers == 0))
                return Grant(exclusive);
            waiter = new Waiter(exclusive);
            waiter.Node = _waiting.AddLast(waiter);
        }
        using var registration = ct.Register(() =>
        {
            lock (_sync)
            {
                if (waiter.Node?.List == null) return;
                _waiting.Remove(waiter.Node);
                waiter.Completion.TrySetCanceled(ct);
                Drain();
            }
        });
        return await waiter.Completion.Task;
    }

    private IDisposable Grant(bool exclusive)
    {
        if (exclusive) _writer = true;
        else _readers++;
        return new Lease(this, exclusive);
    }

    private void Release(bool exclusive)
    {
        lock (_sync)
        {
            if (exclusive) _writer = false;
            else _readers--;
            Drain();
        }
    }

    private void Drain()
    {
        if (_writer) return;
        while (_waiting.First is { } next)
        {
            if (next.Value.Exclusive && _readers > 0) return;
            _waiting.RemoveFirst();
            next.Value.Completion.SetResult(Grant(next.Value.Exclusive));
            if (next.Value.Exclusive) return;
        }
    }

    private sealed class Waiter(bool exclusive)
    {
        public bool Exclusive { get; } = exclusive;
        public TaskCompletionSource<IDisposable> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter>? Node { get; set; }
    }

    private sealed class Lease(BaiduAccountLease owner, bool exclusive) : IDisposable
    {
        private BaiduAccountLease? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(exclusive);
    }
}

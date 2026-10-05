namespace Bakabase.Abstractions.Components.Threading;

/// <summary>Bounds active work while allowing limits to change without replacing waiting semaphores.</summary>
public sealed class AsyncConcurrencyGate(Func<int> getCapacity)
{
    private static readonly TimeSpan ConfigurationRefreshInterval = TimeSpan.FromMilliseconds(250);
    private readonly object _sync = new();
    private int _active;
    private TaskCompletionSource _changed = NewSignal();

    public async Task<IDisposable> EnterAsync(CancellationToken ct = default, Func<Task>? onWaiting = null)
    {
        var reportedWaiting = false;
        while (true)
        {
            Task changed;
            lock (_sync)
            {
                ct.ThrowIfCancellationRequested();
                if (_active < Math.Max(1, getCapacity()))
                {
                    _active++;
                    return new Lease(this);
                }
                changed = _changed.Task;
            }

            if (!reportedWaiting && onWaiting != null)
            {
                reportedWaiting = true;
                await onWaiting().ConfigureAwait(false);
            }
            try
            {
                // Releases wake immediately. A bounded refresh also observes a host's live options
                // delegate when it cannot supply change notifications; no waiter is stored or leaked.
                await changed.WaitAsync(ConfigurationRefreshInterval, ct).ConfigureAwait(false);
            }
            catch (TimeoutException) { }
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Exit()
    {
        lock (_sync)
        {
            _active--;
            var changed = _changed;
            _changed = NewSignal();
            changed.TrySetResult();
        }
    }

    private sealed class Lease(AsyncConcurrencyGate owner) : IDisposable
    {
        private AsyncConcurrencyGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}

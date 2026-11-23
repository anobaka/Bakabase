using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

/// <summary>
/// The AppData owner is one process. Normal registrations/consumer mutations share this fence;
/// reclamation excludes them from its last reference check until its filesystem changes finish.
/// Lease heartbeats do not need the fence and keep already-running producers alive.
/// </summary>
internal sealed class ArtifactMaintenanceGate
{
    private static readonly ConcurrentDictionary<string, ArtifactMaintenanceGate> Gates = new(ArtifactManifestFiles.PathComparer);
    public static ArtifactMaintenanceGate For(string appData) => Gates.GetOrAdd(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(appData)), _ => new());
    private readonly SemaphoreSlim _maintenanceOwner = new(1, 1);
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly SemaphoreSlim _roomEmpty = new(1, 1);
    private readonly SemaphoreSlim _readerMutex = new(1, 1);
    private int _readers;
    private readonly HashSet<Mutation> _admitted = [];
    private TaskCompletionSource? _scopeChanged;
    private bool _publishedFileRemoval;

    private void Changed()
    {
        _scopeChanged?.TrySetResult();
        _scopeChanged = null;
    }

    private Task ChangedTask() => (_scopeChanged ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    private int _waitingMutations;

    public async Task<Mutation> EnterMutationAsync(CancellationToken ct)
    {
        var mutation = new Mutation(this);
        Interlocked.Increment(ref _waitingMutations);
        try
        {
            await _turnstile.WaitAsync(ct);
            try
            {
                await _readerMutex.WaitAsync(ct);
                try
                {
                    if (_readers == 0) await _roomEmpty.WaitAsync(ct);
                    _readers++;
                    lock (_admitted) _admitted.Add(mutation);
                }
                finally { _readerMutex.Release(); }
            }
            finally { _turnstile.Release(); }
        }
        finally { Interlocked.Decrement(ref _waitingMutations); }
        return mutation;
    }

    private async Task EnterExclusiveAsync(CancellationToken ct)
    {
        await _turnstile.WaitAsync(ct);
        try { await _roomEmpty.WaitAsync(ct); }
        catch { _turnstile.Release(); throw; }
    }

    public async Task<IAsyncDisposable> EnterMaintenanceAsync(CancellationToken ct)
    {
        await _maintenanceOwner.WaitAsync(ct);
        try { await EnterExclusiveAsync(ct); }
        catch { _maintenanceOwner.Release(); throw; }
        return new Maintenance(this);
    }

    // Close admission before considering any exception. Newly arriving writers (including
    // parent/consumer attachment) must wait for the normal reference check and deletion.
    internal async Task<IAsyncDisposable> EnterPublishedFileRemovalAsync(ArtifactVerifiedFileScope scope, CancellationToken ct)
    {
        await _maintenanceOwner.WaitAsync(ct);
        try
        {
            await _turnstile.WaitAsync(ct);
            try
            {
                while (true)
                {
                    Task changed;
                    lock (_admitted)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (_admitted.All(m => m.Reading is {} reading && reading.IsIndependentOf(scope)))
                        {
                            _publishedFileRemoval = true;
                            return new PublishedFileRemoval(this);
                        }
                        changed = ChangedTask();
                    }
                    await changed.WaitAsync(ct);
                }
            }
            catch { _turnstile.Release(); throw; }
        }
        catch { _maintenanceOwner.Release(); throw; }
    }

    private sealed class PublishedFileRemoval(ArtifactMaintenanceGate owner) : IAsyncDisposable
    {
        private int _released;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return ValueTask.CompletedTask;
            lock (owner._admitted)
            {
                owner._publishedFileRemoval = false;
                owner.Changed();
            }
            owner._turnstile.Release();
            owner._maintenanceOwner.Release();
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class Mutation(ArtifactMaintenanceGate owner) : IAsyncDisposable
    {
        private int _released;
        internal ArtifactVerifiedFileScope? Reading { get; private set; }

        internal void BeginVerifiedRead(ArtifactVerifiedFileScope scope)
        {
            lock (owner._admitted)
            {
                Reading = scope;
                owner.Changed();
            }
        }

        // Keep protecting the same paths while an admitted independent deletion finishes.
        // No registration, reuse, parent read or CAS can run until general protection resumes.
        internal async Task EndVerifiedReadAsync()
        {
            while (true)
            {
                Task changed;
                lock (owner._admitted)
                {
                    // Announce the transition now so a second cleanup cannot overtake
                    // a verification already waiting to publish its result.
                    if (Reading != null)
                    {
                        Reading = null;
                        owner.Changed();
                    }
                    if (!owner._publishedFileRemoval) return;
                    changed = owner.ChangedTask();
                }
                await changed;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            await owner._readerMutex.WaitAsync();
            try
            {
                lock (owner._admitted)
                {
                    owner._admitted.Remove(this);
                    owner.Changed();
                }
                if (--owner._readers == 0) owner._roomEmpty.Release();
            }
            finally { owner._readerMutex.Release(); }
        }
    }

    // The one maintenance owner calls yield/dispose sequentially. It retains its writer
    // position amongst other cleanups, including if cancellation interrupts reacquisition.
    private sealed class Maintenance(ArtifactMaintenanceGate owner) : IArtifactMaintenanceHandoff
    {
        private bool _exclusive = true;
        private bool _disposed;

        public async Task<bool> YieldToWaitingMutationsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_disposed || !_exclusive) throw new InvalidOperationException("The maintenance fence is not held.");
            if (Volatile.Read(ref owner._waitingMutations) == 0) return false;
            LeaveExclusive();
            await owner.EnterExclusiveAsync(ct);
            _exclusive = true;
            return true;
        }

        private void LeaveExclusive()
        {
            _exclusive = false;
            owner._roomEmpty.Release();
            owner._turnstile.Release();
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            if (_exclusive) LeaveExclusive();
            owner._maintenanceOwner.Release();
            return ValueTask.CompletedTask;
        }
    }
}

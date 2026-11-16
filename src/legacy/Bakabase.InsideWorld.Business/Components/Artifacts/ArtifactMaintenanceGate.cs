using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly SemaphoreSlim _roomEmpty = new(1, 1);
    private readonly SemaphoreSlim _readerMutex = new(1, 1);
    private int _readers;

    public async Task<IAsyncDisposable> EnterMutationAsync(CancellationToken ct)
    {
        await _turnstile.WaitAsync(ct);
        _turnstile.Release();
        await _readerMutex.WaitAsync(ct);
        try
        {
            if (_readers == 0) await _roomEmpty.WaitAsync(ct);
            _readers++;
        }
        finally { _readerMutex.Release(); }
        return new Lease(this, false);
    }

    public async Task<IAsyncDisposable> EnterMaintenanceAsync(CancellationToken ct)
    {
        await _turnstile.WaitAsync(ct);
        try { await _roomEmpty.WaitAsync(ct); }
        catch { _turnstile.Release(); throw; }
        return new Lease(this, true);
    }

    private sealed class Lease(ArtifactMaintenanceGate owner, bool writer) : IAsyncDisposable
    {
        private int _released;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            if (writer) { owner._roomEmpty.Release(); owner._turnstile.Release(); return; }
            await owner._readerMutex.WaitAsync();
            try { if (--owner._readers == 0) owner._roomEmpty.Release(); }
            finally { owner._readerMutex.Release(); }
        }
    }
}

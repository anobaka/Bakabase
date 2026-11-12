using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRecoveryDiagnosisCancellation(string DiagnosisId, string Status);

/// <summary>Cancellation acknowledges the end of a read-only diagnostic request, never the end
/// of a workflow. Retained tombstones also close the cancel-before-request-registration race.</summary>
public sealed class AcquisitionRecoveryDiagnosisSessions
{
    private sealed class Session
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;
        internal bool Registered;
    }
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly object _gate = new();

    public Lease Begin(string id, CancellationToken request)
    {
        RequireId(id);
        lock (_gate)
        {
            Prune();
            var state = GetOrAdd(id);
            if (state.Registered || state.Cancellation.IsCancellationRequested)
                throw new ArgumentException("RECOVERY_DIAGNOSIS_CANCELLED");
            state.Registered = true;
            return new(CancellationTokenSource.CreateLinkedTokenSource(request, state.Cancellation.Token),
                () => state.Finished.TrySetResult());
        }
    }

    public async Task<AcquisitionRecoveryDiagnosisCancellation> CancelAsync(string id, CancellationToken ct)
    {
        RequireId(id);
        Session state;
        lock (_gate)
        {
            Prune();
            state = GetOrAdd(id);
            state.Cancellation.Cancel();
            if (!state.Registered) state.Finished.TrySetResult();
        }
        // In particular, a synchronous blocked filesystem call has NOT stopped until its
        // owning request finally exits. Aborting this wait does not revoke cancellation.
        await state.Finished.Task.WaitAsync(ct);
        return new(id, "stopped");
    }

    private void Prune()
    {
        foreach (var pair in _sessions.Where(p => p.Value.Finished.Task.IsCompleted &&
                     p.Value.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-30)))
            if (_sessions.TryRemove(pair.Key, out var removed)) removed.Cancellation.Dispose();
    }
    private Session GetOrAdd(string id)
    {
        if (_sessions.TryGetValue(id, out var found)) return found;
        if (_sessions.Count >= 128) throw new ArgumentException("RECOVERY_PREVIEW_LIMIT");
        return _sessions.GetOrAdd(id, _ => new());
    }
    private static void RequireId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("RECOVERY_DIAGNOSIS_INVALID");
    }
    public sealed class Lease(CancellationTokenSource cancellation, Action completed) : IDisposable
    {
        public CancellationToken Token => cancellation.Token;
        public void Dispose() { cancellation.Dispose(); completed(); }
    }
}

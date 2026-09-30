using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>What a local change did: the pause the actor check caused, and the Refresh that followed it.</summary>
public sealed record DataSyncLocalChangeResult(DataSyncPauseReason? Pause, DataSyncRefreshResult Refresh);

/// <summary>
/// When Refresh runs outside an apply (§6.6). Every path enters the gate, runs the actor check before any transaction
/// (§5.6), refreshes and writes <c>actor.json</c> after the commit. A Refresh that finds the actor changed (an
/// identity reset raced the call) throws <see cref="DataSyncActorChangedException"/>, and the caller answers Busy.
/// </summary>
/// <remarks>
/// Inside an apply, review, resolution, undo or restore, Refresh runs first in the runner's own transaction instead;
/// a snapshot refreshes with <c>collectPublished</c> under the gate it already holds. Never while the actor is
/// unverified: Refresh is then skipped and writes nothing.
/// </remarks>
public sealed class DataSyncRefreshCoordinator
{
    /// <summary>A head is answered from a Refresh at most this old (§6.6, §7.5.1).</summary>
    public static readonly TimeSpan HeadRefreshMaxAge = TimeSpan.FromSeconds(5);

    /// <summary>How often <see cref="CloseLostUpdateWindowsAsync"/> looks whether a window has closed.</summary>
    public static readonly TimeSpan WindowCheckInterval = TimeSpan.FromSeconds(10);

    private DateTimeOffset _windowsCheckedAt = DateTimeOffset.MinValue;

    private readonly DataSyncGate _gate;
    private readonly IDataSyncActorGuard _guard;
    private readonly IServiceScopeFactory _scopes;
    private readonly DataSyncActorWatermarkFile _watermark;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _headLock = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _refreshedAt = new(StringComparer.Ordinal);

    public DataSyncRefreshCoordinator(DataSyncGate gate, IDataSyncActorGuard guard, IServiceScopeFactory scopes,
        DataSyncActorWatermarkFile watermark, TimeProvider? time = null)
    {
        _gate = gate;
        _guard = guard;
        _scopes = scopes;
        _watermark = watermark;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// For a head (§6.6): makes sure the kinds were refreshed at most <see cref="HeadRefreshMaxAge"/> ago. Concurrent
    /// heads share one Refresh; the wait for it and for the gate is at most <see cref="DataSyncGate.RequestTimeout"/>
    /// in total, then <see cref="DataSyncGateTimeoutException"/> (the feed answers 503 Busy, retryable). Returns the
    /// Refresh it ran, or null when a recent one covered the kinds.
    /// </summary>
    public async Task<DataSyncRefreshResult?> EnsureRecentAsync(IReadOnlyCollection<string> kinds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var deadline = _time.GetUtcNow() + DataSyncGate.RequestTimeout;
        if (!await _headLock.WaitAsync(DataSyncGate.RequestTimeout, ct))
            throw new DataSyncGateTimeoutException(DataSyncGate.RequestTimeout);
        try
        {
            // Evidence waiting (the reader-ahead check of this head, say) is handled by this head's own check.
            if (IsRecent(kinds) && !_guard.HasPendingEvidence) return null;
            var remaining = deadline - _time.GetUtcNow();
            if (remaining <= TimeSpan.Zero) throw new DataSyncGateTimeoutException(DataSyncGate.RequestTimeout);
            using var lease = await _gate.EnterAsync(remaining, ct);
            return await CheckAndRefreshAsync(lease, kinds, ct);
        }
        finally
        {
            _headLock.Release();
        }
    }

    /// <summary>
    /// A local change, such as an entity setting (§6.6, §10.1): under the gate (at most
    /// <see cref="DataSyncGate.RequestTimeout"/>), after the actor check, in one short transaction: the change, then
    /// Refresh of <paramref name="kinds"/>, so the change and the revision it makes commit together. No task runs.
    /// <paramref name="change"/> receives the transaction's scope.
    /// </summary>
    /// <remarks>
    /// Refresh joins this transaction, so committing is this method's job. A Refresh that was skipped (the actor
    /// unverified from the start) issued nothing: the change commits alone and the first Refresh after verification
    /// publishes it.
    /// </remarks>
    /// <exception cref="DataSyncActorChangedException">An identity reset raced the change: nothing was written.</exception>
    public async Task<DataSyncLocalChangeResult> RunLocalChangeAsync(IReadOnlyCollection<string> kinds,
        Func<IServiceProvider, CancellationToken, Task> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(change);
        using var lease = await _gate.EnterAsync(DataSyncGate.RequestTimeout, ct);
        return await RunLocalChangeAsync(lease, kinds, change, ct);
    }

    /// <summary>
    /// <see cref="RunLocalChangeAsync(IReadOnlyCollection{string},Func{IServiceProvider,CancellationToken,Task},CancellationToken)"/>
    /// under a gate the caller already holds: a facade call that entered it (§10.1), such as an entity setting.
    /// </summary>
    /// <exception cref="DataSyncActorChangedException">An identity reset raced the change: nothing was written.</exception>
    public async Task<DataSyncLocalChangeResult> RunLocalChangeAsync(DataSyncGateLease lease,
        IReadOnlyCollection<string> kinds, Func<IServiceProvider, CancellationToken, Task> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(change);
        if (!lease.IsHeld) throw new InvalidOperationException("A local change runs under the data sync gate (§6.6).");
        var pause = await _guard.CheckAsync(lease, ct);
        await using var scope = _scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<DataSyncStore>();
        await using var transaction = await store.Db.Database.BeginTransactionAsync(ct);
        await change(services, ct);
        var refresh = await services.GetRequiredService<DataSyncRefresher>().RefreshAsync(lease, kinds, false, ct);
        await transaction.CommitAsync(ct);
        if (!refresh.Skipped)
        {
            // Committed: actor.json follows whatever a stop requested meanwhile (§5.6).
            await _watermark.WriteAsync((await store.GetLocalStateAsync(CancellationToken.None))!,
                CancellationToken.None);
            NoteRefreshed(kinds);
        }

        return new DataSyncLocalChangeResult(pause, refresh);
    }

    /// <summary>
    /// Closes the lost-update guard's window (§6.5) once it has passed: when the most recent guarded apply is older
    /// than <see cref="DataSyncLostUpdateGuard.Window"/> and a kind's last committed Refresh began before its window closed,
    /// that kind is refreshed now. Refresh judges a change against every apply of the window before the kind's last
    /// Refresh (the change came after it), so without this a person's deliberate revert long after a sync would be
    /// asked about on a device whose content no Refresh looked at meanwhile — one without readers, say. A stale write
    /// the window still covers is held by this Refresh like by any other. Run by the scheduler's tick; it never waits
    /// for the gate (a busy gate is tried again on the next tick) and never runs while the actor is unverified.
    /// </summary>
    /// <returns>The Refresh it ran, or null when nothing was due or the gate was busy.</returns>
    public async Task<DataSyncRefreshResult?> CloseLostUpdateWindowsAsync(CancellationToken ct)
    {
        if (!_guard.IsVerified) return null;
        var now = _time.GetUtcNow();
        lock (_refreshedAt)
        {
            // Looked at no more than every few seconds: the window closes to the minute, not to the tick.
            if (now - _windowsCheckedAt < WindowCheckInterval) return null;
            _windowsCheckedAt = now;
        }

        IReadOnlyList<string> kinds;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<DataSyncStore>();
            if (await store.GetLocalStateAsync(ct) is not { } state) return null;
            var latest = await DataSyncLostUpdateGuard.LatestGuardedApplyAsync(store.Db, null, ct);
            if (latest is not { } appliedAt) return null;
            var closesAt = appliedAt + DataSyncLostUpdateGuard.Window;
            if (now.UtcDateTime <= closesAt) return null;
            kinds = DataSyncStoredJson.ReadCounters(state.RefreshedAtJson, "RefreshedAtJson")
                .Where(k => store.Kinds.ContainsKey(k.Key) && DataSyncLostUpdateGuard.FromUnixMs(k.Value) <= closesAt)
                .Select(k => k.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        if (kinds.Count == 0) return null;
        using var lease = await _gate.TryEnterAsync(TimeSpan.Zero, ct);
        return lease is null ? null : await CheckAndRefreshAsync(lease, kinds, ct);
    }

    /// <summary>Another path (a snapshot, an apply) committed a Refresh of <paramref name="kinds"/> just now.</summary>
    public void NoteRefreshed(IEnumerable<string> kinds)
    {
        var now = _time.GetUtcNow();
        lock (_refreshedAt)
        {
            foreach (var kind in kinds) _refreshedAt[kind] = now;
        }
    }

    private bool IsRecent(IReadOnlyCollection<string> kinds)
    {
        var now = _time.GetUtcNow();
        lock (_refreshedAt)
        {
            return kinds.All(k => _refreshedAt.TryGetValue(k, out var at) && now - at <= HeadRefreshMaxAge);
        }
    }

    /// <summary>The actor check, then Refresh in its own scope and transaction (§5.6).</summary>
    private async Task<DataSyncRefreshResult> CheckAndRefreshAsync(DataSyncGateLease lease,
        IReadOnlyCollection<string> kinds, CancellationToken ct)
    {
        await _guard.CheckAsync(lease, ct);
        await using var scope = _scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<DataSyncRefresher>()
            .RefreshAsync(lease, kinds, false, ct);
        if (!result.Skipped) NoteRefreshed(kinds);
        return result;
    }
}

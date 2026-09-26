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
/// (§5.6), refreshes, writes <c>actor.json</c> after the commit, and retries once when Refresh finds the actor
/// changed (roll back, check again).
/// </summary>
/// <remarks>
/// Inside an apply, review, resolution, undo or restore, Refresh runs first in the runner's own transaction instead;
/// a snapshot refreshes with <c>collectPublished</c> under the gate it already holds. Never while the actor is
/// unverified: Refresh is then skipped and writes nothing.
/// </remarks>
public sealed class DataSyncRefreshCoordinator : IDataSyncLocalChangeRunner
{
    /// <summary>A head is answered from a Refresh at most this old (§6.6, §7.5.1).</summary>
    public static readonly TimeSpan HeadRefreshMaxAge = TimeSpan.FromSeconds(5);

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
            if (IsRecent(kinds)) return null;
            var remaining = deadline - _time.GetUtcNow();
            if (remaining <= TimeSpan.Zero) throw new DataSyncGateTimeoutException(DataSyncGate.RequestTimeout);
            using var lease = await _gate.EnterAsync(remaining, ct);
            await _guard.CheckAsync(lease, ct);
            return await RetryOnceAsync(lease, async (sp, _) =>
                await sp.GetRequiredService<DataSyncRefresher>().RefreshAsync(lease, kinds, false, ct), kinds, ct);
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
    /// <paramref name="change"/> receives the transaction's scope; it may run again when an attempt met a changed
    /// actor, or evidence of a restore arrived while its Refresh ran, since that attempt is rolled back.
    /// </summary>
    /// <remarks>
    /// Refresh joins this transaction, so committing is this method's job, and it commits only while the actor is
    /// still verified (§5.6): counters issued under an actor that pending evidence is about to retire never stand.
    /// A Refresh that was skipped (the actor unverified from the start) issued nothing: the change commits alone and
    /// the first Refresh after verification publishes it.
    /// </remarks>
    /// <exception cref="DataSyncActorUnverifiedException">Evidence kept arriving during every attempt.</exception>
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
    /// <exception cref="DataSyncActorUnverifiedException">Evidence kept arriving during every attempt.</exception>
    public async Task<DataSyncLocalChangeResult> RunLocalChangeAsync(DataSyncGateLease lease,
        IReadOnlyCollection<string> kinds, Func<IServiceProvider, CancellationToken, Task> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(change);
        if (!lease.IsHeld) throw new InvalidOperationException("A local change runs under the data sync gate (§6.6).");
        var pause = await _guard.CheckAsync(lease, ct);
        for (var attempt = 0;; attempt++)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var store = services.GetRequiredService<DataSyncStore>();
            await using var transaction = await store.Db.Database.BeginTransactionAsync(ct);
            try
            {
                await change(services, ct);
                var refresh = await services.GetRequiredService<DataSyncRefresher>().RefreshAsync(lease, kinds, false, ct);
                if (!refresh.Skipped && !_guard.IsVerified)
                {
                    // Evidence arrived while Refresh ran: what it issued may reuse counters a peer has seen.
                    await transaction.RollbackAsync(CancellationToken.None);
                    if (attempt >= MaxLocalChangeAttempts - 1) throw new DataSyncActorUnverifiedException();
                    pause = Strongest(pause, await _guard.CheckAsync(lease, ct));
                    continue;
                }

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
            catch (DataSyncActorChangedException) when (attempt < MaxLocalChangeAttempts - 1)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                pause = Strongest(pause, await _guard.CheckAsync(lease, ct));
            }
        }
    }

    Task<DataSyncLocalChangeResult> IDataSyncLocalChangeRunner.RunAsync(DataSyncGateLease lease,
        IReadOnlyCollection<string> kinds, Func<IServiceProvider, CancellationToken, Task> change, CancellationToken ct) =>
        RunLocalChangeAsync(lease, kinds, change, ct);

    /// <summary>A local change is tried at most this often when the actor changes under it (§5.6).</summary>
    private const int MaxLocalChangeAttempts = 3;

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

    /// <summary>Refresh in its own scope and transaction; a changed actor is checked again and retried once (§5.6).</summary>
    private async Task<DataSyncRefreshResult> RetryOnceAsync(DataSyncGateLease lease,
        Func<IServiceProvider, CancellationToken, Task<DataSyncRefreshResult>> refresh, IReadOnlyCollection<string> kinds,
        CancellationToken ct)
    {
        for (var attempt = 0;; attempt++)
        {
            await using var scope = _scopes.CreateAsyncScope();
            try
            {
                var result = await refresh(scope.ServiceProvider, ct);
                if (!result.Skipped) NoteRefreshed(kinds);
                return result;
            }
            catch (DataSyncActorChangedException) when (attempt == 0)
            {
                await _guard.CheckAsync(lease, ct);
            }
        }
    }

    private static DataSyncPauseReason? Strongest(DataSyncPauseReason? a, DataSyncPauseReason? b) =>
        a == DataSyncPauseReason.LocalRestoreDetected || b == DataSyncPauseReason.LocalRestoreDetected
            ? DataSyncPauseReason.LocalRestoreDetected
            : a ?? b;
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>What changed definitions after an apply committed: the hub's <c>DataSyncApplied</c> (§8.10.6).</summary>
/// <param name="Kinds">The kinds whose definitions changed.</param>
/// <param name="LocalKeys">The local keys that changed, as <c>{kind}:{localKey}</c>.</param>
public sealed record DataSyncAppliedEvent(IReadOnlyList<string> Kinds, IReadOnlyList<string> LocalKeys,
    DataSyncHistoryKind HistoryKind, int? ApplyLogId, int? LinkId);

/// <summary>Told after every apply that changed definitions committed (the runtime's hub publisher, §8.10.6).</summary>
public interface IDataSyncApplyListener
{
    void OnApplied(DataSyncAppliedEvent applied);
}

/// <summary>
/// The apply runner (§8.10): the bodies of <c>DataSyncApply</c>, <c>DataSyncReview:{id}</c>, <c>DataSyncResolve:{id}</c>,
/// <c>DataSyncUndo:{id}</c> and <c>DataSyncRestore</c>. Every method follows one shape: <c>YieldAsync</c> and the
/// attempt check, the gate (waited for without a limit), the attempt check again, the actor check
/// (<see cref="IDataSyncActorGuard.CheckAsync"/>) before any transaction, then its work in <c>BEGIN IMMEDIATE</c>
/// transactions that start with Refresh (§6.6). A <see cref="DataSyncActorChangedException"/> rolls back, checks the
/// actor again and retries once; every other failure rolls back, drops the touched kinds' caches (§8.10.5) and ends the
/// task <c>Error</c>, except an <see cref="OperationCanceledException"/>, which is rethrown unchanged so the task ends
/// <c>Cancelled</c> (v3.1 M-f). After a commit: dominance closure in its own short transaction, <c>actor.json</c>, and
/// the <see cref="IDataSyncApplyListener"/>s.
/// </summary>
/// <remarks>
/// <para>
/// A chunked task commits between chunks, and the actor guard may rotate in that gap. Each later transaction reads the
/// local state row again and checks it against what the attempt's Refresh saw (<see cref="ContinueAsync"/>), and each
/// commit checks the stored actor, so no transaction ever issues or commits counters under a retired actor (§5.6).
/// </para>
/// <para>
/// No task waits for a pause while it holds the gate. Every task honours a pause before it enters the gate. A
/// resolution also honours one between its chunks, with the gate given back meanwhile: each of its chunks validates
/// its items again from what is stored. An auto-sync apply and a review do not: their later chunks write what one
/// merge or one plan decided from the state read under the gate at the start, which other data sync work must not
/// change halfway. Inside a transaction only a stop reaches any task.
/// </para>
/// </remarks>
public sealed partial class DataSyncApplyRunner : IDataSyncApplyRunner
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)];

    private readonly IServiceScopeFactory _scopes;
    private readonly DataSyncGate _gate;
    private readonly DataSyncActorGuard _guard;
    private readonly DataSyncActorWatermarkFile _watermark;
    private readonly IDataSyncTaskRegistry _registry;
    private readonly IDataSyncReviewStore _reviews;
    private readonly DataSyncBackup _backup;
    private readonly DataSyncRefreshCoordinator? _coordinator;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;

    public DataSyncApplyRunner(IServiceProvider services, IServiceScopeFactory scopes, DataSyncGate gate,
        DataSyncActorGuard guard, DataSyncActorWatermarkFile watermark, IDataSyncTaskRegistry registry,
        IDataSyncReviewStore reviews, DataSyncBackup backup, DataSyncRefreshCoordinator? coordinator = null,
        ILogger<DataSyncApplyRunner>? logger = null)
    {
        _services = services;
        _scopes = scopes;
        _gate = gate;
        _guard = guard;
        _watermark = watermark;
        _registry = registry;
        _reviews = reviews;
        _backup = backup;
        _coordinator = coordinator;
        _logger = logger ?? (ILogger) NullLogger.Instance;
    }

    /// <summary>
    /// How long one transaction of a resolution batch runs before it commits and the next begins (≈ 2 s,
    /// non-blocking note 6): the write lock is never held much longer, however many entities a batch decides.
    /// </summary>
    internal TimeSpan TransactionBudget { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a chunked task leaves SQLite's write lock free between two of its transactions. Another writer of the
    /// app that waits for the lock retries every 150 ms (Microsoft.Data.Sqlite's busy loop): a gap shorter than that
    /// would let the task take the lock back before any waiting writer looked, until the writer's timeout ran out.
    /// </summary>
    internal static readonly TimeSpan ChunkGap = TimeSpan.FromMilliseconds(200);

    #region Shared shape

    /// <summary><c>YieldAsync</c>, then the attempt check (§8.10.1). False: exit without writing.</summary>
    private async Task<bool> StartAsync(BTaskArgs args)
    {
        await args.YieldAsync();
        return MayRun(args);
    }

    private bool MayRun(BTaskArgs args) => DataSyncTaskAttempts.MayRun(_registry, args.Task.Id);

    /// <summary>
    /// A write waits until the actor's start-up verification is done (§5.6); pending evidence is then handled by the
    /// actor check under the gate.
    /// </summary>
    private async Task WaitStartupVerifiedAsync(CancellationToken ct)
    {
        while (!_guard.IsStartupVerified) await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
    }

    /// <summary>
    /// Runs <paramref name="body"/> in a new session with an open transaction: a changed actor rolls back, checks the
    /// actor again and runs it once more; anything else rolls back and propagates.
    /// </summary>
    private async Task<T> InTransactionAsync<T>(DataSyncGateLease lease, Func<DataSyncApplySession, Task<T>> body,
        CancellationToken ct)
    {
        for (var attempt = 0;; attempt++)
        {
            await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
            await s.BeginAsync(ct);
            try
            {
                return await body(s);
            }
            catch (DataSyncActorChangedException) when (attempt == 0)
            {
                await s.RollbackAsync();
                await _guard.CheckAsync(lease, ct);
            }
            catch
            {
                await s.RollbackAsync();
                throw;
            }
        }
    }

    /// <summary>
    /// Refresh first, inside the transaction (§6.6); a skipped Refresh means nothing may be applied now. The actor it
    /// saw is pinned: every later transaction of the attempt must still see it (<see cref="ContinueAsync"/>).
    /// </summary>
    private async Task RefreshAsync(DataSyncApplySession s, DataSyncGateLease lease, IReadOnlyCollection<string> kinds,
        DataSyncRefreshOptions? options, CancellationToken ct)
    {
        var refresh = await s.Refresher.RefreshAsync(lease, kinds.Where(s.Kinds.ContainsKey).ToList(), false,
            options ?? DataSyncRefreshOptions.None, ct);
        if (refresh.Skipped) throw new DataSyncActorUnverifiedException();
        await s.LoadStateAsync(ct);
        s.PinActor();
    }

    /// <summary>
    /// Commits while the actor is still verified and still the one the transaction issued counters under; else rolls
    /// back (§5.6: nothing issued under a retiring actor).
    /// </summary>
    /// <param name="recorder">A chunked run's recorder, told how long the committed transaction held the lock.</param>
    private async Task CommitAsync(DataSyncApplySession s, CancellationToken ct, DataSyncApplyRecorder? recorder = null)
    {
        if (!_guard.IsVerified) throw new DataSyncActorUnverifiedException();
        if (await s.StoredActorDiffersAsync(ct)) throw new DataSyncActorChangedException();
        await s.CommitAsync(ct);
        recorder?.TransactionCommitted(s.LastCommittedTransactionMs);
        // Committed counters must reach actor.json (§5.6), whatever a stop requested meanwhile.
        await WriteWatermarkAsync(s);
    }

    /// <summary>
    /// Runs between two transactions of a chunked task, after the commit and before the next <c>BEGIN</c> (tests
    /// only): something another caller does while the task leaves SQLite's writer lock free.
    /// </summary>
    internal Func<Task>? BetweenChunks { get; set; }

    /// <summary>
    /// The gap between two transactions of a chunked task (§8.10.2): nothing is open, and other writers get SQLite's
    /// lock for <see cref="ChunkGap"/> — the actor guard among them, which handles evidence outside the gate. With a
    /// <paramref name="lease"/>, a pause of the task is honoured here, and only here: the gate is given back while it
    /// waits (§8.10.1: heads, the feed and every other data sync caller keep answering), then the attempt and the
    /// actor are checked again, as at the start.
    /// </summary>
    private async Task BetweenChunksAsync(DataSyncTaskLease? lease, BTaskArgs? args, CancellationToken ct)
    {
        await Task.Delay(ChunkGap, ct);
        if (BetweenChunks is { } hook) await hook();
        if (lease is null || args is null || !await lease.WaitWhilePausedAsync(args)) return;
        if (!MayRun(args)) throw new DataSyncAttemptEndedException();
        await _guard.CheckAsync(lease, ct);
    }

    /// <summary>
    /// Begins the next transaction of a chunked task (§5.6, §8.10.2): the local state row is read again — a rotation
    /// the actor guard committed between the two transactions is never issued under, nor is its counter overwritten
    /// by a stale row — and must still show the actor, generation and restore state the attempt's Refresh saw, with
    /// no evidence waiting. Otherwise <see cref="DataSyncActorChangedException"/>: this transaction rolls back, the
    /// committed ones stand, and the caller checks the actor and retries, where the paused link stops it.
    /// </summary>
    private async Task ContinueAsync(DataSyncApplySession s, CancellationToken ct)
    {
        await s.BeginAsync(ct);
        await s.LoadStateAsync(ct);
        if (!_guard.IsVerified || s.ActorMoved) throw new DataSyncActorChangedException();
    }

    /// <summary>
    /// A chunked apply's link after <see cref="ContinueAsync"/>: read again (a later write must not overwrite what was
    /// committed meanwhile) and tracked again (the gap forgot every tracked row, <see
    /// cref="DataSyncApplySession.ForgetTrackedAsync"/>), and still as the apply found it. A link that went away, or
    /// that stopped or paused since
    /// <paramref name="startedAs"/> — the actor guard pauses a restore's links outside the gate — stops the apply like a
    /// changed actor.
    /// </summary>
    private static async Task EnsureLinkRunsAsync(DataSyncApplySession s, DataSyncLinkDbModel link,
        DataSyncLinkState startedAs, CancellationToken ct)
    {
        var entry = s.Db.Entry(link);
        await entry.ReloadAsync(ct);
        if (entry.State == EntityState.Detached ||
            (link.State != startedAs && link.State is DataSyncLinkState.Paused or DataSyncLinkState.Stopped))
        {
            throw new DataSyncActorChangedException();
        }
    }

    private async Task WriteWatermarkAsync(DataSyncApplySession s)
    {
        if (await s.Store.GetLocalStateAsync(CancellationToken.None) is { } state)
            await _watermark.WriteAsync(state, CancellationToken.None);
    }

    /// <summary>
    /// After a commit (§8.10.2): dominance closure across links in its own short transaction (§9.3), the kinds noted as
    /// refreshed, and the listeners told what changed. A failure here never undoes the committed apply, and neither
    /// does a stop requested meanwhile: none of it takes the task's token, so a finished apply never ends Cancelled.
    /// </summary>
    private async Task AfterCommitAsync(DataSyncApplySession s, DataSyncApplyRecorder recorder,
        IReadOnlyCollection<string> refreshedKinds, DataSyncHistoryKind kind, int? logId, int? linkId)
    {
        try
        {
            if (recorder.Touched.Count > 0)
            {
                await s.BeginAsync(CancellationToken.None);
                await s.Store.CloseDominatedItemsAsync(recorder.Touched.ToList(), s.Now, CancellationToken.None);
                await s.CommitAsync(CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            await s.RollbackAsync();
            _logger.LogWarning(e, "Data sync could not close dominated inbox items after an apply.");
        }

        _coordinator?.NoteRefreshed(refreshedKinds);
        if (recorder.ChangedDefinitions.Count == 0) return;
        var applied = new DataSyncAppliedEvent(
            recorder.ChangedDefinitions.Select(c => c.Kind).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList(),
            recorder.ChangedDefinitions.Select(c => c.Kind + ":" + c.LocalKey).Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal).ToList(),
            kind, logId, linkId);
        foreach (var listener in _services.GetServices<IDataSyncApplyListener>())
        {
            try
            {
                listener.OnApplied(applied);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "A data sync apply listener failed.");
            }
        }
    }

    /// <summary>
    /// A merge met row A1 or A2 (§5.6, §8.4), and its transaction was rolled back: outside any transaction, a duplicate
    /// actor pauses the link (<c>PeerIdentityDuplicated</c>); a regression is reported as the peer's evidence and the
    /// actor checked, which rotates and pauses, or only raises a retired actor's recorded counter. Returns the link's
    /// pause, if any.
    /// </summary>
    private async Task<DataSyncPauseReason?> HandleAnomalyAsync(DataSyncGateLease lease, int linkId, string peerNodeId,
        DataSyncAnomaly anomaly, DataSyncPauseReason? pause, string? pauseDetail, CancellationToken ct)
    {
        if (anomaly.Code == DataSyncAnomalies.DuplicateActor)
        {
            var reason = pause ?? DataSyncPauseReason.PeerIdentityDuplicated;
            await PauseLinkAsync(linkId, reason, pauseDetail, ct);
            return reason;
        }

        await _guard.ReportPeerEvidenceAsync(peerNodeId, anomaly.ActorId, anomaly.SeenCounter, ct);
        await _guard.CheckAsync(lease, ct);
        return await PausedReasonAsync(linkId, ct);
    }

    private static async Task<HashSet<long>> OpenItemIdsAsync(DataSyncApplySession s, CancellationToken ct) =>
        (await s.Db.DataSyncInboxItems.AsNoTracking().Where(i => i.ClosedAtUtc == null).Select(i => i.Id)
            .ToListAsync(ct)).ToHashSet();

    /// <summary>§8.10.4: <c>VACUUM INTO</c> before the transaction; a failure ends the task with nothing applied.</summary>
    private async Task BackupAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        try
        {
            await _backup.CreateAsync(scope.ServiceProvider.GetRequiredService<BakabaseDbContext>(), ct);
        }
        catch (DataSyncBackupFailedException e)
        {
            throw new BTaskException(DataSyncBackupFailedException.Code, e.Message);
        }
    }

    private static long ElapsedMs(long started) => (long) Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static TimeSpan BackoffAfter(int failures) => Backoff[Math.Clamp(failures - 1, 0, Backoff.Length - 1)];

    private static string Truncate(string? value, int length) =>
        value is null ? "" : value.Length <= length ? value : value[..length];

    #endregion
}

/// <summary>Refresh was skipped (the actor is unverified or evidence waits): nothing may be applied now (§5.6).</summary>
public sealed class DataSyncActorUnverifiedException() : Exception("actorUnverified");

/// <summary>
/// After a pause the attempt may no longer write (a cancel was requested, or a newer attempt of the task took over,
/// §8.10.1): the task exits there. The transactions it committed stand.
/// </summary>
internal sealed class DataSyncAttemptEndedException() : Exception("attemptEnded");

/// <summary>
/// A merge inside a resolution met row A1 or A2 (§8.4): the transaction rolls back, Refresh included, and the runner
/// handles the anomaly outside it (§5.6) before it runs the batch once more.
/// </summary>
internal sealed class DataSyncMergeAnomalyException(int linkId, string peerNodeId, DataSyncAnomaly anomaly,
    DataSyncPauseReason? pause, string? pauseDetail) : Exception("mergeAnomaly:" + anomaly.Code)
{
    public int LinkId { get; } = linkId;
    public string PeerNodeId { get; } = peerNodeId;
    public DataSyncAnomaly Anomaly { get; } = anomaly;
    public DataSyncPauseReason? Pause { get; } = pause;
    public string? PauseDetail { get; } = pauseDetail;
}

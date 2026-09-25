using System;
using System.Collections.Concurrent;
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

    /// <summary>
    /// "Take the other devices' definitions" (§9.5): the one link whose next full reconciliation takes the peer's
    /// version of concurrent entities (the Follow rule), for that cycle only. Kept in memory: after a restart the
    /// reconciliation merges normally, and concurrent entities become items instead.
    /// </summary>
    private readonly ConcurrentDictionary<int, bool> _othersWinNext = new();

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

    /// <summary>The link whose next full reconciliation follows the peer after an "others win" restore choice.</summary>
    internal bool TakesTheirsNext(int linkId) => _othersWinNext.ContainsKey(linkId);

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

    /// <summary>Refresh first, inside the transaction (§6.6); a skipped Refresh means nothing may be applied now.</summary>
    private async Task RefreshAsync(DataSyncApplySession s, DataSyncGateLease lease, IReadOnlyCollection<string> kinds,
        DataSyncRefreshOptions? options, CancellationToken ct)
    {
        var refresh = await s.Refresher.RefreshAsync(lease, kinds.Where(s.Kinds.ContainsKey).ToList(), false,
            options ?? DataSyncRefreshOptions.None, ct);
        if (refresh.Skipped) throw new DataSyncActorUnverifiedException();
        await s.LoadStateAsync(ct);
    }

    /// <summary>Commits while the actor is still verified; else rolls back (§5.6: nothing issued under a retiring actor).</summary>
    private async Task CommitAsync(DataSyncApplySession s, CancellationToken ct)
    {
        if (!_guard.IsVerified) throw new DataSyncActorUnverifiedException();
        await s.CommitAsync(ct);
        await WriteWatermarkAsync(s, ct);
    }

    private async Task WriteWatermarkAsync(DataSyncApplySession s, CancellationToken ct)
    {
        if (await s.Store.GetLocalStateAsync(ct) is { } state) await _watermark.WriteAsync(state, ct);
    }

    /// <summary>
    /// After a commit (§8.10.2): dominance closure across links in its own short transaction (§9.3), the kinds noted as
    /// refreshed, and the listeners told what changed. A failure here never undoes the committed apply.
    /// </summary>
    private async Task AfterCommitAsync(DataSyncApplySession s, DataSyncApplyRecorder recorder,
        IReadOnlyCollection<string> refreshedKinds, DataSyncHistoryKind kind, int? logId, int? linkId, CancellationToken ct)
    {
        try
        {
            if (recorder.Touched.Count > 0)
            {
                await s.BeginAsync(ct);
                await s.Store.CloseDominatedItemsAsync(recorder.Touched.ToList(), s.Now, ct);
                await s.CommitAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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

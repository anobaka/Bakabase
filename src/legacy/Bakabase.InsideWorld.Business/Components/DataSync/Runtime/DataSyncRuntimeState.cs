using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// What the runtime knows since this process started and never stores (§8.2): whether it started, the pulls waiting
/// for <c>DataSyncApply</c> or a person's Start (§8.3), when the daily and fallback work last ran, which links wait for a
/// re-merge without a pull and this device's LastSeq at each link's last apply, which links fetch or apply a full
/// reconciliation, and a person's stop of the fetch or apply task.
/// </summary>
public sealed class DataSyncRuntimeState
{
    /// <summary>The fetch task's interval, which is also the fallback pull's (§8.2).</summary>
    public static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan RetentionInterval = TimeSpan.FromDays(1);

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<int, (DataSyncStagedPull Pull, long Order)> _pulls = new();
    private long _pullOrder;
    private readonly ConcurrentDictionary<int, DataSyncStagedPull> _previews = new();
    private readonly ConcurrentDictionary<int, (DateTime? At, DateTime? Due)> _attempts = new();
    private readonly ConcurrentDictionary<int, byte> _woken = new();
    private readonly ConcurrentDictionary<int, byte> _reMergeRequested = new();
    private readonly ConcurrentDictionary<int, long> _mergedAtSeq = new();
    private readonly Dictionary<int, int> _fullReconciliations = new();
    private DateTime? _startedAtUtc;
    private DateTime? _lastFallbackAtUtc;
    private DateTime? _lastRetentionAtUtc;
    private DateTime? _fetchStoppedAtUtc;
    private DateTime? _applyStoppedAtUtc;

    /// <summary>Marks the start (the scheduler's first tick with the fetch task registered); true once.</summary>
    public bool TryStart(DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_startedAtUtc is not null) return false;
            _startedAtUtc = nowUtc;
            _lastFallbackAtUtc = nowUtc;
            return true;
        }
    }

    // ---- staged pulls (§2.9, §4.7) -------------------------------------------------------------------------------

    /// <summary>
    /// A pull the <c>DataSync</c> task fetched, waiting for <c>DataSyncApply</c>: one per link, a newer one replaces it,
    /// lost on restart (the link's cursors never moved, so its next head fetches it again).
    /// </summary>
    public void StagePull(int linkId, DataSyncStagedPull pull) =>
        _pulls[linkId] = (pull, Interlocked.Increment(ref _pullOrder));

    public DataSyncStagedPull? TakePull(int linkId) => _pulls.TryRemove(linkId, out var slot) ? slot.Pull : null;

    /// <summary>The fetch half skips a refetch while this is still current (§8.10.2).</summary>
    public DataSyncStagedPull? PeekPull(int linkId) => _pulls.TryGetValue(linkId, out var slot) ? slot.Pull : null;

    /// <summary>Links with a staged pull, oldest first.</summary>
    public IReadOnlyList<int> StagedLinks() => _pulls.OrderBy(p => p.Value.Order).Select(p => p.Key).ToList();

    /// <summary>
    /// A first sync's snapshot (§8.3): one per link waiting for its person's Start, kept until the Start applied it or
    /// the link stopped or went, lost on restart (the next cycle fetches it again). <c>DataSyncApply</c> never takes it.
    /// </summary>
    public void StagePreview(int linkId, DataSyncStagedPull pull) => _previews[linkId] = pull;

    public DataSyncStagedPull? PeekPreview(int linkId) => _previews.GetValueOrDefault(linkId);

    public void DropPreview(int linkId) => _previews.TryRemove(linkId, out _);

    public void ForgetLink(int linkId)
    {
        _woken.TryRemove(linkId, out _);
        _reMergeRequested.TryRemove(linkId, out _);
        _mergedAtSeq.TryRemove(linkId, out _);
        _attempts.TryRemove(linkId, out _);
    }

    // ---- scheduling (§8.2) -----------------------------------------------------------------------------------------

    /// <summary>
    /// When the link's peer was last asked, and when the link is due again (null: now). Kept in memory, never on the
    /// link row: after a start no link has been asked and every link is due (the scheduler then delays them all by the
    /// startup delay), so a head poll that changes nothing writes nothing.
    /// </summary>
    public (DateTime? At, DateTime? Due) GetAttempt(int linkId) => _attempts.GetValueOrDefault(linkId);

    public bool IsDue(int linkId, DateTime nowUtc) => GetAttempt(linkId).Due is not { } due || due <= nowUtc;

    /// <summary>
    /// The link's peer was asked at <paramref name="atUtc"/> and the link is due again at <paramref name="dueUtc"/>.
    /// Recorded once what the attempt wrote on the link row is committed.
    /// </summary>
    public void RecordAttempt(int linkId, DateTime atUtc, DateTime dueUtc) => _attempts[linkId] = (atUtc, dueUtc);

    /// <summary>
    /// The link is due at <paramref name="dueUtc"/>; null is now ("Sync now", a link turned on or resumed).
    /// </summary>
    public void SetDue(int linkId, DateTime? dueUtc = null) =>
        _attempts.AddOrUpdate(linkId, (null, dueUtc), (_, attempt) => (attempt.At, dueUtc));

    /// <summary>
    /// The link has pending records to re-merge although nothing new arrived (§8.4: a <c>Retry</c> record, a local
    /// change, a resume), so <c>DataSyncApply</c> re-merges them without a pull (§8.10.2 step 1, "no pending retry").
    /// </summary>
    public void RequestReMerge(int linkId) => _reMergeRequested[linkId] = 0;

    /// <summary>The link's apply committed while this device's LastSeq (§6.2) was <paramref name="lastSeq"/>.</summary>
    public void NoteMerged(int linkId, long lastSeq) => _mergedAtSeq[linkId] = lastSeq;

    /// <summary>
    /// Whether this device changed a definition since the link's last apply (LastSeq moved past it), which may change
    /// what its pending records merge to. Unknown since the start counts as changed: the first cycle re-merges once,
    /// which also takes the records an upgrade can now read.
    /// </summary>
    public bool ChangedSinceMerge(int linkId, long lastSeq) =>
        !_mergedAtSeq.TryGetValue(linkId, out var at) || lastSeq > at;

    public bool IsReMergeRequested(int linkId) => _reMergeRequested.ContainsKey(linkId);

    /// <summary>True, once, when a re-merge was requested for the link; the apply that runs it takes it.</summary>
    public bool TakeReMerge(int linkId) => _reMergeRequested.TryRemove(linkId, out _);

    // ---- a running full reconciliation (§8.8, §11.6) ---------------------------------------------------------------

    /// <summary>
    /// The link's peer is being read, or its pull applied, with a kind from 0 (§8.8): "Comparing everything with
    /// {{name}}…" (§11.6) until the returned mark is disposed. The fetch holds one while it reads such a pull and the
    /// apply while it applies it; in between, the staged pull says so itself (<see cref="DataSyncStagedKind"/>).
    /// </summary>
    public IDisposable BeginFullReconciliation(int linkId)
    {
        lock (_lock) _fullReconciliations[linkId] = _fullReconciliations.GetValueOrDefault(linkId) + 1;
        return new FullReconciliationMark(this, linkId);
    }

    /// <summary>Whether a fetch or an apply of the link holds a full reconciliation mark now.</summary>
    public bool IsFullReconciliationRunning(int linkId)
    {
        lock (_lock) return _fullReconciliations.ContainsKey(linkId);
    }

    private void EndFullReconciliation(int linkId)
    {
        lock (_lock)
        {
            if (!_fullReconciliations.TryGetValue(linkId, out var holders)) return;
            if (holders <= 1) _fullReconciliations.Remove(linkId);
            else _fullReconciliations[linkId] = holders - 1;
        }
    }

    private sealed class FullReconciliationMark(DataSyncRuntimeState state, int linkId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) state.EndFullReconciliation(linkId);
        }
    }

    // ---- a person's stop (§8.2, §8.10.1) --------------------------------------------------------------------------

    /// <summary>
    /// The person stopped (or called off) the <c>DataSync</c> task: the scheduler does not start it again before its
    /// next interval, unless they press "Sync now" (<c>BTaskManager.Start</c> would otherwise restart a cancelled task
    /// one second later, F71).
    /// </summary>
    public void HoldFetch(DateTime nowUtc)
    {
        lock (_lock) _fetchStoppedAtUtc = nowUtc;
    }

    public bool IsFetchHeld(DateTime nowUtc)
    {
        lock (_lock) return _fetchStoppedAtUtc is { } at && nowUtc - at < FetchInterval;
    }

    public void ReleaseFetch()
    {
        lock (_lock) _fetchStoppedAtUtc = null;
    }

    /// <summary>
    /// The person stopped (or called off) <c>DataSyncApply</c>: the scheduler does not enqueue it again for the pulls
    /// and flags that already waited, until the next pull, "Sync now" or the fetch interval, whichever comes first.
    /// Without this a stop would only interrupt the running apply: the pull is put back, and the next tick would
    /// enqueue it again.
    /// </summary>
    public void HoldApply(DateTime nowUtc)
    {
        lock (_lock) _applyStoppedAtUtc = nowUtc;
    }

    public bool IsApplyHeld(DateTime nowUtc)
    {
        lock (_lock) return _applyStoppedAtUtc is { } at && nowUtc - at < FetchInterval;
    }

    public void ReleaseApply()
    {
        lock (_lock) _applyStoppedAtUtc = null;
    }

    /// <summary>
    /// The link's peer was just seen (discovery answered, §8.2): the link is due now, without writing its row, so the
    /// request that saw it — a GET — writes nothing (F78). The next fetch cycle takes the mark.
    /// </summary>
    public void Wake(int linkId) => _woken[linkId] = 0;

    public bool IsWoken(int linkId) => _woken.ContainsKey(linkId);

    /// <summary>True, once, when the link was woken since the last cycle looked at it.</summary>
    public bool TakeWoken(int linkId) => _woken.TryRemove(linkId, out _);

    /// <summary>
    /// True when the fallback pull is due (every fetch interval since the start), and restarts its interval. The first
    /// cycle is never a fallback: at the start every link is due anyway.
    /// </summary>
    public bool TakeFallbackDue(DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_lastFallbackAtUtc is not { } last)
            {
                _lastFallbackAtUtc = nowUtc;
                return false;
            }

            if (nowUtc - last < FetchInterval) return false;
            _lastFallbackAtUtc = nowUtc;
            return true;
        }
    }

    /// <summary>True when retention is due (once a day), and restarts its interval.</summary>
    public bool TakeRetentionDue(DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_lastRetentionAtUtc is { } last && nowUtc - last < RetentionInterval) return false;
            _lastRetentionAtUtc = nowUtc;
            return true;
        }
    }
}

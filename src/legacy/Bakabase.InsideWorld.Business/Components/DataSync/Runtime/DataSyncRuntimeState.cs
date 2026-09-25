using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// What the runtime knows since this process started and never stores (§5.6, §8.2): when it started, which links
/// answered a head, the peers' comparison form versions from their last head, when the daily and fallback work
/// last ran, which links wait for a re-merge without a pull, and a person's stop of the fetch or apply task.
/// </summary>
public sealed class DataSyncRuntimeState
{
    /// <summary>After a start the actor stays unverified at most this long (§5.6).</summary>
    public static readonly TimeSpan VerificationWindow = TimeSpan.FromMinutes(2);

    /// <summary>The fetch task's interval, which is also the fallback pull's (§8.2).</summary>
    public static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan RetentionInterval = TimeSpan.FromDays(1);

    private readonly object _lock = new();
    private readonly HashSet<int> _headAnswered = [];
    private readonly ConcurrentDictionary<int, IReadOnlyDictionary<string, int>> _formVersions = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastHeadAt = new();
    private readonly ConcurrentDictionary<int, byte> _woken = new();
    private readonly ConcurrentDictionary<int, byte> _reMergeRequested = new();
    private DateTime? _startedAtUtc;
    private DateTime? _lastFallbackAtUtc;
    private DateTime? _lastRetentionAtUtc;
    private DateTime? _fetchStoppedAtUtc;
    private DateTime? _applyStoppedAtUtc;

    /// <summary>When the runtime became ready (the scheduler's first tick with the fetch task registered).</summary>
    public DateTime? StartedAtUtc
    {
        get
        {
            lock (_lock) return _startedAtUtc;
        }
    }

    /// <summary>Marks the start; true the first time only.</summary>
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

    /// <summary>
    /// A head of this link's peer succeeded: its form versions and when ("online", §8.2). It does not yet count for
    /// the actor's verification: <see cref="MarkHeadAnswered"/> does, once the head's evidence was handled.
    /// </summary>
    public void RecordHead(int linkId, DataSyncFeedHead head, DateTime nowUtc)
    {
        _formVersions[linkId] = head.Kinds.GroupBy(k => k.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ComparisonFormVersion, StringComparer.Ordinal);
        _lastHeadAt[linkId] = nowUtc;
    }

    /// <summary>
    /// This link's peer answered one head and the <c>SeenCounter</c> it carried was reported to the actor guard (§5.6:
    /// "every Active link answered one head"). Counted only after the report returned: the scheduler verifies on its
    /// own thread, and a head counted earlier would let it verify — and the apply and Refresh issue counters under the
    /// old actor — while the evidence that rotates that actor was still being recorded.
    /// </summary>
    public void MarkHeadAnswered(int linkId)
    {
        lock (_lock) _headAnswered.Add(linkId);
    }

    /// <summary>The last head's per-kind comparison form versions of the link's peer (§8.4 row A2).</summary>
    public IReadOnlyDictionary<string, int> GetPeerFormVersions(int linkId) =>
        _formVersions.TryGetValue(linkId, out var versions)
            ? versions
            : new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>When the link's peer last answered a head in this process ("online", §8.2).</summary>
    public DateTime? GetLastHeadAt(int linkId) => _lastHeadAt.TryGetValue(linkId, out var at) ? at : null;

    public void ForgetLink(int linkId)
    {
        lock (_lock) _headAnswered.Remove(linkId);
        _formVersions.TryRemove(linkId, out _);
        _lastHeadAt.TryRemove(linkId, out _);
        _woken.TryRemove(linkId, out _);
        _reMergeRequested.TryRemove(linkId, out _);
    }

    /// <summary>
    /// The link has pending records to re-merge although nothing new arrived (§8.4 conditions 2, 3 and 5: a local
    /// change, <c>Retry</c>/<c>OverBudget</c>, a flag), so <c>DataSyncApply</c> re-merges them without a pull
    /// (§8.10.2 step 1, "no pending retry"). Kept in memory: after a restart the next pull re-merges them anyway.
    /// </summary>
    public void RequestReMerge(int linkId) => _reMergeRequested[linkId] = 0;

    public bool IsReMergeRequested(int linkId) => _reMergeRequested.ContainsKey(linkId);

    /// <summary>True, once, when a re-merge was requested for the link; the apply that runs it takes it.</summary>
    public bool TakeReMerge(int linkId) => _reMergeRequested.TryRemove(linkId, out _);

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
    /// Without this a stop would only interrupt the current chunk: the pull is put back, and the next tick would
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
    /// Whether the actor may be marked verified (§5.6): no Active link, every Active link's peer answered one head
    /// since the start and its evidence was handled (<see cref="MarkHeadAnswered"/>), or the verification window
    /// passed.
    /// </summary>
    public bool CanVerify(IEnumerable<DataSyncLinkDbModel> links, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_startedAtUtc is { } started && nowUtc - started >= VerificationWindow) return true;
            return links.Where(l => l.State == DataSyncLinkState.Active).All(l => _headAnswered.Contains(l.Id));
        }
    }

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

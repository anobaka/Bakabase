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
/// answered a head, the peers' comparison form versions from their last head, and when the daily and fallback work
/// last ran.
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
    private DateTime? _startedAtUtc;
    private DateTime? _lastFallbackAtUtc;
    private DateTime? _lastRetentionAtUtc;

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

    /// <summary>A head of this link's peer succeeded (§5.6: "every Active link answered one head").</summary>
    public void RecordHead(int linkId, DataSyncFeedHead head, DateTime nowUtc)
    {
        lock (_lock) _headAnswered.Add(linkId);
        _formVersions[linkId] = head.Kinds.GroupBy(k => k.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ComparisonFormVersion, StringComparer.Ordinal);
        _lastHeadAt[linkId] = nowUtc;
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
    }

    /// <summary>
    /// Whether the actor may be marked verified (§5.6): no Active link, every Active link's peer answered one head
    /// since the start, or the verification window passed.
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

using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// Staged first-link reviews and copy-once pulls (§4.7, §8.3), in memory: a review survives a page reload, not a
/// restart. At most <see cref="MaxStaged"/> are kept; each expires after <see cref="IdleTimeout"/> without access
/// (sliding), except one that is applying. Expired entries are swept on every access. Staging beyond the limit
/// evicts the entry accessed longest ago that is not applying.
/// </summary>
public sealed class DataSyncReviewStore(TimeProvider time) : IDataSyncReviewStore
{
    public const int MaxStaged = 3;
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(60);

    private readonly object _lock = new();
    private readonly Dictionary<string, DataSyncReviewEntry> _entries = new(StringComparer.Ordinal);

    public DataSyncReviewEntry? GetForLink(int linkId)
    {
        lock (_lock)
        {
            Sweep();
            var entry = _entries.Values.Where(e => e.LinkId == linkId).OrderByDescending(e => e.LastAccessUtc)
                .FirstOrDefault();
            return entry is null ? null : Touch(entry);
        }
    }

    /// <summary>
    /// A new review with a new id. It replaces the link's earlier review (a cycle stages only when
    /// <see cref="GetForLink"/> returned null, so that one had expired or was applied).
    /// </summary>
    public DataSyncReviewEntry Stage(int? linkId, bool copyOnce, DataSyncStagedPull pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        lock (_lock)
        {
            Sweep();
            if (linkId is { } id)
            {
                foreach (var old in _entries.Values.Where(e => e.LinkId == id && !IsApplying(e)).ToList())
                    _entries.Remove(old.ReviewId);
            }

            while (_entries.Count >= MaxStaged)
            {
                var evict = _entries.Values.Where(e => !IsApplying(e)).OrderBy(e => e.LastAccessUtc).FirstOrDefault();
                if (evict is null) break;
                _entries.Remove(evict.ReviewId);
            }

            var entry = new DataSyncReviewEntry(Guid.NewGuid().ToString("N"), linkId, copyOnce, pull, null, null, null,
                Now, false);
            _entries[entry.ReviewId] = entry;
            return entry;
        }
    }

    public DataSyncReviewEntry? Get(string reviewId)
    {
        lock (_lock)
        {
            Sweep();
            return _entries.TryGetValue(reviewId, out var entry) ? Touch(entry) : null;
        }
    }

    public void SetLastPlan(string reviewId, DataSyncPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Update(reviewId, e => e with {LastPlan = plan});
    }

    public void MarkApplying(string reviewId, string taskId) => Update(reviewId, e => e with {TaskId = taskId});

    /// <summary>An applied review stays readable (the result screen) until it idles out.</summary>
    public void MarkApplied(string reviewId, int applyLogId) =>
        Update(reviewId, e => e with {ApplyLogId = applyLogId});

    public void Discard(string reviewId)
    {
        lock (_lock) _entries.Remove(reviewId);
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Applying: a task was started and has not recorded its result yet. It never expires.</summary>
    private static bool IsApplying(DataSyncReviewEntry entry) => entry.TaskId is not null && entry.ApplyLogId is null;

    private DataSyncReviewEntry Touch(DataSyncReviewEntry entry) =>
        _entries[entry.ReviewId] = entry with {LastAccessUtc = Now};

    private void Update(string reviewId, Func<DataSyncReviewEntry, DataSyncReviewEntry> change)
    {
        lock (_lock)
        {
            Sweep();
            if (!_entries.TryGetValue(reviewId, out var entry))
                throw new KeyNotFoundException($"No staged data sync review {reviewId}.");
            _entries[reviewId] = change(entry) with {LastAccessUtc = Now};
        }
    }

    private void Sweep()
    {
        var now = Now;
        foreach (var expired in _entries.Values
                     .Where(e => !IsApplying(e) && now - e.LastAccessUtc >= IdleTimeout).ToList())
        {
            _entries.Remove(expired.ReviewId);
        }
    }
}

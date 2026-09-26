using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// Staged first-link reviews and copy-once pulls (§4.7, §8.3), in memory: a review survives a page reload, not a
/// restart. Each expires after <see cref="IdleTimeout"/> without access (sliding: a person reading it — the fetch
/// cycle and the status views only peek), except one that is applying. Expired entries are swept on every access.
/// </summary>
/// <remarks>
/// A review that waits for its person is never evicted to make room for another: the fetch cycle stages one only for a
/// link that has none, so evicting one sends its link back to fetching a full snapshot and announcing it again on the
/// next cycle, and with more links awaiting a review than the store kept they would evict each other every minute
/// (§8.3: "a review the person is reading is never replaced by a cycle", §9.4). What bounds them is the links: one
/// review per link, and the store forgets it when the link stops or is reset. Only applied reviews, kept for their
/// result screen, make room: beyond <see cref="MaxStaged"/> entries the one accessed longest ago goes.
/// </remarks>
public sealed class DataSyncReviewStore(TimeProvider time) : IDataSyncReviewStore
{
    /// <summary>
    /// Beyond this many entries, applied reviews (their result screens) are evicted, least recently read first.
    /// </summary>
    public const int MaxStaged = 3;

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(60);

    private readonly object _lock = new();
    private readonly Dictionary<string, DataSyncReviewEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether applying <paramref name="review"/> is a copy once (§8.1): its link's current mode says so (a copy once is
    /// a link in <c>Off</c> waiting for its review), not the flag the review was staged with. A copy once the person
    /// turned into Follow or two-way before applying its review — the rule editor's receive arrow, or approving the
    /// peer's two-way request — is that link's first contact, which leaves it running. Only a review without a link
    /// goes by its own flag.
    /// </summary>
    public static bool AppliesAsCopyOnce(DataSyncReviewEntry review, DataSyncLinkDbModel? link) =>
        link is null ? review.CopyOnce : link.Mode == DataSyncLinkMode.Off;

    public DataSyncReviewEntry? GetForLink(int linkId)
    {
        lock (_lock)
        {
            var entry = CurrentFor(linkId);
            return entry is null ? null : Touch(entry);
        }
    }

    public DataSyncReviewEntry? PeekForLink(int linkId)
    {
        lock (_lock) return CurrentFor(linkId);
    }

    /// <summary>
    /// A new review with a new id. It replaces the link's earlier review unless that one is applying: a cycle stages
    /// only when <see cref="PeekForLink"/> returned null (it had expired or was applied), and "Fetch again" replaces
    /// the review the person asked to refresh, once the new one has been fetched.
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
                var evict = _entries.Values.Where(IsApplied).OrderBy(e => e.LastAccessUtc).FirstOrDefault();
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

    /// <summary>
    /// The apply attempt ended without applying: the entry stops counting as applying, and its idle time starts
    /// again, so it expires, can be evicted, and the link's next cycle may stage a fresh review once it is gone.
    /// </summary>
    public void MarkApplyEnded(string reviewId)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(reviewId, out var entry) || !IsApplying(entry)) return;
            _entries[reviewId] = entry with {TaskId = null, LastAccessUtc = Now};
        }
    }

    public void Discard(string reviewId)
    {
        lock (_lock) _entries.Remove(reviewId);
    }

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Applying: a task was started and has not recorded its result yet. It never expires.</summary>
    private static bool IsApplying(DataSyncReviewEntry entry) => entry.TaskId is not null && entry.ApplyLogId is null;

    /// <summary>Applied: kept only for its result screen, so it may make room for a review waiting for a person.</summary>
    private static bool IsApplied(DataSyncReviewEntry entry) => entry.ApplyLogId is not null;

    private DataSyncReviewEntry? CurrentFor(int linkId)
    {
        Sweep();
        return _entries.Values.Where(e => e.LinkId == linkId).OrderByDescending(e => e.LastAccessUtc).FirstOrDefault();
    }

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

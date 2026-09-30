using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Feed;

/// <summary>One kind of a stored snapshot: the since it was served from and its precomputed pages.</summary>
public sealed record DataSyncFeedSnapshotKind(string Kind, long SinceSeq, IReadOnlyList<byte[]> Pages,
    IReadOnlyDictionary<string, int> PageByCursor);

/// <summary>A snapshot built for one reader grant (§7.5.2): pages only; nothing in it is read from the database again.</summary>
public sealed class DataSyncFeedSnapshot(string id, string grantId, string readerNodeId,
    IReadOnlyDictionary<string, DataSyncFeedSnapshotKind> kinds, long bytes)
{
    public string Id { get; } = id;
    public string GrantId { get; } = grantId;
    public string ReaderNodeId { get; } = readerNodeId;
    public IReadOnlyDictionary<string, DataSyncFeedSnapshotKind> Kinds { get; } = kinds;
    public long Bytes { get; } = bytes;

    /// <summary>Slides on every page read (<see cref="DataSyncFeedSnapshots.Ttl"/>).</summary>
    internal DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// The feed's snapshots, in memory (§7.5.2): one per reader grant, a new manifest discarding the reader's previous
/// one; a two-minute TTL that slides on each page read; at most one manifest per reader grant every ten seconds; and
/// the total size of every live snapshot bounded by <c>MaxSnapshotBytesTotal</c>. Pages are served from here without
/// the gate, so an apply on this device can never fail a page read. Lost on restart: readers then meet
/// <c>SnapshotExpired</c> and ask for a new manifest.
/// </summary>
public sealed class DataSyncFeedSnapshots(TimeProvider? time = null)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    /// <summary>A reader grant gets at most one manifest per this interval (<c>TooManySnapshots</c>).</summary>
    public static readonly TimeSpan ManifestInterval = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _lock = new();
    private readonly Dictionary<string, DataSyncFeedSnapshot> _byGrant = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _createdAt = new(StringComparer.Ordinal);

    /// <summary>
    /// Admits a new manifest for <paramref name="grantId"/>: throws <c>TooManySnapshots</c> (429, with the seconds
    /// left) within <see cref="ManifestInterval"/> of the grant's last snapshot; otherwise discards its previous
    /// snapshot, whose pages the reader can no longer need.
    /// </summary>
    public void Admit(string grantId)
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            Purge(now);
            if (_createdAt.TryGetValue(grantId, out var created) && now - created < ManifestInterval)
            {
                var wait = (int) Math.Ceiling((ManifestInterval - (now - created)).TotalSeconds);
                throw DataSyncFeedErrors.TooManySnapshots(Math.Max(1, wait));
            }

            _byGrant.Remove(grantId);
        }
    }

    /// <summary>
    /// Stores a built snapshot as the grant's only one, unless the snapshots of other readers already hold so much
    /// that it would take the total over <paramref name="maxTotalBytes"/> (<c>Busy</c>, retry after 30 s).
    /// </summary>
    public void Store(DataSyncFeedSnapshot snapshot, long maxTotalBytes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            Purge(now);
            _byGrant.Remove(snapshot.GrantId);
            var others = _byGrant.Values.Sum(s => s.Bytes);
            if (others + snapshot.Bytes > maxTotalBytes)
                throw DataSyncFeedErrors.Busy(DataSyncFeedErrors.RetryAfterSnapshotSpace);
            snapshot.ExpiresAt = now + Ttl;
            _byGrant[snapshot.GrantId] = snapshot;
            _createdAt[snapshot.GrantId] = now;
        }
    }

    /// <summary>
    /// A precomputed page (§7.5.3): <c>SnapshotExpired</c> (410) for a snapshot that is unknown, expired or another
    /// grant's; <c>UnknownKind</c> (404) for a kind it does not hold; <c>SnapshotMismatch</c> (409) for a since other
    /// than the kind's served one, or a cursor that names none of its pages. Slides the TTL.
    /// </summary>
    public byte[] GetPage(string grantId, string snapshotId, string kind, long sinceSeq, string? cursor)
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            Purge(now);
            if (!_byGrant.TryGetValue(grantId, out var snapshot) || snapshot.Id != snapshotId)
                throw DataSyncFeedErrors.SnapshotExpired();
            if (!snapshot.Kinds.TryGetValue(kind, out var snapshotKind)) throw DataSyncFeedErrors.UnknownKind();
            if (snapshotKind.SinceSeq != sinceSeq) throw DataSyncFeedErrors.SnapshotMismatch();
            if (!snapshotKind.PageByCursor.TryGetValue(cursor ?? "", out var index))
                throw DataSyncFeedErrors.SnapshotMismatch();
            snapshot.ExpiresAt = now + Ttl;
            return snapshotKind.Pages[index];
        }
    }

    /// <summary>The live snapshot of a grant, if any (diagnostics and tests).</summary>
    public DataSyncFeedSnapshot? Find(string grantId)
    {
        lock (_lock)
        {
            Purge(_time.GetUtcNow());
            return _byGrant.GetValueOrDefault(grantId);
        }
    }

    /// <summary>The bytes every live snapshot holds.</summary>
    public long TotalBytes
    {
        get
        {
            lock (_lock)
            {
                Purge(_time.GetUtcNow());
                return _byGrant.Values.Sum(s => s.Bytes);
            }
        }
    }

    private void Purge(DateTimeOffset now)
    {
        foreach (var expired in _byGrant.Where(e => e.Value.ExpiresAt <= now).Select(e => e.Key).ToList())
            _byGrant.Remove(expired);
        foreach (var old in _createdAt.Where(e => now - e.Value >= ManifestInterval).Select(e => e.Key).ToList())
            _createdAt.Remove(old);
    }
}

/// <summary>
/// The feed's refusals (§7.5, §7.6). The node controller answers each with the federation error shape and its
/// status, and a <c>Retry-After</c> header when one is given; the reader maps them to data sync peer errors.
/// </summary>
public static class DataSyncFeedErrors
{
    public const string BusyCode = "Busy";
    public const string TooManySnapshotsCode = "TooManySnapshots";
    public const string SnapshotTooLargeCode = "SnapshotTooLarge";
    public const string SnapshotExpiredCode = "SnapshotExpired";
    public const string SnapshotMismatchCode = "SnapshotMismatch";
    public const string SourceRestorePendingCode = "SourceRestorePending";
    public const string UnknownKindCode = "UnknownKind";

    /// <summary>A snapshot needs a Refresh, which waits while the actor is unverified after a start (§7.5.2).</summary>
    public const int RetryAfterUnverified = 30;

    /// <summary>Other readers' snapshots fill the memory the source gives snapshots (§7.5.2).</summary>
    public const int RetryAfterSnapshotSpace = 30;

    /// <summary>A restore choice waits for a person on this device (§7.5.2, §9.5).</summary>
    public const int RetryAfterRestorePending = 3600;

    /// <summary>503, retryable: the gate was not free in time, the actor is unverified, or snapshots use all their memory.</summary>
    public static DataSyncFeedException Busy(int? retryAfterSeconds = null) =>
        new(BusyCode, 503, retryable: true, retryAfterSeconds);

    /// <summary>429, retryable after the seconds left; the reader maps it to Busy.</summary>
    public static DataSyncFeedException TooManySnapshots(int retryAfterSeconds) =>
        new(TooManySnapshotsCode, 429, retryable: true, retryAfterSeconds);

    /// <summary>413, not retryable: more than one sync can carry (the reader shows TooLarge).</summary>
    public static DataSyncFeedException SnapshotTooLarge() => new(SnapshotTooLargeCode, 413);

    /// <summary>410, retryable with a new manifest.</summary>
    public static DataSyncFeedException SnapshotExpired() => new(SnapshotExpiredCode, 410, retryable: true);

    /// <summary>409: the page asked for is not one this snapshot served.</summary>
    public static DataSyncFeedException SnapshotMismatch() => new(SnapshotMismatchCode, 409);

    /// <summary>409, retryable after an hour: readers wait for the restore choice instead of merging older content.</summary>
    public static DataSyncFeedException SourceRestorePending() =>
        new(SourceRestorePendingCode, 409, retryable: true, RetryAfterRestorePending);

    /// <summary>404: the snapshot holds no such kind.</summary>
    public static DataSyncFeedException UnknownKind() => new(UnknownKindCode, 404);
}

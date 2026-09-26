using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

// The reader log (§7.5.6) and retention (§4.6).
public sealed partial class DataSyncStore
{
    public static readonly TimeSpan TombstoneServedFor = TimeSpan.FromDays(180);
    public static readonly TimeSpan ClosedItemsKeptFor = TimeSpan.FromDays(90);
    public static readonly TimeSpan HistoryKeptFor = TimeSpan.FromDays(30);
    public const int HistoryKeptAtLeast = 500;
    public const long HistoryPreImageBudgetBytes = 64L << 20;
    public static readonly TimeSpan ReadersKeptFor = TimeSpan.FromDays(180);

    #region Readers (§7.5.6)

    /// <summary>
    /// Remembers a reader's head or manifest. The row is written on the reader's first read in this process, when
    /// its declared mode or state changes, or when <see cref="DataSyncReaderLog.PersistInterval"/> passed since this
    /// process last wrote it; otherwise the read stays in memory (<see cref="GetReadersAsync"/> still shows it).
    /// </summary>
    public async Task TouchReaderAsync(DataSyncReader reader, DataSyncFeedQuery query, long seqServed, DateTime nowUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(query);
        var known = _readerLog.Get(reader.NodeId);
        var persist = DataSyncReaderLog.MustPersist(known, query.Mode, query.ReaderState, nowUtc);
        var entry = new DataSyncReaderLog.Entry(reader.Name, nowUtc, seqServed, query.Mode, query.ReaderState,
            known?.PersistedAtUtc, known?.PersistedMode, known?.PersistedState);
        if (persist)
        {
            await FlushAsync(ct);
            var row = await _db.DataSyncReaders.FindAsync([reader.NodeId], ct);
            if (row is null)
            {
                row = new DataSyncReaderDbModel {NodeId = reader.NodeId, FirstReadAtUtc = nowUtc};
                _db.DataSyncReaders.Add(row);
            }

            row.Name = reader.Name;
            row.LastReadAtUtc = nowUtc;
            row.LastSeqServed = seqServed;
            row.Mode = query.Mode;
            row.State = query.ReaderState;
            await _db.SaveChangesAsync(ct);
            entry = entry with {PersistedAtUtc = nowUtc, PersistedMode = query.Mode, PersistedState = query.ReaderState};
        }

        _readerLog.Set(reader.NodeId, entry);
    }

    /// <summary>Every reader, with the latest read this process saw (read-only copies; times UTC).</summary>
    public async Task<IReadOnlyList<DataSyncReaderDbModel>> GetReadersAsync(CancellationToken ct)
    {
        await FlushAsync(ct);
        var rows = await _db.DataSyncReaders.AsNoTracking().OrderBy(r => r.NodeId).ToListAsync(ct);
        foreach (var row in rows)
        {
            if (_readerLog.Get(row.NodeId) is { } latest && latest.LastReadAtUtc > row.LastReadAtUtc)
            {
                row.Name = latest.Name;
                row.LastReadAtUtc = latest.LastReadAtUtc;
                row.LastSeqServed = latest.LastSeqServed;
                row.Mode = latest.Mode;
                row.State = latest.State;
            }

            row.FirstReadAtUtc = DateTime.SpecifyKind(row.FirstReadAtUtc, DateTimeKind.Utc);
            row.LastReadAtUtc = DateTime.SpecifyKind(row.LastReadAtUtc, DateTimeKind.Utc);
            if (row.NotifiedAtUtc is { } notified)
                row.NotifiedAtUtc = DateTime.SpecifyKind(notified, DateTimeKind.Utc);
        }

        return rows;
    }

    /// <summary>
    /// "{0} started syncing definitions with this device" went out for this reader (§9.4). A reader is written on its
    /// first read in this process, so its row exists; an unknown node changes nothing.
    /// </summary>
    public async Task SetReaderNotifiedAsync(string nodeId, DateTime nowUtc, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(nodeId);
        await FlushAsync(ct);
        var row = await _db.DataSyncReaders.FindAsync([nodeId], ct);
        if (row is null) return;
        row.NotifiedAtUtc = nowUtc;
        await _db.SaveChangesAsync(ct);
    }

    #endregion

    #region Retention (§4.6)

    /// <summary>
    /// Retention (§4.6), in the caller's transaction:
    /// <list type="bullet">
    /// <item>a served tombstone deleted more than 180 days ago, and not served again since (row T2 serves it again
    /// with a new Seq and <c>UpdatedAtUtc</c>, which starts its serve window again), stops being served, and its
    /// kind's floor (<c>TombstoneFloorSeqsJson</c>) rises to its Seq; floors are per kind and never go down; the row is
    /// kept forever;</item>
    /// <item>items closed more than 90 days ago are deleted;</item>
    /// <item>apply logs: the newest 500 and every one newer than 30 days are kept, then the oldest go while the kept
    /// pre-images exceed 64 MiB; logs are pruned oldest first only, so a newer log is never pruned while an older
    /// one is kept (v3.1 N15), and the newest log is always kept;</item>
    /// <item>readers not seen for 180 days are forgotten;</item>
    /// <item>retired actors no stored vector names are forgotten, but none while a restore waits for its choice (§9.5:
    /// "This device's definitions win" covers them all); at most <see cref="DataSyncLimits.MaxActorsPerVector"/> are
    /// kept (the highest recorded counters).</item>
    /// </list>
    /// Bases and pending records live as long as their link. Backups are pruned by whoever writes them.
    /// </summary>
    public async Task PruneAsync(DateTime nowUtc, CancellationToken ct)
    {
        await FlushAsync(ct);
        var state = await LoadStateAsync(ct);

        // Tombstones: unserved, never deleted. A tombstone served again (row T2) has 180 days again from then: unserved
        // the next day, a peer that does not pull within it would meet T2 again and again, each time superseding every
        // reader below the kind's new floor.
        var tombstoneCutoff = nowUtc - TombstoneServedFor;
        var expiring = await _db.DataSyncEntities
            .Where(e => e.DeletedAtUtc != null && e.TombstoneServed && e.DeletedAtUtc < tombstoneCutoff &&
                        e.UpdatedAtUtc < tombstoneCutoff)
            .ToListAsync(ct);
        if (expiring.Count > 0)
        {
            if (state is null)
                throw new InvalidOperationException("Tombstones exist but data sync has no local state.");
            var floors = new Dictionary<string, long>(
                DataSyncStoredJson.ReadCounters(state.TombstoneFloorSeqsJson, "TombstoneFloorSeqsJson"),
                StringComparer.Ordinal);
            foreach (var tombstone in expiring)
            {
                tombstone.TombstoneServed = false;
                floors[tombstone.Kind] = Math.Max(floors.GetValueOrDefault(tombstone.Kind), tombstone.Seq);
            }

            state.TombstoneFloorSeqsJson = DataSyncStoredJson.WriteCounters(floors);
            state.UpdatedAtUtc = nowUtc;
        }

        // Closed items.
        var itemCutoff = nowUtc - ClosedItemsKeptFor;
        await _db.DataSyncInboxItems.Where(i => i.ClosedAtUtc != null && i.ClosedAtUtc < itemCutoff)
            .ExecuteDeleteAsync(ct);

        // Apply logs, oldest first only.
        var logs = await _db.DataSyncApplyLogs.AsNoTracking()
            .OrderByDescending(l => l.Id)
            .Select(l => new {l.Id, l.AppliedAtUtc, l.PreImageBytes})
            .ToListAsync(ct);
        var historyCutoff = nowUtc - HistoryKeptFor;
        var kept = 0;
        long keptBytes = 0;
        foreach (var log in logs)
        {
            if (kept >= HistoryKeptAtLeast && log.AppliedAtUtc < historyCutoff) break;
            if (kept > 0 && keptBytes + log.PreImageBytes > HistoryPreImageBudgetBytes) break;
            keptBytes += log.PreImageBytes;
            kept++;
        }

        if (kept < logs.Count)
        {
            var newestPruned = logs[kept].Id;
            await _db.DataSyncApplyLogs.Where(l => l.Id <= newestPruned).ExecuteDeleteAsync(ct);
        }

        // Readers.
        var readerCutoff = nowUtc - ReadersKeptFor;
        var staleReaders = await _db.DataSyncReaders.Where(r => r.LastReadAtUtc < readerCutoff).ToListAsync(ct);
        foreach (var reader in staleReaders)
        {
            if (_readerLog.Get(reader.NodeId) is { } latest && latest.LastReadAtUtc >= readerCutoff) continue;
            _db.DataSyncReaders.Remove(reader);
            _readerLog.Forget(reader.NodeId);
        }

        // Retired actors. While a restore waits for the person's choice (§9.5) every one is kept: an actor the restore
        // lost (actor.json or a peer named it, and every revision it issued is gone) is in no stored vector, yet "This
        // device's definitions win" must cover the counters it issued (gate fix B1(a)). That choice puts every retired
        // actor into every vector it revises, so the rule below keeps it from then on; "Take the other devices'
        // definitions" covers nothing, and a retired actor never issues a counter again, so forgetting it is safe.
        if (state is not null)
        {
            var retired = DataSyncStoredJson.ReadCounters(state.RetiredActorsJson, "RetiredActorsJson");
            if (retired.Count > 0)
            {
                var named = state.RestoreReason is null ? await CollectNamedActorsAsync(ct) : null;
                var keep = retired
                    .Where(e => named is null || named.Contains(e.Key))
                    .OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal)
                    .Take(DataSyncLimits.Default.MaxActorsPerVector)
                    .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
                if (keep.Count != retired.Count)
                {
                    state.RetiredActorsJson = DataSyncStoredJson.WriteCounters(keep);
                    state.UpdatedAtUtc = nowUtc;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Every actor any stored vector names: entities and tombstones, bases, pending records and items.</summary>
    private async Task<HashSet<string>> CollectNamedActorsAsync(CancellationToken ct)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);

        void Add(DataSyncVersionVector? vv)
        {
            if (vv is null) return;
            foreach (var actor in vv.Counters.Keys) named.Add(actor);
        }

        foreach (var vv in await _db.DataSyncEntities.AsNoTracking().Select(e => e.VvJson).ToListAsync(ct))
            Add(DataSyncVersionVector.ParseStored(vv));
        foreach (var peerBase in await _db.DataSyncPeerBases.AsNoTracking()
                     .Select(b => new {b.VvJson, b.PendingRecordJson}).ToListAsync(ct))
        {
            Add(DataSyncStoredJson.ReadVv(peerBase.VvJson));
            Add(DataSyncStoredJson.ReadRecordVv(peerBase.PendingRecordJson));
        }

        foreach (var item in await _db.DataSyncInboxItems.AsNoTracking()
                     .Select(i => new {i.RecordVvJson, i.LocalVvJson}).ToListAsync(ct))
        {
            Add(DataSyncStoredJson.ReadVv(item.RecordVvJson));
            Add(DataSyncStoredJson.ReadVv(item.LocalVvJson));
        }

        return named;
    }

    #endregion
}

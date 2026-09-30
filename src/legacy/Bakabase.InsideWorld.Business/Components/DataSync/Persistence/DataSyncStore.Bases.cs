using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

// Bases and pending records (§4.1, §8.4). A record this device did not agree to is stored ONCE, on its base row;
// inbox items refer to it by hash and never hold a copy.
public sealed partial class DataSyncStore
{
    /// <summary>The link's bases of one kind, with their pending records; read-only.</summary>
    public async Task<IReadOnlyList<DataSyncPeerBase>> GetBasesAsync(int linkId, string kind, CancellationToken ct)
    {
        await FlushAsync(ct);
        var rows = await _db.DataSyncPeerBases.AsNoTracking()
            .Where(b => b.LinkId == linkId && b.Kind == kind)
            .OrderBy(b => b.SyncKey)
            .ToListAsync(ct);
        return rows.Select(ToPeerBase).ToList();
    }

    /// <summary>
    /// What the link views count, per link, in one grouped query over the base rows' columns: no record is read or
    /// parsed (§11.2).
    /// </summary>
    public async Task<IReadOnlyDictionary<int, DataSyncBaseCounts>> CountBasesAsync(int? linkId, CancellationToken ct)
    {
        await FlushAsync(ct);
        var kinds = DataSyncKindIds.All.ToList();
        var rows = _db.DataSyncPeerBases.AsNoTracking().Where(b => kinds.Contains(b.Kind));
        if (linkId is { } id) rows = rows.Where(b => b.LinkId == id);
        var counts = await rows
            .GroupBy(b => b.LinkId)
            .Select(g => new
            {
                LinkId = g.Key,
                Pending = g.Count(b => b.PendingReason != null),
                Excluded = g.Count(b => b.State == DataSyncBaseState.Excluded),
                Held = g.Count(b => b.State == DataSyncBaseState.Held || b.PendingReason == DataSyncPendingReason.Held),
                Missing = g.Count(b => b.State == DataSyncBaseState.MissingAtPeer),
            })
            .ToListAsync(ct);
        return counts.ToDictionary(c => c.LinkId, c => new DataSyncBaseCounts(c.Pending, c.Excluded, c.Held, c.Missing));
    }

    internal static DataSyncPeerBase ToPeerBase(DataSyncPeerBaseDbModel row)
    {
        DataSyncPendingRecord? pending = null;
        if (row.PendingReason is { } reason)
        {
            var record = DataSyncStoredJson.ReadRecord(row.PendingRecordJson, "PendingRecordJson") ??
                         throw new System.IO.InvalidDataException(
                             $"Base {row.Id} has a pending reason but no pending record.");
            pending = new DataSyncPendingRecord(record, row.PendingRecordHash ?? "", reason,
                DataSyncStoredJson.ReadFlags(row.PendingFlagsJson, "PendingFlagsJson"));
        }

        return new DataSyncPeerBase(row.Kind, new SyncKey(row.SyncKey), row.State, row.ExclusionReason,
            DataSyncStoredJson.ReadVv(row.VvJson), DataSyncStoredJson.ReadChildMap(row.ChildMapJson), pending,
            DataSyncStoredJson.ReadRecord(row.RecordJson, "RecordJson"),
            DataSyncStoredJson.ReadStrings(row.ExclusionKeysJson, "ExclusionKeysJson"));
    }

    /// <summary>Every pending record of the link (§8.4), in merge order: by kind, then by the record's Seq and key.</summary>
    public async Task<IReadOnlyList<(string Kind, SyncKey Key, DataSyncPendingReason Reason)>> GetPendingAsync(
        int linkId, CancellationToken ct)
    {
        await FlushAsync(ct);
        var pending = await _db.DataSyncPeerBases.AsNoTracking()
            .Where(b => b.LinkId == linkId && b.PendingReason != null)
            .Select(b => new { b.Kind, b.SyncKey, Reason = b.PendingReason!.Value, b.PendingSeq })
            .ToListAsync(ct);
        return pending.OrderBy(p => p.Kind, StringComparer.Ordinal).ThenBy(p => p.PendingSeq ?? 0)
            .ThenBy(p => p.SyncKey, StringComparer.Ordinal)
            .Select(p => (p.Kind, new SyncKey(p.SyncKey), p.Reason))
            .ToList();
    }

    /// <summary>
    /// Writes the merger's base updates for one link (§8.4). Per update:
    /// <list type="bullet">
    /// <item>the row keyed (link, kind, key) is created when missing;</item>
    /// <item><c>State</c> and the exclusion reason are set;</item>
    /// <item>for an <c>Excluded</c> state, the record's keys and the base key join the exclusion keys (§5.2's
    /// exclusion index); the last agreement stays as it was, because an exclusion is not an agreement;</item>
    /// <item>otherwise a record is the new agreement: <c>RecordJson</c>, its vector and its comparison-form hash
    /// (this build's codec; null when no codec is registered or the record is not content); exclusion keys are
    /// cleared;</item>
    /// <item>a child map replaces the stored one;</item>
    /// <item>a pending record is stored once, replacing any earlier one; <c>ClearPending</c> without one clears
    /// it.</item>
    /// </list>
    /// </summary>
    public async Task UpsertBasesAsync(int linkId, IEnumerable<DataSyncBaseUpdate> updates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(updates);
        await FlushAsync(ct);
        var now = UtcNow;
        var byKey = new Dictionary<(string, string), DataSyncPeerBaseDbModel>();
        foreach (var update in updates)
        {
            var key = update.Key.Value ?? throw new ArgumentException("A base update needs a key.", nameof(updates));
            if (!byKey.TryGetValue((update.Kind, key), out var row))
            {
                row = await _db.DataSyncPeerBases.SingleOrDefaultAsync(
                    b => b.LinkId == linkId && b.Kind == update.Kind && b.SyncKey == key, ct);
                if (row is null)
                {
                    row = new DataSyncPeerBaseDbModel {LinkId = linkId, Kind = update.Kind, SyncKey = key};
                    _db.DataSyncPeerBases.Add(row);
                }

                byKey[(update.Kind, key)] = row;
            }

            row.State = update.State;
            row.ExclusionReason = update.State == DataSyncBaseState.Excluded ? update.Exclusion : null;
            if (update.State == DataSyncBaseState.Excluded)
            {
                var exclusionKeys = new SortedSet<string>(
                    DataSyncStoredJson.ReadStrings(row.ExclusionKeysJson, "ExclusionKeysJson"), StringComparer.Ordinal)
                {
                    key,
                };
                foreach (var recordKey in update.Record?.Keys ?? []) exclusionKeys.Add(recordKey);
                row.ExclusionKeysJson = DataSyncStoredJson.Write(exclusionKeys.ToList());
            }
            else
            {
                row.ExclusionKeysJson = null;
                if (update.Record is { } record)
                {
                    row.RecordJson = DataSyncStoredJson.Write(record);
                    row.VvJson = record.Vv.ToCanonicalString();
                    row.SharedHash = SharedHashOf(update.Kind, record);
                }
            }

            if (update.ChildMap is { } childMap) row.ChildMapJson = DataSyncStoredJson.WriteChildMap(childMap);

            if (update.Pending is { } pending) SetPending(row, pending);
            else if (update.ClearPending) ClearPending(row);

            row.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Excludes <paramref name="keys"/> on one link under the base row keyed <paramref name="key"/> (§8.11 undo, [Include]
    /// reverses it): the row is created when missing, its exclusion keys take <paramref name="key"/>, the given keys and
    /// the keys of the record it held (agreed or pending), and its pending record is cleared. The last agreement stays.
    /// </summary>
    internal async Task ExcludeAsync(int linkId, string kind, string key, DataSyncExclusionReason reason,
        IEnumerable<string> keys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);
        await FlushAsync(ct);
        var row = await _db.DataSyncPeerBases.SingleOrDefaultAsync(
            b => b.LinkId == linkId && b.Kind == kind && b.SyncKey == key, ct);
        if (row is null)
        {
            row = new DataSyncPeerBaseDbModel {LinkId = linkId, Kind = kind, SyncKey = key};
            _db.DataSyncPeerBases.Add(row);
        }

        var exclusionKeys = new SortedSet<string>(
            DataSyncStoredJson.ReadStrings(row.ExclusionKeysJson, "ExclusionKeysJson"), StringComparer.Ordinal) {key};
        exclusionKeys.UnionWith(keys);
        exclusionKeys.UnionWith(DataSyncStoredJson.ReadRecordKeys(row.RecordJson));
        exclusionKeys.UnionWith(DataSyncStoredJson.ReadRecordKeys(row.PendingRecordJson));
        row.State = DataSyncBaseState.Excluded;
        row.ExclusionReason = reason;
        row.ExclusionKeysJson = DataSyncStoredJson.Write(exclusionKeys.ToList());
        ClearPending(row);
        row.UpdatedAtUtc = UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    internal static void SetPending(DataSyncPeerBaseDbModel row, DataSyncPendingRecord pending)
    {
        row.PendingRecordJson = DataSyncStoredJson.Write(pending.Record);
        row.PendingRecordHash = pending.RecordHash;
        row.PendingSeq = pending.Record.Seq;
        row.PendingReason = pending.Reason;
        row.PendingFlagsJson = DataSyncStoredJson.WriteFlags(pending.Flags);
    }

    internal static void ClearPending(DataSyncPeerBaseDbModel row)
    {
        row.PendingRecordJson = null;
        row.PendingRecordHash = null;
        row.PendingSeq = null;
        row.PendingReason = null;
        row.PendingFlagsJson = null;
    }

    /// <summary>
    /// The comparison-form hash of an agreed record with this build's codec (§3.4); null for a tombstone, a held
    /// record, a record this build holds, or a kind without a registered codec. Recomputed when the codec's
    /// ComparisonFormVersion changes (§6.1).
    /// </summary>
    internal string? SharedHashOf(string kind, DataSyncWireRecord record) =>
        Kinds.TryGetValue(kind, out var adapter)
            ? DataSyncEntityForms.RecordSharedHash(adapter.Codec, record,
                _services.GetRequiredService<DataSyncLimits>())
            : null;

    /// <summary>
    /// Retire and rekey (§5.3): on every link, the base keyed <paramref name="fromKey"/> is re-keyed to
    /// <paramref name="toKey"/>, unless that link already has a base for <paramref name="toKey"/>, which then wins
    /// and the other is deleted.
    /// </summary>
    public async Task RepointBasesAsync(string kind, string fromKey, string toKey, CancellationToken ct)
    {
        await FlushAsync(ct);
        if (fromKey == toKey) return;
        var from = await _db.DataSyncPeerBases.Where(b => b.Kind == kind && b.SyncKey == fromKey).ToListAsync(ct);
        if (from.Count == 0) return;
        var linksWithTarget = (await _db.DataSyncPeerBases.Where(b => b.Kind == kind && b.SyncKey == toKey)
            .Select(b => b.LinkId).ToListAsync(ct)).ToHashSet();
        var now = UtcNow;
        foreach (var row in from)
        {
            if (linksWithTarget.Contains(row.LinkId))
            {
                _db.DataSyncPeerBases.Remove(row);
            }
            else
            {
                row.SyncKey = toKey;
                row.UpdatedAtUtc = now;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Rekey (§5.3): the bases and pending records keyed <paramref name="key"/> are deleted on every link.</summary>
    internal async Task<int> DeleteBasesOfKeyAsync(string kind, string key, CancellationToken ct)
    {
        var rows = await _db.DataSyncPeerBases.Where(b => b.Kind == kind && b.SyncKey == key).ToListAsync(ct);
        _db.DataSyncPeerBases.RemoveRange(rows);
        return rows.Count;
    }

    private async Task<List<DataSyncPeerBaseDbModel>> BasesOfEntityAsync(string kind, IReadOnlyCollection<string> keys,
        CancellationToken ct) =>
        await _db.DataSyncPeerBases.Where(b => b.Kind == kind && keys.Contains(b.SyncKey)).ToListAsync(ct);
}

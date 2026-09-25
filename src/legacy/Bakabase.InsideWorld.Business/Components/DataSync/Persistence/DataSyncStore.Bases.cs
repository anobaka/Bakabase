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
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;

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

    internal static DataSyncPeerBase ToPeerBase(DataSyncPeerBaseDbModel row)
    {
        DataSyncPendingRecord? pending = null;
        if (row.PendingReason is { } reason)
        {
            var record = DataSyncStoredJson.ReadRecord(row.PendingRecordJson, "PendingRecordJson") ??
                         throw new System.IO.InvalidDataException(
                             $"Base {row.Id} has a pending reason but no pending record.");
            pending = new DataSyncPendingRecord(record, row.PendingRecordHash ?? "", reason,
                row.PendingEvaluatedLocalSeq ?? 0, DataSyncStoredJson.ReadFlags(row.PendingFlagsJson, "PendingFlagsJson"),
                string.IsNullOrEmpty(row.PendingAppliedBaseJson)
                    ? null
                    : DataSyncStoredJson.Read<DataSyncAppliedBase>(row.PendingAppliedBaseJson, "PendingAppliedBaseJson"));
        }

        return new DataSyncPeerBase(row.Kind, new SyncKey(row.SyncKey), row.State, row.ExclusionReason,
            DataSyncStoredJson.ReadVv(row.VvJson), DataSyncStoredJson.ReadChildMap(row.ChildMapJson), pending,
            DataSyncStoredJson.ReadRecord(row.RecordJson, "RecordJson"),
            DataSyncStoredJson.ReadStrings(row.ExclusionKeysJson, "ExclusionKeysJson"));
    }

    /// <summary>
    /// The pending records to re-merge this pull (§8.4), in merge order: by kind, <c>OverBudget</c> first, then by
    /// the record's Seq and primary key. A pending record is re-merged when
    /// <list type="number">
    /// <item>(a newer record for the key in the pull replaces it — the merger's rule, not a query);</item>
    /// <item>its local entity's Seq moved past <c>PendingEvaluatedLocalSeq</c> (or it was never evaluated);</item>
    /// <item>its reason is <c>Retry</c> or <c>OverBudget</c>;</item>
    /// <item>the pull is a full reconciliation;</item>
    /// <item>a once flag of the link targets it: <c>SkipLargeChange</c> the <c>LargeChange</c> records, B2's
    /// <c>SkipDeletionBreaker</c>/<c>DeletionsAsItems</c> the deletions, a child-deletion mode the
    /// <c>MassChildDeletion</c> records;</item>
    /// <item>its reason is <c>Held</c> and this build's schema version of the kind differs from the one recorded
    /// in <c>KindSchemaVersionsJson</c>.</item>
    /// </list>
    /// </summary>
    public async Task<IReadOnlyList<(string Kind, SyncKey Key)>> GetPendingToMergeAsync(int linkId,
        bool fullReconciliation, CancellationToken ct)
    {
        await FlushAsync(ct);
        var pending = await _db.DataSyncPeerBases.AsNoTracking()
            .Where(b => b.LinkId == linkId && b.PendingReason != null)
            .Select(b => new
            {
                b.Kind, b.SyncKey, Reason = b.PendingReason!.Value, b.PendingSeq, b.PendingEvaluatedLocalSeq,
                b.PendingRecordJson,
            })
            .ToListAsync(ct);
        if (pending.Count == 0) return [];

        var link = await _db.DataSyncLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId, ct);
        var onceFlags = DataSyncStoredJson.ReadFlags(link?.OnceFlagsJson, "OnceFlagsJson");

        var kinds = pending.Select(p => p.Kind).Distinct().ToList();
        var keys = pending.Select(p => p.SyncKey).Distinct().ToList();
        var localSeqs = await _db.DataSyncEntities.AsNoTracking()
            .Where(e => kinds.Contains(e.Kind) && keys.Contains(e.SyncKey))
            .Select(e => new {e.Kind, e.SyncKey, e.Seq})
            .ToListAsync(ct);
        var seqByKey = localSeqs.ToDictionary(e => (e.Kind, e.SyncKey), e => e.Seq);

        IReadOnlyDictionary<string, int>? recordedSchemas = null;
        if (pending.Any(p => p.Reason == DataSyncPendingReason.Held))
        {
            var state = await LoadStateAsync(ct);
            recordedSchemas = DataSyncStoredJson.ReadVersions(state?.KindSchemaVersionsJson, "KindSchemaVersionsJson");
        }

        bool SchemaChanged(string kind)
        {
            var current = Kinds.TryGetValue(kind, out var adapter) ? adapter.Codec.Descriptor.SchemaVersion : (int?) null;
            var recorded = recordedSchemas!.TryGetValue(kind, out var v) ? v : (int?) null;
            return current != recorded;
        }

        bool TargetedByOnceFlags(DataSyncPendingReason reason, string? recordJson) =>
            reason switch
            {
                DataSyncPendingReason.LargeChange => onceFlags.SkipLargeChange,
                DataSyncPendingReason.MassChildDeletion => onceFlags.ChildDeletions != DataSyncChildDeletionMode.Normal,
                _ => (onceFlags.SkipDeletionBreaker || onceFlags.DeletionsAsItems) && IsDeletion(recordJson),
            };

        return pending
            .Where(p =>
                fullReconciliation ||
                p.Reason is DataSyncPendingReason.Retry or DataSyncPendingReason.OverBudget ||
                p.PendingEvaluatedLocalSeq is null ||
                (seqByKey.TryGetValue((p.Kind, p.SyncKey), out var localSeq) && localSeq > p.PendingEvaluatedLocalSeq) ||
                TargetedByOnceFlags(p.Reason, p.PendingRecordJson) ||
                (p.Reason == DataSyncPendingReason.Held && SchemaChanged(p.Kind)))
            .OrderBy(p => p.Kind, StringComparer.Ordinal)
            .ThenBy(p => p.Reason == DataSyncPendingReason.OverBudget ? 0 : 1)
            .ThenBy(p => p.PendingSeq ?? 0)
            .ThenBy(p => p.SyncKey, StringComparer.Ordinal)
            .Select(p => (p.Kind, new SyncKey(p.SyncKey)))
            .ToList();
    }

    private static bool IsDeletion(string? recordJson)
    {
        if (string.IsNullOrEmpty(recordJson)) return false;
        return JsonNode.Parse(recordJson) is JsonObject record && record["deleted"] is JsonValue deleted &&
               deleted.TryGetValue<bool>(out var value) && value;
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
        row.PendingEvaluatedLocalSeq = pending.EvaluatedAtLocalSeq;
        row.PendingFlagsJson = DataSyncStoredJson.WriteFlags(pending.Flags);
        // What a conflicted merge applied stays with the row until the base advances (§8.4 row K6).
        row.PendingAppliedBaseJson = pending.AppliedBase is null ? null : DataSyncStoredJson.Write(pending.AppliedBase);
    }

    internal static void ClearPending(DataSyncPeerBaseDbModel row)
    {
        row.PendingRecordJson = null;
        row.PendingRecordHash = null;
        row.PendingSeq = null;
        row.PendingReason = null;
        row.PendingEvaluatedLocalSeq = null;
        row.PendingFlagsJson = null;
        row.PendingAppliedBaseJson = null;
    }

    /// <summary>
    /// The comparison-form hash of an agreed record with this build's codec (§3.4); null for a tombstone, a held
    /// record, a record this build holds, or a kind without a registered codec. Recomputed when the codec's
    /// ComparisonFormVersion changes (§6.1).
    /// </summary>
    internal string? SharedHashOf(string kind, DataSyncWireRecord record) =>
        Kinds.TryGetValue(kind, out var adapter) ? DataSyncEntityForms.RecordSharedHash(adapter.Codec, record) : null;

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

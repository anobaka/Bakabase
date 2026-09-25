using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// Pending records (§8.4, §7.5.5): a peer record this device received but did not agree to is stored ONCE per
/// link and entity, on the base row, and inbox items refer to it by <see cref="RecordHashOf"/> and never copy it.
/// These helpers are shared by the merger, the store [C] and the simulator.
/// </summary>
public static class DataSyncPendingRecords
{
    /// <summary>
    /// The hash items refer to a pending record by: <c>ContentHash</c> of the record's canonical wire JSON
    /// (<see cref="DataSyncWireFormat.ToJson(DataSyncWireRecord)"/>), so a tombstone and a held record have one too.
    /// </summary>
    public static string RecordHashOf(DataSyncWireRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ContentHash.Of(DataSyncWireFormat.ToJson(record));
    }

    /// <summary>A pending record for <paramref name="record"/>, with its hash.</summary>
    /// <param name="evaluatedAtLocalSeq">
    /// The local entity's Seq when the record was merged (0 when it binds to nothing). The apply runner raises it to
    /// the entity's new Seq when the same apply revised the entity (<see cref="WithEvaluatedSeqs"/>), so the record
    /// is not re-merged just because of that revision.
    /// </param>
    public static DataSyncPendingRecord Create(DataSyncWireRecord record, DataSyncPendingReason reason,
        long evaluatedAtLocalSeq, DataSyncMergeFlags flags)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(flags);
        return new DataSyncPendingRecord(record, RecordHashOf(record), reason, evaluatedAtLocalSeq, flags);
    }

    /// <summary>
    /// A stored pending record staged again through this build's codec, exactly as a pull is
    /// (<see cref="DataSyncRecordValidation.Stage"/>): a record held before may be valid after an upgrade (§8.4
    /// condition 6), and one valid before is still checked.
    /// </summary>
    public static DataSyncIncomingEntity Stage(IDataSyncKindCodec? codec, DataSyncPendingRecord pending,
        DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return DataSyncRecordValidation.Stage(codec, pending.Record, 0, limits);
    }

    /// <summary>
    /// Whether a pending record is re-merged this pull (§8.4 conditions 2–6; condition 1, a newer record for the same
    /// key in the pull replacing it, is the merger's own rule):
    /// <list type="number">
    /// <item value="2">the local entity's Seq is greater than <see cref="DataSyncPendingRecord.EvaluatedAtLocalSeq"/>;</item>
    /// <item value="3">its reason is <c>Retry</c> or <c>OverBudget</c>;</item>
    /// <item value="4">the pull is a full reconciliation;</item>
    /// <item value="5">a once flag of the link targets its reason: <c>SkipLargeChange</c> a <c>LargeChange</c>
    /// record, a child-deletion mode a <c>MassChildDeletion</c> record, B2's flags a deletion;</item>
    /// <item value="6">its reason is <c>Held</c> and this build's schema version of the kind changed.</item>
    /// </list>
    /// </summary>
    /// <param name="localSeq">The Seq of the row the pending record is stored against; null when there is none.</param>
    public static bool ShouldRemerge(DataSyncPendingRecord pending, long? localSeq, bool fullReconciliation,
        DataSyncMergeFlags linkOnceFlags, bool schemaVersionChanged)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(linkOnceFlags);
        if (fullReconciliation) return true;
        if (pending.Reason is DataSyncPendingReason.Retry or DataSyncPendingReason.OverBudget) return true;
        if (localSeq is { } seq && seq > pending.EvaluatedAtLocalSeq) return true;
        if (pending.Reason == DataSyncPendingReason.Held && schemaVersionChanged) return true;
        return pending.Reason switch
        {
            DataSyncPendingReason.LargeChange => linkOnceFlags.SkipLargeChange,
            DataSyncPendingReason.MassChildDeletion => linkOnceFlags.ChildDeletions != DataSyncChildDeletionMode.Normal,
            _ => pending.Record.Deleted && (linkOnceFlags.SkipDeletionBreaker || linkOnceFlags.DeletionsAsItems),
        };
    }

    /// <summary>
    /// The flags a record is merged with: the ones stored with its pending record and item, and the link's once
    /// flags (§8.7). Booleans combine with OR; a child-deletion mode other than <c>Normal</c> wins, the stored one
    /// first.
    /// </summary>
    public static DataSyncMergeFlags Combine(DataSyncMergeFlags stored, DataSyncMergeFlags once)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(once);
        return new DataSyncMergeFlags(
            stored.DeletionsAsItems || once.DeletionsAsItems,
            stored.SkipDeletionBreaker || once.SkipDeletionBreaker,
            stored.SkipLargeChange || once.SkipLargeChange,
            stored.ChildDeletions != DataSyncChildDeletionMode.Normal ? stored.ChildDeletions : once.ChildDeletions);
    }

    /// <summary>
    /// Raises <see cref="DataSyncPendingRecord.EvaluatedAtLocalSeq"/> of the pending records a merge wrote to the
    /// Seq their entity got in the same apply (<paramref name="newSeqByBaseKey"/>, keyed like the base updates), so
    /// that revision alone does not re-merge them next pull (§8.4 condition 2).
    /// </summary>
    public static IReadOnlyList<DataSyncBaseUpdate> WithEvaluatedSeqs(IReadOnlyList<DataSyncBaseUpdate> updates,
        IReadOnlyDictionary<(string Kind, SyncKey Key), long> newSeqByBaseKey)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(newSeqByBaseKey);
        return updates.Select(u =>
                u.Pending is { } pending && newSeqByBaseKey.TryGetValue((u.Kind, u.Key), out var seq) &&
                seq > pending.EvaluatedAtLocalSeq
                    ? u with { Pending = pending with { EvaluatedAtLocalSeq = seq } }
                    : u)
            .ToList();
    }
}

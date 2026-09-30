using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// Pending records (§8.4, §7.5.5): a peer record this device received but did not agree to is stored ONCE per
/// link and entity, on the base row, and inbox items refer to it by <see cref="RecordHashOf"/> and never copy it.
/// These helpers are shared by the merger and the store [C].
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
    public static DataSyncPendingRecord Create(DataSyncWireRecord record, DataSyncPendingReason reason,
        DataSyncMergeFlags flags)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(flags);
        return new DataSyncPendingRecord(record, RecordHashOf(record), reason, flags);
    }

    /// <summary>
    /// A stored pending record staged again through this build's codec, exactly as a pull is
    /// (<see cref="DataSyncRecordValidation.Stage"/>): a record held before may be valid after an upgrade, and one
    /// valid before is still checked.
    /// </summary>
    public static DataSyncIncomingEntity Stage(IDataSyncKindCodec? codec, DataSyncPendingRecord pending,
        DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return DataSyncRecordValidation.Stage(codec, pending.Record, 0, limits);
    }
}

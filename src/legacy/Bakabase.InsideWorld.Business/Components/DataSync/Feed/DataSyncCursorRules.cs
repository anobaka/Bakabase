using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync.Models.Db;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Feed;

/// <summary>
/// When a reader's cursor cannot be served incrementally (§7.5.1 step 3, §7.5.2 step 4, §7.5.5): the kind is then
/// served from 0 in the same snapshot, where missing means unknown (§8.8).
/// </summary>
public static class DataSyncCursorRules
{
    /// <summary>
    /// A kind's cursor is superseded when <c>0 &lt; since &lt;</c> that kind's own tombstone floor (the reader may have
    /// missed a tombstone retention stopped serving), when <c>since &gt; LastSeq</c> (this database lost sequence
    /// numbers), or when the reader is a recorded reader-ahead. Floors are per kind: a kind is superseded only by its
    /// own floor, never by another kind's (§4.6, gate fix B2).
    /// </summary>
    public static bool IsSuperseded(long since, long kindFloor, long lastSeq, bool recordedReaderAhead) =>
        recordedReaderAhead || since > lastSeq || (since > 0 && since < kindFloor);

    /// <summary>The kind's floor: <c>TombstoneFloorSeqsJson[kind]</c>, 0 when absent (§4.1).</summary>
    public static long FloorOf(DataSyncLocalStateDbModel state, string kind)
    {
        ArgumentNullException.ThrowIfNull(state);
        return DataSyncStoredJson.ReadCounters(state.TombstoneFloorSeqsJson, "TombstoneFloorSeqsJson")
            .GetValueOrDefault(kind);
    }

    /// <summary>§7.5.1 step 1: some cursor is above <c>LastSeq</c>; the source reports the reader ahead first.</summary>
    public static bool IsReaderAhead(IReadOnlyDictionary<string, long> since, long lastSeq)
    {
        ArgumentNullException.ThrowIfNull(since);
        return since.Values.Any(s => s > lastSeq);
    }
}

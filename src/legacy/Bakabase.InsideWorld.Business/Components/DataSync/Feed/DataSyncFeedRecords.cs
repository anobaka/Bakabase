using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Feed;

/// <summary>
/// The wire record a snapshot serves for one entity row (§7.5.4, §3.5): its keys, origin, Seq, vector and editor, and
/// — for a live entity — exactly what the snapshot's Refresh published for it. Nothing local (local keys, overlays,
/// hashes of local content) ever reaches a record.
/// </summary>
public static class DataSyncFeedRecords
{
    /// <summary>
    /// The record of <paramref name="row"/>, which the feed serves (a live Synced row or a served tombstone):
    /// <list type="bullet">
    /// <item>a tombstone carries no content, hash or order key;</item>
    /// <item>a live row carries what Refresh published: its content and hash with the order key (kinds with order
    /// only), or — when Refresh held it (the lost-update guard, an unreadable row, content a reader would hold) —
    /// <c>HeldAtSource</c> with the reason and nothing else. A live row Refresh published nothing for is served held
    /// <c>AtSource</c> rather than with content read a second time (§6.6).</item>
    /// </list>
    /// Keys are the primary, then the aliases in ordinal order, at most <c>MaxKeysPerEntity</c>.
    /// </summary>
    public static DataSyncWireRecord ToRecord(DataSyncEntityDbModel row, IEnumerable<string> aliases,
        DataSyncKindDescriptor descriptor, DataSyncPublishedEntity? published, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(limits);
        var keys = KeysOf(row.SyncKey, aliases, limits.MaxKeysPerEntity);
        var vv = DataSyncVersionVector.ParseStored(row.VvJson);
        var editor = EditorOf(row);

        if (row.DeletedAtUtc is not null)
        {
            return new DataSyncWireRecord(keys, row.OriginNodeId, row.Seq, vv, editor, true, descriptor.SchemaVersion,
                null, null, null, null, 0);
        }

        if (published is not {Held: null, Content: not null, Hash: not null})
        {
            var reason = published?.Held ?? DataSyncHeldReason.AtSource;
            return new DataSyncWireRecord(keys, row.OriginNodeId, row.Seq, vv, editor, false, descriptor.SchemaVersion,
                null, null, null, reason, 0);
        }

        return new DataSyncWireRecord(keys, row.OriginNodeId, row.Seq, vv, editor, false, descriptor.SchemaVersion,
            descriptor.HasOrder ? row.OrderKey : null, published.Content, published.Hash, null, 0);
    }

    /// <summary>
    /// The primary first, then the aliases (§5.3). An entity with more keys than a record may carry keeps the
    /// ordinally first aliases, so what it serves is stable.
    /// </summary>
    public static IReadOnlyList<string> KeysOf(string primary, IEnumerable<string> aliases, int maxKeys)
    {
        var keys = new List<string> {primary};
        keys.AddRange(aliases.Where(a => a != primary).Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal).Take(Math.Max(0, maxKeys - 1)));
        return keys;
    }

    /// <summary>Who produced the row's revision, as this device knows it; null when it is not fully known.</summary>
    private static DataSyncEditorRef? EditorOf(DataSyncEntityDbModel row) =>
        row.LastActorId is { } actor && DataSyncActorId.IsValid(actor) && row.LastEditorNodeId is {Length: > 0} node
            ? new DataSyncEditorRef(node, row.LastEditorName ?? "", actor)
            : null;
}

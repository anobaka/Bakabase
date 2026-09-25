using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// One kind's local state as the merger and the first-contact planner see it (§2.4), read after Refresh in the same
/// transaction: live entities with their local content (<c>ReadLocal</c>, never validated), keys, vectors, editors,
/// overlays and flags, and every tombstone, served or not. An entity the lost-update guard holds carries
/// <c>PublishHeld</c>, which freezes incoming merges of it (§6.5, §8.4 row F).
/// </summary>
public sealed class DataSyncLocalStateReader(DataSyncStore store, DataSyncIdentityStore identity)
{
    /// <remarks>
    /// <c>ValueCount</c> is left null: the merger asks for the usage it needs (§2.7 <c>CollectUsageQueries</c>). A live
    /// row whose definition is gone since the Refresh is left out; the next Refresh tombstones it.
    /// </remarks>
    public async Task<DataSyncLocalKindState> ReadAsync(string kind, CancellationToken ct)
    {
        if (!store.Kinds.TryGetValue(kind, out var adapter))
            throw new InvalidOperationException($"No data sync kind adapter is registered for '{kind}'.");
        var codec = adapter.Codec;
        var rows = await store.GetEntitiesAsync(kind, includeTombstones: true, ct);
        var index = await identity.GetKeyIndexAsync(kind, null, ct);
        var keysById = index.Entities.ToDictionary(e => e.Id, e => e.Keys);

        var liveKeys = rows.Where(r => r.DeletedAtUtc is null).Select(r => r.LocalKey).ToList();
        var locals = liveKeys.Count == 0
            ? new Dictionary<string, LocalEntity>(StringComparer.Ordinal)
            : (await adapter.ReadAsync(liveKeys, ct)).ToDictionary(l => l.LocalKey, StringComparer.Ordinal);

        var entities = new List<DataSyncLocalEntityState>();
        var tombstones = new List<DataSyncTombstoneState>();
        foreach (var row in rows)
        {
            var keys = keysById[row.Id];
            var vv = DataSyncVersionVector.ParseStored(row.VvJson);
            if (row.DeletedAtUtc is not null)
            {
                tombstones.Add(new DataSyncTombstoneState(keys, vv, EditorOf(row), row.State,
                    row.TombstoneKind ?? DataSyncTombstoneKind.Deleted, row.TombstoneServed));
                continue;
            }

            if (!locals.TryGetValue(row.LocalKey, out var local)) continue;
            entities.Add(new DataSyncLocalEntityState(row.LocalKey, keys, codec.ReadLocal(local.Content), row.LocalHash,
                row.SharedHash, vv, DataSyncActorId.IsValid(row.LastActorId) ? new DataSyncActorId(row.LastActorId!) : null,
                EditorOf(row), row.OrderKey, row.State, DataSyncStoredJson.ReadOverlay(row.OverlayJson), row.ChildrenLocal,
                row.CreatedBySync, row.PublishHeld, DataSyncEntityForms.ReadUnknown(row.UnknownJson), null, row.Seq,
                row.Unreadable || local.Unreadable));
        }

        return new DataSyncLocalKindState(kind, entities, tombstones);
    }

    private static DataSyncEditorRef? EditorOf(DataSyncEntityDbModel row) =>
        row.LastEditorNodeId is { } node && DataSyncActorId.IsValid(row.LastActorId)
            ? new DataSyncEditorRef(node, row.LastEditorName ?? "", row.LastActorId!)
            : null;
}

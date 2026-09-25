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
    /// <param name="contents">
    /// Contents read earlier in the same transaction, by local key and <c>LocalHash</c>: an entity whose side row still
    /// has that hash is not read through the adapter again. Every write of the apply path re-reads what it wrote and
    /// stores its hash, so a hash that did not move means content that did not either. Null reads every entity.
    /// </param>
    public async Task<DataSyncLocalKindState> ReadAsync(string kind, CancellationToken ct,
        DataSyncLocalContentCache? contents = null)
    {
        if (!store.Kinds.TryGetValue(kind, out var adapter))
            throw new InvalidOperationException($"No data sync kind adapter is registered for '{kind}'.");
        var codec = adapter.Codec;
        var rows = await store.ReadEntitiesAsync(kind, includeTombstones: true, ct);
        var index = await identity.GetKeyIndexAsync(kind, null, ct);
        var keysById = index.Entities.ToDictionary(e => e.Id, e => e.Keys);

        var live = rows.Where(r => r.DeletedAtUtc is null).ToList();
        var known = new Dictionary<string, DataSyncLocalContentCache.Entry>(StringComparer.Ordinal);
        var toRead = new List<string>();
        foreach (var row in live)
        {
            if (contents?.TryGet(kind, row.LocalKey, row.LocalHash) is { } entry) known[row.LocalKey] = entry;
            else toRead.Add(row.LocalKey);
        }

        if (toRead.Count > 0)
        {
            var hashes = live.ToDictionary(r => r.LocalKey, r => r.LocalHash, StringComparer.Ordinal);
            foreach (var local in await adapter.ReadAsync(toRead, ct))
            {
                var entry = new DataSyncLocalContentCache.Entry(codec.ReadLocal(local.Content), local.Unreadable);
                known[local.LocalKey] = entry;
                if (hashes.TryGetValue(local.LocalKey, out var hash)) contents?.Put(kind, local.LocalKey, hash, entry);
            }
        }

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

            if (!known.TryGetValue(row.LocalKey, out var local)) continue;
            entities.Add(new DataSyncLocalEntityState(row.LocalKey, keys, local.Content, row.LocalHash,
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

/// <summary>
/// Local contents a long task read inside one transaction (<see cref="DataSyncLocalStateReader.ReadAsync"/>), by kind,
/// local key and the side row's <c>LocalHash</c>. Valid only while that transaction holds the write lock: clear it
/// whenever the task commits, since other writers may change definitions between its transactions.
/// </summary>
public sealed class DataSyncLocalContentCache
{
    private readonly Dictionary<(string Kind, string LocalKey), (string Hash, Entry Entry)> _entries = new();

    /// <summary>The typed local content (<c>ReadLocal</c>) and whether the stored row could be read.</summary>
    public sealed record Entry(object Content, bool Unreadable);

    public Entry? TryGet(string kind, string localKey, string localHash) =>
        _entries.TryGetValue((kind, localKey), out var cached) &&
        string.Equals(cached.Hash, localHash, StringComparison.Ordinal)
            ? cached.Entry
            : null;

    public void Put(string kind, string localKey, string localHash, Entry entry) =>
        _entries[(kind, localKey)] = (localHash, entry);

    public void Clear() => _entries.Clear();
}

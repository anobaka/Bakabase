using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// Follows an entity's children through a rebuild that gave them new local ids — a subtype change (F73: the
/// converted options are rebuilt from the values with fresh ids), or undo converting it back to the captured ids —
/// everywhere data sync keeps a local child id: the overlay's held and local-only children (§3.6, §8.5.4), and every
/// link's child map of the entity. A child goes to the first child of its label class after the rebuild
/// (<see cref="IDataSyncKindCodec.MapChildrenByClass"/>). Without this a held child would be published again by the
/// merge that follows the conversion — undoing the peer's deletion before anyone decided — its question could no
/// longer be answered, and a child kept on this device only would lose that setting.
/// </summary>
/// <remarks>
/// A held or local-only child whose class did not survive the rebuild is dropped from the overlay: there is nothing
/// left to withhold. A peer child the entity matched by its id alone (sync created the child with the peer's id, so
/// the map had no entry for it) gets an explicit entry to the child's new id, so the item that names the peer's id
/// still finds its hold.
/// </remarks>
internal static class DataSyncChildIdRemap
{
    /// <summary>Remaps the tracked <paramref name="row"/>'s overlay and every base of the entity; saves nothing.</summary>
    /// <param name="before">The entity's <c>ReadLocal</c> content before the rebuild.</param>
    /// <param name="after">The entity's <c>ReadLocal</c> content after it.</param>
    public static async Task ApplyAsync(DataSyncApplySession s, DataSyncEntityDbModel row, object before, object after,
        CancellationToken ct)
    {
        var codec = s.Adapter(row.Kind).Codec;
        var map = codec.MapChildrenByClass(before, after);
        var remaining = codec.ChildrenOf(after).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        string? To(string id) => remaining.Contains(id) ? id : map.GetValueOrDefault(id);

        var overlay = DataSyncStoredJson.ReadOverlay(row.OverlayJson);
        var remapped = new DataSyncOverlay(
            overlay.LocalOnlyChildren.Select(To).OfType<string>().Distinct(StringComparer.Ordinal).ToList(),
            overlay.HeldChildren.Select(h => To(h.ChildId) is { } id ? h with { ChildId = id } : null)
                .OfType<DataSyncHeldChild>().Distinct().ToList());
        if (!remapped.LocalOnlyChildren.SequenceEqual(overlay.LocalOnlyChildren, StringComparer.Ordinal) ||
            !remapped.HeldChildren.SequenceEqual(overlay.HeldChildren))
        {
            row.OverlayJson = DataSyncStoredJson.WriteOverlay(remapped);
            // Marked for Refresh: what the entity withholds changed ids (§6.1).
            row.RawHash = null;
            row.UpdatedAtUtc = s.Now;
        }

        if (map.Count == 0) return;
        var keys = (await s.KeysOfAsync(row, ct)).All.Select(k => k.Value).ToList();
        foreach (var peerBase in await s.Db.DataSyncPeerBases
                     .Where(b => b.Kind == row.Kind && keys.Contains(b.SyncKey)).ToListAsync(ct))
        {
            var childMap = DataSyncStoredJson.ReadChildMap(peerBase.ChildMapJson);
            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (peerId, localId) in childMap) next[peerId] = map.GetValueOrDefault(localId, localId);
            foreach (var (oldId, newId) in map) next.TryAdd(oldId, newId);
            if (next.Count == childMap.Count && next.All(p => childMap.TryGetValue(p.Key, out var v) && v == p.Value))
                continue;
            peerBase.ChildMapJson = DataSyncStoredJson.WriteChildMap(next);
            peerBase.UpdatedAtUtc = s.Now;
        }
    }
}

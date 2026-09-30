using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// The merger's input as the apply runner builds it inside the transaction, after Refresh (§8.10.2): local state per
/// kind (with the any-link flags §8.6 needs), this link's bases, the pending records to re-merge, this link's open
/// items by origin, and the usage of the entities the merge may change (§2.7) read from the adapters.
/// </summary>
internal static class DataSyncMergeInputs
{
    /// <summary>
    /// A link's context from its row and the local state (§2.7): the actor and its counters and the effective mode are
    /// always this device's current ones.
    /// </summary>
    public static DataSyncLinkContext LinkContext(DataSyncApplySession s, DataSyncLinkDbModel link,
        DataSyncCopyOnce? copyOnce = null)
    {
        var effective = Runtime.DataSyncInboxRules.IsEffectivelyTwoWay(link) ? DataSyncLinkMode.TwoWay : link.Mode;
        return new DataSyncLinkContext(link.Id, link.PeerNodeId, link.PeerName, link.Mode, effective,
            DataSyncStoredJson.ReadKinds(link.KindsJson), copyOnce,
            s.Services.GetRequiredService<IDataSyncHostKind>().IsHeadless, s.SelfActor, s.OwnActorCounters(),
            link.PeerActorId);
    }

    /// <summary>The kinds a merge of this link covers that this build can read locally.</summary>
    public static IReadOnlyList<string> LocalKinds(DataSyncApplySession s, DataSyncLinkContext link) =>
        link.Kinds.Where(s.Kinds.ContainsKey).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The whole input, with the usage of <see cref="DataSyncMerger.UsageTargets"/> already read (§2.7).</summary>
    /// <param name="contents">Contents read earlier in this transaction (<see cref="DataSyncLocalStateReader"/>).</param>
    public static async Task<DataSyncMergeInput> BuildAsync(DataSyncApplySession s, DataSyncLinkContext link,
        DataSyncStagedPull? pull, IReadOnlyList<(string Kind, SyncKey Key)> pendingToMerge, CancellationToken ct,
        DataSyncLocalContentCache? contents = null)
    {
        var kinds = LocalKinds(s, link);
        var local = await ReadLocalAsync(s, kinds, ct, contents);
        var bases = new Dictionary<(string Kind, SyncKey Key), DataSyncPeerBase>();
        foreach (var kind in link.Kinds.Distinct(StringComparer.Ordinal))
        {
            foreach (var b in await s.Store.GetBasesAsync(link.LinkId, kind, ct)) bases[(kind, b.Key)] = b;
        }

        var open = await s.Store.GetOpenItemsAsync(link.LinkId, ct);
        var usage = new Dictionary<(string, string), IReadOnlyDictionary<string, int>>();
        var values = new Dictionary<(string, string), int>();
        var input = new DataSyncMergeInput(link, pull, local, bases, pendingToMerge, s.Codecs, usage, values,
            open.Where(i => i.Origin == DataSyncInboxItemOrigin.Merger).ToList(), DataSyncAutoApplyPolicy.Default,
            s.Limits);
        foreach (var (kind, request) in DataSyncMerger.UsageTargets(input))
        {
            if (!s.Kinds.TryGetValue(kind, out var adapter)) continue;
            foreach (var (localKey, entity) in await adapter.GetUsageAsync(request, ct))
            {
                usage[(kind, localKey)] = entity.ResourceCountByChildId;
                values[(kind, localKey)] = entity.ValueCount;
            }
        }

        return input;
    }

    /// <summary>
    /// Local state of <paramref name="kinds"/> (§2.4), each entity with <c>OpenItemAnyLink</c> and
    /// <c>PendingRecordAnyLink</c> filled from every link (§8.6).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, DataSyncLocalKindState>> ReadLocalAsync(DataSyncApplySession s,
        IEnumerable<string> kinds, CancellationToken ct, DataSyncLocalContentCache? contents = null)
    {
        await s.Store.FlushAsync(ct);
        var openKeys = (await s.Db.DataSyncInboxItems.AsNoTracking().Where(i => i.ClosedAtUtc == null)
                .Select(i => new { i.Kind, i.SyncKey }).ToListAsync(ct))
            .Select(i => (i.Kind, i.SyncKey)).ToHashSet();
        var pending = await s.Db.DataSyncPeerBases.AsNoTracking().Where(b => b.PendingReason != null)
            .Select(b => new { b.Kind, b.SyncKey, b.PendingRecordJson }).ToListAsync(ct);
        var pendingKeys = new HashSet<(string, string)>();
        foreach (var p in pending)
        {
            pendingKeys.Add((p.Kind, p.SyncKey));
            foreach (var key in DataSyncStoredJson.ReadRecordKeys(p.PendingRecordJson)) pendingKeys.Add((p.Kind, key));
        }

        var result = new Dictionary<string, DataSyncLocalKindState>(StringComparer.Ordinal);
        foreach (var kind in kinds.Distinct(StringComparer.Ordinal))
        {
            var state = await s.Reader.ReadAsync(kind, ct, contents);
            result[kind] = state with
            {
                Entities = state.Entities.Select(e => e with
                {
                    OpenItemAnyLink = e.Keys.All.Any(k => openKeys.Contains((kind, k.Value))),
                    PendingRecordAnyLink = e.Keys.All.Any(k => pendingKeys.Contains((kind, k.Value))),
                }).ToList(),
            };
        }

        return result;
    }
}

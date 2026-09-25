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
/// items by origin, and the usage the merger asks for (§2.7, phase 1) read from the adapters.
/// </summary>
internal static class DataSyncMergeInputs
{
    /// <summary>
    /// A link's context from its row and the local state (§2.7). The actor and its counters, the once flags and the
    /// effective mode are always this device's current ones; <paramref name="fetched"/> (the fetch half's) supplies
    /// what only a head says: the peer's actor and comparison-form versions.
    /// </summary>
    public static DataSyncLinkContext LinkContext(DataSyncApplySession s, DataSyncLinkDbModel link,
        DataSyncLinkContext? fetched = null)
    {
        var kinds = DataSyncStoredJson.ReadStrings(link.KindsJson, "KindsJson");
        var completed = DataSyncStoredJson.ReadStrings(link.FirstContactKindsJson, "FirstContactKindsJson")
            .ToHashSet(StringComparer.Ordinal);
        var effective = DataSyncStore.IsEffectivelyTwoWay(link) ? DataSyncLinkMode.TwoWay : link.Mode;
        return new DataSyncLinkContext(link.Id, link.PeerNodeId, link.PeerName,
            fetched?.Mode ?? link.Mode,
            fetched?.EffectiveMode ?? effective,
            fetched?.Kinds ?? kinds,
            fetched?.FirstContactKinds ?? kinds.Where(k => !completed.Contains(k)).ToList(),
            s.Services.GetService<IDataSyncHostKind>()?.IsHeadless ?? false,
            s.SelfActor, s.OwnActorCounters(),
            fetched?.PeerActorId ?? link.PeerActorId,
            fetched?.PeerComparisonFormVersions ?? new Dictionary<string, int>(StringComparer.Ordinal),
            DataSyncStoredJson.ReadFlags(link.OnceFlagsJson, "OnceFlagsJson"));
    }

    /// <summary>The kinds a merge of this link covers that this build can read locally.</summary>
    public static IReadOnlyList<string> LocalKinds(DataSyncApplySession s, DataSyncLinkContext link) =>
        link.Kinds.Where(s.Kinds.ContainsKey).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The whole input, with the usage the merger asked for already read (phase 1, §2.7).</summary>
    public static async Task<DataSyncMergeInput> BuildAsync(DataSyncApplySession s, DataSyncLinkContext link,
        DataSyncStagedPull? pull, IReadOnlyList<(string Kind, SyncKey Key)> pendingToMerge, CancellationToken ct)
    {
        var kinds = LocalKinds(s, link);
        var local = await ReadLocalAsync(s, kinds, ct);
        var bases = new Dictionary<(string Kind, SyncKey Key), DataSyncPeerBase>();
        foreach (var kind in link.Kinds.Distinct(StringComparer.Ordinal))
        {
            foreach (var b in await s.Store.GetBasesAsync(link.LinkId, kind, ct)) bases[(kind, b.Key)] = b;
        }

        var open = await s.Store.GetOpenItemsAsync(link.LinkId, ct);
        var input = new DataSyncMergeInput(link, pull, local, bases, pendingToMerge, s.Codecs,
            new Dictionary<(string, string), IReadOnlyDictionary<string, int>>(),
            new Dictionary<(string, string), int>(),
            open.Where(i => i.Origin == DataSyncInboxItemOrigin.Merger).ToList(),
            DataSyncAutoApplyPolicy.Default, s.Limits,
            open.Where(i => i.Origin == DataSyncInboxItemOrigin.State).ToList());
        return await WithUsageAsync(s, input, ct);
    }

    /// <summary>Phase 1 of the merger (§2.7): reads what <see cref="DataSyncMerger.CollectUsageQueries"/> asks for.</summary>
    public static async Task<DataSyncMergeInput> WithUsageAsync(DataSyncApplySession s, DataSyncMergeInput input,
        CancellationToken ct)
    {
        var usage = new Dictionary<(string, string), IReadOnlyDictionary<string, int>>();
        var values = new Dictionary<(string, string), int>();
        foreach (var byKind in DataSyncMerger.CollectUsageQueries(input).GroupBy(q => q.Kind, StringComparer.Ordinal))
        {
            if (!s.Kinds.TryGetValue(byKind.Key, out var adapter)) continue;
            var request = byKind.ToDictionary(q => q.LocalKey, q => (IReadOnlyCollection<string>) q.ChildIds,
                StringComparer.Ordinal);
            var answer = await adapter.GetUsageAsync(request, ct);
            foreach (var query in byKind)
            {
                if (!answer.TryGetValue(query.LocalKey, out var entity)) continue;
                usage[(query.Kind, query.LocalKey)] = entity.ResourceCountByChildId;
                if (query.NeedValueCount) values[(query.Kind, query.LocalKey)] = entity.ValueCount;
            }
        }

        return input with { ChildUsage = usage, ValueCounts = values };
    }

    /// <summary>
    /// Local state of <paramref name="kinds"/> (§2.4), each entity with <c>OpenItemAnyLink</c> and
    /// <c>PendingRecordAnyLink</c> filled from every link (§8.6).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, DataSyncLocalKindState>> ReadLocalAsync(DataSyncApplySession s,
        IEnumerable<string> kinds, CancellationToken ct)
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
            var state = await s.Reader.ReadAsync(kind, ct);
            // The tombstone rows' Seq (the reader does not carry it): a pending record stored against one remembers
            // it, so it is re-merged only when the row changes (§8.4 condition 2).
            var tombstoneSeqs = (await s.Db.DataSyncEntities.AsNoTracking()
                    .Where(e => e.Kind == kind && e.DeletedAtUtc != null)
                    .Select(e => new { e.SyncKey, e.Seq }).ToListAsync(ct))
                .ToDictionary(e => e.SyncKey, e => e.Seq, StringComparer.Ordinal);
            result[kind] = state with
            {
                Entities = state.Entities.Select(e => e with
                {
                    OpenItemAnyLink = e.Keys.All.Any(k => openKeys.Contains((kind, k.Value))),
                    PendingRecordAnyLink = e.Keys.All.Any(k => pendingKeys.Contains((kind, k.Value))),
                }).ToList(),
                Tombstones = state.Tombstones.Select(t => t with
                {
                    Seq = t.Keys.Primary is { } primary ? tombstoneSeqs.GetValueOrDefault(primary.Value) : 0,
                }).ToList(),
            };
        }

        return result;
    }
}

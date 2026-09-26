using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Runtime;

/// <summary>Persistence [C]. Every method runs on the scope's BakabaseDbContext and joins its open transaction.</summary>
public interface IDataSyncStore
{
    Task<DataSyncLocalStateDbModel?> GetLocalStateAsync(CancellationToken ct);
    Task SaveLocalStateAsync(DataSyncLocalStateDbModel state, CancellationToken ct);

    /// <summary>++LastSeq in the local state row; the only way to obtain a Seq (§6.2).</summary>
    Task<long> NextSeqAsync(CancellationToken ct);

    Task<IReadOnlyList<DataSyncEntityDbModel>> GetEntitiesAsync(string kind, bool includeTombstones, CancellationToken ct);
    Task<IReadOnlyList<DataSyncEntityDbModel>> GetPublishedChangedSinceAsync(string kind, long sinceSeq, CancellationToken ct);
    Task<(int Live, int Tombstones)> CountPublishedAsync(string kind, CancellationToken ct);

    /// <summary>Bumps Seq.</summary>
    Task SetEntityStateAsync(string kind, string localKey, DataSyncEntitySyncState state, CancellationToken ct);

    Task SetOverlayAsync(string kind, string localKey, DataSyncOverlay overlay, CancellationToken ct);

    /// <summary>§3.6.</summary>
    Task SetChildrenLocalAsync(string kind, string localKey, bool childrenLocal, CancellationToken ct);

    Task<IReadOnlyList<DataSyncLinkDbModel>> GetLinksAsync(CancellationToken ct);
    Task<DataSyncLinkDbModel?> GetLinkAsync(int id, CancellationToken ct);
    Task<DataSyncLinkDbModel?> GetLinkByPeerAsync(string peerNodeId, CancellationToken ct);
    Task<DataSyncLinkDbModel> AddLinkAsync(DataSyncLinkDbModel link, CancellationToken ct);
    Task UpdateLinkAsync(DataSyncLinkDbModel link, CancellationToken ct);

    /// <summary>
    /// Reset: bases deleted; merger-derived open items close LinkRemoved; holds of this link become LocalOnlyChildren
    /// and their items close LinkRemoved; other state-derived items of the link close LinkRemoved (§8.1).
    /// </summary>
    Task DeleteLinkAsync(int id, CancellationToken ct);

    /// <summary>Off: bases and pending records kept; items close LinkStopped; holds become LocalOnlyChildren (§8.1).</summary>
    Task StopLinkAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<DataSyncPeerBase>> GetBasesAsync(int linkId, string kind, CancellationToken ct);

    /// <summary>
    /// What the link views count (§11.2), per link, over the bases of <see cref="DataSyncKindIds.All"/>: bases with a
    /// pending record, excluded, held (the base, or its pending record) and missing at the peer. One count for every
    /// link when <paramref name="linkId"/> is null; a link without bases has no entry. Reads no record, so it is cheap
    /// for a link with many definitions. The default reads every base; a store should count instead.
    /// </summary>
    async Task<IReadOnlyDictionary<int, DataSyncBaseCounts>> CountBasesAsync(int? linkId, CancellationToken ct)
    {
        IReadOnlyList<int> ids = linkId is { } id ? [id] : (await GetLinksAsync(ct)).Select(l => l.Id).ToList();
        var counts = new Dictionary<int, DataSyncBaseCounts>();
        foreach (var link in ids)
        {
            var bases = new List<DataSyncPeerBase>();
            foreach (var kind in DataSyncKindIds.All) bases.AddRange(await GetBasesAsync(link, kind, ct));
            if (bases.Count == 0) continue;
            counts[link] = new DataSyncBaseCounts(bases.Count(b => b.Pending is not null),
                bases.Count(b => b.State == DataSyncBaseState.Excluded),
                bases.Count(b => b.State == DataSyncBaseState.Held || b.Pending?.Reason == DataSyncPendingReason.Held),
                bases.Count(b => b.State == DataSyncBaseState.MissingAtPeer));
        }

        return counts;
    }

    /// <summary>
    /// Pending records to re-merge this pull: newer record arrived, local Seq moved since evaluation,
    /// Retry/OverBudget, flags set, full reconciliation (§8.4). Returns (kind, key) pairs.
    /// </summary>
    Task<IReadOnlyList<(string Kind, SyncKey Key)>> GetPendingToMergeAsync(int linkId, bool fullReconciliation,
        CancellationToken ct);

    Task UpsertBasesAsync(int linkId, IEnumerable<DataSyncBaseUpdate> updates, CancellationToken ct);

    /// <summary>§5.3 retire, rekey.</summary>
    Task RepointBasesAsync(string kind, string fromKey, string toKey, CancellationToken ct);

    Task<IReadOnlyList<DataSyncOpenInboxItem>> GetOpenItemsAsync(int? linkId, CancellationToken ct);

    /// <summary>§9.3.</summary>
    Task<DataSyncInboxReconcileResult> ReconcileInboxAsync(int linkId, string peerNodeId,
        IReadOnlyList<DataSyncInboxDraft> drafts, IReadOnlyCollection<(string Kind, SyncKey Key)> evaluated,
        IReadOnlyList<DataSyncClosureHint> hints, DateTime nowUtc, CancellationToken ct);

    /// <summary>After a commit: closes merger-derived items (any link) whose record vector ≤ the entity's new vector (§9.3).</summary>
    Task<int> CloseDominatedItemsAsync(IReadOnlyCollection<(string Kind, SyncKey Key)> touched, DateTime nowUtc,
        CancellationToken ct);

    /// <summary>Closes state-derived items whose state is gone (§9.3).</summary>
    Task<int> CloseStaleStateItemsAsync(IReadOnlyCollection<(string Kind, SyncKey Key)> touched, int? linkId,
        DateTime nowUtc, CancellationToken ct);

    Task CloseItemsAsync(IReadOnlyCollection<long> ids, DataSyncInboxClosure closure, DataSyncInboxAction? action,
        DataSyncEditorRef? by, int? applyLogId, CancellationToken ct);

    Task<DataSyncInboxItemDbModel?> GetItemAsync(long id, CancellationToken ct);

    /// <summary>
    /// A page of items, open ones first, then closed ones, newest (the highest id) first within each group; filtered by
    /// peer, kind and local key as given (§9, <see cref="DataSyncInboxQuery"/>). <c>OpenTotal</c> counts the open items
    /// that match the filters, whatever <c>OpenOnly</c> says.
    /// </summary>
    Task<DataSyncInboxPage> QueryInboxAsync(DataSyncInboxQuery query, CancellationToken ct);

    /// <summary>§9.4: the open items of the link that no notification announced yet (<c>NotifiedAtUtc</c> null).</summary>
    Task<IReadOnlyList<long>> GetUnannouncedItemIdsAsync(int linkId, CancellationToken ct);

    /// <summary>§9.4: records that <paramref name="notificationId"/> announced these items (NotificationId, NotifiedAtUtc).</summary>
    Task SetItemsNotifiedAsync(IReadOnlyCollection<long> ids, int notificationId, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// §9.4 read state: the notifications that announced an item closed at or after <paramref name="closedSinceUtc"/>
    /// and announce no open item any more.
    /// </summary>
    Task<IReadOnlyList<int>> GetSettledNotificationsAsync(DateTime closedSinceUtc, CancellationToken ct);

    /// <summary>§7.5.1.</summary>
    Task<DataSyncSourceAttention> GetAttentionAsync(CancellationToken ct);

    /// <summary>Throttled, §7.5.6.</summary>
    Task TouchReaderAsync(DataSyncReader reader, DataSyncFeedQuery query, long seqServed, DateTime nowUtc,
        CancellationToken ct);

    Task<IReadOnlyList<DataSyncReaderDbModel>> GetReadersAsync(CancellationToken ct);

    /// <summary>§9.4: "{0} started syncing definitions with this device" was sent for this reader (<c>NotifiedAtUtc</c>).</summary>
    Task SetReaderNotifiedAsync(string nodeId, DateTime nowUtc, CancellationToken ct);

    Task<int> AddHistoryAsync(DataSyncApplyLogDbModel log, CancellationToken ct);
    Task<IReadOnlyList<DataSyncApplyLogDbModel>> GetHistoryAsync(CancellationToken ct);
    Task<DataSyncApplyLogDbModel?> GetHistoryEntryAsync(int id, CancellationToken ct);

    /// <summary>§4.6.</summary>
    Task PruneAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>A link's bases as its view counts them (<see cref="IDataSyncStore.CountBasesAsync"/>).</summary>
public sealed record DataSyncBaseCounts(int Pending, int Excluded, int Held, int MissingAtPeer)
{
    public static readonly DataSyncBaseCounts None = new(0, 0, 0, 0);
}

public sealed record DataSyncInboxReconcileResult(int Created, int Updated, int Closed, IReadOnlyList<long> CreatedIds);

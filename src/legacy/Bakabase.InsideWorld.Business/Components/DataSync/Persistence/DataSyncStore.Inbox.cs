using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

// The inbox ("Needs you", §9). Two origins (§9.1): merger-derived items are re-derived on every evaluation of their
// entity and close when they are no longer produced; state-derived items are tied to local state and close only when
// that state is gone.
public sealed partial class DataSyncStore
{
    /// <summary>
    /// Open items, read-only: of one link, or of every link (and of none) when <paramref name="linkId"/> is null.
    /// </summary>
    public async Task<IReadOnlyList<DataSyncOpenInboxItem>> GetOpenItemsAsync(int? linkId, CancellationToken ct)
    {
        await FlushAsync(ct);
        var rows = await _db.DataSyncInboxItems.AsNoTracking()
            .Where(i => i.ClosedAtUtc == null && (linkId == null || i.LinkId == linkId))
            .OrderBy(i => i.Id)
            .ToListAsync(ct);
        return rows.Select(ToOpenItem).ToList();
    }

    private static DataSyncOpenInboxItem ToOpenItem(DataSyncInboxItemDbModel row) =>
        new(row.Id, row.LinkId, row.Kind, new SyncKey(row.SyncKey), row.Type, row.Origin, row.SubjectPath, row.Token,
            DataSyncStoredJson.ReadVv(row.RecordVvJson));

    /// <summary>
    /// One pull's items (§9.3): upserts every draft; then closes this link's merger-derived items whose entity was
    /// evaluated and whose subject was not produced again — <c>ResolvedElsewhere</c> (with who) when a closure hint
    /// says the entity took another device's revision, <c>Superseded</c> otherwise. State-derived items and items of
    /// subjects the pull did not evaluate are never closed here.
    /// </summary>
    public async Task<DataSyncInboxReconcileResult> ReconcileInboxAsync(int linkId, string peerNodeId,
        IReadOnlyList<DataSyncInboxDraft> drafts, IReadOnlyCollection<(string Kind, SyncKey Key)> evaluated,
        IReadOnlyList<DataSyncClosureHint> hints, DateTime nowUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(evaluated);
        ArgumentNullException.ThrowIfNull(hints);
        var upsert = await UpsertItemsAsync(linkId, peerNodeId, drafts, nowUtc, ct);

        var produced = drafts
            .Select(d => (d.Kind, Key: d.Key.Value, d.Type, d.SubjectPath))
            .ToHashSet();
        var evaluatedKeys = evaluated.Select(e => (e.Kind, e.Key.Value)).ToHashSet();
        var hintByKey = new Dictionary<(string, string), DataSyncClosureHint>();
        foreach (var hint in hints) hintByKey[(hint.Kind, hint.Key.Value)] = hint;

        var open = await _db.DataSyncInboxItems
            .Where(i => i.LinkId == linkId && i.ClosedAtUtc == null && i.Origin == DataSyncInboxItemOrigin.Merger)
            .ToListAsync(ct);
        var closed = 0;
        foreach (var item in open)
        {
            if (!evaluatedKeys.Contains((item.Kind, item.SyncKey))) continue;
            if (produced.Contains((item.Kind, item.SyncKey, item.Type, item.SubjectPath))) continue;
            if (hintByKey.TryGetValue((item.Kind, item.SyncKey), out var hint))
                Close(item, hint.Closure, null, hint.By, null, nowUtc);
            else
                Close(item, DataSyncInboxClosure.Superseded, null, null, null, nowUtc);
            closed++;
        }

        await _db.SaveChangesAsync(ct);
        return new DataSyncInboxReconcileResult(upsert.Created, upsert.Updated, closed, upsert.CreatedIds);
    }

    /// <summary>
    /// Inserts a draft whose subject has no open item, or refreshes the open one (payload, token, record hash and
    /// vectors, flags, local key; <c>CreatedAtUtc</c> kept). <paramref name="linkId"/> null is for items that belong
    /// to no link (<c>SuspectedLostUpdate</c>, from Refresh, §6.5).
    /// </summary>
    public async Task<DataSyncInboxReconcileResult> UpsertItemsAsync(int? linkId, string? peerNodeId,
        IReadOnlyList<DataSyncInboxDraft> drafts, DateTime nowUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        await FlushAsync(ct);
        var open = await _db.DataSyncInboxItems
            .Where(i => i.ClosedAtUtc == null && i.LinkId == linkId)
            .ToListAsync(ct);
        var bySubject = open.ToDictionary(i => (i.Kind, i.SyncKey, i.Type, i.SubjectPath));
        var created = new List<DataSyncInboxItemDbModel>();
        var updated = 0;
        foreach (var draft in drafts)
        {
            var subject = (draft.Kind, draft.Key.Value, draft.Type, draft.SubjectPath);
            if (!bySubject.TryGetValue(subject, out var item))
            {
                item = new DataSyncInboxItemDbModel
                {
                    LinkId = linkId,
                    Kind = draft.Kind,
                    SyncKey = draft.Key.Value,
                    Type = draft.Type,
                    SubjectPath = draft.SubjectPath,
                    CreatedAtUtc = nowUtc,
                };
                _db.DataSyncInboxItems.Add(item);
                bySubject[subject] = item;
                created.Add(item);
            }
            else if (!created.Contains(item))
            {
                updated++;
            }

            item.PeerNodeId = peerNodeId;
            item.LocalKey = draft.LocalKey;
            item.Origin = draft.Origin;
            item.PayloadJson = DataSyncStoredJson.Write(draft.Payload);
            item.RecordHash = draft.RecordHash;
            item.RecordVvJson = draft.RecordVv?.ToCanonicalString();
            item.LocalVvJson = draft.LocalVv?.ToCanonicalString();
            item.FlagsJson = DataSyncStoredJson.WriteFlags(draft.Flags);
            item.Token = draft.Token;
            item.UpdatedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(ct);
        return new DataSyncInboxReconcileResult(created.Count, updated, 0, created.Select(i => i.Id).ToList());
    }

    /// <summary>
    /// Dominance across links (§9.3, engineering must-fix 21): after a commit that changed entities, every open
    /// merger-derived item of ANY link whose record vector is ≤ its entity's new vector closes —
    /// <c>ResolvedElsewhere</c> when that revision was edited by another device, <c>Superseded</c> when it was made
    /// here. Items are matched by every key of the entity.
    /// </summary>
    public async Task<int> CloseDominatedItemsAsync(IReadOnlyCollection<(string Kind, SyncKey Key)> touched,
        DateTime nowUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(touched);
        await FlushAsync(ct);
        var selfNodeId = (await LoadStateAsync(ct))?.NodeId;
        var closed = 0;
        foreach (var (kind, key) in touched.Distinct())
        {
            var entity = await FindOwnerAsync(kind, key.Value, ct);
            if (entity is null) continue;
            var vv = DataSyncVersionVector.ParseStored(entity.VvJson);
            var keys = await KeysOfAsync(kind, entity.SyncKey, ct);
            var items = await _db.DataSyncInboxItems
                .Where(i => i.ClosedAtUtc == null && i.Origin == DataSyncInboxItemOrigin.Merger && i.Kind == kind &&
                            keys.Contains(i.SyncKey) && i.RecordVvJson != null)
                .ToListAsync(ct);
            foreach (var item in items)
            {
                var recordVv = DataSyncVersionVector.ParseStored(item.RecordVvJson!);
                if (recordVv.CompareTo(vv) is not (DataSyncVvRelation.Equal or DataSyncVvRelation.DominatedBy)) continue;
                var elsewhere = entity.LastEditorNodeId is { } editor && editor != selfNodeId;
                var by = elsewhere
                    ? new DataSyncEditorRef(entity.LastEditorNodeId!, entity.LastEditorName ?? "", entity.LastActorId ?? "")
                    : null;
                Close(item, elsewhere ? DataSyncInboxClosure.ResolvedElsewhere : DataSyncInboxClosure.Superseded,
                    null, by, null, nowUtc);
                closed++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return closed;
    }

    /// <summary>
    /// State-derived closure (§9.3): an open state-derived item of the touched entities (and every
    /// <c>LargeChange</c> item) closes <c>Superseded</c> exactly when its state is gone:
    /// <list type="bullet">
    /// <item><c>ChildDeletedInUse</c>: the entity no longer holds the child for the item's link;</item>
    /// <item><c>MassChildDeletion</c>: the base's pending reason is no longer <c>MassChildDeletion</c>;</item>
    /// <item><c>SuspectedLostUpdate</c>: <c>PublishHeld</c> is false (or the entity is gone);</item>
    /// <item><c>LargeChange</c>: the link has no <c>LargeChange</c> pending record left.</item>
    /// </list>
    /// With <paramref name="linkId"/>, items of that link and items of no link are checked; null checks every link.
    /// </summary>
    public async Task<int> CloseStaleStateItemsAsync(IReadOnlyCollection<(string Kind, SyncKey Key)> touched,
        int? linkId, DateTime nowUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(touched);
        await FlushAsync(ct);

        // Every key of every touched entity, so an item filed under an alias is found too.
        var entityByKey = new Dictionary<(string Kind, string Key), DataSyncEntityDbModel?>();
        foreach (var (kind, key) in touched.Distinct())
        {
            var entity = await FindOwnerAsync(kind, key.Value, ct);
            entityByKey[(kind, key.Value)] = entity;
            if (entity is null) continue;
            foreach (var k in await KeysOfAsync(kind, entity.SyncKey, ct)) entityByKey[(kind, k)] = entity;
        }

        var candidates = await _db.DataSyncInboxItems
            .Where(i => i.ClosedAtUtc == null && i.Origin == DataSyncInboxItemOrigin.State &&
                        (linkId == null || i.LinkId == linkId || i.LinkId == null))
            .ToListAsync(ct);
        candidates = candidates
            .Where(i => i.Type == DataSyncInboxItemType.LargeChange || entityByKey.ContainsKey((i.Kind, i.SyncKey)))
            .ToList();

        var closed = 0;
        foreach (var item in candidates)
        {
            var entity = entityByKey.GetValueOrDefault((item.Kind, item.SyncKey));
            var live = entity is {DeletedAtUtc: null} ? entity : null;
            var gone = item.Type switch
            {
                DataSyncInboxItemType.ChildDeletedInUse => !await HoldExistsAsync(item, live, ct),
                DataSyncInboxItemType.MassChildDeletion => !await _db.DataSyncPeerBases.AnyAsync(b =>
                    b.LinkId == item.LinkId && b.Kind == item.Kind &&
                    (b.SyncKey == item.SyncKey || (live != null && b.SyncKey == live.SyncKey)) &&
                    b.PendingReason == DataSyncPendingReason.MassChildDeletion, ct),
                DataSyncInboxItemType.SuspectedLostUpdate => live is not {PublishHeld: true},
                DataSyncInboxItemType.LargeChange => !await _db.DataSyncPeerBases.AnyAsync(b =>
                    b.LinkId == item.LinkId && b.PendingReason == DataSyncPendingReason.LargeChange, ct),
                _ => false,
            };
            if (!gone) continue;
            Close(item, DataSyncInboxClosure.Superseded, null, null, null, nowUtc);
            closed++;
        }

        await _db.SaveChangesAsync(ct);
        return closed;
    }

    /// <summary>
    /// Whether the hold a <c>ChildDeletedInUse</c> item is about still exists. The item's subject names the peer's
    /// representative of the class (§8.5.1, <c>choice:{peerId}</c>); the base's child map gives the local child.
    /// </summary>
    private async Task<bool> HoldExistsAsync(DataSyncInboxItemDbModel item, DataSyncEntityDbModel? live,
        CancellationToken ct)
    {
        if (live is null || item.LinkId is not { } linkId) return false;
        var holds = DataSyncStoredJson.ReadOverlay(live.OverlayJson).HeldChildren
            .Where(h => h.LinkId == linkId).Select(h => h.ChildId).ToHashSet(StringComparer.Ordinal);
        if (holds.Count == 0) return false;
        var separator = item.SubjectPath.IndexOf(':');
        if (separator < 0) return false;
        var peerChildId = item.SubjectPath[(separator + 1)..];
        if (holds.Contains(peerChildId)) return true;
        var childMapJson = await _db.DataSyncPeerBases
            .Where(b => b.LinkId == linkId && b.Kind == item.Kind && (b.SyncKey == live.SyncKey || b.SyncKey == item.SyncKey))
            .Select(b => b.ChildMapJson)
            .FirstOrDefaultAsync(ct);
        return DataSyncStoredJson.ReadChildMap(childMapJson).TryGetValue(peerChildId, out var localChildId) &&
               holds.Contains(localChildId);
    }

    /// <summary>Closes the open ones among <paramref name="ids"/>; closed ones are left as they are.</summary>
    public async Task CloseItemsAsync(IReadOnlyCollection<long> ids, DataSyncInboxClosure closure,
        DataSyncInboxAction? action, DataSyncEditorRef? by, int? applyLogId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        await FlushAsync(ct);
        var idList = ids.Distinct().ToList();
        var now = UtcNow;
        foreach (var item in await _db.DataSyncInboxItems.Where(i => idList.Contains(i.Id) && i.ClosedAtUtc == null)
                     .ToListAsync(ct))
        {
            Close(item, closure, action, by, applyLogId, now);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>One item, tracked.</summary>
    public async Task<DataSyncInboxItemDbModel?> GetItemAsync(long id, CancellationToken ct) =>
        await _db.DataSyncInboxItems.FindAsync([id], ct);

    /// <summary>
    /// A page of items, open ones first and newest first within each group. Allowed actions follow §9.1 for the
    /// item's link as it is now; times are UTC.
    /// </summary>
    public async Task<DataSyncInboxPage> QueryInboxAsync(DataSyncInboxQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await FlushAsync(ct);
        var filtered = _db.DataSyncInboxItems.AsNoTracking();
        if (query.PeerNodeId is { } peer) filtered = filtered.Where(i => i.PeerNodeId == peer);
        if (query.Kind is { } kind) filtered = filtered.Where(i => i.Kind == kind);

        var openTotal = await filtered.CountAsync(i => i.ClosedAtUtc == null, ct);
        if (query.OpenOnly) filtered = filtered.Where(i => i.ClosedAtUtc == null);
        var total = await filtered.CountAsync(ct);
        var rows = await filtered
            .OrderBy(i => i.ClosedAtUtc == null ? 0 : 1)
            .ThenByDescending(i => i.Id)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 0, MaxInboxPageSize))
            .ToListAsync(ct);

        var linkIds = rows.Where(r => r.LinkId != null).Select(r => r.LinkId!.Value).Distinct().ToList();
        var links = await _db.DataSyncLinks.AsNoTracking().Where(l => linkIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        var items = rows.Select(row =>
        {
            var link = row.LinkId is { } id ? links.GetValueOrDefault(id) : null;
            var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(row.PayloadJson, "PayloadJson");
            var allowed = row.ClosedAtUtc is null
                ? Runtime.DataSyncInboxRules.Allowed(row.Type, row.SubjectPath, payload,
                    Runtime.DataSyncInboxRules.IsEffectivelyTwoWay(link))
                : [];
            return new DataSyncInboxItemView(row.Id, row.LinkId, row.PeerNodeId, link?.PeerName ?? payload.PeerName,
                row.Kind, row.LocalKey, row.Type, row.Origin, row.SubjectPath, payload, allowed, null, row.Token,
                Utc(row.CreatedAtUtc), Utc(row.UpdatedAtUtc), Utc(row.ClosedAtUtc), row.Closure, row.Action,
                row.ClosedByName);
        }).ToList();
        return new DataSyncInboxPage(items, total, openTotal);
    }

    /// <summary>The largest page <see cref="QueryInboxAsync"/> returns.</summary>
    public const int MaxInboxPageSize = 500;

    #region Notification bookkeeping (§9.4)

    /// <summary>The link's open items that no notification announced yet, oldest first.</summary>
    public async Task<IReadOnlyList<long>> GetUnannouncedItemIdsAsync(int linkId, CancellationToken ct)
    {
        await FlushAsync(ct);
        return await _db.DataSyncInboxItems.AsNoTracking()
            .Where(i => i.LinkId == linkId && i.ClosedAtUtc == null && i.NotifiedAtUtc == null)
            .OrderBy(i => i.Id)
            .Select(i => i.Id)
            .ToListAsync(ct);
    }

    /// <summary>Records that <paramref name="notificationId"/> announced these items, open or closed.</summary>
    public async Task SetItemsNotifiedAsync(IReadOnlyCollection<long> ids, int notificationId, DateTime nowUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return;
        await FlushAsync(ct);
        var idList = ids.Distinct().ToList();
        foreach (var item in await _db.DataSyncInboxItems.Where(i => idList.Contains(i.Id)).ToListAsync(ct))
        {
            item.NotificationId = notificationId;
            item.NotifiedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The notifications that announced an item closed at or after <paramref name="closedSinceUtc"/> and announce no
    /// open item any more: every item they announced is decided, so they are marked read.
    /// </summary>
    public async Task<IReadOnlyList<int>> GetSettledNotificationsAsync(DateTime closedSinceUtc, CancellationToken ct)
    {
        await FlushAsync(ct);
        var closed = await _db.DataSyncInboxItems.AsNoTracking()
            .Where(i => i.NotificationId != null && i.ClosedAtUtc != null && i.ClosedAtUtc >= closedSinceUtc)
            .Select(i => i.NotificationId!.Value)
            .Distinct()
            .ToListAsync(ct);
        if (closed.Count == 0) return [];
        var stillOpen = await _db.DataSyncInboxItems.AsNoTracking()
            .Where(i => i.ClosedAtUtc == null && i.NotificationId != null && closed.Contains(i.NotificationId!.Value))
            .Select(i => i.NotificationId!.Value)
            .Distinct()
            .ToListAsync(ct);
        return closed.Except(stillOpen).ToList();
    }

    #endregion

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    private static void Close(DataSyncInboxItemDbModel item, DataSyncInboxClosure closure, DataSyncInboxAction? action,
        DataSyncEditorRef? by, int? applyLogId, DateTime nowUtc)
    {
        item.ClosedAtUtc = nowUtc;
        item.UpdatedAtUtc = nowUtc;
        item.Closure = closure;
        item.Action = action;
        item.ClosedByNodeId = by?.NodeId;
        item.ClosedByName = by?.Name;
        item.ApplyLogId = applyLogId;
    }

    private async Task CloseLinkItemsAsync(int linkId, DataSyncInboxClosure closure, CancellationToken ct)
    {
        var now = UtcNow;
        foreach (var item in await _db.DataSyncInboxItems.Where(i => i.LinkId == linkId && i.ClosedAtUtc == null)
                     .ToListAsync(ct))
        {
            Close(item, closure, null, null, null, now);
        }
    }

    /// <summary>Every open item of the entity with these keys, of any link or none.</summary>
    internal async Task CloseOpenItemsOfAsync(string kind, IReadOnlyCollection<string> keys, DataSyncInboxClosure closure,
        CancellationToken ct)
    {
        var now = UtcNow;
        foreach (var item in await _db.DataSyncInboxItems
                     .Where(i => i.ClosedAtUtc == null && i.Kind == kind && keys.Contains(i.SyncKey))
                     .ToListAsync(ct))
        {
            Close(item, closure, null, null, null, now);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// <see cref="IDataSyncStore"/> over the scope's <see cref="BakabaseDbContext"/> (§4.4). Every member joins the
/// context's open transaction; one that writes saves its own changes (and any the caller left pending) before it
/// returns, except <see cref="NextSeqAsync"/>, whose increment is saved with the row that takes the number.
/// </summary>
/// <remarks>
/// <para>
/// Rows handed out for writing are tracked: a caller changes them and they are saved by the next member that writes
/// (or its own <c>SaveChangesAsync</c>). Queries that filter on columns a caller may have changed save pending
/// changes first, so the database and the tracked rows never disagree about what a filter selects.
/// </para>
/// <para>
/// The key columns of <c>DataSyncEntities</c> and every row of <c>DataSyncKeyAliases</c> belong to
/// <see cref="DataSyncIdentityStore"/> (§4.4); this class never writes them.
/// </para>
/// <para>
/// After a rolled-back transaction the context still tracks what was written in it: the caller clears the change
/// tracker (<c>ChangeTracker.Clear()</c>) before it uses the scope again.
/// </para>
/// </remarks>
public sealed partial class DataSyncStore : IDataSyncStore
{
    private readonly BakabaseDbContext _db;
    private readonly DataSyncReaderLog _readerLog;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private IReadOnlyDictionary<string, IDataSyncKind>? _kinds;

    public DataSyncStore(BakabaseDbContext db, DataSyncReaderLog readerLog, IServiceProvider services)
    {
        _db = db;
        _readerLog = readerLog;
        _services = services;
        _time = services.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    internal BakabaseDbContext Db => _db;

    internal DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// The registered kind adapters by kind id, resolved on first use: adapters depend on the services that own the
    /// definitions, which most store calls never need.
    /// </summary>
    internal IReadOnlyDictionary<string, IDataSyncKind> Kinds =>
        _kinds ??= _services.GetServices<IDataSyncKind>()
            .ToDictionary(k => k.Codec.Descriptor.Kind, StringComparer.Ordinal);

    internal async Task FlushAsync(CancellationToken ct)
    {
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
    }

    #region Local state (§4.1, §6.2)

    public Task<DataSyncLocalStateDbModel?> GetLocalStateAsync(CancellationToken ct) => LoadStateAsync(ct);

    /// <summary>
    /// Inserts the row (Id = 1) or saves it; a row not tracked by this context replaces the stored values.
    /// <c>UpdatedAtUtc</c> is set to now.
    /// </summary>
    public async Task SaveLocalStateAsync(DataSyncLocalStateDbModel state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Id != DataSyncLocalStateRows.SingletonId)
        {
            if (state.Id != 0)
                throw new ArgumentException($"The local state row has Id {DataSyncLocalStateRows.SingletonId}.",
                    nameof(state));
            state.Id = DataSyncLocalStateRows.SingletonId;
        }

        state.UpdatedAtUtc = UtcNow;
        if (_db.Entry(state).State == EntityState.Detached)
        {
            var existing = await _db.DataSyncLocalStates.FindAsync([DataSyncLocalStateRows.SingletonId], ct);
            if (existing is null) _db.DataSyncLocalStates.Add(state);
            else _db.Entry(existing).CurrentValues.SetValues(state);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// ++LastSeq on the local state row: the only source of Seq (§6.2). The increment is saved with the caller's
    /// next save, in the same transaction as the row that takes the number. Throws when Refresh has not created the
    /// row yet (§4.5).
    /// </summary>
    public async Task<long> NextSeqAsync(CancellationToken ct)
    {
        var state = await LoadStateAsync(ct) ??
                    throw new InvalidOperationException(
                        "Data sync has no local state yet: the first Refresh creates it (§4.5).");
        state.LastSeq = checked(state.LastSeq + 1);
        return state.LastSeq;
    }

    /// <summary>
    /// The tracked local state row. A copy this context holds unchanged is re-read first, so a scope that loaded it
    /// earlier never issues a sequence number another scope already issued; a copy the caller is changing is kept.
    /// </summary>
    internal async Task<DataSyncLocalStateDbModel?> LoadStateAsync(CancellationToken ct)
    {
        var state = await _db.DataSyncLocalStates.FindAsync([DataSyncLocalStateRows.SingletonId], ct);
        if (state is null) return null;
        var entry = _db.Entry(state);
        if (entry.State == EntityState.Unchanged)
        {
            await entry.ReloadAsync(ct);
            if (entry.State == EntityState.Detached) return null;
        }

        return state;
    }

    #endregion

    #region Entities (§4.1, §3.6)

    /// <summary>Tracked rows of <paramref name="kind"/> by Id: live rows in any state, plus tombstones when asked.</summary>
    public async Task<IReadOnlyList<DataSyncEntityDbModel>> GetEntitiesAsync(string kind, bool includeTombstones,
        CancellationToken ct)
    {
        await FlushAsync(ct);
        return await _db.DataSyncEntities
            .Where(e => e.Kind == kind && (includeTombstones || e.DeletedAtUtc == null))
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// What the feed serves after <paramref name="sinceSeq"/>, in Seq order (§7.5.2): live Synced rows (held ones
    /// included; the feed serves them as HeldAtSource) and served tombstones. Read-only copies.
    /// </summary>
    public async Task<IReadOnlyList<DataSyncEntityDbModel>> GetPublishedChangedSinceAsync(string kind, long sinceSeq,
        CancellationToken ct)
    {
        await FlushAsync(ct);
        return await Published(kind)
            .Where(e => e.Seq > sinceSeq)
            .OrderBy(e => e.Seq)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>The kind's totals: live published entities and served tombstones (§7.5.2 step 7).</summary>
    public async Task<(int Live, int Tombstones)> CountPublishedAsync(string kind, CancellationToken ct)
    {
        await FlushAsync(ct);
        var live = await Published(kind).CountAsync(e => e.DeletedAtUtc == null, ct);
        var tombstones = await Published(kind).CountAsync(e => e.DeletedAtUtc != null, ct);
        return (live, tombstones);
    }

    private IQueryable<DataSyncEntityDbModel> Published(string kind) =>
        _db.DataSyncEntities.Where(e =>
            e.Kind == kind &&
            ((e.DeletedAtUtc == null && e.State == DataSyncEntitySyncState.Synced) ||
             (e.DeletedAtUtc != null && e.TombstoneServed)));

    /// <summary>
    /// Changes a live entity's state and bumps its Seq; the same state does nothing (§3.6).
    /// <list type="bullet">
    /// <item>Leaving Synced (keep on this device only, stop syncing): the entity's open items close Superseded
    /// (§9.3), its pending records are cleared on every link and <c>PublishHeld</c> is cleared (§9.2 Detach); its
    /// holds become local-only children, since no item is left to decide them.</item>
    /// <item>Rejoining (back to Synced): the entity's bases are deleted on every link, exclusions included, so the next
    /// pulls merge it through its keys with no base (§3.6).</item>
    /// </list>
    /// The row is marked for Refresh, which recomputes what it publishes.
    /// </summary>
    public async Task SetEntityStateAsync(string kind, string localKey, DataSyncEntitySyncState state,
        CancellationToken ct)
    {
        var row = await RequireLiveAsync(kind, localKey, ct);
        if (row.State == state) return;

        var keys = await KeysOfAsync(kind, row.SyncKey, ct);
        var leavingSynced = row.State == DataSyncEntitySyncState.Synced;
        row.State = state;
        row.Seq = await NextSeqAsync(ct);
        row.UpdatedAtUtc = UtcNow;
        row.RawHash = null;
        if (leavingSynced)
        {
            row.PublishHeld = false;
            // Its items close, so nobody could decide its holds any more: they become local-only children, as when
            // a link stops (§8.1, engineering must-fix 28).
            var overlay = DataSyncStoredJson.ReadOverlay(row.OverlayJson);
            if (overlay.HeldChildren.Count > 0)
            {
                row.OverlayJson = DataSyncStoredJson.WriteOverlay(new DataSyncOverlay(
                    overlay.LocalOnlyChildren.Concat(overlay.HeldChildren.Select(h => h.ChildId)).Distinct().ToList(),
                    []));
            }

            await CloseOpenItemsOfAsync(kind, keys, DataSyncInboxClosure.Superseded, ct);
            foreach (var peerBase in await _db.DataSyncPeerBases
                         .Where(b => b.Kind == kind && keys.Contains(b.SyncKey) && b.PendingReason != null)
                         .ToListAsync(ct))
            {
                ClearPending(peerBase);
                peerBase.UpdatedAtUtc = UtcNow;
            }
        }
        else if (state == DataSyncEntitySyncState.Synced)
        {
            _db.DataSyncPeerBases.RemoveRange(await BasesOfEntityAsync(kind, keys, ct));
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Stores the overlay (§3.6) and marks the row for Refresh, which publishes the change if it alters content.</summary>
    public async Task SetOverlayAsync(string kind, string localKey, DataSyncOverlay overlay, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var row = await RequireLiveAsync(kind, localKey, ct);
        row.OverlayJson = DataSyncStoredJson.WriteOverlay(overlay);
        row.RawHash = null;
        row.UpdatedAtUtc = UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Stores the shared <c>childrenLocal</c> field (§3.6) and marks the row for Refresh: the change is content, so
    /// Refresh makes it a revision.
    /// </summary>
    public async Task SetChildrenLocalAsync(string kind, string localKey, bool childrenLocal, CancellationToken ct)
    {
        var row = await RequireLiveAsync(kind, localKey, ct);
        if (row.ChildrenLocal == childrenLocal) return;
        row.ChildrenLocal = childrenLocal;
        row.RawHash = null;
        row.UpdatedAtUtc = UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    internal async Task<DataSyncEntityDbModel> RequireLiveAsync(string kind, string localKey, CancellationToken ct)
    {
        await FlushAsync(ct);
        return await _db.DataSyncEntities.SingleOrDefaultAsync(
                   e => e.Kind == kind && e.LocalKey == localKey && e.DeletedAtUtc == null, ct) ??
               throw new KeyNotFoundException($"No live data sync entity {kind}/{localKey}.");
    }

    /// <summary>The entity row (live or tombstoned) that owns <paramref name="key"/> as its primary or an alias.</summary>
    internal async Task<DataSyncEntityDbModel?> FindOwnerAsync(string kind, string key, CancellationToken ct)
    {
        var row = await _db.DataSyncEntities.SingleOrDefaultAsync(e => e.Kind == kind && e.SyncKey == key, ct);
        if (row is not null) return row;
        var primary = await _db.DataSyncKeyAliases.Where(a => a.Kind == kind && a.AliasKey == key)
            .Select(a => a.SyncKey).SingleOrDefaultAsync(ct);
        return primary is null
            ? null
            : await _db.DataSyncEntities.SingleOrDefaultAsync(e => e.Kind == kind && e.SyncKey == primary, ct);
    }

    /// <summary>Every key of the entity whose primary is <paramref name="primary"/>: the primary and its aliases.</summary>
    internal async Task<List<string>> KeysOfAsync(string kind, string primary, CancellationToken ct)
    {
        var aliases = await _db.DataSyncKeyAliases.Where(a => a.Kind == kind && a.SyncKey == primary)
            .Select(a => a.AliasKey).ToListAsync(ct);
        return [primary, ..aliases];
    }

    #endregion

    #region Links (§8.1)

    public async Task<IReadOnlyList<DataSyncLinkDbModel>> GetLinksAsync(CancellationToken ct)
    {
        await FlushAsync(ct);
        return await _db.DataSyncLinks.OrderBy(l => l.Id).ToListAsync(ct);
    }

    public async Task<DataSyncLinkDbModel?> GetLinkAsync(int id, CancellationToken ct) =>
        await _db.DataSyncLinks.FindAsync([id], ct);

    public async Task<DataSyncLinkDbModel?> GetLinkByPeerAsync(string peerNodeId, CancellationToken ct)
    {
        await FlushAsync(ct);
        return await _db.DataSyncLinks.SingleOrDefaultAsync(l => l.PeerNodeId == peerNodeId, ct);
    }

    /// <summary>Inserts a link (one per peer, §4.2). Unset creation and update times become now.</summary>
    public async Task<DataSyncLinkDbModel> AddLinkAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(link);
        var now = UtcNow;
        if (link.CreatedAtUtc == default) link.CreatedAtUtc = now;
        link.UpdatedAtUtc = now;
        _db.DataSyncLinks.Add(link);
        await _db.SaveChangesAsync(ct);
        return link;
    }

    public async Task UpdateLinkAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(link);
        link.UpdatedAtUtc = UtcNow;
        if (_db.Entry(link).State == EntityState.Detached) _db.DataSyncLinks.Update(link);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reset (§8.1): the link row, its bases and pending records are deleted; every open item of the link closes
    /// <c>LinkRemoved</c>; its holds become local-only children. Definitions are untouched. Items that belong to no
    /// link (<c>SuspectedLostUpdate</c>) are not touched. An unknown id does nothing.
    /// </summary>
    public async Task DeleteLinkAsync(int id, CancellationToken ct)
    {
        await FlushAsync(ct);
        var link = await _db.DataSyncLinks.FindAsync([id], ct);
        if (link is null) return;
        await ReleaseHoldsAsync(id, ct);
        await CloseLinkItemsAsync(id, DataSyncInboxClosure.LinkRemoved, ct);
        _db.DataSyncPeerBases.RemoveRange(await _db.DataSyncPeerBases.Where(b => b.LinkId == id).ToListAsync(ct));
        _db.DataSyncLinks.Remove(link);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Off (§8.1): <c>Mode = Off</c> and <c>State = Stopped</c>, with the mode it had remembered in <c>LastMode</c>;
    /// bases and pending records are kept, so turning it on again is incremental; every open item of the link
    /// closes <c>LinkStopped</c>; its holds become local-only children.
    /// </summary>
    public async Task StopLinkAsync(int id, CancellationToken ct)
    {
        await FlushAsync(ct);
        var link = await _db.DataSyncLinks.FindAsync([id], ct) ??
                   throw new KeyNotFoundException($"No data sync link {id}.");
        if (link.Mode != DataSyncLinkMode.Off) link.LastMode = link.Mode;
        link.Mode = DataSyncLinkMode.Off;
        link.State = DataSyncLinkState.Stopped;
        link.UpdatedAtUtc = UtcNow;
        await ReleaseHoldsAsync(id, ct);
        await CloseLinkItemsAsync(id, DataSyncInboxClosure.LinkStopped, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Nobody can decide a hold of a link that is gone or off (§8.1, engineering must-fix 28): each child it holds
    /// becomes local-only here, unless another link still holds it.
    /// </summary>
    private async Task ReleaseHoldsAsync(int linkId, CancellationToken ct)
    {
        var rows = await _db.DataSyncEntities.Where(e => e.DeletedAtUtc == null && e.OverlayJson != null)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            var overlay = DataSyncStoredJson.ReadOverlay(row.OverlayJson);
            if (overlay.HeldChildren.All(h => h.LinkId != linkId)) continue;
            var held = overlay.HeldChildren.Where(h => h.LinkId != linkId).ToList();
            var localOnly = overlay.LocalOnlyChildren.ToList();
            foreach (var released in overlay.HeldChildren.Where(h => h.LinkId == linkId).Select(h => h.ChildId))
            {
                if (held.Any(h => h.ChildId == released) || localOnly.Contains(released)) continue;
                localOnly.Add(released);
            }

            row.OverlayJson = DataSyncStoredJson.WriteOverlay(new DataSyncOverlay(localOnly, held));
            row.RawHash = null;
            row.UpdatedAtUtc = UtcNow;
        }
    }

    #endregion

    #region History (§4.1, §8.11)

    public async Task<int> AddHistoryAsync(DataSyncApplyLogDbModel log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (log.AppliedAtUtc == default) log.AppliedAtUtc = UtcNow;
        _db.DataSyncApplyLogs.Add(log);
        await _db.SaveChangesAsync(ct);
        return log.Id;
    }

    /// <summary>Every retained entry, newest first; read-only copies.</summary>
    public async Task<IReadOnlyList<DataSyncApplyLogDbModel>> GetHistoryAsync(CancellationToken ct)
    {
        await FlushAsync(ct);
        return await _db.DataSyncApplyLogs.AsNoTracking().OrderByDescending(l => l.Id).ToListAsync(ct);
    }

    /// <summary>One entry, tracked, so undo can record <c>UndoneAtUtc</c> on it.</summary>
    public async Task<DataSyncApplyLogDbModel?> GetHistoryEntryAsync(int id, CancellationToken ct) =>
        await _db.DataSyncApplyLogs.FindAsync([id], ct);

    #endregion

    #region Attention (§7.5.1)

    /// <summary>Counts only, never names (§7.5.1).</summary>
    public async Task<DataSyncSourceAttention> GetAttentionAsync(CancellationToken ct)
    {
        await FlushAsync(ct);
        var headless = _services.GetRequiredService<IDataSyncHostKind>().IsHeadless;
        var openDecisions = await _db.DataSyncInboxItems.CountAsync(i => i.ClosedAtUtc == null, ct);
        var pausedLinks = await _db.DataSyncLinks.CountAsync(l => l.State == DataSyncLinkState.Paused, ct);
        var awaitingReview = await _db.DataSyncLinks.CountAsync(l => l.State == DataSyncLinkState.AwaitingReview, ct);
        var state = await LoadStateAsync(ct);
        return new DataSyncSourceAttention(headless, openDecisions, pausedLinks, state?.RestoreReason is not null,
            awaitingReview);
    }

    #endregion
}

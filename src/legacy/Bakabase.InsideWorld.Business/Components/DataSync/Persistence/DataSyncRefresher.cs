using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>What a Refresh does beyond <see cref="IDataSyncRefresher"/>'s arguments.</summary>
/// <param name="ReleaseHeld">
/// Entities a person decided to keep as they are here after the lost-update guard held them (§6.5 "Keep this device's
/// version", §9.2 <c>Publish</c>): <c>PublishHeld</c> is cleared and their current content becomes an ordinary local
/// revision, without asking the guard again. The caller closes the item and re-merges the held pending records.
/// </param>
public sealed record DataSyncRefreshOptions(IReadOnlyCollection<(string Kind, string LocalKey)>? ReleaseHeld = null)
{
    public static DataSyncRefreshOptions None { get; } = new();
}

/// <summary>
/// Refresh (§6.1; v3.1 §4.4 extended): compares the kinds' local definitions with their side rows and turns every
/// change of what an entity publishes into the next sequence number, and every change of its comparison form into a
/// revision. It hooks no write, so it covers every writer (services, <c>ExecuteUpdate</c>, the enhancer, path marks,
/// bulk edits). It never reads the network, never applies peer data and never rotates the actor.
/// </summary>
/// <remarks>
/// <para>
/// It joins the scope's open transaction, or runs in its own short one. In its own, Refresh writes <c>actor.json</c>
/// after the commit (§4.7), and rolls back instead of committing when evidence of a restore arrived meanwhile. When
/// it joins, the caller does both: it commits only while <see cref="IDataSyncActorGuard.IsVerified"/> still holds,
/// writes <c>actor.json</c> afterwards, and treats a <c>Skipped</c> result as "apply nothing now". After a throw the
/// caller rolls back and clears the scope's change tracker.
/// </para>
/// <para>
/// Only <see cref="IDataSyncActorGuard.CheckAsync"/> rotates, before any transaction. Refresh asserts the actor is
/// current and otherwise throws <see cref="DataSyncActorChangedException"/>; while the actor is unverified or evidence
/// waits to be handled it writes nothing at all (<c>Skipped</c>).
/// </para>
/// </remarks>
public sealed class DataSyncRefresher : IDataSyncRefresher
{
    private readonly DataSyncStore _store;
    private readonly DataSyncIdentityStore _identity;
    private readonly IDataSyncActorGuard _guard;
    private readonly DataSyncActorWatermarkFile _watermark;
    private readonly IServiceProvider _services;

    public DataSyncRefresher(DataSyncStore store, DataSyncIdentityStore identity, IDataSyncActorGuard guard,
        DataSyncActorWatermarkFile watermark, IServiceProvider services)
    {
        _store = store;
        _identity = identity;
        _guard = guard;
        _watermark = watermark;
        _services = services;
    }

    /// <summary>The limits Refresh publishes against (the order key length).</summary>
    public DataSyncLimits Limits { get; init; } = DataSyncLimits.Default;

    public Task<DataSyncRefreshResult> RefreshAsync(DataSyncGateLease lease, IReadOnlyCollection<string> kinds,
        bool collectPublished, CancellationToken ct) =>
        RefreshAsync(lease, kinds, collectPublished, DataSyncRefreshOptions.None, ct);

    public async Task<DataSyncRefreshResult> RefreshAsync(DataSyncGateLease lease, IReadOnlyCollection<string> kinds,
        bool collectPublished, DataSyncRefreshOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(options);
        if (!lease.IsHeld) throw new InvalidOperationException("Refresh runs under the data sync gate (§6.1).");
        if (!_guard.IsVerified) return await SkippedAsync(ct);

        var db = _store.Db;
        if (db.Database.CurrentTransaction is not null)
            return (await RefreshCoreAsync(kinds, collectPublished, options, ct)).Result;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var (result, state) = await RefreshCoreAsync(kinds, collectPublished, options, ct);
            if (!_guard.IsVerified)
            {
                // Evidence of a restore arrived while this ran: nothing it issued may stand under that actor.
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return await SkippedAsync(ct);
            }

            await transaction.CommitAsync(ct);
            // Committed counters must reach actor.json whatever a stop requested meanwhile (§5.6).
            await _watermark.WriteAsync(state, CancellationToken.None);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<DataSyncRefreshResult> SkippedAsync(CancellationToken ct)
    {
        var state = await _store.Db.DataSyncLocalStates.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == DataSyncLocalStateRows.SingletonId, ct);
        return new DataSyncRefreshResult(0, 0, state is null ? default : new DataSyncActorId(state.ActorId), true, null);
    }

    private async Task<(DataSyncRefreshResult Result, DataSyncLocalStateDbModel State)> RefreshCoreAsync(
        IReadOnlyCollection<string> kinds, bool collectPublished, DataSyncRefreshOptions options, CancellationToken ct)
    {
        // Before any content is read: a change a later Refresh meets was made after this moment (§6.5).
        var startedAt = _store.UtcNow;
        var device = await _services.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(ct);
        var file = _watermark.Read().Watermark;
        var state = await _store.GetLocalStateAsync(ct);
        if (state is null)
        {
            // A row missing behind the watermark's back is the guard's to judge (§5.6).
            if (file is not null) throw new DataSyncActorChangedException();
            state = DataSyncLocalStateRows.New(device, _store.UtcNow);
            await _store.SaveLocalStateAsync(state, ct);
        }

        if (!string.Equals(state.NodeId, device.NodeId, StringComparison.Ordinal) ||
            !string.Equals(state.LibraryEpoch, device.LibraryEpoch, StringComparison.Ordinal) ||
            !string.Equals(state.ActorId, DataSyncActorId.Derive(device.NodeId, device.LibraryEpoch, state.ActorSalt).Value,
                StringComparison.Ordinal) ||
            file?.IsAheadOf(state) == true)
        {
            throw new DataSyncActorChangedException();
        }

        // Refresh compares what is stored: rows this scope loaded earlier (before it held the gate, say) are read
        // again instead of trusted. Rows with pending changes are the caller's and are saved with Refresh's.
        foreach (var entry in _store.Db.ChangeTracker.Entries<DataSyncEntityDbModel>()
                     .Where(e => e.State == EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }

        var ordered = Ordered(kinds).ToList();
        var run = new Run(this, state, device, collectPublished, options, ordered);
        foreach (var kind in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (!_store.Kinds.TryGetValue(kind, out var adapter))
                throw new InvalidOperationException($"No data sync kind adapter is registered for '{kind}'.");
            await run.RefreshKindAsync(kind, adapter, ct);
        }

        await run.FlushItemsAsync(ct);
        // Committed with this Refresh, or rolled back with it: the next one measures the lost-update window from here.
        if (await DataSyncLostUpdateGuard.AfterRefreshAsync(_store.Db, state.RefreshedAtJson, ordered, startedAt, ct) is
            { } refreshedAt)
        {
            state.RefreshedAtJson = refreshedAt;
        }

        if (_store.Db.Entry(state).State == EntityState.Modified) state.UpdatedAtUtc = _store.UtcNow;
        await _store.Db.SaveChangesAsync(ct);
        return (new DataSyncRefreshResult(run.Changed, run.Tombstoned, new DataSyncActorId(state.ActorId), false,
            collectPublished ? run.Published : null), state);
    }

    /// <summary>Kinds in apply order (<see cref="DataSyncKindIds.All"/>), others after them ordinally.</summary>
    private static IEnumerable<string> Ordered(IReadOnlyCollection<string> kinds)
    {
        var requested = kinds.ToHashSet(StringComparer.Ordinal);
        return DataSyncKindIds.All.Where(requested.Contains)
            .Concat(requested.Except(DataSyncKindIds.All).OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>One Refresh: the counters it issues, what it drafts and what it collects.</summary>
    private sealed class Run(
        DataSyncRefresher owner,
        DataSyncLocalStateDbModel state,
        DataSyncDevice device,
        bool collectPublished,
        DataSyncRefreshOptions options,
        IReadOnlyList<string> kinds)
    {
        private readonly DataSyncActorId _self = new(state.ActorId);

        /// <summary>When the last committed Refresh of each kind began, as this one found it (§6.5).</summary>
        private readonly IReadOnlyDictionary<string, long> _refreshedAt =
            DataSyncStoredJson.ReadCounters(state.RefreshedAtJson, "RefreshedAtJson");
        private readonly List<DataSyncInboxDraft> _drafts = [];
        private readonly HashSet<(string Kind, string LocalKey)> _release =
            (options.ReleaseHeld ?? []).ToHashSet();
        private readonly Dictionary<(string Kind, string LocalKey), (string? OrderKey, DataSyncEntityForm Form)> _forms =
            new();
        private DataSyncAppliedChangesIndex? _applied;

        public int Changed { get; private set; }
        public int Tombstoned { get; private set; }

        public Dictionary<(string Kind, string LocalKey), DataSyncPublishedEntity> Published { get; } = new();

        private DataSyncStore Store => owner._store;
        private BakabaseDbContext Db => owner._store.Db;
        private DateTime Now => owner._store.UtcNow;

        private long NextCounter() => state.ActorCounter = checked(state.ActorCounter + 1);

        private DataSyncVersionVector Revise(DataSyncRevisionKind kind, DataSyncVersionVector local) =>
            DataSyncRevisionRules.Next(kind, local, null, false, false, _self, NextCounter);

        private void SetEditorSelf(DataSyncEntityDbModel row)
        {
            row.LastActorId = state.ActorId;
            row.LastEditorNodeId = state.NodeId;
            row.LastEditorName = device.Name;
        }

        public async Task RefreshKindAsync(string kind, IDataSyncKind adapter, CancellationToken ct)
        {
            var codec = adapter.Codec;
            var versions = new Dictionary<string, int>(
                DataSyncStoredJson.ReadVersions(state.ComparisonFormVersionsJson, "ComparisonFormVersionsJson"),
                StringComparer.Ordinal);
            var recompute = !versions.TryGetValue(kind, out var storedVersion) ||
                            storedVersion != codec.ComparisonFormVersion;

            var rows = (await Store.GetEntitiesAsync(kind, includeTombstones: false, ct))
                .ToDictionary(r => r.LocalKey, StringComparer.Ordinal);
            var raw = await adapter.ReadRawHashesAsync(ct);

            // Fast path (§6.1): only rows whose raw hash moved, new rows, rows marked dirty (RawHash null: an overlay,
            // childrenLocal or state change) and released holds are read — unless every form must be recomputed or a
            // snapshot needs what every entity publishes.
            var dirty = raw.Keys
                .Where(k => recompute || collectPublished || !rows.TryGetValue(k, out var row) ||
                            !string.Equals(row.RawHash, raw[k], StringComparison.Ordinal) ||
                            _release.Contains((kind, k)))
                .ToList();
            var locals = new Dictionary<string, LocalEntity>(StringComparer.Ordinal);
            var readOrder = new List<string>();
            await ReadIntoAsync(adapter, dirty, locals, readOrder, ct);

            var moves = await DetectMovesAsync(kind, adapter, rows, raw, locals, ct);
            await ReadIntoAsync(adapter, moves.Keys.Where(k => !locals.ContainsKey(k)).ToList(), locals, readOrder, ct);

            var inserts = new List<DataSyncEntityDbModel>();
            foreach (var localKey in readOrder)
            {
                var local = locals[localKey];
                var orderKey = moves.TryGetValue(localKey, out var moved) ? moved : null;
                if (!rows.TryGetValue(localKey, out var row))
                {
                    inserts.Add(NewRow(kind, codec, local, raw[localKey], orderKey));
                    continue;
                }

                if (DataSyncIdentityStore.IsReusedId(row.Fingerprint, local.Fingerprint))
                {
                    // ID reuse (§5.5): the old identity becomes a tombstone before the fresh row takes the local key.
                    await TombstoneAsync(row, ct);
                    rows.Remove(localKey);
                    inserts.Add(NewRow(kind, codec, local, raw[localKey], orderKey));
                    continue;
                }

                await RefreshRowAsync(kind, codec, row, local, raw[localKey], orderKey ?? row.OrderKey, recompute, ct);
            }

            if (inserts.Count > 0)
            {
                await owner._identity.InsertFreshRangeAsync(inserts, ct);
                foreach (var row in inserts) rows[row.LocalKey] = row;
            }

            // Local deletions (§6.3): only a Synced row's tombstone is served; deleting what never synced tells nobody.
            foreach (var row in rows.Values.Where(r => !raw.ContainsKey(r.LocalKey)).ToList())
            {
                await TombstoneAsync(row, ct);
                rows.Remove(row.LocalKey);
            }

            if (recompute) await RecomputeBaseFormsAsync(kind, codec, ct);
            versions[kind] = codec.ComparisonFormVersion;
            state.ComparisonFormVersionsJson = DataSyncStoredJson.WriteVersions(versions);
            await NoteSchemaVersionAsync(kind, codec, ct);

            if (collectPublished) Collect(kind, codec, rows.Values, locals);
            await Db.SaveChangesAsync(ct);
        }

        private static async Task ReadIntoAsync(IDataSyncKind adapter, IReadOnlyCollection<string> keys,
            Dictionary<string, LocalEntity> into, List<string> order, CancellationToken ct)
        {
            if (keys.Count == 0) return;
            foreach (var local in await adapter.ReadAsync(keys, ct))
            {
                if (into.TryAdd(local.LocalKey, local)) order.Add(local.LocalKey);
            }
        }

        /// <summary>
        /// §3.7 local moves, over the synced, live, published entities in local order: new definitions that will be
        /// Synced join without a key, held ones (publish held, unreadable, held at source) keep theirs.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, string>> DetectMovesAsync(string kind, IDataSyncKind adapter,
            IReadOnlyDictionary<string, DataSyncEntityDbModel> rows, IReadOnlyDictionary<string, string> raw,
            IReadOnlyDictionary<string, LocalEntity> locals, CancellationToken ct)
        {
            var codec = adapter.Codec;
            if (!codec.Descriptor.HasOrder) return new Dictionary<string, string>();
            var detector = owner._services.GetService<IDataSyncOrderMoveDetector>() ??
                           throw new InvalidOperationException(
                               $"Kind '{kind}' has an order, but no {nameof(IDataSyncOrderMoveDetector)} is registered (§3.7).");
            var order = await adapter.ReadOrderAsync(ct);
            var index = await owner._identity.GetKeyIndexAsync(kind, null, ct);
            var tieKeys = index.Entities.Where(e => e.Live).ToDictionary(e => e.Id,
                e => e.Keys.All.Select(k => k.Value).Min(StringComparer.Ordinal)!);

            var entries = new List<DataSyncOrderMoveEntry>();
            foreach (var localKey in order)
            {
                if (!raw.ContainsKey(localKey)) continue;
                locals.TryGetValue(localKey, out var local);
                rows.TryGetValue(localKey, out var row);
                var isNew = row is null ||
                            (local is not null && DataSyncIdentityStore.IsReusedId(row.Fingerprint, local.Fingerprint));
                if (isNew)
                {
                    if (state.NewDefinitionsStayLocal || local is null || local.Unreadable) continue;
                    if (Evaluate(codec, null, local, null).IsHeld) continue;
                    entries.Add(new DataSyncOrderMoveEntry(localKey, null, null));
                    continue;
                }

                if (row!.State != DataSyncEntitySyncState.Synced || (row.PublishHeld && !_release.Contains((kind, localKey))))
                    continue;
                var unreadable = local?.Unreadable ?? row.Unreadable;
                if (unreadable) continue;
                var held = local is null
                    ? DataSyncEntityForms.IsHeldMarker(row.SharedHash)
                    : Evaluate(codec, row, local, row.OrderKey).IsHeld;
                if (held) continue;
                entries.Add(new DataSyncOrderMoveEntry(localKey, row.OrderKey, tieKeys[row.Id]));
            }

            return entries.Count == 0
                ? new Dictionary<string, string>()
                : detector.DetectMoves(entries, owner.Limits.MaxOrderKeyLength);
        }

        private DataSyncEntityDbModel NewRow(string kind, IDataSyncKindCodec codec, LocalEntity local, string rawHash,
            string? orderKey)
        {
            var syncState = state.NewDefinitionsStayLocal ? DataSyncEntitySyncState.LocalOnly : DataSyncEntitySyncState.Synced;
            var form = Evaluate(codec, null, local, syncState == DataSyncEntitySyncState.Synced ? orderKey : null);
            var row = new DataSyncEntityDbModel
            {
                Kind = kind,
                LocalKey = local.LocalKey,
                OriginNodeId = state.NodeId,
                Fingerprint = local.Fingerprint,
                LocalHash = form.LocalHash,
                RawHash = rawHash,
                SharedHash = form.SharedHash ?? DataSyncEntityForms.NoSharedHash,
                OrderKey = syncState == DataSyncEntitySyncState.Synced ? orderKey : null,
                State = syncState,
                VvJson = Revise(DataSyncRevisionKind.LocalEdit, DataSyncVersionVector.Empty).ToCanonicalString(),
                Unreadable = local.Unreadable,
                CreatedBySync = false,
            };
            SetEditorSelf(row);
            Changed++;
            return row;
        }

        private async Task TombstoneAsync(DataSyncEntityDbModel row, CancellationToken ct)
        {
            var vv = Revise(DataSyncRevisionKind.LocalDelete, DataSyncVersionVector.ParseStored(row.VvJson));
            await owner._identity.TombstoneAsync(row,
                new DataSyncTombstoneWrite(vv, DataSyncTombstoneKind.Deleted, row.State == DataSyncEntitySyncState.Synced,
                    new DataSyncEditorRef(state.NodeId, device.Name, state.ActorId)), ct);
            Tombstoned++;
        }

        /// <summary>
        /// The entity's forms with <paramref name="orderKey"/>; computed once per Refresh and order key, since
        /// publishing a large property validates every child.
        /// </summary>
        private DataSyncEntityForm Evaluate(IDataSyncKindCodec codec, DataSyncEntityDbModel? row, LocalEntity local,
            string? orderKey)
        {
            var key = (codec.Descriptor.Kind, local.LocalKey);
            if (_forms.TryGetValue(key, out var cached) && string.Equals(cached.OrderKey, orderKey, StringComparison.Ordinal))
                return cached.Form;
            var form = row is null
                ? DataSyncEntityForms.Evaluate(codec, local, DataSyncOverlay.None, false, orderKey, null)
                : DataSyncEntityForms.Evaluate(codec, local, DataSyncStoredJson.ReadOverlay(row.OverlayJson),
                    row.ChildrenLocal, orderKey, DataSyncEntityForms.ReadUnknown(row.UnknownJson));
            _forms[key] = (orderKey, form);
            return form;
        }

        private async Task RefreshRowAsync(string kind, IDataSyncKindCodec codec, DataSyncEntityDbModel row,
            LocalEntity local, string rawHash, string? orderKey, bool recompute, CancellationToken ct)
        {
            var localHash = ContentHash.Of(local.Content);
            // Marked by an overlay, childrenLocal or state change since the last Refresh (§6.1): read again. A mark
            // alone is no Seq: a writer that changed what the entity publishes gave it one already (§6.2).
            var marked = row.RawHash is null;
            var unchangedSinceLastRefresh = !marked && !row.Unreadable && !local.Unreadable &&
                                            string.Equals(row.LocalHash, localHash, StringComparison.Ordinal);
            if (recompute)
            {
                if (row.State == DataSyncEntitySyncState.Synced && !row.PublishHeld && unchangedSinceLastRefresh)
                {
                    // A new ComparisonFormVersion recomputes the hash of what did not change: no revision, no Seq
                    // (§3.4). Only a row that publishes its current content: its SharedHash describes that content.
                    row.SharedHash = Evaluate(codec, row, local, row.OrderKey).SharedHash ??
                                     DataSyncEntityForms.NoSharedHash;
                }
                else if (row.State != DataSyncEntitySyncState.Synced || row.PublishHeld || row.Unreadable ||
                         local.Unreadable)
                {
                    // An unsynced, held or unreadable row's SharedHash describes what it last published, which its
                    // current content may no longer be; this build cannot compute that old form. A hash no form
                    // equals makes rejoining, releasing or reading it again always a revision, never old content
                    // published under an old vector.
                    row.SharedHash = DataSyncEntityForms.NoSharedHash;
                }
            }

            // A local-only difference (child ids, local order, folding) changes these and nothing else: no Seq.
            row.LocalHash = localHash;
            row.RawHash = rawHash;
            if (row.Fingerprint is null && local.Fingerprint is not null) row.Fingerprint = local.Fingerprint;
            // An unreadable row is recorded without bumping anything (§3.3); the feed serves it held.
            row.Unreadable = local.Unreadable;

            var released = _release.Contains((kind, row.LocalKey)) && row.PublishHeld;
            if (released)
            {
                row.PublishHeld = false;
                row.Seq = await Store.NextSeqAsync(ct);
                row.UpdatedAtUtc = Now;
            }

            // Never revisions for unsynced or unreadable rows.
            if (row.State != DataSyncEntitySyncState.Synced || local.Unreadable) return;

            var form = Evaluate(codec, row, local, orderKey);
            if (form.IsHeld)
            {
                // Held at source (§3.5 step 4): no revision; the published record turns into HeldAtSource once.
                if (!string.Equals(row.SharedHash, form.SharedHash, StringComparison.Ordinal))
                {
                    row.SharedHash = form.SharedHash!;
                    row.Seq = await Store.NextSeqAsync(ct);
                    row.UpdatedAtUtc = Now;
                }

                return;
            }

            if (string.Equals(form.SharedHash, row.SharedHash, StringComparison.Ordinal) &&
                string.Equals(orderKey, row.OrderKey, StringComparison.Ordinal))
            {
                // Nothing a reader compares changed. An overlay change that altered only the published content (a
                // duplicate of a label class kept here only) got its Seq when it was stored (SetOverlayAsync).
                return;
            }

            if (row.PublishHeld)
            {
                await RefreshHeldItemAsync(kind, codec, row, local, ct);
                return;
            }

            if (!released && await SuspectAsync(kind, codec, row, local, ct)) return;

            row.SharedHash = form.SharedHash!;
            row.OrderKey = orderKey;
            row.Seq = await Store.NextSeqAsync(ct);
            row.VvJson = Revise(DataSyncRevisionKind.LocalEdit, DataSyncVersionVector.ParseStored(row.VvJson))
                .ToCanonicalString();
            SetEditorSelf(row);
            row.UpdatedAtUtc = Now;
            Changed++;
        }

        /// <summary>
        /// §6.5: the change undoes what a recent apply wrote — an apply of the window before the change may have
        /// been made, which is any time since the kind's last committed Refresh began, however long ago
        /// (<see cref="DataSyncLostUpdateGuard.WindowStart"/>). The entity is held: no revision, <c>PublishHeld</c>, a
        /// Seq bump so readers receive it as <c>HeldAtSource = PendingDecision</c> and keep their version, and a
        /// state-derived <c>SuspectedLostUpdate</c> item of no link. Incoming merges of it freeze (row F).
        /// </summary>
        private async Task<bool> SuspectAsync(string kind, IDataSyncKindCodec codec, DataSyncEntityDbModel row,
            LocalEntity local, CancellationToken ct)
        {
            _applied ??= await DataSyncAppliedChangesIndex.LoadRecentAsync(Db,
                kinds.Select(k => DataSyncLostUpdateGuard.WindowStart(_refreshedAt, k, Now)).Min(), ct);
            var applied = _applied.Get(kind, row.LocalKey);
            if (applied is null || applied.Changes.IsEmpty ||
                applied.AppliedAtUtc < DataSyncLostUpdateGuard.WindowStart(_refreshedAt, kind, Now))
            {
                return false;
            }

            var undone = DataSyncLostUpdateGuard.FindUndone(codec, local.Content, row.ChildrenLocal, applied.Changes);
            if (undone.Count == 0) return false;

            row.PublishHeld = true;
            row.Seq = await Store.NextSeqAsync(ct);
            row.UpdatedAtUtc = Now;
            _drafts.Add(await DraftOfAsync(kind, codec, row, local, applied, undone, ct));
            return true;
        }

        /// <summary>A later local edit of a held entity only updates its item (§6.5).</summary>
        private async Task RefreshHeldItemAsync(string kind, IDataSyncKindCodec codec, DataSyncEntityDbModel row,
            LocalEntity local, CancellationToken ct)
        {
            var applied = await DataSyncAppliedChangesIndex.FindLatestAsync(Db, kind, row.LocalKey, ct);
            if (applied is null) return;
            var undone = DataSyncLostUpdateGuard.FindUndone(codec, local.Content, row.ChildrenLocal, applied.Changes);
            if (undone.Count == 0) return;
            _drafts.Add(await DraftOfAsync(kind, codec, row, local, applied, undone, ct));
        }

        /// <summary>The item, with the children "Put the synced change back" would remove that resources here use.</summary>
        private async Task<DataSyncInboxDraft> DraftOfAsync(string kind, IDataSyncKindCodec codec,
            DataSyncEntityDbModel row, LocalEntity local, DataSyncAppliedChanges applied,
            IReadOnlyList<DataSyncFieldOutcome> undone, CancellationToken ct)
        {
            var content = codec.ReadLocal(local.Content);
            IReadOnlyList<DataSyncDisplayValue> inUse = Store.Kinds.TryGetValue(kind, out var adapter)
                ? await DataSyncLostUpdateGuard.ReapplyInUseAsync(adapter, row.LocalKey, content, row.ChildrenLocal,
                    applied.Changes, ct)
                : [];
            return DataSyncLostUpdateGuard.Draft(kind, new SyncKey(row.SyncKey), row.LocalKey, codec.NameOf(content),
                codec.SubtypeOf(content), applied.PeerName, undone, DataSyncVersionVector.ParseStored(row.VvJson),
                inUse);
        }

        public async Task FlushItemsAsync(CancellationToken ct)
        {
            if (_drafts.Count == 0) return;
            await Store.UpsertItemsAsync(null, null, _drafts, Now, ct);
            _drafts.Clear();
        }

        /// <summary>
        /// §8.4 condition 6: this build's schema version of the kind, recorded in <c>KindSchemaVersionsJson</c>. When
        /// it differs from the recorded one (an upgrade, or the first Refresh of the kind), every <c>Held</c> pending
        /// record of the kind, on every link, is marked never evaluated, so each link's next merge takes it once — a
        /// record held as <c>NewerSchema</c> is then applied — and later merges leave it alone until something else
        /// changes.
        /// </summary>
        private async Task NoteSchemaVersionAsync(string kind, IDataSyncKindCodec codec, CancellationToken ct)
        {
            var schemas = new Dictionary<string, int>(
                DataSyncStoredJson.ReadVersions(state.KindSchemaVersionsJson, "KindSchemaVersionsJson"),
                StringComparer.Ordinal);
            var current = codec.Descriptor.SchemaVersion;
            if (schemas.TryGetValue(kind, out var recorded) && recorded == current) return;

            foreach (var held in await Db.DataSyncPeerBases
                         .Where(b => b.Kind == kind && b.PendingReason == DataSyncPendingReason.Held &&
                                     b.PendingEvaluatedLocalSeq != null)
                         .ToListAsync(ct))
            {
                held.PendingEvaluatedLocalSeq = null;
                held.UpdatedAtUtc = Now;
            }

            schemas[kind] = current;
            state.KindSchemaVersionsJson = DataSyncStoredJson.WriteVersions(schemas);
        }

        /// <summary>A new ComparisonFormVersion also recomputes the bases' hashes of this kind, on every link (§6.1).</summary>
        private async Task RecomputeBaseFormsAsync(string kind, IDataSyncKindCodec codec, CancellationToken ct)
        {
            foreach (var peerBase in await Db.DataSyncPeerBases.Where(b => b.Kind == kind && b.RecordJson != null)
                         .ToListAsync(ct))
            {
                var record = DataSyncStoredJson.ReadRecord(peerBase.RecordJson, "RecordJson")!;
                peerBase.SharedHash = DataSyncEntityForms.RecordSharedHash(codec, record, owner.Limits);
            }
        }

        /// <summary>
        /// What every synced live entity publishes, exactly as computed here (§6.6): a snapshot never reads the
        /// adapter a second time.
        /// </summary>
        private void Collect(string kind, IDataSyncKindCodec codec, IEnumerable<DataSyncEntityDbModel> rows,
            IReadOnlyDictionary<string, LocalEntity> locals)
        {
            foreach (var row in rows.Where(r => r.DeletedAtUtc is null && r.State == DataSyncEntitySyncState.Synced))
            {
                if (!locals.TryGetValue(row.LocalKey, out var local)) continue;
                DataSyncPublishedEntity published;
                if (row.Unreadable)
                    published = new DataSyncPublishedEntity(null, null, 0, DataSyncHeldReason.LocalUnreadable,
                        "localUnreadable");
                else if (row.PublishHeld)
                    published = new DataSyncPublishedEntity(null, null, 0, DataSyncHeldReason.PendingDecision,
                        "suspectedLostUpdate");
                else
                    published = Evaluate(codec, row, local, row.OrderKey).Published;
                Published[(kind, row.LocalKey)] = published;
            }
        }
    }
}

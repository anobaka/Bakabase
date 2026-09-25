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
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// Identity (§5; v3.1 §5.3): the only writer of the key columns of <c>DataSyncEntities</c> (which rows exist, their
/// primary <c>SyncKey</c>, live or tombstoned) and of <c>DataSyncKeyAliases</c>. It keeps the key invariant: within
/// one kind, every key is exactly one of the primary of a live row, the primary of a tombstoned row, or an alias
/// pointing at the primary of one existing row. An alias never points at another alias.
/// </summary>
/// <remarks>
/// Every operation runs in the caller's transaction, takes its Seq numbers from <see cref="DataSyncStore.NextSeqAsync"/>
/// (a row gets one whenever its published record changes, §6.2) and saves before it returns. Vectors come from the
/// caller (through <c>DataSyncRevisionRules</c>) and are checked never to go backwards; Retire computes its own
/// (§5.3). Content columns (hashes, overlay, order key…) are the caller's; this class copies them where a row is
/// created or revived.
/// </remarks>
public sealed class DataSyncIdentityStore(DataSyncStore store)
{
    /// <summary>The detail of an item the pre-flight refused (v3.1 B4).</summary>
    public const string RefusedDetail = "identityConflict";

    private BakabaseDbContext Db => store.Db;

    #region §5.5 ID reuse

    /// <summary>
    /// v3.1 §5.4: a local id was reused when both fingerprints are known and differ. An unknown fingerprint (a custom
    /// property damaged by the old ChangeType bug, every extension group) never splits an identity.
    /// </summary>
    public static bool IsReusedId(string? storedFingerprint, string? currentFingerprint) =>
        storedFingerprint is not null && currentFingerprint is not null &&
        !string.Equals(storedFingerprint, currentFingerprint, StringComparison.Ordinal);

    /// <summary>
    /// A local id now names another definition (§5.5): the old identity becomes a tombstone, which is saved before
    /// the fresh row is inserted so the filtered LocalKey index is free (v3.1 L2).
    /// </summary>
    public async Task<DataSyncEntityDbModel> ReplaceReusedIdAsync(DataSyncEntityDbModel reused,
        DataSyncTombstoneWrite tombstone, DataSyncEntityDbModel fresh, CancellationToken ct)
    {
        await TombstoneAsync(reused, tombstone, ct);
        return await InsertFreshAsync(fresh, ct);
    }

    #endregion

    #region §5.1 Creating keys

    /// <summary>A new local definition: a random primary key (§5.1). See <see cref="InsertFreshRangeAsync"/>.</summary>
    public async Task<DataSyncEntityDbModel> InsertFreshAsync(DataSyncEntityDbModel row, CancellationToken ct) =>
        (await InsertFreshRangeAsync([row], ct))[0];

    /// <summary>
    /// Inserts live rows with fresh random primary keys, each with the next Seq; a row without an origin takes this
    /// device's node (a local definition, §6.1). A fresh key never collides in practice; the invariant is still
    /// checked, and a colliding key is minted again once (v3.1 §4.4).
    /// </summary>
    public async Task<IReadOnlyList<DataSyncEntityDbModel>> InsertFreshRangeAsync(
        IReadOnlyList<DataSyncEntityDbModel> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return rows;
        foreach (var row in rows) RequireNewRow(row);
        await store.FlushAsync(ct);
        var self = (await store.LoadStateAsync(ct))?.NodeId;
        foreach (var row in rows.Where(r => string.IsNullOrEmpty(r.OriginNodeId)))
            row.OriginNodeId = self ?? throw new InvalidOperationException("Data sync has no local state yet (§4.5).");

        var now = store.UtcNow;
        foreach (var byKind in rows.GroupBy(r => r.Kind))
        {
            var group = byKind.ToList();
            var keys = group.Select(_ => SyncKey.New().Value).ToArray();
            for (var attempt = 0;; attempt++)
            {
                var taken = await TakenKeysAsync(byKind.Key, keys, ct);
                if (taken.Count == 0 && keys.Distinct().Count() == keys.Length) break;
                if (attempt == 1)
                    throw new DataSyncIdentityRefusedException(byKind.Key, taken.FirstOrDefault() ?? keys[0],
                        "a freshly minted key is already in use");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < keys.Length; i++)
                {
                    if (taken.Contains(keys[i]) || !seen.Add(keys[i])) keys[i] = SyncKey.New().Value;
                }
            }

            for (var i = 0; i < group.Count; i++)
            {
                var row = group[i];
                row.SyncKey = keys[i];
                row.Seq = await store.NextSeqAsync(ct);
                if (row.CreatedAtUtc == default) row.CreatedAtUtc = now;
                row.UpdatedAtUtc = now;
                Db.DataSyncEntities.Add(row);
            }
        }

        await Db.SaveChangesAsync(ct);
        return rows;
    }

    /// <summary>
    /// A definition created from a peer record takes the record's keys (§5.1; v3.1 §5.3 "Create with primary K"):
    /// <list type="bullet">
    /// <item><see cref="EntityKeys.None"/>: a fresh key (a separate create while the incoming key is bound here);</item>
    /// <item>K is the primary of a tombstone: that row is revived with <paramref name="row"/>'s content columns and
    /// vector, which must be ≥ the tombstone's (revision Revive, §2.8); the old row goes into the pre-image;</item>
    /// <item>K is an alias of a tombstone: that alias row is deleted and a new row takes K;</item>
    /// <item>K is live anywhere: refused.</item>
    /// </list>
    /// The other keys then become aliases (<see cref="AddAliasesAsync"/>). Returns the row that now holds K: the
    /// revived tombstone, or <paramref name="row"/>.
    /// </summary>
    public async Task<DataSyncEntityDbModel> CreateAsync(DataSyncEntityDbModel row, EntityKeys keys,
        DataSyncIdentityPreImage? preImage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);
        RequireNewRow(row);
        if (keys.All.Count == 0) return await InsertFreshAsync(row, ct);
        if (keys.All.Distinct().Count() != keys.All.Count)
            throw new ArgumentException("An entity's keys must be distinct.", nameof(keys));
        await store.FlushAsync(ct);

        var kind = row.Kind;
        var primary = keys.All[0].Value;
        var now = store.UtcNow;
        var owner = await store.FindOwnerAsync(kind, primary, ct);
        if (owner is {DeletedAtUtc: null})
            throw new DataSyncIdentityRefusedException(kind, primary, "the key is live here");

        DataSyncEntityDbModel result;
        if (owner is not null && owner.SyncKey == primary)
        {
            // Revive (v3.1 M-a): the lineage continues in the same row.
            RequireAtLeast(row.VvJson, owner.VvJson, kind, primary, "a revive");
            preImage?.RevivedTombstones.Add(owner with { });
            CopyContent(row, owner);
            owner.DeletedAtUtc = null;
            owner.TombstoneKind = null;
            owner.TombstoneServed = false;
            owner.Seq = await store.NextSeqAsync(ct);
            owner.UpdatedAtUtc = now;
            result = owner;
        }
        else
        {
            if (owner is not null)
            {
                // K is an alias of a tombstone, which keeps its other keys.
                var alias = await Db.DataSyncKeyAliases.SingleAsync(a => a.Kind == kind && a.AliasKey == primary, ct);
                preImage?.RemovedAliases.Add(new DataSyncAliasRow(kind, alias.AliasKey, alias.SyncKey));
                Db.DataSyncKeyAliases.Remove(alias);
                await Db.SaveChangesAsync(ct);
            }

            row.SyncKey = primary;
            row.Seq = await store.NextSeqAsync(ct);
            if (row.CreatedAtUtc == default) row.CreatedAtUtc = now;
            row.UpdatedAtUtc = now;
            Db.DataSyncEntities.Add(row);
            result = row;
        }

        await Db.SaveChangesAsync(ct);
        if (keys.All.Count > 1) await AddAliasesAsync(result, keys.All.Skip(1), preImage, ct);
        return result;
    }

    #endregion

    #region §5.3 Aliases, retire, tombstones

    /// <summary>
    /// Adds keys to live entity <paramref name="live"/> (a link, an update or a bind; v3.1 §5.3):
    /// <list type="bullet">
    /// <item>already one of its keys: nothing;</item>
    /// <item>a key of another live entity (in any state, §5.3): refused;</item>
    /// <item>the primary of tombstone T: T is <b>retired</b> into it — its vector becomes Max(its vector, T's)
    /// (revision Retire), T's aliases and bases are re-pointed to it (a base for it on the same link wins), T's
    /// open items close Superseded and T's row is deleted, all recorded in the pre-image;</item>
    /// <item>an alias of a tombstone: that alias row is deleted, then the key is added.</item>
    /// </list>
    /// The entity gets the next Seq when anything was added (§6.2). Returns whether anything changed.
    /// </summary>
    public async Task<bool> AddAliasesAsync(DataSyncEntityDbModel live, IEnumerable<SyncKey> keys,
        DataSyncIdentityPreImage? preImage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(keys);
        RequireLive(live);
        await store.FlushAsync(ct);
        var kind = live.Kind;
        var now = store.UtcNow;
        var changed = false;
        foreach (var key in keys.Select(k => k.Value).Distinct(StringComparer.Ordinal))
        {
            if (key == live.SyncKey) continue;
            var alias = await Db.DataSyncKeyAliases.SingleOrDefaultAsync(a => a.Kind == kind && a.AliasKey == key, ct);
            if (alias?.SyncKey == live.SyncKey) continue;

            var ownerOfPrimary = await Db.DataSyncEntities.SingleOrDefaultAsync(e => e.Kind == kind && e.SyncKey == key, ct);
            if (ownerOfPrimary is not null)
            {
                if (ownerOfPrimary.DeletedAtUtc is null)
                    throw new DataSyncIdentityRefusedException(kind, key, "the key is live elsewhere");
                await RetireAsync(ownerOfPrimary, live, preImage, ct);
            }
            else if (alias is not null)
            {
                var aliasOwner = await Db.DataSyncEntities.SingleAsync(e => e.Kind == kind && e.SyncKey == alias.SyncKey, ct);
                if (aliasOwner.DeletedAtUtc is null)
                    throw new DataSyncIdentityRefusedException(kind, key, "the key is live elsewhere");
                preImage?.RemovedAliases.Add(new DataSyncAliasRow(kind, key, alias.SyncKey));
                Db.DataSyncKeyAliases.Remove(alias);
                await Db.SaveChangesAsync(ct);
            }

            Db.DataSyncKeyAliases.Add(new DataSyncKeyAliasDbModel
                {Kind = kind, AliasKey = key, SyncKey = live.SyncKey, CreatedAtUtc = now});
            preImage?.AddedAliases.Add(new DataSyncAliasRow(kind, key, live.SyncKey));
            changed = true;
        }

        if (changed)
        {
            live.Seq = await store.NextSeqAsync(ct);
            live.UpdatedAtUtc = now;
        }

        await Db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>
    /// Retire (§5.3): tombstone T's lineage joins live L. L's vector takes Max(L, T) with no counter (revision
    /// Retire, §2.8), so vectors stay monotonic; T's aliases now point at L (N5); T's bases move to L on every link
    /// that has no base for L yet, and are deleted where one exists.
    /// </summary>
    private async Task RetireAsync(DataSyncEntityDbModel tombstone, DataSyncEntityDbModel live,
        DataSyncIdentityPreImage? preImage, CancellationToken ct)
    {
        var kind = live.Kind;
        preImage?.RetiredIdentities.Add(tombstone with { });
        var self = await SelfActorAsync(ct);
        live.VvJson = DataSyncRevisionRules.Next(DataSyncRevisionKind.Retire,
                DataSyncVersionVector.ParseStored(live.VvJson), remote: null, resultEqualsRemote: false,
                resultEqualsLocal: true, self, () => throw new InvalidOperationException("Retire issues no counter."),
                tombstone: DataSyncVersionVector.ParseStored(tombstone.VvJson))
            .ToCanonicalString();

        foreach (var alias in await Db.DataSyncKeyAliases.Where(a => a.Kind == kind && a.SyncKey == tombstone.SyncKey)
                     .ToListAsync(ct))
        {
            preImage?.RepointedAliases.Add(new DataSyncAliasRepoint(kind, alias.AliasKey, tombstone.SyncKey, live.SyncKey));
            alias.SyncKey = live.SyncKey;
        }

        await store.CloseOpenItemsOfAsync(kind, [tombstone.SyncKey], DataSyncInboxClosure.Superseded, ct);
        Db.DataSyncEntities.Remove(tombstone);
        await Db.SaveChangesAsync(ct);
        await store.RepointBasesAsync(kind, tombstone.SyncKey, live.SyncKey, ct);
    }

    /// <summary>
    /// Makes a live row a tombstone (§6.3): its aliases keep pointing at it, it gets the next Seq and the given
    /// vector (never below its own), and every open item of the entity closes Superseded (§9.3). The state column
    /// keeps the state at deletion.
    /// </summary>
    public async Task TombstoneAsync(DataSyncEntityDbModel row, DataSyncTombstoneWrite tombstone, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(tombstone);
        RequireLive(row);
        var vv = tombstone.Vv.ToCanonicalString();
        RequireAtLeast(vv, row.VvJson, row.Kind, row.SyncKey, "a tombstone");
        await store.FlushAsync(ct);
        var now = store.UtcNow;
        row.DeletedAtUtc = now;
        row.UpdatedAtUtc = now;
        row.Seq = await store.NextSeqAsync(ct);
        row.VvJson = vv;
        row.TombstoneKind = tombstone.TombstoneKind;
        row.TombstoneServed = tombstone.Served;
        row.PublishHeld = false;
        if (tombstone.Editor is { } editor)
        {
            row.LastActorId = editor.ActorId;
            row.LastEditorNodeId = editor.NodeId;
            row.LastEditorName = editor.Name;
        }

        await store.CloseOpenItemsOfAsync(row.Kind, await store.KeysOfAsync(row.Kind, row.SyncKey, ct),
            DataSyncInboxClosure.Superseded, ct);
        await Db.SaveChangesAsync(ct);
    }

    #endregion

    #region §5.3 Rekey and key moves (KeepWithEntity, KeepRecordLinked)

    /// <summary>
    /// Rekey (§5.3): the entity gets a fresh primary; its aliases follow it; its bases and pending records are
    /// deleted on every link (its next merge on each link then runs without a base, which never deletes; engineering
    /// must-fix 15); its open merger-derived items close Superseded (their pending records are gone) and its
    /// state-derived items follow the new key. The old primary is free afterwards. Returns it.
    /// </summary>
    /// <remarks>A resolution closes the items it decides (<c>ResolvedHere</c>) before it re-keys.</remarks>
    public async Task<string> RekeyAsync(DataSyncEntityDbModel entity, DataSyncIdentityPreImage? preImage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entity);
        RequireLive(entity);
        await store.FlushAsync(ct);
        var kind = entity.Kind;
        var oldPrimary = entity.SyncKey;
        string newPrimary;
        do
        {
            newPrimary = SyncKey.New().Value;
        } while ((await TakenKeysAsync(kind, [newPrimary], ct)).Count > 0);

        foreach (var alias in await Db.DataSyncKeyAliases.Where(a => a.Kind == kind && a.SyncKey == oldPrimary)
                     .ToListAsync(ct))
        {
            alias.SyncKey = newPrimary;
        }

        var keys = await store.KeysOfAsync(kind, oldPrimary, ct);
        var now = store.UtcNow;
        foreach (var item in await Db.DataSyncInboxItems
                     .Where(i => i.ClosedAtUtc == null && i.Kind == kind && keys.Contains(i.SyncKey)).ToListAsync(ct))
        {
            if (item.Origin == DataSyncInboxItemOrigin.Merger)
            {
                item.ClosedAtUtc = now;
                item.UpdatedAtUtc = now;
                item.Closure = DataSyncInboxClosure.Superseded;
            }
            else if (item.SyncKey == oldPrimary)
            {
                item.SyncKey = newPrimary;
                item.UpdatedAtUtc = now;
            }
        }

        await store.DeleteBasesOfKeyAsync(kind, oldPrimary, ct);
        entity.SyncKey = newPrimary;
        entity.Seq = await store.NextSeqAsync(ct);
        entity.UpdatedAtUtc = now;
        preImage?.KeyMoves.Add(new DataSyncKeyMove(kind, oldPrimary, new DataSyncKeyOwner(entity.Id, true), null));
        preImage?.KeyMoves.Add(new DataSyncKeyMove(kind, newPrimary, null, new DataSyncKeyOwner(entity.Id, true)));
        await Db.SaveChangesAsync(ct);
        return oldPrimary;
    }

    /// <summary>
    /// KeepWithEntity (§9.2, row I): the given keys that belong to <paramref name="from"/> move to
    /// <paramref name="to"/>: its aliases are re-pointed; its primary, if among them, is taken by a rekey of
    /// <paramref name="from"/> and becomes an alias of <paramref name="to"/>. Both get the next Seq. Returns the keys
    /// that moved.
    /// </summary>
    public async Task<IReadOnlyList<string>> MoveKeysAsync(DataSyncEntityDbModel from, DataSyncEntityDbModel to,
        IEnumerable<SyncKey> keys, DataSyncIdentityPreImage? preImage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        RequireLive(from);
        RequireLive(to);
        if (from.Kind != to.Kind || from.Id == to.Id)
            throw new ArgumentException("Keys move between two entities of one kind.", nameof(to));
        await store.FlushAsync(ct);
        var kind = from.Kind;
        var moved = new List<string>();
        var now = store.UtcNow;
        foreach (var key in keys.Select(k => k.Value).Distinct(StringComparer.Ordinal))
        {
            if (key == from.SyncKey)
            {
                await RekeyAsync(from, preImage, ct);
                Db.DataSyncKeyAliases.Add(new DataSyncKeyAliasDbModel
                    {Kind = kind, AliasKey = key, SyncKey = to.SyncKey, CreatedAtUtc = now});
                preImage?.KeyMoves.Add(new DataSyncKeyMove(kind, key, null, new DataSyncKeyOwner(to.Id, false)));
                moved.Add(key);
                continue;
            }

            var alias = await Db.DataSyncKeyAliases.SingleOrDefaultAsync(
                a => a.Kind == kind && a.AliasKey == key && a.SyncKey == from.SyncKey, ct);
            if (alias is null) continue;
            alias.SyncKey = to.SyncKey;
            preImage?.KeyMoves.Add(new DataSyncKeyMove(kind, key, new DataSyncKeyOwner(from.Id, false),
                new DataSyncKeyOwner(to.Id, false)));
            moved.Add(key);
        }

        if (moved.Count > 0)
        {
            from.Seq = await store.NextSeqAsync(ct);
            from.UpdatedAtUtc = now;
            to.Seq = await store.NextSeqAsync(ct);
            to.UpdatedAtUtc = now;
        }

        await Db.SaveChangesAsync(ct);
        return moved;
    }

    /// <summary>
    /// KeepRecordLinked (§9.2, row M): the given keys stop being the entity's. Aliases are removed; the primary, if
    /// among them, is taken by a rekey. The keys are free afterwards: the caller records them in an
    /// <c>Excluded(DroppedIdentity)</c> base. The entity gets the next Seq. Returns the keys dropped.
    /// </summary>
    public async Task<IReadOnlyList<string>> DropKeysAsync(DataSyncEntityDbModel entity, IEnumerable<SyncKey> keys,
        DataSyncIdentityPreImage? preImage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entity);
        RequireLive(entity);
        await store.FlushAsync(ct);
        var kind = entity.Kind;
        var dropped = new List<string>();
        foreach (var key in keys.Select(k => k.Value).Distinct(StringComparer.Ordinal))
        {
            if (key == entity.SyncKey)
            {
                await RekeyAsync(entity, preImage, ct);
                dropped.Add(key);
                continue;
            }

            var alias = await Db.DataSyncKeyAliases.SingleOrDefaultAsync(
                a => a.Kind == kind && a.AliasKey == key && a.SyncKey == entity.SyncKey, ct);
            if (alias is null) continue;
            Db.DataSyncKeyAliases.Remove(alias);
            preImage?.KeyMoves.Add(new DataSyncKeyMove(kind, key, new DataSyncKeyOwner(entity.Id, false), null));
            dropped.Add(key);
        }

        if (dropped.Count > 0)
        {
            entity.Seq = await store.NextSeqAsync(ct);
            entity.UpdatedAtUtc = store.UtcNow;
        }

        await Db.SaveChangesAsync(ct);
        return dropped;
    }

    /// <summary>
    /// Undo of key moves (§8.11 "KeepWithEntity / rekey"): every key goes back to its pre-image owner, newest move
    /// first. Nothing is deleted: a primary a rekey minted stays on its entity as an alias (a peer may already know
    /// it), and bases keyed by it follow the entity's restored primary. A key that is now used elsewhere refuses
    /// the undo (<see cref="DataSyncIdentityRefusedException"/>). Every entity touched gets the next Seq.
    /// </summary>
    public async Task RestoreKeyOwnersAsync(DataSyncIdentityPreImage preImage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(preImage);
        await store.FlushAsync(ct);
        var touched = new Dictionary<int, DataSyncEntityDbModel>();
        var now = store.UtcNow;

        async Task<DataSyncEntityDbModel> EntityAsync(int id, string kind, string key)
        {
            if (touched.TryGetValue(id, out var cached)) return cached;
            var entity = await Db.DataSyncEntities.FindAsync([id], ct);
            if (entity is null || entity.Kind != kind || entity.DeletedAtUtc is not null)
                throw new DataSyncIdentityRefusedException(kind, key, "its owner is no longer a live entity");
            return touched[id] = entity;
        }

        foreach (var move in Enumerable.Reverse(preImage.KeyMoves))
        {
            var (kind, key) = (move.Kind, move.Key);
            switch (move)
            {
                case {Before: null, After.Primary: true}:
                    // A primary a rekey minted: kept, it becomes an alias when the old primary returns below.
                    break;
                case {After: {Primary: false} after}:
                {
                    var owner = await EntityAsync(after.EntityId, kind, key);
                    var alias = await Db.DataSyncKeyAliases.SingleOrDefaultAsync(
                        a => a.Kind == kind && a.AliasKey == key && a.SyncKey == owner.SyncKey, ct);
                    if (alias is null) throw new DataSyncIdentityRefusedException(kind, key, "it moved again since");
                    if (move.Before is {Primary: false} before)
                    {
                        alias.SyncKey = (await EntityAsync(before.EntityId, kind, key)).SyncKey;
                    }
                    else
                    {
                        Db.DataSyncKeyAliases.Remove(alias);
                        await Db.SaveChangesAsync(ct);
                        if (move.Before is {Primary: true} primaryBefore)
                            await RestorePrimaryAsync(await EntityAsync(primaryBefore.EntityId, kind, key), key, now, ct);
                    }

                    break;
                }
                case {Before: {Primary: false} before, After: null}:
                {
                    if ((await TakenKeysAsync(kind, [key], ct)).Count > 0)
                        throw new DataSyncIdentityRefusedException(kind, key, "the key is used again since");
                    var owner = await EntityAsync(before.EntityId, kind, key);
                    Db.DataSyncKeyAliases.Add(new DataSyncKeyAliasDbModel
                        {Kind = kind, AliasKey = key, SyncKey = owner.SyncKey, CreatedAtUtc = now});
                    break;
                }
                case {Before: {Primary: true} before, After: null}:
                    await RestorePrimaryAsync(await EntityAsync(before.EntityId, kind, key), key, now, ct);
                    break;
                default:
                    throw new InvalidOperationException($"An unknown key move of {kind}/{key}.");
            }

            await Db.SaveChangesAsync(ct);
        }

        foreach (var entity in touched.Values)
        {
            entity.Seq = await store.NextSeqAsync(ct);
            entity.UpdatedAtUtc = now;
        }

        await Db.SaveChangesAsync(ct);
    }

    private async Task RestorePrimaryAsync(DataSyncEntityDbModel entity, string key, DateTime now, CancellationToken ct)
    {
        var kind = entity.Kind;
        if (entity.SyncKey == key) return;
        if ((await TakenKeysAsync(kind, [key], ct)).Count > 0)
            throw new DataSyncIdentityRefusedException(kind, key, "the key is used again since");
        var current = entity.SyncKey;
        foreach (var alias in await Db.DataSyncKeyAliases.Where(a => a.Kind == kind && a.SyncKey == current)
                     .ToListAsync(ct))
        {
            alias.SyncKey = key;
        }

        foreach (var item in await Db.DataSyncInboxItems
                     .Where(i => i.ClosedAtUtc == null && i.Kind == kind && i.SyncKey == current).ToListAsync(ct))
        {
            item.SyncKey = key;
        }

        entity.SyncKey = key;
        Db.DataSyncKeyAliases.Add(new DataSyncKeyAliasDbModel
            {Kind = kind, AliasKey = current, SyncKey = key, CreatedAtUtc = now});
        await Db.SaveChangesAsync(ct);
        await store.RepointBasesAsync(kind, current, key, ct);
    }

    #endregion

    #region §5.3 Pre-flight (v3.1 B4)

    /// <summary>
    /// The identity pre-flight: evaluates every create, alias-add and delete of the batches in order, against the
    /// current tables plus the simulated effects of the earlier operations of the same apply, without writing. An
    /// item any of whose key operations would be refused is removed from every batch (outcome
    /// <c>ChangedDuringApply</c>, detail <see cref="RefusedDetail"/>), and nothing of it is written. The refusal is
    /// reachable (gate note 1): an item that revives a tombstone makes its keys live for every later item.
    /// </summary>
    public async Task<DataSyncIdentityCheck> CheckApplyAsync(IReadOnlyList<ApplyBatch> batches, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batches);
        await store.FlushAsync(ct);
        var initial = new Dictionary<string, KeySimulation>(StringComparer.Ordinal);
        foreach (var kind in batches.Select(b => b.Kind).Distinct(StringComparer.Ordinal))
            initial[kind] = await KeySimulation.LoadAsync(Db, kind, ct);

        var refused = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            // A refused item leaves no trace, including the effects of its earlier operations: simulate again
            // without it until nothing new is refused (one extra round per wave of refusals).
            var simulations = initial.ToDictionary(e => e.Key, e => e.Value.Clone(), StringComparer.Ordinal);
            var newlyRefused = new HashSet<string>(StringComparer.Ordinal);
            foreach (var batch in batches)
            {
                var simulation = simulations[batch.Kind];
                foreach (var operation in batch.Operations)
                {
                    if (refused.Contains(operation.ItemId) || newlyRefused.Contains(operation.ItemId)) continue;
                    var mark = simulation.Mark;
                    if (!simulation.TryApply(operation))
                    {
                        simulation.RollbackTo(mark);
                        newlyRefused.Add(operation.ItemId);
                    }
                }
            }

            if (newlyRefused.Count == 0) break;
            refused.UnionWith(newlyRefused);
        }

        var filtered = batches
            .Select(b => b with {Operations = b.Operations.Where(o => !refused.Contains(o.ItemId)).ToList()})
            .ToList();
        return new DataSyncIdentityCheck(filtered, refused);
    }

    /// <summary>
    /// Who holds each key of one kind, as the pre-flight simulates it. Every change is journaled, so an operation
    /// that is refused half-way is rolled back in O(its own changes).
    /// </summary>
    private sealed class KeySimulation
    {
        // An owner is a token: existing rows are "e:{primary}", rows the apply creates "n:{itemId}".
        private readonly Dictionary<string, string> _owner;                 // key → owner
        private readonly Dictionary<string, HashSet<string>> _keysOf;      // owner → keys
        private readonly Dictionary<string, bool> _live;                    // owner → live
        private readonly Dictionary<string, string> _byLocalKey;            // live LocalKey → owner
        private readonly HashSet<string> _primaries;                        // keys that are some row's primary
        private readonly List<Action> _journal = [];

        private KeySimulation(Dictionary<string, string> owner, Dictionary<string, HashSet<string>> keysOf,
            Dictionary<string, bool> live, Dictionary<string, string> byLocalKey, HashSet<string> primaries)
        {
            _owner = owner;
            _keysOf = keysOf;
            _live = live;
            _byLocalKey = byLocalKey;
            _primaries = primaries;
        }

        public static async Task<KeySimulation> LoadAsync(BakabaseDbContext db, string kind, CancellationToken ct)
        {
            var simulation = new KeySimulation(new(StringComparer.Ordinal), new(StringComparer.Ordinal),
                new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal));
            foreach (var row in await db.DataSyncEntities.AsNoTracking().Where(e => e.Kind == kind)
                         .Select(e => new {e.SyncKey, e.LocalKey, Live = e.DeletedAtUtc == null}).ToListAsync(ct))
            {
                var token = "e:" + row.SyncKey;
                simulation._primaries.Add(row.SyncKey);
                simulation._live[token] = row.Live;
                simulation.KeysOf(token).Add(row.SyncKey);
                simulation._owner[row.SyncKey] = token;
                if (row.Live) simulation._byLocalKey[row.LocalKey] = token;
            }

            foreach (var alias in await db.DataSyncKeyAliases.AsNoTracking().Where(a => a.Kind == kind)
                         .Select(a => new {a.AliasKey, a.SyncKey}).ToListAsync(ct))
            {
                var token = "e:" + alias.SyncKey;
                simulation._owner[alias.AliasKey] = token;
                simulation.KeysOf(token).Add(alias.AliasKey);
            }

            return simulation;
        }

        public KeySimulation Clone() => new(
            new Dictionary<string, string>(_owner, StringComparer.Ordinal),
            _keysOf.ToDictionary(e => e.Key, e => new HashSet<string>(e.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
            new Dictionary<string, bool>(_live, StringComparer.Ordinal),
            new Dictionary<string, string>(_byLocalKey, StringComparer.Ordinal),
            new HashSet<string>(_primaries, StringComparer.Ordinal));

        public int Mark => _journal.Count;

        public void RollbackTo(int mark)
        {
            for (var i = _journal.Count - 1; i >= mark; i--) _journal[i]();
            _journal.RemoveRange(mark, _journal.Count - mark);
        }

        public bool TryApply(ApplyOperation operation) =>
            operation switch
            {
                CreateEntityOperation create => TryCreate(create),
                UpdateEntityOperation update => TryAddAliases(update.LocalKey, update.AliasKeysToAdd),
                BindOnlyOperation bind => TryAddAliases(bind.LocalKey, bind.AliasKeysToAdd),
                DeleteEntityOperation delete => Delete(delete.LocalKey),
                _ => true,
            };

        private bool TryCreate(CreateEntityOperation create)
        {
            var keys = create.Keys.All.Select(k => k.Value).ToList();
            if (keys.Count == 0) return true;
            var primary = keys[0];
            string token;
            if (_owner.TryGetValue(primary, out var existing))
            {
                if (_live[existing]) return false;
                if (_primaries.Contains(primary))
                {
                    // Revive: the tombstone's row, with every key it has, is live again.
                    token = existing;
                }
                else
                {
                    // An alias of a tombstone: it moves to the new row as its primary.
                    token = "n:" + create.ItemId;
                    Move(primary, token);
                    AddPrimary(primary);
                }
            }
            else
            {
                token = "n:" + create.ItemId;
                Move(primary, token);
                AddPrimary(primary);
            }

            SetLive(token, true);
            return keys.Skip(1).All(alias => TryAddAlias(token, alias));
        }

        private bool TryAddAliases(string localKey, EntityKeys aliases)
        {
            if (aliases.All.Count == 0) return true;
            // An operation on an entity the pre-flight does not see live is not an identity question.
            if (!_byLocalKey.TryGetValue(localKey, out var token)) return true;
            return aliases.All.All(alias => TryAddAlias(token, alias.Value));
        }

        private bool TryAddAlias(string token, string key)
        {
            if (!_owner.TryGetValue(key, out var owner))
            {
                Move(key, token);
                return true;
            }

            if (owner == token) return true;
            if (_live[owner]) return false;
            if (_primaries.Contains(key))
            {
                // Retire: every key of the tombstone joins the live entity, and the tombstone is gone.
                foreach (var k in KeysOf(owner).ToList()) Move(k, token);
                RemovePrimary(key);
            }
            else
            {
                Move(key, token);
            }

            return true;
        }

        private bool Delete(string localKey)
        {
            if (!_byLocalKey.TryGetValue(localKey, out var token)) return true;
            _byLocalKey.Remove(localKey);
            _journal.Add(() => _byLocalKey[localKey] = token);
            SetLive(token, false);
            return true;
        }

        private HashSet<string> KeysOf(string token)
        {
            if (!_keysOf.TryGetValue(token, out var keys)) _keysOf[token] = keys = new(StringComparer.Ordinal);
            return keys;
        }

        private void Move(string key, string token)
        {
            var had = _owner.TryGetValue(key, out var previous);
            if (had) KeysOf(previous!).Remove(key);
            _owner[key] = token;
            KeysOf(token).Add(key);
            _journal.Add(() =>
            {
                KeysOf(token).Remove(key);
                if (had)
                {
                    _owner[key] = previous!;
                    KeysOf(previous!).Add(key);
                }
                else
                {
                    _owner.Remove(key);
                }
            });
        }

        private void SetLive(string token, bool live)
        {
            var had = _live.TryGetValue(token, out var previous);
            _live[token] = live;
            _journal.Add(() =>
            {
                if (had) _live[token] = previous;
                else _live.Remove(token);
            });
        }

        private void AddPrimary(string key)
        {
            if (_primaries.Add(key)) _journal.Add(() => _primaries.Remove(key));
        }

        private void RemovePrimary(string key)
        {
            if (_primaries.Remove(key)) _journal.Add(() => _primaries.Add(key));
        }
    }

    #endregion

    #region §5.2 Binding indexes

    /// <summary>The binding indexes of one kind, with the exclusion index of <paramref name="linkId"/> (§5.2).</summary>
    public async Task<DataSyncKeyIndex> GetKeyIndexAsync(string kind, int? linkId, CancellationToken ct)
    {
        await store.FlushAsync(ct);
        var rows = await Db.DataSyncEntities.AsNoTracking().Where(e => e.Kind == kind).OrderBy(e => e.Id)
            .Select(e => new {e.Id, e.SyncKey, e.LocalKey, e.DeletedAtUtc, e.State, e.TombstoneKind})
            .ToListAsync(ct);
        var aliases = (await Db.DataSyncKeyAliases.AsNoTracking().Where(a => a.Kind == kind)
                .Select(a => new {a.AliasKey, a.SyncKey}).ToListAsync(ct))
            .ToLookup(a => a.SyncKey, a => a.AliasKey, StringComparer.Ordinal);
        var entities = rows.Select(r => new DataSyncIndexedEntity(r.Id, r.LocalKey,
                DataSyncStoredJson.ToEntityKeys(r.SyncKey, aliases[r.SyncKey]), r.DeletedAtUtc == null, r.State,
                r.TombstoneKind))
            .ToList();

        var excluded = new List<string>();
        if (linkId is { } id)
        {
            foreach (var json in await Db.DataSyncPeerBases.AsNoTracking()
                         .Where(b => b.LinkId == id && b.Kind == kind && b.State == DataSyncBaseState.Excluded)
                         .Select(b => b.ExclusionKeysJson).ToListAsync(ct))
            {
                excluded.AddRange(DataSyncStoredJson.ReadStrings(json, "ExclusionKeysJson"));
            }
        }

        return new DataSyncKeyIndex(kind, entities, excluded);
    }

    #endregion

    #region Helpers

    private async Task<HashSet<string>> TakenKeysAsync(string kind, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        var list = keys.ToList();
        var taken = await Db.DataSyncEntities.Where(e => e.Kind == kind && list.Contains(e.SyncKey))
            .Select(e => e.SyncKey).ToListAsync(ct);
        taken.AddRange(await Db.DataSyncKeyAliases.Where(a => a.Kind == kind && list.Contains(a.AliasKey))
            .Select(a => a.AliasKey).ToListAsync(ct));
        return taken.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<DataSyncActorId> SelfActorAsync(CancellationToken ct)
    {
        var state = await store.LoadStateAsync(ct) ??
                    throw new InvalidOperationException("Data sync has no local state yet (§4.5).");
        return new DataSyncActorId(state.ActorId);
    }

    private static void RequireNewRow(DataSyncEntityDbModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Id != 0) throw new ArgumentException("A new entity row has no Id yet.", nameof(row));
        if (string.IsNullOrEmpty(row.Kind) || string.IsNullOrEmpty(row.LocalKey))
            throw new ArgumentException("A new entity row needs its kind and local key.", nameof(row));
        if (row.DeletedAtUtc is not null) throw new ArgumentException("A new entity row is live.", nameof(row));
        DataSyncVersionVector.ParseStored(row.VvJson);
    }

    private static void RequireLive(DataSyncEntityDbModel row)
    {
        if (row.DeletedAtUtc is not null)
            throw new InvalidOperationException($"Entity {row.Kind}/{row.SyncKey} is a tombstone.");
    }

    /// <summary>Vectors never go backwards (§2.8, invariant I7).</summary>
    private static void RequireAtLeast(string vvJson, string floorJson, string kind, string key, string what)
    {
        var relation = DataSyncVersionVector.ParseStored(vvJson).CompareTo(DataSyncVersionVector.ParseStored(floorJson));
        if (relation is not (DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates))
            throw new InvalidOperationException(
                $"The vector of {what} of {kind}/{key} ({vvJson}) does not include the row's ({floorJson}).");
    }

    /// <summary>The content columns a create or a revive takes from the caller's row (never the keys).</summary>
    private static void CopyContent(DataSyncEntityDbModel from, DataSyncEntityDbModel to)
    {
        to.LocalKey = from.LocalKey;
        to.Fingerprint = from.Fingerprint;
        to.LocalHash = from.LocalHash;
        to.RawHash = from.RawHash;
        to.SharedHash = from.SharedHash;
        to.VvJson = from.VvJson;
        to.LastActorId = from.LastActorId;
        to.LastEditorNodeId = from.LastEditorNodeId;
        to.LastEditorName = from.LastEditorName;
        to.OrderKey = from.OrderKey;
        to.State = from.State;
        to.OverlayJson = from.OverlayJson;
        to.UnknownJson = from.UnknownJson;
        to.ChildrenLocal = from.ChildrenLocal;
        to.CreatedBySync = from.CreatedBySync;
        to.PublishHeld = from.PublishHeld;
        to.Unreadable = from.Unreadable;
    }

    #endregion
}

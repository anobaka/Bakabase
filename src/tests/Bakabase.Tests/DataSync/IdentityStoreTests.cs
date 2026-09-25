using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.DataSyncStoreFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// Identity (spec §5; v3.1 §5.3 and its IdentityStoreTests row): key creation, revive, retire with alias and base
/// re-pointing, refusals, rekey and key moves, the ID-reuse fingerprint, binding, and the pre-flight — including
/// both reachable pre-flight cases of §5.3. After every sequence, each key is used once per kind.
/// </summary>
[TestClass]
public class IdentityStoreTests
{
    private DataSyncStoreFixture _f = null!;

    [TestInitialize]
    public async Task Setup() => _f = await DataSyncStoreFixture.CreateAsync();

    #region Creating keys (§5.1)

    [TestMethod]
    public async Task A_fresh_definition_gets_a_random_key_and_the_next_seq()
    {
        var rows = await _f.Identity.InsertFreshRangeAsync([_f.Row("1"), _f.Row("2"), _f.Row("3")], default);

        Assert.AreEqual(3, rows.Select(r => r.SyncKey).Distinct().Count());
        Assert.IsTrue(rows.All(r => SyncKey.IsValid(r.SyncKey)));
        CollectionAssert.AreEqual(new long[] {1, 2, 3}, rows.Select(r => r.Seq).ToArray());
        Assert.AreEqual(3, (await _f.Store.GetLocalStateAsync(default))!.LastSeq);
        await _f.AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task A_create_from_a_peer_record_takes_its_keys()
    {
        var (primary, alias) = (NewKey(), NewKey());
        var row = await _f.Identity.CreateAsync(_f.Row("7"), Keys(primary, alias), null, default);

        Assert.AreEqual(primary, row.SyncKey);
        CollectionAssert.AreEqual(new[] {primary, alias}, (await _f.KeysOfAsync(row)).ToArray());
        await _f.AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task A_separate_create_gets_a_fresh_key()
    {
        var row = await _f.Identity.CreateAsync(_f.Row("7"), EntityKeys.None, null, default);
        Assert.IsTrue(SyncKey.IsValid(row.SyncKey));
    }

    [TestMethod]
    public async Task A_create_whose_key_is_live_here_is_refused()
    {
        var live = await _f.LiveWithAliasesAsync("1", NewKey());
        var alias = (await _f.KeysOfAsync(live))[1];

        await Assert.ThrowsExceptionAsync<DataSyncIdentityRefusedException>(() =>
            _f.Identity.CreateAsync(_f.Row("2"), Keys(live.SyncKey), null, default));
        await Assert.ThrowsExceptionAsync<DataSyncIdentityRefusedException>(() =>
            _f.Identity.CreateAsync(_f.Row("3"), Keys(alias), null, default));
    }

    #endregion

    #region Revive (v3.1 M-a, §5.3)

    [TestMethod]
    public async Task A_create_with_a_tombstoned_primary_revives_that_row()
    {
        var old = await _f.LiveWithAliasesAsync("9", NewKey());
        var key = old.SyncKey;
        await _f.TombstoneAsync(old);
        var tombstoneVv = DataSyncVersionVector.ParseStored(old.VvJson);
        var seqBefore = old.Seq;

        var preImage = new DataSyncIdentityPreImage();
        var reviveVv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Revive, DataSyncVersionVector.Empty,
            remote: Vv((ActorB, 4)), resultEqualsRemote: true, resultEqualsLocal: false, new DataSyncActorId(ActorA),
            () => 7, tombstone: tombstoneVv);
        var template = _f.Row("57", reviveVv, fingerprint: "638000");
        var revived = await _f.Identity.CreateAsync(template, Keys(key), preImage, default);

        Assert.AreEqual(old.Id, revived.Id, "the lineage continues in the same row");
        Assert.AreEqual(key, revived.SyncKey);
        Assert.IsNull(revived.DeletedAtUtc);
        Assert.AreEqual("57", revived.LocalKey);
        Assert.AreEqual("638000", revived.Fingerprint);
        Assert.AreEqual(template.LocalHash, revived.LocalHash);
        Assert.IsTrue(revived.Seq > seqBefore);
        Assert.AreEqual(1, preImage.RevivedTombstones.Count);
        Assert.AreEqual("9", preImage.RevivedTombstones[0].LocalKey);
        Assert.IsNotNull(preImage.RevivedTombstones[0].DeletedAtUtc);
        Assert.AreEqual(2, (await _f.KeysOfAsync(revived)).Count, "the tombstone's alias is live again with it");
        await _f.AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task A_revive_is_at_least_the_tombstone()
    {
        var old = await _f.LiveAsync("9", Vv((ActorA, 3)));
        await _f.TombstoneAsync(old, Vv((ActorA, 4)));

        // A vector below the tombstone's would go backwards.
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            _f.Identity.CreateAsync(_f.Row("10", Vv((ActorB, 9))), Keys(old.SyncKey), null, default));

        var revive = DataSyncRevisionRules.Next(DataSyncRevisionKind.Revive, DataSyncVersionVector.Empty,
            Vv((ActorB, 9)), true, false, new DataSyncActorId(ActorA), () => 5, tombstone: Vv((ActorA, 4)));
        var revived = await _f.Identity.CreateAsync(_f.Row("10", revive), Keys(old.SyncKey), null, default);
        Assert.AreEqual(DataSyncVvRelation.Dominates,
            DataSyncVersionVector.ParseStored(revived.VvJson).CompareTo(Vv((ActorA, 4))));
    }

    [TestMethod]
    public async Task A_create_over_an_alias_of_a_tombstone_deletes_that_alias()
    {
        var (a, b) = (NewKey(), NewKey());
        var tombstone = await _f.LiveWithAliasesAsync("9", a, b);
        await _f.TombstoneAsync(tombstone);
        var preImage = new DataSyncIdentityPreImage();

        var created = await _f.Identity.CreateAsync(_f.Row("11"), Keys(a), preImage, default);

        Assert.AreNotEqual(tombstone.Id, created.Id);
        Assert.AreEqual(a, created.SyncKey);
        var aliases = await _f.AliasesAsync();
        Assert.IsFalse(aliases.Any(x => x.AliasKey == a));
        Assert.AreEqual(tombstone.SyncKey, aliases.Single(x => x.AliasKey == b).SyncKey,
            "the tombstone keeps its other keys");
        Assert.AreEqual(a, preImage.RemovedAliases.Single().AliasKey);
        await _f.AssertKeyInvariantAsync();
    }

    #endregion

    #region Aliases, retire (§5.3, N5)

    [TestMethod]
    public async Task Adding_an_alias_bumps_seq_and_is_idempotent()
    {
        var live = await _f.LiveAsync("1");
        var key = NewKey();
        var seq = live.Seq;

        Assert.IsTrue(await _f.Identity.AddAliasesAsync(live, [Key(key)], null, default));
        Assert.IsTrue(live.Seq > seq, "an alias added changes the published record (§6.2)");
        var seqAfter = live.Seq;
        Assert.IsFalse(await _f.Identity.AddAliasesAsync(live, [Key(key), Key(live.SyncKey)], null, default));
        Assert.AreEqual(seqAfter, live.Seq);
    }

    [TestMethod]
    public async Task An_alias_of_another_live_entity_is_refused_whatever_its_state()
    {
        var target = await _f.LiveAsync("1");
        var synced = await _f.LiveWithAliasesAsync("2", NewKey());
        var localOnly = await _f.LiveAsync("3", state: DataSyncEntitySyncState.LocalOnly);
        var detached = await _f.LiveAsync("4", state: DataSyncEntitySyncState.Detached);
        var syncedAlias = (await _f.KeysOfAsync(synced))[1];

        foreach (var key in new[] {synced.SyncKey, syncedAlias, localOnly.SyncKey, detached.SyncKey})
        {
            await Assert.ThrowsExceptionAsync<DataSyncIdentityRefusedException>(() =>
                _f.Identity.AddAliasesAsync(target, [Key(key)], null, default), key);
        }

        Assert.AreEqual(1, (await _f.KeysOfAsync(target)).Count);
        await _f.AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task Adding_a_tombstoned_primary_retires_it_and_repoints_its_aliases_and_bases()
    {
        var link1 = await _f.LinkAsync("peer-1");
        var link2 = await _f.LinkAsync("peer-2");
        var tAlias = NewKey();
        var t = await _f.LiveWithAliasesAsync("9", tAlias);
        await _f.TombstoneAsync(t, Vv((ActorA, 1), (ActorC, 5)));
        var l = await _f.LiveAsync("1", Vv((ActorA, 2), (ActorB, 1)));
        var tKey = t.SyncKey;

        // Link 1 has a base only for T; link 2 has one for both, and L's wins.
        await _f.Store.UpsertBasesAsync(link1.Id, [BaseUpdate(tKey, "t1")], default);
        await _f.Store.UpsertBasesAsync(link2.Id, [BaseUpdate(tKey, "t2"), BaseUpdate(l.SyncKey, "l2")], default);
        var seq = l.Seq;
        var preImage = new DataSyncIdentityPreImage();

        await _f.Identity.AddAliasesAsync(l, [Key(tKey)], preImage, default);

        Assert.IsNull(await _f.ByPrimaryAsync(tKey), "the retired row is gone");
        CollectionAssert.AreEquivalent(new[] {l.SyncKey, tKey, tAlias}, (await _f.KeysOfAsync(l)).ToArray());
        Assert.AreEqual(Vv((ActorA, 2), (ActorB, 1), (ActorC, 5)), DataSyncVersionVector.ParseStored(l.VvJson),
            "Retire takes Max(L, T) with no counter");
        Assert.IsTrue(l.Seq > seq);

        var bases1 = await _f.Store.GetBasesAsync(link1.Id, Kind, default);
        Assert.AreEqual(l.SyncKey, bases1.Single().Key.Value, "a link without a base for L takes T's");
        Assert.AreEqual("t1", bases1.Single().Content!["name"]!.GetValue<string>());
        var bases2 = await _f.Store.GetBasesAsync(link2.Id, Kind, default);
        Assert.AreEqual("l2", bases2.Single().Content!["name"]!.GetValue<string>(), "L's base wins");

        Assert.AreEqual(tKey, preImage.RetiredIdentities.Single().SyncKey);
        Assert.AreEqual(tAlias, preImage.RepointedAliases.Single().AliasKey);
        await _f.AssertKeyInvariantAsync();

        // A record carrying only T's old alias binds to L with no question (N5).
        var index = await _f.Identity.GetKeyIndexAsync(Kind, null, default);
        Assert.AreEqual(l.Id, index.Bind([tAlias]).Bound!.Id);
    }

    [TestMethod]
    public async Task Adding_an_alias_of_a_tombstone_moves_just_that_alias()
    {
        var (a, b) = (NewKey(), NewKey());
        var t = await _f.LiveWithAliasesAsync("9", a, b);
        await _f.TombstoneAsync(t);
        var l = await _f.LiveAsync("1");

        await _f.Identity.AddAliasesAsync(l, [Key(a)], null, default);

        Assert.IsNotNull(await _f.ByPrimaryAsync(t.SyncKey), "the tombstone stays");
        var aliases = await _f.AliasesAsync();
        Assert.AreEqual(l.SyncKey, aliases.Single(x => x.AliasKey == a).SyncKey);
        Assert.AreEqual(t.SyncKey, aliases.Single(x => x.AliasKey == b).SyncKey);
        await _f.AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task Tombstoned_keys_include_the_aliases_of_tombstones()
    {
        var alias = NewKey();
        var t = await _f.LiveWithAliasesAsync("9", alias);
        await _f.TombstoneAsync(t);
        var live = await _f.LiveAsync("1");

        var index = await _f.Identity.GetKeyIndexAsync(Kind, null, default);

        CollectionAssert.AreEquivalent(new[] {Key(t.SyncKey), Key(alias)}, index.TombstonedKeys.ToArray());
        Assert.IsFalse(index.TombstonedKeys.Contains(Key(live.SyncKey)));
    }

    [TestMethod]
    public async Task A_tombstone_keeps_its_aliases_bumps_seq_and_closes_the_entity_items()
    {
        var link = await _f.LinkAsync("peer-1");
        var alias = NewKey();
        var row = await _f.LiveWithAliasesAsync("1", alias);
        await _f.Store.UpsertItemsAsync(link.Id, "peer-1",
            [Draft(Kind, alias, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name")],
            DateTime.UtcNow, default);
        var seq = row.Seq;

        await _f.Identity.TombstoneAsync(row,
            new DataSyncTombstoneWrite(Vv((ActorA, 2)), DataSyncTombstoneKind.Deleted, true, null), default);

        Assert.IsNotNull(row.DeletedAtUtc);
        Assert.IsTrue(row.Seq > seq);
        Assert.IsTrue(row.TombstoneServed);
        Assert.AreEqual(alias, (await _f.AliasesAsync()).Single().AliasKey);
        Assert.AreEqual(DataSyncInboxClosure.Superseded, (await _f.ItemsAsync()).Single().Closure);

        // A tombstone never goes backwards.
        var other = await _f.LiveAsync("2", Vv((ActorA, 5)));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _f.Identity.TombstoneAsync(other,
            new DataSyncTombstoneWrite(Vv((ActorA, 4)), DataSyncTombstoneKind.Deleted, true, null), default));
    }

    #endregion

    #region Rekey and key moves (§5.3, §9.2)

    [TestMethod]
    public async Task Rekey_gives_a_fresh_primary_and_deletes_the_bases_and_pending_records_on_every_link()
    {
        var link1 = await _f.LinkAsync("peer-1");
        var link2 = await _f.LinkAsync("peer-2");
        var alias = NewKey();
        var b = await _f.LiveWithAliasesAsync("2", alias);
        var old = b.SyncKey;
        await _f.Store.UpsertBasesAsync(link1.Id, [BaseUpdate(old, "b1")], default);
        await _f.Store.UpsertBasesAsync(link2.Id,
            [BaseUpdate(old, "b2") with {Pending = Pending(Record([old], Vv((ActorB, 3))), DataSyncPendingReason.Conflict)}],
            default);
        await _f.Store.UpsertItemsAsync(link2.Id, "peer-2",
        [
            Draft(Kind, old, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name"),
            Draft(Kind, old, DataSyncInboxItemType.ChildDeletedInUse, DataSyncInboxItemOrigin.State, "choice:x"),
        ], DateTime.UtcNow, default);
        var seq = b.Seq;
        var preImage = new DataSyncIdentityPreImage();

        var returned = await _f.Identity.RekeyAsync(b, preImage, default);

        Assert.AreEqual(old, returned);
        Assert.AreNotEqual(old, b.SyncKey);
        Assert.IsTrue(b.Seq > seq);
        CollectionAssert.AreEquivalent(new[] {b.SyncKey, alias}, (await _f.KeysOfAsync(b)).ToArray(),
            "its aliases follow; the old primary is free");
        Assert.AreEqual(0, (await _f.Store.GetBasesAsync(link1.Id, Kind, default)).Count);
        Assert.AreEqual(0, (await _f.Store.GetBasesAsync(link2.Id, Kind, default)).Count);
        var items = await _f.ItemsAsync();
        Assert.AreEqual(DataSyncInboxClosure.Superseded,
            items.Single(i => i.Type == DataSyncInboxItemType.FieldConflict).Closure);
        var held = items.Single(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse);
        Assert.IsNull(held.ClosedAtUtc);
        Assert.AreEqual(b.SyncKey, held.SyncKey, "a state-derived item follows the new key");
        Assert.AreEqual(2, preImage.KeyMoves.Count);
        await _f.AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task KeepWithEntity_moves_the_record_keys_to_the_chosen_entity()
    {
        var link = await _f.LinkAsync("peer-1");
        var bAlias = NewKey();
        var bOther = NewKey();
        var a = await _f.LiveAsync("1");
        var b = await _f.LiveWithAliasesAsync("2", bAlias, bOther);
        var bPrimary = b.SyncKey;
        await _f.Store.UpsertBasesAsync(link.Id, [BaseUpdate(bPrimary, "b")], default);
        var (aSeq, bSeq) = (a.Seq, b.Seq);
        var preImage = new DataSyncIdentityPreImage();

        // The record's keys are B's primary and one of its aliases.
        var moved = await _f.Identity.MoveKeysAsync(b, a, [Key(bPrimary), Key(bAlias)], preImage, default);

        CollectionAssert.AreEquivalent(new[] {bPrimary, bAlias}, moved.ToArray());
        CollectionAssert.AreEquivalent(new[] {a.SyncKey, bPrimary, bAlias}, (await _f.KeysOfAsync(a)).ToArray());
        CollectionAssert.AreEquivalent(new[] {b.SyncKey, bOther}, (await _f.KeysOfAsync(b)).ToArray());
        Assert.AreNotEqual(bPrimary, b.SyncKey, "B was re-keyed");
        Assert.AreEqual(0, (await _f.Store.GetBasesAsync(link.Id, Kind, default)).Count, "B's bases are deleted");
        Assert.IsTrue(a.Seq > aSeq && b.Seq > bSeq);
        await _f.AssertKeyInvariantAsync();

        // Undo gives every key back to its owner and deletes nothing: B's minted primary stays as its alias.
        var minted = b.SyncKey;
        await _f.Identity.RestoreKeyOwnersAsync(preImage, default);
        await _f.ReloadAsync(a);
        await _f.ReloadAsync(b);
        Assert.AreEqual(bPrimary, b.SyncKey);
        CollectionAssert.AreEquivalent(new[] {bPrimary, bAlias, bOther, minted}, (await _f.KeysOfAsync(b)).ToArray());
        CollectionAssert.AreEquivalent(new[] {a.SyncKey}, (await _f.KeysOfAsync(a)).ToArray());
        await _f.AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task KeepRecordLinked_drops_the_other_record_keys()
    {
        var qAlias = NewKey();
        var keep = NewKey();
        var entity = await _f.LiveWithAliasesAsync("1", qAlias, keep);
        var primary = entity.SyncKey;
        var preImage = new DataSyncIdentityPreImage();

        var dropped = await _f.Identity.DropKeysAsync(entity, [Key(primary), Key(qAlias)], preImage, default);

        CollectionAssert.AreEquivalent(new[] {primary, qAlias}, dropped.ToArray());
        CollectionAssert.AreEquivalent(new[] {entity.SyncKey, keep}, (await _f.KeysOfAsync(entity)).ToArray());
        var index = await _f.Identity.GetKeyIndexAsync(Kind, null, default);
        Assert.IsTrue(index.Bind([primary]).IsUnknown && index.Bind([qAlias]).IsUnknown, "the dropped keys are free");
        await _f.AssertKeyInvariantAsync();

        await _f.Identity.RestoreKeyOwnersAsync(preImage, default);
        await _f.ReloadAsync(entity);
        Assert.AreEqual(primary, entity.SyncKey);
        Assert.IsTrue((await _f.KeysOfAsync(entity)).Contains(qAlias));
        await _f.AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task Restoring_a_key_that_is_used_again_is_refused()
    {
        var alias = NewKey();
        var entity = await _f.LiveWithAliasesAsync("1", alias);
        var preImage = new DataSyncIdentityPreImage();
        await _f.Identity.DropKeysAsync(entity, [Key(alias)], preImage, default);
        await _f.Identity.CreateAsync(_f.Row("2"), Keys(alias), null, default);

        await Assert.ThrowsExceptionAsync<DataSyncIdentityRefusedException>(() =>
            _f.Identity.RestoreKeyOwnersAsync(preImage, default));
    }

    #endregion

    #region ID reuse (§5.5)

    [TestMethod]
    public void Only_two_known_different_fingerprints_mean_a_reused_id()
    {
        Assert.IsTrue(DataSyncIdentityStore.IsReusedId("1", "2"));
        Assert.IsFalse(DataSyncIdentityStore.IsReusedId("1", "1"));
        Assert.IsFalse(DataSyncIdentityStore.IsReusedId(null, "2"));
        Assert.IsFalse(DataSyncIdentityStore.IsReusedId("1", null));
    }

    [TestMethod]
    public async Task A_reused_id_tombstones_the_old_identity_and_mints_a_new_one_for_the_same_local_key()
    {
        var old = await _f.Identity.InsertFreshAsync(_f.Row("12", fingerprint: "100"), default);
        var oldKey = old.SyncKey;

        var fresh = await _f.Identity.ReplaceReusedIdAsync(old,
            new DataSyncTombstoneWrite(Vv((ActorA, 2)), DataSyncTombstoneKind.Deleted, true, null),
            _f.Row("12", fingerprint: "200"), default);

        Assert.AreNotEqual(oldKey, fresh.SyncKey);
        Assert.AreEqual("12", fresh.LocalKey);
        Assert.IsNotNull((await _f.ByPrimaryAsync(oldKey))!.DeletedAtUtc);
        Assert.IsTrue(fresh.Seq > old.Seq);
        await _f.AssertKeyInvariantAsync();
    }

    #endregion

    #region Binding (§5.2)

    [TestMethod]
    public async Task Binding_checks_exclusions_then_live_rows_then_tombstones()
    {
        var link = await _f.LinkAsync("peer-1");
        var synced = await _f.LiveWithAliasesAsync("1", NewKey());
        var syncedAlias = (await _f.KeysOfAsync(synced))[1];
        var other = await _f.LiveAsync("2");
        var detached = await _f.LiveAsync("3", state: DataSyncEntitySyncState.Detached);
        var tombstone = await _f.TombstoneAsync(await _f.LiveAsync("4"));
        var skipped = NewKey();
        await _f.Store.UpsertBasesAsync(link.Id,
        [
            new DataSyncBaseUpdate(Kind, Key(skipped), DataSyncBaseState.Excluded, DataSyncExclusionReason.Skipped,
                Record([skipped, synced.SyncKey], Vv((ActorB, 1))), null, null, true),
        ], default);

        var index = await _f.Identity.GetKeyIndexAsync(Kind, link.Id, default);

        Assert.IsTrue(index.Bind([synced.SyncKey]).Excluded, "an exclusion matches any record key first (row E)");
        Assert.AreEqual(synced.Id, index.Bind([syncedAlias]).Bound!.Id);
        Assert.IsTrue(index.Bind([syncedAlias, other.SyncKey]).IsIdentityConflict, "row I");
        Assert.IsTrue(index.Bind([detached.SyncKey]).IsNotSyncedHere, "row X");
        Assert.AreEqual(tombstone.Id, index.Bind([tombstone.SyncKey]).Tombstone!.Id);
        Assert.IsTrue(index.Bind([NewKey()]).IsUnknown);

        // Without the link, nothing is excluded.
        var unlinked = await _f.Identity.GetKeyIndexAsync(Kind, null, default);
        Assert.AreEqual(synced.Id, unlinked.Bind([synced.SyncKey]).Bound!.Id);
    }

    #endregion

    #region Pre-flight (v3.1 B4, §5.3)

    [TestMethod]
    public async Task The_preflight_refuses_an_item_whose_alias_is_live_elsewhere_and_writes_nothing()
    {
        var a = await _f.LiveAsync("1");
        var b = await _f.LiveAsync("2");
        var aliasesBefore = (await _f.AliasesAsync()).Count;
        var batches = new[]
        {
            new ApplyBatch(Kind,
            [
                new BindOnlyOperation("ok", a.LocalKey, Keys(NewKey())),
                new BindOnlyOperation("bad", a.LocalKey, Keys(b.SyncKey)),
            ]),
        };

        var check = await _f.Identity.CheckApplyAsync(batches, default);

        CollectionAssert.AreEquivalent(new[] {"bad"}, check.RefusedItemIds.ToArray());
        Assert.AreEqual("ok", check.Batches.Single().Operations.Single().ItemId);
        Assert.AreEqual(aliasesBefore, (await _f.AliasesAsync()).Count, "the pre-flight writes nothing");
        Assert.AreEqual("identityConflict", DataSyncIdentityStore.RefusedDetail);
    }

    [TestMethod]
    public async Task The_preflight_is_reachable_when_an_earlier_item_revives_a_tombstone_whose_primary_a_later_item_adds()
    {
        var t = await _f.TombstoneAsync(await _f.LiveAsync("9"));
        var l2 = await _f.LiveAsync("2");

        // Alone, adding T's primary to L2 retires T: allowed.
        var alone = await _f.Identity.CheckApplyAsync(
            [new ApplyBatch(Kind, [new BindOnlyOperation("y", l2.LocalKey, Keys(t.SyncKey))])], default);
        Assert.AreEqual(0, alone.RefusedItemIds.Count);

        // After X revives T, the key is live and Y is refused.
        var check = await _f.Identity.CheckApplyAsync(
        [
            new ApplyBatch(Kind,
            [
                new CreateEntityOperation("x", Keys(t.SyncKey), "origin", 0, new JsonObject()),
                new BindOnlyOperation("y", l2.LocalKey, Keys(t.SyncKey)),
            ]),
        ], default);
        CollectionAssert.AreEquivalent(new[] {"y"}, check.RefusedItemIds.ToArray());
    }

    [TestMethod]
    public async Task The_preflight_is_reachable_when_an_earlier_item_revives_a_tombstone_whose_alias_a_later_item_adds()
    {
        var tAlias = NewKey();
        var t = await _f.TombstoneAsync(await _f.LiveWithAliasesAsync("9", tAlias));
        var l2 = await _f.LiveAsync("2");

        var alone = await _f.Identity.CheckApplyAsync(
            [new ApplyBatch(Kind, [new UpdateEntityOperation("y", l2.LocalKey, "h", new JsonObject(), Keys(tAlias), [], [])])],
            default);
        Assert.AreEqual(0, alone.RefusedItemIds.Count);

        var check = await _f.Identity.CheckApplyAsync(
        [
            new ApplyBatch(Kind,
            [
                new CreateEntityOperation("x", Keys(t.SyncKey), "origin", 0, new JsonObject()),
                new UpdateEntityOperation("y", l2.LocalKey, "h", new JsonObject(), Keys(tAlias), [], []),
            ]),
        ], default);
        CollectionAssert.AreEquivalent(new[] {"y"}, check.RefusedItemIds.ToArray());
    }

    [TestMethod]
    public async Task A_refused_item_leaves_no_trace_in_the_simulation()
    {
        var a = await _f.LiveAsync("1");
        var b = await _f.LiveAsync("2");
        var shared = NewKey();

        // Z first binds a fresh key to A, then is refused on B's key; W binding the same fresh key to B must then
        // see it free, because nothing of Z happens.
        var check = await _f.Identity.CheckApplyAsync(
        [
            new ApplyBatch(Kind,
            [
                new BindOnlyOperation("z", a.LocalKey, Keys(shared, b.SyncKey)),
                new BindOnlyOperation("w", b.LocalKey, Keys(shared)),
            ]),
        ], default);

        CollectionAssert.AreEquivalent(new[] {"z"}, check.RefusedItemIds.ToArray());
    }

    [TestMethod]
    public async Task An_alias_add_after_a_delete_in_the_same_apply_retires_the_deleted_entity()
    {
        var gone = await _f.LiveAsync("1");
        var l = await _f.LiveAsync("2");

        var check = await _f.Identity.CheckApplyAsync(
        [
            new ApplyBatch(Kind,
            [
                new DeleteEntityOperation("d", gone.LocalKey, "h"),
                new BindOnlyOperation("b", l.LocalKey, Keys(gone.SyncKey)),
            ]),
        ], default);

        Assert.AreEqual(0, check.RefusedItemIds.Count);
    }

    #endregion

    private static DataSyncBaseUpdate BaseUpdate(string key, string name) =>
        new(Kind, Key(key), DataSyncBaseState.Normal, null,
            Record([key], Vv((ActorB, 1)), content: new JsonObject {["name"] = name}), null, null, false);
}

using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.TestKit.DataSync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// Refresh (spec §6.1; v3.1 §4.4): lazy rows, revisions only on comparison-form changes, the raw-hash fast path,
/// order moves, ComparisonFormVersion recomputes, tombstones, NewDefinitionsStayLocal, what a snapshot collects, and
/// the actor: skipped while unverified, asserting (never rotating), the watermark after commit.
/// </summary>
[TestClass]
public class RefreshTests
{
    #region Rows, revisions and the fast path

    [TestMethod]
    public async Task The_first_Refresh_creates_the_local_state_and_one_revision_per_definition()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        f.Kind.Add("2", "Mood");

        var result = await f.RefreshAsync();

        Assert.IsFalse(result.Skipped);
        Assert.AreEqual(2, result.Changed);
        var state = await f.StateAsync();
        Assert.AreEqual(f.Identity.Device.NodeId, state.NodeId);
        Assert.AreEqual(1, state.ActorGeneration);
        Assert.AreEqual(state.ActorId, result.Actor.Value);
        Assert.AreEqual(2, state.ActorCounter);
        Assert.AreEqual(2, state.LastSeq, "Seq comes from LastSeq only (§6.2)");
        var rows = await f.RowsAsync();
        CollectionAssert.AreEqual(new[] {"1", "2"}, rows.Select(r => r.LocalKey).ToArray());
        CollectionAssert.AreEquivalent(new long[] {1, 2}, rows.Select(r => r.Seq).ToArray());
        foreach (var row in rows)
        {
            Assert.IsTrue(SyncKey.IsValid(row.SyncKey));
            Assert.AreEqual(DataSyncEntitySyncState.Synced, row.State);
            Assert.AreEqual(state.NodeId, row.OriginNodeId);
            Assert.AreEqual(state.ActorId, row.LastActorId);
            Assert.AreEqual(f.Identity.Device.Name, row.LastEditorName);
            Assert.AreEqual(ContentHash.Of(f.Kind.Definitions[row.LocalKey].ToContent()), row.LocalHash);
            Assert.IsNotNull(row.RawHash);
            Assert.IsTrue(ContentHash.IsValid(row.SharedHash));
            Assert.AreEqual(1, DataSyncRefreshFixture.Vv(row.VvJson).Counters.Count);
            Assert.AreEqual(state.ActorId, DataSyncRefreshFixture.Vv(row.VvJson).Counters.Keys.Single());
        }

        Assert.AreEqual(DataSyncActorWatermark.Of(state), f.Watermark.Read().Watermark, "actor.json after the commit");
        Assert.AreEqual(1, DataSyncStoredJson.ReadVersions(state.ComparisonFormVersionsJson, "")[f.KindId]);
    }

    [TestMethod]
    public async Task Nothing_changed_reads_no_content_and_issues_nothing()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        f.Kind.Add("2", "Mood");
        await f.RefreshAsync();
        var before = await f.RowsAsync();
        f.Kind.Reads.Clear();

        var result = await f.RefreshAsync();

        Assert.AreEqual(0, result.Changed);
        Assert.AreEqual(0, f.Kind.Reads.Count, "the raw-hash fast path skips unchanged rows (§6.1)");
        CollectionAssert.AreEqual(before.Select(r => (r.Seq, r.VvJson, r.SharedHash)).ToList(),
            (await f.RowsAsync()).Select(r => (r.Seq, r.VvJson, r.SharedHash)).ToList());
        Assert.AreEqual(2, (await f.StateAsync()).LastSeq);
    }

    [TestMethod]
    public async Task Only_rows_whose_raw_hash_moved_are_read_and_a_form_change_is_a_revision()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        f.Kind.Add("2", "Mood");
        await f.RefreshAsync();
        var before = await f.RowAsync("1");
        var untouched = await f.RowAsync("2");
        f.Kind.Reads.Clear();

        f.Kind.Definitions["1"].Name = "Genres";
        var result = await f.RefreshAsync();

        Assert.AreEqual(1, f.Kind.Reads.Count);
        CollectionAssert.AreEqual(new[] {"1"}, f.Kind.Reads[0]!.ToArray());
        Assert.AreEqual(1, result.Changed);
        var after = await f.RowAsync("1");
        Assert.AreEqual(3, after.Seq);
        Assert.AreNotEqual(before.SharedHash, after.SharedHash);
        var state = await f.StateAsync();
        Assert.AreEqual(3, state.ActorCounter);
        Assert.AreEqual(3, DataSyncRefreshFixture.Vv(after.VvJson)[new DataSyncActorId(state.ActorId)],
            "LocalEdit: local.With(self, next)");
        Assert.AreEqual(untouched.Seq, (await f.RowAsync("2")).Seq);
    }

    [TestMethod]
    public async Task A_local_only_difference_is_no_revision()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"), ("b", "Drama"));
        await f.RefreshAsync();
        var before = await f.RowAsync("1");

        // Child ids and the local order of children are not in the comparison form (§3.4).
        f.Kind.Definitions["1"].Children = [new MemoryChild("b2", "Drama"), new MemoryChild("a", "Action")];
        var result = await f.RefreshAsync();

        Assert.AreEqual(0, result.Changed);
        var after = await f.RowAsync("1");
        Assert.AreNotEqual(before.LocalHash, after.LocalHash, "the local hash follows the local content");
        Assert.AreNotEqual(before.RawHash, after.RawHash);
        Assert.AreEqual(before.SharedHash, after.SharedHash);
        Assert.AreEqual(before.Seq, after.Seq, "no Seq for a local-only difference");
        Assert.AreEqual(before.VvJson, after.VvJson);
    }

    [TestMethod]
    public async Task Overlay_and_childrenLocal_changes_mark_the_row_and_become_revisions()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"), ("b", "Drama"));
        await f.RefreshAsync();
        var initial = await f.RowAsync("1");

        await f.Store.SetOverlayAsync(f.KindId, "1", new DataSyncOverlay(["b"], []), default);
        Assert.AreEqual(1, (await f.RefreshAsync()).Changed, "a local-only child leaves what is published");
        var afterOverlay = await f.RowAsync("1");
        Assert.AreNotEqual(initial.SharedHash, afterOverlay.SharedHash);

        await f.Store.SetChildrenLocalAsync(f.KindId, "1", true, default);
        var result = await f.RefreshAsync(collectPublished: true);
        Assert.AreEqual(1, result.Changed, "childrenLocal is shared content (§3.6)");
        var published = result.Published![(f.KindId, "1")];
        Assert.IsTrue(published.Content!["childrenLocal"]!.GetValue<bool>());
        Assert.IsNull(published.Content["children"]);
    }

    [TestMethod]
    public async Task An_overlay_change_that_leaves_the_form_alone_is_served_again_without_a_revision()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"), ("a2", "Action"));
        await f.RefreshAsync();
        var before = await f.RowAsync("1");

        // The duplicate member of a label class goes local-only: what travels changes, the comparison form does not.
        await f.Store.SetOverlayAsync(f.KindId, "1", new DataSyncOverlay(["a2"], []), default);
        var result = await f.RefreshAsync(collectPublished: true);

        Assert.AreEqual(0, result.Changed);
        var after = await f.RowAsync("1");
        Assert.AreEqual((before.SharedHash, before.VvJson), (after.SharedHash, after.VvJson));
        Assert.IsTrue(after.Seq > before.Seq, "its published record changed (§6.2)");
        Assert.AreEqual(1, result.Published![(f.KindId, "1")].Content!["children"]!.AsArray().Count);

        await f.RefreshAsync();
        Assert.AreEqual(after.Seq, (await f.RowAsync("1")).Seq, "once");
    }

    [TestMethod]
    public async Task Deleting_a_definition_tombstones_it_and_only_a_synced_one_is_served()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        f.Kind.Add("2", "Mood");
        await f.RefreshAsync();
        await f.Store.SetEntityStateAsync(f.KindId, "2", DataSyncEntitySyncState.LocalOnly, default);
        var before = (await f.RowsAsync()).ToDictionary(r => r.LocalKey);

        f.Kind.Remove("1");
        f.Kind.Remove("2");
        var result = await f.RefreshAsync();

        Assert.AreEqual(2, result.Tombstoned);
        var rows = (await f.RowsAsync()).ToDictionary(r => r.LocalKey);
        Assert.AreEqual(2, rows.Count, "tombstones are kept");
        Assert.IsTrue(rows.Values.All(r => r.DeletedAtUtc != null && r.TombstoneKind == DataSyncTombstoneKind.Deleted));
        Assert.IsTrue(rows["1"].TombstoneServed);
        Assert.IsFalse(rows["2"].TombstoneServed, "deleting a local-only definition tells peers nothing (§6.3)");
        var actor = new DataSyncActorId((await f.StateAsync()).ActorId);
        foreach (var key in new[] {"1", "2"})
        {
            Assert.IsTrue(DataSyncRefreshFixture.Vv(rows[key].VvJson)[actor] >
                          DataSyncRefreshFixture.Vv(before[key].VvJson)[actor], "LocalDelete adds a counter");
            Assert.IsTrue(rows[key].Seq > before[key].Seq);
        }

        Assert.AreEqual((0, 1), await f.Store.CountPublishedAsync(f.KindId, default));
    }

    [TestMethod]
    public async Task A_reused_local_id_tombstones_the_old_identity_and_mints_a_new_one()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre").Fingerprint = "638000";
        await f.RefreshAsync();
        var old = await f.RowAsync("1");

        f.Kind.Definitions["1"].Fingerprint = "638999";
        f.Kind.Definitions["1"].Name = "Studio";
        var result = await f.RefreshAsync();

        Assert.AreEqual(1, result.Tombstoned);
        Assert.AreEqual(1, result.Changed);
        var rows = await f.RowsAsync();
        Assert.AreEqual(2, rows.Count);
        var tombstone = rows.Single(r => r.SyncKey == old.SyncKey);
        Assert.IsNotNull(tombstone.DeletedAtUtc);
        Assert.IsTrue(tombstone.TombstoneServed);
        var fresh = await f.RowAsync("1");
        Assert.AreNotEqual(old.SyncKey, fresh.SyncKey);
        Assert.AreEqual("638999", fresh.Fingerprint);
    }

    [TestMethod]
    public async Task New_definitions_stay_local_when_asked()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        await f.RefreshAsync();
        var state = (await f.Store.GetLocalStateAsync(default))!;
        state.NewDefinitionsStayLocal = true;
        await f.Store.SaveLocalStateAsync(state, default);

        f.Kind.Add("1", "Genre");
        var result = await f.RefreshAsync(collectPublished: true);

        Assert.AreEqual(DataSyncEntitySyncState.LocalOnly, (await f.RowAsync("1")).State);
        Assert.AreEqual(0, result.Published!.Count, "nothing is published before its owner chooses (§3.6)");
        Assert.AreEqual((0, 0), await f.Store.CountPublishedAsync(f.KindId, default));
    }

    [TestMethod]
    public async Task An_unreadable_row_is_recorded_without_a_bump_and_published_held()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();
        var before = await f.RowAsync("1");

        f.Kind.Definitions["1"].Unreadable = true;
        var result = await f.RefreshAsync(collectPublished: true);

        var after = await f.RowAsync("1");
        Assert.IsTrue(after.Unreadable);
        Assert.AreEqual(before.Seq, after.Seq, "recorded without bumping anything (§3.3)");
        Assert.AreEqual(before.VvJson, after.VvJson);
        Assert.AreEqual(DataSyncHeldReason.LocalUnreadable, result.Published![(f.KindId, "1")].Held);

        f.Kind.Definitions["1"].Unreadable = false;
        Assert.AreEqual(0, (await f.RefreshAsync()).Changed, "readable again with the content it had: no revision");
        Assert.IsFalse((await f.RowAsync("1")).Unreadable);
    }

    [TestMethod]
    public async Task A_snapshot_collects_exactly_what_Refresh_published()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"), ("b", "Drama"), ("c", ""));
        f.Kind.Add("2", "Mood");
        f.Kind.Add("3", "Local");
        await f.RefreshAsync();
        await f.Store.SetOverlayAsync(f.KindId, "1", new DataSyncOverlay(["b"], []), default);
        await f.Store.SetEntityStateAsync(f.KindId, "3", DataSyncEntitySyncState.LocalOnly, default);
        var row = await f.Db.DataSyncEntities.SingleAsync(e => e.LocalKey == "1");
        row.UnknownJson = "{\"x-future\":7}";
        await f.Db.SaveChangesAsync();
        f.Kind.Reads.Clear();

        var result = await f.RefreshAsync(collectPublished: true);

        Assert.AreEqual(1, f.Kind.Reads.Count, "one read of every entity, never a second one (§6.6)");
        CollectionAssert.AreEquivalent(new[] {(f.KindId, "1"), (f.KindId, "2")}, result.Published!.Keys.ToArray(),
            "only synced entities are published");
        var published = result.Published[(f.KindId, "1")];
        Assert.AreEqual(ContentHash.Of(published.Content), published.Hash);
        CollectionAssert.AreEqual(new[] {"a"},
            published.Content!["children"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ToArray());
        Assert.AreEqual(2, published.ChildrenWithheld, "the local-only child and the one without a label");
        Assert.AreEqual(7, published.Content["x-future"]!.GetValue<int>(), "preserved unknown members (§3.5 step 5)");
    }

    [TestMethod]
    public async Task An_entity_held_at_source_is_published_held_and_bumped_once()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();
        var before = await f.RowAsync("1");

        f.Kind.Definitions["1"].Extra["tooBig"] = true;
        var result = await f.RefreshAsync(collectPublished: true);

        var held = result.Published![(f.KindId, "1")];
        Assert.AreEqual(DataSyncHeldReason.Invalid, held.Held);
        Assert.AreEqual("tooManyChildren", held.HeldDetail);
        Assert.IsNull(held.Content);
        var after = await f.RowAsync("1");
        Assert.IsTrue(after.Seq > before.Seq, "readers receive HeldAtSource once");
        Assert.AreEqual(before.VvJson, after.VvJson, "held at source is not a revision");
        Assert.IsTrue(DataSyncEntityForms.IsHeldMarker(after.SharedHash));

        await f.RefreshAsync();
        Assert.AreEqual(after.Seq, (await f.RowAsync("1")).Seq);
    }

    #endregion

    #region Order

    [TestMethod]
    public async Task Order_moves_revise_only_the_entities_that_moved()
    {
        var f = await DataSyncRefreshFixture.CreateAsync(hasOrder: true);
        f.Detector.Answer = FakeOrderMoveDetector.KeyNewOnes;
        f.Kind.Add("1", "A");
        f.Kind.Add("2", "B");
        f.Kind.Add("3", "C");
        f.Kind.Add("4", "Here only");
        await f.RefreshAsync();
        CollectionAssert.AreEqual(new[] {"k1", "k2", "k3", "k4"},
            (await f.RowsAsync()).Select(r => r.OrderKey).ToArray(), "a local create gets its key from DetectMoves");
        Assert.IsTrue(f.Detector.Calls[0].All(e => e.OrderKey is null && e.TieKey is null));
        await f.Store.SetEntityStateAsync(f.KindId, "4", DataSyncEntitySyncState.LocalOnly, default);
        await f.RefreshAsync();
        var before = (await f.RowsAsync()).ToDictionary(r => r.LocalKey);
        f.Kind.Reads.Clear();

        // C moves between A and B.
        f.Kind.Order.Remove("3");
        f.Kind.Order.Insert(1, "3");
        f.Detector.Answer = _ => new Dictionary<string, string> {["3"] = "k15"};
        var result = await f.RefreshAsync();

        var input = f.Detector.Calls[^1];
        CollectionAssert.AreEqual(new[] {"1", "3", "2"}, input.Select(e => e.LocalKey).ToArray(),
            "synced, live, published entities in local order; a local-only one keeps its slot");
        CollectionAssert.AreEqual(new[] {"k1", "k3", "k2"}, input.Select(e => e.OrderKey).ToArray());
        CollectionAssert.AreEqual(new[] {before["1"].SyncKey, before["3"].SyncKey, before["2"].SyncKey},
            input.Select(e => e.TieKey).ToArray());
        Assert.AreEqual(1, result.Changed);
        CollectionAssert.AreEqual(new[] {"3"}, f.Kind.Reads.Single()!.ToArray(),
            "a moved entity is read although its content did not change");
        var after = (await f.RowsAsync()).ToDictionary(r => r.LocalKey);
        Assert.AreEqual("k15", after["3"].OrderKey);
        Assert.IsTrue(after["3"].Seq > before["3"].Seq);
        Assert.AreNotEqual(before["3"].SharedHash, after["3"].SharedHash, "orderKey is in the comparison form");
        foreach (var key in new[] {"1", "2", "4"})
            Assert.AreEqual((before[key].Seq, before[key].VvJson), (after[key].Seq, after[key].VvJson), key);
    }

    [TestMethod]
    public async Task A_kind_with_order_needs_a_move_detector()
    {
        var f = await DataSyncRefreshFixture.CreateAsync(hasOrder: true,
            configure: s => s.RemoveAll<IDataSyncOrderMoveDetector>());
        f.Kind.Add("1", "A");

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => f.RefreshAsync());
        Assert.AreEqual(0, await f.Db.DataSyncEntities.CountAsync(), "the Refresh rolled back");
    }

    #endregion

    #region ComparisonFormVersion

    [TestMethod]
    public async Task A_new_ComparisonFormVersion_recomputes_hashes_without_revisions()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        f.Kind.Add("2", "Mood");
        await f.RefreshAsync();
        var link = await f.LinkAsync("peer-1");
        var row1 = await f.RowAsync("1");
        var record = DataSyncStoreFixture.Record([row1.SyncKey], DataSyncStoreFixture.Vv(("aaaaaaaaaaaaaaaa", 1)),
            content: f.Kind.Definitions["1"].ToContent());
        await f.Store.UpsertBasesAsync(link.Id,
            [new DataSyncBaseUpdate(f.KindId, new SyncKey(row1.SyncKey), DataSyncBaseState.Normal, null, record, null, null, false)],
            default);
        var baseBefore = await f.Db.DataSyncPeerBases.AsNoTracking().SingleAsync();
        var before = (await f.RowsAsync()).ToDictionary(r => r.LocalKey);

        f.Kind.MemoryCodec.ComparisonFormVersion = 2;
        f.Kind.Definitions["2"].Name = "Moods";
        var result = await f.RefreshAsync();

        Assert.AreEqual(1, result.Changed, "only the real edit is a revision (§3.4, §8.12)");
        var after = (await f.RowsAsync()).ToDictionary(r => r.LocalKey);
        Assert.AreNotEqual(before["1"].SharedHash, after["1"].SharedHash);
        Assert.AreEqual((before["1"].Seq, before["1"].VvJson), (after["1"].Seq, after["1"].VvJson));
        Assert.IsTrue(after["2"].Seq > before["2"].Seq);
        var baseAfter = await f.Db.DataSyncPeerBases.AsNoTracking().SingleAsync();
        Assert.AreNotEqual(baseBefore.SharedHash, baseAfter.SharedHash);
        Assert.AreEqual(DataSyncEntityForms.RecordSharedHash(f.Kind.Codec, record), baseAfter.SharedHash);
        Assert.AreEqual(2, DataSyncStoredJson.ReadVersions((await f.StateAsync()).ComparisonFormVersionsJson, "")[f.KindId]);

        Assert.AreEqual(0, (await f.RefreshAsync()).Changed, "and the next Refresh finds nothing");
    }

    [TestMethod]
    public async Task A_new_ComparisonFormVersion_never_takes_an_unsynced_rows_content_for_what_it_published()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        await f.Store.SetEntityStateAsync(f.KindId, "1", DataSyncEntitySyncState.LocalOnly, default);
        await f.RefreshAsync();
        f.Kind.Definitions["1"].Name = "Genres";
        Assert.AreEqual(0, (await f.RefreshAsync()).Changed, "an unsynced row takes no revision");
        var published = await f.RowAsync("1");

        f.Kind.MemoryCodec.ComparisonFormVersion = 2;
        Assert.AreEqual(0, (await f.RefreshAsync()).Changed);
        await f.Store.SetEntityStateAsync(f.KindId, "1", DataSyncEntitySyncState.Synced, default);
        var result = await f.RefreshAsync(collectPublished: true);

        Assert.AreEqual(1, result.Changed,
            "it rejoins with content it never published: a revision, never the new name under the old vector");
        var after = await f.RowAsync("1");
        Assert.AreEqual(DataSyncVvRelation.Dominates, DataSyncRefreshFixture.Vv(after.VvJson).CompareTo(DataSyncRefreshFixture.Vv(published.VvJson)));
        Assert.AreEqual("Genres", result.Published![(f.KindId, "1")].Content!["name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task A_new_ComparisonFormVersion_while_held_leaves_Publish_a_revision()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Horror"));
        await f.RefreshAsync();
        // What a sync apply wrote (a renamed child), recorded as the runner records it…
        f.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror films");
        await f.RefreshAsync();
        await f.LogApplyAsync(DataSyncHistoryKind.AutoSync, new Bakabase.InsideWorld.Business.Components.DataSync.Apply
            .DataSyncEntityChanges(f.KindId, "1", [],
            [
                new Bakabase.InsideWorld.Business.Components.DataSync.Apply.DataSyncChildChange("choice:pa",
                    DataSyncRefreshFixture.Child("a", "Horror"), DataSyncRefreshFixture.Child("a", "Horror films")),
            ]));
        // …and a stale whole-row write that undoes it: held (§6.5).
        f.Kind.Definitions["1"].Children[0] = new MemoryChild("a", "Horror");
        await f.RefreshAsync();
        var held = await f.RowAsync("1");
        Assert.IsTrue(held.PublishHeld);

        f.Kind.MemoryCodec.ComparisonFormVersion = 2;
        Assert.AreEqual(0, (await f.RefreshAsync()).Changed);
        Assert.IsTrue((await f.RowAsync("1")).PublishHeld);
        var result = await f.RefreshAsync(options: new DataSyncRefreshOptions([(f.KindId, "1")]));

        Assert.AreEqual(1, result.Changed, "Publish releases the held content as a revision, not under the old vector");
        var published = await f.RowAsync("1");
        Assert.IsFalse(published.PublishHeld);
        Assert.AreEqual(DataSyncVvRelation.DominatedBy, DataSyncRefreshFixture.Vv(held.VvJson).CompareTo(DataSyncRefreshFixture.Vv(published.VvJson)));
    }

    #endregion

    #region Kind schema versions (§8.4 condition 6)

    [TestMethod]
    public async Task Refresh_records_the_kind_schema_version_and_an_upgrade_remerges_held_records_once_on_every_link()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        Assert.AreEqual(1, DataSyncStoredJson.ReadVersions((await f.StateAsync()).KindSchemaVersionsJson, "")[f.KindId]);
        var entity = await f.RowAsync("1");
        var links = new[] { await f.LinkAsync("peer-1"), await f.LinkAsync("peer-2") };
        var record = DataSyncStoreFixture.Record([entity.SyncKey], DataSyncStoreFixture.Vv(("bbbbbbbbbbbbbbbb", 1))) with
        {
            SchemaVersion = 2,
        };
        foreach (var link in links)
        {
            await f.Store.UpsertBasesAsync(link.Id,
            [
                new DataSyncBaseUpdate(f.KindId, new SyncKey(entity.SyncKey), DataSyncBaseState.Normal, null, null, null,
                    DataSyncPendingRecords.Create(record, DataSyncPendingReason.Held, entity.Seq, DataSyncMergeFlags.None),
                    false),
            ], default);
        }

        await f.RefreshAsync();
        foreach (var link in links)
            Assert.AreEqual(0, (await f.Store.GetPendingToMergeAsync(link.Id, false, default)).Count,
                "the version this build recorded is its own: a held record is not merged again on every pull");

        // An upgrade: this build reads schema 2 now.
        f.Kind.MemoryCodec.SchemaVersion = 2;
        await f.RefreshAsync();

        Assert.AreEqual(2, DataSyncStoredJson.ReadVersions((await f.StateAsync()).KindSchemaVersionsJson, "")[f.KindId]);
        foreach (var link in links)
        {
            Assert.AreEqual(entity.SyncKey, (await f.Store.GetPendingToMergeAsync(link.Id, false, default)).Single().Key.Value,
                "each link merges it once after the upgrade");
        }

        // A merge evaluated it again on one link: that link leaves it alone, the other still takes it.
        (await f.Db.DataSyncPeerBases.SingleAsync(b => b.LinkId == links[0].Id)).PendingEvaluatedLocalSeq = entity.Seq;
        await f.Db.SaveChangesAsync();
        await f.RefreshAsync();
        Assert.AreEqual(0, (await f.Store.GetPendingToMergeAsync(links[0].Id, false, default)).Count);
        Assert.AreEqual(1, (await f.Store.GetPendingToMergeAsync(links[1].Id, false, default)).Count);
    }

    #endregion

    #region The actor

    [TestMethod]
    public async Task Refresh_is_skipped_while_unverified_and_runs_after_MarkVerified()
    {
        var f = await DataSyncRefreshFixture.CreateAsync(verified: false);
        f.Kind.Add("1", "Genre");

        var skipped = await f.RefreshAsync();

        Assert.IsTrue(skipped.Skipped);
        Assert.AreEqual(0, await f.Db.DataSyncLocalStates.CountAsync(), "no writes at all (§5.6)");
        Assert.AreEqual(0, await f.Db.DataSyncEntities.CountAsync());
        Assert.IsTrue(f.Watermark.Read().Missing);

        f.Guard.MarkVerified();
        var result = await f.RefreshAsync();
        Assert.IsFalse(result.Skipped);
        Assert.AreEqual(1, result.Changed);
    }

    [TestMethod]
    public async Task Refresh_asserts_the_actor_and_never_rotates()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        var state = await f.StateAsync();

        f.Identity.Device = TestDataSyncDeviceIdentity.NewDevice();
        f.Kind.Definitions["1"].Name = "Genres";
        await Assert.ThrowsExceptionAsync<DataSyncActorChangedException>(() => f.RefreshAsync());
        Assert.AreEqual((state.ActorId, state.ActorCounter), ((await f.StateAsync()).ActorId, (await f.StateAsync()).ActorCounter));

        await f.CheckAsync();
        var result = await f.RefreshAsync();
        var rotated = await f.StateAsync();
        Assert.AreNotEqual(state.ActorId, rotated.ActorId);
        Assert.AreEqual(rotated.ActorId, result.Actor.Value);
        Assert.AreEqual(1, result.Changed);

        // actor.json ahead of the row is the guard's to judge, never Refresh's.
        await f.Watermark.WriteAsync(DataSyncActorWatermark.Of(rotated) with {Generation = rotated.ActorGeneration + 5},
            default);
        await Assert.ThrowsExceptionAsync<DataSyncActorChangedException>(() => f.RefreshAsync());
    }

    [TestMethod]
    public async Task The_watermark_follows_only_committed_counters()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        var committed = f.Watermark.Read().Watermark;

        // Joined to a transaction that rolls back: nothing it issued reaches the file.
        f.Kind.Definitions["1"].Name = "Genres";
        await using (var transaction = await f.Db.Database.BeginTransactionAsync())
        {
            using var lease = await f.Gate.EnterAsync(null, default);
            var joined = await f.Refresher.RefreshAsync(lease, [f.KindId], false, default);
            Assert.AreEqual(1, joined.Changed);
            await transaction.RollbackAsync();
        }

        f.Db.ChangeTracker.Clear();
        Assert.AreEqual(committed, f.Watermark.Read().Watermark);
        Assert.AreEqual(committed!.Counter, (await f.StateAsync()).ActorCounter);

        // A local change through the coordinator commits, then writes the file.
        var change = await f.Coordinator.RunLocalChangeAsync([f.KindId], (_, _) => Task.CompletedTask, default);
        Assert.AreEqual(1, change.Refresh.Changed);
        Assert.AreEqual(DataSyncActorWatermark.Of(await f.StateAsync()), f.Watermark.Read().Watermark);
    }

    [TestMethod]
    public async Task Refresh_needs_the_gate()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        var lease = await f.Gate.EnterAsync(null, default);
        lease.Dispose();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            f.Refresher.RefreshAsync(lease, [f.KindId], false, default));
    }

    [TestMethod]
    public async Task An_unknown_kind_is_refused()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        using var lease = await f.Gate.EnterAsync(null, default);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            f.Refresher.RefreshAsync(lease, ["noSuchKind"], false, default));
    }

    #endregion

    #region When Refresh runs (§6.6)

    [TestMethod]
    public async Task A_head_uses_a_Refresh_at_most_5_seconds_old_shared_by_concurrent_heads()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");

        var heads = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.Coordinator.EnsureRecentAsync([f.KindId], default)));
        Assert.AreEqual(1, heads.Count(h => h is not null), "concurrent heads share one Refresh");

        f.Kind.Definitions["1"].Name = "Genres";
        f.Clock.Advance(TimeSpan.FromSeconds(4));
        Assert.IsNull(await f.Coordinator.EnsureRecentAsync([f.KindId], default));
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        var refreshed = await f.Coordinator.EnsureRecentAsync([f.KindId], default);
        Assert.AreEqual(1, refreshed!.Changed);
    }

    [TestMethod]
    public async Task A_head_waits_for_a_held_gate()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        using var held = await f.Gate.EnterAsync(null, default);
        using var cts = new CancellationTokenSource();

        var head = f.Coordinator.EnsureRecentAsync([f.KindId], cts.Token);
        await Task.Delay(100);
        Assert.IsFalse(head.IsCompleted, "the head waits for the gate (at most DataSyncGate.RequestTimeout)");
        cts.Cancel();
        try
        {
            await head;
            Assert.Fail("a cancelled wait holds nothing");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.IsTrue(f.Gate.IsHeld, "the test still holds it");
    }

    [TestMethod]
    public async Task An_entity_setting_commits_with_its_Refresh_in_one_transaction()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();
        var before = await f.RowAsync("1");

        var result = await f.Coordinator.RunLocalChangeAsync([f.KindId],
            (services, ct) => services.GetRequiredService<DataSyncStore>().SetChildrenLocalAsync(f.KindId, "1", true, ct),
            default);

        Assert.IsNull(result.Pause);
        Assert.AreEqual(1, result.Refresh.Changed, "the shared childrenLocal change is a revision (§3.6)");
        var after = await f.RowAsync("1");
        Assert.IsTrue(after.ChildrenLocal);
        Assert.IsTrue(after.Seq > before.Seq);
    }

    [TestMethod]
    public async Task A_local_change_that_meets_a_changed_actor_is_checked_and_retried_once()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre");
        await f.RefreshAsync();
        var before = await f.StateAsync();
        var runs = 0;

        var result = await f.Coordinator.RunLocalChangeAsync([f.KindId], async (services, ct) =>
        {
            runs++;
            // The device identity changes after the check, inside the first attempt's transaction.
            if (runs == 1) f.Identity.Device = TestDataSyncDeviceIdentity.NewDevice();
            await services.GetRequiredService<DataSyncStore>().SetEntityStateAsync(f.KindId, "1",
                DataSyncEntitySyncState.LocalOnly, ct);
        }, default);

        Assert.AreEqual(2, runs, "rolled back, checked, retried once (§5.6)");
        Assert.IsFalse(result.Refresh.Skipped);
        var after = await f.StateAsync();
        Assert.AreNotEqual(before.ActorId, after.ActorId);
        Assert.AreEqual(f.Identity.Device.NodeId, after.NodeId);
        Assert.AreEqual(DataSyncEntitySyncState.LocalOnly, (await f.RowAsync("1")).State);
    }

    [TestMethod]
    public async Task A_local_change_whose_Refresh_meets_evidence_commits_nothing_under_the_retiring_actor()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        await f.LinkAsync("peer-1");
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();
        var before = await f.StateAsync();
        var old = new DataSyncActorId(before.ActorId);
        var issued = DataSyncRefreshFixture.Vv((await f.RowAsync("1")).VvJson)[old];
        var guard = new EvidenceInFlightGuard(f.Guard);
        var coordinator = new DataSyncRefreshCoordinator(f.Gate, guard,
            f.Services.GetRequiredService<IServiceScopeFactory>(), f.Watermark, f.Clock);
        // A local edit the change's Refresh makes a revision of; while that Refresh runs, a head shows that a peer saw
        // counters of this actor that this database lost.
        f.Kind.Definitions["1"].Name = "Genres";
        f.Kind.OnReadRawHashes = () =>
        {
            f.Kind.OnReadRawHashes = null;
            guard.Arrive("peer-1", before.ActorId, before.ActorCounter + 5);
        };

        var result = await coordinator.RunLocalChangeAsync([f.KindId], (_, _) => Task.CompletedTask, default);

        Assert.IsFalse(result.Refresh.Skipped);
        Assert.AreEqual(1, result.Refresh.Changed, "the edit became a revision, on the second attempt");
        var after = await f.StateAsync();
        Assert.AreNotEqual(before.ActorId, after.ActorId, "rotated between the attempts, outside any transaction");
        Assert.AreEqual(before.ActorCounter + 5, DataSyncStoredJson.ReadCounters(after.RetiredActorsJson, "x")[before.ActorId]);
        var vv = DataSyncRefreshFixture.Vv((await f.RowAsync("1")).VvJson);
        Assert.AreEqual(issued, vv[old], "no counter of the retiring actor was committed");
        Assert.AreEqual(1L, vv[new DataSyncActorId(after.ActorId)]);
        Assert.AreEqual(after.ActorId, f.Watermark.Read().Watermark!.ActorId, "actor.json follows the commit");
    }

    /// <summary>An entity setting through the facade (<c>PUT /data-sync/entities/…</c>), in its own request scope.</summary>
    private static async Task<DataSyncTaskStart> SetEntitySyncAsync(DataSyncRefreshFixture f, string localKey,
        DataSyncEntitySyncInput input)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDataSyncService>()
            .SetEntitySyncAsync(f.KindId, localKey, input, default);
    }

    private static async Task<List<DataSyncApplyLogDbModel>> EntitySettingsAsync(DataSyncRefreshFixture f)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().DataSyncApplyLogs.AsNoTracking()
            .Where(l => l.Kind == DataSyncHistoryKind.EntitySetting).ToListAsync();
    }

    [TestMethod]
    public async Task An_entity_setting_through_the_facade_writes_the_counters_it_committed_to_the_watermark()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"), ("b", "Drama"));
        await f.RefreshAsync();
        var before = await f.StateAsync();

        // Only the overlay changes: a child kept on this device only is withheld, which is a revision (§3.6, §6.6).
        var start = await SetEntitySyncAsync(f, "1", new DataSyncEntitySyncInput(null, null, ["a"], null));

        Assert.IsNull(start.Problem, start.Problem?.Code.ToString());
        var after = await f.StateAsync();
        Assert.AreEqual(before.ActorCounter + 1, after.ActorCounter, "the setting's Refresh issued a counter");
        Assert.AreEqual(DataSyncActorWatermark.Of(after), f.Watermark.Read().Watermark, "actor.json follows the commit");
        Assert.AreEqual(1, (await EntitySettingsAsync(f)).Count);
    }

    [TestMethod]
    public async Task An_entity_setting_whose_Refresh_meets_evidence_commits_nothing_under_the_retiring_actor()
    {
        EvidenceInFlightGuard guard = null!;
        var f = await DataSyncRefreshFixture.CreateAsync(configure: s => s.AddSingleton<IDataSyncActorGuard>(sp =>
            guard = new EvidenceInFlightGuard(sp.GetRequiredService<DataSyncActorGuard>())));
        await f.LinkAsync("peer-1");
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();
        var before = await f.StateAsync();
        var old = new DataSyncActorId(before.ActorId);
        var issued = DataSyncRefreshFixture.Vv((await f.RowAsync("1")).VvJson)[old];
        _ = f.Services.GetRequiredService<IDataSyncActorGuard>();
        // While the setting's Refresh runs, a head shows that a peer saw counters of this actor that this database lost.
        f.Kind.OnReadRawHashes = () =>
        {
            f.Kind.OnReadRawHashes = null;
            guard.Arrive("peer-1", before.ActorId, before.ActorCounter + 5);
        };

        // "Sync the definition only" is a shared field: its Refresh issues a revision (§3.6).
        var start = await SetEntitySyncAsync(f, "1", new DataSyncEntitySyncInput(null, true, null, null));

        Assert.IsNull(start.Problem, start.Problem?.Code.ToString());
        var after = await f.StateAsync();
        Assert.AreNotEqual(before.ActorId, after.ActorId, "rotated between the attempts, outside any transaction");
        var row = await f.RowAsync("1");
        Assert.IsTrue(row.ChildrenLocal, "the second attempt wrote the setting");
        var vv = DataSyncRefreshFixture.Vv(row.VvJson);
        Assert.AreEqual(issued, vv[old], "no counter of the retiring actor was committed");
        Assert.AreEqual(1L, vv[new DataSyncActorId(after.ActorId)]);
        Assert.AreEqual(after.ActorId, f.Watermark.Read().Watermark!.ActorId, "actor.json follows the commit");
        Assert.AreEqual(1, (await EntitySettingsAsync(f)).Count, "the first attempt's history entry rolled back");
    }

    [TestMethod]
    public async Task An_entity_setting_whose_Refresh_meets_a_changed_actor_is_written_by_the_retry()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        f.Kind.Add("1", "Genre", ("a", "Action"));
        await f.RefreshAsync();
        // As after an identity reset racing the request: the first attempt's Refresh finds the actor changed.
        f.Kind.OnReadRawHashes = () =>
        {
            f.Kind.OnReadRawHashes = null;
            throw new DataSyncActorChangedException();
        };

        var start = await SetEntitySyncAsync(f, "1",
            new DataSyncEntitySyncInput(DataSyncEntitySyncState.LocalOnly, null, null, null));

        Assert.IsNull(start.Problem, start.Problem?.Code.ToString());
        Assert.IsNull(f.Kind.OnReadRawHashes, "the first attempt met the change");
        Assert.AreEqual(DataSyncEntitySyncState.LocalOnly, (await f.RowAsync("1")).State,
            "the retry ran in a fresh scope and wrote the setting again");
        Assert.AreEqual(1, (await EntitySettingsAsync(f)).Count);
    }

    [TestMethod]
    public async Task An_overlay_that_withholds_the_same_children_gives_no_Seq()
    {
        var f = await DataSyncRefreshFixture.CreateAsync();
        var link = await f.LinkAsync("peer-1");
        f.Kind.Add("1", "Genre", ("a", "Action"), ("b", "Drama"));
        await f.RefreshAsync();
        var before = await f.RowAsync("1");

        // The same overlay again, then a held child made local-only: neither is published, before or after.
        await f.Store.SetOverlayAsync(f.KindId, "1", DataSyncOverlay.None, default);
        await f.Store.SetOverlayAsync(f.KindId, "1", new DataSyncOverlay([], [new DataSyncHeldChild("b", link.Id)]), default);
        var held = await f.RowAsync("1");
        Assert.IsTrue(held.Seq > before.Seq, "withholding a published child changes the record (§6.2)");
        await f.RefreshAsync();
        held = await f.RowAsync("1");
        await f.Store.SetOverlayAsync(f.KindId, "1", new DataSyncOverlay(["b"], []), default);
        await f.Store.SetOverlayAsync(f.KindId, "1", new DataSyncOverlay(["b"], []), default);
        var result = await f.RefreshAsync();

        var after = await f.RowAsync("1");
        Assert.AreEqual(0, result.Changed);
        Assert.AreEqual((held.Seq, held.VvJson, held.SharedHash), (after.Seq, after.VvJson, after.SharedHash),
            "readers are not sent the same record again");
    }

    /// <summary>The real guard, and evidence a test makes arrive while a Refresh runs (§5.6).</summary>
    private sealed class EvidenceInFlightGuard(DataSyncActorGuard inner) : IDataSyncActorGuard
    {
        private (string Peer, string Actor, long Counter)? _arrived;

        public void Arrive(string peer, string actor, long counter) => _arrived = (peer, actor, counter);

        public bool IsVerified => _arrived is null && inner.IsVerified;

        public async Task<DataSyncPauseReason?> CheckAsync(DataSyncGateLease lease, CancellationToken ct)
        {
            if (_arrived is { } evidence)
            {
                _arrived = null;
                await inner.ReportPeerEvidenceAsync(evidence.Peer, evidence.Actor, evidence.Counter, ct);
            }

            return await inner.CheckAsync(lease, ct);
        }

        public Task ReportPeerEvidenceAsync(string peerNodeId, string actorId, long seenCounter, CancellationToken ct) =>
            inner.ReportPeerEvidenceAsync(peerNodeId, actorId, seenCounter, ct);

        public Task ReportReaderAheadAsync(string readerNodeId, CancellationToken ct) =>
            inner.ReportReaderAheadAsync(readerNodeId, ct);

        public void MarkVerified() => inner.MarkVerified();
    }

    #endregion
}

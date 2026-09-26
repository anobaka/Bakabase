using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// §8.11: continuous undo. Every history kind but <c>Undo</c> is undoable; an update is undone path by path (a
/// child-level diff) as a new local revision; a deletion is re-created with its keys and child ids; a create leaves an
/// unserved <c>UndoneCreate</c> tombstone with its keys and <c>Excluded(Undone)</c> bases, so neither this device nor
/// the peer creates or links anything again until the person includes it.
/// </summary>
[TestClass]
public partial class UndoTests
{
    private DataSyncApplyFixture _f = null!;
    private DataSyncPeer _peer = null!;
    private DataSyncLinkDbModel _link = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _f = await CreateAsync();
        _peer = new DataSyncPeer("PC-1");
        _link = await _f.LinkAsync(_peer);
    }

    private DataSyncUndoPlanner Planner => _f.Services.GetRequiredService<DataSyncUndoPlanner>();

    /// <summary>
    /// An undo whose every step is refused undoes nothing, and its task fails saying so (<c>UndoNotAvailable</c>)
    /// rather than completing with the entry still undoable.
    /// </summary>
    private static async Task NothingUndoneAsync(Func<Task<int?>> undo)
    {
        var e = await Assert.ThrowsExceptionAsync<BTaskException>(undo);
        Assert.AreEqual(nameof(DataSyncProblemCode.UndoNotAvailable), e.BriefMessage);
    }

    private async Task<(string Key, string LocalKey, DataSyncVersionVector Vv, int LogId, DataSyncStagedPullOf Pull)>
        CreatedByPeerAsync()
    {
        var key = SyncKey.New().Value;
        var alias = SyncKey.New().Value;
        var vv = _peer.Next();
        var record = _peer.Record([key, alias], vv, Content("Genre", ("a", "Rock"), ("b", "Jazz")), "a0");
        var outcome = await _f.ApplyAsync(_link, _peer, (Item, record));
        return (key, _f.Kind.KeyOf("Genre"), vv, outcome.ApplyLogId!.Value, new DataSyncStagedPullOf(record));
    }

    private sealed record DataSyncStagedPullOf(Bakabase.Modules.DataSync.Wire.DataSyncWireRecord Record);

    #region Creates

    [TestMethod]
    public async Task Undoing_a_create_leaves_an_unserved_tombstone_with_its_keys_and_Excluded_Undone_bases()
    {
        var (key, localKey, vv, logId, _) = await CreatedByPeerAsync();
        var keys = await _f.KeysOfAsync(localKey);
        var other = await _f.LinkAsync(new DataSyncPeer("NAS"));

        var preview = await Planner.PreviewAsync(logId, default);
        Assert.IsTrue(preview.CanUndo);
        var item = preview.Items.Single();
        Assert.AreEqual((DataSyncUndoAction.Remove, (DataSyncUndoBlock?) null, true),
            (item.Action, item.Blocked, item.SettingsMayReferenceIt));

        var undoId = await _f.UndoAsync(logId);

        Assert.IsNotNull(undoId);
        Assert.IsFalse(_f.Kind.Definitions.ContainsKey(localKey), "deleted through the service");
        var row = await _f.ByKeyAsync(key);
        Assert.IsNotNull(row!.DeletedAtUtc, "the side row is kept as a tombstone");
        Assert.AreEqual((DataSyncTombstoneKind.UndoneCreate, false), (row.TombstoneKind, row.TombstoneServed),
            "never served: nobody is asked to delete it");
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv(row.VvJson).CompareTo(vv), "Next(Undo)");
        var aliases = _f.NewDb().DataSyncKeyAliases.Where(a => a.SyncKey == key).Select(a => a.AliasKey).ToList();
        CollectionAssert.AreEquivalent(keys.Skip(1).ToArray(), aliases, "aliases are never deleted");
        foreach (var linkId in new[] { _link.Id, other.Id })
        {
            var b = (await _f.BasesAsync(linkId)).Single(x => x.SyncKey == key);
            Assert.AreEqual((DataSyncBaseState.Excluded, DataSyncExclusionReason.Undone), (b.State, b.ExclusionReason));
            CollectionAssert.IsSubsetOf(keys.ToArray(), DataSyncStoredJson.ReadStrings(b.ExclusionKeysJson, "x").ToArray());
        }

        var history = await _f.HistoryAsync();
        var original = history.Single(l => l.Id == logId);
        Assert.IsNotNull(original.UndoneAtUtc);
        var undo = history.Single(l => l.Id == undoId);
        Assert.AreEqual((DataSyncHistoryKind.Undo, (int?) logId), (undo.Kind, undo.UndoOfLogId));
        Assert.IsFalse((await Planner.PreviewAsync(logId, default)).CanUndo, "no second undo");
        Assert.IsFalse((await Planner.PreviewAsync(undoId!.Value, default)).CanUndo, "there is no redo");
    }

    [TestMethod]
    public async Task Undo_then_a_full_reconciliation_changes_nothing_and_nothing_relinks_on_the_next_pull()
    {
        var (key, _, vv, logId, pulled) = await CreatedByPeerAsync();
        await _f.UndoAsync(logId);
        var rowsBefore = await _f.RowsAsync();

        await _f.ApplyAsync(_link, _peer, _f.Pull(_peer, full: true, (Item, pulled.Record)));
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], _peer.Next(vv), Content("Genre", ("a", "Rock"), ("c", "Pop")), "a0")));

        Assert.AreEqual(0, _f.Kind.Definitions.Count, "a two-way undo creates nothing again");
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count, "and asks nothing");
        var rowsAfter = await _f.RowsAsync();
        Assert.AreEqual(rowsBefore.Single().VvJson, rowsAfter.Single().VvJson);
        Assert.IsFalse(rowsAfter.Single().TombstoneServed, "the peer is never offered a deletion of it");
    }

    [TestMethod]
    public async Task Include_revives_an_undone_create_through_row_T0()
    {
        var (key, _, _, logId, pulled) = await CreatedByPeerAsync();
        await _f.UndoAsync(logId);
        var tombstone = Vv((await _f.ByKeyAsync(key))!.VvJson);

        // [Include] (link view, §8.11): the exclusion goes and the row stays, unbound.
        var db = _f.NewDb();
        var b = db.DataSyncPeerBases.Single(x => x.LinkId == _link.Id && x.SyncKey == key);
        b.State = DataSyncBaseState.Normal;
        b.ExclusionReason = null;
        b.ExclusionKeysJson = null;
        await db.SaveChangesAsync();

        await _f.ApplyAsync(_link, _peer, _f.Pull(_peer, full: true, (Item, pulled.Record)));

        var row = await _f.ByKeyAsync(key);
        Assert.IsNull(row!.DeletedAtUtc, "row T0: a revive-create reusing the tombstone's key");
        Assert.IsTrue(_f.Kind.Definitions.ContainsKey(row.LocalKey));
        var vv = Vv(row.VvJson);
        Assert.IsTrue(vv.CompareTo(tombstone) is DataSyncVvRelation.Dominates or DataSyncVvRelation.Equal);
    }

    [TestMethod]
    public async Task A_create_with_values_is_kept_InUse_and_one_changed_since_is_kept_ChangedSinceImport()
    {
        var (_, localKey, _, logId, _) = await CreatedByPeerAsync();
        _f.Kind.Values[localKey] = 5;
        var preview = await Planner.PreviewAsync(logId, default);
        Assert.AreEqual((DataSyncUndoBlock?) DataSyncUndoBlock.InUse, preview.Items.Single().Blocked);
        Assert.AreEqual(5, preview.Items.Single().ValueCount);
        await NothingUndoneAsync(() => _f.UndoAsync(logId));
        Assert.IsTrue(_f.Kind.Definitions.ContainsKey(localKey), "values are never deleted by an undo");

        _f.Kind.Values.Remove(localKey);
        _f.Kind.Definitions[localKey] = _f.Kind[localKey].With(name: "Genre 2");
        await _f.RefreshAsync();
        Assert.AreEqual((DataSyncUndoBlock?) DataSyncUndoBlock.ChangedSinceImport,
            (await Planner.PreviewAsync(logId, default)).Items.Single().Blocked);
    }

    #endregion

    #region Updates (child-level diffs)

    private async Task<(string Key, string LocalKey, int LogId)> UpdatedByPeerAsync()
    {
        var (key, localKey, v1, _, _) = await CreatedByPeerAsync();
        var outcome = await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], _peer.Next(v1),
            Content("Genres", ("a", "Rock!"), ("b", "Jazz"), ("c", "Pop")), "a0")));
        Assert.AreEqual("Genres", _f.Kind[localKey].Name);
        return (key, localKey, outcome.ApplyLogId!.Value);
    }

    [TestMethod]
    public async Task Undoing_an_AutoSync_update_writes_back_exactly_the_changed_paths_as_a_local_revision()
    {
        var (_, localKey, logId) = await UpdatedByPeerAsync();
        // An unrelated local edit since: child b renamed.
        var content = _f.Kind[localKey];
        _f.Kind.Definitions[localKey] = content.With(children: content.Children
            .Select(c => c.Id == "b" ? new TestChild("b", "Jazz 2") : c).ToList());
        await _f.RefreshAsync();
        var before = await _f.RowAsync(localKey);
        var log = (await _f.HistoryAsync()).Single(l => l.Id == logId);
        Assert.IsTrue(log.PreImageBytes < 2_000, "a diff, not the whole row");

        var undoId = await _f.UndoAsync(logId);

        Assert.IsNotNull(undoId);
        var after = _f.Kind[localKey];
        Assert.AreEqual("Genre", after.Name);
        CollectionAssert.AreEqual(new[] { "a:Rock", "b:Jazz 2" }, after.Children.Select(c => c.Id + ":" + c.Label).ToArray(),
            "the rename and the add are undone by id; the local edit stays");
        var row = await _f.RowAsync(localKey);
        var vv = Vv(row.VvJson);
        Assert.AreEqual(DataSyncVvRelation.Dominates, vv.CompareTo(Vv(before.VvJson)), "a new local revision (Undo)");
        Assert.AreEqual((await _f.StateAsync()).ActorId, row.LastActorId);
        Assert.IsTrue(row.Seq > before.Seq);

        // Exempt from the lost-update guard: Refresh holds nothing.
        await _f.RefreshAsync();
        Assert.IsFalse((await _f.RowAsync(localKey)).PublishHeld);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task A_path_changed_since_refuses_and_an_added_child_in_use_refuses()
    {
        var (_, localKey, logId) = await UpdatedByPeerAsync();
        _f.Kind.Use(localKey, "c", 2);
        Assert.AreEqual((DataSyncUndoBlock?) DataSyncUndoBlock.AddedOptionsInUse,
            (await Planner.PreviewAsync(logId, default)).Items.Single().Blocked);
        _f.Kind.Usage.Remove(localKey);

        _f.Kind.Definitions[localKey] = _f.Kind[localKey].With(name: "Genres here");
        await _f.RefreshAsync();
        Assert.AreEqual((DataSyncUndoBlock?) DataSyncUndoBlock.ChangedSinceImport,
            (await Planner.PreviewAsync(logId, default)).Items.Single().Blocked);
        await NothingUndoneAsync(() => _f.UndoAsync(logId));
        Assert.AreEqual("Genres here", _f.Kind[localKey].Name);
    }

    [TestMethod]
    public async Task Newest_first_an_older_entry_waits_for_the_newer_one_on_the_same_entity()
    {
        var (key, localKey, v1, createId, _) = await CreatedByPeerAsync();
        var update = await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], _peer.Next(v1), Content("Genres", ("a", "Rock"), ("b", "Jazz")), "a0")));

        Assert.AreEqual((DataSyncUndoBlock?) DataSyncUndoBlock.ChangedSinceImport,
            (await Planner.PreviewAsync(createId, default)).Items.Single().Blocked, "v3.1 N15");
        Assert.IsNotNull(await _f.UndoAsync(update.ApplyLogId!.Value));
        Assert.AreEqual("Genre", _f.Kind[localKey].Name);
        Assert.IsNotNull(await _f.UndoAsync(createId), "the newer one undone first, the older one follows");
        Assert.IsFalse(_f.Kind.Definitions.ContainsKey(localKey));
    }

    [TestMethod]
    public async Task A_resolution_is_undoable()
    {
        var (key, localKey, v1, _, _) = await CreatedByPeerAsync();
        _f.Kind.Definitions[localKey] = _f.Kind[localKey].With(name: "Style");
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], _peer.Next(v1), Content("Kind", ("a", "Rock"), ("b", "Jazz")), "a0")));
        var item = (await _f.OpenItemsAsync()).Single();
        var logId = await _f.ResolveAsync(item, DataSyncInboxAction.UseRemote);
        Assert.AreEqual("Kind", _f.Kind[localKey].Name);

        Assert.IsNotNull(await _f.UndoAsync(logId!.Value));

        Assert.AreEqual("Style", _f.Kind[localKey].Name);
    }

    /// <summary>
    /// Key moves return to their owners all together or not at all (§5.3, §8.11): here B, re-keyed by Keep with
    /// entity, was deleted since, so its old primary cannot go back to it. Taken halfway, that key would be owned by no
    /// entity and a record carrying it would bind as new; so every key stays, and the undo's entry says so.
    /// </summary>
    [TestMethod]
    public async Task A_refused_key_restore_leaves_every_key_where_it_is()
    {
        var a = _f.Kind.Add(Content("Artist", ("x", "One")));
        var b = _f.Kind.Add(Content("Author", ("y", "Two")));
        await _f.RefreshAsync();
        var keyA = (await _f.RowAsync(a)).SyncKey;
        var keyB = (await _f.RowAsync(b)).SyncKey;
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([keyA, keyB], _peer.Next(), Content("Artist", ("x", "One"), ("z", "Three")), "a0")));
        var item = (await _f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.IdentityConflict);
        var logId = await _f.ResolveAsync(item, DataSyncInboxAction.KeepWithEntity, target: a);
        CollectionAssert.AreEquivalent(new[] { keyA, keyB }, (await _f.KeysOfAsync(a)).ToArray());
        Assert.AreEqual(2, _f.Kind[a].Children.Count, "the record merged into A");
        // B, re-keyed by the decision, is deleted here since.
        _f.Kind.Remove(b);
        await _f.RefreshAsync();

        var undoId = await _f.UndoAsync(logId!.Value);

        Assert.IsNotNull(undoId, "A's merge was undone");
        Assert.AreEqual(1, _f.Kind[a].Children.Count);
        CollectionAssert.AreEquivalent(new[] { keyA, keyB }, (await _f.KeysOfAsync(a)).ToArray(),
            "B's old primary, which B's lineage was published under, is still owned: undo never frees a key");
        await new DataSyncStoreFixtureKeys(_f).AssertKeyInvariantAsync();
        var undo = (await _f.HistoryAsync()).Single(l => l.Id == undoId);
        StringAssert.Contains(undo.ResultJson, "keys/" + Item + "/" + keyB, "the entry names the key it kept");
    }

    #endregion

    #region Deletions and type changes

    [TestMethod]
    public async Task Undoing_a_deletion_recreates_the_definition_with_its_keys_and_child_ids_and_a_new_local_id()
    {
        var (key, localKey, v1, _, _) = await CreatedByPeerAsync();
        var tombstoneVv = _peer.Next(v1);
        var outcome = await _f.ApplyAsync(_link, _peer, (Item, _peer.Tombstone([key], tombstoneVv)));
        Assert.IsFalse(_f.Kind.Definitions.ContainsKey(localKey), "created by sync, no values: deleted by itself (§8.6)");
        var preview = await Planner.PreviewAsync(outcome.ApplyLogId!.Value, default);
        Assert.AreEqual((DataSyncUndoAction.Recreate, true), (preview.Items.Single().Action,
            preview.Items.Single().RecreatedGetsNewId));

        await _f.UndoAsync(outcome.ApplyLogId!.Value);

        var row = await _f.ByKeyAsync(key);
        Assert.IsNull(row!.DeletedAtUtc, "its key revives");
        Assert.AreNotEqual(localKey, row.LocalKey, "a new local id (§8.11)");
        CollectionAssert.AreEqual(new[] { "a", "b" }, _f.Kind[row.LocalKey].Children.Select(c => c.Id).ToArray(),
            "the same child ids");
        Assert.AreEqual(0, _f.Kind.Values.GetValueOrDefault(row.LocalKey), "the values deleted with it do not come back");
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv(row.VvJson).CompareTo(tombstoneVv), "Undo ≥ the tombstone");
    }

    [TestMethod]
    public async Task A_type_change_is_converted_back()
    {
        var key = SyncKey.New().Value;
        var v1 = _peer.Next();
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v1, Content("Genre", ("a", "Rock")).With(type: "Choice"), "a0")));
        var localKey = _f.Kind.KeyOf("Genre");
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], _peer.Next(v1), Content("Genre", ("a", "Rock")).With(type: "Tags"), "a0")));
        var logId = await _f.ResolveAsync((await _f.OpenItemsAsync()).Single(), DataSyncInboxAction.Convert);
        Assert.AreEqual("Tags", _f.Kind[localKey].Type);

        await _f.UndoAsync(logId!.Value);

        Assert.AreEqual("Choice", _f.Kind[localKey].Type, "values lost by the first conversion stay lost");
    }

    [TestMethod]
    public async Task An_undo_entry_is_not_undoable()
    {
        var (_, _, _, logId, _) = await CreatedByPeerAsync();
        var undoId = await _f.UndoAsync(logId);
        await Assert.ThrowsExceptionAsync<BTaskException>(() => _f.UndoAsync(undoId!.Value));
    }

    #endregion

    #region Entity settings

    [TestMethod]
    public async Task An_entity_setting_is_undone_back_to_how_the_entity_synced()
    {
        var group = await _f.ExtensionGroups.Add(new ExtensionGroupAddInputModel("Video", [".mkv", ".mp4"]));
        var localKey = group.Id.ToString();
        await _f.RefreshAsync();
        var before = await _f.RowAsync(localKey, Groups);

        // How it syncs, set through the facade (§6.6): kept here only, and one extension kept on this device only.
        var logId = await SetEntitySyncAsync(localKey,
            new DataSyncEntitySyncInput(DataSyncEntitySyncState.LocalOnly, null, [".mp4"], null));
        var set = await _f.RowAsync(localKey, Groups);
        Assert.AreEqual(DataSyncEntitySyncState.LocalOnly, set.State);
        CollectionAssert.AreEqual(new[] {".mp4"}, DataSyncViews.ReadOverlay(set.OverlayJson).LocalOnlyChildren.ToArray());

        var preview = await Planner.PreviewAsync(logId, default);
        Assert.IsTrue(preview.CanUndo, preview.Problem?.Code.ToString());
        var item = preview.Items.Single();
        Assert.AreEqual((localKey, "Video", DataSyncUndoAction.Revert, (DataSyncUndoBlock?) null),
            (item.LocalKey, item.Name, item.Action, item.Blocked));

        var undoId = await _f.UndoAsync(logId);

        Assert.IsNotNull(undoId);
        var after = await _f.RowAsync(localKey, Groups);
        Assert.AreEqual(DataSyncEntitySyncState.Synced, after.State);
        Assert.AreEqual(0, DataSyncViews.ReadOverlay(after.OverlayJson).LocalOnlyChildren.Count);
        Assert.IsTrue(after.Seq > before.Seq, "published again");
        Assert.IsNotNull((await _f.HistoryAsync()).Single(h => h.Id == logId).UndoneAtUtc);
    }

    [TestMethod]
    public async Task A_newer_entity_setting_refuses_undoing_an_older_one()
    {
        var group = await _f.ExtensionGroups.Add(new ExtensionGroupAddInputModel("Video", [".mkv"]));
        var localKey = group.Id.ToString();
        await _f.RefreshAsync();
        var first = await SetEntitySyncAsync(localKey, new DataSyncEntitySyncInput(DataSyncEntitySyncState.LocalOnly,
            null, null, null));
        await SetEntitySyncAsync(localKey, new DataSyncEntitySyncInput(DataSyncEntitySyncState.Synced, null, null, null));

        var preview = await Planner.PreviewAsync(first, default);

        Assert.IsFalse(preview.CanUndo);
        Assert.AreEqual(DataSyncUndoBlock.ChangedSinceImport, preview.Items.Single().Blocked);
    }

    private async Task<int> SetEntitySyncAsync(string localKey, DataSyncEntitySyncInput input)
    {
        await using var scope = _f.Services.CreateAsyncScope();
        var start = await scope.ServiceProvider.GetRequiredService<IDataSyncService>()
            .SetEntitySyncAsync(Groups, localKey, input, default);
        Assert.IsNull(start.Problem, start.Problem?.Code.ToString());
        return (await _f.HistoryAsync()).Last(h => h.Kind == DataSyncHistoryKind.EntitySetting).Id;
    }

    #endregion
}

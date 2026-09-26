using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// §9.2 on real SQLite: every item type resolved with every allowed action, each <b>after at least one unrelated pull
/// of the same entity</b> (engineering B3; the resolving half of <c>InboxOriginTests</c>). A resolution closes its
/// items <c>ResolvedHere</c>, writes its revision through <c>DataSyncRevisionRules</c>, advances or keeps the bases as
/// the table says, and logs one <c>Resolution</c> entry.
/// </summary>
[TestClass]
public partial class ResolutionTests
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

    #region Scenarios

    /// <summary>The peer created "Genre" (a:Rock, b:Jazz) here: returns its key, local key and the peer's vector.</summary>
    private async Task<(string Key, string LocalKey, DataSyncVersionVector Vv)> SyncedFromPeerAsync(
        TestItemContent? content = null)
    {
        var key = SyncKey.New().Value;
        var vv = _peer.Next();
        content ??= Content("Genre", ("a", "Rock"), ("b", "Jazz"));
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], vv, content, "a0")));
        return (key, _f.Kind.KeyOf(content.Name), vv);
    }

    private async Task<DataSyncInboxItemDbModel> SingleOpenAsync(DataSyncInboxItemType type)
    {
        var open = (await _f.OpenItemsAsync()).Where(i => i.Type == type).ToList();
        Assert.AreEqual(1, open.Count, $"one open {type} item");
        return open[0];
    }

    /// <summary>The entity's conflict: renamed here and there; then the peer adds a child (an unrelated pull).</summary>
    private async Task<(string Key, string LocalKey, DataSyncVersionVector Remote)> NameConflictAsync()
    {
        var (key, localKey, v1) = await SyncedFromPeerAsync();
        _f.Kind.Definitions[localKey] = _f.Kind[localKey].With(name: "Style");
        var v2 = _peer.Next(v1);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v2, Content("Kind", ("a", "Rock"), ("b", "Jazz")), "a0")));
        var item = await SingleOpenAsync(DataSyncInboxItemType.FieldConflict);
        var v3 = _peer.Next(v2);
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], v3, Content("Kind", ("a", "Rock"), ("b", "Jazz"), ("c", "Pop")), "a0")));
        var again = await SingleOpenAsync(DataSyncInboxItemType.FieldConflict);
        Assert.AreEqual((item.Id, item.Token), (again.Id, again.Token), "the unrelated pull derived the same card again");
        CollectionAssert.Contains(_f.Kind[localKey].Children.Select(c => c.Label).ToList(), "Pop",
            "the safe part of the conflicted record applied");
        return (key, localKey, v3);
    }

    #endregion

    #region FieldConflict, ChildRenameConflict

    [TestMethod]
    [DataRow(DataSyncInboxAction.KeepLocal, "Style")]
    [DataRow(DataSyncInboxAction.UseRemote, "Kind")]
    [DataRow(DataSyncInboxAction.UseCustom, "Mix")]
    public async Task A_name_conflict_resolves_with_a_revision_over_every_record_and_the_base_takes_it(
        DataSyncInboxAction action, string expected)
    {
        var (key, localKey, remote) = await NameConflictAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.FieldConflict);
        var before = Vv((await _f.RowAsync(localKey)).VvJson);

        var logId = await _f.ResolveAsync(item, action, custom: action == DataSyncInboxAction.UseCustom ? "Mix" : null);

        Assert.IsNotNull(logId);
        Assert.AreEqual(expected, _f.Kind[localKey].Name);
        var row = await _f.RowAsync(localKey);
        var vv = Vv(row.VvJson);
        Assert.AreEqual(DataSyncVvRelation.Dominates, vv.CompareTo(remote), "Resolution = Max(L, R) + self dominates R");
        Assert.AreEqual(DataSyncVvRelation.Dominates, vv.CompareTo(before));
        Assert.AreEqual((await _f.StateAsync()).ActorId, row.LastActorId);
        var b = (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == key);
        Assert.AreEqual((remote.ToCanonicalString(), (DataSyncPendingReason?) null, (string?) null),
            (b.VvJson, b.PendingReason, b.PendingAppliedBaseJson), "base := R, pending cleared");
        var closed = (await _f.ItemsAsync()).Single(i => i.Id == item.Id);
        Assert.AreEqual((DataSyncInboxClosure.ResolvedHere, action, logId),
            (closed.Closure, closed.Action, closed.ApplyLogId));
        var log = (await _f.HistoryAsync()).Single(l => l.Id == logId);
        Assert.AreEqual(DataSyncHistoryKind.Resolution, log.Kind);

        // The next pull of the same record merges nothing: R is an ancestor now (row K4).
        var again = await _f.ApplyAsync(_link, _peer);
        Assert.AreEqual(0, again.NewInboxItems);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    [DataRow(DataSyncInboxAction.KeepLocal, "Rock2")]
    [DataRow(DataSyncInboxAction.UseRemote, "Rock3")]
    [DataRow(DataSyncInboxAction.UseCustom, "Rocks")]
    public async Task A_child_rename_conflict_writes_the_chosen_label_by_id(DataSyncInboxAction action, string expected)
    {
        var (key, localKey, v1) = await SyncedFromPeerAsync();
        _f.Kind.Definitions[localKey] = Content("Genre", ("a", "Rock2"), ("b", "Jazz"));
        var v2 = _peer.Next(v1);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v2, Content("Genre", ("a", "Rock3"), ("b", "Jazz")), "a0")));
        var v3 = _peer.Next(v2);
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], v3, Content("Genres", ("a", "Rock3"), ("b", "Jazz")), "a0")));
        var item = await SingleOpenAsync(DataSyncInboxItemType.ChildRenameConflict);
        Assert.AreEqual("child:a", item.SubjectPath);
        Assert.AreEqual("Genres", _f.Kind[localKey].Name, "the unrelated rename applied");

        await _f.ResolveAsync(item, action, custom: action == DataSyncInboxAction.UseCustom ? "Rocks" : null);

        var content = _f.Kind[localKey];
        Assert.AreEqual(("a", expected), (content.Children[0].Id, content.Children[0].Label), "the local id stays");
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv((await _f.RowAsync(localKey)).VvJson).CompareTo(v3));
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Detach_stops_syncing_the_entity_closes_its_items_and_clears_its_pending_records()
    {
        var (key, localKey, _) = await NameConflictAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.FieldConflict);

        await _f.ResolveAsync(item, DataSyncInboxAction.Detach);

        var row = await _f.RowAsync(localKey);
        Assert.AreEqual(DataSyncEntitySyncState.Detached, row.State);
        Assert.AreEqual("Style", _f.Kind[localKey].Name, "nothing of the content changes");
        Assert.IsNull((await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == key).PendingReason);
        var closed = (await _f.ItemsAsync()).Single(i => i.Id == item.Id);
        Assert.AreEqual((DataSyncInboxClosure.ResolvedHere, DataSyncInboxAction.Detach), (closed.Closure, closed.Action));
    }

    [TestMethod]
    public async Task A_card_that_changed_since_it_was_shown_is_updated_and_not_applied()
    {
        var (_, localKey, _) = await NameConflictAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.FieldConflict);
        // The local side changes again: the card now shows another local value, so another token.
        _f.Kind.Definitions[localKey] = _f.Kind[localKey].With(name: "Style 2");
        var logId = await _f.ResolveAsync(item, DataSyncInboxAction.UseRemote);

        Assert.AreEqual("Style 2", _f.Kind[localKey].Name, "nothing applied");
        var updated = (await _f.ItemsAsync()).Single(i => i.Id == item.Id);
        Assert.IsNull(updated.ClosedAtUtc);
        Assert.AreNotEqual(item.Token, updated.Token, "the item was derived again with its new token");
        Assert.IsNull(logId);
    }

    #endregion

    #region TypeChange

    private async Task<(string Key, string LocalKey, DataSyncVersionVector Remote)> TypeChangeAsync()
    {
        var (key, localKey, v1) = await SyncedFromPeerAsync(Content("Genre", ("a", "Rock"), ("b", "Jazz")).With(type: "Choice"));
        var v2 = _peer.Next(v1);
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], v2, Content("Genre", ("a", "Rock"), ("b", "Jazz")).With(type: "Tags"), "a0")));
        var item = await SingleOpenAsync(DataSyncInboxItemType.TypeChange);
        Assert.AreEqual("Choice", _f.Kind[localKey].Type, "a type change never applies by itself");
        var v3 = _peer.Next(v2);
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], v3, Content("Genre", ("a", "Rock"), ("b", "Jazz"), ("c", "Pop")).With(type: "Tags"), "a0")));
        Assert.AreEqual(item.Token, (await SingleOpenAsync(DataSyncInboxItemType.TypeChange)).Token);
        Assert.AreEqual(2, _f.Kind[localKey].Children.Count, "frozen for the link: nothing of the record applied");
        return (key, localKey, v3);
    }

    [TestMethod]
    public async Task Convert_changes_the_type_through_the_service_then_merges_the_waiting_record_in_Convert_mode()
    {
        var (key, localKey, remote) = await TypeChangeAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.TypeChange);

        var logId = await _f.ResolveAsync(item, DataSyncInboxAction.Convert);

        var content = _f.Kind[localKey];
        Assert.AreEqual("Tags", content.Type);
        CollectionAssert.AreEquivalent(new[] { "Rock", "Jazz", "Pop" }, content.Children.Select(c => c.Label).ToArray(),
            "children unioned by class: the rebuilt options map to the record's by label (§8.5.6)");
        Assert.IsTrue(_f.Kind.Applied.OfType<Bakabase.Modules.DataSync.Abstractions.ChangeSubtypeOperation>().Any());
        var b = (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == key);
        Assert.AreEqual((remote.ToCanonicalString(), (DataSyncPendingReason?) null), (b.VvJson, b.PendingReason));
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
        var log = (await _f.HistoryAsync()).Single(l => l.Id == logId);
        StringAssert.Contains(log.PreImageJson, "typeChanged");
    }

    [TestMethod]
    public async Task Convert_on_a_paused_link_waits_whole_and_nothing_half_converted_is_published()
    {
        var (key, localKey, _) = await TypeChangeAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.TypeChange);
        var before = await _f.RowAsync(localKey);
        await _f.SetLinkStateAsync(_link.Id, DataSyncLinkState.Paused, DataSyncPauseReason.ByUser);

        var logId = await _f.ResolveAsync(item, DataSyncInboxAction.Convert);

        Assert.IsNull(logId);
        Assert.AreEqual("Choice", _f.Kind[localKey].Type, "no phase one while phase two cannot follow it");
        Assert.IsFalse(_f.Kind.Applied.OfType<ChangeSubtypeOperation>().Any());
        Assert.IsNull((await _f.ItemsAsync()).Single(i => i.Id == item.Id).ClosedAtUtc, "the question stays open");
        await _f.RefreshAsync();
        var after = await _f.RowAsync(localKey);
        Assert.AreEqual((before.VvJson, before.Seq), (after.VvJson, after.Seq), "nothing is published");
        Assert.AreEqual(DataSyncPendingReason.TypeChange,
            (await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == key).PendingReason);

        // Resumed, the same decision converts in full.
        await _f.SetLinkStateAsync(_link.Id, DataSyncLinkState.Active);
        await _f.ResolveAsync(item, DataSyncInboxAction.Convert);
        Assert.AreEqual("Tags", _f.Kind[localKey].Type);
        Assert.AreEqual(3, _f.Kind[localKey].Children.Count);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Keep_this_devices_type_dominates_the_record_with_the_local_type()
    {
        var (key, localKey, remote) = await TypeChangeAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.TypeChange);

        await _f.ResolveAsync(item, DataSyncInboxAction.KeepLocal);

        Assert.AreEqual("Choice", _f.Kind[localKey].Type);
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv((await _f.RowAsync(localKey)).VvJson).CompareTo(remote));
        Assert.AreEqual(remote.ToCanonicalString(), (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == key).VvJson);
    }

    [TestMethod]
    public async Task A_type_change_can_be_detached()
    {
        var (_, localKey, _) = await TypeChangeAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.TypeChange), DataSyncInboxAction.Detach);
        Assert.AreEqual(DataSyncEntitySyncState.Detached, (await _f.RowAsync(localKey)).State);
        Assert.AreEqual("Choice", _f.Kind[localKey].Type);
    }

    #endregion

    #region DeletedThere

    /// <summary>A definition made here that the peer deleted after seeing it: never deleted by itself (§8.6).</summary>
    private async Task<(string Key, string LocalKey, DataSyncVersionVector Tombstone)> DeletedThereAsync()
    {
        var localKey = _f.Kind.Add(Content("Mood", ("m", "Calm")));
        _f.Kind.Values[localKey] = 3;
        await _f.RefreshAsync();
        var row = await _f.RowAsync(localKey);
        var tombstone = _peer.Next(Vv(row.VvJson));
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Tombstone([row.SyncKey], tombstone)));
        var item = await SingleOpenAsync(DataSyncInboxItemType.DeletedThere);
        // Delivered again (a daily full reconciliation re-sends tombstones): the same card.
        await _f.ApplyAsync(_link, _peer, _f.Pull(_peer, full: true, (Item, _peer.Tombstone([row.SyncKey], tombstone))));
        Assert.AreEqual(item.Token, (await SingleOpenAsync(DataSyncInboxItemType.DeletedThere)).Token);
        return (row.SyncKey, localKey, tombstone);
    }

    [TestMethod]
    public async Task Delete_here_deletes_through_the_service_and_takes_the_peers_tombstone()
    {
        var (key, localKey, tombstone) = await DeletedThereAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.DeletedThere), DataSyncInboxAction.DeleteHere);

        Assert.IsFalse(_f.Kind.Definitions.ContainsKey(localKey));
        var row = await _f.ByKeyAsync(key);
        Assert.IsNotNull(row!.DeletedAtUtc);
        Assert.AreEqual((tombstone.ToCanonicalString(), true, DataSyncTombstoneKind.Deleted),
            (row.VvJson, row.TombstoneServed, row.TombstoneKind), "AcceptRemoteDelete: the tombstone's own vector");
        var b = (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == key);
        Assert.AreEqual((tombstone.ToCanonicalString(), (DataSyncPendingReason?) null), (b.VvJson, b.PendingReason));
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Keep_here_only_detaches_the_entity()
    {
        var (_, localKey, _) = await DeletedThereAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.DeletedThere), DataSyncInboxAction.KeepHereOnly);
        Assert.AreEqual(DataSyncEntitySyncState.Detached, (await _f.RowAsync(localKey)).State);
    }

    [TestMethod]
    public async Task Restore_everywhere_dominates_the_tombstone_and_keeps_the_base()
    {
        var (key, localKey, tombstone) = await DeletedThereAsync();
        var baseBefore = (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == key);

        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.DeletedThere),
            DataSyncInboxAction.RestoreEverywhere);

        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv((await _f.RowAsync(localKey)).VvJson).CompareTo(tombstone));
        var b = (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == key);
        Assert.AreEqual((baseBefore.VvJson, (DataSyncPendingReason?) null), (b.VvJson, b.PendingReason));
    }

    #endregion

    #region ChildDeletedInUse

    private async Task<(string Key, string LocalKey, DataSyncVersionVector Remote)> ChildInUseAsync()
    {
        var (key, localKey, v1) = await SyncedFromPeerAsync();
        _f.Kind.Use(localKey, "a", 30);
        var v2 = _peer.Next(v1);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v2, Content("Genre", ("b", "Jazz")), "a0")));
        var item = await SingleOpenAsync(DataSyncInboxItemType.ChildDeletedInUse);
        Assert.AreEqual("child:a", item.SubjectPath);
        var v3 = _peer.Next(v2);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v3, Content("Genres", ("b", "Jazz")), "a0")));
        Assert.AreEqual(item.Id, (await SingleOpenAsync(DataSyncInboxItemType.ChildDeletedInUse)).Id,
            "state-derived: it stands through pulls");
        Assert.AreEqual("Genres", _f.Kind[localKey].Name);
        CollectionAssert.Contains(_f.Kind[localKey].Children.Select(c => c.Id).ToList(), "a", "kept and held");
        return (key, localKey, v3);
    }

    [TestMethod]
    public async Task Delete_here_removes_the_held_child_without_a_revision()
    {
        var (_, localKey, _) = await ChildInUseAsync();
        var before = await _f.RowAsync(localKey);

        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.ChildDeletedInUse), DataSyncInboxAction.DeleteHere);

        Assert.IsFalse(_f.Kind[localKey].Children.Any(c => c.Id == "a"));
        var row = await _f.RowAsync(localKey);
        Assert.AreEqual(before.VvJson, row.VvJson, "it was never published: no revision");
        Assert.AreEqual(0, DataSyncStoredJson.ReadOverlay(row.OverlayJson).HeldChildren.Count);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Keep_here_only_makes_the_held_child_local_only()
    {
        var (_, localKey, _) = await ChildInUseAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.ChildDeletedInUse), DataSyncInboxAction.KeepHereOnly);
        var overlay = DataSyncStoredJson.ReadOverlay((await _f.RowAsync(localKey)).OverlayJson);
        CollectionAssert.AreEqual(new[] { "a" }, overlay.LocalOnlyChildren.ToArray());
        Assert.AreEqual(0, overlay.HeldChildren.Count);
    }

    [TestMethod]
    public async Task Restore_everywhere_releases_the_hold_and_publishes_the_child_with_a_revision()
    {
        var (_, localKey, remote) = await ChildInUseAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.ChildDeletedInUse),
            DataSyncInboxAction.RestoreEverywhere);
        var row = await _f.RowAsync(localKey);
        Assert.AreEqual(0, DataSyncStoredJson.ReadOverlay(row.OverlayJson).HeldChildren.Count);
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv(row.VvJson).CompareTo(remote));
    }

    #endregion

    #region DeletedHereEditedThere

    private async Task<(string Key, DataSyncVersionVector Remote)> DeletedHereAsync()
    {
        var (key, localKey, v1) = await SyncedFromPeerAsync();
        _f.Kind.Remove(localKey);
        await _f.RefreshAsync();
        var v2 = _peer.Next(v1);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v2, Content("Genre", ("a", "Rock"), ("c", "Pop")), "a0")));
        var item = await SingleOpenAsync(DataSyncInboxItemType.DeletedHereEditedThere);
        var v3 = _peer.Next(v2);
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], v3, Content("Genres", ("a", "Rock"), ("c", "Pop")), "a0")));
        Assert.AreEqual(item.Token, (await SingleOpenAsync(DataSyncInboxItemType.DeletedHereEditedThere)).Token);
        return (key, v3);
    }

    [TestMethod]
    public async Task Restore_here_revives_the_tombstones_key_with_the_peers_content()
    {
        var (key, remote) = await DeletedHereAsync();
        var tombstone = Vv((await _f.ByKeyAsync(key))!.VvJson);

        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.DeletedHereEditedThere),
            DataSyncInboxAction.RestoreHere);

        var row = await _f.ByKeyAsync(key);
        Assert.IsNull(row!.DeletedAtUtc, "the tombstone's own row is revived");
        Assert.AreEqual("Genres", _f.Kind[row.LocalKey].Name);
        var vv = Vv(row.VvJson);
        Assert.AreEqual(DataSyncVvRelation.Dominates, vv.CompareTo(tombstone));
        Assert.AreEqual(DataSyncVvRelation.Dominates, vv.CompareTo(remote), "Max(T, R) + self");
        Assert.AreEqual(remote.ToCanonicalString(), (await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == key).VvJson);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Keep_deleted_gives_the_tombstone_a_revision_over_the_record()
    {
        var (key, remote) = await DeletedHereAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.DeletedHereEditedThere),
            DataSyncInboxAction.KeepDeleted);
        var row = await _f.ByKeyAsync(key);
        Assert.IsNotNull(row!.DeletedAtUtc);
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv(row.VvJson).CompareTo(remote));
        Assert.IsNull((await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == key).PendingReason);
    }

    #endregion

    #region LinkSuggestion

    private async Task<(string LocalKey, string RecordKey, DataSyncVersionVector Remote)> SuggestionAsync()
    {
        var localKey = _f.Kind.Add(Content("Genre", ("x", "Rock")));
        await _f.RefreshAsync();
        var recordKey = SyncKey.New().Value;
        var v1 = _peer.Next();
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([recordKey], v1, Content("Genre", ("a", "Rock"), ("b", "Jazz")), "a0")));
        var item = await SingleOpenAsync(DataSyncInboxItemType.LinkSuggestion);
        var v2 = _peer.Next(v1);
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([recordKey], v2, Content("Genre", ("a", "Rock"), ("b", "Jazz"), ("c", "Pop")), "a0")));
        Assert.AreEqual(item.Token, (await SingleOpenAsync(DataSyncInboxItemType.LinkSuggestion)).Token);
        Assert.AreEqual(1, _f.Kind.Definitions.Count, "nothing is linked or created by name alone");
        return (localKey, recordKey, v2);
    }

    [TestMethod]
    public async Task Link_records_the_peers_keys_on_the_candidate_and_merges_the_record_without_a_base()
    {
        var (localKey, recordKey, remote) = await SuggestionAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.LinkSuggestion), DataSyncInboxAction.Link,
            target: localKey);

        CollectionAssert.Contains((await _f.KeysOfAsync(localKey)).ToList(), recordKey);
        CollectionAssert.AreEquivalent(new[] { "Rock", "Jazz", "Pop" },
            _f.Kind[localKey].Children.Select(c => c.Label).ToArray(), "NoBase: a union by class");
        var row = await _f.RowAsync(localKey);
        var bases = await _f.BasesAsync(_link.Id);
        Assert.IsFalse(bases.Any(b => b.SyncKey == recordKey), "the Unbound base is replaced by the candidate's");
        Assert.AreEqual(remote.ToCanonicalString(), bases.Single(b => b.SyncKey == row.SyncKey).VvJson);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
        var log = (await _f.HistoryAsync()).Last();
        StringAssert.Contains(log.PreImageJson, "bound");
    }

    [TestMethod]
    public async Task Keep_both_creates_the_peers_entity_under_another_name_with_its_keys()
    {
        var (_, recordKey, remote) = await SuggestionAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.LinkSuggestion), DataSyncInboxAction.KeepBoth);

        var created = _f.Kind.Definitions.Single(d => d.Value.Name == "Genre (PC-1)");
        var row = await _f.RowAsync(created.Key);
        Assert.AreEqual(recordKey, row.SyncKey);
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv(row.VvJson).CompareTo(remote), "a changed name: + self");
        Assert.IsTrue(row.CreatedBySync);
        Assert.AreEqual(remote.ToCanonicalString(), (await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == recordKey).VvJson);
    }

    [TestMethod]
    public async Task Skip_excludes_the_record_so_it_is_not_proposed_again()
    {
        var (_, recordKey, remote) = await SuggestionAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.LinkSuggestion), DataSyncInboxAction.Skip);

        var b = (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == recordKey);
        Assert.AreEqual((DataSyncBaseState.Excluded, DataSyncExclusionReason.Skipped), (b.State, b.ExclusionReason));
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([recordKey], _peer.Next(remote), Content("Genre"), "a0")));
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count, "row E: ignored");
    }

    #endregion

    #region IdentityConflict

    [TestMethod]
    public async Task Keep_with_entity_moves_the_other_candidates_key_and_merges_the_record()
    {
        var a = _f.Kind.Add(Content("Artist", ("x", "One")));
        var b = _f.Kind.Add(Content("Author", ("y", "Two")));
        await _f.RefreshAsync();
        var keyA = (await _f.RowAsync(a)).SyncKey;
        var keyB = (await _f.RowAsync(b)).SyncKey;
        var v1 = _peer.Next();
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([keyA, keyB], v1, Content("Artist", ("x", "One")), "a0")));
        var item = await SingleOpenAsync(DataSyncInboxItemType.IdentityConflict);
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([keyA, keyB], _peer.Next(v1), Content("Artist", ("x", "One")), "a0")));
        item = await SingleOpenAsync(DataSyncInboxItemType.IdentityConflict);

        await _f.ResolveAsync(item, DataSyncInboxAction.KeepWithEntity, target: a);

        CollectionAssert.AreEquivalent(new[] { keyA, keyB }, (await _f.KeysOfAsync(a)).ToArray());
        var rowB = await _f.RowAsync(b);
        Assert.AreNotEqual(keyB, rowB.SyncKey, "B's primary moved: B was re-keyed (§5.3)");
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
        await new DataSyncStoreFixtureKeys(_f).AssertKeyInvariantAsync();
    }

    [TestMethod]
    public async Task Detach_of_an_identity_conflict_excludes_the_record()
    {
        var a = _f.Kind.Add(Content("Artist"));
        var b = _f.Kind.Add(Content("Author"));
        await _f.RefreshAsync();
        var keyA = (await _f.RowAsync(a)).SyncKey;
        var keyB = (await _f.RowAsync(b)).SyncKey;
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([keyA, keyB], _peer.Next(), Content("Artist"), "a0")));

        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.IdentityConflict), DataSyncInboxAction.Detach);

        var excluded = (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == keyA);
        Assert.AreEqual((DataSyncBaseState.Excluded, DataSyncExclusionReason.DroppedIdentity),
            (excluded.State, excluded.ExclusionReason));
        Assert.AreEqual(DataSyncEntitySyncState.Synced, (await _f.RowAsync(a)).State, "both entities keep syncing");
    }

    [TestMethod]
    public async Task Keep_record_linked_drops_the_other_records_keys_into_an_exclusion()
    {
        var keyE = SyncKey.New().Value;
        var alias = SyncKey.New().Value;
        var v1 = _peer.Next();
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([keyE, alias], v1, Content("Artist", ("x", "One")), "a0")));
        var localKey = _f.Kind.KeyOf("Artist");
        var keyQ = SyncKey.New().Value;
        var p = _peer.Record([keyE], _peer.Next(v1), Content("Artist", ("x", "One"), ("y", "Two")), "a0");
        var q = _peer.Record([keyQ, alias], _peer.Next(), Content("Author"), "a1");
        await _f.ApplyAsync(_link, _peer, (Item, p), (Item, q));
        var item = await SingleOpenAsync(DataSyncInboxItemType.IdentityConflict);
        Assert.AreEqual(1, _f.Kind[localKey].Children.Count, "row M: nothing applies until someone decides");

        await _f.ResolveAsync(item, DataSyncInboxAction.KeepRecordLinked, targetRecord: keyE);

        var keys = await _f.KeysOfAsync(localKey);
        CollectionAssert.AreEqual(new[] { keyE }, keys.ToArray(), "Q's alias is no longer the entity's");
        var excluded = (await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == keyQ);
        Assert.AreEqual((DataSyncBaseState.Excluded, DataSyncExclusionReason.DroppedIdentity),
            (excluded.State, excluded.ExclusionReason));
        CollectionAssert.AreEquivalent(new[] { alias, keyQ },
            DataSyncStoredJson.ReadStrings(excluded.ExclusionKeysJson, "x").ToArray());
        Assert.AreEqual(2, _f.Kind[localKey].Children.Count, "then P merged with the entity");
    }

    #endregion

    #region MassChildDeletion

    private async Task<(string Key, string LocalKey)> MassDeletionAsync()
    {
        var children = Enumerable.Range(0, 20).Select(i => ("c" + i, "L" + i)).ToArray();
        var (key, localKey, v1) = await SyncedFromPeerAsync(Content("Tags", children));
        var v2 = _peer.Next(v1);
        var record = _peer.Record([key], v2, Content("Tags", children.Skip(12).ToArray()), "a0");
        await _f.ApplyAsync(_link, _peer, (Item, record));
        var item = await SingleOpenAsync(DataSyncInboxItemType.MassChildDeletion);
        Assert.AreEqual(20, _f.Kind[localKey].Children.Count, "frozen: nothing of the change applies (B4)");
        await _f.ApplyAsync(_link, _peer, _f.Pull(_peer, full: true, (Item, record)));
        Assert.AreEqual(item.Id, (await SingleOpenAsync(DataSyncInboxItemType.MassChildDeletion)).Id);
        return (key, localKey);
    }

    [TestMethod]
    public async Task Apply_all_removes_the_unused_children()
    {
        var (_, localKey) = await MassDeletionAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.MassChildDeletion), DataSyncInboxAction.ApplyAll);
        Assert.AreEqual(8, _f.Kind[localKey].Children.Count);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Apply_all_on_a_paused_link_stays_decided_and_the_next_merge_after_resuming_applies_it()
    {
        var (key, localKey) = await MassDeletionAsync();
        await _f.SetLinkStateAsync(_link.Id, DataSyncLinkState.Paused, DataSyncPauseReason.ByUser);

        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.MassChildDeletion), DataSyncInboxAction.ApplyAll);

        Assert.AreEqual(20, _f.Kind[localKey].Children.Count, "a paused link merges nothing now");
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count, "the decision is taken");
        var waiting = (await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == key);
        Assert.AreEqual(DataSyncPendingReason.MassChildDeletion, waiting.PendingReason);
        Assert.AreEqual(DataSyncChildDeletionMode.Apply,
            DataSyncStoredJson.ReadFlags(waiting.PendingFlagsJson, "x").ChildDeletions, "with the flags it chose");
        Assert.IsNull(waiting.PendingEvaluatedLocalSeq, "re-merged by the link's next merge (§8.4 condition 5)");

        await _f.SetLinkStateAsync(_link.Id, DataSyncLinkState.Active);
        await _f.Runner.RunAutoSyncAsync(Context(await _f.LinkRowAsync(_link.Id), _peer), null, _f.Args());

        Assert.AreEqual(8, _f.Kind[localKey].Children.Count, "the decision applies once the link merges again");
        Assert.IsNull((await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == key).PendingReason);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count, "and asks nothing again");
    }

    [TestMethod]
    public async Task Review_each_holds_every_candidate_with_its_own_item()
    {
        var (_, localKey) = await MassDeletionAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.MassChildDeletion), DataSyncInboxAction.ReviewEach);
        Assert.AreEqual(20, _f.Kind[localKey].Children.Count);
        var open = await _f.OpenItemsAsync();
        Assert.AreEqual(12, open.Count(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse));
        Assert.AreEqual(12, DataSyncStoredJson.ReadOverlay((await _f.RowAsync(localKey)).OverlayJson).HeldChildren.Count);
    }

    [TestMethod]
    public async Task Restore_everywhere_keeps_the_children_and_publishes_them_again()
    {
        var (key, localKey) = await MassDeletionAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.MassChildDeletion),
            DataSyncInboxAction.RestoreEverywhere);
        Assert.AreEqual(20, _f.Kind[localKey].Children.Count);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
        var b = (await _f.BasesAsync(_link.Id)).Single(x => x.SyncKey == key);
        Assert.IsNull(b.PendingReason);
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv((await _f.RowAsync(localKey)).VvJson).CompareTo(Vv(b.VvJson!)),
            "the result differs from R: it carries this device's counter");
    }

    #endregion

    #region LargeChange, SuspectedLostUpdate

    [TestMethod]
    public async Task Apply_all_of_a_large_change_sets_the_once_flag_and_the_next_apply_merges_the_waiting_records()
    {
        var records = Enumerable.Range(0, 51)
            .Select(i => (Item, _peer.Record([SyncKey.New().Value], _peer.Next(), Content("P" + i), "a" + i))).ToArray();
        await _f.ApplyAsync(_link, _peer, records);
        Assert.AreEqual(0, _f.Kind.Definitions.Count, "B5: the side over its limit waits");
        var item = await SingleOpenAsync(DataSyncInboxItemType.LargeChange);

        await _f.ResolveAsync(item, DataSyncInboxAction.ApplyAll);

        var link = await _f.LinkRowAsync(_link.Id);
        Assert.IsTrue(DataSyncStoredJson.ReadFlags(link.OnceFlagsJson, "x").SkipLargeChange);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedHere, (await _f.ItemsAsync()).Single(i => i.Id == item.Id).Closure);

        var outcome = await _f.Runner.RunAutoSyncAsync(Context(link, _peer), null, _f.Args());
        Assert.AreEqual(51, outcome.Applied, "no refetch: the waiting records apply");
        Assert.AreEqual(51, _f.Kind.Definitions.Count);
        Assert.IsNull((await _f.LinkRowAsync(_link.Id)).OnceFlagsJson, "consumed");
    }

    private async Task<(string LocalKey, DataSyncVersionVector Applied)> LostUpdateAsync()
    {
        var (key, localKey, v1) = await SyncedFromPeerAsync();
        var v2 = _peer.Next(v1);
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], v2, Content("Genre", ("a", "Rock"), ("b", "Jazz"), ("c", "Pop")), "a0")));
        // A whole-row writer that read before the apply writes back afterwards: "Pop" is gone again, and it renames.
        _f.Kind.Definitions[localKey] = Content("Genre 2", ("a", "Rock"), ("b", "Jazz"));
        await _f.RefreshAsync();
        var row = await _f.RowAsync(localKey);
        Assert.IsTrue(row.PublishHeld, "the lost-update guard held it (§6.5)");
        await SingleOpenAsync(DataSyncInboxItemType.SuspectedLostUpdate);
        return (localKey, Vv(row.VvJson));
    }

    [TestMethod]
    public async Task Publish_keeps_this_devices_version_as_a_local_revision()
    {
        var (localKey, before) = await LostUpdateAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.SuspectedLostUpdate), DataSyncInboxAction.Publish);

        var row = await _f.RowAsync(localKey);
        Assert.IsFalse(row.PublishHeld);
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv(row.VvJson).CompareTo(before));
        Assert.AreEqual(2, _f.Kind[localKey].Children.Count, "the local content stays");
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Reapply_writes_back_only_the_undone_changes_and_keeps_the_other_local_edit()
    {
        var (localKey, before) = await LostUpdateAsync();
        await _f.ResolveAsync(await SingleOpenAsync(DataSyncInboxItemType.SuspectedLostUpdate), DataSyncInboxAction.Reapply);

        var content = _f.Kind[localKey];
        CollectionAssert.AreEqual(new[] { "Rock", "Jazz", "Pop" }, content.Children.Select(c => c.Label).ToArray(),
            "the child the writer dropped is back, by id");
        Assert.AreEqual("Genre 2", content.Name, "a change the apply did not make is kept");
        var row = await _f.RowAsync(localKey);
        Assert.IsFalse(row.PublishHeld);
        Assert.AreEqual(DataSyncVvRelation.Dominates, Vv(row.VvJson).CompareTo(before));
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    /// <summary>
    /// §6.5 with §8.5.4 step 3: putting a removal back never takes away an option resources here use. The card names
    /// it from the start; a Reapply while it is in use writes nothing and keeps the hold; once nothing uses it,
    /// Reapply removes it.
    /// </summary>
    [TestMethod]
    public async Task Reapply_never_removes_an_option_resources_here_use()
    {
        var (key, localKey, v1) = await SyncedFromPeerAsync(Content("Genre", ("a", "Rock"), ("b", "Jazz"), ("c", "Pop")));
        // The peer deletes Pop, which nothing here uses: removed by itself.
        await _f.ApplyAsync(_link, _peer,
            (Item, _peer.Record([key], _peer.Next(v1), Content("Genre", ("a", "Rock"), ("b", "Jazz")), "a0")));
        CollectionAssert.AreEqual(new[] { "a", "b" }, _f.Kind[localKey].Children.Select(c => c.Id).ToArray());
        // A whole-row writer puts it back, and seven resources then use it.
        _f.Kind.Definitions[localKey] = Content("Genre", ("a", "Rock"), ("b", "Jazz"), ("c", "Pop"));
        _f.Kind.Use(localKey, "c", 7);
        await _f.RefreshAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.SuspectedLostUpdate);
        var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "x");
        Assert.AreEqual((DataSyncInboxRules.ReapplyInUseDetail, 1), (payload.Detail, payload.ChildrenTotal),
            "the card says Reapply would remove an option in use");
        Assert.AreEqual("Pop", payload.Children!.Single().Text);
        CollectionAssert.AreEqual(new[] { DataSyncInboxAction.Publish, DataSyncInboxAction.Reapply },
            DataSyncInboxRules.AllowedActions(item.Type, item.SubjectPath, payload, false).ToArray());

        await _f.ResolveAsync(item, DataSyncInboxAction.Reapply);

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, _f.Kind[localKey].Children.Select(c => c.Id).ToArray(),
            "nothing removed while resources use it");
        Assert.IsTrue((await _f.RowAsync(localKey)).PublishHeld, "the hold stays: the stale overwrite is not published");
        var open = await SingleOpenAsync(DataSyncInboxItemType.SuspectedLostUpdate);
        Assert.AreEqual(item.Id, open.Id);
        Assert.AreEqual("Pop",
            DataSyncStoredJson.Read<DataSyncInboxPayload>(open.PayloadJson, "x").Children!.Single().Text);

        // Nothing uses it any more: Reapply puts the peer's deletion back.
        _f.Kind.Usage.Remove(localKey);
        await _f.ResolveAsync(open, DataSyncInboxAction.Reapply);

        CollectionAssert.AreEqual(new[] { "a", "b" }, _f.Kind[localKey].Children.Select(c => c.Id).ToArray());
        Assert.IsFalse((await _f.RowAsync(localKey)).PublishHeld);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Reapply_after_the_apply_entry_is_gone_keeps_the_hold_and_the_item_and_withdraws_Reapply()
    {
        var (localKey, before) = await LostUpdateAsync();
        // Retention pruned the apply's entry (its pre-images over the budget): nothing says what to write back.
        var db = _f.NewDb();
        db.DataSyncApplyLogs.RemoveRange(db.DataSyncApplyLogs);
        await db.SaveChangesAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.SuspectedLostUpdate);

        await _f.ResolveAsync(item, DataSyncInboxAction.Reapply);

        var row = await _f.RowAsync(localKey);
        Assert.IsTrue(row.PublishHeld, "the hold stays: clearing it would publish the stale overwrite");
        Assert.AreEqual(before, Vv(row.VvJson));
        Assert.AreEqual(("Genre 2", 2), (_f.Kind[localKey].Name, _f.Kind[localKey].Children.Count), "nothing written");
        var open = await SingleOpenAsync(DataSyncInboxItemType.SuspectedLostUpdate);
        Assert.AreEqual(item.Id, open.Id);
        var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(open.PayloadJson, "x");
        Assert.AreEqual(Bakabase.InsideWorld.Business.Components.DataSync.Persistence.DataSyncLostUpdateGuard.ReapplyUnavailable,
            payload.Detail, "the card says why");
        CollectionAssert.AreEqual(new[] { DataSyncInboxAction.Publish },
            DataSyncInboxRules.AllowedActions(open.Type, open.SubjectPath, payload, false).ToArray());

        await _f.RefreshAsync();
        row = await _f.RowAsync(localKey);
        Assert.IsTrue(row.PublishHeld);
        Assert.AreEqual(before, Vv(row.VvJson), "no Refresh publishes it meanwhile");

        await _f.ResolveAsync(open, DataSyncInboxAction.Publish);
        Assert.IsFalse((await _f.RowAsync(localKey)).PublishHeld, "Publish still settles it");
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count);
    }

    #endregion

    #region Regressions, long batches, pausing

    [TestMethod]
    public async Task A_regression_met_while_validating_is_reported_and_the_batch_runs_once_more()
    {
        var (localKey, recordKey, _) = await SuggestionAsync();
        var item = await SingleOpenAsync(DataSyncInboxItemType.LinkSuggestion);
        var state = await _f.StateAsync();
        // The waiting record shows a counter of this device's actor that this database never issued (row A1): a peer
        // saw revisions this device lost (§5.6).
        var db = _f.NewDb();
        var waiting = await db.DataSyncPeerBases.SingleAsync(b => b.LinkId == _link.Id && b.SyncKey == recordKey);
        var record = DataSyncStoredJson.ReadRecord(waiting.PendingRecordJson, "x")!;
        waiting.PendingRecordJson = DataSyncStoredJson.Write(record with
        {
            Vv = record.Vv.With(new DataSyncActorId(state.ActorId), state.ActorCounter + 5),
        });
        await db.SaveChangesAsync();

        await _f.ResolveAsync(item, DataSyncInboxAction.Link, target: localKey);

        var after = await _f.StateAsync();
        Assert.AreNotEqual(state.ActorId, after.ActorId, "reported through the actor guard, which rotated");
        Assert.AreEqual(state.ActorCounter + 5,
            DataSyncStoredJson.ReadCounters(after.RetiredActorsJson, "x")[state.ActorId], "the recorded counter");
        var link = await _f.LinkRowAsync(_link.Id);
        Assert.AreEqual((DataSyncLinkState.Paused, (DataSyncPauseReason?) DataSyncPauseReason.LocalRestoreSuspected),
            (link.State, link.PausedReason));
        Assert.AreEqual(DataSyncInboxClosure.ResolvedHere, (await _f.ItemsAsync()).Single(i => i.Id == item.Id).Closure,
            "the second run decided it");
        CollectionAssert.Contains((await _f.KeysOfAsync(localKey)).ToList(), recordKey);
        var row = await _f.RowAsync(localKey);
        var kept = (await _f.BasesAsync(_link.Id)).Single(b => b.SyncKey == row.SyncKey);
        Assert.AreEqual((DataSyncPendingReason?) DataSyncPendingReason.Retry, kept.PendingReason,
            "the paused link merges the record once it resumes");
        Assert.IsNull(kept.PendingEvaluatedLocalSeq);
    }

    /// <summary>Definitions P0… here and the peer's records of the first <paramref name="records"/> of them: suggestions.</summary>
    private async Task<List<DataSyncResolveInput>> BulkSuggestionsAsync(int definitions, int records)
    {
        for (var i = 0; i < definitions; i++) _f.Kind.Add(Content("P" + i, ("x" + i, "L" + i)));
        await _f.RefreshAsync();
        await _f.ApplyAsync(_link, _peer, Enumerable.Range(0, records)
            .Select(i => (Item, _peer.Record([SyncKey.New().Value], _peer.Next(), Content("P" + i, ("a", "M" + i)), "a" + i)))
            .ToArray());
        var open = (await _f.OpenItemsAsync()).Where(i => i.Type == DataSyncInboxItemType.LinkSuggestion).ToList();
        Assert.AreEqual(records, open.Count);
        return open.Select(i => new DataSyncResolveInput(i.Id, DataSyncInboxAction.Link, i.Token, null,
            _f.Kind.KeyOf(DataSyncStoredJson.Read<DataSyncInboxPayload>(i.PayloadJson, "x").EntityName), null, null)).ToList();
    }

    [TestMethod]
    public async Task A_bulk_link_over_hundreds_of_definitions_never_holds_the_write_lock_for_long()
    {
        var inputs = await BulkSuggestionsAsync(300, 100);
        _f.Runner.TransactionBudget = TimeSpan.FromMilliseconds(100);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var resolve = Task.Run(() => _f.ResolveAsync(inputs));
        var longest = TimeSpan.Zero;
        var taken = 0;
        while (!resolve.IsCompleted)
        {
            // Every other writer of the app waits like this for the lock; SQLite gives up after 30 s.
            var wait = System.Diagnostics.Stopwatch.StartNew();
            if (await _f.TryTakeWriteLockAsync(TimeSpan.FromSeconds(30))) taken++;
            if (wait.Elapsed > longest) longest = wait.Elapsed;
            await Task.Delay(10);
        }

        var logId = await resolve;
        Assert.IsNotNull(logId);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count, "every item linked");
        Assert.AreEqual(1, (await _f.HistoryAsync()).Count(l => l.Kind == DataSyncHistoryKind.Resolution),
            "one entry for the batch");
        Assert.IsTrue(taken >= 2, $"other writers got in between the batch's transactions ({taken} times)");
        Assert.IsTrue(longest < TimeSpan.FromSeconds(2),
            $"no transaction held the lock for long: the longest wait was {longest.TotalMilliseconds:F0} ms");
        Assert.IsTrue(started.Elapsed < TimeSpan.FromSeconds(60),
            $"a few hundred decisions over a few hundred definitions took {started.Elapsed.TotalSeconds:F1} s");
    }

    [TestMethod]
    public async Task A_paused_resolution_waits_between_its_transactions_holding_neither_the_write_lock_nor_the_gate()
    {
        // Three rename conflicts resolved with the peer's name: each writes through the adapter.
        var inputs = new List<DataSyncResolveInput>();
        for (var i = 0; i < 3; i++)
        {
            var key = SyncKey.New().Value;
            var v1 = _peer.Next();
            await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], v1, Content("Genre" + i), "a" + i)));
            var localKey = _f.Kind.KeyOf("Genre" + i);
            _f.Kind.Definitions[localKey] = _f.Kind[localKey].With(name: "Style" + i);
            await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([key], _peer.Next(v1), Content("Kind" + i), "a" + i)));
            var item = (await _f.OpenItemsAsync()).Single(x => x.SyncKey == key);
            inputs.Add(new DataSyncResolveInput(item.Id, DataSyncInboxAction.UseRemote, item.Token, null, null, null, null));
        }

        _f.Runner.TransactionBudget = TimeSpan.Zero;
        var pause = new PauseTokenSource();
        var writes = 0;
        _f.Kind.FailOn = _ =>
        {
            // Asked to pause at its first write, inside the first transaction: only the gap after it may wait.
            if (Interlocked.Increment(ref writes) == 1) pause.Pause();
            return null;
        };

        try
        {
            var resolve = Task.Run(() => _f.Runner.RunResolutionsAsync(inputs, new DataSyncApplyOptions(false),
                _f.Args("DataSyncResolve:paused", pause: pause.Token)));
            for (var i = 0; i < 100 && (_f.Gate.IsHeld || Volatile.Read(ref writes) == 0) && !resolve.IsCompleted; i++)
                await Task.Delay(50);

            Assert.IsTrue(pause.IsPauseRequested);
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.IsFalse(resolve.IsCompleted, "it waits while paused");
            Assert.AreEqual(1, Volatile.Read(ref writes), "the first chunk committed; nothing after it ran");
            Assert.IsTrue(await _f.TryTakeWriteLockAsync(TimeSpan.FromSeconds(1)), "no transaction is open while it waits");
            using (await _f.Gate.EnterAsync(TimeSpan.FromSeconds(1), default))
            {
                // The gate was given back: heads and every other data sync caller keep answering.
            }

            Assert.AreEqual(1, _f.Kind.Definitions.Values.Count(d => d.Name.StartsWith("Kind", StringComparison.Ordinal)));

            pause.Resume();
            Assert.IsNotNull(await resolve.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            _f.Kind.FailOn = null;
        }

        Assert.AreEqual(3, writes);
        Assert.AreEqual(0, (await _f.OpenItemsAsync()).Count, "it went on after Resume");
        CollectionAssert.AreEquivalent(new[] { "Kind0", "Kind1", "Kind2" },
            _f.Kind.Definitions.Values.Select(d => d.Name).ToArray());
    }

    [TestMethod]
    public async Task A_paused_undo_never_waits_inside_its_transaction()
    {
        // One apply that renamed two definitions: undoing it takes two steps in one transaction.
        var (genre, mood) = (SyncKey.New().Value, SyncKey.New().Value);
        var (g1, m1) = (_peer.Next(), _peer.Next());
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([genre], g1, Content("Genre", ("a", "Rock")), "a0")),
            (Item, _peer.Record([mood], m1, Content("Mood", ("c", "Calm")), "a1")));
        await _f.ApplyAsync(_link, _peer, (Item, _peer.Record([genre], _peer.Next(g1), Content("Genres", ("a", "Rock")), "a0")),
            (Item, _peer.Record([mood], _peer.Next(m1), Content("Moods", ("c", "Calm")), "a1")));
        var logId = (await _f.HistoryAsync()).Last().Id;
        var pause = new PauseTokenSource();
        _f.Kind.FailOn = _ =>
        {
            pause.Pause();
            return null;
        };

        // Asked to pause at its first write, the undo still finishes: inside the transaction only a stop counts.
        var undone = await Task.Run(() => _f.Runner.RunUndoAsync(logId, _f.Args("DataSyncUndo:" + logId, pause: pause.Token)))
            .WaitAsync(TimeSpan.FromSeconds(30));
        _f.Kind.FailOn = null;
        pause.Resume();

        Assert.IsNotNull(undone);
        CollectionAssert.AreEquivalent(new[] { "Genre", "Mood" }, _f.Kind.Definitions.Values.Select(d => d.Name).ToArray());
        Assert.IsTrue(await _f.TryTakeWriteLockAsync(TimeSpan.FromSeconds(1)));
    }

    #endregion
}

/// <summary>The key invariant (§5.3) over a fixture's database.</summary>
internal sealed class DataSyncStoreFixtureKeys(DataSyncApplyFixture f)
{
    public async Task AssertKeyInvariantAsync(string kind = Item)
    {
        var db = f.NewDb();
        var rows = db.DataSyncEntities.Where(e => e.Kind == kind).ToList();
        var aliases = db.DataSyncKeyAliases.Where(a => a.Kind == kind).ToList();
        var primaries = rows.Select(r => r.SyncKey).ToList();
        Assert.AreEqual(primaries.Count, primaries.Distinct().Count(), "a primary key is used twice");
        Assert.AreEqual(aliases.Count, aliases.Select(a => a.AliasKey).Distinct().Count(), "an alias is used twice");
        foreach (var alias in aliases)
        {
            Assert.IsFalse(primaries.Contains(alias.AliasKey), $"alias {alias.AliasKey} is also a primary");
            Assert.IsTrue(primaries.Contains(alias.SyncKey), $"alias {alias.AliasKey} points at no row");
        }

        await Task.CompletedTask;
    }
}

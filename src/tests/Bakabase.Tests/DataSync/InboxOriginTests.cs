using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Tests.DataSync.DataSyncStoreFixture;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// The two item origins (spec §9.1, §9.3) at the store: merger-derived items close when an evaluation no longer
/// produces them; state-derived items survive every pull and close only with their state; a link reset or Off turns
/// its holds into local-only children and closes its items, never touching <c>SuspectedLostUpdate</c>; dominance
/// closes another link's item. Resolving every item type with every action is the resolution runner's part of this
/// class.
/// </summary>
[TestClass]
public class InboxOriginTests
{
    private DataSyncStoreFixture _f = null!;

    [TestInitialize]
    public async Task Setup() => _f = await DataSyncStoreFixture.CreateAsync();

    [TestMethod]
    public async Task A_pull_upserts_its_drafts_and_closes_only_merger_items_it_evaluated_and_no_longer_produced()
    {
        var link = await _f.LinkAsync("peer-1");
        var (e1, e2, e3) = (await _f.LiveAsync("1"), await _f.LiveAsync("2"), await _f.LiveAsync("3"));
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        await _f.Store.ReconcileInboxAsync(link.Id, "peer-1",
        [
            Draft(Kind, e1.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name"),
            Draft(Kind, e2.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name"),
            Draft(Kind, e3.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name"),
            Draft(Kind, e1.SyncKey, DataSyncInboxItemType.ChildDeletedInUse, DataSyncInboxItemOrigin.State, "choice:x"),
        ], [(Kind, Key(e1.SyncKey)), (Kind, Key(e2.SyncKey)), (Kind, Key(e3.SyncKey))], [], t0, default);

        // The next pull evaluates e1 and e2 only: e1's conflict is produced again with a new token, e2's is gone
        // because the entity took another device's revision, and e1's state-derived item is not produced at all.
        var by = new DataSyncEditorRef("node-pc2", "PC-2", ActorB);
        var result = await _f.Store.ReconcileInboxAsync(link.Id, "peer-1",
            [Draft(Kind, e1.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name", token: "t2")],
            [(Kind, Key(e1.SyncKey)), (Kind, Key(e2.SyncKey))],
            [new DataSyncClosureHint(Kind, Key(e2.SyncKey), DataSyncInboxClosure.ResolvedElsewhere, by)],
            DateTime.UtcNow, default);

        Assert.AreEqual((0, 1, 1), (result.Created, result.Updated, result.Closed));
        var items = await _f.ItemsAsync();
        var e1Conflict = items.Single(i => i.SyncKey == e1.SyncKey && i.Type == DataSyncInboxItemType.FieldConflict);
        Assert.IsNull(e1Conflict.ClosedAtUtc);
        Assert.AreEqual("t2", e1Conflict.Token);
        Assert.AreEqual(t0, e1Conflict.CreatedAtUtc, "an updated item keeps its creation time");
        var e2Conflict = items.Single(i => i.SyncKey == e2.SyncKey);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedElsewhere, e2Conflict.Closure);
        Assert.AreEqual("PC-2", e2Conflict.ClosedByName);
        Assert.IsNull(items.Single(i => i.SyncKey == e3.SyncKey).ClosedAtUtc, "e3 was not evaluated");
        Assert.IsNull(items.Single(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse).ClosedAtUtc,
            "a state-derived item is never closed for not being produced");
    }

    [TestMethod]
    public async Task A_subject_has_one_open_item_per_link_and_one_without_a_link()
    {
        var link1 = await _f.LinkAsync("peer-1");
        var link2 = await _f.LinkAsync("peer-2");
        var e = await _f.LiveAsync("1");
        var draft = Draft(Kind, e.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name");
        var lost = Draft(Kind, e.SyncKey, DataSyncInboxItemType.SuspectedLostUpdate, DataSyncInboxItemOrigin.State);

        for (var i = 0; i < 2; i++)
        {
            await _f.Store.UpsertItemsAsync(link1.Id, "peer-1", [draft], DateTime.UtcNow, default);
            await _f.Store.UpsertItemsAsync(link2.Id, "peer-2", [draft], DateTime.UtcNow, default);
            await _f.Store.UpsertItemsAsync(null, null, [lost], DateTime.UtcNow, default);
        }

        var items = await _f.ItemsAsync();
        Assert.AreEqual(3, items.Count);
        Assert.IsNull(items.Single(i => i.Type == DataSyncInboxItemType.SuspectedLostUpdate).LinkId);
    }

    [TestMethod]
    public async Task State_items_close_exactly_when_their_state_is_gone()
    {
        var link = await _f.LinkAsync("peer-1");
        var held = await _f.LiveAsync("1");
        var frozen = await _f.LiveAsync("2");
        var suspect = await _f.LiveAsync("3");
        var waiting = await _f.LiveAsync("4");

        // The hold: the peer's child p-horror maps to the local child l-horror.
        await _f.Store.SetOverlayAsync(Kind, held.LocalKey,
            new DataSyncOverlay([], [new DataSyncHeldChild("l-horror", link.Id)]), default);
        await _f.Store.UpsertBasesAsync(link.Id,
        [
            new DataSyncBaseUpdate(Kind, Key(held.SyncKey), DataSyncBaseState.Normal, null,
                Record([held.SyncKey], Vv((ActorB, 1))), new Dictionary<string, string> {["p-horror"] = "l-horror"},
                null, false),
            new DataSyncBaseUpdate(Kind, Key(frozen.SyncKey), DataSyncBaseState.Normal, null, null, null,
                Pending(Record([frozen.SyncKey], Vv((ActorB, 2))), DataSyncPendingReason.MassChildDeletion), false),
            new DataSyncBaseUpdate(Kind, Key(waiting.SyncKey), DataSyncBaseState.Normal, null, null, null,
                Pending(Record([waiting.SyncKey], Vv((ActorB, 3))), DataSyncPendingReason.LargeChange), false),
        ], default);
        suspect.PublishHeld = true;
        await _f.Db.SaveChangesAsync();

        await _f.Store.UpsertItemsAsync(link.Id, "peer-1",
        [
            Draft(Kind, held.SyncKey, DataSyncInboxItemType.ChildDeletedInUse, DataSyncInboxItemOrigin.State, "choice:p-horror"),
            Draft(Kind, frozen.SyncKey, DataSyncInboxItemType.MassChildDeletion, DataSyncInboxItemOrigin.State),
            Draft("", SyncKey.LinkLevel.Value, DataSyncInboxItemType.LargeChange, DataSyncInboxItemOrigin.State, "largeChange"),
        ], DateTime.UtcNow, default);
        await _f.Store.UpsertItemsAsync(null, null,
            [Draft(Kind, suspect.SyncKey, DataSyncInboxItemType.SuspectedLostUpdate, DataSyncInboxItemOrigin.State)],
            DateTime.UtcNow, default);
        var touched = new[] {held, frozen, suspect, waiting}.Select(e => (Kind, Key(e.SyncKey))).ToList();

        // Every state still holds: nothing closes, however often it is checked.
        Assert.AreEqual(0, await _f.Store.CloseStaleStateItemsAsync(touched, link.Id, DateTime.UtcNow, default));
        Assert.AreEqual(0, await _f.Store.CloseStaleStateItemsAsync(touched, null, DateTime.UtcNow, default));

        // Take every state away.
        await _f.Store.SetOverlayAsync(Kind, held.LocalKey, DataSyncOverlay.None, default);
        await _f.Store.UpsertBasesAsync(link.Id,
        [
            new DataSyncBaseUpdate(Kind, Key(frozen.SyncKey), DataSyncBaseState.Normal, null, null, null,
                Pending(Record([frozen.SyncKey], Vv((ActorB, 2))), DataSyncPendingReason.Conflict), false),
            new DataSyncBaseUpdate(Kind, Key(waiting.SyncKey), DataSyncBaseState.Normal, null, null, null, null, true),
        ], default);
        suspect.PublishHeld = false;
        await _f.Db.SaveChangesAsync();

        Assert.AreEqual(4, await _f.Store.CloseStaleStateItemsAsync(touched, link.Id, DateTime.UtcNow, default));
        Assert.IsTrue((await _f.ItemsAsync()).All(i => i.Closure == DataSyncInboxClosure.Superseded));
    }

    [TestMethod]
    public async Task Resetting_a_link_turns_its_holds_local_only_and_closes_its_items()
    {
        var (link, other) = (await _f.LinkAsync("peer-1"), await _f.LinkAsync("peer-2"));
        var e = await _f.LiveAsync("1");
        await _f.Store.SetOverlayAsync(Kind, e.LocalKey, new DataSyncOverlay(["kept"],
        [
            new DataSyncHeldChild("only-this-link", link.Id),
            new DataSyncHeldChild("both-links", link.Id),
            new DataSyncHeldChild("both-links", other.Id),
        ]), default);
        await SeedItemsAndBasesAsync(link, e);

        await _f.Store.DeleteLinkAsync(link.Id, default);

        Assert.IsNull(await _f.Store.GetLinkAsync(link.Id, default));
        Assert.AreEqual(0, await _f.Db.DataSyncPeerBases.CountAsync(b => b.LinkId == link.Id));
        var overlay = DataSyncStoredJson.ReadOverlay((await _f.ByPrimaryAsync(e.SyncKey))!.OverlayJson);
        CollectionAssert.AreEquivalent(new[] {"kept", "only-this-link"}, overlay.LocalOnlyChildren.ToArray());
        Assert.AreEqual(new DataSyncHeldChild("both-links", other.Id), overlay.HeldChildren.Single(),
            "a child another link still holds stays held");
        var items = await _f.ItemsAsync();
        Assert.IsTrue(items.Where(i => i.LinkId == link.Id).All(i => i.Closure == DataSyncInboxClosure.LinkRemoved));
        Assert.IsNull(items.Single(i => i.Type == DataSyncInboxItemType.SuspectedLostUpdate).ClosedAtUtc,
            "SuspectedLostUpdate belongs to no link");
    }

    [TestMethod]
    public async Task Turning_a_link_off_keeps_its_bases_and_closes_its_items()
    {
        var link = await _f.LinkAsync("peer-1", DataSyncLinkMode.Follow);
        var e = await _f.LiveAsync("1");
        await _f.Store.SetOverlayAsync(Kind, e.LocalKey,
            new DataSyncOverlay([], [new DataSyncHeldChild("held", link.Id)]), default);
        await SeedItemsAndBasesAsync(link, e);

        await _f.Store.StopLinkAsync(link.Id, default);

        var stopped = (await _f.Store.GetLinkAsync(link.Id, default))!;
        Assert.AreEqual((DataSyncLinkMode.Off, DataSyncLinkMode.Follow, DataSyncLinkState.Stopped),
            (stopped.Mode, stopped.LastMode, stopped.State));
        var bases = await _f.Store.GetBasesAsync(link.Id, Kind, default);
        Assert.IsNotNull(bases.Single().Pending, "bases and pending records are kept");
        var overlay = DataSyncStoredJson.ReadOverlay((await _f.ByPrimaryAsync(e.SyncKey))!.OverlayJson);
        CollectionAssert.AreEqual(new[] {"held"}, overlay.LocalOnlyChildren.ToArray());
        Assert.AreEqual(0, overlay.HeldChildren.Count);
        var items = await _f.ItemsAsync();
        Assert.IsTrue(items.Where(i => i.LinkId == link.Id).All(i => i.Closure == DataSyncInboxClosure.LinkStopped));
        Assert.IsNull(items.Single(i => i.Type == DataSyncInboxItemType.SuspectedLostUpdate).ClosedAtUtc);
    }

    /// <summary>
    /// A link waiting for the access its request asked for, with a hold on an entity of its own, items, a base and a
    /// pending record.
    /// </summary>
    private async Task<(DataSyncLinkDbModel Link, DataSyncEntityDbModel Entity)> AwaitingAccessWithAHoldAsync(
        string peer, string localKey, DataSyncPauseReason? pausedReason = null)
    {
        var link = await _f.LinkAsync(peer, DataSyncLinkMode.Follow, DataSyncLinkState.AwaitingAccess);
        link.PendingRequestId = "req-" + peer;
        link.PausedReason = pausedReason;
        await _f.Store.UpdateLinkAsync(link, default);
        var e = await _f.LiveAsync(localKey);
        await _f.Store.SetOverlayAsync(Kind, e.LocalKey,
            new DataSyncOverlay([], [new DataSyncHeldChild("held", link.Id)]), default);
        await SeedItemsAndBasesAsync(link, e);
        return (link, e);
    }

    private Task<DataSyncLinkDbModel> LinkRowAsync(int id) =>
        _f.Db.DataSyncLinks.AsNoTracking().SingleAsync(l => l.Id == id);

    private async Task<DataSyncOverlay> OverlayAsync(DataSyncEntityDbModel e) =>
        DataSyncStoredJson.ReadOverlay((await _f.ByPrimaryAsync(e.SyncKey))!.OverlayJson);

    /// <summary>
    /// A request this device filed that ends — rejected, expired, no longer listed, or withdrawn by the person — stops a
    /// link with sync state as Off does (§8.1): nobody can decide its items and holds any more (must-fix 28), so the
    /// holds become local-only and the items close <c>LinkStopped</c>; bases, pending records and the last mode stay.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_request_that_ends_stops_its_link_as_Off_does(bool withdrawn)
    {
        var (link, e) = await AwaitingAccessWithAHoldAsync("peer-1", "1");
        var links = _f.Services.GetRequiredService<DataSyncLinkService>();

        if (withdrawn) await links.OnRequestCancelledAsync(link.Id, default);
        else await links.OnRequestEndedAsync(link.Id, DataSyncLinkService.AccessRejected, default);

        var stopped = await LinkRowAsync(link.Id);
        Assert.AreEqual((DataSyncLinkMode.Off, DataSyncLinkMode.Follow, DataSyncLinkState.Stopped),
            (stopped.Mode, stopped.LastMode, stopped.State));
        Assert.AreEqual(withdrawn ? DataSyncLinkService.AccessCancelled : DataSyncLinkService.AccessRejected,
            stopped.LastErrorCode);
        Assert.IsNotNull((await _f.Store.GetBasesAsync(link.Id, Kind, default)).Single().Pending,
            "bases and pending records are kept");
        var overlay = await OverlayAsync(e);
        CollectionAssert.AreEqual(new[] {"held"}, overlay.LocalOnlyChildren.ToArray());
        Assert.AreEqual(0, overlay.HeldChildren.Count);
        var items = await _f.ItemsAsync();
        Assert.IsTrue(items.Where(i => i.LinkId == link.Id).All(i => i.Closure == DataSyncInboxClosure.LinkStopped));
        Assert.IsNull(items.Single(i => i.Type == DataSyncInboxItemType.SuspectedLostUpdate).ClosedAtUtc);
    }

    /// <summary>
    /// That stop rewrites entity rows, which only a gate holder writes (§2.9): the fetch half, outside the gate, waits
    /// for it and changes nothing meanwhile. A link that asked a reset peer again goes back to its pause with its holds
    /// (§8.7 B1), which needs no gate.
    /// </summary>
    [TestMethod]
    public async Task A_request_that_ends_stops_its_link_only_under_the_gate()
    {
        var (link, e) = await AwaitingAccessWithAHoldAsync("peer-1", "1");
        var (reset, resetEntity) = await AwaitingAccessWithAHoldAsync("peer-2", "2", DataSyncPauseReason.PeerReset);
        var links = _f.Services.GetRequiredService<DataSyncLinkService>();
        var gate = _f.Services.GetRequiredService<DataSyncGate>();

        Task ended;
        using (await gate.EnterAsync(null, default))
        {
            await links.OnRequestEndedAsync(reset.Id, DataSyncLinkService.AccessExpired, default)
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(DataSyncLinkState.Paused, (await LinkRowAsync(reset.Id)).State);
            Assert.AreEqual(1, (await OverlayAsync(resetEntity)).HeldChildren.Count, "still held: the link goes on");

            ended = links.OnRequestEndedAsync(link.Id, DataSyncLinkService.AccessExpired, default);
            await Task.Delay(300);
            Assert.IsFalse(ended.IsCompleted, "the stop waits for the gate");
            Assert.AreEqual(DataSyncLinkState.AwaitingAccess, (await LinkRowAsync(link.Id)).State);
            Assert.AreEqual(1, (await OverlayAsync(e)).HeldChildren.Count);
        }

        await ended.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.AreEqual(DataSyncLinkState.Stopped, (await LinkRowAsync(link.Id)).State);
        CollectionAssert.AreEqual(new[] {"held"}, (await OverlayAsync(e)).LocalOnlyChildren.ToArray());
    }

    [TestMethod]
    public async Task Dominance_closes_the_items_of_every_link_whose_record_the_entity_absorbed()
    {
        var (l1, l2, l3) = (await _f.LinkAsync("peer-1"), await _f.LinkAsync("peer-2"), await _f.LinkAsync("peer-3"));
        var alias = NewKey();
        var e = await _f.LiveWithAliasesAsync("1", alias);
        await _f.Store.UpsertItemsAsync(l1.Id, "peer-1",
            [Draft(Kind, e.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name", Vv((ActorB, 2)))],
            DateTime.UtcNow, default);
        await _f.Store.UpsertItemsAsync(l3.Id, "peer-3",
            [Draft(Kind, alias, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name", Vv((ActorC, 1)))],
            DateTime.UtcNow, default);
        await _f.Store.UpsertItemsAsync(l2.Id, "peer-2",
            [Draft(Kind, e.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name", Vv((ActorC, 9)))],
            DateTime.UtcNow, default);

        // A revision edited on PC-2 that absorbed the records of links 1 and 3, not link 2's.
        e.VvJson = Vv((ActorA, 1), (ActorB, 3), (ActorC, 1)).ToCanonicalString();
        e.LastEditorNodeId = "node-pc2";
        e.LastEditorName = "PC-2";
        e.LastActorId = ActorB;
        await _f.Db.SaveChangesAsync();

        Assert.AreEqual(2, await _f.Store.CloseDominatedItemsAsync([(Kind, Key(e.SyncKey))], DateTime.UtcNow, default));
        var items = await _f.ItemsAsync();
        var third = items.Single(i => i.LinkId == l3.Id);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedElsewhere, third.Closure, "found through the entity's alias");
        Assert.AreEqual("PC-2", third.ClosedByName);
        Assert.IsNull(items.Single(i => i.LinkId == l2.Id).ClosedAtUtc, "a concurrent record is still a question");

        // A revision made here supersedes.
        e.VvJson = Vv((ActorA, 2), (ActorB, 3), (ActorC, 9)).ToCanonicalString();
        e.LastEditorNodeId = _f.SelfNodeId;
        await _f.Db.SaveChangesAsync();
        await _f.Store.CloseDominatedItemsAsync([(Kind, Key(e.SyncKey))], DateTime.UtcNow, default);
        Assert.AreEqual(DataSyncInboxClosure.Superseded, (await _f.ItemsAsync()).Single(i => i.LinkId == l2.Id).Closure);
    }

    [TestMethod]
    public async Task The_inbox_page_offers_the_actions_of_9_1_for_the_link_as_it_is()
    {
        var twoWay = await _f.LinkAsync("peer-1");
        var follow = await _f.LinkAsync("peer-2", DataSyncLinkMode.Follow);
        var e = await _f.LiveAsync("1");
        await _f.Store.UpsertItemsAsync(twoWay.Id, "peer-1",
        [
            Draft(Kind, e.SyncKey, DataSyncInboxItemType.DeletedThere, DataSyncInboxItemOrigin.Merger),
            Draft(Kind, e.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name"),
        ], DateTime.UtcNow, default);
        await _f.Store.UpsertItemsAsync(follow.Id, "peer-2",
        [
            Draft(Kind, e.SyncKey, DataSyncInboxItemType.DeletedThere, DataSyncInboxItemOrigin.Merger),
            Draft(Kind, e.SyncKey, DataSyncInboxItemType.ChildRenameConflict, DataSyncInboxItemOrigin.Merger, "node:p1:parent"),
        ], DateTime.UtcNow, default);

        var page = await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(), default);
        Assert.AreEqual((4, 4), (page.Total, page.OpenTotal));
        Assert.IsTrue(page.Items.All(i => i.CreatedAt.Kind == DateTimeKind.Utc && i.DefaultAction is null));
        CollectionAssert.Contains(Actions(page, twoWay.Id, DataSyncInboxItemType.DeletedThere).ToList(),
            DataSyncInboxAction.RestoreEverywhere);
        CollectionAssert.DoesNotContain(Actions(page, follow.Id, DataSyncInboxItemType.DeletedThere).ToList(),
            DataSyncInboxAction.RestoreEverywhere);
        CollectionAssert.Contains(Actions(page, twoWay.Id, DataSyncInboxItemType.FieldConflict).ToList(),
            DataSyncInboxAction.UseCustom);
        CollectionAssert.DoesNotContain(Actions(page, follow.Id, DataSyncInboxItemType.ChildRenameConflict).ToList(),
            DataSyncInboxAction.UseCustom, "a parent cannot be typed");
        Assert.AreEqual("PEER-1", page.Items.First(i => i.LinkId == twoWay.Id).PeerName);

        // Both devices following each other work as two-way (§8.1).
        follow.CounterpartJson = DataSyncStoredJson.Write(new Bakabase.Modules.DataSync.Wire.DataSyncFeedCounterpart(
            "follow", true, [Kind]));
        await _f.Store.UpdateLinkAsync(follow, default);
        page = await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(PeerNodeId: "peer-2"), default);
        Assert.AreEqual(2, page.Total);
        CollectionAssert.Contains(Actions(page, follow.Id, DataSyncInboxItemType.DeletedThere).ToList(),
            DataSyncInboxAction.RestoreEverywhere);

        // A closed item allows nothing; OpenOnly hides it.
        await _f.Store.CloseItemsAsync([page.Items[0].Id], DataSyncInboxClosure.ResolvedHere,
            DataSyncInboxAction.KeepLocal, new DataSyncEditorRef(_f.SelfNodeId, "me", ActorA), 3, default);
        var all = await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(OpenOnly: false, PeerNodeId: "peer-2"), default);
        Assert.AreEqual((2, 1), (all.Total, all.OpenTotal));
        var closed = all.Items.Single(i => i.ClosedAt is not null);
        Assert.AreEqual(0, closed.AllowedActions.Count);
        Assert.AreEqual(DataSyncInboxAction.KeepLocal, closed.Action);
        Assert.AreEqual(1, (await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(PeerNodeId: "peer-2"), default)).Total);
    }

    /// <summary>
    /// The order is the contract's (<see cref="DataSyncInboxQuery"/>): open items first, then closed ones, newest first
    /// in each group, so the closed ones start at <c>Skip = OpenTotal</c>. A local key with its kind reads one
    /// definition's items whole.
    /// </summary>
    [TestMethod]
    public async Task The_inbox_lists_open_items_first_newest_first_and_reads_one_definition_whole()
    {
        var link = await _f.LinkAsync("peer-1");
        var entities = new List<DataSyncEntityDbModel>();
        for (var n = 1; n <= 5; n++) entities.Add(await _f.LiveAsync(n.ToString()));
        var t = DateTime.UtcNow;
        foreach (var e in entities)
        {
            await _f.Store.UpsertItemsAsync(link.Id, "peer-1",
                [Draft(Kind, e.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name",
                    localKey: e.LocalKey)], t, default);
        }

        // A second conflict of definition 2, and an item of the other kind under the same local key.
        await _f.Store.UpsertItemsAsync(link.Id, "peer-1",
            [Draft(Kind, entities[1].SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger,
                "color", localKey: "2")], t, default);
        var other = await _f.LiveAsync("2", kind: DataSyncKindIds.ExtensionGroup);
        await _f.Store.UpsertItemsAsync(link.Id, "peer-1",
            [Draft(DataSyncKindIds.ExtensionGroup, other.SyncKey, DataSyncInboxItemType.FieldConflict,
                DataSyncInboxItemOrigin.Merger, "name", localKey: "2")], t, default);

        var ids = (await _f.ItemsAsync()).Select(i => i.Id).ToList();
        Assert.AreEqual(7, ids.Count);
        var closedIds = new[] { ids[0], ids[3] };
        await _f.Store.CloseItemsAsync(closedIds, DataSyncInboxClosure.ResolvedHere, DataSyncInboxAction.KeepLocal,
            null, null, default);

        var all = await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(OpenOnly: false), default);
        var open = ids.Except(closedIds).OrderByDescending(id => id).ToList();
        var closed = closedIds.OrderByDescending(id => id).ToList();
        CollectionAssert.AreEqual(open.Concat(closed).ToList(), all.Items.Select(i => i.Id).ToList());
        Assert.AreEqual((7, 5), (all.Total, all.OpenTotal));

        // Reading the closed ones after the open ones, page by page.
        var closedPage = await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(OpenOnly: false, Skip: all.OpenTotal),
            default);
        CollectionAssert.AreEqual(closed, closedPage.Items.Select(i => i.Id).ToList());
        var firstTwo = await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(Take: 2), default);
        CollectionAssert.AreEqual(open.Take(2).ToList(), firstTwo.Items.Select(i => i.Id).ToList());
        Assert.AreEqual((5, 5), (firstTwo.Total, firstTwo.OpenTotal));

        // One definition: both of its conflicts, not the other kind's item under the same local key.
        var definition = await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(Kind: Kind, LocalKey: "2"), default);
        Assert.AreEqual((2, 2), (definition.Total, definition.OpenTotal));
        Assert.IsTrue(definition.Items.All(i => i.Kind == Kind && i.LocalKey == "2"));
        CollectionAssert.AreEquivalent(new[] { "name", "color" }, definition.Items.Select(i => i.SubjectPath).ToArray());
        var bothKinds = await _f.Store.QueryInboxAsync(new DataSyncInboxQuery(LocalKey: "2"), default);
        Assert.AreEqual(3, bothKinds.Total, "a local key is unique per kind only");
    }

    private static IReadOnlyList<DataSyncInboxAction> Actions(DataSyncInboxPage page, int linkId,
        DataSyncInboxItemType type) =>
        page.Items.Single(i => i.LinkId == linkId && i.Type == type).AllowedActions;

    private async Task SeedItemsAndBasesAsync(DataSyncLinkDbModel link, DataSyncEntityDbModel e)
    {
        await _f.Store.UpsertBasesAsync(link.Id,
        [
            new DataSyncBaseUpdate(Kind, Key(e.SyncKey), DataSyncBaseState.Normal, null,
                Record([e.SyncKey], Vv((ActorB, 1))), null,
                Pending(Record([e.SyncKey], Vv((ActorB, 2))), DataSyncPendingReason.Conflict), false),
        ], default);
        await _f.Store.UpsertItemsAsync(link.Id, link.PeerNodeId,
        [
            Draft(Kind, e.SyncKey, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name"),
            Draft(Kind, e.SyncKey, DataSyncInboxItemType.ChildDeletedInUse, DataSyncInboxItemOrigin.State, "choice:held"),
        ], DateTime.UtcNow, default);
        await _f.Store.UpsertItemsAsync(null, null,
            [Draft(Kind, e.SyncKey, DataSyncInboxItemType.SuspectedLostUpdate, DataSyncInboxItemOrigin.State)],
            DateTime.UtcNow, default);
    }
}

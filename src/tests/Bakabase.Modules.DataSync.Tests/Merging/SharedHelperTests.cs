using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>The pure core of Refresh (§6.1) shared by C and the simulator.</summary>
[TestClass]
public class RefreshRulesTests
{
    private static DataSyncRefreshRow Row(object content, DataSyncEntitySyncState state = DataSyncEntitySyncState.Synced,
        bool publishHeld = false, string? orderKey = null, DataSyncOverlay? overlay = null) =>
        new(state, DataSyncPublication.Of(Items, content, overlay ?? DataSyncOverlay.None, false, orderKey, null).SharedHash,
            orderKey, overlay ?? DataSyncOverlay.None, false, publishHeld, null);

    [TestMethod]
    public void ARevisionOnlyWhenTheComparisonFormChanges()
    {
        var stored = T("Genre", ("1", "Action"), ("2", "Drama"));
        // Another local order of the same children: a local-only difference.
        var reordered = Items.Write(T("Genre", ("2", "Drama"), ("1", "Action")));
        var same = DataSyncRefreshRules.Evaluate(Items, Row(stored), reordered, false, null);
        Assert.AreEqual(DataSyncRefreshAction.HashesOnly, same.Action);
        Assert.AreEqual(ContentHash.Of(reordered), same.LocalHash);

        var edited = DataSyncRefreshRules.Evaluate(Items, Row(stored), Items.Write(T("Genres", ("1", "Action"))), false, null);
        Assert.AreEqual(DataSyncRefreshAction.LocalEdit, edited.Action);
        Assert.AreNotEqual(Row(stored).SharedHash, edited.SharedHash);

        var moved = DataSyncRefreshRules.Evaluate(Items, Row(stored, orderKey: "a0"), Items.Write(stored), false, "a5");
        Assert.AreEqual(DataSyncRefreshAction.LocalEdit, moved.Action, "a move is a comparison-form change");
        Assert.AreEqual("a5", moved.OrderKey);
    }

    [TestMethod]
    public void NeverARevisionForAnUnsyncedOrUnreadableRow()
    {
        var changed = Items.Write(T("Other"));
        Assert.AreEqual(DataSyncRefreshAction.HashesOnly,
            DataSyncRefreshRules.Evaluate(Items, Row(T("Genre"), DataSyncEntitySyncState.LocalOnly), changed, false, null).Action);
        Assert.AreEqual(DataSyncRefreshAction.HashesOnly,
            DataSyncRefreshRules.Evaluate(Items, Row(T("Genre"), DataSyncEntitySyncState.Detached), changed, false, null).Action);
        Assert.AreEqual(DataSyncRefreshAction.HashesOnly,
            DataSyncRefreshRules.Evaluate(Items, Row(T("Genre")), changed, true, null).Action);
    }

    [TestMethod]
    public void AnOverlayChildIsNotPublishedSoItsChangesAreLocal()
    {
        var overlay = new DataSyncOverlay(["2"], []);
        var stored = T("Genre", ("1", "Action"), ("2", "Mine"));
        var renamedLocalOnly = Items.Write(T("Genre", ("1", "Action"), ("2", "Mine too")));
        Assert.AreEqual(DataSyncRefreshAction.HashesOnly,
            DataSyncRefreshRules.Evaluate(Items, Row(stored, overlay: overlay), renamedLocalOnly, false, null).Action);
    }

    [TestMethod]
    public void TheGuardHoldsARevertInsteadOfPublishingIt()
    {
        var applied = DataSyncEntityChangeList.Between(Items, T("Genre", ("1", "Horror")), T("Genre", ("1", "Horror films")));
        var reverted = Items.Write(T("Genre", ("1", "Horror")));
        var decision = DataSyncRefreshRules.Evaluate(Items, Row(T("Genre", ("1", "Horror films"))), reverted, false, null,
            typed => DataSyncLostUpdateGuard.UndoneChanges(Items, typed, applied));
        Assert.AreEqual(DataSyncRefreshAction.HoldForLostUpdate, decision.Action);
        Assert.AreEqual("1", decision.Undone!.Renamed.Single().ChildId);
        Assert.AreEqual(Row(T("Genre", ("1", "Horror films"))).SharedHash, decision.SharedHash, "nothing is published");

        var later = DataSyncRefreshRules.Evaluate(Items, Row(T("Genre", ("1", "Horror films")), publishHeld: true),
            Items.Write(T("Genre", ("1", "Horror"), ("2", "More"))), false, null);
        Assert.AreEqual(DataSyncRefreshAction.RefreshHeldItem, later.Action, "a later local edit only updates the item");
    }

    [TestMethod]
    public void LocalRevisionsAndTombstones()
    {
        var vv = DataSyncRefreshRules.LocalRevision(Vv((Peer, 2)), Self, () => 11);
        Assert.AreEqual(Vv((Peer, 2), (Self, 11)), vv);
        Assert.IsTrue(DataSyncRefreshRules.ServesTombstone(DataSyncEntitySyncState.Synced));
        Assert.IsFalse(DataSyncRefreshRules.ServesTombstone(DataSyncEntitySyncState.LocalOnly));
        Assert.IsFalse(DataSyncRefreshRules.ServesTombstone(DataSyncEntitySyncState.Detached));
    }

    [TestMethod]
    public void AHeldPublicationStillRevisesOnEveryLocalChange()
    {
        var limits = DataSyncLimits.Default;
        var tooMany = T("Huge", Enumerable.Range(0, limits.MaxOptionsPerProperty + 1).Select(i => (i.ToString(), "L" + i)).ToArray());
        var publication = DataSyncPublication.Of(Items, tooMany, DataSyncOverlay.None, false, null, null);
        Assert.AreEqual(DataSyncHeldReason.Invalid, publication.Held);
        Assert.IsNull(publication.SharedHash);

        var row = new DataSyncRefreshRow(DataSyncEntitySyncState.Synced,
            DataSyncRefreshRules.HeldSharedHash(ContentHash.Of(Items.Write(tooMany))), null, DataSyncOverlay.None, false,
            false, null);
        Assert.AreEqual(DataSyncRefreshAction.HashesOnly,
            DataSyncRefreshRules.Evaluate(Items, row, Items.Write(tooMany), false, null).Action);
        var bigger = tooMany.With(children: [.. tooMany.Children, new TestChild("x", "More")]);
        Assert.AreEqual(DataSyncRefreshAction.LocalEdit,
            DataSyncRefreshRules.Evaluate(Items, row, Items.Write(bigger), false, null).Action);
    }
}

/// <summary>The pure half of RecordApply (§8.10.2, §6.4).</summary>
[TestClass]
public class RecordApplyTests
{
    private static readonly SyncKey A = K(0xa);

    private static DataSyncRevisionDecision Decision(DataSyncRevisionKind kind, DataSyncVersionVector remote,
        bool intendedEqualsRemote = true) =>
        new(ItemKind, new EntityKeys([A]), "1", kind, remote, null, intendedEqualsRemote, false, null, null, false, PeerEditor);

    private static string? Shared(object content) =>
        DataSyncPublication.Of(Items, content, DataSyncOverlay.None, false, null, null).SharedHash;

    [TestMethod]
    public void AReReadThatReachesThePeerAdoptsItsVectorAndEditor()
    {
        var remote = Vv((Peer, 4));
        var counter = 0L;
        var applied = DataSyncRecordApply.Revise(Items, Decision(DataSyncRevisionKind.FastForward, remote),
            Vv((Peer, 1)), Shared(T("Old")), T("New"), DataSyncOverlay.None, Shared(T("New")), PeerEditor, SelfEditor, Self,
            () => ++counter);
        Assert.AreEqual(remote, applied.Vv);
        Assert.AreEqual(PeerEditor, applied.LastEditor);
        Assert.AreEqual(0, counter);
        Assert.AreEqual(ContentHash.Of(Items.Write(T("New"))), applied.LocalHash, "hashes come from the re-read");
        Assert.IsFalse(applied.NormalizationChanged);
    }

    [TestMethod]
    public void NormalizationDriftAddsThisDevicesCounterOnce()
    {
        var remote = Vv((Peer, 4));
        var applied = DataSyncRecordApply.Revise(Items, Decision(DataSyncRevisionKind.FastForward, remote),
            Vv((Peer, 1)), Shared(T("Old")), T("New (normalized)"), DataSyncOverlay.None, Shared(T("New")), PeerEditor,
            SelfEditor, Self, () => 12);
        Assert.AreEqual(Vv((Peer, 4), (Self, 12)), applied.Vv);
        Assert.AreEqual(SelfEditor, applied.LastEditor, "this device's counter: this device is the last editor");
        Assert.IsTrue(applied.NormalizationChanged);
    }

    [TestMethod]
    public void ConflictsNeverAbsorbThePeersCounters()
    {
        var applied = DataSyncRecordApply.Revise(Items, Decision(DataSyncRevisionKind.MergedWithConflicts, Vv((Peer, 9)), false),
            Vv((Self, 3)), Shared(T("Mine")), T("Mine"), DataSyncOverlay.None, Shared(T("Theirs")), PeerEditor, SelfEditor,
            Self, () => 4);
        Assert.AreEqual(Vv((Self, 3)), applied.Vv, "the re-read equals the local form: no counter either");
    }

    [TestMethod]
    public void AnAcceptedDeletionHasNoContent()
    {
        var applied = DataSyncRecordApply.Revise(Items, Decision(DataSyncRevisionKind.AcceptRemoteDelete, Vv((Peer, 5))),
            Vv((Peer, 2)), Shared(T("Gone")), null, DataSyncOverlay.None, null, PeerEditor, SelfEditor, Self, () => 1);
        Assert.AreEqual(Vv((Peer, 5)), applied.Vv);
        Assert.IsNull(applied.LocalHash);
        Assert.AreEqual(PeerEditor, applied.LastEditor);
    }

    [TestMethod]
    public void CreatedEntitiesAreMappedIntoTheSharedOrder()
    {
        var itemId = DataSyncMergeItemIds.Of(ItemKind, A);
        var other = DataSyncMergeItemIds.Of(ItemKind, K(0xb));
        var order = DataSyncRecordApply.ResolveOrder(
            new DataSyncOrderAssignment(ItemKind, [("7", "a0"), (itemId, "a1"), (other, "a2"), ("3", "a3")]),
            new Dictionary<string, string> { [itemId] = "12" });
        CollectionAssert.AreEqual(new[] { "7", "12", "3" }, order.ToArray(), "a create that did not happen is left out");
        Assert.IsTrue(DataSyncMergeItemIds.TryParse(itemId, out var kind, out var key));
        Assert.AreEqual((ItemKind, A), (kind, key));
        Assert.IsFalse(DataSyncMergeItemIds.TryParse("12", out _, out _));
    }
}

/// <summary>The lost-update guard (§6.5), the pure half: which changes a current content undoes.</summary>
[TestClass]
public class LostUpdateGuardTests
{
    private static DataSyncEntityChangeList Applied =>
        DataSyncEntityChangeList.Between(Items,
            T("Genre", ("1", "Horror"), ("2", "Drama"), ("3", "Old")),
            T("Genres", ("1", "Horror films"), ("2", "Drama"), ("4", "Isekai")));

    [TestMethod]
    public void TheChangeListIsByPathAndById()
    {
        var changes = Applied;
        Assert.AreEqual("name", changes.Scalars.Single().Path);
        Assert.AreEqual("4", changes.Added.Single().ChildId);
        Assert.AreEqual("3", changes.Removed.Single().ChildId);
        Assert.AreEqual("1", changes.Renamed.Single().ChildId);
        Assert.IsTrue(DataSyncEntityChangeList.Between(Items, T("A"), T("A")).IsEmpty);
    }

    [TestMethod]
    public void EveryKindOfRevertIsSuspected()
    {
        // A whole-row write of the content read before the apply undoes all of it.
        var undone = DataSyncLostUpdateGuard.UndoneChanges(Items, T("Genre", ("1", "Horror"), ("2", "Drama"), ("3", "Old")), Applied);
        Assert.AreEqual(1, undone.Scalars.Count);
        Assert.AreEqual(1, undone.Added.Count, "an added child's id is gone");
        Assert.AreEqual(1, undone.Removed.Count, "a removed child's id is back");
        Assert.AreEqual(1, undone.Renamed.Count, "a renamed child carries its old label");
    }

    [TestMethod]
    public void OtherLocalEditsAreNotSuspect()
    {
        var current = T("Genres", ("1", "Horror films"), ("2", "Drama 2"), ("4", "Isekai"), ("5", "New here"));
        Assert.IsTrue(DataSyncLostUpdateGuard.UndoneChanges(Items, current, Applied).IsEmpty);
    }

    [TestMethod]
    public void TheWindowAndTheCoveredHistoryKinds()
    {
        var at = new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc);
        Assert.IsTrue(DataSyncLostUpdateGuard.InWindow(at, at.AddMinutes(10)));
        Assert.IsFalse(DataSyncLostUpdateGuard.InWindow(at, at.AddMinutes(10).AddTicks(1)));
        Assert.IsTrue(DataSyncLostUpdateGuard.Covers(DataSyncHistoryKind.AutoSync));
        Assert.IsTrue(DataSyncLostUpdateGuard.Covers(DataSyncHistoryKind.FirstLink));
        Assert.IsFalse(DataSyncLostUpdateGuard.Covers(DataSyncHistoryKind.Undo), "undo is exempt");
    }

    [TestMethod]
    public void TheItemBelongsToNoLinkAndNamesTheUndoneChanges()
    {
        var undone = DataSyncLostUpdateGuard.UndoneChanges(Items, T("Genre", ("1", "Horror"), ("2", "Drama"), ("3", "Old")), Applied);
        var draft = DataSyncLostUpdateGuard.Draft(ItemKind, new EntityKeys([K(0xa)]), "1", "Genre", null, undone, Vv((Self, 1)));
        Assert.AreEqual(DataSyncInboxItemType.SuspectedLostUpdate, draft.Type);
        Assert.AreEqual(DataSyncInboxItemOrigin.State, draft.Origin);
        Assert.AreEqual(4, draft.Payload.Fields.Count);
        Assert.AreEqual("Genres", draft.Payload.Fields.Single(f => f.Path == "name").Remote!.Text);
    }

    [TestMethod]
    public void SettingsAreScalarPaths()
    {
        var scalars = DataSyncEntityChangeList.ScalarsOf(JsonNode.Parse(
            """{"name":"Score","settings":{"precision":1},"defaultValue":[{"uuid":"a"}],"choices":[{"uuid":"a"}]}""")!.AsObject());
        CollectionAssert.AreEqual(new[] { "defaultValue", "name", "settings.precision" }, scalars.Keys.ToArray());
    }
}

/// <summary><c>ToPlannerSnapshot</c> (§2.4): only Synced entities are the first-contact planner's candidates.</summary>
[TestClass]
public class LocalStateTests
{
    [TestMethod]
    public void OnlySyncedEntitiesAndEveryTombstonedKey()
    {
        var f = new MergeFixture();
        f.Local("1", K(1), T("A"), Vv((Self, 1)));
        f.Local("2", K(2), T("B"), Vv((Self, 2)), state: DataSyncEntitySyncState.LocalOnly);
        f.Local("3", K(3), T("C"), Vv((Self, 3)), unreadable: true, aliases: [K(33)]);
        f.Tombstones[ItemKind] = [new DataSyncTombstoneState(new EntityKeys([K(4), K(44)]), Vv((Self, 4)), null,
            DataSyncEntitySyncState.Synced, DataSyncTombstoneKind.Deleted, true)];

        var snapshot = f.Input().Local[ItemKind].ToPlannerSnapshot();
        Assert.AreEqual(ItemKind, snapshot.Kind);
        CollectionAssert.AreEqual(new[] { "1", "3" }, snapshot.Entities.Select(e => e.LocalKey).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 2 }, snapshot.Entities.Select(e => e.Position).ToArray());
        Assert.IsTrue(snapshot.Entities[1].Unreadable);
        CollectionAssert.AreEqual(new[] { K(3), K(33) }, snapshot.Entities[1].Keys.All.ToArray());
        CollectionAssert.AreEquivalent(new[] { K(4), K(44) }, snapshot.TombstonedKeys.ToArray());
        Assert.AreEqual(f.EntitiesOf(ItemKind)[0].LocalHash, snapshot.Entities[0].ContentHash);
    }
}

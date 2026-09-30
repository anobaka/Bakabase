using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>The pure half of RecordApply (§8.10.2, §6.4).</summary>
[TestClass]
public class RecordApplyTests
{
    private static readonly SyncKey A = K(0xa);

    private static DataSyncRevisionDecision Decision(DataSyncRevisionKind kind, DataSyncVersionVector remote,
        bool intendedEqualsRemote = true) =>
        new(ItemKind, new EntityKeys([A]), "1", kind, remote, null, intendedEqualsRemote, false, null, false, PeerEditor);

    private static string? Shared(object content) =>
        DataSyncPublication.Of(Items, content, DataSyncOverlay.None, false, null).SharedHash;

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
    public void AnAcceptedDeletionHasNoContent()
    {
        var applied = DataSyncRecordApply.Revise(Items, Decision(DataSyncRevisionKind.AcceptRemoteDelete, Vv((Peer, 5))),
            Vv((Peer, 2)), Shared(T("Gone")), null, DataSyncOverlay.None, null, PeerEditor, SelfEditor, Self, () => 1);
        Assert.AreEqual(Vv((Peer, 5)), applied.Vv);
        Assert.IsNull(applied.LocalHash);
        Assert.AreEqual(PeerEditor, applied.LastEditor);
    }

    // ---- WithoutChangedDuringApply: nothing of a skipped entity is reported --------------------------

    private static DataSyncMergeResult Skip(MergeFixture f, out DataSyncMergeResult merged)
    {
        merged = f.Merge();
        var itemId = merged.Batches.Single().Operations.Single().ItemId;
        var deferred = DataSyncRecordApply.WithoutChangedDuringApply(merged, [itemId], f.Input().Bases);
        MergeFixture.AssertWellFormed(deferred);
        Assert.AreEqual(0, deferred.Batches.Count + deferred.Revisions.Count);
        Assert.AreEqual(DataSyncPendingReason.Retry, deferred.BaseUpdates.Single(u => u.Key == A).Pending!.Reason);
        Assert.IsFalse(deferred.Evaluated.Contains((ItemKind, A)), "its open items stand until the Retry record merges");
        return deferred;
    }

    [TestMethod]
    public void ASkippedAutomaticDeletionWritesNoHistoryFact()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Mood"), Vv((Peer, 1)), lastEditor: PeerEditor, createdBySync: true);
        f.ValueCounts[(ItemKind, "1")] = 0;
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var deferred = Skip(f, out var merged);
        Assert.AreEqual(DataSyncMergeNoteCodes.AutoDeleted, merged.Notes.Single().Code);
        Assert.AreEqual(0, deferred.Notes.Count, "nothing was deleted");
    }

    [TestMethod]
    public void ASkippedFollowUpdateNotifiesNothing()
    {
        var f = new MergeFixture { Mode = DataSyncLinkMode.Follow };
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 3))));
        f.Local("1", A, T("作者"), Vv((Self, 4)));
        f.Pull(f.Record(A, T("Artists"), Vv((Self, 3), (Peer, 1))));

        var deferred = Skip(f, out var merged);
        Assert.AreEqual(DataSyncMergeNoteCodes.FollowOverride, merged.Notes.Single().Code);
        Assert.AreEqual(0, deferred.Notes.Count, "no change on this device was replaced");
    }

    [TestMethod]
    public void ASkippedFastForwardClosesNoItem()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre"), Vv((Self, 3))));
        f.Local("1", A, T("Genre"), Vv((Self, 3)));
        f.OpenItem(41, A, DataSyncInboxItemType.FieldConflict, "name", Vv((Self, 3), (Third, 1)));
        f.Pull(f.Record(A, T("Genres"), Vv((Self, 3), (Peer, 1)), editedBy: ThirdEditor));

        var deferred = Skip(f, out var merged);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedElsewhere, merged.ClosureHints.Single().Closure);
        Assert.AreEqual(0, deferred.ClosureHints.Count + deferred.Inbox.Count, "the entity did not take the revision");
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

/// <summary>The scalar paths of canonical content, which a change list records (§6.5, §8.11).</summary>
[TestClass]
public class ContentScalarsTests
{
    [TestMethod]
    public void SettingsAreScalarPaths()
    {
        var scalars = DataSyncContentScalars.Of(JsonNode.Parse(
            """{"name":"Score","settings":{"precision":1},"defaultValue":[{"uuid":"a"}],"choices":[{"uuid":"a"}]}""")!.AsObject());
        CollectionAssert.AreEqual(new[] { "defaultValue", "name", "settings.precision" }, scalars.Keys.ToArray());
    }
}

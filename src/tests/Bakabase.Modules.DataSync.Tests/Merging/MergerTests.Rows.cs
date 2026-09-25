using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>§8.4, one case per row (M, H, E, A1, A2 with both verdicts, X, F, K1–K6, T0–T3, I, N1–N3).</summary>
[TestClass]
public partial class MergerTests
{
    private static readonly SyncKey A = K(0xa), B = K(0xb), C = K(0xc), D = K(0xd);

    private static DataSyncBaseUpdate BaseOf(DataSyncMergeResult r, SyncKey key, string kind = ItemKind) =>
        r.BaseUpdates.Single(u => u.Kind == kind && u.Key == key);

    private static bool Evaluated(DataSyncMergeResult r, SyncKey key, string kind = ItemKind) =>
        r.Evaluated.Contains((kind, key));

    private static IReadOnlyList<ApplyOperation> Ops(DataSyncMergeResult r) =>
        r.Batches.SelectMany(b => b.Operations).ToList();

    // ---- E ------------------------------------------------------------------------------------

    [TestMethod]
    public void E_AnExcludedRecordIsIgnoredWithoutAPendingRecord()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        f.Base(A, null, DataSyncBaseState.Excluded, exclusion: DataSyncExclusionReason.Skipped,
            exclusionKeys: [B.Value]);
        f.Pull(f.Record(B, T("Genre!"), Vv((Peer, 3)), aliases: [A]));

        var r = f.Merge();
        Assert.AreEqual(0, r.BaseUpdates.Count);
        Assert.AreEqual(0, Ops(r).Count);
        Assert.AreEqual(0, r.Evaluated.Count);
        Assert.AreEqual(f.Seq, r.CursorAdvance[ItemKind], "the cursor still moves past an ignored record");
    }

    // ---- M ------------------------------------------------------------------------------------

    [TestMethod]
    public void M_TwoRecordsBindingToOneEntityWaitUnderTheirOwnKeys()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Artist"), Vv((Self, 1)), aliases: [B]);
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 1))));
        f.Pull(f.Record(A, T("Artist"), Vv((Self, 1), (Peer, 2))));
        f.Pull(f.Record(C, T("Author"), Vv((Peer, 3)), aliases: [B]));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "nothing applies to the entity");
        Assert.AreEqual(0, r.Revisions.Count);
        var own = BaseOf(r, A);
        Assert.IsNull(own.Record, "no base written");
        Assert.AreEqual(DataSyncBaseState.Normal, own.State, "the row's state is unchanged");
        Assert.AreEqual(DataSyncPendingReason.IdentityConflict, own.Pending!.Reason);
        var other = BaseOf(r, C);
        Assert.AreEqual(DataSyncBaseState.Unbound, other.State);
        Assert.AreEqual(DataSyncPendingReason.IdentityConflict, other.Pending!.Reason);

        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.IdentityConflict, item.Type);
        Assert.AreEqual(A, item.Key);
        Assert.AreEqual("1", item.LocalKey);
        CollectionAssert.AreEqual(new[] { A.Value, C.Value }, item.Payload.Records!.Select(x => x.PrimaryKey).ToArray());
        CollectionAssert.AreEquivalent(new[] { DataSyncInboxAction.KeepRecordLinked, DataSyncInboxAction.Detach },
            DataSyncInboxRules.AllowedActions(item.Type, item.SubjectPath, item.Payload, true).ToArray());
        Assert.IsTrue(Evaluated(r, A) && Evaluated(r, C));
    }

    // ---- H ------------------------------------------------------------------------------------

    [TestMethod]
    public void H_AHeldRecordWaitsAndTheBaseKeepsItsContent()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        var agreed = f.Record(A, T("Genre"), Vv((Self, 1)));
        f.Base(A, agreed);
        f.Pull(f.Record(A, T("Genre 2"), Vv((Self, 1), (Peer, 1)), schemaVersion: 2));

        var r = f.Merge();
        var b = BaseOf(r, A);
        Assert.IsNull(b.Record, "the base keeps its content");
        Assert.AreEqual(DataSyncPendingReason.Held, b.Pending!.Reason);
        Assert.AreEqual(0, Ops(r).Count);
        Assert.IsFalse(Evaluated(r, A), "a held record evaluates nothing: the entity's items stand");
        Assert.IsTrue(r.CursorAdvance.ContainsKey(ItemKind), "the cursor still advances");
    }

    [TestMethod]
    public void H_AnUnreadableLocalEntityIsNeverTargeted()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)), unreadable: true);
        f.Pull(f.Record(A, T("Genre 2"), Vv((Self, 1), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(DataSyncPendingReason.Held, BaseOf(r, A).Pending!.Reason);
        Assert.AreEqual(0, Ops(r).Count);
    }

    // ---- A1 -----------------------------------------------------------------------------------

    [TestMethod]
    public void A1_ACounterAboveOursIsARegressionAndNothingElseIsDecided()
    {
        var f = new MergeFixture { ActorCounter = 5 };
        f.Local("1", A, T("Genre"), Vv((Self, 5)));
        f.Pull(f.Record(A, T("Genre 2"), Vv((Self, 7), (Peer, 1))));
        f.Pull(f.Record(B, T("Other"), Vv((Self, 9))));

        var r = f.Merge();
        Assert.IsNotNull(r.Anomaly);
        Assert.AreEqual(DataSyncAnomalies.Regression, r.Anomaly.Code);
        Assert.AreEqual(Self.Value, r.Anomaly.ActorId);
        Assert.AreEqual(9, r.Anomaly.SeenCounter, "the highest counter of the merge");
        Assert.AreEqual(B, r.Anomaly.Key);
        Assert.IsNull(r.Pause, "the actor guard decides the pause");
        Assert.AreEqual(0, r.BaseUpdates.Count + r.Batches.Count + r.CursorAdvance.Count);
    }

    // ---- A2 -----------------------------------------------------------------------------------

    [TestMethod]
    [DataRow("self")]
    [DataRow("peer")]
    public void A2_EqualVectorsWithDifferentFormsFromTheActorsOwnerAreADuplicateActor(string producer)
    {
        var f = new MergeFixture();
        var vv = Vv((Self, 3), (Peer, 2));
        f.Local("1", A, T("Genre"), vv);
        f.Pull(f.Record(A, T("Other"), vv, editedBy: producer == "self" ? SelfEditor : PeerEditor));

        var r = f.Merge();
        Assert.AreEqual(DataSyncAnomalies.DuplicateActor, r.Anomaly!.Code);
        Assert.AreEqual(DataSyncPauseReason.PeerIdentityDuplicated, r.Pause);
        StringAssert.Contains(r.PauseDetail, producer == "self" ? "own=true" : "own=false");
        Assert.AreEqual(0, r.BaseUpdates.Count);
    }

    [TestMethod]
    public void A2_ARevisionRelayedFromAThirdDeviceIsDrift()
    {
        var f = new MergeFixture();
        var vv = Vv((Self, 3), (Third, 2));
        f.Local("1", A, T("Genre"), vv);
        var record = f.Pull(f.Record(A, T("Genre "), vv, editedBy: ThirdEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.IsNull(r.Pause);
        Assert.AreEqual(record, BaseOf(r, A).Record, "base := R");
        Assert.AreEqual(0, r.Revisions.Count, "no revision");
        Assert.AreEqual(DataSyncMergeNoteCodes.NormalizationChanged, r.Notes.Single().Code);
    }

    [TestMethod]
    public void A2_AnotherComparisonFormVersionIsDriftNeverADuplicateActor()
    {
        var f = new MergeFixture { PeerComparisonFormVersion = 2 };
        var vv = Vv((Self, 3), (Peer, 2));
        f.Local("1", A, T("Genre"), vv);
        f.Pull(f.Record(A, T("Other"), vv, editedBy: PeerEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.AreEqual(DataSyncMergeNoteCodes.NormalizationChanged, r.Notes.Single().Code);
    }

    [TestMethod]
    public void A2_ARetiredActorsRevisionUnderAnotherFormVersionIsDriftNeverACollision()
    {
        // Retired actors exist after any identity reset or restore detection, and form versions change across betas.
        var f = new MergeFixture { PeerComparisonFormVersion = 2 };
        f.RetiredCounters[Retired.Value] = 5;
        var retiredEditor = new DataSyncEditorRef(SelfNode, "This PC", Retired.Value);
        f.Base(A, f.Record(A, T("Artist"), Vv((Retired, 4)), editedBy: retiredEditor));
        f.Local("1", A, T("Artists"), Vv((Retired, 5)), lastEditor: retiredEditor);
        var record = f.Pull(f.Record(A, T("Artist!"), Vv((Retired, 5)), editedBy: retiredEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.IsNull(r.Pause);
        Assert.AreEqual(record, BaseOf(r, A).Record, "base := R");
        Assert.AreEqual(DataSyncMergeNoteCodes.NormalizationChanged, r.Notes.Single().Code);
        Assert.AreEqual(0, r.Inbox.Count, "no question");
        Assert.AreEqual(0, r.Revisions.Count + Ops(r).Count, "no revision, nothing applied");

        // Under this build's form version the same pair is a collision: merged as concurrent versions.
        var same = new MergeFixture();
        same.RetiredCounters[Retired.Value] = 5;
        same.Base(A, same.Record(A, T("Artist"), Vv((Retired, 4)), editedBy: retiredEditor));
        same.Local("1", A, T("Artists"), Vv((Retired, 5)), lastEditor: retiredEditor);
        same.Pull(same.Record(A, T("Artist!"), Vv((Retired, 5)), editedBy: retiredEditor));
        Assert.AreEqual("name", same.Merge().Inbox.Single().SubjectPath);
    }

    [TestMethod]
    public void A2_EqualVectorsAndEqualFormsAreRowK4()
    {
        var f = new MergeFixture();
        var vv = Vv((Self, 3), (Peer, 2));
        f.Local("1", A, T("Genre", ("1", "A")), vv);
        var record = f.Pull(f.Record(A, T("Genre", ("9", "A")), vv, editedBy: PeerEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.AreEqual(record, BaseOf(r, A).Record);
        Assert.AreEqual(0, r.Notes.Count);
    }

    // ---- X ------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(DataSyncEntitySyncState.LocalOnly)]
    [DataRow(DataSyncEntitySyncState.Detached)]
    public void X_ARecordOfAnUnsyncedEntityIsExcluded(DataSyncEntitySyncState state)
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)), state: state);
        var record = f.Pull(f.Record(A, T("Genre 2"), Vv((Self, 1), (Peer, 1))));

        var r = f.Merge();
        var b = BaseOf(r, A);
        Assert.AreEqual(DataSyncBaseState.Excluded, b.State);
        Assert.AreEqual(DataSyncExclusionReason.NotSyncedHere, b.Exclusion);
        Assert.AreEqual(record, b.Record, "the record's keys join the exclusion index");
        Assert.AreEqual(0, Ops(r).Count);
    }

    [TestMethod]
    public void X_ATombstoneOfAnUnsyncedEntityIsExcludedToo()
    {
        var f = new MergeFixture();
        f.Tombstone(A, Vv((Self, 2)), stateAtDeletion: DataSyncEntitySyncState.LocalOnly);
        f.Pull(f.Record(A, T("Genre"), Vv((Peer, 4))));

        Assert.AreEqual(DataSyncExclusionReason.NotSyncedHere, BaseOf(f.Merge(), A).Exclusion);
    }

    // ---- F ------------------------------------------------------------------------------------

    [TestMethod]
    public void F_AnEntityHeldByTheLostUpdateGuardIsFrozen()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)), publishHeld: true);
        f.Pull(f.Record(A, T("Genre 2"), Vv((Self, 1), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(DataSyncPendingReason.PublishHeld, BaseOf(r, A).Pending!.Reason);
        Assert.AreEqual(0, Ops(r).Count + r.Revisions.Count + r.Inbox.Count);
        Assert.IsFalse(Evaluated(r, A));
    }

    // ---- K1–K3 --------------------------------------------------------------------------------

    [TestMethod]
    public void K1_ADominatingTombstoneDeletesADefinitionSyncCreatedWithNoValues()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Mood"), Vv((Peer, 1)), lastEditor: PeerEditor, createdBySync: true);
        f.ValueCounts[(ItemKind, "1")] = 0;
        var tombstone = f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        var delete = (DeleteEntityOperation)Ops(r).Single();
        Assert.AreEqual("1", delete.LocalKey);
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.AcceptRemoteDelete, revision.Revision);
        Assert.AreEqual(tombstone.Vv, revision.RemoteVv);
        Assert.AreEqual(tombstone, BaseOf(r, A).Record);
        Assert.AreEqual(DataSyncMergeNoteCodes.AutoDeleted, r.Notes.Single().Code);
        Assert.AreEqual(0, r.Inbox.Count);
    }

    [TestMethod]
    public void K1_OtherwiseADeletionIsAQuestion()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Mood"), Vv((Peer, 1)), lastEditor: PeerEditor);
        f.ValueCounts[(ItemKind, "1")] = 412;
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count);
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.DeletedThere, item.Type);
        Assert.AreEqual(DataSyncInboxItemOrigin.Merger, item.Origin);
        Assert.AreEqual(412, item.Payload.ValueCount);
        var b = BaseOf(r, A);
        Assert.AreEqual(DataSyncPendingReason.AwaitingDecision, b.Pending!.Reason);
        Assert.AreEqual(b.Pending.RecordHash, item.RecordHash, "the item refers to the pending record, never copies it");
    }

    [TestMethod]
    public void K2_AConcurrentEditWinsOverADeletion()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Mood 2"), Vv((Peer, 1), (Self, 5)), createdBySync: true);
        f.ValueCounts[(ItemKind, "1")] = 0;
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count + r.Inbox.Count + r.Revisions.Count);
        Assert.AreEqual(DataSyncMergeNoteCodes.EditWinsKept, r.Notes.Single().Code);
        Assert.IsTrue(Evaluated(r, A));
    }

    [TestMethod]
    public void K2_UnderFollowTheSourceWinsAsK1()
    {
        var f = new MergeFixture { Mode = DataSyncLinkMode.Follow };
        f.Local("1", A, T("Mood 2"), Vv((Peer, 1), (Self, 5)), createdBySync: true);
        f.ValueCounts[(ItemKind, "1")] = 0;
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        // Not dominated: §8.6 asks.
        Assert.AreEqual(DataSyncInboxItemType.DeletedThere, r.Inbox.Single().Type);
    }

    [TestMethod]
    public void K3_AnOlderTombstoneIsIgnored()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Mood"), Vv((Peer, 3), (Self, 5)));
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count + r.Inbox.Count + r.Notes.Count + r.BaseUpdates.Count);
    }

    // ---- K4–K6 --------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void K4_AnAncestorOrEqualRecordOnlyMovesTheBase(bool equal)
    {
        var f = new MergeFixture();
        var local = Vv((Peer, 2), (Self, 3));
        f.Local("1", A, T("Genre", ("1", "A")), local);
        var record = f.Pull(f.Record(A, T("Genre", ("1", "A")), equal ? local : Vv((Peer, 2)), aliases: [B]));

        var r = f.Merge();
        var b = BaseOf(r, A);
        Assert.AreEqual(record, b.Record);
        Assert.IsTrue(b.ClearPending);
        Assert.AreEqual(0, r.Revisions.Count);
        var bind = (BindOnlyOperation)Ops(r).Single();
        CollectionAssert.AreEqual(new[] { B }, bind.AliasKeysToAdd.All.ToArray(), "keys are recorded");
    }

    [TestMethod]
    public void K5_FastForwardTakesTheRecordAndClosesItemsAsResolvedElsewhere()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre", ("1", "Action")), Vv((Self, 3)), orderKey: "a0");
        f.Base(A, f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3)), orderKey: "a0"),
            childMap: new Dictionary<string, string> { ["1"] = "1" });
        f.UseAllChildren(0);
        var record = f.Pull(f.Record(A, T("Genres", ("1", "Action"), ("2", "Drama")), Vv((Self, 3), (Peer, 1)),
            orderKey: "a5", editedBy: ThirdEditor));

        var r = f.Merge();
        var update = (UpdateEntityOperation)Ops(r).Single();
        Assert.AreEqual(T("Genres", ("1", "Action"), ("2", "Drama")), Items.ReadLocal(update.MergedContent));
        CollectionAssert.AreEqual(new[] { "2" }, update.AddedChildIds.ToArray());
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.FastForward, revision.Revision);
        Assert.IsTrue(revision.ResultEqualsRemote);
        Assert.AreEqual("a5", revision.OrderKey);
        Assert.AreEqual(ThirdEditor, revision.AdoptEditor);
        Assert.AreEqual(record, BaseOf(r, A).Record);
        Assert.AreEqual("2", BaseOf(r, A).ChildMap!["2"]);
        var hint = r.ClosureHints.Single();
        Assert.AreEqual(DataSyncInboxClosure.ResolvedElsewhere, hint.Closure);
        Assert.AreEqual(ThirdEditor, hint.By);
        Assert.AreEqual("1", r.Order.Single().Synced.Single().LocalKey);
    }

    [TestMethod]
    public void K5_APeerTypeChangeFreezesTheEntity()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre", null, "MultipleChoice"), Vv((Self, 3)));
        f.ValueCounts[(ItemKind, "1")] = 1234;
        f.Pull(f.Record(A, T("Genres", null, "SingleChoice"), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count + r.Revisions.Count);
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.TypeChange, item.Type);
        Assert.AreEqual("type", item.SubjectPath);
        Assert.AreEqual("SingleChoice", item.Payload.RemoteSubtype);
        Assert.AreEqual("MultipleChoice", item.Payload.LocalSubtype);
        Assert.AreEqual(1234, item.Payload.ValueCount);
        Assert.AreEqual(DataSyncPendingReason.TypeChange, BaseOf(r, A).Pending!.Reason);
    }

    [TestMethod]
    public void K5_AMassChildDeletionFreezesTheEntity()
    {
        var f = new MergeFixture();
        var children = Enumerable.Range(1, 60).Select(i => (i.ToString(), "L" + i)).ToArray();
        f.Local("1", A, T("Tags", children), Vv((Self, 3)));
        f.UseAllChildren(0);
        f.Pull(f.Record(A, T("Tags", children[..5]), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count + r.Revisions.Count);
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.MassChildDeletion, item.Type);
        Assert.AreEqual(DataSyncInboxItemOrigin.State, item.Origin);
        Assert.AreEqual(55, item.Payload.ChildrenTotal);
        Assert.AreEqual(55, item.Payload.Children!.Count);
        Assert.AreEqual(DataSyncPendingReason.MassChildDeletion, BaseOf(r, A).Pending!.Reason);
    }

    [TestMethod]
    public void K6_DifferentPathsMergeWithoutAnItem()
    {
        var f = new MergeFixture();
        var baseRecord = f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3)));
        f.Base(A, baseRecord, childMap: new Dictionary<string, string> { ["1"] = "1" });
        f.Local("1", A, T("类型", ("1", "Action")), Vv((Self, 4)));
        f.UseAllChildren(0);
        var record = f.Pull(f.Record(A, T("Genre", ("1", "Action"), ("2", "Isekai")), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        var update = (UpdateEntityOperation)Ops(r).Single();
        Assert.AreEqual(T("类型", ("1", "Action"), ("2", "Isekai")), Items.ReadLocal(update.MergedContent));
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, r.Revisions.Single().Revision);
        Assert.IsFalse(r.Revisions.Single().ResultEqualsRemote);
        Assert.AreEqual(record, BaseOf(r, A).Record);
        Assert.AreEqual(0, r.Inbox.Count);
    }

    [TestMethod]
    public void K6_AConflictKeepsTheLocalValueAndWaits()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Artist", ("1", "X")), Vv((Self, 3))));
        f.Local("1", A, T("作者", ("1", "X")), Vv((Self, 4)));
        f.UseAllChildren(0);
        f.Pull(f.Record(A, T("Artists", ("1", "X"), ("2", "Y")), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        var update = (UpdateEntityOperation)Ops(r).Single();
        Assert.AreEqual(T("作者", ("1", "X"), ("2", "Y")), Items.ReadLocal(update.MergedContent), "safe fields apply");
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.MergedWithConflicts, revision.Revision);
        var b = BaseOf(r, A);
        Assert.IsNull(b.Record, "the base is unchanged");
        Assert.AreEqual(DataSyncPendingReason.Conflict, b.Pending!.Reason);
        Assert.AreEqual("2", b.ChildMap!["2"], "the child map learns what this merge added");
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, item.Type);
        Assert.AreEqual("name", item.SubjectPath);
        Assert.AreEqual("作者", item.Payload.Fields.Single().Local!.Text);
        Assert.AreEqual("Artists", item.Payload.Fields.Single().Remote!.Text);
    }

    [TestMethod]
    public void K6_UnderFollowThePeerWinsAndTheRevisionAddsACounter()
    {
        var f = new MergeFixture { Mode = DataSyncLinkMode.Follow };
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 3))));
        f.Local("1", A, T("作者"), Vv((Self, 4)));
        f.Pull(f.Record(A, T("Artists"), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(T("Artists"), Items.ReadLocal(((UpdateEntityOperation)Ops(r).Single()).MergedContent));
        Assert.AreEqual(DataSyncRevisionKind.FollowMerged, r.Revisions.Single().Revision);
        Assert.AreEqual(DataSyncMergeNoteCodes.FollowOverride, r.Notes.Single().Code);
        Assert.AreEqual(0, r.Inbox.Count);
    }

    [TestMethod]
    public void K6_UnderFollowAnotherDevicesValueIsNeverOverriddenSilently()
    {
        var f = new MergeFixture { Mode = DataSyncLinkMode.Follow };
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 3))));
        f.Local("1", A, T("作者"), Vv((Self, 3), (Third, 1)), lastEditor: ThirdEditor);
        f.Pull(f.Record(A, T("Artists"), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, r.Inbox.Single().Type);
    }

    [TestMethod]
    public void K5_AnInUseChildThePeerDeletedIsHeldAndThePublishedFormEqualsThePeers()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre", ("1", "Action"), ("2", "Horror")), Vv((Self, 3))));
        f.Local("1", A, T("Genre", ("1", "Action"), ("2", "Horror")), Vv((Self, 3)));
        f.Usage[(ItemKind, "1")] = new Dictionary<string, int> { ["1"] = 0, ["2"] = 30 };
        f.Pull(f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "the held child stays: nothing to write");
        Assert.AreEqual(new DataSyncHeldChild("2", LinkId), r.OverlayChanges.Single().Hold.Single());
        // The held child is not published, so this device's form equals the peer's: nothing ping-pongs.
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.FastForward, revision.Revision);
        Assert.IsTrue(revision.ResultEqualsRemote);
        Assert.AreEqual(DataSyncInboxItemType.ChildDeletedInUse, r.Inbox.Single().Type);
    }

    [TestMethod]
    public void K6_AChildThePeerDeletedThatIsInUseHereIsHeld()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre", ("1", "Action"), ("2", "Horror")), Vv((Self, 3))),
            childMap: new Dictionary<string, string> { ["1"] = "1", ["2"] = "2" });
        f.Local("1", A, T("Genre!", ("1", "Action"), ("2", "Horror")), Vv((Self, 4)));
        f.Usage[(ItemKind, "1")] = new Dictionary<string, int> { ["1"] = 0, ["2"] = 30 };
        f.Pull(f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        var overlay = r.OverlayChanges.Single();
        Assert.AreEqual(new DataSyncHeldChild("2", LinkId), overlay.Hold.Single());
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.ChildDeletedInUse, item.Type);
        Assert.AreEqual(DataSyncInboxItemOrigin.State, item.Origin);
        Assert.AreEqual("child:2", item.SubjectPath);
        Assert.AreEqual(30, item.Payload.UsageCount);
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, r.Revisions.Single().Revision);
    }

    [TestMethod]
    public void K6_WithoutABaseADifferentSubtypeIsATypeChange()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Notes", null, "SingleLineText"), Vv((Self, 3)));
        f.Pull(f.Record(A, T("Notes", null, "MultilineText"), Vv((Peer, 2))));

        Assert.AreEqual(DataSyncInboxItemType.TypeChange, f.Merge().Inbox.Single().Type);
    }

    [TestMethod]
    public void K6_BothChangedTheTypeDifferently()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre", null, "MultipleChoice"), Vv((Self, 3))));
        f.Local("1", A, T("Genre", null, "Tags"), Vv((Self, 4)));
        f.Pull(f.Record(A, T("Genre", null, "SingleChoice"), Vv((Self, 3), (Peer, 1))));

        var item = f.Merge().Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.TypeChange, item.Type);
        Assert.AreEqual("MultipleChoice", item.Payload.Fields.Single().Base!.Text);
    }

    [TestMethod]
    public void K6_ALocalTypeChangeIsKept()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre", null, "MultipleChoice"), Vv((Self, 3))));
        f.Local("1", A, T("Genre", null, "Tags"), Vv((Self, 4)));
        f.Pull(f.Record(A, T("Genres", null, "MultipleChoice"), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(0, r.Inbox.Count);
        Assert.AreEqual(T("Genres", null, "Tags"), Items.ReadLocal(((UpdateEntityOperation)Ops(r).Single()).MergedContent));
    }

    // ---- T0–T3 --------------------------------------------------------------------------------

    [TestMethod]
    public void T0_AnIncludedUndoneCreateIsRevivedWithItsKey()
    {
        var f = new MergeFixture();
        var tombstone = f.Tombstone(A, Vv((Peer, 2), (Self, 6)), DataSyncTombstoneKind.UndoneCreate, served: false);
        var record = f.Pull(f.Record(A, T("Genre", ("1", "A")), Vv((Peer, 2)), orderKey: "a1"));

        var r = f.Merge();
        var create = (CreateEntityOperation)Ops(r).Single();
        Assert.AreEqual(A, create.Keys.Primary);
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.Revive, revision.Revision);
        Assert.AreEqual(tombstone.Vv, revision.TombstoneVv);
        Assert.AreEqual(record, BaseOf(r, A).Record);
    }

    [TestMethod]
    public void T1_BothDeleted()
    {
        var f = new MergeFixture();
        f.Tombstone(A, Vv((Self, 2)));
        var record = f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        Assert.AreEqual(record, BaseOf(r, A).Record);
        Assert.AreEqual(0, Ops(r).Count + r.Inbox.Count);
    }

    [TestMethod]
    public void T2_AnOlderLiveRecordIsIgnoredAndAnUnservedTombstoneIsServedAgain()
    {
        var f = new MergeFixture();
        f.Tombstone(A, Vv((Peer, 2), (Self, 4)), served: false);
        f.Pull(f.Record(A, T("Genre"), Vv((Peer, 2))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count + r.Inbox.Count);
        Assert.AreEqual((ItemKind, A), r.TombstonesToServe!.Single());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void T3_ANewerOrConcurrentLiveRecordAsks(bool restored)
    {
        var f = new MergeFixture();
        var tombstone = f.Tombstone(A, Vv((Peer, 2), (Self, 4)));
        f.Pull(f.Record(A, T("Mood"), restored ? Vv((Peer, 5), (Self, 4)) : Vv((Peer, 3))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "never an automatic revive");
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.DeletedHereEditedThere, item.Type);
        Assert.AreEqual(restored ? DataSyncInboxDrafts.DetailRestored : DataSyncInboxDrafts.DetailChangedAfterDelete,
            item.Payload.Detail);
        Assert.AreEqual(tombstone.Vv, item.LocalVv);
        Assert.AreEqual(DataSyncPendingReason.AwaitingDecision, BaseOf(r, A).Pending!.Reason);
        Assert.AreEqual(20, BaseOf(r, A).Pending!.EvaluatedAtLocalSeq, "the tombstone row's Seq");
    }

    [TestMethod]
    public void T3_TwoLineagesAskingAboutOneTombstoneAreOneQuestion()
    {
        // This device had merged two of the peer's lineages (A and B) into one entity, then deleted it; the peer still
        // publishes both, each changed since.
        var f = new MergeFixture();
        var tombstone = f.Tombstone(A, Vv((Peer, 2), (Self, 4)), aliases: [B]);
        var a = f.Pull(f.Record(A, T("Mood"), Vv((Peer, 5))));
        var b = f.Pull(f.Record(B, T("Feeling"), Vv((Peer, 6))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "never an automatic revive");
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.DeletedHereEditedThere, item.Type);
        Assert.AreEqual(A, item.Key, "the tombstone's key");
        CollectionAssert.AreEqual(new[] { A.Value, B.Value }, item.Payload.Records!.Select(x => x.PrimaryKey).ToArray());
        Assert.AreEqual(Vv((Peer, 6)), item.RecordVv, "the records' vectors combined");
        Assert.AreEqual(tombstone.Vv, item.LocalVv);

        // Every record waits once, on its own row: none is lost, and no row holds two.
        var onTombstone = BaseOf(r, A);
        Assert.AreEqual(a, onTombstone.Pending!.Record);
        Assert.AreEqual(DataSyncPendingReason.AwaitingDecision, onTombstone.Pending.Reason);
        var own = BaseOf(r, B);
        Assert.AreEqual(b, own.Pending!.Record);
        Assert.AreEqual(DataSyncBaseState.Unbound, own.State);
        Assert.AreEqual(DataSyncInboxDrafts.CombinedRecordHash([onTombstone.Pending.RecordHash, own.Pending.RecordHash]),
            item.RecordHash);

        var reconciled = DataSyncInboxRules.Reconcile([], r.Inbox, r.Evaluated, r.ClosureHints);
        Assert.AreEqual(1, reconciled.Upserts.Count, "one item for one subject");

        // Delivered again (both records waiting as they were stored), the same question and the same rows.
        var again = new MergeFixture();
        again.Tombstone(A, Vv((Peer, 2), (Self, 4)), aliases: [B]);
        again.Base(A, null, pending: onTombstone.Pending);
        again.Base(B, null, DataSyncBaseState.Unbound, pending: own.Pending);
        again.PendingToMerge.AddRange([(ItemKind, A), (ItemKind, B)]);
        again.NoPull = true;
        var r2 = again.Merge();
        Assert.AreEqual(item.Token, r2.Inbox.Single().Token);
        Assert.AreEqual(item.RecordHash, r2.Inbox.Single().RecordHash);
        Assert.AreEqual(a, BaseOf(r2, A).Pending!.Record);
        Assert.AreEqual(b, BaseOf(r2, B).Pending!.Record);
    }

    // ---- I -------------------------------------------------------------------------------------

    [TestMethod]
    public void I_OneRecordMatchingTwoEntitiesAsks()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Artist", null, "Text"), Vv((Self, 1)));
        f.Local("2", B, T("Author", null, "Number"), Vv((Self, 2)));
        f.Pull(f.Record(C, T("Artist", null, "Text"), Vv((Peer, 1)), aliases: [A, B]));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count);
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.IdentityConflict, item.Type);
        Assert.AreEqual(C, item.Key);
        CollectionAssert.AreEqual(new[] { true, false }, item.Payload.Candidates!.Select(x => x.Updatable).ToArray(),
            "a candidate of another type cannot be chosen");
        var b = BaseOf(r, C);
        Assert.AreEqual(DataSyncBaseState.Unbound, b.State);
        Assert.AreEqual(DataSyncPendingReason.IdentityConflict, b.Pending!.Reason);
    }

    [TestMethod]
    public void I_ARecordWhosePrimaryKeysABaseRowWaitsOnThatRowWithItsStateUnchanged()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Artist"), Vv((Self, 1)));
        f.Local("2", B, T("Author"), Vv((Self, 2)));
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 1))), DataSyncBaseState.MissingAtPeer);
        f.Pull(f.Record(A, T("Artist"), Vv((Peer, 1)), aliases: [B]));

        var b = BaseOf(f.Merge(), A);
        Assert.AreEqual(DataSyncBaseState.MissingAtPeer, b.State);
        Assert.AreEqual(DataSyncPendingReason.IdentityConflict, b.Pending!.Reason);
    }

    // ---- N1–N3 --------------------------------------------------------------------------------

    [TestMethod]
    public void N1_ATombstoneOfSomethingNeverKnownIsIgnored()
    {
        var f = new MergeFixture();
        f.Pull(f.Record(A, null, Vv((Peer, 1)), deleted: true));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count + r.BaseUpdates.Count + r.Inbox.Count);
    }

    [TestMethod]
    public void N2_ANameMatchIsASuggestionNeverALink()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Rating"), Vv((Self, 1)));
        f.Pull(f.Record(B, T("rating"), Vv((Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count);
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.LinkSuggestion, item.Type);
        Assert.AreEqual(DataSyncNaturalMatch.Similar, item.Payload.Candidates!.Single().Match);
        CollectionAssert.AreEquivalent(
            new[] { DataSyncInboxAction.Link, DataSyncInboxAction.KeepBoth, DataSyncInboxAction.Skip },
            DataSyncInboxRules.AllowedActions(item.Type, item.SubjectPath, item.Payload, false).ToArray());
        var b = BaseOf(r, B);
        Assert.AreEqual(DataSyncBaseState.Unbound, b.State);
        Assert.AreEqual(DataSyncPendingReason.AwaitingDecision, b.Pending!.Reason);
    }

    [TestMethod]
    public void N2_AnIdenticalExtensionGroupWithAUniqueCandidateLinksByItself()
    {
        var f = new MergeFixture();
        var video = new ExtensionGroupContentV1("Video", [".mkv", ".mp4"]);
        f.Local("5", A, video, Vv((Self, 1)), kind: GroupKind);
        var record = f.Pull(f.Record(B, video, Vv((Peer, 1)), kind: GroupKind), GroupKind);

        var r = f.Merge();
        var bind = (BindOnlyOperation)Ops(r).Single();
        Assert.AreEqual("5", bind.LocalKey);
        CollectionAssert.AreEqual(new[] { B }, bind.AliasKeysToAdd.All.ToArray());
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, revision.Revision);
        Assert.IsTrue(revision.ResultEqualsRemote);
        Assert.AreEqual(record, BaseOf(r, A, GroupKind).Record);
        Assert.AreEqual(0, r.Inbox.Count);
    }

    [TestMethod]
    public void N3_NoMatchCreates()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Other"), Vv((Self, 1)), orderKey: "a0");
        var record = f.Pull(f.Record(B, T("Genre", ("p1", "Action")), Vv((Peer, 1)), orderKey: "a5", aliases: [C]));

        var r = f.Merge();
        var create = (CreateEntityOperation)Ops(r).Single();
        CollectionAssert.AreEqual(new[] { B, C }, create.Keys.All.ToArray());
        Assert.AreEqual(PeerNode, create.OriginNodeId);
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.Create, revision.Revision);
        Assert.IsTrue(revision.ResultEqualsRemote);
        Assert.AreEqual(PeerEditor, revision.AdoptEditor);
        Assert.AreEqual(record, BaseOf(r, B).Record);
        Assert.AreEqual("p1", BaseOf(r, B).ChildMap!["p1"]);
        CollectionAssert.AreEqual(new[] { "1", DataSyncMergeItemIds.Of(ItemKind, B) },
            r.Order.Single().Synced.Select(s => s.LocalKey).ToArray(), "the create is placed by its order key");
    }
}

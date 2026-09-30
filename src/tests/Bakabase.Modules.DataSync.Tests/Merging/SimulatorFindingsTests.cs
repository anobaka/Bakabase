using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>
/// One regression test per engine defect the retired in-memory convergence simulator found (§13.3), reduced to a
/// single merge. The convergence property test now runs on the real stack (<c>Bakabase.Tests</c>).
/// </summary>
[TestClass]
public class SimulatorFindingsTests
{
    private static readonly SyncKey A = K(0xa), B = K(0xb), C = K(0xc), D = K(0xd);

    /// <summary>An editor whose revision this device produced under an actor it has since retired (§5.6).</summary>
    private static readonly DataSyncEditorRef RetiredEditor = new(SelfNode, "This PC", Retired.Value);

    private static DataSyncBaseUpdate BaseOf(DataSyncMergeResult r, SyncKey key, string kind = ItemKind) =>
        r.BaseUpdates.Single(u => u.Kind == kind && u.Key == key);

    private static IReadOnlyList<ApplyOperation> Ops(DataSyncMergeResult r) =>
        r.Batches.SelectMany(b => b.Operations).ToList();

    private static MergeFixture WithRetiredActor() => new() { RetiredCounters = { [Retired.Value] = 5 } };

    // ---- identity questions ----------------------------------------------------------------------

    [TestMethod]
    public void RowI_TheRecordWaitsOnItsOwnersRow()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Artist"), Vv((Self, 1)));
        f.Local("2", B, T("Author"), Vv((Self, 2)));
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 1))));
        f.Pull(f.Record(A, T("Artist"), Vv((Self, 1), (Peer, 1)), aliases: [B]));

        var pending = BaseOf(f.Merge(), A).Pending!;
        Assert.AreEqual(DataSyncPendingReason.IdentityConflict, pending.Reason);
    }

    [TestMethod]
    public void AnIdenticalExtensionGroupNeverLinksByItselfOntoAnEntityAlreadyAgreedOnThisLink()
    {
        var f = new MergeFixture();
        var video = new ExtensionGroupContentV1("Video", [".mkv", ".mp4"]);
        f.Local("5", A, video, Vv((Self, 1)), kind: GroupKind);
        f.Base(A, f.Record(A, video, Vv((Self, 1)), kind: GroupKind), kind: GroupKind);
        f.Pull(f.Record(B, video, Vv((Peer, 1)), kind: GroupKind), GroupKind);

        var r = f.Merge();
        Assert.IsFalse(Ops(r).OfType<BindOnlyOperation>().Any(), "two of the peer's records would bind to one entity");
        Assert.AreEqual(DataSyncInboxItemType.LinkSuggestion, r.Inbox.Single().Type);
        Assert.AreEqual(DataSyncBaseState.Unbound, BaseOf(r, B, GroupKind).State);
    }

    [TestMethod]
    public void AKeyADetachedEntityOwnsIsNeverProposedAsAnAlias()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Mood"), Vv((Peer, 2)));
        f.Local("2", B, T("Mood"), Vv((Peer, 1)), state: DataSyncEntitySyncState.Detached);
        f.Base(A, f.Record(A, T("Mood"), Vv((Peer, 2))));
        f.Pull(f.Record(A, T("Mood!"), Vv((Peer, 3)), aliases: [B]));

        var r = f.Merge();
        var update = (UpdateEntityOperation)Ops(r).Single();
        Assert.AreEqual(0, update.AliasKeysToAdd.All.Count(),
            "B is live elsewhere: the identity pre-flight would refuse it at every apply");
        Assert.AreEqual(DataSyncRevisionKind.FastForward, r.Revisions.Single().Revision);
    }

    [TestMethod]
    public void AnAliasThatChangesTheTieKeyPlacesTheEntityAgain()
    {
        // Two definitions share the order key "a0"; the tie key (the smallest key) puts Series (B) before Mood (C).
        var f = new MergeFixture();
        f.Local("1", C, T("Mood"), Vv((Self, 1)), orderKey: "a0");
        f.Local("2", B, T("Series"), Vv((Peer, 1)), orderKey: "a0");
        // The peer linked Mood with its own lineage A: A becomes Mood's smallest key, and Mood comes first.
        f.Pull(f.Record(C, T("Mood"), Vv((Self, 1), (Peer, 2)), orderKey: "a0", aliases: [A]));

        var order = f.Merge().Order.Single();
        CollectionAssert.AreEqual(new[] { "1", "2" }, order.Synced.Select(s => s.LocalKey).ToArray(),
            "without a Place the next Refresh reads the old local order as a move and issues a revision");
    }

    // ---- conflicts merged again ---------------------------------------------------------------------

    [TestMethod]
    public void AConflictRecordMergedAgainDerivesItsItemsAndAppliesNothing()
    {
        var f = new MergeFixture { NoPull = true };
        var baseRecord = f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 1)));
        var record = f.Record(A, T("Genre R", ("1", "Action"), ("2", "Drama")), Vv((Self, 1), (Peer, 1)));
        f.Base(A, baseRecord, childMap: new Dictionary<string, string> { ["1"] = "1" },
            pending: PendingOf(record, DataSyncPendingReason.Conflict));
        // The first merge added "Drama"; another link's merge has removed it since.
        f.Local("1", A, T("Genre L", ("1", "Action")), Vv((Self, 3), (Third, 1)));
        f.PendingToMerge.Add((ItemKind, A));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "a conflict freezes the entity");
        Assert.AreEqual(0, r.Revisions.Count);
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, item.Type);
        Assert.AreEqual("name", item.SubjectPath);
        var pending = BaseOf(r, A).Pending!;
        Assert.AreEqual(DataSyncPendingReason.Conflict, pending.Reason);
        Assert.AreEqual(DataSyncPendingRecords.RecordHashOf(record), pending.RecordHash);
    }

    [TestMethod]
    public void UnderFollowAValueFromAnotherDeviceAsksAndNothingOfTheRecordApplies()
    {
        // The name came from a third device, so Follow asks instead of overriding it (§8.5.2's exception).
        var f = new MergeFixture { Mode = DataSyncLinkMode.Follow };
        f.Local("1", A, T("Genre!"), Vv((Third, 2)), lastEditor: ThirdEditor);
        f.Pull(f.Record(A, T("Genre", ("1", "Action")), Vv((Third, 1), (Peer, 1))));
        var first = f.Merge();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, first.Inbox.Single().Type);
        Assert.AreEqual(0, Ops(first).Count + first.Revisions.Count, "the option waits with the name");
    }

    // ---- collisions (a retired actor's counter issued twice) -----------------------------------------

    [TestMethod]
    public void JudgeEqualVectors_ARetiredOwnActorsRevisionIsACollision()
    {
        Assert.AreEqual(DataSyncAnomalies.Collision, DataSyncAnomalies.JudgeEqualVectors(false, Retired.Value, Self,
            Peer.Value, [Retired.Value]));
        Assert.AreEqual(DataSyncAnomalies.Drift, DataSyncAnomalies.JudgeEqualVectors(false, Retired.Value, Self,
            Peer.Value), "without the retired actors it reads as a relayed revision");
        Assert.IsNull(DataSyncAnomalies.JudgeEqualVectors(true, Retired.Value, Self, Peer.Value, [Retired.Value]));
        Assert.AreEqual(DataSyncAnomalies.DuplicateActor, DataSyncAnomalies.JudgeEqualVectors(false, Self.Value, Self,
            Peer.Value, [Retired.Value]), "the current actor is a duplicate actor as before");
    }

    [TestMethod]
    public void ACollisionMergesAsConcurrentAndItsQuestionKeepsNoRecordVector()
    {
        var f = WithRetiredActor();
        var vv = Vv((Retired, 3), (Peer, 1));
        f.Local("1", A, T("Genre L"), vv);
        f.Base(A, f.Record(A, T("Genre"), Vv((Retired, 2), (Peer, 1))));
        f.Pull(f.Record(A, T("Genre R"), vv, editedBy: RetiredEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.IsNull(r.Pause);
        Assert.AreEqual(0, r.Notes.Count, "not drift: the two contents are two versions");
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, item.Type);
        Assert.IsNull(item.RecordVv, "the entity's vector equals the record's: dominance would close it unseen");
        Assert.AreEqual(DataSyncPendingReason.Conflict, BaseOf(r, A).Pending!.Reason);
    }

    [TestMethod]
    public void ACollisionOfALiveEntityWithThePeersDeletionKeepsItUnderAFreshCounter()
    {
        var f = WithRetiredActor();
        var vv = Vv((Retired, 3));
        f.Local("1", A, T("Genre"), vv);
        f.Base(A, f.Record(A, T("Genre"), Vv((Retired, 2))));
        f.Pull(f.Record(A, null, vv, deleted: true, editedBy: RetiredEditor));

        var r = f.Merge();
        Assert.IsFalse(Ops(r).OfType<DeleteEntityOperation>().Any(), "K2: the live version wins");
        var revision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.LocalEdit, revision.Revision,
            "reissued, so the deleting device meets it as newer (row T3) instead of equal to its tombstone");
    }

    [TestMethod]
    public void ACollisionOfALiveRecordWithThisDevicesTombstoneAsks()
    {
        var f = WithRetiredActor();
        var vv = Vv((Retired, 3));
        f.Tombstone(A, vv);
        f.Pull(f.Record(A, T("Genre"), vv, editedBy: RetiredEditor));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count);
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.DeletedHereEditedThere, item.Type, "row T3, not T2");
        Assert.IsNull(item.RecordVv);
        Assert.IsTrue(r.TombstonesToServe is null or { Count: 0 }, "nothing is served again for a question");
    }

    [TestMethod]
    public void EqualVectorsOnADeletionAndALiveVersionFromAThirdDeviceAreNeverDrift()
    {
        var f = new MergeFixture();
        var vv = Vv((Third, 3));
        f.Tombstone(A, vv);
        f.Pull(f.Record(A, T("Genre"), vv, editedBy: ThirdEditor));

        var r = f.Merge();
        Assert.IsNull(r.Anomaly);
        Assert.AreEqual(0, r.Notes.Count(n => n.Code == DataSyncMergeNoteCodes.NormalizationChanged),
            "drift re-records a live base; against a tombstone rows T decide");
    }

    // ---- a merge without a base ---------------------------------------------------------------------

    [TestMethod]
    public void ANoBaseUnionThatBringsBackALocalRemovalAddsThisDevicesCounter()
    {
        // This device removed "Comedy" (Self 2); the peer's version descends from the one before (Self 1) and this
        // device has no base with that peer. The union brings "Comedy" back: the result equals the peer's form.
        var f = new MergeFixture();
        var localVv = Vv((Self, 2));
        f.Local("1", A, T("Genre", ("3", "Mecha"), ("4", "Slice")), localVv);
        var record = f.Pull(f.Record(A, T("Genre", ("3", "Mecha"), ("4", "Slice"), ("5", "Comedy")),
            Vv((Self, 1), (Peer, 4))));

        var r = f.Merge();
        var decision = r.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, decision.Revision);
        Assert.IsTrue(decision.ResultEqualsRemote);
        Assert.IsTrue(decision.SeenBoth);

        // Applied, the vector is not a bare Max: a device that merged the same two versions with a base kept the
        // removal under Max(Self 2, Peer 4), and one vector must never carry both contents.
        var merged = Items.ReadLocal(((UpdateEntityOperation)Ops(r).Single()).MergedContent);
        var counter = 100L;
        var applied = DataSyncRecordApply.Revise(Items, decision, localVv,
            DataSyncPublication.Of(Items, T("Genre", ("3", "Mecha"), ("4", "Slice")), DataSyncOverlay.None, false, null).SharedHash,
            merged, DataSyncOverlay.None, DataSyncPublication.SharedHashOfRecord(Items, record, f.Limits), PeerEditor,
            SelfEditor, Self,
            () => ++counter);
        Assert.AreEqual(Vv((Self, 101), (Peer, 4)), applied.Vv);
        Assert.AreEqual(SelfEditor, applied.LastEditor);

        // With a base, the same pair keeps the removal and needs no counter of its own.
        var g = new MergeFixture();
        g.Local("1", A, T("Genre", ("3", "Mecha"), ("4", "Slice")), localVv);
        g.Base(A, g.Record(A, T("Genre", ("3", "Mecha"), ("4", "Slice"), ("5", "Comedy")), Vv((Self, 1))),
            childMap: new Dictionary<string, string> { ["3"] = "3", ["4"] = "4", ["5"] = "5" });
        g.Pull(g.Record(A, T("Genre!", ("3", "Mecha"), ("4", "Slice"), ("5", "Comedy")), Vv((Self, 1), (Peer, 4))));
        Assert.IsFalse(g.Merge().Revisions.Single().SeenBoth);
    }

    [TestMethod]
    public void AThreeWayMergeAgainstAnOlderBaseThatTakesThePeersContentAddsThisDevicesCounter()
    {
        // Restored from a backup, this device asserted its old name again (RestoreWins, Self 7) while its base with
        // the peer is from before a rename it had made and lost; the peer relays that rename. Against the old base
        // the rename looks new, so the result is the peer's: a device whose base is the rename keeps the restored
        // name under Max of the same two vectors, so this side must not end at a bare Max.
        var f = new MergeFixture();
        f.Local("1", A, T("Mood"), Vv((Self, 7), (Retired, 2)));
        f.Base(A, f.Record(A, T("Mood"), Vv((Retired, 1))));
        f.Pull(f.Record(A, T("Mood+"), Vv((Retired, 2), (Peer, 3))));

        var decision = f.Merge().Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, decision.Revision);
        Assert.IsTrue(decision.ResultEqualsRemote);
        Assert.IsTrue(decision.SeenBoth);

        // Equal on both sides (example D of §9.1) stays a bare Max.
        var d = new MergeFixture();
        d.Local("1", A, T("Mood", ("1", "Action")), Vv((Self, 2)));
        d.Base(A, d.Record(A, T("Mood"), Vv((Self, 1))));
        d.Pull(d.Record(A, T("Mood", ("9", "Action")), Vv((Self, 1), (Peer, 1))));
        var equal = d.Merge().Revisions.SingleOrDefault();
        Assert.IsTrue(equal is null || !equal.SeenBoth);
    }

    [TestMethod]
    public void ARecordWhoseKeysRetireATombstoneIsComparedWithTheVectorTheEntityWillHold()
    {
        // This device deleted its copy of the peer's "Image" (A) and renamed its own group (B); the peer linked the
        // two and publishes [A, B] under a vector this device's deletion and rename both cover.
        var f = new MergeFixture();
        f.Local("1", B, T("Archive!"), Vv((Self, 4)));
        f.Tombstone(A, Vv((Peer, 1), (Self, 3)));
        var record = f.Pull(f.Record(A, T("Image"), Vv((Peer, 1), (Self, 1)), aliases: [B]));

        var r = f.Merge();
        var bind = (BindOnlyOperation)Ops(r).Single();
        CollectionAssert.AreEqual(new[] { A }, bind.AliasKeysToAdd.All.ToArray(), "A's tombstone retires into B");
        Assert.AreEqual(0, r.Inbox.Count, "an ancestor once the tombstone's history is B's: no conflict on the first delivery either");
        Assert.AreEqual(record, BaseOf(r, B).Record);

        // Concurrent after all, and B has no base on this link: the tombstone's base becomes B's when it retires, so
        // the merge is three-way against it already — the rename here is kept, not asked about.
        var g = new MergeFixture();
        g.Local("1", B, T("Archive!"), Vv((Self, 4)));
        g.Tombstone(A, Vv((Peer, 1), (Self, 3)));
        g.Base(A, g.Record(A, T("Image"), Vv((Peer, 1))));
        g.Pull(g.Record(A, T("Image", ("1", "Action")), Vv((Peer, 5), (Self, 1)), aliases: [B]));
        var merged = g.Merge();
        Assert.AreEqual(0, merged.Inbox.Count);
        var decision = merged.Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.MergedNoConflict, decision.Revision);
        var content = (TestKinds.TestItemContent)Items.ReadLocal(((UpdateEntityOperation)Ops(merged).Single()).MergedContent);
        Assert.AreEqual("Archive!", content.Name);
        Assert.AreEqual(1, content.Children.Count, "and the peer's option arrives");
    }

    // ---- rows T -------------------------------------------------------------------------------------

    [TestMethod]
    public void T1_TheTombstoneTakesThePeersDeletionHistory()
    {
        var f = new MergeFixture();
        var tombstone = f.Tombstone(A, Vv((Self, 2)));
        var record = f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var revision = f.Merge().Revisions.Single();
        Assert.AreEqual(DataSyncRevisionKind.AcceptRemoteDelete, revision.Revision);
        Assert.IsNull(revision.LocalKey, "a tombstone has no local entity");
        Assert.AreEqual(tombstone.Keys, revision.Keys);
        Assert.AreEqual(record.Vv, revision.RemoteVv);

        var covered = new MergeFixture();
        covered.Tombstone(A, Vv((Self, 2), (Peer, 2)));
        covered.Pull(covered.Record(A, null, Vv((Peer, 2)), deleted: true));
        Assert.AreEqual(0, covered.Merge().Revisions.Count, "nothing to take when the tombstone has it");
    }

    [TestMethod]
    public void T2_ATombstoneIsServedAgainOncePerRecordThePeerStillPublishes()
    {
        var f = new MergeFixture();
        f.Tombstone(A, Vv((Peer, 2), (Self, 4)), served: true);
        var record = f.Pull(f.Record(A, T("Genre"), Vv((Peer, 2))));

        var r = f.Merge();
        Assert.AreEqual((ItemKind, A), r.TombstonesToServe!.Single(),
            "the peer may have read past the tombstone before it knew the entity");
        Assert.AreEqual(record, BaseOf(r, A).Record);

        var again = new MergeFixture();
        again.Tombstone(A, Vv((Peer, 2), (Self, 4)), served: true);
        again.Base(A, record);
        again.Pull(record);
        var second = again.Merge();
        Assert.IsTrue(second.TombstonesToServe is null or { Count: 0 }, "the same record delivered again serves nothing");
        Assert.AreEqual(0, Ops(second).Count + second.Inbox.Count);
    }

    [TestMethod]
    public void T2_NoServingAgainWhileAnotherRecordOfTheTombstoneAsks()
    {
        // One tombstone, two of the peer's lineages: A was edited there after the deletion (T3 asks), B is older.
        var f = new MergeFixture();
        f.Tombstones[ItemKind] =
        [
            new DataSyncTombstoneState(new EntityKeys([A, B]), Vv((Peer, 2), (Self, 4)), SelfEditor,
                DataSyncEntitySyncState.Synced, DataSyncTombstoneKind.Deleted, true),
        ];
        f.Pull(f.Record(A, T("Archive (PC-4)"), Vv((Peer, 5))));
        f.Pull(f.Record(B, T("Archive"), Vv((Peer, 2))));

        var r = f.Merge();
        Assert.AreEqual(DataSyncInboxItemType.DeletedHereEditedThere, r.Inbox.Single().Type);
        Assert.IsTrue(r.TombstonesToServe is null or { Count: 0 },
            "the answer decides what the peer receives; serving meanwhile repeats at every delivery");
        Assert.AreEqual(DataSyncPendingReason.AwaitingDecision, BaseOf(r, A).Pending!.Reason,
            "the question keeps its pending record on the shared base row");
    }

    [TestMethod]
    public void T2_TakesTheSharedRowOverADeletionOfAnotherLineageAndServesOncePerDelivery()
    {
        // The peer deleted lineage B and still publishes lineage A, older than this device's deletion of both.
        DataSyncTombstoneState Tombstone() => new(new EntityKeys([A, B]), Vv((Peer, 2), (Self, 4)), SelfEditor,
            DataSyncEntitySyncState.Synced, DataSyncTombstoneKind.Deleted, false);
        var f = new MergeFixture();
        f.Tombstones[ItemKind] = [Tombstone()];
        var live = f.Pull(f.Record(A, T("Studio"), Vv((Peer, 1))));
        var deleted = f.Pull(f.Record(B, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        Assert.AreEqual((ItemKind, A), r.TombstonesToServe!.Single(), "unserved by retention: the peer must receive it");
        Assert.AreEqual(live, BaseOf(r, A).Record, "the live record's agreement owns the row, whatever the order");

        var again = new MergeFixture();
        again.Tombstones[ItemKind] = [Tombstone() with { Served = true }];
        again.Base(A, live);
        again.Pull(live);
        again.Pull(deleted);
        Assert.IsTrue(again.Merge().TombstonesToServe is null or { Count: 0 }, "the same delivery again serves nothing");
    }

    // ---- pending records a pull replaces ------------------------------------------------------------

    [TestMethod]
    public void APullRecordMeetsTheOtherRecordOfAWaitingIdentityQuestion()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Artist"), Vv((Self, 1)), aliases: [B]);
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 1))));
        // Row M asked last time: B's record waits for the answer under its own key.
        f.Base(B, null, DataSyncBaseState.Unbound,
            pending: PendingOf(f.Record(B, T("Author"), Vv((Peer, 3))), DataSyncPendingReason.IdentityConflict));
        f.Pull(f.Record(A, T("Artist!"), Vv((Self, 1), (Peer, 4))));

        var r = f.Merge();
        Assert.AreEqual(0, Ops(r).Count, "A's record does not merge into the entity as if B's did not exist");
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.IdentityConflict, item.Type);
        CollectionAssert.AreEqual(new[] { A.Value, B.Value }, item.Payload.Records!.Select(x => x.PrimaryKey).ToArray());
    }

    [TestMethod]
    public void APullRecordClearsItsLineagesPendingRecordWaitingOnAnotherBaseRow()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        f.Base(A, f.Record(A, T("Genre"), Vv((Self, 1))));
        // Earlier, B waited as a name match; the peer has since linked B into A.
        f.Base(B, null, DataSyncBaseState.Unbound,
            pending: PendingOf(f.Record(B, T("genre"), Vv((Peer, 1))), DataSyncPendingReason.AwaitingDecision));
        f.Pull(f.Record(A, T("Genre"), Vv((Self, 1), (Peer, 2)), aliases: [B]));

        var r = f.Merge();
        var stale = BaseOf(r, B);
        Assert.IsNull(stale.Pending);
        Assert.IsTrue(stale.ClearPending);
    }

    [TestMethod]
    public void APendingRecordIsNotReMergedWhenThePullCarriesItsKeyAsAnAlias()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        f.Base(A, f.Record(A, T("Genre"), Vv((Self, 1))),
            pending: PendingOf(f.Record(A, T("Genre?"), Vv((Self, 1), (Peer, 1))), DataSyncPendingReason.Retry));
        f.PendingToMerge.Add((ItemKind, A));
        f.Pull(f.Record(C, T("Genre!"), Vv((Self, 1), (Peer, 3)), aliases: [A]));

        var r = f.Merge();
        var update = (UpdateEntityOperation)Ops(r).Single();
        Assert.AreEqual("Genre!", ((Tests.TestKinds.TestItemContent)Items.ReadLocal(update.MergedContent)).Name,
            "the newer record of the lineage, never the pending one");
    }

    // ---- reconciliation -----------------------------------------------------------------------------

    [TestMethod]
    public void AReMergedPendingRecordThePeerNoLongerOffersIsMissingThereAtOnce()
    {
        var f = new MergeFixture();
        f.FullReconciliation.Add(ItemKind);
        var agreed = f.Record(A, T("Genre"), Vv((Self, 1), (Peer, 1)));
        f.Local("1", A, T("Genre"), Vv((Self, 1), (Peer, 1)));
        f.Base(A, f.Record(A, T("Genre"), Vv((Self, 1))), pending: PendingOf(agreed, DataSyncPendingReason.Retry));
        f.PendingToMerge.Add((ItemKind, A));
        f.Local("2", C, T("Other"), Vv((Peer, 1)));
        f.Base(C, f.Record(C, T("Other"), Vv((Peer, 1))));
        f.Pull(f.Record(C, T("Other"), Vv((Peer, 1))));

        var r = f.Merge();
        Assert.AreEqual(DataSyncBaseState.MissingAtPeer, BaseOf(r, A).State,
            "delivering the same pull again finds nothing left to change (I3)");
        Assert.AreEqual(agreed, BaseOf(r, A).Record);
    }

    [TestMethod]
    public void AConflictReMergedWhileThePeerOffersNoLiveEntityKeepsWaitingAndIsMissingThere()
    {
        var f = new MergeFixture();
        var baseRecord = f.Record(A, T("Genre"), Vv((Self, 1)));
        var record = f.Record(A, T("Genre R"), Vv((Self, 1), (Peer, 1)));
        f.Base(A, baseRecord, pending: PendingOf(record, DataSyncPendingReason.Conflict));
        f.Local("1", A, T("Genre L"), Vv((Self, 3)));
        f.PendingToMerge.Add((ItemKind, A));
        f.LiveCounts[ItemKind] = 0;                  // the peer detached it: it offers nothing live
        f.FullReconciliation.Add(ItemKind);

        var r = f.Merge();
        var b = BaseOf(r, A);
        Assert.AreEqual(DataSyncBaseState.MissingAtPeer, b.State, "a second delivery finds nothing left to change (I3)");
        Assert.AreEqual(DataSyncPendingReason.Conflict, b.Pending!.Reason, "the question still stands");
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, r.Inbox.Single().Type);
    }
}

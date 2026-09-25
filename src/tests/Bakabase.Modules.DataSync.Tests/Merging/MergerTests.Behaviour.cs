using System.Text.Json;
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

// Breakers (§8.7), the §8.6 bullets, pending records (§8.4), completeness of items, usage queries, determinism,
// full reconciliation (§8.8), unknown members (§8.9) and the children budget (§7.5.4).
public partial class MergerTests
{
    // ---- §8.6: each automatic-deletion condition missing asks -------------------------------------

    [TestMethod]
    [DataRow("notDominated")]
    [DataRow("notCreatedBySync")]
    [DataRow("hasValues")]
    [DataRow("valueCountUnknown")]
    [DataRow("openItem")]
    [DataRow("pendingRecord")]
    [DataRow("heldChildren")]
    [DataRow("deletionsAsItems")]
    public void EveryAutomaticDeletionConditionMissingAsks(string missing)
    {
        var f = new MergeFixture();
        if (missing == "deletionsAsItems") f.LinkFlags = new DataSyncMergeFlags(DeletionsAsItems: true);
        var overlay = missing == "heldChildren"
            ? new DataSyncOverlay([], [new DataSyncHeldChild("1", 99)])
            : DataSyncOverlay.None;
        var localVv = missing == "notDominated" ? Vv((Peer, 1), (Self, 9)) : Vv((Peer, 1));
        f.Mode = missing == "notDominated" ? DataSyncLinkMode.Follow : DataSyncLinkMode.TwoWay;
        f.Local("1", A, T("Mood", ("1", "x")), localVv, lastEditor: PeerEditor,
            createdBySync: missing != "notCreatedBySync", overlay: overlay);
        if (missing != "valueCountUnknown") f.ValueCounts[(ItemKind, "1")] = missing == "hasValues" ? 3 : 0;
        if (missing == "openItem") f.OpenItem(1, A, DataSyncInboxItemType.FieldConflict, "name");
        if (missing == "pendingRecord")
            f.Base(A, null, pending: PendingOf(f.Record(A, T("Mood 2"), Vv((Peer, 1), (Third, 1))), DataSyncPendingReason.Conflict));
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        Assert.AreEqual(0, r.Batches.Count, missing);
        Assert.AreEqual(DataSyncInboxItemType.DeletedThere, r.Inbox.Single(i => i.Type == DataSyncInboxItemType.DeletedThere).Type);
        Assert.AreEqual(missing, new DataSyncAutoApplyPolicy().DecideEntityDeletion(new DataSyncEntityDeletionFacts(
            localVv.CompareTo(Vv((Peer, 2))), missing != "notCreatedBySync",
            missing == "valueCountUnknown" ? null : missing == "hasValues" ? 3 : 0, missing == "openItem",
            missing == "pendingRecord", missing == "heldChildren", missing == "deletionsAsItems")).Reason);
    }

    [TestMethod]
    public void TheDeletionsAsItemsFlagIsKeptWithThePendingRecordAndTheItem()
    {
        var f = new MergeFixture { LinkFlags = new DataSyncMergeFlags(DeletionsAsItems: true) };
        f.Local("1", A, T("Mood"), Vv((Peer, 1)), createdBySync: true);
        f.ValueCounts[(ItemKind, "1")] = 0;
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        Assert.IsTrue(r.Inbox.Single().Flags.DeletionsAsItems);
        Assert.IsTrue(BaseOf(r, A).Pending!.Flags.DeletionsAsItems);
    }

    // ---- B2, B3 -------------------------------------------------------------------------------

    private static MergeFixture Deletions(int count, int bases, bool items = false)
    {
        var f = new MergeFixture();
        for (var i = 1; i <= Math.Max(count, bases); i++)
        {
            var key = K(0x100 + i);
            f.Local(i.ToString(), key, T("E" + i), Vv((Peer, 1)), lastEditor: PeerEditor);
            f.Base(key, f.Record(key, T("E" + i), Vv((Peer, 1))));
            if (i > count) continue;
            f.Pull(f.Record(key, null, Vv((Peer, 2)), deleted: true));
            if (items) f.OpenItem(i, key, DataSyncInboxItemType.DeletedThere, recordVv: Vv((Peer, 2)));
        }

        return f;
    }

    [TestMethod]
    public void B2_MoreThanTenNewDeletionsPause()
    {
        var r = Deletions(11, 11).Merge();
        Assert.AreEqual(DataSyncPauseReason.MassDeletion, r.Pause);
        Assert.AreEqual("deletions=11;kind=testItem", r.PauseDetail);
        Assert.AreEqual(0, r.Batches.Count + r.Inbox.Count + r.BaseUpdates.Count + r.CursorAdvance.Count,
            "nothing is applied");

        Assert.IsNull(Deletions(6, 30).Merge().Pause, "six of thirty is under both limits");
        Assert.AreEqual(DataSyncPauseReason.MassDeletion, Deletions(7, 30).Merge().Pause, "over 20% of thirty");
        Assert.IsNull(Deletions(2, 5).Merge().Pause, "2 of 5: the ratio needs 20 bases");
    }

    [TestMethod]
    public void B2_TwoDeletionsOfFiveExtensionGroupsDoNotPause()
    {
        var f = new MergeFixture();
        for (var i = 1; i <= 5; i++)
        {
            var key = K(0x500 + i);
            var group = new Bakabase.Modules.DataSync.Kinds.ExtensionGroups.ExtensionGroupContentV1("G" + i, [".e" + i]);
            f.Local("g" + i, key, group, Vv((Peer, 1)), lastEditor: PeerEditor, createdBySync: true, kind: GroupKind);
            f.Base(key, f.Record(key, group, Vv((Peer, 1)), kind: GroupKind), kind: GroupKind);
            if (i <= 2) f.Pull(f.Record(key, null, Vv((Peer, 2)), deleted: true, kind: GroupKind), GroupKind);
            f.ValueCounts[(GroupKind, "g" + i)] = 0;
        }

        var r = f.Merge();
        Assert.IsNull(r.Pause);
        Assert.AreEqual(2, r.Batches.Single(b => b.Kind == GroupKind).Operations.OfType<DeleteEntityOperation>().Count(),
            "created by sync, no values: they apply by themselves");
    }

    [TestMethod]
    public void B2_DeletionsAlreadyAskedAboutAreNotNew()
    {
        // A full reconciliation re-sending 30 tombstones whose items are open does not pause.
        var f = Deletions(30, 30, items: true);
        f.FullReconciliation.Add(ItemKind);
        Assert.IsNull(f.Merge().Pause);
    }

    [TestMethod]
    public void B2_TheResumeFlagsSkipIt()
    {
        var apply = Deletions(11, 11);
        apply.LinkFlags = new DataSyncMergeFlags(SkipDeletionBreaker: true);
        Assert.IsNull(apply.Merge().Pause);

        var review = Deletions(11, 11);
        review.LinkFlags = new DataSyncMergeFlags(DeletionsAsItems: true);
        var r = review.Merge();
        Assert.IsNull(r.Pause);
        Assert.AreEqual(11, r.Inbox.Count(i => i.Type == DataSyncInboxItemType.DeletedThere));
    }

    [TestMethod]
    public void B3_AKindOfferedEmptyPausesOnlyWithThreeBases()
    {
        var f = new MergeFixture();
        for (var i = 1; i <= 3; i++) f.Base(K(i), f.Record(K(i), T("E" + i), Vv((Peer, 1))));
        f.LiveCounts[ItemKind] = 0;
        var r = f.Merge();
        Assert.AreEqual(DataSyncPauseReason.KindEmptied, r.Pause);
        Assert.AreEqual("kind=testItem;bases=3", r.PauseDetail);

        var two = new MergeFixture();
        for (var i = 1; i <= 2; i++) two.Base(K(i), two.Record(K(i), T("E" + i), Vv((Peer, 1))));
        two.LiveCounts[ItemKind] = 0;
        Assert.IsNull(two.Merge().Pause);
    }

    // ---- B5 ----------------------------------------------------------------------------------

    private static MergeFixture Updates(int count, bool creates = false)
    {
        var f = new MergeFixture();
        for (var i = 1; i <= count; i++)
        {
            var key = K(0x200 + i);
            if (!creates) f.Local(i.ToString(), key, T("E" + i), Vv((Self, 1)));
            f.Pull(f.Record(key, T("E" + i + "!"), Vv((Self, 1), (Peer, i))));
        }

        return f;
    }

    [TestMethod]
    public void B5_OverFiftyUpdatesWaitAsOneLargeChangeItem()
    {
        var r = Updates(51).Merge();
        Assert.IsNull(r.Pause, "B5 is not a pause: everything else applies");
        Assert.AreEqual(0, r.Batches.Count);
        Assert.AreEqual(51, r.BaseUpdates.Count(u => u.Pending?.Reason == DataSyncPendingReason.LargeChange));
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.LargeChange, item.Type);
        Assert.AreEqual("", item.Kind);
        Assert.AreEqual(SyncKey.LinkLevel, item.Key);
        Assert.AreEqual(DataSyncInboxDrafts.LargeChangeSubject, item.SubjectPath);
        Assert.AreEqual(DataSyncInboxItemOrigin.State, item.Origin);
        Assert.AreEqual(51, item.Payload.LargeChange!.Count);
        Assert.IsTrue(item.Payload.LargeChange.All(e => !e.Create && e.Changes > 0));
        Assert.AreEqual(0, r.Evaluated.Count, "waiting records evaluate nothing");

        Assert.AreEqual(50, Updates(50).Merge().Batches.Single().Operations.Count);
    }

    [TestMethod]
    public void B5_CountsCreatesAndIsSkippedOnAFirstContactOrApplyAll()
    {
        var creates = Updates(51, creates: true).Merge();
        Assert.IsTrue(creates.Inbox.Single().Payload.LargeChange!.All(e => e.Create));
        Assert.AreEqual(51, creates.BaseUpdates.Count(u => u.State == DataSyncBaseState.Unbound &&
                                                           u.Pending?.Reason == DataSyncPendingReason.LargeChange));

        var first = Updates(51, creates: true);
        first.FirstContactKinds.Add(ItemKind);
        Assert.AreEqual(51, first.Merge().Batches.Single().Operations.Count);

        var applyAll = Updates(51);
        applyAll.LinkFlags = new DataSyncMergeFlags(SkipLargeChange: true);
        Assert.AreEqual(51, applyAll.Merge().Batches.Single().Operations.Count);
    }

    // ---- B8 ----------------------------------------------------------------------------------

    [TestMethod]
    public void B8_TooManyOpenItemsPause()
    {
        var f = new MergeFixture { Limits = DataSyncLimits.Default with { MaxOpenInboxItemsPerLink = 2 } };
        for (var i = 1; i <= 3; i++)
        {
            f.Local(i.ToString(), K(i), T("Rating" + i), Vv((Self, 1)));
            f.Pull(f.Record(K(0x300 + i), T("rating" + i), Vv((Peer, i))));
        }

        var r = f.Merge();
        Assert.AreEqual(DataSyncPauseReason.TooManyDecisions, r.Pause);
        Assert.AreEqual("openItems=3", r.PauseDetail);
    }

    // ---- pending records (§8.4) -----------------------------------------------------------------

    [TestMethod]
    public void APendingRecordIsReplacedByANewerRecordWithItsKey()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 3))),
            pending: PendingOf(f.Record(A, T("Artists"), Vv((Self, 3), (Peer, 1))), DataSyncPendingReason.Conflict));
        f.Local("1", A, T("作者"), Vv((Self, 4)));
        f.PendingToMerge.Add((ItemKind, A));
        var newer = f.Pull(f.Record(A, T("作者"), Vv((Self, 3), (Peer, 2))));

        var r = f.Merge();
        Assert.AreEqual(newer, BaseOf(r, A).Record, "the newer record resolved it; the old one is gone");
        Assert.IsTrue(BaseOf(r, A).ClearPending);
        Assert.AreEqual(0, r.Inbox.Count);
    }

    [TestMethod]
    public void APendingRecordIsReMergedOnlyWhenAsked()
    {
        var f = new MergeFixture { NoPull = true };
        var pending = PendingOf(f.Record(A, T("Artists"), Vv((Self, 3), (Peer, 1))), DataSyncPendingReason.Conflict);
        f.Base(A, f.Record(A, T("Artist"), Vv((Self, 3))), pending: pending);
        f.Local("1", A, T("作者"), Vv((Self, 4)), seq: 11);

        var untouched = f.Merge();
        Assert.AreEqual(0, untouched.BaseUpdates.Count + untouched.Inbox.Count + untouched.Evaluated.Count);

        f.PendingToMerge.Add((ItemKind, A));
        var r = f.Merge();
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, item.Type);
        Assert.AreEqual(pending.RecordHash, item.RecordHash, "the same record, the same hash");
        Assert.AreEqual(11, BaseOf(r, A).Pending!.EvaluatedAtLocalSeq);
        Assert.AreEqual(0, r.CursorAdvance.Count, "a re-merge without a pull moves no cursor");
    }

    [TestMethod]
    public void EachReMergeConditionHoldsAndNothingElse()
    {
        var conflict = PendingOf(new MergeFixture().Record(A, T("x"), Vv((Peer, 1))), DataSyncPendingReason.Conflict, 10);
        var none = DataSyncMergeFlags.None;
        Assert.IsFalse(DataSyncPendingRecords.ShouldRemerge(conflict, 10, false, none, false), "nothing relevant changed");
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(conflict, 11, false, none, false), "2: the local Seq moved");
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(conflict with { Reason = DataSyncPendingReason.Retry }, 10,
            false, none, false), "3: Retry");
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(conflict with { Reason = DataSyncPendingReason.OverBudget },
            10, false, none, false), "3: OverBudget");
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(conflict, 10, true, none, false), "4: full reconciliation");
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(conflict with { Reason = DataSyncPendingReason.LargeChange },
            10, false, new DataSyncMergeFlags(SkipLargeChange: true), false), "5: Apply all");
        Assert.IsFalse(DataSyncPendingRecords.ShouldRemerge(conflict, 10, false,
            new DataSyncMergeFlags(SkipLargeChange: true), false), "5 targets its reason only");
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(conflict with { Reason = DataSyncPendingReason.MassChildDeletion },
            10, false, new DataSyncMergeFlags(ChildDeletions: DataSyncChildDeletionMode.ReviewEach), false), "5: B4");
        var deletion = PendingOf(new MergeFixture().Record(A, null, Vv((Peer, 2)), deleted: true),
            DataSyncPendingReason.AwaitingDecision, 10);
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(deletion, 10, false,
            new DataSyncMergeFlags(SkipDeletionBreaker: true), false), "5: B2's resume");
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(conflict with { Reason = DataSyncPendingReason.Held }, 10,
            false, none, true), "6: schema versions changed");
        Assert.IsFalse(DataSyncPendingRecords.ShouldRemerge(conflict with { Reason = DataSyncPendingReason.Held }, 10,
            false, none, false));
    }

    [TestMethod]
    public void AChangedDuringApplyItemBecomesARetryRecordAndAppliesAfterTheCursorMoved()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre"), Vv((Self, 3))));
        f.Local("1", A, T("Genre"), Vv((Self, 3)));
        var record = f.Pull(f.Record(A, T("Genres"), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        var itemId = r.Batches.Single().Operations.Single().ItemId;
        var deferred = DataSyncRecordApply.WithoutChangedDuringApply(r, [itemId], f.Input().Bases);
        Assert.AreEqual(0, deferred.Batches.Count + deferred.Revisions.Count);
        var b = BaseOf(deferred, A);
        Assert.IsNull(b.Record, "the base is not advanced");
        Assert.AreEqual(DataSyncPendingReason.Retry, b.Pending!.Reason);
        Assert.AreEqual(record, b.Pending.Record);
        Assert.AreEqual(r.CursorAdvance[ItemKind], deferred.CursorAdvance[ItemKind], "the cursor still moves");

        // The next pull (nothing new from the peer) merges the Retry record and applies it.
        var next = new MergeFixture { NoPull = true };
        next.Base(A, f.Record(A, T("Genre"), Vv((Self, 3))), pending: b.Pending);
        next.Local("1", A, T("Genre"), Vv((Self, 3)));
        next.PendingToMerge.Add((ItemKind, A));
        Assert.IsTrue(DataSyncPendingRecords.ShouldRemerge(b.Pending, 10, false, DataSyncMergeFlags.None, false));
        var applied = next.Merge();
        Assert.AreEqual(T("Genres"), Items.ReadLocal(((UpdateEntityOperation)applied.Batches.Single().Operations.Single())
            .MergedContent));
        Assert.AreEqual(record, BaseOf(applied, A).Record);
    }

    [TestMethod]
    public void APullRecordTakingOverAnotherRecordsBaseMeetsRowM()
    {
        // B's record waits on A's base row (bound before); now C's record binds to the same entity.
        var f = new MergeFixture();
        f.Local("1", A, T("Artist"), Vv((Self, 1)), aliases: [B, C]);
        f.Base(A, f.Record(B, T("Artist"), Vv((Self, 1))),
            pending: PendingOf(f.Record(B, T("Artist 2"), Vv((Self, 1), (Peer, 1)), aliases: [A]), DataSyncPendingReason.Conflict));
        f.Pull(f.Record(C, T("Author"), Vv((Peer, 5)), aliases: [A]));

        var r = f.Merge();
        CollectionAssert.AreEquivalent(new[] { B.Value, C.Value },
            r.Inbox.Single().Payload.Records!.Select(x => x.PrimaryKey).ToArray());
    }

    // ---- completeness, usage queries, determinism -------------------------------------------------

    [TestMethod]
    public void ItemsAreCompletePerEvaluatedEntity()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Artist", ("1", "X")), Vv((Self, 3))), childMap: new Dictionary<string, string> { ["1"] = "1" });
        f.Local("1", A, T("作者", ("1", "Y")), Vv((Self, 4)));
        f.Pull(f.Record(A, T("Artists", ("1", "Z")), Vv((Self, 3), (Peer, 1))));

        var r = f.Merge();
        CollectionAssert.AreEquivalent(new[] { "name", "child:1" }, r.Inbox.Select(i => i.SubjectPath).ToArray());
        CollectionAssert.AreEquivalent(new[] { DataSyncInboxItemType.FieldConflict, DataSyncInboxItemType.ChildRenameConflict },
            r.Inbox.Select(i => i.Type).ToArray());
        Assert.IsTrue(r.Inbox.All(i => i.Origin == DataSyncInboxItemOrigin.Merger && i.Key == A));
        Assert.IsTrue(Evaluated(r, A));
    }

    [TestMethod]
    public void CollectUsageQueriesAsksForExactlyTheDeletionCandidates()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("G", ("1", "A"), ("2", "B"), ("3", "C")), Vv((Self, 3))));
        f.Local("1", A, T("G", ("1", "A"), ("2", "B"), ("3", "C")), Vv((Self, 3)));
        f.Pull(f.Record(A, T("G", ("1", "A")), Vv((Self, 3), (Peer, 1))));
        f.Local("2", B, T("Mood"), Vv((Peer, 1)), createdBySync: true);
        f.Pull(f.Record(B, null, Vv((Peer, 2)), deleted: true));
        f.Local("3", C, T("Untouched", ("9", "Z")), Vv((Self, 1)));

        var queries = DataSyncMerger.CollectUsageQueries(f.Input());
        Assert.AreEqual(2, queries.Count);
        var children = queries.Single(q => q.LocalKey == "1");
        CollectionAssert.AreEqual(new[] { "2", "3" }, children.ChildIds.ToArray());
        Assert.IsFalse(children.NeedValueCount);
        var deleted = queries.Single(q => q.LocalKey == "2");
        Assert.AreEqual(0, deleted.ChildIds.Count);
        Assert.IsTrue(deleted.NeedValueCount);
    }

    [TestMethod]
    public void TheSameInputTwiceGivesByteIdenticalOutput()
    {
        static MergeFixture Build()
        {
            var f = new MergeFixture();
            f.Base(A, f.Record(A, T("Artist", ("1", "X"), ("2", "W")), Vv((Self, 3)), seq: 1));
            f.Local("1", A, T("作者", ("1", "X"), ("2", "W")), Vv((Self, 4)), orderKey: "a0");
            f.Local("2", B, T("Rating"), Vv((Self, 1)), orderKey: "a1");
            f.Local("3", C, T("Mood"), Vv((Peer, 1)), createdBySync: true);
            f.Tombstone(D, Vv((Self, 2)));
            f.UseAllChildren(1);
            f.ValueCounts[(ItemKind, "3")] = 0;
            f.Pull(f.Record(A, T("Artists", ("1", "X")), Vv((Self, 3), (Peer, 1)), orderKey: "a2", seq: 10));
            f.Pull(f.Record(K(0x77), T("rating"), Vv((Peer, 2)), seq: 11));
            f.Pull(f.Record(C, null, Vv((Peer, 3)), deleted: true, seq: 12));
            f.Pull(f.Record(D, T("Back"), Vv((Peer, 4)), seq: 13));
            f.Pull(f.Record(K(0x78), T("New", ("n", "N")), Vv((Peer, 5)), orderKey: "Zz", seq: 14));
            return f;
        }

        var first = Dump(Build().Merge());
        var second = Dump(Build().Merge());
        Assert.AreEqual(first, second);
        StringAssert.Contains(first, "FieldConflict");
        StringAssert.Contains(first, "LinkSuggestion");
        StringAssert.Contains(first, "DeletedHereEditedThere");
    }

    /// <summary>A canonical text of a whole result: every member, operations by their runtime type.</summary>
    internal static string Dump(DataSyncMergeResult r)
    {
        var options = DataSyncJson.Options;
        JsonNode? Node(object? value) => value is null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), options);
        var json = new JsonObject
        {
            ["pause"] = r.Pause?.ToString(),
            ["detail"] = r.PauseDetail,
            ["anomaly"] = Node(r.Anomaly),
            ["batches"] = new JsonArray(r.Batches.Select(b => (JsonNode?)new JsonObject
            {
                ["kind"] = b.Kind,
                ["ops"] = new JsonArray(b.Operations.Select(o => (JsonNode?)new JsonObject { ["type"] = o.GetType().Name, ["op"] = Node(o) }).ToArray()),
            }).ToArray()),
            ["revisions"] = new JsonArray(r.Revisions.Select(Node).ToArray()),
            ["bases"] = new JsonArray(r.BaseUpdates.Select(Node).ToArray()),
            ["inbox"] = new JsonArray(r.Inbox.Select(Node).ToArray()),
            ["overlays"] = new JsonArray(r.OverlayChanges.Select(Node).ToArray()),
            ["order"] = new JsonArray(r.Order.Select(o => (JsonNode?)new JsonObject
            {
                ["kind"] = o.Kind,
                ["synced"] = new JsonArray(o.Synced.Select(s => (JsonNode?)new JsonArray(s.LocalKey, s.OrderKey)).ToArray()),
            }).ToArray()),
            ["cursors"] = Node(r.CursorAdvance),
            ["notes"] = new JsonArray(r.Notes.Select(Node).ToArray()),
            ["hints"] = new JsonArray(r.ClosureHints.Select(Node).ToArray()),
            ["evaluated"] = new JsonArray(r.Evaluated.Select(e => (JsonNode?)new JsonArray(e.Kind, e.Key.Value)).ToArray()),
            ["serve"] = new JsonArray((r.TombstonesToServe ?? []).Select(e => (JsonNode?)new JsonArray(e.Kind, e.Key.Value)).ToArray()),
        };
        return json.ToJsonString();
    }

    [TestMethod]
    public void AKindThisBuildCannotReadHoldsEveryRecord()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Genre"), Vv((Self, 1)));
        f.Pull(f.Record(A, T("Genre 2"), Vv((Self, 1), (Peer, 1))));
        var input = f.Input();
        var pull = input.Incoming!;
        var unsupported = pull with
        {
            Kinds = pull.Kinds.Select(k => k with { Supported = false, KindHeld = DataSyncHeldReason.NewerSchema }).ToList(),
        };

        var r = DataSyncMerger.Merge(input with { Incoming = unsupported });
        Assert.AreEqual(DataSyncPendingReason.Held, BaseOf(r, A).Pending!.Reason);
        Assert.AreEqual(0, r.Batches.Count);
        Assert.IsTrue(r.CursorAdvance.ContainsKey(ItemKind));
    }

    [TestMethod]
    public void AKindOutsideTheLinkIsNeitherMergedNorAdvanced()
    {
        var f = new MergeFixture();
        f.Pull(f.Record(A, T("Genre"), Vv((Peer, 1))));
        var input = f.Input();
        var r = DataSyncMerger.Merge(input with { Link = input.Link with { Kinds = [GroupKind] } });
        Assert.AreEqual(0, r.Batches.Count + r.BaseUpdates.Count);
        Assert.AreEqual(0, r.CursorAdvance.Count);
    }

    // ---- §8.8 full reconciliation ----------------------------------------------------------------

    [TestMethod]
    public void AFullReconciliationMarksAbsentBasesMissingAndChangesNothing()
    {
        var f = new MergeFixture();
        f.Local("1", A, T("Offered"), Vv((Peer, 1)));
        f.Local("2", B, T("Gone"), Vv((Peer, 1)));
        f.Base(A, f.Record(A, T("Offered"), Vv((Peer, 1))));
        f.Base(B, f.Record(B, T("Gone"), Vv((Peer, 1))));
        f.Pull(f.Record(A, T("Offered"), Vv((Peer, 1))));
        f.FullReconciliation.Add(ItemKind);

        var r = f.Merge();
        var missing = BaseOf(r, B);
        Assert.AreEqual(DataSyncBaseState.MissingAtPeer, missing.State);
        Assert.IsNull(missing.Record);
        Assert.AreEqual(0, r.Batches.Count, "missing is unknown, never a deletion");
        Assert.AreEqual(DataSyncMergeNoteCodes.MissingAtPeer, r.Notes.Single().Code);
        Assert.AreEqual("Gone", r.Notes.Single().Name);

        f.FullReconciliation.Clear();
        Assert.IsFalse(f.Merge().BaseUpdates.Any(u => u.State == DataSyncBaseState.MissingAtPeer),
            "an incremental pull never infers absence");
    }

    // ---- §8.9 unknown members -------------------------------------------------------------------

    [TestMethod]
    public void UnknownMembersMergePerMemberAndNeverRegress()
    {
        var f = new MergeFixture();
        var baseUnknown = new JsonObject { ["xA"] = 1, ["xB"] = 1 };
        f.Base(A, f.Record(A, T("Genre"), Vv((Self, 3)), unknown: baseUnknown));
        // This older build never changes xA; the peer changed xA and dropped xB; locally xB was changed.
        f.Local("1", A, T("Genre 2"), Vv((Self, 4)), unknown: new JsonObject { ["xA"] = 1, ["xB"] = 2 });
        f.Pull(f.Record(A, T("Genre"), Vv((Self, 3), (Peer, 1)), unknown: new JsonObject { ["xA"] = 5, ["xC"] = 7 }));

        var r = f.Merge();
        var unknown = r.Revisions.Single().Unknown!;
        Assert.AreEqual(5, (int)unknown["xA"]!, "unchanged here: the peer's");
        Assert.AreEqual(2, (int)unknown["xB"]!, "removed there, changed here: a conflict keeps the local value");
        Assert.AreEqual(7, (int)unknown["xC"]!, "added there");
        Assert.AreEqual(DataSyncMergeNoteCodes.UnknownMembersKeptLocal, r.Notes.Single().Code);
        Assert.AreEqual(0, r.Inbox.Count, "an unknown member is never an item");
    }

    // ---- §7.5.4 children budget ------------------------------------------------------------------

    [TestMethod]
    public void AnEntityOverTheChildrenBudgetWaitsAndIsMergedFirstNextTime()
    {
        var f = new MergeFixture { Limits = DataSyncLimits.Default with { MaxChildrenPerStagedPull = 3 } };
        f.Pull(f.Record(A, T("One", ("1", "a"), ("2", "b")), Vv((Peer, 1))));
        var second = f.Pull(f.Record(B, T("Two", ("3", "c"), ("4", "d")), Vv((Peer, 2))));

        var r = f.Merge();
        Assert.AreEqual(1, r.Batches.Single().Operations.Count);
        var waiting = BaseOf(r, B);
        Assert.AreEqual(DataSyncPendingReason.OverBudget, waiting.Pending!.Reason);
        Assert.AreEqual(second, waiting.Pending.Record);

        var next = new MergeFixture { Limits = f.Limits };
        next.Base(B, null, DataSyncBaseState.Unbound, pending: waiting.Pending);
        next.PendingToMerge.Add((ItemKind, B));
        next.Pull(next.Record(C, T("Three", ("5", "e"), ("6", "f")), Vv((Peer, 3))));
        var created = next.Merge().Batches.Single().Operations.Cast<CreateEntityOperation>().ToList();
        Assert.AreEqual(B, created[0].Keys.Primary, "OverBudget records are merged first");
    }
}

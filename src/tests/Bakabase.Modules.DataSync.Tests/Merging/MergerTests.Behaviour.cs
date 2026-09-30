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
// and full reconciliation (§8.8).
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
    public void EveryAutomaticDeletionConditionMissingAsks(string missing)
    {
        var f = new MergeFixture();
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
            missing == "pendingRecord", missing == "heldChildren", false)).Reason);
    }

    // ---- B2 -------------------------------------------------------------------------------------

    /// <summary>Deletions the peer made of definitions sync created here, unused: each applies by itself alone.</summary>
    private static MergeFixture Deletions(int count, int bases)
    {
        var f = new MergeFixture();
        for (var i = 1; i <= Math.Max(count, bases); i++)
        {
            var key = K(0x100 + i);
            f.Local(i.ToString(), key, T("E" + i), Vv((Peer, 1)), lastEditor: PeerEditor, createdBySync: true);
            f.ValueCounts[(ItemKind, i.ToString())] = 0;
            f.Base(key, f.Record(key, T("E" + i), Vv((Peer, 1))));
            if (i <= count) f.Pull(f.Record(key, null, Vv((Peer, 2)), deleted: true));
        }

        return f;
    }

    private static int AutoDeleted(DataSyncMergeResult r) => Ops(r).OfType<DeleteEntityOperation>().Count();

    [TestMethod]
    public void B2_MoreThanTenNewDeletionsAreEachAQuestionAndTheRestOfThePullApplies()
    {
        var f = Deletions(11, 11);
        f.Pull(f.Record(K(0x999), T("New"), Vv((Peer, 3))));
        var r = f.Merge();
        Assert.IsNull(r.Pause, "never a pause");
        Assert.AreEqual(0, AutoDeleted(r));
        Assert.AreEqual(11, r.Inbox.Count(i => i.Type == DataSyncInboxItemType.DeletedThere));
        Assert.IsTrue(r.BaseUpdates.Where(u => u.Pending is not null)
            .All(u => u.Pending!.Reason == DataSyncPendingReason.AwaitingDecision && !u.ClearPending));
        Assert.AreEqual(1, Ops(r).OfType<CreateEntityOperation>().Count(), "the rest of the pull applies");

        Assert.AreEqual(10, AutoDeleted(Deletions(10, 11).Merge()), "ten apply by themselves");
        Assert.AreEqual(6, AutoDeleted(Deletions(6, 30).Merge()), "six of thirty is under both limits");
        Assert.AreEqual(0, AutoDeleted(Deletions(7, 30).Merge()), "over 20% of thirty");
        Assert.AreEqual(2, AutoDeleted(Deletions(2, 5).Merge()), "2 of 5: the ratio needs 20 bases");
    }

    [TestMethod]
    public void B2_ADeletionAlreadyAskedAboutIsNotNew()
    {
        // Eleven deletions, one asked about before: ten are new, within the limit, and apply by themselves.
        var f = Deletions(11, 11);
        f.OpenItem(1, K(0x101), DataSyncInboxItemType.DeletedThere, recordVv: Vv((Peer, 2)));
        var r = f.Merge();
        Assert.AreEqual(10, AutoDeleted(r));
        Assert.AreEqual(K(0x101), r.Inbox.Single(i => i.Type == DataSyncInboxItemType.DeletedThere).Key);
    }

    [TestMethod]
    public void B2_TwoDeletionsOfFiveExtensionGroupsApplyByThemselves()
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

        Assert.AreEqual(2, AutoDeleted(f.Merge()), "created by sync, no values: they apply by themselves");
    }

    [TestMethod]
    public void B2_DeletionsWaitingForADecisionAreNotNew()
    {
        // Deletions already waiting as AwaitingDecision pending records — their items not open (not yet drafted, or
        // closed by a reset of the inbox) — re-sent by a full reconciliation: each is asked again, none is new.
        var f = new MergeFixture();
        for (var i = 1; i <= 30; i++)
        {
            var key = K(0x100 + i);
            f.Local(i.ToString(), key, T("E" + i), Vv((Peer, 1)), lastEditor: PeerEditor);
            var tombstone = f.Record(key, null, Vv((Peer, 2)), deleted: true);
            f.Base(key, f.Record(key, T("E" + i), Vv((Peer, 1))),
                pending: PendingOf(tombstone, DataSyncPendingReason.AwaitingDecision));
            f.Pull(tombstone with { Seq = tombstone.Seq + 100 });
        }

        f.FullReconciliation.Add(ItemKind);
        var r = f.Merge();
        Assert.AreEqual(30, r.Inbox.Count(i => i.Type == DataSyncInboxItemType.DeletedThere), "each is asked again");
        Assert.AreEqual(0, Ops(r).Count);
    }

    // ---- a large pull ----------------------------------------------------------------------------

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
    public void AnyNumberOfChangesAndCreatesAppliesAtOnce()
    {
        foreach (var creates in new[] { false, true })
        {
            var r = Updates(500, creates).Merge();
            Assert.IsNull(r.Pause);
            Assert.AreEqual(500, r.Batches.Single().Operations.Count);
            Assert.AreEqual(0, r.Inbox.Count);
            Assert.IsTrue(r.BaseUpdates.All(u => u.Pending is null));
        }
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
        f.Local("1", A, T("作者"), Vv((Self, 4)));

        var untouched = f.Merge();
        Assert.AreEqual(0, untouched.BaseUpdates.Count + untouched.Inbox.Count + untouched.Evaluated.Count);

        f.PendingToMerge.Add((ItemKind, A));
        var r = f.Merge();
        var item = r.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, item.Type);
        Assert.AreEqual(pending.RecordHash, item.RecordHash, "the same record, the same hash");
        Assert.AreEqual(0, r.CursorAdvance.Count, "a re-merge without a pull moves no cursor");
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
    public void UsageIsReadForTheEntitiesAPullMayChange()
    {
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("G", ("1", "A"), ("2", "B"), ("3", "C")), Vv((Self, 3))));
        f.Local("1", A, T("G", ("1", "A"), ("2", "B"), ("3", "C")), Vv((Self, 3)));
        f.Pull(f.Record(A, T("G", ("1", "A")), Vv((Self, 3), (Peer, 1))));
        f.Local("2", B, T("Mood"), Vv((Peer, 1)), createdBySync: true);
        f.Pull(f.Record(B, null, Vv((Peer, 2)), deleted: true));
        f.Local("3", C, T("Untouched", ("9", "Z")), Vv((Self, 1)));
        f.Local("4", D, T("Known", ("8", "Y")), Vv((Peer, 5)));
        f.Pull(f.Record(D, T("Known"), Vv((Peer, 4))));

        var targets = DataSyncMerger.UsageTargets(f.Input())[ItemKind];
        CollectionAssert.AreEquivalent(new[] { "1", "2" }, targets.Keys.ToArray(), "an older record changes nothing");
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, targets["1"].ToArray(), "every child: a missing one is in use");
        Assert.AreEqual(0, targets["2"].Count, "a deletion needs the value count only");
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

    // ---- §8.6 across links ----------------------------------------------------------------------

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void AQuestionOrAPendingRecordOnAnotherLinkStopsTheAutomaticDeletion(bool openItem, bool pendingRecord)
    {
        // Created by sync, no values, dominated: deleted by itself — unless a person is still asked about it anywhere.
        var f = new MergeFixture();
        f.Local("1", A, T("Mood"), Vv((Peer, 1)), lastEditor: PeerEditor, createdBySync: true,
            openItemAnyLink: openItem, pendingRecordAnyLink: pendingRecord);
        f.ValueCounts[(ItemKind, "1")] = 0;
        f.Pull(f.Record(A, null, Vv((Peer, 2)), deleted: true));

        var r = f.Merge();
        Assert.AreEqual(0, r.Batches.Count, "not deleted by itself");
        Assert.AreEqual(0, r.Notes.Count(n => n.Code == DataSyncMergeNoteCodes.AutoDeleted));
        Assert.AreEqual(DataSyncInboxItemType.DeletedThere, r.Inbox.Single().Type);
        Assert.AreEqual(DataSyncPendingReason.AwaitingDecision, BaseOf(r, A).Pending!.Reason);

        var alone = new MergeFixture();
        alone.Local("1", A, T("Mood"), Vv((Peer, 1)), lastEditor: PeerEditor, createdBySync: true);
        alone.ValueCounts[(ItemKind, "1")] = 0;
        alone.Pull(alone.Record(A, null, Vv((Peer, 2)), deleted: true));
        Assert.IsInstanceOfType<DeleteEntityOperation>(alone.Merge().Batches.Single().Operations.Single());
    }

    // ---- a stored conflict published again ---------------------------------------------------------

    [TestMethod]
    [DataRow("unchanged")]
    [DataRow("alias")]
    [DataRow("seq")]
    public void AConflictRecordPublishedAgainOnlyDerivesItsItems(string republish)
    {
        // R1 conflicted on the name and its safe part (the colour) applied; another link's device recoloured the
        // entity since. The same revision again — with a new alias, or a bumped Seq — must not put R1's colour back.
        var f = new MergeFixture();
        var r1 = f.Record(A, T("Kinds", "#0090ff", null), Vv((Peer, 2)), seq: 10);
        f.Base(A, f.Record(A, T("Genre", "#e5484d", null), Vv((Peer, 1))),
            pending: PendingOf(r1, DataSyncPendingReason.Conflict));
        f.Local("1", A, T("Genres", "#30a46c", null), Vv((Peer, 1), (Self, 3), (Third, 1)), lastEditor: ThirdEditor);
        f.Pull(republish switch
        {
            "alias" => r1 with { Keys = [A.Value, B.Value] },
            "seq" => r1 with { Seq = 20 },
            _ => r1,
        });

        var r = f.Merge();
        Assert.IsFalse(Ops(r).OfType<UpdateEntityOperation>().Any(), "the safe part is not applied again");
        Assert.AreEqual(0, r.Revisions.Count);
        Assert.AreEqual("name", r.Inbox.Single().SubjectPath);
        Assert.AreEqual(DataSyncPendingReason.Conflict, BaseOf(r, A).Pending!.Reason);
        if (republish == "alias")
            CollectionAssert.AreEqual(new[] { B }, ((BindOnlyOperation)Ops(r).Single()).AliasKeysToAdd.All.ToArray());
        else Assert.AreEqual(0, Ops(r).Count);
    }

    // ---- one record, one row ------------------------------------------------------------------------

    /// <summary>Entity 1 has keys [A, D]; the peer's record R (keys [D, A]) waits as Retry on the Unbound row D.</summary>
    private static (MergeFixture F, DataSyncWireRecord R) WaitingOnAnAlias()
    {
        var f = new MergeFixture { NoPull = true };
        f.Local("1", A, T("Genre"), Vv((Self, 1)), aliases: [D]);
        f.Base(A, f.Record(A, T("Genre"), Vv((Self, 1))));
        var record = f.Record(D, T("Genres"), Vv((Self, 1), (Peer, 1)), aliases: [A]);
        f.Base(D, null, DataSyncBaseState.Unbound, pending: PendingOf(record, DataSyncPendingReason.Retry));
        f.PendingToMerge.Add((ItemKind, D));
        return (f, record);
    }

    [TestMethod]
    public void OneRecordStoredOnTwoRowsIsMergedOnce()
    {
        // A store that lost a clear: the same record waits on the entity's row and on the alias row.
        var (f, record) = WaitingOnAnAlias();
        f.Bases[(ItemKind, A)] = f.Bases[(ItemKind, A)] with
        {
            Pending = PendingOf(record, DataSyncPendingReason.Retry),
        };
        f.PendingToMerge.Add((ItemKind, A));

        var r = f.Merge();
        Assert.AreEqual(1, r.Batches.Single().Operations.Count, "one operation for one entity");
        Assert.AreEqual(1, r.Revisions.Count);
        Assert.AreEqual(record, BaseOf(r, A).Record);
        Assert.IsTrue(BaseOf(r, D).ClearPending);
        Assert.IsNull(BaseOf(r, D).Pending);
    }
}

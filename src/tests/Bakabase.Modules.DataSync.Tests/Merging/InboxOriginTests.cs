using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.Merging.MergeFixture;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>
/// The two item origins (§9.1, §9.3), the pure half: merger-derived items are re-derived on every evaluation and
/// close when no longer produced; state-derived items survive pulls and close only with their state; dominance
/// closes any link's item. The store half is <c>Bakabase.Tests/DataSync/InboxOriginTests</c> [C].
/// </summary>
[TestClass]
public class InboxOriginTests
{
    private static readonly SyncKey A = K(0xa), B = K(0xb);

    private static DataSyncOpenInboxItem Open(long id, SyncKey key, DataSyncInboxItemType type, string subject = "",
        DataSyncVersionVector? recordVv = null) =>
        new(id, LinkId, ItemKind, key, type, DataSyncInboxDrafts.OriginOf(type), subject, "t", recordVv);

    private static DataSyncInboxDraft Draft(SyncKey key, DataSyncInboxItemType type, string subject = "") =>
        DataSyncInboxDrafts.Create(ItemKind, key, "1", type, subject,
            new DataSyncInboxPayload("E", null, "PC-1", null, null, [], null, null, null, 0, null, null, null, null, null),
            null, null, null, DataSyncMergeFlags.None);

    [TestMethod]
    public void EveryTypeHasItsOrigin()
    {
        var state = new[]
        {
            DataSyncInboxItemType.ChildDeletedInUse, DataSyncInboxItemType.MassChildDeletion,
            DataSyncInboxItemType.SuspectedLostUpdate, DataSyncInboxItemType.LargeChange,
        };
        foreach (var type in Enum.GetValues<DataSyncInboxItemType>())
        {
            Assert.AreEqual(state.Contains(type) ? DataSyncInboxItemOrigin.State : DataSyncInboxItemOrigin.Merger,
                DataSyncInboxDrafts.OriginOf(type), type.ToString());
        }
    }

    [TestMethod]
    public void AMergerDerivedItemNotProducedAgainCloses()
    {
        var open = new[]
        {
            Open(1, A, DataSyncInboxItemType.FieldConflict, "name"),
            Open(2, A, DataSyncInboxItemType.ChildRenameConflict, "child:1"),
            Open(3, B, DataSyncInboxItemType.FieldConflict, "name"),
        };
        var r = DataSyncInboxRules.Reconcile(open, [Draft(A, DataSyncInboxItemType.FieldConflict, "name")],
            [(ItemKind, A)], []);

        Assert.AreEqual(1L, r.Upserts.Single().ExistingId, "the same subject is updated, not duplicated");
        var close = r.Closes.Single();
        Assert.AreEqual(2, close.ItemId);
        Assert.AreEqual(DataSyncInboxClosure.Superseded, close.Closure);
        // B was not evaluated: its item is never touched.
    }

    [TestMethod]
    public void ATakenPeerRevisionClosesAsResolvedElsewhere()
    {
        var r = DataSyncInboxRules.Reconcile([Open(1, A, DataSyncInboxItemType.FieldConflict, "name")], [],
            [(ItemKind, A)], [new DataSyncClosureHint(ItemKind, A, DataSyncInboxClosure.ResolvedElsewhere, ThirdEditor)]);
        Assert.AreEqual(new DataSyncInboxClose(1, DataSyncInboxClosure.ResolvedElsewhere, ThirdEditor), r.Closes.Single());
    }

    [TestMethod]
    public void AStateDerivedItemIsNeverClosedForNotBeingProduced()
    {
        var r = DataSyncInboxRules.Reconcile(
            [Open(1, A, DataSyncInboxItemType.ChildDeletedInUse, "child:2"), Open(2, A, DataSyncInboxItemType.MassChildDeletion)],
            [], [(ItemKind, A)], []);
        Assert.AreEqual(0, r.Closes.Count);
    }

    [TestMethod]
    public void AHeldChildsItemSurvivesAnUnrelatedPullOfTheSameEntity()
    {
        // Pull 1 holds the child and opens the item.
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Genre", ("1", "Action"), ("2", "Horror")), Vv((Self, 3))));
        f.Local("1", A, T("Genre", ("1", "Action"), ("2", "Horror")), Vv((Self, 3)));
        f.Usage[(ItemKind, "1")] = new Dictionary<string, int> { ["2"] = 30 };
        var first = f.Pull(f.Record(A, T("Genre", ("1", "Action")), Vv((Self, 3), (Peer, 1))));
        var r1 = f.Merge();
        var item = r1.Inbox.Single();
        Assert.AreEqual(DataSyncInboxItemOrigin.State, item.Origin);

        // Pull 2 changes something unrelated; the child is held, so the merge never produces the item again.
        var g = new MergeFixture();
        g.Base(A, first, childMap: r1.BaseUpdates.Single().ChildMap);
        g.Local("1", A, T("Genre", ("1", "Action"), ("2", "Horror")), first.Vv,
            overlay: new DataSyncOverlay([], [new DataSyncHeldChild("2", LinkId)]));
        g.Pull(g.Record(A, T("Genres", ("1", "Action")), Vv((Self, 3), (Peer, 2))));
        var r2 = g.Merge();
        Assert.AreEqual(0, r2.Inbox.Count);
        Assert.IsTrue(r2.Evaluated.Contains((ItemKind, A)));

        var open = new DataSyncOpenInboxItem(9, LinkId, ItemKind, A, item.Type, item.Origin, item.SubjectPath, item.Token,
            item.RecordVv);
        Assert.AreEqual(0, DataSyncInboxRules.Reconcile([open], r2.Inbox, r2.Evaluated, r2.ClosureHints).Closes.Count);
        Assert.IsTrue(DataSyncInboxRules.StateItemStands(item.Type, new DataSyncStateItemFacts(HoldExists: true)));
        Assert.IsFalse(DataSyncInboxRules.StateItemStands(item.Type, new DataSyncStateItemFacts(HoldExists: false)),
            "it closes with its state");
    }

    [TestMethod]
    public void StateDerivedItemsCloseExactlyWhenTheirStateIsGone()
    {
        Assert.IsTrue(DataSyncInboxRules.StateItemStands(DataSyncInboxItemType.MassChildDeletion,
            new DataSyncStateItemFacts(BasePendingReason: DataSyncPendingReason.MassChildDeletion)));
        Assert.IsFalse(DataSyncInboxRules.StateItemStands(DataSyncInboxItemType.MassChildDeletion,
            new DataSyncStateItemFacts(BasePendingReason: DataSyncPendingReason.Conflict)));
        Assert.IsTrue(DataSyncInboxRules.StateItemStands(DataSyncInboxItemType.SuspectedLostUpdate,
            new DataSyncStateItemFacts(PublishHeld: true)));
        Assert.IsFalse(DataSyncInboxRules.StateItemStands(DataSyncInboxItemType.SuspectedLostUpdate,
            new DataSyncStateItemFacts()));
        Assert.IsTrue(DataSyncInboxRules.StateItemStands(DataSyncInboxItemType.LargeChange,
            new DataSyncStateItemFacts(LargeChangeRecordsWaiting: true)));
        Assert.IsFalse(DataSyncInboxRules.StateItemStands(DataSyncInboxItemType.LargeChange, new DataSyncStateItemFacts()));
        Assert.IsTrue(DataSyncInboxRules.StateItemStands(DataSyncInboxItemType.FieldConflict, new DataSyncStateItemFacts()),
            "merger-derived items do not close this way");
    }

    [TestMethod]
    public void DominanceClosesAnyLinksItem()
    {
        var item = Open(1, A, DataSyncInboxItemType.FieldConflict, "name", Vv((Peer, 2)));
        Assert.IsNull(DataSyncInboxRules.DominanceClosure(item, Vv((Peer, 1), (Self, 5)), SelfEditor, true),
            "not covered");
        Assert.AreEqual(new DataSyncInboxClose(1, DataSyncInboxClosure.ResolvedElsewhere, ThirdEditor),
            DataSyncInboxRules.DominanceClosure(item, Vv((Peer, 2), (Third, 3)), ThirdEditor, false));
        Assert.AreEqual(new DataSyncInboxClose(1, DataSyncInboxClosure.Superseded, null),
            DataSyncInboxRules.DominanceClosure(item, Vv((Peer, 2), (Self, 3)), SelfEditor, true));
        Assert.IsNull(DataSyncInboxRules.DominanceClosure(
            Open(2, A, DataSyncInboxItemType.ChildDeletedInUse, "child:1", Vv((Peer, 1))), Vv((Peer, 9)), null, false),
            "state-derived items close with their state only");
    }

    [TestMethod]
    public void TheAllowedActionsFollowTheTable()
    {
        static DataSyncInboxAction[] Allowed(DataSyncInboxItemType type, string subject = "", bool twoWay = true,
            DataSyncInboxPayload? payload = null) =>
            DataSyncInboxRules.AllowedActions(type, subject, payload, twoWay).ToArray();

        CollectionAssert.AreEqual(new[] { DataSyncInboxAction.KeepLocal, DataSyncInboxAction.UseRemote, DataSyncInboxAction.UseCustom, DataSyncInboxAction.Detach },
            Allowed(DataSyncInboxItemType.FieldConflict, "name"));
        CollectionAssert.DoesNotContain(Allowed(DataSyncInboxItemType.FieldConflict, "ignoreCase"), DataSyncInboxAction.UseCustom);
        CollectionAssert.DoesNotContain(Allowed(DataSyncInboxItemType.ChildRenameConflict, "node:1:parent"), DataSyncInboxAction.UseCustom);
        CollectionAssert.Contains(Allowed(DataSyncInboxItemType.ChildRenameConflict, "choice:1"), DataSyncInboxAction.UseCustom);
        CollectionAssert.DoesNotContain(Allowed(DataSyncInboxItemType.TypeChange, twoWay: false), DataSyncInboxAction.KeepLocal);
        CollectionAssert.DoesNotContain(Allowed(DataSyncInboxItemType.DeletedThere, twoWay: false), DataSyncInboxAction.RestoreEverywhere);
        CollectionAssert.AreEqual(new[] { DataSyncInboxAction.RestoreHere, DataSyncInboxAction.KeepDeleted },
            Allowed(DataSyncInboxItemType.DeletedHereEditedThere));
        CollectionAssert.AreEqual(new[] { DataSyncInboxAction.KeepBoth, DataSyncInboxAction.Skip },
            Allowed(DataSyncInboxItemType.LinkSuggestion, payload: PayloadWith(new DataSyncInboxCandidate("1", "x", "Tags", DataSyncNaturalMatch.Clash, false))),
            "no updatable candidate: no Link");
        CollectionAssert.AreEqual(new[] { DataSyncInboxAction.Publish, DataSyncInboxAction.Reapply },
            Allowed(DataSyncInboxItemType.SuspectedLostUpdate, twoWay: false));
        CollectionAssert.AreEqual(new[] { DataSyncInboxAction.ApplyAll }, Allowed(DataSyncInboxItemType.LargeChange));
    }

    private static DataSyncInboxPayload PayloadWith(DataSyncInboxCandidate candidate) =>
        new("E", null, null, null, null, [], null, null, null, 0, null, null, [candidate], null, null);
}

/// <summary>Tokens and payloads of inbox drafts (§9.1, §9.2).</summary>
[TestClass]
public class InboxDraftTests
{
    private static readonly DataSyncFieldOutcome Name = new("name", DataSyncFieldResolution.Conflict,
        new DataSyncDisplayValue("Artist"), new DataSyncDisplayValue("作者"), new DataSyncDisplayValue("Artists"),
        new DataSyncDisplayValue("作者"));

    [TestMethod]
    public void TheTokenIs32HexOverTypeSubjectAndFields()
    {
        var token = DataSyncInboxDrafts.Token(DataSyncInboxItemType.FieldConflict, "name", [Name]);
        Assert.AreEqual(32, token.Length);
        Assert.IsTrue(token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));
        Assert.AreEqual(token, DataSyncInboxDrafts.Token(DataSyncInboxItemType.FieldConflict, "name", [Name with { }]));
        Assert.AreNotEqual(token, DataSyncInboxDrafts.Token(DataSyncInboxItemType.FieldConflict, "name",
            [Name with { Remote = new DataSyncDisplayValue("Artist 3") }]), "another remote value is another question");
        Assert.AreNotEqual(token, DataSyncInboxDrafts.Token(DataSyncInboxItemType.ChildRenameConflict, "name", [Name]));
        Assert.AreEqual(token, DataSyncInboxDrafts.Token(DataSyncInboxItemType.FieldConflict, "name",
            [Name with { Result = new DataSyncDisplayValue("x") }]), "the result is not part of the question");
    }

    [TestMethod]
    public void AnUnrelatedPeerEditNeverInvalidatesACard()
    {
        // The enhancer adds a tag on the peer between two pulls: the conflict on the name keeps its token.
        var f = new MergeFixture();
        f.Base(A, f.Record(A, T("Artist", ("1", "X")), Vv((Self, 3))));
        f.Local("1", A, T("作者", ("1", "X")), Vv((Self, 4)));
        f.Pull(f.Record(A, T("Artists", ("1", "X")), Vv((Self, 3), (Peer, 1))));
        var before = f.Merge().Inbox.Single();

        var g = new MergeFixture();
        g.Base(A, g.Record(A, T("Artist", ("1", "X")), Vv((Self, 3))));
        g.Local("1", A, T("作者", ("1", "X")), Vv((Self, 4)));
        g.Pull(g.Record(A, T("Artists", ("1", "X"), ("2", "Tag")), Vv((Self, 3), (Peer, 2))));
        var after = g.Merge().Inbox.Single(i => i.SubjectPath == "name");

        Assert.AreEqual(before.Token, after.Token);
        Assert.AreNotEqual(before.RecordHash, after.RecordHash);
    }

    [TestMethod]
    public void ConflictPathsDecideTheItemType()
    {
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, DataSyncInboxDrafts.ConflictTypeOf("name"));
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, DataSyncInboxDrafts.ConflictTypeOf("settings.precision"));
        Assert.AreEqual(DataSyncInboxItemType.ChildRenameConflict, DataSyncInboxDrafts.ConflictTypeOf("choice:abc"));
        Assert.AreEqual(DataSyncInboxItemType.ChildRenameConflict, DataSyncInboxDrafts.ConflictTypeOf("node:abc:parent"));
        Assert.IsFalse(DataSyncInboxDrafts.IsChildPath("x:member"));
        Assert.IsFalse(DataSyncInboxDrafts.IsChildPath("ext:.mkv"));
    }

    [TestMethod]
    public void MultiRecordItemsReferToOneCombinedHash()
    {
        Assert.AreEqual(DataSyncInboxDrafts.CombinedRecordHash(["b", "a"]), DataSyncInboxDrafts.CombinedRecordHash(["a", "b"]));
        Assert.AreNotEqual(DataSyncInboxDrafts.CombinedRecordHash(["a"]), DataSyncInboxDrafts.CombinedRecordHash(["a", "b"]));
    }

    private static readonly SyncKey A = K(0xa);
}

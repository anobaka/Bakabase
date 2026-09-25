using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// The worked examples of §9.1 (design §6.8 A–I and J–N) as fixed multi-node scenarios, plus the engineering
/// critique's cases the test kind can express: (c) example D with concurrent and with sequential pulls, (e) a
/// rename onto another local option's label keeps both ids, (f) a cleared colour stays cleared everywhere, and
/// mutual Follow ending in items instead of a flip-flop. The custom-property-only cases — (a) node moves, (b) tag
/// groups, (d) IgnoreCase folding, example D under IgnoreCase — run with B's codec
/// (<c>DesignExamplesTests.CustomProperties.cs</c>).
/// </summary>
[TestClass]
public partial class DesignExamplesTests
{
    private readonly SimWorld _world = new();

    private SimClock _clock => _world.Clock;

    private SimNode Node(string name, bool headless = false) => _world.AddNode(name, headless);

    private static TestItemContent T(string name, params (string Id, string Label)[] children) =>
        new(name, null, children.Select(c => new TestChild(c.Id, c.Label)));

    private static (SimNode, SimNode) TwoWay(SimNode a, SimNode b)
    {
        a.Follow(b);
        b.Follow(a);
        return (a, b);
    }

    /// <summary>Both directions until nothing changes (at most a few rounds), then one more round as a check.</summary>
    private static void Settle(params SimNode[] nodes)
    {
        for (var round = 0; round < 6; round++)
        {
            var before = nodes.Sum(n => n.LastSeq);
            foreach (var node in nodes)
            {
                foreach (var link in node.Links.Values) node.Pull(link.Peer);
            }

            if (nodes.Sum(n => n.LastSeq) == before) return;
        }

        Assert.Fail("The nodes did not settle: a ping-pong.");
    }

    private static string Form(SimNode node, string name)
    {
        var row = node.Row(name);
        return DataSyncPublication.Of(TestItemCodec.Instance, row.Content!, row.Overlay, row.ChildrenLocal, row.OrderKey, row.Unknown)
            .SharedHash!;
    }

    private static void AssertSameForm(SimNode a, SimNode b, string name) => Assert.AreEqual(Form(a, name), Form(b, name),
        $"{a} and {b} differ on {name}: {a.Row(name).Content} / {b.Row(name).Content}");

    // ---- A -----------------------------------------------------------------------------------------

    [TestMethod]
    public void A_TheSamePropertyRenamedOnTwoDevices()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Artist", ("1", "X")));
        Settle(pc1, pc2);

        pc1.Edit("Artist", c => c.With(name: "Artists", children: [.. c.Children, new TestChild("2", "Added on PC-1")]));
        pc2.Edit("Artist", c => c.With(name: "作者"));
        pc2.Pull(pc1);
        pc1.Pull(pc2);

        // Both ask; each keeps its own name; options added meanwhile merged normally.
        var onPc2 = pc2.Item(DataSyncInboxItemType.FieldConflict);
        var onPc1 = pc1.Item(DataSyncInboxItemType.FieldConflict);
        Assert.AreEqual("name", onPc2.Subject);
        Assert.AreEqual("作者", onPc2.Payload.Fields.Single().Local!.Text);
        Assert.AreEqual("Artists", onPc2.Payload.Fields.Single().Remote!.Text);
        Assert.AreEqual("作者", pc2.Row("作者").Name);
        Assert.IsTrue(pc2.Row("作者").Item!.Children.Any(c => c.Label == "Added on PC-1"));
        Assert.IsTrue(pc2.Row("作者").Vv[pc1.Actor] < pc1.Row("Artists").Vv[pc1.Actor], "PC-2 never absorbs PC-1's counter");

        // PC-2 decides "作者": Max(L, R) + PC-2, dominating both.
        pc2.Resolve(onPc2, DataSyncInboxAction.KeepLocal);
        Assert.IsFalse(onPc2.Open);
        Assert.AreEqual(DataSyncVvRelation.Dominates, pc2.Row("作者").Vv.CompareTo(pc1.Row("Artists").Vv));

        // PC-1 fast-forwards, and its item closes as resolved on PC-2.
        pc1.Pull(pc2);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedElsewhere, onPc1.Closure);
        Assert.AreEqual("PC-2", onPc1.ClosedBy!.Name);
        Settle(pc1, pc2);
        AssertSameForm(pc1, pc2, "作者");
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
    }

    // ---- B -----------------------------------------------------------------------------------------

    [TestMethod]
    public void B_OneSideRenamesTheOtherAddsAnOption()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Genre", ("1", "Action")));
        Settle(pc1, pc2);

        pc1.Edit("Genre", c => c.With(name: "类型"));
        pc2.Edit("Genre", c => c.With(children: [.. c.Children, new TestChild("i", "Isekai")]));
        Settle(pc1, pc2);

        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
        AssertSameForm(pc1, pc2, "类型");
        CollectionAssert.AreEqual(new[] { "Action", "Isekai" }, pc1.Row("类型").Item!.Children.Select(c => c.Label).ToArray());
    }

    // ---- C -----------------------------------------------------------------------------------------

    [TestMethod]
    public void C_AnOptionDeletedOnOneSideIsInUseOnTheOther()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Genre", ("1", "Action"), ("2", "Horror")));
        Settle(pc1, pc2);
        pc2.Use("Genre", "2", 30);

        pc1.Edit("Genre", c => c.With(children: c.Children.Where(x => x.Label != "Horror")));
        pc2.Pull(pc1);

        var item = pc2.Item(DataSyncInboxItemType.ChildDeletedInUse);
        Assert.AreEqual(DataSyncInboxItemOrigin.State, item.Origin);
        Assert.AreEqual(30, item.Payload.UsageCount);
        Assert.IsTrue(pc2.Row("Genre").Item!.Children.Any(c => c.Label == "Horror"), "kept and held");
        AssertSameForm(pc1, pc2, "Genre");   // not published: nothing ping-pongs
        Settle(pc1, pc2);
        Assert.IsTrue(item.Open, "a state-derived item survives pulls");

        pc2.Resolve(item, DataSyncInboxAction.KeepHereOnly);
        CollectionAssert.AreEqual(new[] { "2" }, pc2.Row("Genre").Overlay.LocalOnlyChildren.ToArray());
        Settle(pc1, pc2);
        Assert.AreEqual(0, pc2.OpenDecisions, "never asked again");
        AssertSameForm(pc1, pc2, "Genre");
    }

    [TestMethod]
    public void C_AnUnusedOptionDeletedThereIsRemovedHere()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Genre", ("1", "Action"), ("2", "Horror")));
        Settle(pc1, pc2);

        pc1.Edit("Genre", c => c.With(children: c.Children.Where(x => x.Label != "Horror")));
        Settle(pc1, pc2);
        Assert.AreEqual(1, pc2.Row("Genre").Item!.Children.Count);
        Assert.AreEqual(DataSyncMergeNoteCodes.ChildrenRemoved, pc2.Notes.Single().Code);
    }

    // ---- D and critique (c) -------------------------------------------------------------------------

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void D_BothDevicesAddTheSameOption(bool concurrent)
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Genre", ("1", "Drama")));
        Settle(pc1, pc2);

        pc1.Edit("Genre", c => c.With(children: [.. c.Children, new TestChild("a1", "Action")]));
        if (!concurrent) Settle(pc1, pc2);
        pc2.Edit("Genre", c => c.With(children: [.. c.Children, new TestChild("b1", "Action")]));
        pc2.Pull(pc1);
        pc1.Pull(pc2);
        Settle(pc1, pc2);

        // Mapped, not duplicated; no rename, no item, and no further revision after settling.
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
        AssertSameForm(pc1, pc2, "Genre");
        var seqs = (pc1.LastSeq, pc2.LastSeq);
        Settle(pc1, pc2);
        Assert.AreEqual(seqs, (pc1.LastSeq, pc2.LastSeq), "no livelock");
        Assert.AreEqual(DataSyncVvRelation.Equal, pc1.Row("Genre").Vv.CompareTo(pc2.Row("Genre").Vv));
    }

    // ---- E -----------------------------------------------------------------------------------------

    [TestMethod]
    public void E_ATypeChangeIsAlwaysADecisionAndOnlyThatEntityWaits()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(new TestItemContent("Genre", null, [new TestChild("1", "A")], "MultipleChoice"));
        pc1.Create(T("Mood"));
        Settle(pc1, pc2);
        pc2.SetValues("Genre", 1234);

        pc1.Edit("Genre", c => c.With(type: "SingleChoice", name: "Genre!"));
        pc1.Edit("Mood", c => c.With(name: "Moods"));
        pc2.Pull(pc1);

        var item = pc2.Item(DataSyncInboxItemType.TypeChange);
        Assert.AreEqual("SingleChoice", item.Payload.RemoteSubtype);
        Assert.AreEqual(1234, item.Payload.ValueCount);
        Assert.AreEqual("MultipleChoice", pc2.Row("Genre").Item!.Type, "frozen for this link");
        Assert.IsNotNull(pc2.Find("Moods"), "everything else syncs");
        pc2.Pull(pc1);
        Assert.IsTrue(item.Open);
    }

    // ---- F -----------------------------------------------------------------------------------------

    [TestMethod]
    public void F_ADeletionAndAnEditAtTheSameTime()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Mood", ("1", "Calm")));
        Settle(pc1, pc2);
        pc2.SetValues("Mood", 5);

        pc1.Delete("Mood");
        pc2.Edit("Mood", c => c.With(children: [.. c.Children, new TestChild("2", "Tense")]));
        pc2.Pull(pc1);
        Assert.IsNotNull(pc2.Find("Mood"), "the edit wins on PC-2, silently");
        Assert.AreEqual(DataSyncMergeNoteCodes.EditWinsKept, pc2.Notes.Last().Code);
        Assert.AreEqual(0, pc2.OpenDecisions);

        pc1.Pull(pc2);
        var item = pc1.Item(DataSyncInboxItemType.DeletedHereEditedThere);
        Assert.AreEqual(DataSyncInboxDrafts.DetailChangedAfterDelete, item.Payload.Detail);
        Assert.IsNull(pc1.Find("Mood"), "never an automatic revive");

        // Keep it deleted: the tombstone dominates, and PC-2 is asked (it has values there).
        pc1.Resolve(item, DataSyncInboxAction.KeepDeleted);
        pc2.Pull(pc1);
        Assert.AreEqual(DataSyncInboxItemType.DeletedThere, pc2.Item(DataSyncInboxItemType.DeletedThere).Type);
    }

    [TestMethod]
    public void F_RestoreHereBringsTheDefinitionBack()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Mood", ("1", "Calm")));
        Settle(pc1, pc2);
        pc1.Delete("Mood");
        pc2.Edit("Mood", c => c.With(name: "Mood 2"));
        pc2.Pull(pc1);
        pc1.Pull(pc2);

        pc1.Resolve(pc1.Item(DataSyncInboxItemType.DeletedHereEditedThere), DataSyncInboxAction.RestoreHere);
        Settle(pc1, pc2);
        AssertSameForm(pc1, pc2, "Mood 2");
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
    }

    // ---- G -----------------------------------------------------------------------------------------

    [TestMethod]
    public void G_StarTopologyWithAHeadlessNasInTheMiddle()
    {
        var pc1 = Node("PC-1");
        var pc2 = Node("PC-2");
        var nas = Node("NAS", headless: true);
        TwoWay(pc1, nas);
        TwoWay(pc2, nas);
        pc1.Create(T("Artist"));
        Settle(pc1, nas, pc2);

        pc1.Edit("Artist", c => c.With(name: "Artists"));
        pc2.Edit("Artist", c => c.With(name: "作者"));

        nas.Pull(pc1);                                        // K5 fast-forward
        Assert.AreEqual("Artists", nas.Rows.Single().Name);
        var nasVv = nas.Rows.Single().Vv;
        nas.Pull(pc2);                                        // K6 concurrent: an item, no notification (headless)
        var nasItem = nas.Item(DataSyncInboxItemType.FieldConflict);
        Assert.AreEqual("Artists", nas.Rows.Single().Name);
        Assert.AreEqual(nasVv, nas.Rows.Single().Vv, "the NAS keeps its version and absorbs nothing");
        Assert.AreEqual(1, nas.Attention.OpenDecisions, "its heads report the decision");

        pc2.Pull(nas);                                        // PC-2 sees the conflict and decides
        pc2.Resolve(pc2.Item(DataSyncInboxItemType.FieldConflict), DataSyncInboxAction.KeepLocal);

        nas.Pull(pc2);                                        // K5: the NAS's item closes, resolved on PC-2
        Assert.AreEqual("作者", nas.Rows.Single().Name);
        Assert.AreEqual(DataSyncInboxClosure.ResolvedElsewhere, nasItem.Closure);
        Assert.AreEqual("PC-2", nasItem.ClosedBy!.Name);

        pc1.Pull(nas);                                        // PC-1 fast-forwards and never saw a conflict
        Assert.AreEqual("作者", pc1.Rows.Single().Name);
        Assert.AreEqual(0, pc1.Items.Count);
        Settle(pc1, nas, pc2);
        AssertSameForm(pc1, pc2, "作者");
    }

    // ---- H -----------------------------------------------------------------------------------------

    [TestMethod]
    public void H_TwoDevicesCreatedTheSamePropertySeparately()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        Settle(pc1, pc2);                                     // first contacts done
        pc1.Create(T("Rating", ("1", "Good")));
        pc2.Create(T("Rating", ("x", "Good")));
        pc2.Pull(pc1);

        var suggestion = pc2.Item(DataSyncInboxItemType.LinkSuggestion);
        Assert.AreEqual(DataSyncNaturalMatch.Identical, suggestion.Payload.Candidates!.Single().Match);
        Assert.AreEqual(1, pc2.Rows.Count(r => !r.Deleted), "never linked by name, and not created: it waits");

        pc2.Resolve(suggestion, DataSyncInboxAction.Link, targetLocalKey: suggestion.Payload.Candidates!.Single().LocalKey);
        Assert.AreEqual(1, pc2.Rows.Count(r => !r.Deleted));
        Assert.AreEqual(2, pc2.Rows.Single(r => !r.Deleted).Keys.Count, "keys become aliases; no id or value changes");

        // PC-2 publishes [its key, PC-1's key]: PC-1 key-matches its own and records the alias. Nobody asks again.
        Settle(pc1, pc2);
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
        Assert.AreEqual(2, pc1.Rows.Single(r => !r.Deleted).Keys.Count);
        AssertSameForm(pc1, pc2, "Rating");
    }

    [TestMethod]
    public void H_SkipIsRemembered()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        Settle(pc1, pc2);
        pc1.Create(T("Rating"));
        pc2.Create(T("rating"));
        pc2.Pull(pc1);

        pc2.Resolve(pc2.Item(DataSyncInboxItemType.LinkSuggestion), DataSyncInboxAction.Skip);
        pc1.Edit("Rating", c => c.With(children: [new TestChild("1", "Good")]));
        pc2.Pull(pc1);
        Assert.AreEqual(0, pc2.OpenDecisions, "a skipped entity is not proposed again");
        Assert.AreEqual(1, pc2.Rows.Count(r => !r.Deleted), "and no duplicate is created");
    }

    [TestMethod]
    public void H_KeepBothCreatesThePeersEntityUnderAnotherName()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        Settle(pc1, pc2);
        pc1.Create(T("Rating", ("1", "Good")));
        pc2.Create(T("Rating", ("x", "Bad")));
        pc2.Pull(pc1);

        pc2.Resolve(pc2.Item(DataSyncInboxItemType.LinkSuggestion), DataSyncInboxAction.KeepBoth);
        Assert.IsNotNull(pc2.Find("Rating (PC-1)"));
        Settle(pc1, pc2);
        // Two-way: the name is used everywhere.
        Assert.IsNotNull(pc1.Find("Rating (PC-1)"));
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
    }

    // ---- I -----------------------------------------------------------------------------------------

    [TestMethod]
    public void I_LocalChangesInAFollowedDefinition()
    {
        var pc1 = Node("PC-1");
        var nas = Node("NAS", headless: true);
        var pc2 = Node("PC-2");
        nas.Follow(pc1, DataSyncLinkMode.Follow);
        TwoWay(nas, pc2);
        pc1.Create(T("Genre"));
        pc1.Create(T("Mood"));
        nas.Pull(pc1);
        Settle(nas, pc2);

        // The NAS's own change to a field PC-1 changes too: PC-1 wins, with a note.
        nas.Edit("Genre", c => c.With(name: "Genre (NAS)"));
        pc1.Edit("Genre", c => c.With(name: "Genre (PC-1)"));
        nas.Pull(pc1);
        Assert.AreEqual("Genre (PC-1)", nas.Rows.Single(r => r.Keys.Contains(pc1.Row("Genre (PC-1)").Primary)).Name);
        Assert.AreEqual(DataSyncMergeNoteCodes.FollowOverride, nas.Notes.Last().Code);
        Assert.AreEqual(0, nas.OpenDecisions);

        // Exception: the value came from PC-2 through the two-way link; overriding it would undo PC-2's edit.
        pc2.Edit("Mood", c => c.With(name: "Mood (PC-2)"));
        nas.Pull(pc2);
        pc1.Edit("Mood", c => c.With(name: "Mood (PC-1)"));
        nas.Pull(pc1);
        Assert.AreEqual(DataSyncInboxItemType.FieldConflict, nas.Item(DataSyncInboxItemType.FieldConflict).Type);
        Assert.AreEqual("Mood (PC-2)", nas.Find("Mood (PC-2)")!.Name);
    }

    [TestMethod]
    public void MutualFollowEndsInItemsNotAFlipFlop()
    {
        var pc1 = Node("PC-1");
        var pc2 = Node("PC-2");
        pc1.Follow(pc2, DataSyncLinkMode.Follow);
        pc2.Follow(pc1, DataSyncLinkMode.Follow);
        pc1.Create(T("Genre"));
        Settle(pc1, pc2);

        pc1.Edit("Genre", c => c.With(name: "One"));
        pc2.Edit("Genre", c => c.With(name: "Two"));
        Settle(pc1, pc2);
        Assert.AreEqual("One", pc1.Rows.Single().Name);
        Assert.AreEqual("Two", pc2.Rows.Single().Name);
        Assert.AreEqual(1, pc1.OpenDecisions);
        Assert.AreEqual(1, pc2.OpenDecisions);
    }

    // ---- J -----------------------------------------------------------------------------------------

    [TestMethod]
    public void J_ASuspectedLostUpdate()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Genre", ("1", "Horror")));
        Settle(pc1, pc2);

        pc1.Edit("Genre", c => c.With(children: [new TestChild("1", "Horror films"), new TestChild("2", "Comedy")]));
        pc2.Pull(pc1);
        // A whole-row writer that read Genre before the apply writes it back afterwards.
        pc2.Edit("Genre", _ => T("Genre", ("1", "Horror")));
        _clock.Advance(TimeSpan.FromMinutes(2));
        pc2.Refresh();

        var item = pc2.Item(DataSyncInboxItemType.SuspectedLostUpdate);
        Assert.IsNull(item.LinkId, "it belongs to no link");
        Assert.IsTrue(pc2.Row("Genre").PublishHeld);
        pc1.Pull(pc2);
        Assert.AreEqual("Horror films", pc1.Row("Genre").Item!.Children[0].Label, "peers keep their version");

        pc1.Edit("Genre", c => c.With(name: "Genres"));
        pc2.Pull(pc1);
        Assert.AreEqual("Genre", pc2.Row("Genre").Name, "incoming changes wait");

        pc2.Resolve(item, DataSyncInboxAction.Reapply);
        Assert.IsFalse(pc2.Row("Genres").PublishHeld);
        Settle(pc1, pc2);
        AssertSameForm(pc1, pc2, "Genres");
        CollectionAssert.AreEqual(new[] { "Horror films", "Comedy" },
            pc2.Row("Genres").Item!.Children.Select(c => c.Label).ToArray());
    }

    [TestMethod]
    public void J_OutsideTheWindowARevertIsAnOrdinaryRevision()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Genre", ("1", "Horror")));
        Settle(pc1, pc2);
        pc1.Edit("Genre", c => c.With(children: [new TestChild("1", "Horror films")]));
        pc2.Pull(pc1);

        _clock.Advance(TimeSpan.FromMinutes(11));
        pc2.Edit("Genre", _ => T("Genre", ("1", "Horror")));
        Settle(pc1, pc2);
        Assert.AreEqual(0, pc2.OpenDecisions);
        Assert.AreEqual("Horror", pc1.Row("Genre").Item!.Children[0].Label);
    }

    // ---- K -----------------------------------------------------------------------------------------

    [TestMethod]
    public void K_ALargeChangeWaitsForApplyAll()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        for (var i = 0; i < 60; i++) pc1.Create(T("P" + i));
        Settle(pc1, pc2);                                     // the first contact is never a large change
        Assert.AreEqual(60, pc2.Rows.Count);

        for (var i = 0; i < 60; i++) pc1.Edit("P" + i, c => c.With(name: c.Name + "!"));
        pc2.Pull(pc1);
        var item = pc2.Item(DataSyncInboxItemType.LargeChange);
        Assert.AreEqual(60, item.Payload.ChildrenTotal);
        Assert.AreEqual(0, pc2.Rows.Count(r => r.Name.EndsWith('!')), "the side over its limit waits");

        pc2.Resolve(item, DataSyncInboxAction.ApplyAll);
        Assert.AreEqual(60, pc2.Rows.Count(r => r.Name.EndsWith('!')));
        Assert.IsFalse(item.Open, "its state is gone");
    }

    // ---- L -----------------------------------------------------------------------------------------

    [TestMethod]
    public void L_AnEntityDeletedOnThePeer()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Mood"));
        pc1.Create(T("Tone"));
        Settle(pc1, pc2);
        pc2.SetValues("Mood", 412);

        pc1.Delete("Mood");
        pc1.Delete("Tone");
        pc2.Pull(pc1);
        Assert.IsNull(pc2.Find("Tone"), "created by sync, no values: deleted by itself");
        var item = pc2.Item(DataSyncInboxItemType.DeletedThere);
        Assert.AreEqual(412, item.Payload.ValueCount);

        pc2.Resolve(item, DataSyncInboxAction.DeleteHere);
        Assert.IsNull(pc2.Find("Mood"));
        Settle(pc1, pc2);
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
    }

    [TestMethod]
    public void L_KeepItOnThisDeviceDetachesIt()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Mood"));
        Settle(pc1, pc2);
        pc2.SetValues("Mood", 3);
        pc1.Delete("Mood");
        pc2.Pull(pc1);

        pc2.Resolve(pc2.Item(DataSyncInboxItemType.DeletedThere), DataSyncInboxAction.KeepHereOnly);
        Assert.AreEqual(DataSyncEntitySyncState.Detached, pc2.Row("Mood").State);
        pc1.Create(T("Other"));
        Settle(pc1, pc2);
        Assert.AreEqual(0, pc2.OpenDecisions);
        Assert.IsNotNull(pc2.Find("Mood"));
    }

    // ---- M -----------------------------------------------------------------------------------------

    [TestMethod]
    [DataRow(DataSyncInboxAction.ReviewEach)]
    [DataRow(DataSyncInboxAction.ApplyAll)]
    public void M_AMassOptionDeletion(DataSyncInboxAction action)
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Genre", Enumerable.Range(0, 70).Select(i => (i.ToString(), "Tag" + i)).ToArray()));
        Settle(pc1, pc2);
        pc1.Use("Genre", "30", 2);

        pc2.Edit("Genre", c => c.With(name: "Genres", children: c.Children.Take(10)));
        pc1.Pull(pc2);
        var item = pc1.Item(DataSyncInboxItemType.MassChildDeletion);
        Assert.AreEqual(60, item.Payload.ChildrenTotal);
        Assert.AreEqual("Genre", pc1.Rows.Single().Name, "nothing of PC-2's change applies");

        pc1.Resolve(item, action);
        Assert.AreEqual("Genres", pc1.Rows.Single().Name, "the rest of the change applies");
        var inUse = pc1.OpenItems.Where(i => i.Type == DataSyncInboxItemType.ChildDeletedInUse).ToList();
        if (action == DataSyncInboxAction.ReviewEach)
        {
            Assert.AreEqual(60, inUse.Count, "every one becomes its own item, used or not");
            Assert.AreEqual(70, pc1.Rows.Single().Item!.Children.Count);
        }
        else
        {
            Assert.AreEqual(1, inUse.Count, "only the used one is held");
            Assert.AreEqual(11, pc1.Rows.Single().Item!.Children.Count);
        }
    }

    // ---- N -----------------------------------------------------------------------------------------

    [TestMethod]
    public void N_ADecisionWaitsOnAHeadlessHub()
    {
        var pc1 = Node("PC-1");
        var pc2 = Node("PC-2");
        var nas = Node("NAS", headless: true);
        TwoWay(pc1, nas);
        TwoWay(pc2, nas);
        pc1.Create(T("Mood"));
        Settle(pc1, nas, pc2);
        nas.SetValues("Mood", 12);

        pc1.Delete("Mood");
        Settle(pc1, nas, pc2);
        Assert.IsNotNull(nas.Find("Mood"), "the NAS keeps it until someone decides");
        Assert.IsNotNull(pc2.Find("Mood"), "so PC-2 never receives the deletion");
        Assert.AreEqual(1, nas.Attention.OpenDecisions, "readers see what waits there");
        Assert.IsTrue(nas.Attention.Headless);

        // Decided on the NAS (through server switching): the deletion flows on.
        nas.Resolve(nas.Item(DataSyncInboxItemType.DeletedThere), DataSyncInboxAction.DeleteHere);
        Settle(pc1, nas, pc2);
        Assert.IsNull(pc2.Find("Mood"));
    }

    // ---- critique (e) and (f) -------------------------------------------------------------------------

    [TestMethod]
    public void ARenameOntoAnotherLocalOptionsLabelKeepsBothIds()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(T("Genre", ("a1", "Action"), ("a2", "Drama")));
        Settle(pc1, pc2);

        pc1.Edit("Genre", c => c.With(children: c.Children.Select(x => x.Id == "a2" ? x with { Label = "Action" } : x)));
        Settle(pc1, pc2);
        CollectionAssert.AreEqual(new[] { "a1", "a2" }, pc2.Row("Genre").Item!.Children.Select(c => c.Id).ToArray());
        Assert.IsTrue(pc2.Row("Genre").Item!.Children.All(c => c.Label == "Action"));
        AssertSameForm(pc1, pc2, "Genre");
    }

    [TestMethod]
    public void AClearedColourStaysClearedEverywhere()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(new TestItemContent("Genre", "#e5484d", []));
        Settle(pc1, pc2);

        pc1.Edit("Genre", c => c.With(clearColor: true));
        pc2.Edit("Genre", c => c.With(name: "Genres"));
        Settle(pc1, pc2);
        Assert.IsNull(pc1.Row("Genres").Item!.Color);
        Assert.IsNull(pc2.Row("Genres").Item!.Color);
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions);
    }

    // ---- §8.5.5: a concurrent appearance change merged crosswise ---------------------------------------

    [TestMethod]
    public void AConcurrentAppearanceChangeMergedCrosswiseSettlesWithoutAnotherRevision()
    {
        var (pc1, pc2) = TwoWay(Node("PC-1"), Node("PC-2"));
        pc1.Create(new TestItemContent("Genre", "#e5484d", []));
        pc1.Create(new TestItemContent("Mood", null, []));
        Settle(pc1, pc2);

        // Both recolour Genre and move it, then both take their snapshot before either applies.
        pc1.Edit("Genre", c => c.With(color: "#0090ff"));
        pc1.Move(pc1.Row("Genre"), 1);
        pc2.Edit("Genre", c => c.With(color: "#30a46c"));
        Assert.AreEqual(SimPullOutcome.Applied, pc1.Fetch(pc1.LinkTo(pc2), out var toPc1));
        Assert.AreEqual(SimPullOutcome.Applied, pc2.Fetch(pc2.LinkTo(pc1), out var toPc2));
        pc1.Redeliver(pc1.LinkTo(pc2), toPc1!);
        pc2.Redeliver(pc2.LinkTo(pc1), toPc2!);

        // Each merged the other's revision against its own: both picked the same winner, so both hold one content.
        Assert.AreEqual(0, pc1.OpenDecisions + pc2.OpenDecisions, "appearance is never an item");
        AssertSameForm(pc1, pc2, "Genre");

        // The two revisions are concurrent with equal content: the next pulls end at Max(Vv), no counter issued.
        var counters = (pc1.ActorCounter, pc2.ActorCounter);
        var max = DataSyncVersionVector.Max(pc1.Row("Genre").Vv, pc2.Row("Genre").Vv);
        pc1.Pull(pc2);
        pc2.Pull(pc1);
        Assert.AreEqual(counters, (pc1.ActorCounter, pc2.ActorCounter), "no further revision");
        Assert.AreEqual(max, pc1.Row("Genre").Vv);
        Assert.AreEqual(max, pc2.Row("Genre").Vv);
        var seqs = (pc1.LastSeq, pc2.LastSeq);
        Settle(pc1, pc2);
        Assert.AreEqual(seqs, (pc1.LastSeq, pc2.LastSeq), "settled");
        AssertSameForm(pc1, pc2, "Genre");
    }
}

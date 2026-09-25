using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.M3;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>§8.5.4 for multilevel nodes: moves, cycles, whole subtrees and their usage.</summary>
[TestClass]
public class Merge3TreeTests
{
    private static readonly CustomPropertyContentV1 World = Tree("R", false,
        N("asia", "Asia", N("jp", "Japan", N("kyoto", "Kyoto")), N("kr", "Korea")),
        N("eu", "Europe", N("fr", "France")));

    [TestMethod]
    public void APeerMoveIsTakenAndAppendedUnderTheNewParentWithItsSubtree()
    {
        var remote = Tree("R", false,
            N("asia", "Asia", N("kr", "Korea")),
            N("eu", "Europe", N("fr", "France"), N("jp", "Japan", N("kyoto", "Kyoto"))));
        var result = Merge(World, World, remote);
        Assert.AreEqual("Asia(Korea),Europe(France,Japan(Kyoto))", Shape(result));
        var field = Field(result, "node:jp:parent");
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, field.Resolution);
        CollectionAssert.AreEqual(new[] { "Asia" }, field.Local!.Path!.ToArray());
        CollectionAssert.AreEqual(new[] { "Europe" }, field.Remote!.Path!.ToArray());
        Assert.AreEqual(0, result.RemovedChildIds.Count);
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void AMoveHereIsKeptAndMovesOnBothSidesAreAConflict()
    {
        var @base = Tree("R", false, N("asia", "Asia", N("jp", "Japan")), N("eu", "Europe"), N("af", "Africa"));
        var local = Tree("R", false, N("asia", "Asia"), N("eu", "Europe", N("jp", "Japan")), N("af", "Africa"));
        var kept = Merge(@base, local, @base);
        Assert.AreEqual("Asia,Europe(Japan),Africa", Shape(kept));
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(kept, "node:jp:parent").Resolution);

        var remote = Tree("R", false, N("asia", "Asia"), N("eu", "Europe"), N("af", "Africa", N("jp", "Japan")));
        var conflict = Merge(@base, local, remote);
        Assert.AreEqual("Asia,Europe(Japan),Africa", Shape(conflict));
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(conflict, "node:jp:parent").Resolution);

        var follow = Merge(@base, local, remote, mode: DataSyncLinkMode.Follow);
        Assert.AreEqual("Asia,Europe,Africa(Japan)", Shape(follow));
    }

    [TestMethod]
    public void MovesThatWouldFormACycleKeepTheirLocalParentsAndBecomeConflicts()
    {
        // Here A went under B; on the peer B went under A.
        var @base = Tree("R", false, N("a", "A"), N("b", "B"));
        var local = Tree("R", false, N("b", "B", N("a", "A")));
        var remote = Tree("R", false, N("a", "A", N("b", "B")));
        var result = Merge(@base, local, remote);
        Assert.AreEqual("B(A)", Shape(result));
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(result, "node:b:parent").Resolution);
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(result, "node:a:parent").Resolution);

        // The same in Follow: the peer's move would still close the cycle.
        var follow = Merge(@base, local, remote, mode: DataSyncLinkMode.Follow);
        Assert.AreEqual("B(A)", Shape(follow));
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(follow, "node:b:parent").Resolution);
    }

    [TestMethod]
    public void AMoveUnderAParentDeletedHereBringsTheParentBack()
    {
        var @base = Tree("R", false, N("asia", "Asia", N("jp", "Japan")), N("eu", "Europe"));
        var local = Tree("R", false, N("asia", "Asia", N("jp", "Japan")));
        var remote = Tree("R", false, N("asia", "Asia"), N("eu", "Europe", N("jp", "Japan")));
        var result = Merge(@base, local, remote);
        Assert.AreEqual("Asia,Europe(Japan)", Shape(result));
        Assert.AreEqual(DataSyncFieldResolution.EditWinsRestored, Field(result, "node:eu").Resolution);
        CollectionAssert.AreEqual(new[] { "eu" }, result.AddedChildIds.ToArray());
    }

    [TestMethod]
    public void RenamesAndAddsFindTheirPlaceByClass()
    {
        // Two local "Asia" roots are one class: the peer's add under Asia lands under its representative.
        var @base = Tree("R", true, N("asia", "Asia", N("jp", "Japan")));
        var local = Tree("R", true, N("asia", "Asia", N("jp", "Japan")), N("asia2", "ASIA", N("kr", "Korea")));
        var remote = Tree("R", true, N("asia", "Asia", N("jp", "Nippon"), N("cn", "China")));
        var result = Merge(@base, local, remote);
        Assert.AreEqual("Asia(Nippon,China),ASIA(Korea)", Shape(result));
        CollectionAssert.AreEqual(new[] { "cn" }, result.AddedChildIds.ToArray());
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(result, "node:jp").Resolution);
        CollectionAssert.AreEqual(new[] { "Asia", "Nippon" }, Field(result, "node:jp").Result!.Path!.ToArray());
        NoConflicts(result);
    }

    [TestMethod]
    public void APeerDeletionTakesTheWholeSubtreeWhenNothingInItIsUsed()
    {
        var remote = Tree("R", false, N("eu", "Europe", N("fr", "France")));
        var result = Merge(World, World, remote);
        Assert.AreEqual("Europe(France)", Shape(result));
        CollectionAssert.AreEqual(new[] { "asia", "jp", "kyoto", "kr" }, result.RemovedChildIds.ToArray());
        Assert.AreEqual(1, result.Fields.Count, "one outcome for the subtree");
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(result, "node:asia").Resolution);
        CollectionAssert.AreEqual(new[] { "asia", "jp", "kyoto", "kr" },
            Untyped.ChildDeletionCandidates(Merge3ChildTests.Candidates(World, World, remote)).ToArray(),
            "the usage of every node of the subtree is asked");
    }

    [TestMethod]
    public void ASubtreeWithANodeInUseIsHeldAsOneClass()
    {
        var remote = Tree("R", false, N("eu", "Europe", N("fr", "France")));
        var usage = Unused(World);
        usage["kyoto"] = 2;
        var result = Merge(World, World, remote, usage: usage);
        Assert.AreEqual("Asia(Japan(Kyoto),Korea),Europe(France)", Shape(result), "kept here");
        CollectionAssert.AreEqual(new[] { "asia" }, result.HeldChildIds.ToArray(), "the class's members; the subtree goes with them");
        Assert.AreEqual(DataSyncFieldResolution.DeletionHeldInUse, Field(result, "node:asia").Resolution);
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void ASubtreeWithSomethingAddedHereKeepsItsRoot()
    {
        var @base = Tree("R", false, N("asia", "Asia", N("jp", "Japan"), N("kr", "Korea")), N("eu", "Europe"));
        var local = Tree("R", false, N("asia", "Asia", N("jp", "Japan"), N("kr", "Korea"), N("cn", "China")), N("eu", "Europe"));
        var remote = Tree("R", false, N("eu", "Europe"));
        var result = Merge(@base, local, remote);
        Assert.AreEqual("Asia(China),Europe", Shape(result), "Asia stays for China; Japan and Korea go");
        CollectionAssert.AreEqual(new[] { "jp", "kr" }, result.RemovedChildIds.ToArray());
    }

    [TestMethod]
    public void ANodeMovedAwayByThePeerIsNotDeletedWithItsOldParent()
    {
        var remote = Tree("R", false, N("eu", "Europe", N("fr", "France"), N("jp", "Japan", N("kyoto", "Kyoto"))));
        var result = Merge(World, World, remote);
        Assert.AreEqual("Europe(France,Japan(Kyoto))", Shape(result));
        CollectionAssert.AreEqual(new[] { "asia", "kr" }, result.RemovedChildIds.ToArray());
    }

    [TestMethod]
    public void ChildDeletionCandidatesCoverTheReadingsOfEveryLinkMode()
    {
        // Here Japan moved under Europe; the peer moved it under Africa and deleted Europe. In two-way the move is a
        // conflict and Japan keeps Europe alive; in Follow Japan leaves and Europe goes.
        var @base = Tree("R", false, N("asia", "Asia", N("jp", "Japan")), N("eu", "Europe"), N("af", "Africa"));
        var local = Tree("R", false, N("asia", "Asia"), N("eu", "Europe", N("jp", "Japan")), N("af", "Africa"));
        var remote = Tree("R", false, N("asia", "Asia"), N("af", "Africa", N("jp", "Japan")));
        CollectionAssert.AreEqual(new[] { "eu" },
            Untyped.ChildDeletionCandidates(Merge3ChildTests.Candidates(@base, local, remote)).ToArray());

        var twoWay = Merge(@base, local, remote);
        Assert.AreEqual("Asia,Europe(Japan),Africa", Shape(twoWay));
        Assert.AreEqual(0, twoWay.RemovedChildIds.Count);
        var follow = Merge(@base, local, remote, mode: DataSyncLinkMode.Follow);
        Assert.AreEqual("Asia,Africa(Japan)", Shape(follow));
        CollectionAssert.AreEqual(new[] { "eu" }, follow.RemovedChildIds.ToArray());
    }

    /// <summary>
    /// Two roots of one local class that different peer classes own: the class is one here and ends as one, so nothing
    /// under it moves. Moving l1 to the other root is no peer's change (§8.5.2 b|l|b → l, §8.5.4 steps 6 and 8).
    /// </summary>
    [TestMethod]
    public void NothingMovesWhileTheParentsEndInOneClass()
    {
        var @base = Tree("R", false, N("a1", "A", N("x1", "x")), N("b1", "B"));
        var local = Tree("R", false, N("a1", "A", N("x1", "x")), N("b1", "A", N("l1", "x")));

        // The peer changed nothing.
        var unchanged = Merge(@base, local, @base);
        Assert.AreEqual(Canon(local), Canon(Merged(unchanged)));
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(unchanged, "node:b1").Resolution);
        NoField(unchanged, "node:x1:parent");

        // The peer only renamed the property.
        var renamed = Merge(@base, local, @base with { Name = "Region" });
        Assert.AreEqual(Canon(local with { Name = "Region" }), Canon(Merged(renamed)));

        // No base, two-way: the key is a conflict and stays; nothing moves.
        var noBase = Merge(null, local, Tree("R", false, N("a1", "A", N("x1", "x")), N("b1", "B")),
            DataSyncMerge3Mode.NoBase);
        Assert.AreEqual("A(x),A(x)", Shape(noBase));
        Assert.AreEqual(Canon(local), Canon(Merged(noBase)));
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(noBase, "node:b1").Resolution);
    }

    /// <summary>
    /// The peer renamed one root of a local class; a node this device added under that root stays with it, as merging
    /// the other way keeps it (§8.5.4 step 8). In a FastForward R is the truth: the class goes along, and says so.
    /// </summary>
    [TestMethod]
    public void ANodeAddedUnderARenamedMemberStaysWithIt_UnlessAFastForwardTakesTheClassAlong()
    {
        var @base = Tree("R", false, N("a1", "A", N("x1", "x")), N("b1", "A"));
        var local = Tree("R", false, N("a1", "A", N("x1", "x")), N("b1", "A", N("l1", "x")));
        var remote = Tree("R", false, N("a1", "A", N("x1", "x")), N("b1", "B"));
        var threeWay = Merge(@base, local, remote);
        Assert.AreEqual("A(x),B(x)", Shape(threeWay));
        NoConflicts(threeWay);
        var expected = Form(Peer(Tree("R", false, N("p", "A", N("q", "x")), N("s", "B", N("t", "x")))));
        Assert.AreEqual(expected, PublishedForm(threeWay));
        Assert.AreEqual(expected, PublishedForm(Merge(@base, remote, local)), "the other way: the same form");

        var fastForward = Merge(null, local, remote, DataSyncMerge3Mode.FastForward,
            deletions: DataSyncChildDeletionMode.Apply);
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(fastForward));
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(fastForward, "node:x1:parent").Resolution,
            "l1 moved with its class");
    }

    [TestMethod]
    public void OverlayNodesAndTheirSubtreesAreNeverTouched()
    {
        var overlay = LocalOnly("jp");
        var remote = Tree("R", false,
            N("asia", "Asia", N("kr", "Korea")),
            N("eu", "Europe", N("fr", "France"), N("kyoto", "Kyoto2")));
        var result = Merge(World, World, remote, overlay: overlay);
        Assert.AreEqual("Asia(Japan(Kyoto),Korea),Europe(France)", Shape(result),
            "neither moved nor renamed nor deleted, and nothing added for it");
        Assert.AreEqual(0, result.RemovedChildIds.Count);
        Assert.AreEqual(0, result.AddedChildIds.Count);
    }
}

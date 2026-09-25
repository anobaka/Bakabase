using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.M3;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// §3.3, §8.5.4 step 0: local options this device keeps but never publishes — without a uuid, or below a node without
/// one — are kept by every merge, and a peer's class never disappears into one. What this device publishes after a
/// FastForward is the peer's form (the closure property), and the result agrees with itself: every added id and every
/// child map target is an option of it, and no default value names a missing one.
/// </summary>
[TestClass]
public class Merge3UnpublishedTests
{
    [TestMethod]
    public void APeerClassMeetingALocalOptionWithoutAnId_IsStoredInIt()
    {
        var local = Choice("G", true, C(null, "Action", "#1"), C("l2", "Drama"));
        var remote = Choice("G", true, C("r1", "action", "#2"), C("l2", "Drama"));
        var result = Merge(null, local, remote, DataSyncMerge3Mode.FastForward);

        // The option keeps its label and place, and takes the peer's id and colour: one member of the class, published.
        Assert.AreEqual(Canon(Choice("G", true, C("r1", "Action", "#2"), C("l2", "Drama"))), Canon(Merged(result)));
        CollectionAssert.AreEqual(new[] { "r1" }, result.AddedChildIds.ToArray());
        Assert.AreEqual("r1", result.ChildMap["r1"]);
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
        Assert.AreEqual(0, Warnings(result.Warnings, DataSyncWarningCode.OptionLabelConflict).Count);
    }

    [TestMethod]
    public void ATagWithoutAnId_TakesThePeersTagOfItsClass()
    {
        var local = Tags("T", true, T(null, null, "Kyoto"));
        var remote = Tags("T", true, T("r1", "", "KYOTO", "#2"));
        var result = Merge(null, local, remote, DataSyncMerge3Mode.NoBase);

        Assert.AreEqual(Canon(Tags("T", true, T("r1", null, "Kyoto", "#2"))), Canon(Merged(result)));
        CollectionAssert.AreEqual(new[] { "r1" }, result.AddedChildIds.ToArray());
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void ThreeWay_OnlyWhatThePeerAddsOrRestoresIsStoredInIt()
    {
        // This device's Action lost its id: as far as anyone can tell it deleted the class.
        var @base = Choice("G", true, C("a", "Action"), C("d", "Drama"));
        var local = Choice("G", true, C(null, "Action"), C("d", "Drama"));

        // Unchanged at the peer since the base: this device's deletion stands, and its option stays as it is.
        var stands = Merge(@base, local, @base);
        Assert.AreEqual(Canon(local), Canon(Merged(stands)));
        Assert.AreEqual(0, stands.AddedChildIds.Count);

        // Changed at the peer: edit wins, and the class comes back in the option without an id.
        var recoloured = Merge(@base, local, Choice("G", true, C("a", "Action", "#3"), C("d", "Drama")));
        Assert.AreEqual(Canon(Choice("G", true, C("a", "Action", "#3"), C("d", "Drama"))), Canon(Merged(recoloured)));
        CollectionAssert.AreEqual(new[] { "a" }, recoloured.AddedChildIds.ToArray());
        Assert.AreEqual(1, Warnings(recoloured.Warnings, DataSyncWarningCode.ChildRestored).Count);

        // Added at the peer since the base.
        var addedBase = Choice("G", true, C("d", "Drama"));
        var added = Merge(addedBase, local, Choice("G", true, C("d", "Drama"), C("r1", "ACTION")));
        Assert.AreEqual(Canon(Choice("G", true, C("r1", "Action"), C("d", "Drama"))), Canon(Merged(added)));
        Assert.AreEqual("r1", added.ChildMap["r1"]);
    }

    [TestMethod]
    public void ADefaultValueNamesTheOptionItsClassIsStoredIn()
    {
        var local = Single("S", true, C(null, "b"));
        var remote = Single("S", true, C("b0", "B")) with { DefaultValue = [Ref("b0", "B")] };
        var result = Merge(null, local, remote, DataSyncMerge3Mode.FastForward);

        var merged = Merged(result);
        Assert.AreEqual(Canon(Single("S", true, C("b0", "b")) with { DefaultValue = [Ref("b0", "b")] }), Canon(merged));
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void AChildlessNodeWithoutAnId_TakesTheAddedNodeAndItsChildren()
    {
        var local = Tree("R", true, N("e", "Europe"), N(null, "Asia"));
        var remote = Tree("R", true, N("e", "Europe"), N("p", "ASIA", N("j", "Japan")));
        var result = Merge(null, local, remote, DataSyncMerge3Mode.FastForward);

        Assert.AreEqual(Canon(Tree("R", true, N("e", "Europe"), N("p", "Asia", N("j", "Japan")))), Canon(Merged(result)));
        CollectionAssert.AreEquivalent(new[] { "p", "j" }, result.AddedChildIds.ToArray());
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void ANodeBelowAParentWithoutAnId_IsNoCounterpart_ThePeersClassIsAddedBeside()
    {
        // Japan is not published here (its parent has no id), so the peer's Japan cannot be matched to it: it is added
        // under the peer's Asia, under a fresh id since "x" is taken. A node without an id that has children keeps them
        // and stays as it is: taking the add would publish its subtree too.
        var local = Tree("R", true, N(null, "Asia", N("x", "Japan")));
        var remote = Tree("R", true, N("p", "Asia", N("x", "Japan", "#1")));
        var result = Merge(null, local, remote, DataSyncMerge3Mode.FastForward);

        var merged = Merged(result);
        Assert.AreEqual("Asia(Japan),Asia(Japan)", Shape(result));
        Assert.IsNull(merged.Nodes[0].Uuid);
        Assert.AreEqual("x", merged.Nodes[0].Children[0].Uuid, "the local node stays where it was");
        Assert.AreEqual("p", merged.Nodes[1].Uuid);
        var fresh = merged.Nodes[1].Children[0].Uuid!;
        Assert.AreNotEqual("x", fresh);
        Assert.AreEqual(fresh, result.ChildMap["x"]);
        Assert.AreEqual("x", Arg(Warnings(result.Warnings, DataSyncWarningCode.OptionUuidRemapped).Single(), "uuid"));
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void NothingIsFoldedIntoAnOptionThisDeviceDoesNotPublish()
    {
        // "k" has a colour past the reader's limit, and the second "dup" repeats an id: both are kept here and never
        // published. The peer's classes of their keys are added beside them, not folded into them.
        var badColour = new string('c', 65);
        var local = Choice("G", true, C("k", "Kyoto", badColour), C("dup", "Action"), C("dup", "Drama"));
        var remote = Choice("G", true, C("dup", "Action"), C("r1", "KYOTO", "#1"), C("r2", "drama"));
        var result = Merge(null, local, remote, DataSyncMerge3Mode.FastForward);

        Assert.AreEqual(Canon(Choice("G", true, C("k", "Kyoto", badColour), C("dup", "Action"), C("dup", "Drama"),
            C("r1", "KYOTO", "#1"), C("r2", "drama"))), Canon(Merged(result)));
        CollectionAssert.AreEquivalent(new[] { "r1", "r2" }, result.AddedChildIds.ToArray());
        Assert.AreEqual("r2", result.ChildMap["r2"], "never the option the published dup is");
        Assert.AreEqual(0, Warnings(result.Warnings, DataSyncWarningCode.OptionLabelConflict).Count);
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void AnOverlayChildStaysTheOnlyInvisibleCounterpart()
    {
        // Below a local-only node, a peer's class is mapped and left alone (§3.6): nothing is added beside it.
        var local = Tree("R", true, N("a", "Asia", N("x", "Japan")));
        var remote = Tree("R", true, N("a", "Asia", N("x", "Japan", "#1")));
        var result = Merge(null, local, remote, DataSyncMerge3Mode.FastForward, overlay: LocalOnly("a"));

        Assert.AreEqual(Canon(local), Canon(Merged(result)));
        Assert.AreEqual(0, result.AddedChildIds.Count);
        Assert.AreEqual("x", result.ChildMap["x"]);
    }
}

using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.M3;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>§8.5.4 step 7 (B4, mass child deletion) and the <c>ChildDeletions</c> modes an item's flags choose.</summary>
[TestClass]
public class Merge3MassDeletionTests
{
    private static CustomPropertyContentV1 TagList(int count) =>
        Tags("T", false, Enumerable.Range(0, count).Select(i => T($"t{i}", null, $"Tag {i}")).ToArray());

    [TestMethod]
    public void B4TripsAboveFiftyClasses()
    {
        var local = TagList(300);
        var fifty = Merge(local, local, TagList(250));
        Assert.AreEqual(0, fifty.MassDeletionCandidates.Count, "exactly 50 is allowed");
        Assert.AreEqual(50, fifty.RemovedChildIds.Count);

        var fiftyOne = Merge(local, local, TagList(249));
        CollectionAssert.AreEqual(Enumerable.Range(249, 51).Select(i => $"t{i}").ToArray(),
            fiftyOne.MassDeletionCandidates.ToArray());
        Assert.AreEqual(Canon(local), Canon(Merged(fiftyOne)), "Merged == Local");
        Assert.AreEqual(0, fiftyOne.Fields.Count, "nothing of the entity applies");
        Assert.AreEqual(0, fiftyOne.RemovedChildIds.Count);
        Assert.AreEqual(0, fiftyOne.HeldChildIds.Count);
        Assert.AreEqual(300, fiftyOne.ChildMap.Count, "the child map is left as it was");
    }

    [TestMethod]
    public void B4TripsAboveTwentyPercentOfTenClassesOrMore()
    {
        Assert.AreEqual(0, Merge(TagList(10), TagList(10), TagList(8)).MassDeletionCandidates.Count, "exactly 20 %");
        Assert.AreEqual(3, Merge(TagList(10), TagList(10), TagList(7)).MassDeletionCandidates.Count);
        Assert.AreEqual(0, Merge(TagList(9), TagList(9), TagList(6)).MassDeletionCandidates.Count,
            "fewer than MinChildrenForRatio classes");
    }

    [TestMethod]
    public void AnItemsChildDeletionsDecideWhatHappensInstead()
    {
        var local = TagList(10);
        var remote = TagList(7) with
        {
            Tags = TagList(7).Tags.Select(t => t.Uuid == "t0" ? t with { Name = "Renamed" } : t).ToArray(),
        };
        var usage = Unused(local);
        usage["t7"] = 1;

        var apply = Merge(local, local, remote, usage: usage, deletions: DataSyncChildDeletionMode.Apply);
        CollectionAssert.AreEqual(new[] { "t8", "t9" }, apply.RemovedChildIds.ToArray(), "B4 skipped: unused ones go");
        CollectionAssert.AreEqual(new[] { "t7" }, apply.HeldChildIds.ToArray(), "used ones are held with items");
        Assert.AreEqual("Renamed", Merged(apply).Tags[0].Name);

        var reviewEach = Merge(local, local, remote, usage: usage, deletions: DataSyncChildDeletionMode.ReviewEach);
        CollectionAssert.AreEqual(new[] { "t7", "t8", "t9" }, reviewEach.HeldChildIds.ToArray());
        Assert.AreEqual(3, reviewEach.Fields.Count(f => f.Resolution == DataSyncFieldResolution.DeletionHeldInUse),
            "one item per class, usage 0 included");
        Assert.AreEqual(0, reviewEach.RemovedChildIds.Count);

        var restore = Merge(local, local, remote, usage: usage, deletions: DataSyncChildDeletionMode.Restore);
        Assert.AreEqual(10, Merged(restore).Tags.Count, "kept");
        Assert.AreEqual(0, restore.HeldChildIds.Count, "not held");
        Assert.AreEqual(0, restore.RemovedChildIds.Count);
        Assert.AreEqual(0, restore.Fields.Count(f => f.Resolution == DataSyncFieldResolution.DeletionHeldInUse));
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(restore, "tag:t8").Resolution);
        Assert.AreEqual("Renamed", Merged(restore).Tags[0].Name, "everything else applies");
    }

    [TestMethod]
    public void TheThresholdsComeFromTheCodecsPolicy()
    {
        var codec = new CustomPropertyCodec(policy: new DataSyncAutoApplyPolicy
        {
            MaxChildDeletionsPerEntity = 2, MinChildrenForRatio = 1_000,
        });
        Assert.AreEqual(3, Merge(TagList(300), TagList(300), TagList(297), codec: codec).MassDeletionCandidates.Count);
        Assert.AreEqual(0, Merge(TagList(300), TagList(300), TagList(298), codec: codec).MassDeletionCandidates.Count);
    }

    [TestMethod]
    public void AMultilevelSubtreeCountsEveryClassInIt()
    {
        var local = Tree("R", false,
            Enumerable.Range(0, 5).Select(i => N($"p{i}", $"P{i}", N($"c{i}", $"C{i}"))).ToArray());
        var remote = Tree("R", false, N("p0", "P0", N("c0", "C0")), N("p1", "P1", N("c1", "C1")),
            N("p2", "P2", N("c2", "C2")), N("p3", "P3", N("c3", "C3")));
        // One subtree of two classes out of ten: 20 %.
        Assert.AreEqual(0, Merge(local, local, remote).MassDeletionCandidates.Count);
        var fewer = Tree("R", false, N("p0", "P0", N("c0", "C0")), N("p1", "P1", N("c1", "C1")),
            N("p2", "P2", N("c2", "C2")));
        CollectionAssert.AreEqual(new[] { "p3", "c3", "p4", "c4" }, Merge(local, local, fewer).MassDeletionCandidates.ToArray());
    }
}

/// <summary>§8.5.4 step 9: <c>defaultValue</c> merges as the classes it names and is translated through the class map.</summary>
[TestClass]
public class Merge3DefaultValueTests
{
    [TestMethod]
    public void ThePeersDefaultIsTranslatedThroughTheClassMap()
    {
        var @base = Choice("G", false, C("pa", "Action"), C("pd", "Drama")) with { DefaultValue = [Ref("pd", "Drama")] };
        var local = Choice("G", false, C("a", "Action"), C("d", "Drama")) with { DefaultValue = [Ref("d", "Drama")] };
        var remote = @base with { DefaultValue = [Ref("pa", "Action")] };
        var result = Merge(@base, local, remote, childMap: new Dictionary<string, string> { ["pa"] = "a", ["pd"] = "d" });
        CollectionAssert.AreEqual(new[] { Ref("a", "Action") }, Merged(result).DefaultValue.ToArray());
        var field = Field(result, "defaultValue");
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, field.Resolution);
        Assert.AreEqual("Drama", field.Local!.Text);
        Assert.AreEqual("Action", field.Result!.Text);
    }

    [TestMethod]
    public void ARenameIsNotAChangeOfTheDefault()
    {
        var @base = Choice("G", false, C("a", "A"), C("d", "D")) with { DefaultValue = [Ref("d", "D")] };
        var local = @base with { DefaultValue = [Ref("a", "A")] };
        var remote = Choice("G", false, C("a", "A"), C("d", "D2")) with { DefaultValue = [Ref("d", "D2")] };
        var result = Merge(@base, local, remote);
        CollectionAssert.AreEqual(new[] { Ref("a", "A") }, Merged(result).DefaultValue.ToArray());
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(result, "defaultValue").Resolution);
        CollectionAssert.AreEqual(new[] { "A", "D2" }, ChoiceLabels(result));
        NoConflicts(result);
    }

    [TestMethod]
    public void ARefToAClassGoneHereIsDroppedNotAConflict()
    {
        var @base = Choice("G", false, C("a", "A"), C("h", "H")) with { DefaultValue = [Ref("a", "A")] };
        var local = Choice("G", false, C("a", "A")) with { DefaultValue = [Ref("a", "A")] };
        var remote = @base with { DefaultValue = [Ref("h", "H")] };
        var result = Merge(@base, local, remote);
        Assert.AreEqual(0, Merged(result).DefaultValue.Count);
        Assert.AreEqual("h", Arg(Warnings(result.Warnings, DataSyncWarningCode.DefaultValueRefDropped).Single(), "uuid"));
        NoConflicts(result);
    }

    [TestMethod]
    public void ALocalRefToAnOptionTheMergeRemovedIsDropped()
    {
        var @base = Choice("G", false, C("a", "A"), C("h", "H")) with { DefaultValue = [Ref("a", "A")] };
        var local = @base with { DefaultValue = [Ref("a", "A"), Ref("h", "H")] };
        var remote = Choice("G", false, C("a", "A")) with { DefaultValue = [Ref("a", "A")] };
        var result = Merge(@base, local, remote);
        CollectionAssert.AreEqual(new[] { "h" }, result.RemovedChildIds.ToArray());
        CollectionAssert.AreEqual(new[] { Ref("a", "A") }, Merged(result).DefaultValue.ToArray());
        Assert.AreEqual("h", Arg(Warnings(result.Warnings, DataSyncWarningCode.DefaultValueRefDropped).Single(), "uuid"));
    }

    [TestMethod]
    public void MultilevelDefaultsAreTranslatedAndRebuiltByPath()
    {
        var @base = Tree("R", false, N("asia", "Asia", N("jp", "Japan")), N("eu", "Europe", N("fr", "France")))
            with { DefaultValue = [NodeRef("jp", "Asia", "Japan")] };
        var toFrance = Merge(@base, @base, @base with { DefaultValue = [NodeRef("fr", "Europe", "France")] });
        CollectionAssert.AreEqual(new[] { NodeRef("fr", "Europe", "France") }, Merged(toFrance).DefaultValue.ToArray());

        // Unchanged, but its ancestor was renamed: the ref's path follows.
        var renamed = Tree("R", false, N("asia", "Orient", N("jp", "Japan")), N("eu", "Europe", N("fr", "France")))
            with { DefaultValue = [NodeRef("jp", "Orient", "Japan")] };
        var result = Merge(@base, @base, renamed);
        CollectionAssert.AreEqual(new[] { NodeRef("jp", "Orient", "Japan") }, Merged(result).DefaultValue.ToArray());
        NoField(result, "defaultValue");
        Assert.AreEqual(Form(Peer(renamed)), PublishedForm(result));
    }

    [TestMethod]
    public void WhileChildrenAreKeptLocalTheDefaultIsUntouched()
    {
        var @base = Choice("G", false, C("a", "A"), C("d", "D")) with { DefaultValue = [Ref("d", "D")] };
        var result = Merge(@base, @base, @base with { DefaultValue = [Ref("a", "A")] }, localChildrenLocal: true);
        CollectionAssert.AreEqual(new[] { Ref("d", "D") }, Merged(result).DefaultValue.ToArray());
        NoField(result, "defaultValue");
    }
}

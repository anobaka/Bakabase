using System.Diagnostics;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.M3;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// Cases the symmetry property found, kept as examples: two devices merging each other's change from one base end at
/// one form. Each names the rule it pins.
/// </summary>
[TestClass]
public class Merge3ExampleTests
{
    /// <summary>Both directions, without conflicts, end at <paramref name="expected"/>.</summary>
    private static void BothWays(CustomPropertyContentV1 @base, CustomPropertyContentV1 l, CustomPropertyContentV1 r,
        CustomPropertyContentV1 expected)
    {
        var lr = Merge(@base, l, r, winner: DataSyncMergeSide.Local);
        var rl = Merge(@base, r, l, winner: DataSyncMergeSide.Remote);
        NoConflicts(lr);
        NoConflicts(rl);
        Assert.AreEqual(Form(Peer(expected)), PublishedForm(lr), "this device's merge");
        Assert.AreEqual(Form(Peer(expected)), PublishedForm(rl), "the peer's merge");
    }

    [TestMethod]
    public void ADuplicateDeletedThereGoesWhenItsClassMovedAwayHere()
    {
        // "A" was one class of two members. Here the first became "Z"; there the second was deleted.
        var @base = Choice("G", false, C("b0", "A"), C("b4", "A"));
        BothWays(@base, Choice("G", false, C("b0", "Z"), C("b4", "A")), Choice("G", false, C("b0", "A")),
            Choice("G", false, C("b0", "Z")));
    }

    [TestMethod]
    public void MovingADuplicateIntoAnotherExistingClassChangesNothing()
    {
        // There, the second "x" was renamed to "y": both classes still exist, so the form did not change. Here, "y" and
        // the second "x" were deleted.
        var @base = Choice("G", false, C("b0", "x"), C("b1", "y"), C("b2", "x"));
        BothWays(@base, Choice("G", false, C("b0", "x")), Choice("G", false, C("b0", "x"), C("b1", "y"), C("b2", "y")),
            Choice("G", false, C("b0", "x")));
    }

    [TestMethod]
    public void AMoveBetweenTwoMembersOfOneClassIsCarriedWhenAnotherChangeSplitsThem()
    {
        // Two "E" roots are one class; there, "b" moved from the second to the first. Here, the first was renamed.
        var @base = Tree("R", false, N("b0", "E"), N("b3", "E", N("b4", "b")));
        var local = Tree("R", false, N("b0", "aB"), N("b3", "E", N("b4", "b")));
        var remote = Tree("R", false, N("b0", "E", N("b4", "b")), N("b3", "E"));
        BothWays(@base, local, remote, Tree("R", false, N("b0", "aB", N("b4", "b")), N("b3", "E")));
    }

    [TestMethod]
    public void MembersOfOneClassWithDifferentHistoriesMergeAgainstTheirOwnBase()
    {
        // "b9" was renamed here into B; "b14" was renamed there into AB: each rename is one side's only.
        var @base = Choice("G", true, C("b9", "aB"), C("b14", "B"), C("b19", "ab"));
        var local = Choice("G", true, C("b14", "B"), C("b19", "ab"), C("b9", "b"));
        var remote = Choice("G", true, C("b9", "aB"), C("b14", "ab"), C("b19", "ab"));
        BothWays(@base, local, remote, Choice("G", true, C("b9", "b"), C("b14", "ab"), C("b19", "ab")));
    }

    [TestMethod]
    public void AClassColourComesFromTheMemberThatIsThatClassOnBothSides()
    {
        // There "AB" was recoloured through its first member; here that member was deleted and "é" renamed into AB.
        var @base = Choice("G", true, C("b0", "ab"), C("b1", "é"), C("b2", "ab"));
        var local = Choice("G", true, C("b1", "aB"), C("b2", "ab"));
        var remote = Choice("G", true, C("b0", "ab", "#2"), C("b1", "é"), C("b2", "ab"));
        BothWays(@base, local, remote, Choice("G", true, C("b2", "ab", "#2")));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void LargePropertiesMergeInLinearTime()
    {
        // 12,000 options on each side, no id shared: every peer class is matched by key or added.
        var local = Tags("T", false, Enumerable.Range(0, 12_000).Select(i => T($"l{i}", null, $"Tag {i}")).ToArray());
        var remote = Tags("T", false, Enumerable.Range(6_000, 12_000).Select(i => T($"r{i}", null, $"Tag {i}")).ToArray());
        var watch = Stopwatch.StartNew();
        var result = Merge(null, local, remote, DataSyncMerge3Mode.NoBase);
        watch.Stop();
        Assert.AreEqual(18_000, Merged(result).Tags.Count);
        Assert.AreEqual(6_000, result.AddedChildIds.Count);
        Assert.AreEqual(Form(Peer(Tags("T", false,
            Enumerable.Range(0, 18_000).Select(i => T($"x{i}", null, $"Tag {i}")).ToArray()))), PublishedForm(result));
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed}");
    }
}

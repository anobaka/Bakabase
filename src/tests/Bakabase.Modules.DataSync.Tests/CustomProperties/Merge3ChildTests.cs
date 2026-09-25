using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.M3;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>§8.5.4 steps 1–4 and 8 for choices and tags: renames, adds and deletions by usage, as label classes.</summary>
[TestClass]
public class Merge3ChildTests
{
    // ---- renames (step 1) ---------------------------------------------------------------------

    [TestMethod]
    public void APeerRenameIsTaken()
    {
        var @base = Choice("G", false, C("a", "Action"), C("d", "Drama"));
        var result = Merge(@base, @base, Choice("G", false, C("a", "Fight"), C("d", "Drama")));
        CollectionAssert.AreEqual(new[] { "Fight", "Drama" }, ChoiceLabels(result));
        CollectionAssert.AreEqual(new[] { "a", "d" }, ChoiceIds(result));
        var field = Field(result, "choice:a");
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, field.Resolution);
        Assert.AreEqual("Action", field.Local!.Text);
        Assert.AreEqual("Fight", field.Result!.Text);
    }

    [TestMethod]
    public void ALocalRenameIsKeptAndABothSidesRenameIsAConflict()
    {
        var @base = Choice("G", false, C("a", "Action"), C("d", "Drama"));
        var local = Choice("G", false, C("a", "Brawl"), C("d", "Drama"));
        var kept = Merge(@base, local, @base);
        CollectionAssert.AreEqual(new[] { "Brawl", "Drama" }, ChoiceLabels(kept));
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(kept, "choice:a").Resolution);

        var conflict = Merge(@base, local, Choice("G", false, C("a", "Fight"), C("d", "Drama")));
        CollectionAssert.AreEqual(new[] { "Brawl", "Drama" }, ChoiceLabels(conflict));
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(conflict, "choice:a").Resolution);
    }

    [TestMethod]
    public void ARenameOntoAKeyAnotherLocalClassHasKeepsBothIdsAsOneClass()
    {
        var @base = Choice("G", false, C("a", "Action"), C("d", "Drama"));
        var local = Choice("G", false, C("a", "Action"), C("d", "Drama"), C("f", "Fight"));
        var remote = Choice("G", false, C("a", "Fight"), C("d", "Drama"));
        var result = Merge(@base, local, remote);
        CollectionAssert.AreEqual(new[] { "a", "d", "f" }, ChoiceIds(result), "nothing is dropped or folded");
        CollectionAssert.AreEqual(new[] { "Fight", "Drama", "Fight" }, ChoiceLabels(result));
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result), "one class Fight");
        NoConflicts(result);

        // The peer renamed Action onto its own Drama: one peer class, two local counterparts, both kept.
        var merged = Merge(@base, @base, Choice("G", false, C("a", "Drama"), C("d", "Drama")));
        CollectionAssert.AreEqual(new[] { "Drama", "Drama" }, ChoiceLabels(merged));
        CollectionAssert.AreEqual(new[] { "a", "d" }, ChoiceIds(merged));
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(merged, "choice:a").Resolution);
    }

    [TestMethod]
    public void EveryMemberOfALocalClassFollowsItsRename()
    {
        // "action" is this device's duplicate of Action: one class, never published apart.
        var @base = Choice("G", true, C("a", "Action"));
        var local = Choice("G", true, C("a", "Action"), C("a2", "action"));
        var remote = Choice("G", true, C("a", "Fight"));
        var result = Merge(@base, local, remote);
        CollectionAssert.AreEqual(new[] { "Fight", "Fight" }, ChoiceLabels(result));
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
        Assert.AreEqual(0, result.RemovedChildIds.Count);
    }

    [TestMethod]
    public void ACaseOnlyRenameUnderIgnoreCaseIsNoChange()
    {
        var @base = Choice("G", true, C("a", "Action"));
        var result = Merge(@base, @base, Choice("G", true, C("a", "ACTION")));
        CollectionAssert.AreEqual(new[] { "Action" }, ChoiceLabels(result));
        Assert.AreEqual(0, result.Fields.Count);
    }

    [TestMethod]
    public void ATagRenameMovesItToAnotherGroupAndNullEqualsEmpty()
    {
        var @base = Tags("T", false, T("k", "Studio", "Kyoto"), T("i", null, "Isekai"));
        var local = Tags("T", false, T("k", "Studio", "Kyoto"), T("i", null, "Isekai"));
        var remote = Tags("T", false, T("k", "Place", "Kyoto"), T("i", "", "Isekai"));
        var result = Merge(@base, local, remote);
        var tags = Merged(result).Tags;
        Assert.AreEqual("Place", tags[0].Group);
        Assert.IsNull(tags[1].Group, "\"\" and null are one group: no change");
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(result, "tag:k").Resolution);
        NoField(result, "tag:i");
        Assert.AreEqual("Studio", Field(result, "tag:k").Local!.Group);
    }

    // ---- adds (step 2) ------------------------------------------------------------------------

    [TestMethod]
    public void PeerAddsAreAppendedAfterLocalChildrenInThePeersOrder()
    {
        var @base = Choice("G", false, C("a", "A"));
        var local = Choice("G", false, C("a", "A"), C("x", "X"));
        var remote = Choice("G", false, C("n2", "N2"), C("a", "A"), C("n1", "N1", "#f00"));
        var result = Merge(@base, local, remote);
        CollectionAssert.AreEqual(new[] { "a", "x", "n2", "n1" }, ChoiceIds(result));
        CollectionAssert.AreEqual(new[] { "n2", "n1" }, result.AddedChildIds.ToArray());
        Assert.AreEqual("#f00", Merged(result).Choices[3].Color);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(result, "choice:n1").Resolution);
        Assert.IsNull(Field(result, "choice:n1").Local);
        Assert.AreEqual("n1", result.ChildMap["n1"]);
        Assert.AreEqual("a", result.ChildMap["a"]);
    }

    [TestMethod]
    public void APeerClassWithSeveralMembersIsAddedOnceAndEveryMemberMapped()
    {
        var @base = Choice("G", true, C("a", "A"));
        var remote = Choice("G", true, C("a", "A"), C("p1", "Isekai"), C("p2", "ISEKAI"));
        var result = Merge(@base, @base, remote);
        CollectionAssert.AreEqual(new[] { "a", "p1" }, ChoiceIds(result));
        CollectionAssert.AreEqual(new[] { "p1" }, result.AddedChildIds.ToArray());
        Assert.AreEqual("p1", result.ChildMap["p1"]);
        Assert.AreEqual("p1", result.ChildMap["p2"]);
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void AnAddOfAKeyThisDeviceAlreadyHasIsMappedNotDuplicated()
    {
        var @base = Choice("G", true, C("a", "A"));
        var local = Choice("G", true, C("a", "A"), C("x", "Action", "#111"));
        var remote = Choice("G", true, C("a", "A"), C("p", "action", "#222"));
        var result = Merge(@base, local, remote, winner: DataSyncMergeSide.Remote);
        CollectionAssert.AreEqual(new[] { "a", "x" }, ChoiceIds(result));
        Assert.AreEqual("x", result.ChildMap["p"]);
        Assert.AreEqual(0, result.AddedChildIds.Count);
        Assert.AreEqual("#222", Merged(result).Choices[1].Color, "both added it: the appearance winner's colour");
        NoField(result, "choice:p");
    }

    [TestMethod]
    public void AnAddWhoseIdIsTakenHereGetsAFreshId()
    {
        // This device folded the peer's two "A"s into a; its own "Zed" happens to have the id x.
        var @base = Choice("G", false, C("p", "A"), C("x", "A"));
        var local = Choice("G", false, C("a", "A"), C("x", "Zed"));
        var remote = Choice("G", false, C("p", "A"), C("x", "B"));
        var result = Merge(@base, local, remote, childMap: new Dictionary<string, string> { ["p"] = "a", ["x"] = "a" });
        var added = result.AddedChildIds.Single();
        Assert.AreNotEqual("x", added);
        CollectionAssert.AreEqual(new[] { "a", "x", added }, ChoiceIds(result));
        CollectionAssert.AreEqual(new[] { "A", "Zed", "B" }, ChoiceLabels(result));
        var warning = Warnings(result.Warnings, DataSyncWarningCode.OptionUuidRemapped).Single();
        Assert.AreEqual("x", Arg(warning, "uuid"));
        Assert.AreEqual(added, Arg(warning, "newUuid"));
        Assert.AreEqual(added, result.ChildMap["x"]);
        Assert.AreEqual("a", result.ChildMap["p"]);
    }

    // ---- peer deletions (steps 3 and 8) ---------------------------------------------------------

    [TestMethod]
    public void APeerDeletionOfAnUnusedClassRemovesEveryMember()
    {
        var @base = Choice("G", true, C("a", "Action"), C("h", "Horror"), C("d", "Drama"));
        var local = Choice("G", true, C("a", "Action"), C("h", "Horror"), C("h2", "horror"), C("d", "Drama"));
        var remote = Choice("G", true, C("a", "Action"), C("d", "Drama"));
        var result = Merge(@base, local, remote);
        CollectionAssert.AreEqual(new[] { "a", "d" }, ChoiceIds(result));
        CollectionAssert.AreEqual(new[] { "h", "h2" }, result.RemovedChildIds.ToArray());
        var field = Field(result, "choice:h");
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, field.Resolution);
        Assert.IsNull(field.Result);
        CollectionAssert.AreEqual(new[] { "h", "h2" }, Untyped.ChildDeletionCandidates(Candidates(@base, local, remote)).ToArray());
        Assert.IsFalse(result.ChildMap.ContainsKey("h"));
    }

    [TestMethod]
    public void APeerDeletionOfAClassInUseHoldsEveryMemberWithOneItem()
    {
        var @base = Choice("G", true, C("a", "Action"), C("h", "Horror"));
        var local = Choice("G", true, C("a", "Action"), C("h", "Horror"), C("h2", "horror"));
        var remote = Choice("G", true, C("a", "Action"));
        var used = Unused(local);
        used["h2"] = 30;
        var result = Merge(@base, local, remote, usage: used);
        CollectionAssert.AreEqual(new[] { "a", "h", "h2" }, ChoiceIds(result), "kept here");
        CollectionAssert.AreEqual(new[] { "h", "h2" }, result.HeldChildIds.ToArray());
        Assert.AreEqual(0, result.RemovedChildIds.Count);
        Assert.AreEqual(1, result.Fields.Count(f => f.Resolution == DataSyncFieldResolution.DeletionHeldInUse));
        Assert.AreEqual(DataSyncFieldResolution.DeletionHeldInUse, Field(result, "choice:h").Resolution);
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result), "held: not published, so nothing ping-pongs");

        // A missing usage entry counts as in use.
        var unknown = Unused(local);
        unknown.Remove("h");
        Assert.AreEqual(2, Merge(@base, local, remote, usage: unknown).HeldChildIds.Count);
    }

    [TestMethod]
    public void APeerDeletionOfAChildChangedHereIsKept()
    {
        var @base = Choice("G", false, C("a", "Action"), C("h", "Horror"));
        var remote = Choice("G", false, C("a", "Action"));
        foreach (var local in new[]
                 {
                     Choice("G", false, C("a", "Action"), C("h", "Horror films")),
                     Choice("G", false, C("a", "Action"), C("h", "Horror", "#000")),
                 })
        {
            var result = Merge(@base, local, remote);
            CollectionAssert.AreEqual(new[] { "a", "h" }, ChoiceIds(result), "edit wins");
            Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(result, "choice:h").Resolution);
            Assert.AreEqual(0, Untyped.ChildDeletionCandidates(Candidates(@base, local, remote)).Count);
        }
    }

    [TestMethod]
    public void AChildThatIsNotPublishedIsNeverACandidate()
    {
        var @base = Choice("G", false, C("a", "Action"), C("k", "K"));
        var remote = Choice("G", false, C("a", "Action"));
        // A colour past the reader's limit: the reader would drop it, so it is not published.
        var local = Choice("G", false, C("a", "Action"), C("k", "K", new string('c', 65)), C(null, "No id"));
        var result = Merge(@base, local, remote);
        CollectionAssert.AreEqual(new[] { "Action", "K", "No id" }, ChoiceLabels(result));
        Assert.AreEqual(0, result.RemovedChildIds.Count);
        Assert.AreEqual(0, result.HeldChildIds.Count);
        Assert.AreEqual(0, Untyped.ChildDeletionCandidates(Candidates(@base, local, remote)).Count);
    }

    // ---- local deletions (step 1) -------------------------------------------------------------

    [TestMethod]
    public void ALocalDeletionStandsUnlessThePeerChangedTheChildSince()
    {
        var @base = Choice("G", false, C("a", "Action"), C("h", "Horror"));
        var local = Choice("G", false, C("a", "Action"));

        var stands = Merge(@base, local, @base);
        CollectionAssert.AreEqual(new[] { "a" }, ChoiceIds(stands));
        Assert.AreEqual(0, stands.Fields.Count);
        Assert.IsFalse(stands.ChildMap.ContainsKey("h"));

        foreach (var remote in new[]
                 {
                     Choice("G", false, C("a", "Action"), C("h", "Horror films")),
                     Choice("G", false, C("a", "Action"), C("h", "Horror", "#000")),
                 })
        {
            var restored = Merge(@base, local, remote);
            CollectionAssert.AreEqual(new[] { "a", "h" }, ChoiceIds(restored));
            CollectionAssert.AreEqual(new[] { "h" }, restored.AddedChildIds.ToArray());
            Assert.AreEqual(DataSyncFieldResolution.EditWinsRestored, Field(restored, "choice:h").Resolution);
            Assert.AreEqual("h", Arg(Warnings(restored.Warnings, DataSyncWarningCode.ChildRestored).Single(), "uuid"));
        }
    }

    // ---- colours (§8.5.5) ---------------------------------------------------------------------

    [TestMethod]
    public void AColourSetOrClearedOnOneSideIsTaken()
    {
        var plain = Choice("G", false, C("a", "A"));
        var red = Choice("G", false, C("a", "A", "#f00"));

        var set = Merge(plain, plain, red);
        Assert.AreEqual("#f00", Merged(set).Choices[0].Color);
        Assert.AreEqual(DataSyncFieldResolution.AppearanceTookRemote, Field(set, "choice:a:color").Resolution);

        var cleared = Merge(red, red, plain);
        Assert.IsNull(Merged(cleared).Choices[0].Color, "a cleared colour is a value in ThreeWay");

        var fastForward = Merge(null, red, plain, DataSyncMerge3Mode.FastForward);
        Assert.IsNull(Merged(fastForward).Choices[0].Color);

        var noBase = Merge(null, red, plain, DataSyncMerge3Mode.NoBase);
        Assert.AreEqual("#f00", Merged(noBase).Choices[0].Color, "without a base a colour is never cleared");
        Assert.AreEqual(DataSyncFieldResolution.AppearanceKeptLocal, Field(noBase, "choice:a:color").Resolution);
        var noBaseGives = Merge(null, plain, red, DataSyncMerge3Mode.NoBase);
        Assert.AreEqual("#f00", Merged(noBaseGives).Choices[0].Color);
    }

    [TestMethod]
    public void BothChangingAColourTakesTheAppearanceWinnersAndNeverAsks()
    {
        var @base = Choice("G", false, C("a", "A", "#111"));
        var local = Choice("G", false, C("a", "A", "#222"));
        var remote = Choice("G", false, C("a", "A", "#333"));
        var localWins = Merge(@base, local, remote, winner: DataSyncMergeSide.Local);
        Assert.AreEqual("#222", Merged(localWins).Choices[0].Color);
        Assert.AreEqual(DataSyncFieldResolution.AppearanceKeptLocal, Field(localWins, "choice:a:color").Resolution);
        var remoteWins = Merge(@base, local, remote, winner: DataSyncMergeSide.Remote);
        Assert.AreEqual("#333", Merged(remoteWins).Choices[0].Color);
        NoConflicts(remoteWins);
    }

    [TestMethod]
    public void AColourGoesToTheRepresentativeOfTheClassAsItEnds()
    {
        // This device's class ACTION has "action" first: its representative, whose colour is the class's.
        var @base = Choice("G", true, C("a", "Action", "#111"));
        var local = Choice("G", true, C("x", "action"), C("a", "Action", "#111"));
        var remote = Choice("G", true, C("a", "Action", "#222"));
        var result = Merge(@base, local, remote, winner: DataSyncMergeSide.Remote);
        Assert.AreEqual("#222", Merged(result).Choices[0].Color);
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    // ---- modes --------------------------------------------------------------------------------

    [TestMethod]
    public void FastForwardDeletesByUsage()
    {
        var local = Choice("G", false, C("a", "A"), C("h", "H"), C("k", "K"));
        var remote = Choice("G", false, C("a", "A"));
        var usage = Unused(local);
        usage["k"] = 5;
        var result = Merge(null, local, remote, DataSyncMerge3Mode.FastForward, usage: usage);
        CollectionAssert.AreEqual(new[] { "h" }, result.RemovedChildIds.ToArray());
        CollectionAssert.AreEqual(new[] { "k" }, result.HeldChildIds.ToArray());
        Assert.AreEqual(Form(Peer(remote)), PublishedForm(result));
    }

    [TestMethod]
    public void NoBaseUnionsAndDeletesNothing()
    {
        var local = Choice("G", false, C("a", "A"), C("b", "B"), C("x", "X"));
        var remote = Choice("G", false, C("b", "B"), C("c", "C"), C("x", "Y"));
        var twoWay = Merge(null, local, remote, DataSyncMerge3Mode.NoBase);
        CollectionAssert.AreEqual(new[] { "A", "B", "X", "C" }, ChoiceLabels(twoWay));
        Assert.AreEqual(0, twoWay.RemovedChildIds.Count);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(twoWay, "choice:x").Resolution,
            "a key that differs for the same id");

        var follow = Merge(null, local, remote, DataSyncMerge3Mode.NoBase, mode: DataSyncLinkMode.Follow);
        CollectionAssert.AreEqual(new[] { "A", "B", "Y", "C" }, ChoiceLabels(follow));
        Assert.AreEqual(DataSyncFieldResolution.FollowTookRemote, Field(follow, "choice:x").Resolution);
    }

    [TestMethod]
    public void AnIgnoreCaseConflictMatchesWithTheLocalComparerUntilDecided()
    {
        var local = Choice("G", false, C("a", "Action"));
        var remote = Choice("G", true, C("p", "action"));
        var twoWay = Merge(null, local, remote, DataSyncMerge3Mode.NoBase);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(twoWay, "ignoreCase").Resolution);
        Assert.AreEqual(false, Merged(twoWay).IgnoreCase);
        CollectionAssert.AreEqual(new[] { "Action", "action" }, ChoiceLabels(twoWay), "two classes under Ordinal");

        var follow = Merge(null, local, remote, DataSyncMerge3Mode.NoBase, mode: DataSyncLinkMode.Follow);
        Assert.AreEqual(true, Merged(follow).IgnoreCase);
        CollectionAssert.AreEqual(new[] { "Action" }, ChoiceLabels(follow), "one class under IgnoreCase");
        Assert.AreEqual("a", follow.ChildMap["p"]);
    }

    // ---- childrenLocal (§3.6) -----------------------------------------------------------------

    [TestMethod]
    public void WhileChildrenLocalIsOnChildrenAreUntouched()
    {
        var @base = Choice("G", false, C("a", "A"));
        var local = Choice("G", false, C("x", "Mine"));
        var remote = Choice("G", false, C("a", "A"), C("n", "N"));
        var result = Merge(@base, local, remote, localChildrenLocal: true);
        CollectionAssert.AreEqual(new[] { "x" }, ChoiceIds(result), "nothing added, nothing deleted");
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(result, "childrenLocal").Resolution);
        Assert.IsFalse(result.Fields.Any(f => f.Path.StartsWith("choice:", StringComparison.Ordinal)));
        Assert.IsFalse(Merged(result).ChildrenLocal, "unchanged here: the flag stays on the side row");

        // The peer turned it on: its record has no options, and none is deleted here.
        var turnedOn = Merge(@base, @base, Choice("G", false) with { ChildrenLocal = true });
        CollectionAssert.AreEqual(new[] { "a" }, ChoiceIds(turnedOn));
        Assert.IsTrue(Merged(turnedOn).ChildrenLocal);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(turnedOn, "childrenLocal").Resolution);
    }

    [TestMethod]
    public void TurningChildrenLocalOffUnionsByTheNoBaseRules()
    {
        var @base = Choice("G", false) with { ChildrenLocal = true };
        var local = Choice("G", false, C("a", "A"), C("x", "X"));
        var remote = Choice("G", false, C("a", "A"), C("n", "N"));
        var result = Merge(@base, local, remote, localChildrenLocal: true, baseChildrenLocal: true);
        CollectionAssert.AreEqual(new[] { "A", "X", "N" }, ChoiceLabels(result));
        Assert.AreEqual(0, result.RemovedChildIds.Count);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(result, "childrenLocal").Resolution);
        Assert.AreEqual(false, Field(result, "childrenLocal").Result!.Flag);
        Assert.AreEqual(1, Warnings(result.Warnings, DataSyncWarningCode.ChildrenLocalTurnedOff).Count);
        Assert.AreEqual(0, Untyped.ChildDeletionCandidates(new DataSyncChildCandidatesInput(Peer(@base), local,
            DataSyncOverlay.None, Peer(remote), DataSyncMerge3Mode.ThreeWay, IdentityMap(@base), true)).Count);
    }

    [TestMethod]
    public void ChildrenLocalChangedBothWaysIsAConflict()
    {
        var local = Choice("G", false, C("a", "A"));
        var remote = Choice("G", false, C("a", "A"), C("n", "N"));
        var result = Merge(null, local, remote, DataSyncMerge3Mode.NoBase, localChildrenLocal: true);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(result, "childrenLocal").Resolution);
        CollectionAssert.AreEqual(new[] { "a" }, ChoiceIds(result), "still on here: untouched");
    }

    internal static DataSyncChildCandidatesInput Candidates(CustomPropertyContentV1 @base, CustomPropertyContentV1 local,
        CustomPropertyContentV1 remote, DataSyncOverlay? overlay = null,
        DataSyncMerge3Mode mode3 = DataSyncMerge3Mode.ThreeWay) =>
        new(Peer(@base), local, overlay ?? DataSyncOverlay.None, Peer(remote), mode3, IdentityMap(@base), false);
}

/// <summary>
/// §8.5.4 step 0: overlay children (<c>LocalOnlyChildren</c>, <c>HeldChildren</c>) are invisible to merging and always
/// stay in <c>Merged</c>, since the adapter's <c>Put</c> writes back exactly that list; and the one release.
/// </summary>
[TestClass]
public class MergeKeepsOverlayChildrenTests
{
    [TestMethod]
    public void MergeKeepsOverlayChildren()
    {
        var @base = Choice("G", false, C("a", "A"), C("k", "K"), C("h", "H"));
        var local = Choice("G", false, C("k", "K"), C("a", "A"), C("h", "H"));
        var overlay = new DataSyncOverlay(["k"], [new DataSyncHeldChild("h", 3)]);

        // Renamed, recoloured and deleted on the peer: overlay children are never touched.
        var remote = Choice("G", false, C("a", "A"), C("k", "K2", "#f00"));
        var result = Merge(@base, local, remote, overlay: overlay);
        CollectionAssert.AreEqual(new[] { "k", "a", "h" }, ChoiceIds(result), "every local child, in local order");
        CollectionAssert.AreEqual(new[] { "K", "A", "H" }, ChoiceLabels(result));
        Assert.IsNull(Merged(result).Choices[0].Color);
        Assert.AreEqual(0, result.Fields.Count);
        Assert.AreEqual(0, result.AddedChildIds.Count);
        Assert.AreEqual("k", result.ChildMap["k"], "the peer's child stays mapped to it");
        Assert.AreEqual(0, Untyped.ChildDeletionCandidates(Merge3ChildTests.Candidates(@base, local, remote, overlay)).Count);

        // A peer class keyed like an overlay child maps onto it; nothing is added beside it.
        var sameKey = Merge(@base, local, Choice("G", false, C("a", "A"), C("k", "K"), C("n", "K")), overlay: overlay);
        CollectionAssert.AreEqual(new[] { "k", "a", "h" }, ChoiceIds(sameKey));
    }

    [TestMethod]
    public void AHeldChildIsReleasedOnlyWhenThePeerReAddedItsClassSinceTheBase()
    {
        var overlay = Held("h");
        var local = Choice("G", false, C("a", "A"), C("h", "H"));

        // The base is the peer's content after the deletion; the peer has it again.
        var afterDeletion = Choice("G", false, C("a", "A"));
        var readded = Merge(afterDeletion, local, Choice("G", false, C("a", "A"), C("h", "H")), overlay: overlay);
        CollectionAssert.AreEqual(new[] { "h" }, readded.ReleasedChildIds.ToArray());
        CollectionAssert.AreEqual(new[] { "a", "h" }, ChoiceIds(readded), "mapped, nothing added");
        Assert.AreEqual("h", readded.ChildMap["h"]);

        // Re-added under a new id, found by its key.
        var byKey = Merge(afterDeletion, local, Choice("G", false, C("a", "A"), C("h9", "H")), overlay: overlay);
        CollectionAssert.AreEqual(new[] { "h" }, byKey.ReleasedChildIds.ToArray());
        Assert.AreEqual(0, byKey.AddedChildIds.Count);

        // The peer merely still has it (it has not seen another device's deletion): no release.
        var stillThere = Choice("G", false, C("a", "A"), C("h", "H"));
        Assert.AreEqual(0, Merge(stillThere, local, stillThere, overlay: overlay).ReleasedChildIds.Count);

        // Local-only children are never released.
        var localOnly = Merge(afterDeletion, local, Choice("G", false, C("a", "A"), C("h", "H")), overlay: LocalOnly("h"));
        Assert.AreEqual(0, localOnly.ReleasedChildIds.Count);
        Assert.AreEqual(0, localOnly.AddedChildIds.Count);
    }
}

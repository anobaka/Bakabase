using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.M3;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>§8.5.2: <c>name</c>, <c>ignoreCase</c>, <c>childrenLocal</c> and <c>settings.*</c> per mode, and Follow.</summary>
[TestClass]
public class Merge3ScalarTests
{
    private static CustomPropertyContentV1 Num(string name, int precision) => new()
    {
        Name = name, Type = PropertyType.Number, Settings = new CustomPropertySettingsV1 { Precision = precision },
    };

    private static CustomPropertyContentV1 Pct(string name, int precision, bool bar) => new()
    {
        Name = name, Type = PropertyType.Percentage,
        Settings = new CustomPropertySettingsV1 { Precision = precision, ShowProgressBar = bar },
    };

    [TestMethod]
    public void NameFollowsTheThreeWayTable()
    {
        var took = Merge(Num("A", 0), Num("A", 0), Num("B", 0));
        Assert.AreEqual("B", Merged(took).Name);
        var field = Field(took, "name");
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, field.Resolution);
        Assert.AreEqual("A", field.Base!.Text);
        Assert.AreEqual("A", field.Local!.Text);
        Assert.AreEqual("B", field.Remote!.Text);
        Assert.AreEqual("B", field.Result!.Text);

        var kept = Merge(Num("A", 0), Num("L", 0), Num("A", 0));
        Assert.AreEqual("L", Merged(kept).Name);
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(kept, "name").Resolution);

        var same = Merge(Num("A", 0), Num("X", 0), Num("X", 0));
        Assert.AreEqual("X", Merged(same).Name);
        Assert.AreEqual(0, same.Fields.Count, "an unchanged path has no outcome");

        var conflict = Merge(Num("A", 0), Num("L", 0), Num("R", 0));
        Assert.AreEqual("L", Merged(conflict).Name, "a conflict keeps the local value");
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(conflict, "name").Resolution);
    }

    [TestMethod]
    public void FollowTakesThePeersValueUnlessTheLocalOneCameFromAnotherDevice()
    {
        var follow = Merge(Num("A", 0), Num("L", 0), Num("R", 0), mode: DataSyncLinkMode.Follow);
        Assert.AreEqual("R", Merged(follow).Name);
        Assert.AreEqual(DataSyncFieldResolution.FollowTookRemote, Field(follow, "name").Resolution);

        var relayed = Merge(Num("A", 0), Num("L", 0), Num("R", 0), mode: DataSyncLinkMode.Follow,
            lastEditorIsSelf: false);
        Assert.AreEqual("L", Merged(relayed).Name);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(relayed, "name").Resolution);
    }

    [TestMethod]
    public void DifferentPathsMergeWithoutAConflict()
    {
        var result = Merge(Num("A", 0), Num("Local name", 0), Num("A", 2));
        Assert.AreEqual("Local name", Merged(result).Name);
        Assert.AreEqual(2, Merged(result).Settings!.Precision);
        var precision = Field(result, "settings.precision");
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, precision.Resolution);
        Assert.AreEqual(0, precision.Local!.Number);
        Assert.AreEqual(2, precision.Result!.Number);
        NoConflicts(result);

        var both = Merge(Num("A", 0), Num("A", 1), Num("A", 2));
        Assert.AreEqual(1, Merged(both).Settings!.Precision);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(both, "settings.precision").Resolution);
    }

    [TestMethod]
    public void AnAbsentLocalSettingReadsAsTheTypesDefault()
    {
        var local = new CustomPropertyContentV1 { Name = "A", Type = PropertyType.Percentage };
        var result = Merge(Pct("A", 0, false), local, Pct("A", 0, true));
        Assert.AreEqual(0, Merged(result).Settings!.Precision);
        Assert.AreEqual(true, Merged(result).Settings!.ShowProgressBar);
        NoField(result, "settings.precision");

        var unchanged = Merge(Pct("A", 0, false), local, Pct("A", 0, false));
        Assert.IsNull(Merged(unchanged).Settings, "nothing changed: the local content is kept as it is");
    }

    [TestMethod]
    public void FastForwardTakesEveryScalarFromThePeer()
    {
        var local = Choice("L", false, C("a", "A"));
        var remote = Choice("R", true, C("a", "A"));
        var result = Merge(null, local, remote, DataSyncMerge3Mode.FastForward);
        Assert.AreEqual("R", Merged(result).Name);
        Assert.AreEqual(true, Merged(result).IgnoreCase);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(result, "name").Resolution);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(result, "ignoreCase").Resolution);
        Assert.IsNull(Field(result, "name").Base, "a fast-forward has no base");
    }

    [TestMethod]
    public void WithoutABaseADifferenceIsAConflictInTwoWayAndThePeersInFollow()
    {
        var twoWay = Merge(null, Num("L", 1), Num("R", 2), DataSyncMerge3Mode.NoBase);
        Assert.AreEqual("L", Merged(twoWay).Name);
        Assert.AreEqual(1, Merged(twoWay).Settings!.Precision);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(twoWay, "name").Resolution);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(twoWay, "settings.precision").Resolution);

        var follow = Merge(null, Num("L", 1), Num("R", 2), DataSyncMerge3Mode.NoBase, mode: DataSyncLinkMode.Follow);
        Assert.AreEqual("R", Merged(follow).Name);
        Assert.AreEqual(2, Merged(follow).Settings!.Precision);
        Assert.AreEqual(DataSyncFieldResolution.FollowTookRemote, Field(follow, "settings.precision").Resolution);
    }

    [TestMethod]
    public void IgnoreCaseMergesAsAScalarAndAnUnsetLocalFlagReadsFalse()
    {
        var local = Choice("G", false, C("a", "A")) with { IgnoreCase = null };
        var took = Merge(Choice("G", false, C("a", "A")), local, Choice("G", true, C("a", "A")));
        Assert.AreEqual(true, Merged(took).IgnoreCase);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(took, "ignoreCase").Resolution);

        var same = Merge(Choice("G", false, C("a", "A")), local, Choice("G", false, C("a", "A")));
        Assert.IsNull(Merged(same).IgnoreCase, "unchanged: the local content keeps its own shape");
        Assert.AreEqual(0, same.Fields.Count);
    }
}

/// <summary>§8.5.6: a subtype is never changed without a decision; <c>Convert</c> is phase two (N7).</summary>
[TestClass]
public class Merge3TypeChangeTests
{
    [TestMethod]
    public void APeerTypeChangeFreezesTheWholeEntity()
    {
        var @base = Choice("G", false, C("a", "A"));
        var local = Choice("Renamed here", false, C("a", "A"), C("x", "X"));
        var remote = Single("G", false, C("a", "A"), C("b", "B"));
        var result = Merge(@base, local, remote);
        Assert.IsTrue(result.TypeChanged);
        Assert.AreEqual(Canon(local), Canon(Merged(result)), "Merged == Local");
        var field = result.Fields.Single();
        Assert.AreEqual("type", field.Path);
        Assert.AreEqual(DataSyncFieldResolution.TypeChangeHeld, field.Resolution);
        Assert.AreEqual("MultipleChoice", field.Local!.Text);
        Assert.AreEqual("SingleChoice", field.Remote!.Text);
        Assert.AreEqual(0, result.AddedChildIds.Count);
        Assert.AreEqual(0, result.RemovedChildIds.Count);
        CollectionAssert.AreEquivalent(new[] { "a" }, result.ChildMap.Keys.ToArray(), "the child map is left as it was");
        Assert.AreEqual(0, Untyped.ChildDeletionCandidates(new DataSyncChildCandidatesInput(Peer(@base), local,
            DataSyncOverlay.None, Peer(remote), DataSyncMerge3Mode.ThreeWay, IdentityMap(@base), false)).Count);
    }

    [TestMethod]
    public void BothChangingTheTypeDifferentlyFreezesToo()
    {
        var number = new CustomPropertyContentV1 { Name = "S", Type = PropertyType.Number };
        var result = Merge(number, number with { Type = PropertyType.Percentage },
            number with { Type = PropertyType.Rating });
        Assert.IsTrue(result.TypeChanged);
        Assert.AreEqual(PropertyType.Percentage, Merged(result).Type);
    }

    [TestMethod]
    public void WithoutABaseOrWhenThePeerIsNewerAnotherTypeIsThePeers()
    {
        var local = new CustomPropertyContentV1 { Name = "S", Type = PropertyType.Number };
        var remote = local with { Type = PropertyType.Rating };
        foreach (var mode3 in new[] { DataSyncMerge3Mode.NoBase, DataSyncMerge3Mode.FastForward, DataSyncMerge3Mode.Convert })
            Assert.IsTrue(Merge(null, local, remote, mode3).TypeChanged, mode3.ToString());
    }

    [TestMethod]
    public void ATypeChangedOnlyHereIsKeptAndEverythingElseMerges()
    {
        var @base = Choice("G", false, C("a", "A"));
        var local = Single("G", false, C("a", "A"));
        var remote = Choice("Genre", false, C("a", "A"), C("b", "B"));
        var result = Merge(@base, local, remote);
        Assert.IsFalse(result.TypeChanged);
        Assert.AreEqual(PropertyType.SingleChoice, Merged(result).Type);
        Assert.AreEqual("Genre", Merged(result).Name);
        CollectionAssert.AreEqual(new[] { "a", "b" }, ChoiceIds(result), "both types keep choices");
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(result, "type").Resolution);

        // Another list entirely: the children stay as they are.
        var tags = Tags("G", false, T("t", null, "T"));
        var other = Merge(@base, tags, remote);
        Assert.IsFalse(other.TypeChanged);
        Assert.AreEqual(Canon(tags with { Name = "Genre" }), Canon(Merged(other)));
    }

    [TestMethod]
    public void ConvertTakesEveryScalarButTheNameFromThePeer()
    {
        // The peer changed Number to Percentage and set its settings; this device converted (ChangeType rebuilt the
        // settings) and renamed it meanwhile. Only the name merges, against the base from before the type change.
        var oldBase = new CustomPropertyContentV1
        {
            Name = "Score", Type = PropertyType.Number, Settings = new CustomPropertySettingsV1 { Precision = 1 },
        };
        var remote = new CustomPropertyContentV1
        {
            Name = "Score", Type = PropertyType.Percentage,
            Settings = new CustomPropertySettingsV1 { Precision = 2, ShowProgressBar = true },
        };
        var local = new CustomPropertyContentV1
        {
            Name = "Score (mine)", Type = PropertyType.Percentage,
            Settings = new CustomPropertySettingsV1 { Precision = 0, ShowProgressBar = false },
        };

        var convert = Merge(oldBase, local, remote, DataSyncMerge3Mode.Convert);
        Assert.AreEqual("Score (mine)", Merged(convert).Name);
        Assert.AreEqual(DataSyncFieldResolution.KeptLocal, Field(convert, "name").Resolution);
        Assert.AreEqual(2, Merged(convert).Settings!.Precision);
        Assert.AreEqual(true, Merged(convert).Settings!.ShowProgressBar);
        Assert.AreEqual(DataSyncFieldResolution.TookRemote, Field(convert, "settings.precision").Resolution);
        NoConflicts(convert);

        // The same contents merged without a base would make every setting a conflict.
        var noBase = Merge(null, local, remote, DataSyncMerge3Mode.NoBase);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(noBase, "settings.precision").Resolution);

        // A name changed on both sides is still a conflict; without a base it is the NoBase rule.
        var renamed = Merge(oldBase, local, remote with { Name = "Rating" }, DataSyncMerge3Mode.Convert);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(renamed, "name").Resolution);
        var baseless = Merge(null, local, remote, DataSyncMerge3Mode.Convert);
        Assert.AreEqual(DataSyncFieldResolution.Conflict, Field(baseless, "name").Resolution);
    }

    [TestMethod]
    public void ConvertUnionsTheChildrenWithoutDeletingAny()
    {
        // ChangeType rebuilt this device's options from its values, with fresh ids (F73).
        var oldBase = Choice("Genre", false, C("a", "Action"), C("d", "Drama"));
        var remote = Single("Genre", false, C("a", "Action"), C("d", "Drama"), C("h", "Horror"));
        var local = Single("Genre", false, C("x1", "Action", "#111"), C("x2", "Comedy"));
        var result = Merge(oldBase, local, remote, DataSyncMerge3Mode.Convert);
        Assert.IsFalse(result.TypeChanged);
        CollectionAssert.AreEqual(new[] { "Action", "Comedy", "Drama", "Horror" }, ChoiceLabels(result));
        CollectionAssert.AreEqual(new[] { "x1", "x2", "d", "h" }, ChoiceIds(result));
        Assert.AreEqual(0, result.RemovedChildIds.Count);
        Assert.AreEqual("x1", result.ChildMap["a"], "Action maps by key onto the converted option");
        Assert.AreEqual("#111", Merged(result).Choices[0].Color, "the side that has a colour gives it (NoBase)");
        NoConflicts(result);
    }
}

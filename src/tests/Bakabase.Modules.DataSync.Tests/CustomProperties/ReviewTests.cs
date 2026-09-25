using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// The first-contact review (v3.1 §7.4, <c>CustomPropertyCodecTests</c>' Diff/Merge part): renames, recolours, adds,
/// label conflicts, the local comparer, nodes under added parents, <c>NodeMoveIgnored</c>, default value translation,
/// folding with its condition, and local content that is never dropped.
/// </summary>
[TestClass]
public class ReviewTests
{
    private static EntityDiff Diff(CustomPropertyContentV1 local, CustomPropertyContentV1 incoming) =>
        Untyped.Diff(local, incoming);

    private static MergeResult Merge(CustomPropertyContentV1 local, CustomPropertyContentV1 incoming,
        params string[] accepted) =>
        Untyped.Merge(local, incoming, accepted.ToHashSet(StringComparer.Ordinal));

    private static MergeResult MergeAll(CustomPropertyContentV1 local, CustomPropertyContentV1 incoming) =>
        Merge(local, incoming, Diff(local, incoming).Changes.Select(c => c.ChangeId).ToArray());

    private static CustomPropertyContentV1 Content(MergeResult result) => (CustomPropertyContentV1)result.Content;

    private static string[] Ids(EntityDiff diff) => diff.Changes.Select(c => c.ChangeId).ToArray();

    // ---- scalars ------------------------------------------------------------------------------

    [TestMethod]
    public void ScalarsAreSetOnlyWhenAccepted()
    {
        var local = new CustomPropertyContentV1
        {
            Name = "Score", Type = PropertyType.Percentage,
            Settings = new CustomPropertySettingsV1 { Precision = 0, ShowProgressBar = false },
        };
        var incoming = local with
        {
            Name = "Rating", Settings = new CustomPropertySettingsV1 { Precision = 2, ShowProgressBar = true },
        };
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "name", "settings.precision", "settings.showProgressBar" }, Ids(diff));
        Assert.AreEqual(new DataSyncDisplayValue("0", Number: 0), diff.Changes[1].From);
        Assert.AreEqual(new DataSyncDisplayValue("2", Number: 2), diff.Changes[1].To);
        Assert.IsTrue(diff.Changes.All(c => c.Kind == DataSyncFieldChangeKind.Set && c.Path == c.ChangeId));

        var merged = Content(Merge(local, incoming, "settings.precision"));
        Assert.AreEqual("Score", merged.Name);
        Assert.AreEqual(2, merged.Settings!.Precision);
        Assert.AreEqual(false, merged.Settings.ShowProgressBar);
        Assert.AreEqual(Canon(incoming), Canon(Content(MergeAll(local, incoming))));
    }

    [TestMethod]
    public void IgnoreCaseAndChildrenLocalAreScalarsOfReferenceTypes()
    {
        var local = Tags("T", false, T("1", null, "A"));
        var incoming = Tags("T", true, T("1", null, "A")) with { ChildrenLocal = true, Tags = [] };
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "ignoreCase", "childrenLocal" }, Ids(diff));
        Assert.AreEqual(1, diff.LocalOnlyChildren);
        var merged = Content(MergeAll(local, incoming));
        Assert.IsTrue(merged.ChildrenLocal);
        Assert.AreEqual(1, merged.Tags.Count, "every device keeps its own options");
    }

    [TestMethod]
    public void IdenticalContentHasNoChanges()
    {
        var genre = Choice("G", true, C("a", "Action", "#f00"), C("d", "Drama")) with { DefaultValue = [Ref("d", "Drama")] };
        var diff = Diff(genre, genre);
        Assert.AreEqual(0, diff.Changes.Count);
        Assert.AreEqual(2, diff.UnchangedChildren);
        Assert.AreEqual(0, diff.LocalOnlyChildren);
        var merged = MergeAll(genre, genre);
        Assert.AreEqual(Canon(genre), Canon(Content(merged)));
        Assert.AreEqual(0, merged.AddedChildIds.Count);
        Assert.AreEqual("a", merged.ChildIdMap["a"]);
    }

    // ---- choices ------------------------------------------------------------------------------

    [TestMethod]
    public void ARenameByUuidIsARow()
    {
        var local = Choice("G", false, C("a", "Action", "#f00"));
        var incoming = Choice("G", false, C("a", "Fight", "#f00"));
        var diff = Diff(local, incoming);
        var rename = diff.Changes.Single();
        Assert.AreEqual("choice:rename:a", rename.ChangeId);
        Assert.AreEqual(DataSyncFieldChangeKind.RenameChild, rename.Kind);
        Assert.AreEqual("choices", rename.Path);
        Assert.AreEqual("Action", rename.From!.Text);
        Assert.AreEqual("Fight", rename.To!.Text);
        Assert.AreEqual("Fight", Content(MergeAll(local, incoming)).Choices[0].Label);
        Assert.AreEqual("Action", Content(Merge(local, incoming)).Choices[0].Label);
    }

    [TestMethod]
    public void MatchingUsesTheLocalComparer()
    {
        // A case-only difference under IgnoreCase is no change; with IgnoreCase off it is a rename.
        Assert.AreEqual(0, Diff(Choice("G", true, C("a", "Action")), Choice("G", true, C("a", "ACTION"))).Changes.Count);
        Assert.AreEqual("choice:rename:a",
            Diff(Choice("G", false, C("a", "Action")), Choice("G", false, C("a", "ACTION"))).Changes.Single().ChangeId);

        // An unknown uuid maps by label under the local comparer, even when the incoming one differs.
        var diff = Diff(Choice("G", true, C("a", "Action")), Choice("G", false, C("x", "action")));
        CollectionAssert.AreEqual(new[] { "ignoreCase" }, Ids(diff));
        Assert.AreEqual(1, diff.UnchangedChildren);
        Assert.AreEqual("a", Merge(Choice("G", true, C("a", "Action")), Choice("G", false, C("x", "action"))).ChildIdMap["x"]);
    }

    [TestMethod]
    public void ARenameOntoAnotherLocalLabelMapsToThatOptionInstead()
    {
        var local = Choice("G", false, C("a", "Action"), C("b", "Drama"));
        var incoming = Choice("G", false, C("a", "Drama", "#123"));
        var diff = Diff(local, incoming);
        Assert.AreEqual(0, diff.Changes.Count, "no rename, and no recolour of the other option");
        var conflict = Warnings(diff.Warnings, DataSyncWarningCode.OptionLabelConflict).Single();
        Assert.AreEqual("a", Arg(conflict, "uuid"));
        Assert.AreEqual("b", Arg(conflict, "into"));
        Assert.AreEqual("Drama", Arg(conflict, "intoLabel"));
        var merged = MergeAll(local, incoming);
        Assert.AreEqual("b", merged.ChildIdMap["a"]);
        Assert.AreEqual(Canon(local), Canon(Content(merged)));
        Assert.AreEqual(1, diff.LocalOnlyChildren, "Action is no longer mentioned");
    }

    [TestMethod]
    public void RecoloursNeverClear()
    {
        var local = Choice("G", false, C("a", "A", "#111"), C("b", "B", "#222"));
        var incoming = Choice("G", false, C("a", "A", "#999"), C("b", "B"));
        var diff = Diff(local, incoming);
        var recolor = diff.Changes.Single();
        Assert.AreEqual("choice:recolor:a", recolor.ChangeId);
        Assert.AreEqual("#111", recolor.From!.Color);
        Assert.AreEqual("#999", recolor.To!.Color);
        Assert.AreEqual(1, diff.UnchangedChildren);
        var merged = Content(MergeAll(local, incoming));
        Assert.AreEqual("#999", merged.Choices[0].Color);
        Assert.AreEqual("#222", merged.Choices[1].Color);
    }

    [TestMethod]
    public void AddsAreAppendedInIncomingOrder()
    {
        var local = Choice("G", false, C("a", "A"));
        var incoming = Choice("G", false, C("y", "Y", "#1"), C("a", "A"), C("x", "X"));
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "choice:add:y", "choice:add:x" }, Ids(diff));
        Assert.AreEqual(DataSyncFieldChangeKind.AddChild, diff.Changes[0].Kind);
        Assert.IsNull(diff.Changes[0].From);
        Assert.AreEqual(new DataSyncDisplayValue("Y", "#1"), diff.Changes[0].To);

        var merged = MergeAll(local, incoming);
        Assert.AreEqual(Canon(Choice("G", false, C("a", "A"), C("y", "Y", "#1"), C("x", "X"))), Canon(Content(merged)));
        CollectionAssert.AreEqual(new[] { "y", "x" }, merged.AddedChildIds.ToArray());
        Assert.AreEqual("x", merged.ChildIdMap["x"]);

        var partial = Merge(local, incoming, "choice:add:x");
        CollectionAssert.AreEqual(new[] { "a", "x" }, Content(partial).Choices.Select(c => c.Uuid).ToArray());
        Assert.IsFalse(partial.ChildIdMap.ContainsKey("y"), "an unaccepted add maps nowhere");
    }

    [TestMethod]
    public void IncomingMembersOfOneClassAreAddedOnce()
    {
        // A source that holds "New" and "NEW" under IgnoreCase: one class, one add, both ids mapped (§3.4).
        var local = Choice("G", true, C("a", "A"));
        var incoming = Choice("G", true, C("x", "New"), C("y", "NEW"));
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "choice:add:x" }, Ids(diff));
        var merged = MergeAll(local, incoming);
        Assert.AreEqual("x", merged.ChildIdMap["y"]);
        Assert.AreEqual(2, Content(merged).Choices.Count);
    }

    // ---- tags ---------------------------------------------------------------------------------

    [TestMethod]
    public void ANullAndAnEmptyTagGroupAreOneClass()
    {
        // A uuid-matched tag differing only by null ↔ "" is no change.
        Assert.AreEqual(0, Diff(Tags("T", false, T("1", null, "Kyoto")), Tags("T", false, T("1", "", "Kyoto"))).Changes.Count);

        // Matched by class: the review never adds a tag the service would store beside an equal one (§3.3).
        var local = Tags("T", false, T("1", null, "Kyoto"));
        var incoming = Tags("T", false, T("9", "", "Kyoto"));
        var diff = Diff(local, incoming);
        Assert.AreEqual(0, diff.Changes.Count);
        Assert.AreEqual("1", MergeAll(local, incoming).ChildIdMap["9"]);
    }

    [TestMethod]
    public void ATagRenameCoversItsGroupAndName()
    {
        var local = Tags("T", false, T("1", null, "Kyoto"), T("2", "Studio", "Other"));
        var incoming = Tags("T", false, T("1", "Studio", "Kyoto", "#abc"), T("2", "Studio", "Other"));
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "tag:rename:1", "tag:recolor:1" }, Ids(diff));
        Assert.AreEqual(new DataSyncDisplayValue("Kyoto", null, "Studio"), diff.Changes[0].To);
        var merged = Content(MergeAll(local, incoming));
        Assert.AreEqual(T("1", "Studio", "Kyoto", "#abc"), merged.Tags[0]);
    }

    // ---- multilevel ---------------------------------------------------------------------------

    [TestMethod]
    public void NodeAddsUnderAnAddedParentDependOnIt()
    {
        var local = Tree("R", false, N("eu", "Europe"));
        var incoming = Tree("R", false, N("asia", "Asia", N("jp", "Japan", N("kyoto", "Kyoto"))));
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "node:add:asia", "node:add:jp", "node:add:kyoto" }, Ids(diff));
        Assert.IsNull(diff.Changes[0].DependsOnChangeId);
        Assert.AreEqual("node:add:asia", diff.Changes[1].DependsOnChangeId);
        Assert.AreEqual("node:add:jp", diff.Changes[2].DependsOnChangeId);
        CollectionAssert.AreEqual(new[] { "Asia", "Japan" }, diff.Changes[1].To!.Path!.ToArray());

        Assert.AreEqual(Canon(Tree("R", false, N("eu", "Europe"), N("asia", "Asia", N("jp", "Japan", N("kyoto", "Kyoto"))))),
            Canon(Content(MergeAll(local, incoming))));
        Assert.AreEqual(Canon(Tree("R", false, N("eu", "Europe"), N("asia", "Asia"))),
            Canon(Content(Merge(local, incoming, "node:add:asia"))));
        Assert.AreEqual(Canon(local), Canon(Content(Merge(local, incoming, "node:add:jp", "node:add:kyoto"))),
            "a child add without its parent's has nowhere to go");
    }

    [TestMethod]
    public void NodesMatchByLabelAmongTheMappedParentsChildrenAndAddUnderIt()
    {
        var local = Tree("R", true, N("asia", "Asia", N("jp", "Japan")), N("asia2", "ASIA", N("kr", "Korea")));
        var incoming = Tree("R", true, N("x", "asia", N("y", "korea"), N("z", "China")));
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "node:add:z" }, Ids(diff), "Korea is found among the class's united children");
        var merged = MergeAll(local, incoming);
        Assert.AreEqual("asia", merged.ChildIdMap["x"]);
        Assert.AreEqual("kr", merged.ChildIdMap["y"]);
        Assert.AreEqual(Canon(Tree("R", true, N("asia", "Asia", N("jp", "Japan"), N("z", "China")), N("asia2", "ASIA", N("kr", "Korea")))),
            Canon(Content(merged)));
    }

    [TestMethod]
    public void AUuidMatchUnderAnotherParentIsNeverMoved()
    {
        var local = Tree("R", false, N("asia", "Asia", N("jp", "Japan")), N("eu", "Europe"));
        var incoming = Tree("R", false, N("eu", "Europe", N("jp", "Japan", N("tokyo", "Tokyo"))));
        var diff = Diff(local, incoming);
        Assert.AreEqual("jp", Arg(Warnings(diff.Warnings, DataSyncWarningCode.NodeMoveIgnored).Single(), "uuid"));
        CollectionAssert.AreEqual(new[] { "node:add:tokyo" }, Ids(diff));
        var merged = MergeAll(local, incoming);
        Assert.AreEqual(Canon(Tree("R", false, N("asia", "Asia", N("jp", "Japan", N("tokyo", "Tokyo"))), N("eu", "Europe"))),
            Canon(Content(merged)), "the child goes under the node where it is here");
        Assert.AreEqual(0, Warnings(merged.Warnings, DataSyncWarningCode.NodeMoveIgnored).Count, "a plan warning only");
    }

    [TestMethod]
    public void ANodeRenameAndRecolour()
    {
        var local = Tree("R", false, N("asia", "Asia", N("jp", "Japan")));
        var incoming = Tree("R", false, N("asia", "Asia", "#0f0", N("jp", "Nippon")));
        CollectionAssert.AreEqual(new[] { "node:recolor:asia", "node:rename:jp" }, Ids(Diff(local, incoming)));
        Assert.AreEqual(Canon(incoming), Canon(Content(MergeAll(local, incoming))));
    }

    // ---- defaultValue -------------------------------------------------------------------------

    [TestMethod]
    public void DefaultValuesAreTranslatedThroughTheMapping()
    {
        var local = Choice("G", true, C("a", "Action"));
        var incoming = Choice("G", true, C("x", "action"), C("n", "New")) with { DefaultValue = [Ref("x", "action"), Ref("n", "New")] };
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "defaultValue", "choice:add:n" }, Ids(diff), "scalars come first");
        Assert.AreEqual("action, New", diff.Changes[0].To!.Text);

        var all = Content(MergeAll(local, incoming));
        CollectionAssert.AreEqual(new[] { Ref("a", "Action"), Ref("n", "New") }, all.DefaultValue.ToArray());

        var withoutAdd = Merge(local, incoming, "defaultValue");
        CollectionAssert.AreEqual(new[] { Ref("a", "Action") }, Content(withoutAdd).DefaultValue.ToArray());
        Assert.AreEqual("n", Arg(Warnings(withoutAdd.Warnings, DataSyncWarningCode.DefaultValueRefDropped).Single(), "uuid"));
    }

    [TestMethod]
    public void ADefaultThatTranslatesToNothingIsANoOp()
    {
        var local = Choice("G", false, C("a", "A")) with { DefaultValue = [Ref("a", "A")] };
        var incoming = Choice("G", false, C("n", "New")) with { DefaultValue = [Ref("n", "New")] };
        var merged = Merge(local, incoming, "defaultValue");
        CollectionAssert.AreEqual(new[] { Ref("a", "A") }, Content(merged).DefaultValue.ToArray());
        // The same default after translation is no change at all.
        Assert.IsFalse(Ids(Diff(local, Choice("G", false, C("q", "A")) with { DefaultValue = [Ref("q", "A")] })).Contains("defaultValue"));
    }

    [TestMethod]
    public void MultilevelDefaultsUseLocalPaths()
    {
        var local = Tree("R", false, N("asia", "Asia", N("jp", "Japan")));
        var incoming = Tree("R", false, N("x", "Asia", N("y", "Japan"))) with { DefaultValue = [NodeRef("y", "Asia", "Japan")] };
        var merged = Content(MergeAll(local, incoming));
        CollectionAssert.AreEqual(new[] { NodeRef("jp", "Asia", "Japan") }, merged.DefaultValue.ToArray());
    }

    // ---- folding (v3.1 §3.3.2, H4) --------------------------------------------------------------

    [TestMethod]
    public void TurningIgnoreCaseOnFoldsAddsIntoLocalOptionsAndEachOther()
    {
        var local = Choice("G", false, C("a", "Action"));
        var incoming = Choice("G", true, C("x", "action"), C("y", "New"), C("z", "NEW"));
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "ignoreCase", "choice:add:x", "choice:add:y", "choice:add:z" }, Ids(diff));
        var folds = Warnings(diff.Warnings, DataSyncWarningCode.OptionLabelConflict);
        Assert.AreEqual(2, folds.Count);
        Assert.IsTrue(folds.All(w => Arg(w, "when") == "withIgnoreCaseChange"));
        Assert.AreEqual("choice:add:x", folds[0].ChangeId);
        Assert.AreEqual("a", Arg(folds[0], "into"));
        Assert.AreEqual("Action", Arg(folds[0], "intoLabel"));
        Assert.AreEqual("choice:add:z", folds[1].ChangeId);
        Assert.AreEqual("y", Arg(folds[1], "into"));

        var ticked = MergeAll(local, incoming);
        CollectionAssert.AreEqual(new[] { "a", "y" }, Content(ticked).Choices.Select(c => c.Uuid).ToArray());
        Assert.AreEqual("a", ticked.ChildIdMap["x"]);
        Assert.AreEqual("y", ticked.ChildIdMap["z"]);
        CollectionAssert.AreEqual(new[] { "y" }, ticked.AddedChildIds.ToArray());
        Assert.AreEqual(2, Warnings(ticked.Warnings, DataSyncWarningCode.OptionLabelConflict).Count);

        var unticked = Merge(local, incoming, "choice:add:x", "choice:add:y", "choice:add:z");
        CollectionAssert.AreEqual(new[] { "a", "x", "y", "z" }, Content(unticked).Choices.Select(c => c.Uuid).ToArray());
        Assert.AreEqual(0, Warnings(unticked.Warnings, DataSyncWarningCode.OptionLabelConflict).Count);
    }

    [TestMethod]
    public void UnderTheLocalIgnoreCaseEquivalentsAreMatchedSoNothingIsLeftToFold()
    {
        // Matching already uses the local comparer: with IgnoreCase on here, an equivalent is a match, never an add.
        var local = Choice("G", true, C("a", "Action"));
        var diff = Diff(local, Choice("G", false, C("x", "ACTION"), C("y", "action"), C("n", "New")));
        CollectionAssert.AreEqual(new[] { "ignoreCase", "choice:add:n" }, Ids(diff));
        Assert.AreEqual(0, Warnings(diff.Warnings, DataSyncWarningCode.OptionLabelConflict).Count);
    }

    [TestMethod]
    public void MultilevelFoldsMergeChildrenThenRecurse()
    {
        var local = Tree("R", false, N("asia", "Asia", N("jp", "Japan")));
        var incoming = Tree("R", true, N("x", "ASIA", N("y", "Japan"), N("k", "Korea")));
        var diff = Diff(local, incoming);
        CollectionAssert.AreEqual(new[] { "ignoreCase", "node:add:x", "node:add:y", "node:add:k" }, Ids(diff));
        var folds = Warnings(diff.Warnings, DataSyncWarningCode.OptionLabelConflict);
        CollectionAssert.AreEqual(new[] { "x", "y" }, folds.Select(w => Arg(w, "uuid")).ToArray());

        var merged = MergeAll(local, incoming);
        Assert.AreEqual(Canon(Tree("R", true, N("asia", "Asia", N("jp", "Japan"), N("k", "Korea")))), Canon(Content(merged)));
        Assert.AreEqual("asia", merged.ChildIdMap["x"]);
        Assert.AreEqual("jp", merged.ChildIdMap["y"]);
        Assert.AreEqual("k", merged.ChildIdMap["k"]);
        CollectionAssert.AreEqual(new[] { "k" }, merged.AddedChildIds.ToArray());
    }

    // ---- local content is never dropped (v3.1 B3) ------------------------------------------------

    [TestMethod]
    public void EveryLocalChildIsKeptInLocalOrderAndOnlyIncomingAddsFold()
    {
        var local = Codec.ReadLocal(Json($$"""
            {"choices":[{"label":"Long","uuid":"{{new string('u', 200)}}"},{"label":"One","uuid":"same"},{"label":"Two","uuid":"same"},
                        {"label":"a\u0000b","uuid":"nul"},{"label":"No id","uuid":""},{"label":"dup","uuid":"d1"},{"label":"DUP","uuid":"d2"}],
             "ignoreCase":false,"name":"G","type":"MultipleChoice"}
            """));
        var incoming = Choice("G", true, C("n1", "new"), C("n2", "NEW"), C("n3", "Dup"));
        var before = Canon(local);

        var nothing = Merge(local, incoming);
        Assert.AreEqual(before, Canon(Content(nothing)), "nothing accepted changes nothing");

        var merged = Content(MergeAll(local, incoming));
        var expected = local.Choices.Select(c => c.Uuid).Concat(["n1"]).ToArray();
        CollectionAssert.AreEqual(expected, merged.Choices.Select(c => c.Uuid).ToArray(),
            "the stored duplicates d1/d2 stay; n2 folds into n1, n3 into d1");
        CollectionAssert.AreEqual(local.Choices.ToArray(), merged.Choices.Take(local.Choices.Count).ToArray());
        Assert.AreEqual(before, Canon(local), "the local content is not mutated");
    }

    [TestMethod]
    public void AMergeNeverRemovesALocalChild()
    {
        var local = Tree("R", false, N("a", "A", N("b", "B")), N("c", "C"));
        var incoming = Tree("R", false, N("z", "Z"));
        var merged = Content(MergeAll(local, incoming));
        Assert.AreEqual(Canon(Tree("R", false, N("a", "A", N("b", "B")), N("c", "C"), N("z", "Z"))), Canon(merged));
        Assert.AreEqual(3, Diff(local, incoming).LocalOnlyChildren);
    }

    [TestMethod]
    public void TheDiffIsDeterministic()
    {
        var local = Tree("R", false, N("asia", "Asia", N("jp", "Japan")), N("eu", "Europe"));
        var incoming = Tree("R", true, N("x", "ASIA", N("y", "Japan"), N("k", "Korea")), N("eu", "Europe", "#1"))
            with { DefaultValue = [NodeRef("k", "ASIA", "Korea")] };
        var first = Diff(local, incoming);
        var second = Diff(local, incoming);
        Assert.AreEqual(string.Join(";", first.Changes.Select(Change)), string.Join(";", second.Changes.Select(Change)));
        Assert.AreEqual(string.Join(";", first.Warnings.Select(Describe)), string.Join(";", second.Warnings.Select(Describe)));

        static string Change(DataSyncFieldChange c) =>
            $"{c.ChangeId}/{c.Kind}/{c.Path}/{Value(c.From)}/{Value(c.To)}/{c.DependsOnChangeId}";

        static string Value(DataSyncDisplayValue? v) =>
            v is null ? "-" : $"{v.Text}|{v.Color}|{v.Group}|{string.Join(">", v.Path ?? [])}|{v.Flag}|{v.Number}";

        static string Describe(DataSyncPlanWarning w) =>
            $"{w.Code}/{w.ChangeId}/{string.Join(",", (w.Args ?? new Dictionary<string, string>()).OrderBy(a => a.Key, StringComparer.Ordinal))}";
    }

    // ---- creates ------------------------------------------------------------------------------

    [TestMethod]
    public void ACreateFoldsAsAFreshAddRange()
    {
        var incoming = Choice("Genre", true, C("a", "Action"), C("b", "action"), C("d", "Drama"))
            with { DefaultValue = [Ref("b", "action"), Ref("d", "Drama")] };
        var result = Untyped.PrepareCreate(incoming, null);
        var created = Content(result);
        CollectionAssert.AreEqual(new[] { "a", "d" }, created.Choices.Select(c => c.Uuid).ToArray());
        CollectionAssert.AreEqual(new[] { Ref("a", "Action"), Ref("d", "Drama") }, created.DefaultValue.ToArray());
        Assert.AreEqual("a", result.ChildIdMap["b"]);
        Assert.AreEqual("d", result.ChildIdMap["d"]);
        CollectionAssert.AreEqual(new[] { "a", "d" }, result.AddedChildIds.ToArray());
        var fold = Warnings(result.Warnings, DataSyncWarningCode.OptionLabelConflict).Single();
        Assert.AreEqual("b", Arg(fold, "uuid"));
        Assert.AreEqual("a", Arg(fold, "into"));
        Assert.AreEqual("always", Arg(fold, "when"));
        Assert.AreEqual("Genre", created.Name);
        Assert.AreEqual("Genre (NAS)", Content(Untyped.PrepareCreate(incoming, "Genre (NAS)")).Name);
    }

    [TestMethod]
    public void ACreateWithIgnoreCaseOffKeepsEveryOption()
    {
        var incoming = Choice("Genre", false, C("a", "Action"), C("b", "Action"));
        var result = Untyped.PrepareCreate(incoming, null);
        Assert.AreEqual(Canon(incoming), Canon(Content(result)));
        Assert.AreEqual(0, result.Warnings.Count);
        Assert.AreEqual(2, result.AddedChildIds.Count);
    }

    [TestMethod]
    public void RemappedUuidsAreDerivedAndFree()
    {
        var taken = new HashSet<string> { "u1" };
        var first = CustomPropertyUuids.Remap("u1", taken.Contains);
        Assert.AreEqual(first, CustomPropertyUuids.Remap("u1", taken.Contains));
        Assert.IsTrue(Guid.TryParse(first, out _));
        taken.Add(first);
        var second = CustomPropertyUuids.Remap("u1", taken.Contains);
        Assert.AreNotEqual(first, second);
        Assert.AreNotEqual(first, CustomPropertyUuids.Remap("u2", _ => false));
    }
}

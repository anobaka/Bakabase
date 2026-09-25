using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>
/// v3.1 §3.3.2: <see cref="OptionFolding"/> reproduces <c>ReferencePropertyOptionsNormalizer.Normalize</c>. The
/// expected results below are the normalizer's, worked by hand; <c>OptionEquivalenceCrossCheckTests</c> in
/// Bakabase.Tests runs the two side by side.
/// </summary>
[TestClass]
public class OptionFoldingTests
{
    private static readonly HashSet<string> None = [];

    [TestMethod]
    public void NothingFoldsWithIgnoreCaseOff()
    {
        var content = Choice("G", false, C("a", "Action"), C("b", "action"), C("c", "Action"));
        var result = OptionFolding.Fold(content, None);
        Assert.AreSame(content, result.Content);
        Assert.AreEqual(0, result.Folds.Count);
        Assert.AreEqual(0, OptionFolding.Fold(Choice("G", true, C("a", "A"), C("b", "a")), None, ignoreCase: false).Folds.Count);
    }

    [TestMethod]
    public void AFreshCreateFoldsEveryLaterEquivalentIntoTheFirst()
    {
        var result = OptionFolding.Fold(Choice("G", true, C("a", "Action"), C("b", "action"), C("c", "Drama"), C("d", "ACTION")));
        CollectionAssert.AreEqual(new[] { "a", "c" }, result.Content.Choices.Select(c => c.Uuid).ToArray());
        Assert.AreEqual("a", result.Aliases["b"]);
        Assert.AreEqual("a", result.Aliases["d"]);
        CollectionAssert.AreEqual(new[] { new OptionFold("b", "a", "Action"), new OptionFold("d", "a", "Action") },
            result.Folds.ToArray());
    }

    [TestMethod]
    public void APreservedOptionIsNeverFoldedAndSeedsTheLabelsFirst()
    {
        // The stored "action" comes after a new "Action": the new one folds into the stored one.
        var result = OptionFolding.Fold(Choice("G", true, C("a", "Action"), C("b", "action")), new HashSet<string> { "b" });
        CollectionAssert.AreEqual(new[] { "b" }, result.Content.Choices.Select(c => c.Uuid).ToArray());
        Assert.AreEqual("b", result.Aliases["a"]);

        // Two stored equivalents both stay (F72); a new one folds into the first stored.
        var both = OptionFolding.Fold(Choice("G", true, C("p1", "X"), C("n", "x"), C("p2", "X")),
            new HashSet<string> { "p1", "p2" });
        CollectionAssert.AreEqual(new[] { "p1", "p2" }, both.Content.Choices.Select(c => c.Uuid).ToArray());
        Assert.AreEqual("p1", both.Aliases["n"]);
    }

    [TestMethod]
    public void TagsFoldByGroupAndNameWithAnEmptyGroupAsNone()
    {
        var result = OptionFolding.Fold(Tags("T", true, T("1", null, "A"), T("2", "", "a"), T("3", "G", "x"), T("4", "g", "X")));
        CollectionAssert.AreEqual(new[] { "1", "3" }, result.Content.Tags.Select(t => t.Uuid).ToArray());
        Assert.AreEqual("1", result.Aliases["2"]);
        Assert.AreEqual("3", result.Aliases["4"]);
    }

    [TestMethod]
    public void MultilevelFoldsMergeChildrenThenRecurse()
    {
        var tree = Tree("R", true,
            N("n1", "Asia", N("j1", "Japan")),
            N("n2", "ASIA", N("j2", "japan"), N("k2", "Korea")),
            N("e", "Europe")) with { DefaultValue = [NodeRef("k2", "ASIA", "Korea"), NodeRef("j2", "ASIA", "japan")] };
        var result = OptionFolding.Fold(tree);
        Assert.AreEqual(Canon(Tree("R", true, N("n1", "Asia", N("j1", "Japan"), N("k2", "Korea")), N("e", "Europe"))
                with { DefaultValue = [NodeRef("k2", "Asia", "Korea"), NodeRef("j1", "Asia", "Japan")] }),
            Canon(result.Content));
        CollectionAssert.AreEqual(new[] { new OptionFold("n2", "n1", "Asia"), new OptionFold("j2", "j1", "Japan") },
            result.Folds.ToArray());
    }

    [TestMethod]
    public void AStoredNodeReceivesTheChildrenOfANewEquivalentBeforeIt()
    {
        var tree = Tree("R", true, N("n1", "Asia", N("j1", "Japan")), N("n2", "ASIA", N("j2", "japan")));
        var result = OptionFolding.Fold(tree, new HashSet<string> { "n2", "j2" });
        Assert.AreEqual(Canon(Tree("R", true, N("n2", "ASIA", N("j2", "japan")))), Canon(result.Content));
        CollectionAssert.AreEqual(new[] { new OptionFold("n1", "n2", "ASIA"), new OptionFold("j1", "j2", "japan") },
            result.Folds.ToArray());
    }

    [TestMethod]
    public void DefaultValuesAreMappedThroughTheAliasesAndDeduplicated()
    {
        var multiple = Choice("G", true, C("a", "A"), C("b", "a")) with { DefaultValue = [Ref("b", "a"), Ref("a", "A")] };
        var result = OptionFolding.Fold(multiple);
        CollectionAssert.AreEqual(new[] { "a" }, result.Content.DefaultValue.Select(r => r.Uuid).ToArray());
        Assert.AreEqual("A", result.Content.DefaultValue[0].Label);

        var single = Single("S", true, C("a", "A"), C("b", "a")) with { DefaultValue = [Ref("b", "a")] };
        Assert.AreEqual(Ref("a", "A"), OptionFolding.Fold(single).Content.DefaultValue.Single());
    }

    [TestMethod]
    public void AnOptionWithTheSameUuidFoldsWithoutAnAlias()
    {
        var result = OptionFolding.Fold(Choice("G", true, C("a", "A"), C("a", "a")));
        Assert.AreEqual(1, result.Content.Choices.Count);
        Assert.AreEqual(0, result.Aliases.Count);
        Assert.AreEqual(0, result.Folds.Count);
    }

    [TestMethod]
    public void AnOptionWithoutAUuidIsNeverFolded()
    {
        var result = OptionFolding.Fold(Choice("G", true, C("a", "A"), C(null, "a")));
        Assert.AreEqual(2, result.Content.Choices.Count);
    }

    [TestMethod]
    public void NothingIsFoldedIntoAnOptionWithoutAUuid()
    {
        // It is never published: an option folded into it would leave its class out of what this device publishes,
        // with no id to map to (§3.3). The next option of its class with a uuid is the survivor.
        var choices = OptionFolding.Fold(Choice("G", true, C(null, "Action"), C("r1", "action"), C("r2", "ACTION")),
            None);
        CollectionAssert.AreEqual(new[] { null, "r1" }, choices.Content.Choices.Select(c => c.Uuid).ToArray());
        CollectionAssert.AreEqual(new[] { new OptionFold("r2", "r1", "action") }, choices.Folds.ToArray());

        var tags = OptionFolding.Fold(Tags("T", true, T(null, null, "Kyoto"), T("r1", "", "KYOTO")), None);
        CollectionAssert.AreEqual(new[] { null, "r1" }, tags.Content.Tags.Select(t => t.Uuid).ToArray());
        Assert.AreEqual(0, tags.Folds.Count);

        var tree = OptionFolding.Fold(Tree("R", true, N(null, "Asia", N("j1", "Japan")), N("n2", "ASIA", N("j2", "japan"))),
            None);
        Assert.AreEqual(Canon(Tree("R", true, N(null, "Asia", N("j1", "Japan")), N("n2", "ASIA", N("j2", "japan")))),
            Canon(tree.Content));
        Assert.AreEqual(0, tree.Folds.Count);
    }

    [TestMethod]
    public void OnlyWhatMayAbsorbIsASurvivor()
    {
        // A merge lets only published options take others in: here the stored "Action" may not, so the new "action"
        // stays and takes in the later "ACTION".
        var content = Choice("G", true, C("a", "Action"), C("b", "action"), C("c", "ACTION"));
        var stored = content.Choices[0];
        var result = OptionFolding.Fold(content, new HashSet<string> { "a" },
            mayAbsorb: o => !ReferenceEquals(o, stored));
        CollectionAssert.AreEqual(new[] { "a", "b" }, result.Content.Choices.Select(c => c.Uuid).ToArray());
        CollectionAssert.AreEqual(new[] { new OptionFold("c", "b", "action") }, result.Folds.ToArray());

        // Nodes are asked by their records, at every level.
        var japan = N("j1", "Japan");
        var tree = Tree("R", true, N("n1", "Asia", japan), N("n2", "ASIA", N("j2", "japan")));
        var nodes = OptionFolding.Fold(tree, None, mayAbsorb: o => !ReferenceEquals(o, japan));
        Assert.AreEqual(Canon(Tree("R", true, N("n1", "Asia", N("j1", "Japan"), N("j2", "japan")))), Canon(nodes.Content));
    }

    [TestMethod]
    public void OnlyTheTypesOwnListFolds()
    {
        var odd = new CustomPropertyContentV1
        {
            Name = "N", Type = PropertyType.Number, IgnoreCase = true, Choices = [C("a", "A"), C("b", "a")],
        };
        Assert.AreSame(odd, OptionFolding.Fold(odd).Content);
    }
}

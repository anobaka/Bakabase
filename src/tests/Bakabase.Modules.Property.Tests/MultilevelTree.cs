using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Extensions;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Boundary coverage for MultilevelPropertyExtensions tree operations: label
/// chain lookup in both directions, node search, and value/branch extraction
/// with the leaf-only / include-intermediate switches.
/// </summary>
[TestClass]
public sealed class MultilevelTree
{
    private static MultilevelDataOptions Node(string value, string label, params MultilevelDataOptions[] children) =>
        new() { Value = value, Label = label, Children = children.Length > 0 ? children.ToList() : null };

    // Asia -> {Japan, China}, Europe -> {France}
    private static List<MultilevelDataOptions> Tree() =>
    [
        Node("v-asia", "Asia", Node("v-japan", "Japan"), Node("v-china", "China")),
        Node("v-europe", "Europe", Node("v-france", "France"))
    ];

    #region FindLabelChain (value -> path)

    [TestMethod]
    public void FindLabelChain_LeafValue_ReturnsFullPath()
    {
        CollectionAssert.AreEqual(new[] { "Europe", "France" }, Tree().FindLabelChain("v-france"));
    }

    [TestMethod]
    public void FindLabelChain_RootValue_ReturnsSingleLabel()
    {
        CollectionAssert.AreEqual(new[] { "Asia" }, Tree().FindLabelChain("v-asia"));
    }

    [TestMethod]
    public void FindLabelChain_UnknownValue_ReturnsNull()
    {
        Assert.IsNull(Tree().FindLabelChain("v-unknown"));
    }

    #endregion

    #region FindValueByLabelChain (path -> value)

    [TestMethod]
    public void FindValueByLabelChain_LeafPath_ReturnsNodeValue()
    {
        Assert.AreEqual("v-japan", Tree().FindValueByLabelChain(["Asia", "Japan"]));
    }

    [TestMethod]
    public void FindValueByLabelChain_IntermediatePath_ReturnsNodeValue()
    {
        Assert.AreEqual("v-asia", Tree().FindValueByLabelChain(["Asia"]));
    }

    [TestMethod]
    public void FindValueByLabelChain_UnknownAndEmptyPath_ReturnNull()
    {
        Assert.IsNull(Tree().FindValueByLabelChain(["Asia", "Korea"]));
        Assert.IsNull(Tree().FindValueByLabelChain([]));
    }

    [TestMethod]
    public void FindValuesByLabelChains_MapsEachChain_NullForMisses()
    {
        var values = Tree().FindValuesByLabelChains([["Asia", "Japan"], ["Asia", "Korea"]]);
        Assert.AreEqual(2, values.Count);
        Assert.AreEqual("v-japan", values[0]);
        Assert.IsNull(values[1]);
    }

    #endregion

    #region FindNode

    [TestMethod]
    public void FindFirstNode_MatchingPredicate_ReturnsNode()
    {
        var node = Tree().FindFirstNode(n => n.Value == "v-china");
        Assert.IsNotNull(node);
        Assert.AreEqual("China", node!.Label);
    }

    [TestMethod]
    public void FindFirstNode_NoMatch_ReturnsNull()
    {
        Assert.IsNull(Tree().FindFirstNode(n => n.Label == "Antarctica"));
    }

    #endregion

    #region ExtractValues

    [TestMethod]
    public void ExtractValues_AllNodes_IncludesIntermediates()
    {
        var values = Tree().ExtractValues(leafOnly: false).ToList();
        CollectionAssert.AreEquivalent(
            new List<string> { "v-asia", "v-japan", "v-china", "v-europe", "v-france" }, values);
    }

    [TestMethod]
    public void ExtractValues_LeafOnly_ExcludesIntermediates()
    {
        var values = Tree().ExtractValues(leafOnly: true).ToList();
        CollectionAssert.AreEquivalent(new List<string> { "v-japan", "v-china", "v-france" }, values);
    }

    #endregion

    #region ExtractBranches

    [TestMethod]
    public void ExtractBranches_LeafBranchesOnly()
    {
        var branches = Tree().ExtractBranches(includeIntermediate: false);
        Assert.AreEqual(3, branches.Count);
        Assert.IsTrue(branches.Any(b => b.Value == "v-japan" && b.Branch.SequenceEqual(new[] { "Asia", "Japan" })));
    }

    [TestMethod]
    public void ExtractBranches_IncludeIntermediate_AddsParentBranches()
    {
        var branches = Tree().ExtractBranches(includeIntermediate: true);
        Assert.AreEqual(5, branches.Count);
        Assert.IsTrue(branches.Any(b => b.Value == "v-asia" && b.Branch.SequenceEqual(new[] { "Asia" })));
    }

    #endregion
}

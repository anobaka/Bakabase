using System.Text.RegularExpressions;
using Bakabase.Abstractions.Extensions;

namespace Bakabase.Tests;

/// <summary>
/// Boundary coverage for the abstractions-level collection and regex utilities:
/// FirstNotNullOrDefault, ToNullIfEmpty, and named-group match merging.
/// </summary>
[TestClass]
public sealed class UtilityExtensionsTests
{
    #region FirstNotNullOrDefault

    [TestMethod]
    public void FirstNotNullOrDefault_ReturnsFirstNonNullProjection()
    {
        var result = new[] { "", "", "x", "y" }
            .FirstNotNullOrDefault(s => string.IsNullOrEmpty(s) ? null : s);
        Assert.AreEqual("x", result);
    }

    [TestMethod]
    public void FirstNotNullOrDefault_AllNullProjections_ReturnsDefault()
    {
        var result = new[] { "", "", "" }
            .FirstNotNullOrDefault(s => string.IsNullOrEmpty(s) ? null : s);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void FirstNotNullOrDefault_NullSource_ReturnsDefault()
    {
        var result = ((IEnumerable<string>?)null).FirstNotNullOrDefault(s => s);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void FirstNotNullOrDefault_ValueTypeProjection()
    {
        var result = new[] { "a", "5", "b" }
            .FirstNotNullOrDefault(s => int.TryParse(s, out var n) ? n : (int?)null);
        Assert.AreEqual(5, result);
    }

    #endregion

    #region ToNullIfEmpty

    [TestMethod]
    public void ToNullIfEmpty_NonEmptyList_ReturnsSameList()
    {
        var list = new List<int> { 1, 2 };
        Assert.AreSame(list, list.ToNullIfEmpty());
    }

    [TestMethod]
    public void ToNullIfEmpty_EmptyOrNull_ReturnsNull()
    {
        Assert.IsNull(new List<int>().ToNullIfEmpty());
        Assert.IsNull(((List<int>?)null).ToNullIfEmpty());
    }

    #endregion

    #region MatchAllAndMergeByNamedGroups

    [TestMethod]
    public void MatchAllAndMergeByNamedGroups_CollectsValuesPerNamedGroup()
    {
        var regex = new Regex(@"(?<key>\w+)=(?<val>\d+)");
        var merged = regex.MatchAllAndMergeByNamedGroups("a=1 b=2 c=3");
        CollectionAssert.AreEqual(new List<string> { "a", "b", "c" }, merged["key"]);
        CollectionAssert.AreEqual(new List<string> { "1", "2", "3" }, merged["val"]);
    }

    [TestMethod]
    public void MatchAllAndMergeByNamedGroups_NoMatch_ReturnsEmpty()
    {
        var regex = new Regex(@"(?<key>\w+)=(?<val>\d+)");
        Assert.AreEqual(0, regex.MatchAllAndMergeByNamedGroups("nothing here").Count);
    }

    [TestMethod]
    public void MatchAllAndMergeByNamedGroups_SingleMatch_OneValuePerGroup()
    {
        var regex = new Regex(@"(?<key>\w+)=(?<val>\d+)");
        var merged = regex.MatchAllAndMergeByNamedGroups("only=42");
        CollectionAssert.AreEqual(new List<string> { "only" }, merged["key"]);
        CollectionAssert.AreEqual(new List<string> { "42" }, merged["val"]);
    }

    #endregion
}

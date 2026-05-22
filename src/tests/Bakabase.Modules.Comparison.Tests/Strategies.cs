using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Components.Strategies;

namespace Bakabase.Modules.Comparison.Tests;

/// <summary>
/// Boundary coverage for the context-free comparison strategies. Each strategy's
/// Calculate returns a 0.0 - 1.0 score; these tests pin the score at the
/// boundaries (tolerance edges, empty sets, null inputs, type mismatches).
/// </summary>
[TestClass]
public sealed class Strategies
{
    // The strategies below do not read the context; null is safe.
    private static readonly ComparisonContext Ctx = null!;
    private const double Delta = 1e-9;

    #region StrictEqual

    private static readonly StrictEqualStrategy StrictEqual = new();

    [TestMethod]
    public void StrictEqual_BothNull_ReturnsOne()
        => Assert.AreEqual(1.0, StrictEqual.Calculate(null, null, null, Ctx), Delta);

    [TestMethod]
    public void StrictEqual_OneNull_ReturnsZero()
    {
        Assert.AreEqual(0.0, StrictEqual.Calculate(null, 5, null, Ctx), Delta);
        Assert.AreEqual(0.0, StrictEqual.Calculate(5, null, null, Ctx), Delta);
    }

    [TestMethod]
    public void StrictEqual_EqualVsUnequal()
    {
        Assert.AreEqual(1.0, StrictEqual.Calculate(5, 5, null, Ctx), Delta);
        Assert.AreEqual(1.0, StrictEqual.Calculate("a", "a", null, Ctx), Delta);
        Assert.AreEqual(0.0, StrictEqual.Calculate(5, 6, null, Ctx), Delta);
    }

    #endregion

    #region FixedTolerance

    private static readonly FixedToleranceStrategy FixedTolerance = new();

    [TestMethod]
    public void FixedTolerance_WithinAndOutsideTolerance()
    {
        const string param = """{"tolerance": 5}""";
        Assert.AreEqual(1.0, FixedTolerance.Calculate(10, 7, param, Ctx), Delta);
        Assert.AreEqual(1.0, FixedTolerance.Calculate(10, 5, param, Ctx), Delta); // exactly on the edge
        Assert.AreEqual(0.0, FixedTolerance.Calculate(10, 3, param, Ctx), Delta);
    }

    [TestMethod]
    public void FixedTolerance_NoParameter_RequiresExactMatch()
    {
        Assert.AreEqual(1.0, FixedTolerance.Calculate(10, 10, null, Ctx), Delta);
        Assert.AreEqual(0.0, FixedTolerance.Calculate(10, 11, null, Ctx), Delta);
    }

    [TestMethod]
    public void FixedTolerance_NonNumeric_ReturnsZero()
        => Assert.AreEqual(0.0, FixedTolerance.Calculate("abc", "def", null, Ctx), Delta);

    #endregion

    #region RelativeTolerance

    private static readonly RelativeToleranceStrategy RelativeTolerance = new();

    [TestMethod]
    public void RelativeTolerance_WithinAndOutsideDefaultFivePercent()
    {
        Assert.AreEqual(1.0, RelativeTolerance.Calculate(100, 103, null, Ctx), Delta);
        Assert.AreEqual(0.0, RelativeTolerance.Calculate(100, 110, null, Ctx), Delta);
    }

    [TestMethod]
    public void RelativeTolerance_BothZero_ReturnsOne()
        => Assert.AreEqual(1.0, RelativeTolerance.Calculate(0, 0, null, Ctx), Delta);

    [TestMethod]
    public void RelativeTolerance_CustomPercent()
    {
        const string param = """{"tolerancePercent": 0.2}""";
        Assert.AreEqual(1.0, RelativeTolerance.Calculate(100, 115, param, Ctx), Delta);
    }

    #endregion

    #region SameDay

    private static readonly SameDayStrategy SameDay = new();

    [TestMethod]
    public void SameDay_SameDateDifferentTime_ReturnsOne()
        => Assert.AreEqual(1.0,
            SameDay.Calculate(new DateTime(2024, 6, 1, 9, 0, 0), new DateTime(2024, 6, 1, 23, 30, 0), null, Ctx),
            Delta);

    [TestMethod]
    public void SameDay_DifferentDate_ReturnsZero()
        => Assert.AreEqual(0.0,
            SameDay.Calculate(new DateTime(2024, 6, 1), new DateTime(2024, 6, 2), null, Ctx), Delta);

    [TestMethod]
    public void SameDay_NonDate_ReturnsZero()
        => Assert.AreEqual(0.0, SameDay.Calculate("not a date", "also not", null, Ctx), Delta);

    #endregion

    #region SetIntersection

    private static readonly SetIntersectionStrategy SetIntersection = new();

    [TestMethod]
    public void SetIntersection_JaccardIndex()
    {
        // {a,b,c} vs {b,c,d}: intersection 2, union 4 -> 0.5
        var result = SetIntersection.Calculate(
            new List<string> { "a", "b", "c" }, new List<string> { "b", "c", "d" }, null, Ctx);
        Assert.AreEqual(0.5, result, Delta);
    }

    [TestMethod]
    public void SetIntersection_IdenticalAndDisjoint()
    {
        Assert.AreEqual(1.0, SetIntersection.Calculate(
            new List<string> { "a", "b" }, new List<string> { "a", "b" }, null, Ctx), Delta);
        Assert.AreEqual(0.0, SetIntersection.Calculate(
            new List<string> { "a" }, new List<string> { "b" }, null, Ctx), Delta);
    }

    [TestMethod]
    public void SetIntersection_IsCaseInsensitive()
        => Assert.AreEqual(1.0, SetIntersection.Calculate(
            new List<string> { "Tag" }, new List<string> { "TAG" }, null, Ctx), Delta);

    #endregion

    #region Subset

    private static readonly SubsetStrategy Subset = new();

    [TestMethod]
    public void Subset_OneSetContainedInOther_ReturnsOne()
    {
        Assert.AreEqual(1.0, Subset.Calculate(
            new List<string> { "a", "b" }, new List<string> { "a", "b", "c" }, null, Ctx), Delta);
        // either direction counts
        Assert.AreEqual(1.0, Subset.Calculate(
            new List<string> { "a", "b", "c" }, new List<string> { "a" }, null, Ctx), Delta);
    }

    [TestMethod]
    public void Subset_NeitherContained_ReturnsZero()
        => Assert.AreEqual(0.0, Subset.Calculate(
            new List<string> { "a", "x" }, new List<string> { "a", "b" }, null, Ctx), Delta);

    #endregion

    #region TextSimilarity

    private static readonly TextSimilarityStrategy TextSimilarity = new();

    [TestMethod]
    public void TextSimilarity_Identical_ReturnsOne()
        => Assert.AreEqual(1.0, TextSimilarity.Calculate("hello", "hello", null, Ctx), Delta);

    [TestMethod]
    public void TextSimilarity_BothEmpty_ReturnsOne()
        => Assert.AreEqual(1.0, TextSimilarity.Calculate("", "", null, Ctx), Delta);

    [TestMethod]
    public void TextSimilarity_OneEmpty_ReturnsZero()
        => Assert.AreEqual(0.0, TextSimilarity.Calculate("hello", "", null, Ctx), Delta);

    [TestMethod]
    public void TextSimilarity_PartialCharOverlap()
    {
        // char sets {a,b,c} vs {a,b,d}: intersection 2, union 4 -> 0.5
        Assert.AreEqual(0.5, TextSimilarity.Calculate("abc", "abd", null, Ctx), Delta);
    }

    #endregion

    #region TimeWindow

    private static readonly TimeWindowStrategy TimeWindow = new();

    [TestMethod]
    public void TimeWindow_WithinAndOutsideDefault24Hours()
    {
        Assert.AreEqual(1.0, TimeWindow.Calculate(
            new DateTime(2024, 6, 1, 0, 0, 0), new DateTime(2024, 6, 1, 10, 0, 0), null, Ctx), Delta);
        Assert.AreEqual(0.0, TimeWindow.Calculate(
            new DateTime(2024, 6, 1), new DateTime(2024, 6, 3), null, Ctx), Delta);
    }

    [TestMethod]
    public void TimeWindow_CustomWindow()
    {
        const string param = """{"windowHours": 72}""";
        Assert.AreEqual(1.0, TimeWindow.Calculate(
            new DateTime(2024, 6, 1), new DateTime(2024, 6, 3), param, Ctx), Delta);
    }

    [TestMethod]
    public void TimeWindow_NonDate_ReturnsZero()
        => Assert.AreEqual(0.0, TimeWindow.Calculate("x", "y", null, Ctx), Delta);

    #endregion
}

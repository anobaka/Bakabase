using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Boundary coverage for property search-filter matching
/// (<see cref="IPropertySearchHandler.IsMatch"/>): comparison boundaries for
/// numeric / date types, case-insensitive text operations, regex matching, and
/// the IsNull / IsNotNull / type-mismatch edges.
/// </summary>
[TestClass]
public sealed class SearchMatching
{
    private static readonly IPropertySearchHandler Number =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Number)!;

    private static readonly IPropertySearchHandler Text =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.SingleLineText)!;

    private static readonly IPropertySearchHandler Boolean =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Boolean)!;

    private static readonly IPropertySearchHandler Date =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.DateTime)!;

    #region Number

    [TestMethod]
    public void Number_Equals()
    {
        Assert.IsTrue(Number.IsMatch(3m, SearchOperation.Equals, 3m));
        Assert.IsFalse(Number.IsMatch(3m, SearchOperation.Equals, 4m));
    }

    [TestMethod]
    public void Number_NotEquals()
    {
        Assert.IsTrue(Number.IsMatch(3m, SearchOperation.NotEquals, 4m));
        Assert.IsFalse(Number.IsMatch(3m, SearchOperation.NotEquals, 3m));
    }

    [TestMethod]
    public void Number_GreaterThan_IsStrictAtBoundary()
    {
        Assert.IsTrue(Number.IsMatch(4m, SearchOperation.GreaterThan, 3m));
        Assert.IsFalse(Number.IsMatch(3m, SearchOperation.GreaterThan, 3m));
        Assert.IsFalse(Number.IsMatch(2m, SearchOperation.GreaterThan, 3m));
    }

    [TestMethod]
    public void Number_GreaterThanOrEquals_IncludesBoundary()
    {
        Assert.IsTrue(Number.IsMatch(3m, SearchOperation.GreaterThanOrEquals, 3m));
        Assert.IsTrue(Number.IsMatch(4m, SearchOperation.GreaterThanOrEquals, 3m));
        Assert.IsFalse(Number.IsMatch(2m, SearchOperation.GreaterThanOrEquals, 3m));
    }

    [TestMethod]
    public void Number_LessThan_And_LessThanOrEquals()
    {
        Assert.IsTrue(Number.IsMatch(2m, SearchOperation.LessThan, 3m));
        Assert.IsFalse(Number.IsMatch(3m, SearchOperation.LessThan, 3m));
        Assert.IsTrue(Number.IsMatch(3m, SearchOperation.LessThanOrEquals, 3m));
    }

    [TestMethod]
    public void Number_NegativeValues()
    {
        Assert.IsTrue(Number.IsMatch(-5m, SearchOperation.LessThan, 0m));
        Assert.IsTrue(Number.IsMatch(-1m, SearchOperation.GreaterThan, -2m));
    }

    [TestMethod]
    public void Number_MismatchedFilterValueType_ReturnsFalse()
    {
        // A non-decimal filter value cannot match a Number property.
        Assert.IsFalse(Number.IsMatch(3m, SearchOperation.Equals, "3"));
    }

    [TestMethod]
    public void Number_IsNull_And_IsNotNull()
    {
        Assert.IsTrue(Number.IsMatch(null, SearchOperation.IsNull, null));
        Assert.IsFalse(Number.IsMatch(null, SearchOperation.IsNotNull, null));
        Assert.IsFalse(Number.IsMatch(5m, SearchOperation.IsNull, null));
        Assert.IsTrue(Number.IsMatch(5m, SearchOperation.IsNotNull, null));
    }

    #endregion

    #region Text

    [TestMethod]
    public void Text_Equals_IsCaseInsensitive()
    {
        Assert.IsTrue(Text.IsMatch("Hello", SearchOperation.Equals, "hello"));
        Assert.IsFalse(Text.IsMatch("Hello", SearchOperation.Equals, "Hell"));
    }

    [TestMethod]
    public void Text_Contains_IsCaseInsensitive()
    {
        Assert.IsTrue(Text.IsMatch("Hello World", SearchOperation.Contains, "WORLD"));
        Assert.IsFalse(Text.IsMatch("Hello World", SearchOperation.Contains, "xyz"));
    }

    [TestMethod]
    public void Text_NotContains()
    {
        Assert.IsTrue(Text.IsMatch("Hello", SearchOperation.NotContains, "xyz"));
        Assert.IsFalse(Text.IsMatch("Hello", SearchOperation.NotContains, "ell"));
    }

    [TestMethod]
    public void Text_StartsWith_And_EndsWith()
    {
        Assert.IsTrue(Text.IsMatch("Hello World", SearchOperation.StartsWith, "hello"));
        Assert.IsTrue(Text.IsMatch("Hello World", SearchOperation.EndsWith, "WORLD"));
        Assert.IsFalse(Text.IsMatch("Hello World", SearchOperation.StartsWith, "World"));
    }

    [TestMethod]
    public void Text_Matches_Regex()
    {
        Assert.IsTrue(Text.IsMatch("abc123", SearchOperation.Matches, @"\d+"));
        Assert.IsFalse(Text.IsMatch("abcdef", SearchOperation.Matches, @"\d+"));
    }

    [TestMethod]
    public void Text_NotMatches_Regex()
    {
        Assert.IsTrue(Text.IsMatch("abcdef", SearchOperation.NotMatches, @"\d+"));
    }

    [TestMethod]
    public void Text_IsNull_And_IsNotNull()
    {
        Assert.IsTrue(Text.IsMatch(null, SearchOperation.IsNull, null));
        Assert.IsTrue(Text.IsMatch("x", SearchOperation.IsNotNull, null));
    }

    #endregion

    #region Boolean

    [TestMethod]
    public void Boolean_Equals_And_NotEquals()
    {
        Assert.IsTrue(Boolean.IsMatch(true, SearchOperation.Equals, true));
        Assert.IsFalse(Boolean.IsMatch(true, SearchOperation.Equals, false));
        Assert.IsTrue(Boolean.IsMatch(true, SearchOperation.NotEquals, false));
    }

    [TestMethod]
    public void Boolean_IsNull_And_IsNotNull()
    {
        Assert.IsTrue(Boolean.IsMatch(null, SearchOperation.IsNull, null));
        Assert.IsFalse(Boolean.IsMatch(true, SearchOperation.IsNull, null));
        Assert.IsTrue(Boolean.IsMatch(false, SearchOperation.IsNotNull, null));
    }

    #endregion

    #region DateTime

    [TestMethod]
    public void DateTime_Equals()
    {
        var d = new DateTime(2024, 6, 1, 12, 0, 0);
        Assert.IsTrue(Date.IsMatch(d, SearchOperation.Equals, d));
        Assert.IsFalse(Date.IsMatch(d, SearchOperation.Equals, d.AddDays(1)));
    }

    [TestMethod]
    public void DateTime_GreaterThan_And_LessThan()
    {
        var earlier = new DateTime(2024, 1, 1);
        var later = new DateTime(2024, 12, 31);
        Assert.IsTrue(Date.IsMatch(later, SearchOperation.GreaterThan, earlier));
        Assert.IsTrue(Date.IsMatch(earlier, SearchOperation.LessThan, later));
        Assert.IsFalse(Date.IsMatch(earlier, SearchOperation.GreaterThan, later));
    }

    [TestMethod]
    public void DateTime_GreaterThanOrEquals_IncludesBoundary()
    {
        var d = new DateTime(2024, 6, 1);
        Assert.IsTrue(Date.IsMatch(d, SearchOperation.GreaterThanOrEquals, d));
        Assert.IsFalse(Date.IsMatch(d, SearchOperation.GreaterThan, d));
    }

    #endregion
}

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

    private static readonly IPropertySearchHandler SingleChoice =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.SingleChoice)!;

    private static readonly IPropertySearchHandler MultipleChoice =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.MultipleChoice)!;

    private static readonly IPropertySearchHandler Time =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Time)!;

    private static readonly IPropertySearchHandler Percentage =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Percentage)!;

    private static readonly IPropertySearchHandler Rating =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Rating)!;

    private static readonly IPropertySearchHandler Formula =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Formula)!;

    private static readonly IPropertySearchHandler Tags =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Tags)!;

    private static readonly IPropertySearchHandler Multilevel =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Multilevel)!;

    private static readonly IPropertySearchHandler Attachment =
        PropertySystem.Property.TryGetSearchHandler(PropertyType.Attachment)!;

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

    #region SingleChoice

    [TestMethod]
    public void SingleChoice_Equals_And_NotEquals()
    {
        Assert.IsTrue(SingleChoice.IsMatch("uuid-1", SearchOperation.Equals, "uuid-1"));
        Assert.IsFalse(SingleChoice.IsMatch("uuid-1", SearchOperation.Equals, "uuid-2"));
        Assert.IsTrue(SingleChoice.IsMatch("uuid-1", SearchOperation.NotEquals, "uuid-2"));
    }

    [TestMethod]
    public void SingleChoice_IsNull_And_IsNotNull()
    {
        Assert.IsTrue(SingleChoice.IsMatch(null, SearchOperation.IsNull, null));
        Assert.IsTrue(SingleChoice.IsMatch("uuid-1", SearchOperation.IsNotNull, null));
    }

    #endregion

    #region MultipleChoice

    [TestMethod]
    public void MultipleChoice_Contains_RequiresAllFilterValues()
    {
        var dbValue = new List<string> { "a", "b", "c" };
        Assert.IsTrue(MultipleChoice.IsMatch(dbValue, SearchOperation.Contains, new List<string> { "a", "b" }));
        Assert.IsFalse(MultipleChoice.IsMatch(dbValue, SearchOperation.Contains, new List<string> { "a", "x" }));
    }

    [TestMethod]
    public void MultipleChoice_NotContains()
    {
        var dbValue = new List<string> { "a", "b" };
        Assert.IsTrue(MultipleChoice.IsMatch(dbValue, SearchOperation.NotContains, new List<string> { "x", "y" }));
        Assert.IsFalse(MultipleChoice.IsMatch(dbValue, SearchOperation.NotContains, new List<string> { "a" }));
    }

    [TestMethod]
    public void MultipleChoice_In_RequiresEveryValueWithinFilterSet()
    {
        Assert.IsTrue(MultipleChoice.IsMatch(new List<string> { "a", "b" },
            SearchOperation.In, new List<string> { "a", "b", "c" }));
        Assert.IsFalse(MultipleChoice.IsMatch(new List<string> { "a", "x" },
            SearchOperation.In, new List<string> { "a", "b" }));
    }

    #endregion

    #region Time

    [TestMethod]
    public void Time_Equals_And_NotEquals()
    {
        Assert.IsTrue(Time.IsMatch(TimeSpan.FromHours(2), SearchOperation.Equals, TimeSpan.FromHours(2)));
        Assert.IsTrue(Time.IsMatch(TimeSpan.FromHours(2), SearchOperation.NotEquals, TimeSpan.FromHours(3)));
    }

    [TestMethod]
    public void Time_GreaterThan_And_LessThan()
    {
        Assert.IsTrue(Time.IsMatch(TimeSpan.FromHours(3), SearchOperation.GreaterThan, TimeSpan.FromHours(2)));
        Assert.IsFalse(Time.IsMatch(TimeSpan.FromHours(2), SearchOperation.GreaterThan, TimeSpan.FromHours(2)));
        Assert.IsTrue(Time.IsMatch(TimeSpan.FromHours(1), SearchOperation.LessThan, TimeSpan.FromHours(2)));
    }

    #endregion

    #region Percentage / Rating

    [TestMethod]
    public void Percentage_ComparisonBoundaries()
    {
        Assert.IsTrue(Percentage.IsMatch(0.5m, SearchOperation.Equals, 0.5m));
        Assert.IsTrue(Percentage.IsMatch(0.6m, SearchOperation.GreaterThan, 0.5m));
        Assert.IsFalse(Percentage.IsMatch(0.5m, SearchOperation.GreaterThan, 0.5m));
    }

    [TestMethod]
    public void Rating_ComparisonBoundaries()
    {
        Assert.IsTrue(Rating.IsMatch(4m, SearchOperation.Equals, 4m));
        Assert.IsTrue(Rating.IsMatch(4m, SearchOperation.GreaterThanOrEquals, 4m));
        Assert.IsFalse(Rating.IsMatch(3m, SearchOperation.GreaterThanOrEquals, 4m));
    }

    #endregion

    #region Formula

    [TestMethod]
    public void Formula_Equals_IsCaseInsensitive()
    {
        Assert.IsTrue(Formula.IsMatch("Result", SearchOperation.Equals, "result"));
        Assert.IsFalse(Formula.IsMatch("Result", SearchOperation.Equals, "other"));
    }

    [TestMethod]
    public void Formula_Contains()
    {
        Assert.IsTrue(Formula.IsMatch("computed value", SearchOperation.Contains, "value"));
        Assert.IsFalse(Formula.IsMatch("computed value", SearchOperation.Contains, "missing"));
    }

    #endregion

    #region Tags / Multilevel

    [TestMethod]
    public void Tags_Contains_RequiresAllFilterValues()
    {
        var dbValue = new List<string> { "a", "b", "c" };
        Assert.IsTrue(Tags.IsMatch(dbValue, SearchOperation.Contains, new List<string> { "a", "b" }));
        Assert.IsFalse(Tags.IsMatch(dbValue, SearchOperation.Contains, new List<string> { "a", "x" }));
    }

    [TestMethod]
    public void Tags_In_RequiresEveryValueWithinFilterSet()
    {
        Assert.IsTrue(Tags.IsMatch(new List<string> { "a" },
            SearchOperation.In, new List<string> { "a", "b" }));
        Assert.IsFalse(Tags.IsMatch(new List<string> { "a", "x" },
            SearchOperation.In, new List<string> { "a", "b" }));
    }

    [TestMethod]
    public void Multilevel_Contains_And_In()
    {
        var dbValue = new List<string> { "n1", "n2" };
        Assert.IsTrue(Multilevel.IsMatch(dbValue, SearchOperation.Contains, new List<string> { "n1" }));
        Assert.IsTrue(Multilevel.IsMatch(dbValue, SearchOperation.In, new List<string> { "n1", "n2", "n3" }));
        Assert.IsFalse(Multilevel.IsMatch(dbValue, SearchOperation.Contains, new List<string> { "n9" }));
    }

    #endregion

    #region Attachment

    [TestMethod]
    public void Attachment_Contains_MatchesPathSubstring()
    {
        var dbValue = new List<string> { "/photos/a.jpg", "/photos/b.png" };
        Assert.IsTrue(Attachment.IsMatch(dbValue, SearchOperation.Contains, "b.png"));
        Assert.IsFalse(Attachment.IsMatch(dbValue, SearchOperation.Contains, "c.gif"));
    }

    [TestMethod]
    public void Attachment_NotContains()
    {
        var dbValue = new List<string> { "/photos/a.jpg" };
        Assert.IsTrue(Attachment.IsMatch(dbValue, SearchOperation.NotContains, "missing"));
        Assert.IsFalse(Attachment.IsMatch(dbValue, SearchOperation.NotContains, "a.jpg"));
    }

    #endregion
}

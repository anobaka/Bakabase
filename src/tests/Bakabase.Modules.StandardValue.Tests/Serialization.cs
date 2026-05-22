using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// Boundary coverage for StandardValue serialization. Every type must survive a
/// serialize -> deserialize round trip unchanged, including values that contain
/// the separator and escape characters of the serialization format
/// (',' low-level separator, ';' high-level separator, '\' escape).
/// </summary>
[TestClass]
public sealed class Serialization
{
    private static object? RoundTrip(object value, StandardValueType type)
    {
        var serialized = value.SerializeAsStandardValue(type);
        Assert.IsNotNull(serialized, $"Serializing a {type} value should not return null.");
        return serialized.DeserializeAsStandardValue(type);
    }

    private static void AssertNestedEqual(List<List<string>> expected, List<List<string>>? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Count, actual!.Count, "Outer list count mismatch.");
        for (var i = 0; i < expected.Count; i++)
        {
            CollectionAssert.AreEqual(expected[i], actual[i], $"Inner list at index {i} mismatch.");
        }
    }

    private static void AssertTagsEqual(List<TagValue> expected, List<TagValue>? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Count, actual!.Count, "Tag count mismatch.");
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i].Group, actual[i].Group, $"Tag at index {i} group mismatch.");
            Assert.AreEqual(expected[i].Name, actual[i].Name, $"Tag at index {i} name mismatch.");
        }
    }

    #region String

    [TestMethod]
    public void String_Plain_RoundTrips()
    {
        var result = (string?)RoundTrip("Hello, World", StandardValueType.String);
        Assert.AreEqual("Hello, World", result);
    }

    [TestMethod]
    public void String_Empty_RoundTrips()
    {
        var result = (string?)RoundTrip("", StandardValueType.String);
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void String_WithSeparatorAndEscapeChars_RoundTrips()
    {
        const string original = @"comma, semicolon; backslash\ done";
        var result = (string?)RoundTrip(original, StandardValueType.String);
        Assert.AreEqual(original, result);
    }

    #endregion

    #region ListString

    [TestMethod]
    public void ListString_Simple_RoundTrips()
    {
        var original = new List<string> { "alpha", "beta", "gamma" };
        var result = (List<string>?)RoundTrip(original, StandardValueType.ListString);
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(original, result);
    }

    [TestMethod]
    public void ListString_SingleItem_RoundTrips()
    {
        var original = new List<string> { "solo" };
        var result = (List<string>?)RoundTrip(original, StandardValueType.ListString);
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(original, result);
    }

    [TestMethod]
    public void ListString_ItemContainingComma_RoundTrips()
    {
        var original = new List<string> { "a,b", "plain" };
        var result = (List<string>?)RoundTrip(original, StandardValueType.ListString);
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(original, result);
    }

    [TestMethod]
    public void ListString_ItemContainingBackslash_RoundTrips()
    {
        var original = new List<string> { @"a\b", @"c\\d" };
        var result = (List<string>?)RoundTrip(original, StandardValueType.ListString);
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(original, result);
    }

    [TestMethod]
    public void ListString_ItemContainingSemicolon_RoundTrips()
    {
        var original = new List<string> { "a;b", "c" };
        var result = (List<string>?)RoundTrip(original, StandardValueType.ListString);
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(original, result);
    }

    #endregion

    #region Decimal

    [TestMethod]
    public void Decimal_Positive_RoundTrips()
    {
        var result = (decimal?)RoundTrip(123.45m, StandardValueType.Decimal);
        Assert.AreEqual(123.45m, result);
    }

    [TestMethod]
    public void Decimal_Negative_RoundTrips()
    {
        var result = (decimal?)RoundTrip(-987.654m, StandardValueType.Decimal);
        Assert.AreEqual(-987.654m, result);
    }

    [TestMethod]
    public void Decimal_Zero_RoundTrips()
    {
        var result = (decimal?)RoundTrip(0m, StandardValueType.Decimal);
        Assert.AreEqual(0m, result);
    }

    [TestMethod]
    public void Decimal_MaxValue_RoundTrips()
    {
        var result = (decimal?)RoundTrip(decimal.MaxValue, StandardValueType.Decimal);
        Assert.AreEqual(decimal.MaxValue, result);
    }

    #endregion

    #region Link

    [TestMethod]
    public void Link_Simple_RoundTrips()
    {
        var result = (LinkValue?)RoundTrip(new LinkValue("Google", "https://google.com"), StandardValueType.Link);
        Assert.IsNotNull(result);
        Assert.AreEqual("Google", result!.Text);
        Assert.AreEqual("https://google.com", result.Url);
    }

    [TestMethod]
    public void Link_UrlContainingComma_RoundTrips()
    {
        var result = (LinkValue?)RoundTrip(new LinkValue("Map", "https://x.com/place?ll=1,2"), StandardValueType.Link);
        Assert.IsNotNull(result);
        Assert.AreEqual("Map", result!.Text);
        Assert.AreEqual("https://x.com/place?ll=1,2", result.Url);
    }

    [TestMethod]
    public void Link_TextContainingComma_RoundTrips()
    {
        var result = (LinkValue?)RoundTrip(new LinkValue("one, two, three", "https://x.com"), StandardValueType.Link);
        Assert.IsNotNull(result);
        Assert.AreEqual("one, two, three", result!.Text);
        Assert.AreEqual("https://x.com", result.Url);
    }

    #endregion

    #region Boolean

    [TestMethod]
    public void Boolean_True_RoundTrips()
    {
        var result = (bool?)RoundTrip(true, StandardValueType.Boolean);
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void Boolean_False_RoundTrips()
    {
        var result = (bool?)RoundTrip(false, StandardValueType.Boolean);
        Assert.AreEqual(false, result);
    }

    #endregion

    #region DateTime

    [TestMethod]
    public void DateTime_MillisecondPrecision_RoundTrips()
    {
        var original = new DateTime(2023, 11, 7, 9, 15, 30, 250);
        var result = (DateTime?)RoundTrip(original, StandardValueType.DateTime);
        Assert.AreEqual(original, result);
    }

    [TestMethod]
    public void DateTime_DateOnlyMidnight_RoundTrips()
    {
        var original = new DateTime(2020, 1, 1);
        var result = (DateTime?)RoundTrip(original, StandardValueType.DateTime);
        Assert.AreEqual(original, result);
    }

    #endregion

    #region Time

    [TestMethod]
    public void Time_Zero_RoundTrips()
    {
        var result = (TimeSpan?)RoundTrip(TimeSpan.Zero, StandardValueType.Time);
        Assert.AreEqual(TimeSpan.Zero, result);
    }

    [TestMethod]
    public void Time_HoursMinutesSeconds_RoundTrips()
    {
        var original = new TimeSpan(0, 2, 30, 45);
        var result = (TimeSpan?)RoundTrip(original, StandardValueType.Time);
        Assert.AreEqual(original, result);
    }

    [TestMethod]
    public void Time_WithMilliseconds_RoundTrips()
    {
        var original = new TimeSpan(0, 1, 12, 33, 500);
        var result = (TimeSpan?)RoundTrip(original, StandardValueType.Time);
        Assert.AreEqual(original, result);
    }

    #endregion

    #region ListListString

    [TestMethod]
    public void ListListString_Simple_RoundTrips()
    {
        var original = new List<List<string>>
        {
            new() { "a", "b" },
            new() { "c" }
        };
        var result = (List<List<string>>?)RoundTrip(original, StandardValueType.ListListString);
        AssertNestedEqual(original, result);
    }

    [TestMethod]
    public void ListListString_InnerValueContainingComma_RoundTrips()
    {
        var original = new List<List<string>>
        {
            new() { "a,b", "c" }
        };
        var result = (List<List<string>>?)RoundTrip(original, StandardValueType.ListListString);
        AssertNestedEqual(original, result);
    }

    [TestMethod]
    public void ListListString_InnerValueContainingSemicolon_RoundTrips()
    {
        var original = new List<List<string>>
        {
            new() { "a;b" },
            new() { "c" }
        };
        var result = (List<List<string>>?)RoundTrip(original, StandardValueType.ListListString);
        AssertNestedEqual(original, result);
    }

    #endregion

    #region ListTag

    [TestMethod]
    public void ListTag_Simple_RoundTrips()
    {
        var original = new List<TagValue>
        {
            new("Studio", "Ghibli"),
            new("Genre", "Fantasy")
        };
        var result = (List<TagValue>?)RoundTrip(original, StandardValueType.ListTag);
        AssertTagsEqual(original, result);
    }

    [TestMethod]
    public void ListTag_NameContainingComma_RoundTrips()
    {
        var original = new List<TagValue>
        {
            new("Group", "a,b,c")
        };
        var result = (List<TagValue>?)RoundTrip(original, StandardValueType.ListTag);
        AssertTagsEqual(original, result);
    }

    [TestMethod]
    public void ListTag_NameContainingSemicolon_RoundTrips()
    {
        var original = new List<TagValue>
        {
            new("Group", "a;b"),
            new("Other", "plain")
        };
        var result = (List<TagValue>?)RoundTrip(original, StandardValueType.ListTag);
        AssertTagsEqual(original, result);
    }

    #endregion
}

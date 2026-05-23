using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Tests;

/// <summary>
/// Round-trip coverage for the awkward serialization cases: list/tag values whose
/// content contains the separator or escape characters, tags with a null group, and the
/// empty-list edge.
/// </summary>
[TestClass]
public sealed class SerializationEdgeCases
{
    private static T RoundTrip<T>(T value, StandardValueType type)
    {
        var serialized = ((object?)value).SerializeAsStandardValue(type);
        return (T)serialized!.DeserializeAsStandardValue(type)!;
    }

    [TestMethod]
    public void ListString_RoundTrips_ValuesWithSeparatorAndEscapeChar()
    {
        var original = new List<string> { "a,b", "c\\d", "x\\,y", "plain" };
        CollectionAssert.AreEqual(original, RoundTrip(original, StandardValueType.ListString));
    }

    [TestMethod]
    public void ListTag_RoundTrips_NullGroup()
    {
        var original = new List<TagValue> { new(null, "NoGroup"), new("Grp", "WithGroup") };
        CollectionAssert.AreEqual(original, RoundTrip(original, StandardValueType.ListTag));
    }

    [TestMethod]
    public void ListTag_RoundTrips_NamesWithSeparatorsAndEscapeChar()
    {
        var original = new List<TagValue>
        {
            new("G", "name,with,commas"),
            new("G", "name;with;semicolons"),
            new("G", "name\\with\\backslash"),
        };
        CollectionAssert.AreEqual(original, RoundTrip(original, StandardValueType.ListTag));
    }

    [TestMethod]
    public void ListListString_RoundTrips_ValuesWithSeparators()
    {
        var original = new List<List<string>>
        {
            new() { "a,b", "c" },
            new() { "d;e", "f\\g" },
        };
        var result = RoundTrip(original, StandardValueType.ListListString);
        Assert.AreEqual(2, result.Count);
        CollectionAssert.AreEqual(original[0], result[0]);
        CollectionAssert.AreEqual(original[1], result[1]);
    }

    [TestMethod]
    public void Link_RoundTrips_TextContainingSeparator()
    {
        var original = new LinkValue("Comma, separated, text", "https://example.com");
        Assert.AreEqual(original, RoundTrip(original, StandardValueType.Link));
    }

    [TestMethod]
    public void ListString_EmptyList_SerializesToEmptyString()
    {
        Assert.AreEqual("", ((object)new List<string>()).SerializeAsStandardValue(StandardValueType.ListString));
    }
}

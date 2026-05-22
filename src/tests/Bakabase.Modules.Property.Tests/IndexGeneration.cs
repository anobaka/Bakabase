using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.StandardValue.Models.Domain;
using DomainProperty = Bakabase.Abstractions.Models.Domain.Property;

namespace Bakabase.Modules.Property.Tests;

/// <summary>
/// Boundary coverage for inverted-index entry generation
/// (IPropertyIndexProvider.GenerateIndexEntries): null values produce no
/// entries, list types produce one entry per item, and numeric / date types
/// carry a comparable RangeValue for range queries.
/// </summary>
[TestClass]
public sealed class IndexGeneration
{
    private static DomainProperty Prop(PropertyType type) =>
        new(PropertyPool.Custom, 1, type, "Test", null, 0);

    private static List<PropertyIndexEntry> Entries(PropertyType type, object? dbValue) =>
        PropertySystem.Property.TryGetIndexProvider(type)!.GenerateIndexEntries(Prop(type), dbValue).ToList();

    [TestMethod]
    public void NullValue_ProducesNoEntries()
    {
        Assert.AreEqual(0, Entries(PropertyType.SingleLineText, null).Count);
        Assert.AreEqual(0, Entries(PropertyType.Number, null).Count);
        Assert.AreEqual(0, Entries(PropertyType.MultipleChoice, null).Count);
    }

    [TestMethod]
    public void SingleLineText_ProducesSingleKeyWithoutRangeValue()
    {
        var entries = Entries(PropertyType.SingleLineText, "Hello");
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("Hello", entries[0].Key);
        Assert.IsNull(entries[0].RangeValue);
    }

    [TestMethod]
    public void Number_ProducesKeyAndComparableRangeValue()
    {
        var entries = Entries(PropertyType.Number, 42m);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("42", entries[0].Key);
        Assert.AreEqual(42m, entries[0].RangeValue);
    }

    [TestMethod]
    public void Boolean_ProducesKeyWithoutRangeValue()
    {
        var entries = Entries(PropertyType.Boolean, true);
        Assert.AreEqual(1, entries.Count);
        Assert.IsNull(entries[0].RangeValue);
    }

    [TestMethod]
    public void DateTime_ProducesComparableRangeValue()
    {
        var entries = Entries(PropertyType.DateTime, new DateTime(2024, 6, 1));
        Assert.AreEqual(1, entries.Count);
        Assert.IsNotNull(entries[0].RangeValue);
    }

    [TestMethod]
    public void MultipleChoice_ProducesOneEntryPerItem()
    {
        var entries = Entries(PropertyType.MultipleChoice, new List<string> { "uuid-a", "uuid-b", "uuid-c" });
        Assert.AreEqual(3, entries.Count);
        CollectionAssert.AreEquivalent(
            new List<string> { "uuid-a", "uuid-b", "uuid-c" },
            entries.Select(e => e.Key).ToList());
    }

    [TestMethod]
    public void MultipleChoice_EmptyList_ProducesNoEntries()
    {
        Assert.AreEqual(0, Entries(PropertyType.MultipleChoice, new List<string>()).Count);
    }

    [TestMethod]
    public void MultipleChoice_SkipsNullAndEmptyItems()
    {
        var entries = Entries(PropertyType.MultipleChoice, new List<string> { "uuid-a", "", null! });
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("uuid-a", entries[0].Key);
    }

    [TestMethod]
    public void MultilineText_ProducesSingleKeyWithoutRangeValue()
    {
        var entries = Entries(PropertyType.MultilineText, "line1\nline2");
        Assert.AreEqual(1, entries.Count);
        Assert.IsNull(entries[0].RangeValue);
    }

    [TestMethod]
    public void SingleChoice_ProducesSingleKey()
    {
        var entries = Entries(PropertyType.SingleChoice, "uuid-x");
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("uuid-x", entries[0].Key);
    }

    [TestMethod]
    public void Formula_ProducesSingleKey()
        => Assert.AreEqual(1, Entries(PropertyType.Formula, "computed").Count);

    [TestMethod]
    public void Percentage_ProducesComparableRangeValue()
    {
        var entries = Entries(PropertyType.Percentage, 0.5m);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(0.5m, entries[0].RangeValue);
    }

    [TestMethod]
    public void Rating_ProducesComparableRangeValue()
    {
        var entries = Entries(PropertyType.Rating, 4m);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(4m, entries[0].RangeValue);
    }

    [TestMethod]
    public void Date_ProducesComparableRangeValue()
    {
        var entries = Entries(PropertyType.Date, new DateTime(2024, 6, 1));
        Assert.AreEqual(1, entries.Count);
        Assert.IsNotNull(entries[0].RangeValue);
    }

    [TestMethod]
    public void Time_ProducesComparableRangeValue()
    {
        var entries = Entries(PropertyType.Time, TimeSpan.FromHours(2));
        Assert.AreEqual(1, entries.Count);
        Assert.IsNotNull(entries[0].RangeValue);
    }

    [TestMethod]
    public void Link_ProducesEntriesForTextAndUrl()
    {
        var entries = Entries(PropertyType.Link, new LinkValue("Google", "https://google.com"));
        Assert.AreEqual(2, entries.Count);
        CollectionAssert.AreEquivalent(
            new List<string> { "Google", "https://google.com" }, entries.Select(e => e.Key).ToList());
    }

    [TestMethod]
    public void Link_EmptyUrl_ProducesOnlyTextEntry()
    {
        var entries = Entries(PropertyType.Link, new LinkValue("OnlyText", null));
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("OnlyText", entries[0].Key);
    }

    [TestMethod]
    public void Attachment_ProducesOneEntryPerPath()
        => Assert.AreEqual(2, Entries(PropertyType.Attachment, new List<string> { "/a.jpg", "/b.png" }).Count);

    [TestMethod]
    public void Tags_ProducesOneEntryPerTag()
        => Assert.AreEqual(3, Entries(PropertyType.Tags, new List<string> { "t-1", "t-2", "t-3" }).Count);

    [TestMethod]
    public void Multilevel_ProducesOneEntryPerNode()
        => Assert.AreEqual(2, Entries(PropertyType.Multilevel, new List<string> { "node-1", "node-2" }).Count);
}

using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
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
}

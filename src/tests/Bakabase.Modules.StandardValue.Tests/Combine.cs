using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using FluentAssertions;

namespace Bakabase.Modules.StandardValue.Tests;

[TestClass]
public sealed class Combine
{
    #region Single-Value Types (First Non-Null Wins)

    [TestMethod]
    public void Combine_String_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new object?[] { null, "first", "second", null };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.String);

        // Assert
        result.Should().Be("first");
    }

    [TestMethod]
    public void Combine_String_AllNull_ReturnsNull()
    {
        // Arrange
        var values = new object?[] { null, null, null };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.String);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void Combine_String_EmptyCollection_ReturnsNull()
    {
        // Arrange
        var values = Array.Empty<object?>();

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.String);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void Combine_Decimal_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new object?[] { null, 42.5m, 100m };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.Decimal);

        // Assert
        result.Should().Be(42.5m);
    }

    [TestMethod]
    public void Combine_Boolean_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new object?[] { null, true, false };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.Boolean);

        // Assert
        result.Should().Be(true);
    }

    [TestMethod]
    public void Combine_DateTime_ReturnsFirstNonNull()
    {
        // Arrange
        var date1 = new DateTime(2024, 1, 1);
        var date2 = new DateTime(2024, 12, 31);
        var values = new object?[] { null, date1, date2 };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.DateTime);

        // Assert
        result.Should().Be(date1);
    }

    [TestMethod]
    public void Combine_Time_ReturnsFirstNonNull()
    {
        // Arrange
        var time1 = TimeSpan.FromHours(1);
        var time2 = TimeSpan.FromHours(2);
        var values = new object?[] { null, time1, time2 };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.Time);

        // Assert
        result.Should().Be(time1);
    }

    [TestMethod]
    public void Combine_Link_ReturnsFirstNonNull()
    {
        // Arrange
        var link1 = new LinkValue("First", "https://first.com");
        var link2 = new LinkValue("Second", "https://second.com");
        var values = new object?[] { null, link1, link2 };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.Link);

        // Assert
        result.Should().Be(link1);
    }

    #endregion

    #region Collection Types (Union/Aggregation)

    [TestMethod]
    public void Combine_ListString_AggregatesWithDeduplication()
    {
        // Arrange
        var list1 = new List<string> { "a", "b", "c" };
        var list2 = new List<string> { "b", "c", "d" };
        var list3 = new List<string> { "d", "e" };
        var values = new object?[] { list1, null, list2, list3 };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListString) as List<string>;

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(new[] { "a", "b", "c", "d", "e" });
    }

    [TestMethod]
    public void Combine_ListString_EmptyLists_ReturnsNull()
    {
        // Arrange
        var values = new object?[] { new List<string>(), new List<string>() };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListString);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void Combine_ListString_AllNull_ReturnsNull()
    {
        // Arrange
        var values = new object?[] { null, null };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListString);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void Combine_ListTag_AggregatesWithDeduplicationByGroupAndName()
    {
        // Arrange
        var list1 = new List<TagValue>
        {
            new TagValue("Group1", "Tag1"),
            new TagValue(null, "Tag2")
        };
        var list2 = new List<TagValue>
        {
            new TagValue("Group1", "Tag1"), // Duplicate
            new TagValue("Group2", "Tag3")
        };
        var list3 = new List<TagValue>
        {
            new TagValue(null, "Tag2"), // Duplicate (no group)
            new TagValue(null, "Tag4")
        };
        var values = new object?[] { list1, null, list2, list3 };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListTag) as List<TagValue>;

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(4);
        result.Should().ContainEquivalentOf(new TagValue("Group1", "Tag1"));
        result.Should().ContainEquivalentOf(new TagValue(null, "Tag2"));
        result.Should().ContainEquivalentOf(new TagValue("Group2", "Tag3"));
        result.Should().ContainEquivalentOf(new TagValue(null, "Tag4"));
    }

    [TestMethod]
    public void Combine_ListTag_EmptyLists_ReturnsNull()
    {
        // Arrange
        var values = new object?[] { new List<TagValue>(), new List<TagValue>() };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListTag);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void Combine_ListListString_AggregatesWithDeduplication()
    {
        // Arrange
        var list1 = new List<List<string>>
        {
            new List<string> { "a", "b" },
            new List<string> { "c" }
        };
        var list2 = new List<List<string>>
        {
            new List<string> { "a", "b" }, // Duplicate
            new List<string> { "d", "e" }
        };
        var values = new object?[] { list1, null, list2 };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListListString) as List<List<string>>;

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
    }

    [TestMethod]
    public void Combine_ListListString_EmptyLists_ReturnsNull()
    {
        // Arrange
        var values = new object?[] { new List<List<string>>(), new List<List<string>>() };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListListString);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Combine_SingleValue_ReturnsValue()
    {
        // Arrange
        var values = new object?[] { "only-one" };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.String);

        // Assert
        result.Should().Be("only-one");
    }

    [TestMethod]
    public void Combine_ListString_SingleList_ReturnsList()
    {
        // Arrange
        var list = new List<string> { "a", "b" };
        var values = new object?[] { list };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListString) as List<string>;

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(list);
    }

    [TestMethod]
    public void Combine_ListTag_PreservesInsertionOrder()
    {
        // Arrange
        var list1 = new List<TagValue>
        {
            new TagValue(null, "Z"),
            new TagValue(null, "A")
        };
        var list2 = new List<TagValue>
        {
            new TagValue(null, "M"),
            new TagValue(null, "Z") // Duplicate
        };
        var values = new object?[] { list1, list2 };

        // Act
        var result = StandardValueSystem.Combine(values, StandardValueType.ListTag) as List<TagValue>;

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result![0].Name.Should().Be("Z");
        result![1].Name.Should().Be("A");
        result![2].Name.Should().Be("M");
    }

    #endregion
}

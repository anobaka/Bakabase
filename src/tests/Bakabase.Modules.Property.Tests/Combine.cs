using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using FluentAssertions;

namespace Bakabase.Modules.Property.Tests;

[TestClass]
public sealed class Combine
{
    #region Single-Value Property Types (First Non-Null Wins)

    [TestMethod]
    public void CombineSerializedDbValues_SingleLineText_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new[]
        {
            null,
            "first".SerializeAsStandardValue(StandardValueType.String),
            "second".SerializeAsStandardValue(StandardValueType.String)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.SingleLineText, values);

        // Assert
        result.Should().Be("first".SerializeAsStandardValue(StandardValueType.String));
    }

    [TestMethod]
    public void CombineSerializedDbValues_MultilineText_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new[]
        {
            null,
            "first line".SerializeAsStandardValue(StandardValueType.String),
            "second line".SerializeAsStandardValue(StandardValueType.String)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.MultilineText, values);

        // Assert
        result.Should().Be("first line".SerializeAsStandardValue(StandardValueType.String));
    }

    [TestMethod]
    public void CombineSerializedDbValues_Number_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new[]
        {
            null,
            42.5m.SerializeAsStandardValue(StandardValueType.Decimal),
            100m.SerializeAsStandardValue(StandardValueType.Decimal)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Number, values);

        // Assert
        result.Should().Be(42.5m.SerializeAsStandardValue(StandardValueType.Decimal));
    }

    [TestMethod]
    public void CombineSerializedDbValues_Percentage_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new[]
        {
            null,
            0.75m.SerializeAsStandardValue(StandardValueType.Decimal),
            0.5m.SerializeAsStandardValue(StandardValueType.Decimal)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Percentage, values);

        // Assert
        result.Should().Be(0.75m.SerializeAsStandardValue(StandardValueType.Decimal));
    }

    [TestMethod]
    public void CombineSerializedDbValues_Rating_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new[]
        {
            null,
            4m.SerializeAsStandardValue(StandardValueType.Decimal),
            5m.SerializeAsStandardValue(StandardValueType.Decimal)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Rating, values);

        // Assert
        result.Should().Be(4m.SerializeAsStandardValue(StandardValueType.Decimal));
    }

    [TestMethod]
    public void CombineSerializedDbValues_Boolean_ReturnsFirstNonNull()
    {
        // Arrange
        var values = new[]
        {
            null,
            true.SerializeAsStandardValue(StandardValueType.Boolean),
            false.SerializeAsStandardValue(StandardValueType.Boolean)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Boolean, values);

        // Assert
        result.Should().Be(true.SerializeAsStandardValue(StandardValueType.Boolean));
    }

    [TestMethod]
    public void CombineSerializedDbValues_Link_ReturnsFirstNonNull()
    {
        // Arrange
        var link1 = new LinkValue("First", "https://first.com");
        var link2 = new LinkValue("Second", "https://second.com");
        var values = new[]
        {
            null,
            link1.SerializeAsStandardValue(StandardValueType.Link),
            link2.SerializeAsStandardValue(StandardValueType.Link)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Link, values);

        // Assert
        result.Should().Be(link1.SerializeAsStandardValue(StandardValueType.Link));
    }

    [TestMethod]
    public void CombineSerializedDbValues_Date_ReturnsFirstNonNull()
    {
        // Arrange
        var date1 = new DateTime(2024, 1, 1);
        var date2 = new DateTime(2024, 12, 31);
        var values = new[]
        {
            null,
            date1.SerializeAsStandardValue(StandardValueType.DateTime),
            date2.SerializeAsStandardValue(StandardValueType.DateTime)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Date, values);

        // Assert
        result.Should().Be(date1.SerializeAsStandardValue(StandardValueType.DateTime));
    }

    [TestMethod]
    public void CombineSerializedDbValues_DateTime_ReturnsFirstNonNull()
    {
        // Arrange
        var date1 = new DateTime(2024, 1, 1, 10, 30, 0);
        var date2 = new DateTime(2024, 12, 31, 23, 59, 59);
        var values = new[]
        {
            null,
            date1.SerializeAsStandardValue(StandardValueType.DateTime),
            date2.SerializeAsStandardValue(StandardValueType.DateTime)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.DateTime, values);

        // Assert
        result.Should().Be(date1.SerializeAsStandardValue(StandardValueType.DateTime));
    }

    [TestMethod]
    public void CombineSerializedDbValues_Time_ReturnsFirstNonNull()
    {
        // Arrange
        var time1 = TimeSpan.FromHours(10.5);
        var time2 = TimeSpan.FromHours(15);
        var values = new[]
        {
            null,
            time1.SerializeAsStandardValue(StandardValueType.Time),
            time2.SerializeAsStandardValue(StandardValueType.Time)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Time, values);

        // Assert
        result.Should().Be(time1.SerializeAsStandardValue(StandardValueType.Time));
    }

    [TestMethod]
    public void CombineSerializedDbValues_SingleChoice_ReturnsFirstNonNull()
    {
        // Arrange - SingleChoice uses String in DB (UUID reference)
        var values = new[]
        {
            null,
            "choice-uuid-1".SerializeAsStandardValue(StandardValueType.String),
            "choice-uuid-2".SerializeAsStandardValue(StandardValueType.String)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.SingleChoice, values);

        // Assert - SingleChoice returns first non-null (it's a single-value type)
        result.Should().Be("choice-uuid-1".SerializeAsStandardValue(StandardValueType.String));
    }

    #endregion

    #region Multi-Value Property Types (Aggregation/Union)

    [TestMethod]
    public void CombineSerializedDbValues_MultipleChoice_AggregatesValues()
    {
        // Arrange
        var list1 = new List<string> { "a", "b" };
        var list2 = new List<string> { "b", "c" };
        var values = new[]
        {
            list1.SerializeAsStandardValue(StandardValueType.ListString),
            null,
            list2.SerializeAsStandardValue(StandardValueType.ListString)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.MultipleChoice, values);

        // Assert
        var deserialized = StandardValueSystem.Deserialize<List<string>>(result, StandardValueType.ListString);
        deserialized.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [TestMethod]
    public void CombineSerializedDbValues_Tags_AggregatesValues()
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
        var values = new[]
        {
            list1.SerializeAsStandardValue(StandardValueType.ListTag),
            null,
            list2.SerializeAsStandardValue(StandardValueType.ListTag)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Tags, values);

        // Assert
        var deserialized = StandardValueSystem.Deserialize<List<TagValue>>(result, StandardValueType.ListTag);
        deserialized.Should().HaveCount(3);
        deserialized.Should().ContainEquivalentOf(new TagValue("Group1", "Tag1"));
        deserialized.Should().ContainEquivalentOf(new TagValue(null, "Tag2"));
        deserialized.Should().ContainEquivalentOf(new TagValue("Group2", "Tag3"));
    }

    [TestMethod]
    public void CombineSerializedDbValues_Multilevel_AggregatesValues()
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
        var values = new[]
        {
            list1.SerializeAsStandardValue(StandardValueType.ListListString),
            null,
            list2.SerializeAsStandardValue(StandardValueType.ListListString)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Multilevel, values);

        // Assert
        var deserialized = StandardValueSystem.Deserialize<List<List<string>>>(result, StandardValueType.ListListString);
        deserialized.Should().HaveCount(3); // Deduplicated
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void CombineSerializedDbValues_AllNull_ReturnsNull()
    {
        // Arrange
        var values = new string?[] { null, null, null };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.SingleLineText, values);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void CombineSerializedDbValues_AllEmpty_ReturnsNull()
    {
        // Arrange
        var values = new[] { "", "", "" };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.SingleLineText, values);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void CombineSerializedDbValues_EmptyCollection_ReturnsNull()
    {
        // Arrange
        var values = Array.Empty<string?>();

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.SingleLineText, values);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void CombineSerializedDbValues_SingleValue_ReturnsSameValue()
    {
        // Arrange
        var value = "only-one".SerializeAsStandardValue(StandardValueType.String);
        var values = new[] { value };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.SingleLineText, values);

        // Assert
        result.Should().Be(value);
    }

    [TestMethod]
    public void CombineSerializedDbValues_MultipleChoice_EmptyLists_ReturnsNull()
    {
        // Arrange
        var emptyList = new List<string>();
        var values = new[]
        {
            emptyList.SerializeAsStandardValue(StandardValueType.ListString),
            emptyList.SerializeAsStandardValue(StandardValueType.ListString)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.MultipleChoice, values);

        // Assert
        result.Should().BeNull();
    }

    [TestMethod]
    public void CombineSerializedDbValues_Tags_EmptyLists_ReturnsNull()
    {
        // Arrange
        var emptyList = new List<TagValue>();
        var values = new[]
        {
            emptyList.SerializeAsStandardValue(StandardValueType.ListTag),
            emptyList.SerializeAsStandardValue(StandardValueType.ListTag)
        };

        // Act
        var result = PropertySystem.Property.CombineSerializedDbValues(PropertyType.Tags, values);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}

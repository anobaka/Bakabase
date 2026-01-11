using Bakabase.Modules.BulkModification.Components.Processors.ListTag;
using Bakabase.Modules.StandardValue.Models.Domain;
using FluentAssertions;

namespace Bakabase.Modules.BulkModification.Tests.Processors;

[TestClass]
public class ListTagProcessorTests
{
    private readonly BulkModificationListTagProcessor _processor = new();

    #region Delete

    [TestMethod]
    public void Delete_WithValue_ReturnsNull()
    {
        var tags = new List<TagValue>
        {
            new("Group1", "Tag1"),
            new("Group2", "Tag2")
        };
        var result = _processor.Process(tags, (int)BulkModificationListTagProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    [TestMethod]
    public void Delete_WithNull_ReturnsNull()
    {
        var result = _processor.Process(null, (int)BulkModificationListTagProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    #endregion

    #region SetWithFixedValue

    [TestMethod]
    public void SetWithFixedValue_ReturnsNewList()
    {
        var newTags = new List<TagValue>
        {
            new("NewGroup", "NewTag1"),
            new("NewGroup", "NewTag2")
        };
        var options = new BulkModificationListTagProcessorOptions { Value = newTags };
        var result = _processor.Process(
            new List<TagValue> { new("Old", "OldTag") },
            (int)BulkModificationListTagProcessOperation.SetWithFixedValue,
            options);

        result.Should().BeEquivalentTo(newTags);
    }

    [TestMethod]
    public void SetWithFixedValue_WithNullCurrent_ReturnsNewList()
    {
        var newTags = new List<TagValue> { new("Group", "Tag") };
        var options = new BulkModificationListTagProcessorOptions { Value = newTags };
        var result = _processor.Process(null, (int)BulkModificationListTagProcessOperation.SetWithFixedValue, options);

        result.Should().BeEquivalentTo(newTags);
    }

    #endregion

    #region Append

    [TestMethod]
    public void Append_AddsTagsToEnd()
    {
        var current = new List<TagValue> { new("Group1", "Tag1") };
        var toAppend = new List<TagValue> { new("Group2", "Tag2"), new("Group3", "Tag3") };
        var options = new BulkModificationListTagProcessorOptions { Value = toAppend };

        var result = (List<TagValue>?)_processor.Process(current, (int)BulkModificationListTagProcessOperation.Append, options);

        result.Should().HaveCount(3);
        result![0].Should().BeEquivalentTo(new TagValue("Group1", "Tag1"));
        result[1].Should().BeEquivalentTo(new TagValue("Group2", "Tag2"));
        result[2].Should().BeEquivalentTo(new TagValue("Group3", "Tag3"));
    }

    [TestMethod]
    public void Append_WithNullCurrent_ReturnsValueList()
    {
        var toAppend = new List<TagValue> { new("Group", "Tag") };
        var options = new BulkModificationListTagProcessorOptions { Value = toAppend };
        var result = _processor.Process(null, (int)BulkModificationListTagProcessOperation.Append, options);

        result.Should().BeEquivalentTo(toAppend);
    }

    [TestMethod]
    public void Append_WithNullValue_ReturnsCurrent()
    {
        var current = new List<TagValue> { new("Group", "Tag") };
        var options = new BulkModificationListTagProcessorOptions { Value = null };
        var result = _processor.Process(current, (int)BulkModificationListTagProcessOperation.Append, options);

        result.Should().BeEquivalentTo(current);
    }

    [TestMethod]
    public void Append_WithEmptyValue_ReturnsCurrent()
    {
        var current = new List<TagValue> { new("Group", "Tag") };
        var options = new BulkModificationListTagProcessorOptions { Value = new List<TagValue>() };
        var result = _processor.Process(current, (int)BulkModificationListTagProcessOperation.Append, options);

        result.Should().BeEquivalentTo(current);
    }

    #endregion

    #region Prepend

    [TestMethod]
    public void Prepend_AddsTagsToStart()
    {
        var current = new List<TagValue> { new("Group2", "Tag2") };
        var toPrepend = new List<TagValue> { new("Group1", "Tag1") };
        var options = new BulkModificationListTagProcessorOptions { Value = toPrepend };

        var result = (List<TagValue>?)_processor.Process(current, (int)BulkModificationListTagProcessOperation.Prepend, options);

        result.Should().HaveCount(2);
        result![0].Should().BeEquivalentTo(new TagValue("Group1", "Tag1"));
        result[1].Should().BeEquivalentTo(new TagValue("Group2", "Tag2"));
    }

    [TestMethod]
    public void Prepend_WithNullCurrent_ReturnsValueList()
    {
        var toPrepend = new List<TagValue> { new("Group", "Tag") };
        var options = new BulkModificationListTagProcessorOptions { Value = toPrepend };
        var result = _processor.Process(null, (int)BulkModificationListTagProcessOperation.Prepend, options);

        result.Should().BeEquivalentTo(toPrepend);
    }

    #endregion

    #region Remove

    [TestMethod]
    public void Remove_RemovesMatchingTags()
    {
        var current = new List<TagValue>
        {
            new("Group1", "Tag1"),
            new("Group2", "Tag2"),
            new("Group3", "Tag3")
        };
        var toRemove = new List<TagValue> { new("Group2", "Tag2") };
        var options = new BulkModificationListTagProcessorOptions { Value = toRemove };

        var result = (List<TagValue>?)_processor.Process(current, (int)BulkModificationListTagProcessOperation.Remove, options);

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Group == "Group1" && t.Name == "Tag1");
        result.Should().Contain(t => t.Group == "Group3" && t.Name == "Tag3");
        result.Should().NotContain(t => t.Group == "Group2" && t.Name == "Tag2");
    }

    [TestMethod]
    public void Remove_RemovesMultipleTags()
    {
        var current = new List<TagValue>
        {
            new("Group1", "Tag1"),
            new("Group2", "Tag2"),
            new("Group3", "Tag3")
        };
        var toRemove = new List<TagValue>
        {
            new("Group1", "Tag1"),
            new("Group3", "Tag3")
        };
        var options = new BulkModificationListTagProcessorOptions { Value = toRemove };

        var result = (List<TagValue>?)_processor.Process(current, (int)BulkModificationListTagProcessOperation.Remove, options);

        result.Should().HaveCount(1);
        result![0].Should().BeEquivalentTo(new TagValue("Group2", "Tag2"));
    }

    [TestMethod]
    public void Remove_NoMatch_ReturnsCurrent()
    {
        var current = new List<TagValue> { new("Group1", "Tag1") };
        var toRemove = new List<TagValue> { new("Group2", "Tag2") };
        var options = new BulkModificationListTagProcessorOptions { Value = toRemove };

        var result = _processor.Process(current, (int)BulkModificationListTagProcessOperation.Remove, options);

        result.Should().BeEquivalentTo(current);
    }

    [TestMethod]
    public void Remove_WithNullCurrent_ReturnsNull()
    {
        var toRemove = new List<TagValue> { new("Group", "Tag") };
        var options = new BulkModificationListTagProcessorOptions { Value = toRemove };
        var result = _processor.Process(null, (int)BulkModificationListTagProcessOperation.Remove, options);

        result.Should().BeNull();
    }

    [TestMethod]
    public void Remove_WithNullValue_ReturnsCurrent()
    {
        var current = new List<TagValue> { new("Group", "Tag") };
        var options = new BulkModificationListTagProcessorOptions { Value = null };
        var result = _processor.Process(current, (int)BulkModificationListTagProcessOperation.Remove, options);

        result.Should().BeEquivalentTo(current);
    }

    [TestMethod]
    public void Remove_WithEmptyValue_ReturnsCurrent()
    {
        var current = new List<TagValue> { new("Group", "Tag") };
        var options = new BulkModificationListTagProcessorOptions { Value = new List<TagValue>() };
        var result = _processor.Process(current, (int)BulkModificationListTagProcessOperation.Remove, options);

        result.Should().BeEquivalentTo(current);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Remove_MatchesByGroupAndName()
    {
        var current = new List<TagValue>
        {
            new("Group", "Tag1"),
            new("Group", "Tag2"),
            new("OtherGroup", "Tag1")
        };
        var toRemove = new List<TagValue> { new("Group", "Tag1") };
        var options = new BulkModificationListTagProcessorOptions { Value = toRemove };

        var result = (List<TagValue>?)_processor.Process(current, (int)BulkModificationListTagProcessOperation.Remove, options);

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Group == "Group" && t.Name == "Tag2");
        result.Should().Contain(t => t.Group == "OtherGroup" && t.Name == "Tag1");
    }

    [TestMethod]
    public void Append_PreservesDuplicates()
    {
        var current = new List<TagValue> { new("Group", "Tag") };
        var toAppend = new List<TagValue> { new("Group", "Tag") }; // Same tag
        var options = new BulkModificationListTagProcessorOptions { Value = toAppend };

        var result = (List<TagValue>?)_processor.Process(current, (int)BulkModificationListTagProcessOperation.Append, options);

        result.Should().HaveCount(2); // Duplicates are allowed
    }

    [TestMethod]
    public void Tags_WithNullGroup()
    {
        var current = new List<TagValue> { new(null, "Tag1") };
        var toAppend = new List<TagValue> { new(null, "Tag2") };
        var options = new BulkModificationListTagProcessorOptions { Value = toAppend };

        var result = (List<TagValue>?)_processor.Process(current, (int)BulkModificationListTagProcessOperation.Append, options);

        result.Should().HaveCount(2);
        result![0].Group.Should().BeNull();
        result[1].Group.Should().BeNull();
    }

    #endregion
}

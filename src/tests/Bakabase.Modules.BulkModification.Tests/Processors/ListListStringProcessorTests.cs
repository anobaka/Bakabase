using Bakabase.Modules.BulkModification.Components.Processors.ListListString;
using FluentAssertions;

namespace Bakabase.Modules.BulkModification.Tests.Processors;

[TestClass]
public class ListListStringProcessorTests
{
    private readonly BulkModificationListListStringProcessor _processor = new();

    #region Delete

    [TestMethod]
    public void Delete_WithValue_ReturnsNull()
    {
        var paths = new List<List<string>>
        {
            new() { "Category", "SubCategory", "Item" }
        };
        var result = _processor.Process(paths, (int)BulkModificationListListStringProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    [TestMethod]
    public void Delete_WithNull_ReturnsNull()
    {
        var result = _processor.Process(null, (int)BulkModificationListListStringProcessOperation.Delete, null);
        result.Should().BeNull();
    }

    #endregion

    #region SetWithFixedValue

    [TestMethod]
    public void SetWithFixedValue_ReturnsNewList()
    {
        var newPaths = new List<List<string>>
        {
            new() { "New", "Path1" },
            new() { "New", "Path2" }
        };
        var options = new BulkModificationListListStringProcessorOptions { Value = newPaths };
        var result = _processor.Process(
            new List<List<string>> { new() { "Old", "Path" } },
            (int)BulkModificationListListStringProcessOperation.SetWithFixedValue,
            options);

        result.Should().BeEquivalentTo(newPaths);
    }

    [TestMethod]
    public void SetWithFixedValue_WithNullCurrent_ReturnsNewList()
    {
        var newPaths = new List<List<string>> { new() { "New", "Path" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = newPaths };
        var result = _processor.Process(null, (int)BulkModificationListListStringProcessOperation.SetWithFixedValue, options);

        result.Should().BeEquivalentTo(newPaths);
    }

    #endregion

    #region Append

    [TestMethod]
    public void Append_AddsPathsToEnd()
    {
        var current = new List<List<string>> { new() { "Path1", "A" } };
        var toAppend = new List<List<string>> { new() { "Path2", "B" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toAppend };

        var result = (List<List<string>>?)_processor.Process(current, (int)BulkModificationListListStringProcessOperation.Append, options);

        result.Should().HaveCount(2);
        result![0].Should().BeEquivalentTo(new List<string> { "Path1", "A" });
        result[1].Should().BeEquivalentTo(new List<string> { "Path2", "B" });
    }

    [TestMethod]
    public void Append_WithNullCurrent_ReturnsValueList()
    {
        var toAppend = new List<List<string>> { new() { "Path", "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toAppend };
        var result = _processor.Process(null, (int)BulkModificationListListStringProcessOperation.Append, options);

        result.Should().BeEquivalentTo(toAppend);
    }

    [TestMethod]
    public void Append_WithNullValue_ReturnsCurrent()
    {
        var current = new List<List<string>> { new() { "Path", "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = null };
        var result = _processor.Process(current, (int)BulkModificationListListStringProcessOperation.Append, options);

        result.Should().BeEquivalentTo(current);
    }

    [TestMethod]
    public void Append_WithEmptyValue_ReturnsCurrent()
    {
        var current = new List<List<string>> { new() { "Path", "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = new List<List<string>>() };
        var result = _processor.Process(current, (int)BulkModificationListListStringProcessOperation.Append, options);

        result.Should().BeEquivalentTo(current);
    }

    [TestMethod]
    public void Append_MultipleComplexPaths()
    {
        var current = new List<List<string>>
        {
            new() { "Category1", "Sub1", "Item1" }
        };
        var toAppend = new List<List<string>>
        {
            new() { "Category2", "Sub2" },
            new() { "Category3", "Sub3", "Item3", "SubItem3" }
        };
        var options = new BulkModificationListListStringProcessorOptions { Value = toAppend };

        var result = (List<List<string>>?)_processor.Process(current, (int)BulkModificationListListStringProcessOperation.Append, options);

        result.Should().HaveCount(3);
        result![0].Should().HaveCount(3);
        result[1].Should().HaveCount(2);
        result[2].Should().HaveCount(4);
    }

    #endregion

    #region Prepend

    [TestMethod]
    public void Prepend_AddsPathsToStart()
    {
        var current = new List<List<string>> { new() { "Path2", "B" } };
        var toPrepend = new List<List<string>> { new() { "Path1", "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toPrepend };

        var result = (List<List<string>>?)_processor.Process(current, (int)BulkModificationListListStringProcessOperation.Prepend, options);

        result.Should().HaveCount(2);
        result![0].Should().BeEquivalentTo(new List<string> { "Path1", "A" });
        result[1].Should().BeEquivalentTo(new List<string> { "Path2", "B" });
    }

    [TestMethod]
    public void Prepend_WithNullCurrent_ReturnsValueList()
    {
        var toPrepend = new List<List<string>> { new() { "Path", "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toPrepend };
        var result = _processor.Process(null, (int)BulkModificationListListStringProcessOperation.Prepend, options);

        result.Should().BeEquivalentTo(toPrepend);
    }

    #endregion

    #region Remove

    [TestMethod]
    public void Remove_RemovesMatchingPaths()
    {
        var current = new List<List<string>>
        {
            new() { "Path1", "A" },
            new() { "Path2", "B" },
            new() { "Path3", "C" }
        };
        var toRemove = new List<List<string>> { new() { "Path2", "B" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toRemove };

        var result = (List<List<string>>?)_processor.Process(current, (int)BulkModificationListListStringProcessOperation.Remove, options);

        result.Should().HaveCount(2);
        result![0].Should().BeEquivalentTo(new List<string> { "Path1", "A" });
        result[1].Should().BeEquivalentTo(new List<string> { "Path3", "C" });
    }

    [TestMethod]
    public void Remove_RemovesMultiplePaths()
    {
        var current = new List<List<string>>
        {
            new() { "Path1", "A" },
            new() { "Path2", "B" },
            new() { "Path3", "C" }
        };
        var toRemove = new List<List<string>>
        {
            new() { "Path1", "A" },
            new() { "Path3", "C" }
        };
        var options = new BulkModificationListListStringProcessorOptions { Value = toRemove };

        var result = (List<List<string>>?)_processor.Process(current, (int)BulkModificationListListStringProcessOperation.Remove, options);

        result.Should().HaveCount(1);
        result![0].Should().BeEquivalentTo(new List<string> { "Path2", "B" });
    }

    [TestMethod]
    public void Remove_NoMatch_ReturnsCurrent()
    {
        var current = new List<List<string>> { new() { "Path1", "A" } };
        var toRemove = new List<List<string>> { new() { "Path2", "B" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toRemove };

        var result = _processor.Process(current, (int)BulkModificationListListStringProcessOperation.Remove, options);

        result.Should().BeEquivalentTo(current);
    }

    [TestMethod]
    public void Remove_PartialMatch_DoesNotRemove()
    {
        var current = new List<List<string>>
        {
            new() { "Path1", "A", "B" } // 3 elements
        };
        var toRemove = new List<List<string>>
        {
            new() { "Path1", "A" } // 2 elements - partial match
        };
        var options = new BulkModificationListListStringProcessorOptions { Value = toRemove };

        var result = _processor.Process(current, (int)BulkModificationListListStringProcessOperation.Remove, options);

        result.Should().BeEquivalentTo(current); // Not removed because not exact match
    }

    [TestMethod]
    public void Remove_WithNullCurrent_ReturnsNull()
    {
        var toRemove = new List<List<string>> { new() { "Path", "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toRemove };
        var result = _processor.Process(null, (int)BulkModificationListListStringProcessOperation.Remove, options);

        result.Should().BeNull();
    }

    [TestMethod]
    public void Remove_WithNullValue_ReturnsCurrent()
    {
        var current = new List<List<string>> { new() { "Path", "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = null };
        var result = _processor.Process(current, (int)BulkModificationListListStringProcessOperation.Remove, options);

        result.Should().BeEquivalentTo(current);
    }

    [TestMethod]
    public void Remove_WithEmptyValue_ReturnsCurrent()
    {
        var current = new List<List<string>> { new() { "Path", "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = new List<List<string>>() };
        var result = _processor.Process(current, (int)BulkModificationListListStringProcessOperation.Remove, options);

        result.Should().BeEquivalentTo(current);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Process_WithEmptyInnerPaths()
    {
        var current = new List<List<string>> { new() };
        var toAppend = new List<List<string>> { new() { "A" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toAppend };

        var result = (List<List<string>>?)_processor.Process(current, (int)BulkModificationListListStringProcessOperation.Append, options);

        result.Should().HaveCount(2);
        result![0].Should().BeEmpty();
        result[1].Should().BeEquivalentTo(new List<string> { "A" });
    }

    [TestMethod]
    public void Remove_ExactSequenceMatch()
    {
        var current = new List<List<string>>
        {
            new() { "A", "B", "C" },
            new() { "A", "B" },
            new() { "A", "C", "B" } // Same elements, different order
        };
        var toRemove = new List<List<string>> { new() { "A", "B", "C" } };
        var options = new BulkModificationListListStringProcessorOptions { Value = toRemove };

        var result = (List<List<string>>?)_processor.Process(current, (int)BulkModificationListListStringProcessOperation.Remove, options);

        result.Should().HaveCount(2);
        result![0].Should().BeEquivalentTo(new List<string> { "A", "B" });
        result[1].Should().BeEquivalentTo(new List<string> { "A", "C", "B" }); // Not removed - different order
    }

    [TestMethod]
    public void Append_PreservesDuplicates()
    {
        var current = new List<List<string>> { new() { "A", "B" } };
        var toAppend = new List<List<string>> { new() { "A", "B" } }; // Same path
        var options = new BulkModificationListListStringProcessorOptions { Value = toAppend };

        var result = (List<List<string>>?)_processor.Process(current, (int)BulkModificationListListStringProcessOperation.Append, options);

        result.Should().HaveCount(2); // Duplicates allowed
    }

    [TestMethod]
    public void Process_WithDeepNesting()
    {
        var current = new List<List<string>>
        {
            new() { "Level1", "Level2", "Level3", "Level4", "Level5" }
        };
        var options = new BulkModificationListListStringProcessorOptions { Value = current };

        var result = (List<List<string>>?)_processor.Process(
            null,
            (int)BulkModificationListListStringProcessOperation.SetWithFixedValue,
            options);

        result.Should().HaveCount(1);
        result![0].Should().HaveCount(5);
    }

    #endregion
}

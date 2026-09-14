using System;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed class WorkflowOutputPreviewTests
{
    [TestMethod]
    public void StructuredOutputAndEmptyRunsRemainValidJson()
    {
        var result = WorkflowOutputPreview.Capture([new {Name = "item", Links = new[] {"a", "b"}}]);
        using var json = JsonDocument.Parse(result.Json);
        Assert.AreEqual("item", json.RootElement[0].GetProperty("name").GetString());
        Assert.IsFalse(result.Truncated);
        Assert.AreEqual("[]", WorkflowOutputPreview.Capture([]).Json);
    }

    [TestMethod]
    public void PreviewStopsAtTwentyItems()
    {
        var result = WorkflowOutputPreview.Capture(Enumerable.Range(0, 21).Cast<object>());
        using var json = JsonDocument.Parse(result.Json);
        Assert.AreEqual(20, json.RootElement.GetArrayLength());
        Assert.IsTrue(result.Truncated);
    }

    [TestMethod]
    public void OversizedItemIsOmittedWithoutBreakingLaterItemsOrJson()
    {
        var result = WorkflowOutputPreview.Capture([new string('x', 65536), new {Name = "small"}]);
        using var json = JsonDocument.Parse(result.Json);
        Assert.IsTrue(result.Json.Length <= WorkflowOutputPreview.MaximumCharacters);
        Assert.AreEqual(1, json.RootElement.GetArrayLength());
        Assert.AreEqual("small", json.RootElement[0].GetProperty("name").GetString());
        Assert.IsTrue(result.Truncated);
    }

    [TestMethod]
    public void BudgetIncludesArrayPunctuationAndNeverCutsAString()
    {
        var result = WorkflowOutputPreview.Capture([new string('a', 32765), new string('b', 32765)]);
        using var json = JsonDocument.Parse(result.Json);
        Assert.IsTrue(result.Json.Length <= 65536);
        Assert.AreEqual(1, json.RootElement.GetArrayLength());
        Assert.IsTrue(result.Truncated);
    }

    [TestMethod]
    public void LegacyRunsDoNotInventAnOutputPreview()
    {
        var view = WorkflowRunViewModel.From(new WorkflowRunDbModel().ToDomainModel());
        Assert.IsNull(view.OutputItemsJson);
        Assert.IsFalse(view.OutputPreviewTruncated);
        var current = WorkflowRunViewModel.From(new WorkflowRunDbModel
            {OutputItemsJson = "[1]", OutputPreviewTruncated = true}.ToDomainModel());
        Assert.AreEqual("[1]", current.OutputItemsJson);
        Assert.IsTrue(current.OutputPreviewTruncated);
    }
}

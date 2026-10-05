using System;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
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
    public void LargeFileResultKeepsItsOutputDirectoryWhileItsExecutionSnapshotRemainsComplete()
    {
        var item = new AcquisitionWorkItem
        {
            ResourceId = 0, LeadKind = AcquisitionLeadKind.Manual, LeadValue = "manual-binding",
            Title = "Downloaded resource", WorkingDirectory = "/downloads/resource",
            ExtractedDirectory = "/data/file-processing/123/result",
            TargetDirectory = "/library/resource",
            Files = Enumerable.Range(0, 3000).Select(i => $"/data/file-processing/123/result/chapter-{i}/page-{i}.png").ToArray()
        };
        var snapshot = WorkflowItemSnapshot.Capture(item);
        Assert.IsTrue(snapshot.Length > WorkflowOutputPreview.MaximumCharacters);
        var result = WorkflowOutputPreview.Capture([item, new {Name = "following item"}]);
        using var json = JsonDocument.Parse(result.Json);
        Assert.IsTrue(result.Truncated);
        Assert.IsTrue(result.Json.Length <= WorkflowOutputPreview.MaximumCharacters);
        Assert.AreEqual(2, json.RootElement.GetArrayLength());
        var summary = json.RootElement[0];
        Assert.AreEqual(item.ExtractedDirectory, summary.GetProperty("extractedDirectory").GetString());
        Assert.AreEqual(item.WorkingDirectory, summary.GetProperty("workingDirectory").GetString());
        Assert.AreEqual(item.TargetDirectory, summary.GetProperty("targetDirectory").GetString());
        Assert.AreEqual(3000, summary.GetProperty("filesCount").GetInt32());
        Assert.IsFalse(summary.TryGetProperty("files", out _));
        Assert.AreEqual("following item", json.RootElement[1].GetProperty("name").GetString());
        var restored = (AcquisitionWorkItem)WorkflowItemSnapshot.Restore(snapshot);
        CollectionAssert.AreEqual(item.Files.ToArray(), restored.Files.ToArray());
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

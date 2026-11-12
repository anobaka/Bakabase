using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    [TestMethod]
    public async Task Unknown_processing_plan_creates_neither_operation_nor_workspace_snapshot()
    {
        var input = await Input();
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "unknown"}, Json);
        var result = await Execute(Intent(plan), input, plan);
        Assert.IsNull(result.Artifact); Assert.AreEqual("ExtractionPlanUnknown", result.WaitReason);
        Assert.AreEqual(0, await _db.Set<ArtifactOperationDbModel>().CountAsync(operation => operation.Kind == "files.process"));
        Assert.IsFalse(Directory.Exists(Path.Combine(input.RootDirectory, ".bakabase-work")));
        Assert.AreEqual(0, _archives.Extractions);
        Assert.AreEqual("single", await File.ReadAllTextAsync(input.Files.Single()));
    }
}

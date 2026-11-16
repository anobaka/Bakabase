using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [TestMethod]
    public async Task Committing_stage_files_preserves_unrelated_caller_builder_fields()
    {
        var (task, runId, source) = await StageCommitInput(false);
        var row = await Db.AcquisitionTasks.SingleAsync(t => t.Id == task.Id);
        var builder = JsonNode.Parse(row.BuilderStateJson ?? "{}")!.AsObject();
        builder["callerExtension"] = new JsonObject {["note"] = "preserve caller state", ["revision"] = 7};
        builder["stageDirectories"] = JsonSerializer.SerializeToNode(task.Plan!.StageDirectories, WorkflowJson.Options);
        row.BuilderStateJson = builder.ToJsonString();
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var binding = task.Plan.StageDirectories!.Parts["main"].Processing!;
        await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, runId, "main", binding, source, default);

        var saved = JsonNode.Parse((await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).BuilderStateJson!)!;
        Assert.IsTrue(JsonNode.DeepEquals(builder["callerExtension"], saved["callerExtension"]));
        Assert.IsTrue(JsonNode.DeepEquals(builder["stageDirectories"], saved["stageDirectories"]));
        Assert.IsNull(saved["processingStageCommitId"]);
    }
}

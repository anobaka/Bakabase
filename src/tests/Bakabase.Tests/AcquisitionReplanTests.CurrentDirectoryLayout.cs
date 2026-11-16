using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(null, FileExtractionLayouts.CurrentDirectory)]
    [DataRow(FileExtractionLayouts.ArchiveDirectory, FileExtractionLayouts.ArchiveDirectory)]
    public async Task Current_directory_revision_inherits_current_extraction_default_without_rewriting_old_execution(
        string? recordedLayout, string expectedLayout)
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var row = await Db.AcquisitionTasks.SingleAsync(t => t.Id == task.Id);
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
        row.PlanJson = JsonSerializer.Serialize(plan with {ExtractionLayout = recordedLayout}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        var history = run.PlanSnapshotJson;
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default);
        Assert.AreEqual(expectedLayout, revised.Plan!.ExtractionLayout);
        var config = AcquisitionPlanCompiler.Compile(revised.Plan).Nodes
            .Single(n => n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind).ConfigJson;
        Assert.AreEqual(expectedLayout, JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(config, WorkflowJson.Options)!.ExtractionLayout);
        Assert.AreEqual(history, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).PlanSnapshotJson);
        Assert.IsTrue(original.Files.All(File.Exists));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}

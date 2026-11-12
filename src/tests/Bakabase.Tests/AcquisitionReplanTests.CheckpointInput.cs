using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [TestMethod]
    public async Task Recorded_later_acquisition_boundary_uses_verified_private_input_after_original_disconnects()
    {
        var (task, old, download) = await Failed();
        var input = await ProcessedInput(old.Id, download);
        var request = new AcquisitionPlanReplanInput(task.Task.PlanRevision, old.Id, "Two explicit remaining instructions.",
            [new("main", new() {Requirement = "required", Steps =
                [new() {Id = "open", Op = "extractArchive", Selector = "*.r"},
                    new() {Id = "rename", Op = "renameFile", Input = "open", Selector = "final.txt", TargetName = "renamed.txt"}]},
                [input.Id], input.Id)]);
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var first = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), continued.Replacement!, default);
        Assert.IsNull(first.Suspension, first.Suspension?.PromptJson);
        File.Delete(input.Files.Single());
        var second = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(2), first.Replacement!, default);
        Assert.IsNull(second.Suspension, second.Suspension?.PromptJson);
        var output = ((DownloadWorkset)second.Replacement!).Targets.Single();
        Assert.AreEqual("renamed.txt", Path.GetFileName(output.ProcessedFiles!.Single()));
        Assert.AreEqual("remaining extraction", await File.ReadAllTextAsync(output.ProcessedFiles.Single()));
        Assert.AreEqual(1, _continuationArchives.Extractions);
        Assert.IsFalse(File.Exists(input.Files.Single()));
        Assert.IsFalse(await Artifacts.HasActiveConsumersAsync(input.OperationId));
        Assert.AreEqual(2, output.ProcessingStepArtifacts.Count);
    }
}

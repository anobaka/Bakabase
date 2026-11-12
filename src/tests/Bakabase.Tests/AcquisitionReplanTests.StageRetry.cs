using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stopped_delivery_retry_uses_only_the_reviewed_checkpoint_and_task_destination(bool terminalFailure)
    {
        var f = await PublicationStorageInput();
        if (terminalFailure)
        {
            f.Run.Status = WorkflowRunStatus.Failed;
            f.Run.WaitPromptJson = null;
            f.Run.WaitReason = null;
            var task = await Db.AcquisitionTasks.FindAsync(f.Task.Id);
            task!.Status = Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionStatus.Failed;
            await Db.SaveChangesAsync();
        }
        var details = (await Plans.GetAsync(f.Task.Id, default))!;
        CollectionAssert.AreEqual(new[] {f.Processed.Id}, details.ReplanArtifacts.Where(a => a.CanRetryDelivery == true)
            .Select(a => a.ArtifactId).ToArray());
        var work = Directory.CreateDirectory(Path.Combine(_root, "work")).FullName;
        var defaultRoot = Path.Combine(_root, "global-delivery");
        var taskRoot = Path.Combine(_root, "task-delivery");
        var plans = CurrentStoragePlans(work, defaultRoot);
        var input = ContinuePublication(f.Task, f.Run.Id, f.Processed.Id) with
            {RetryDeliveryOnly = true, DeliveryRootDirectory = taskRoot};
        var revised = await plans.ReplanAsync(f.Task.Id, input, default);
        var reloaded = (await plans.GetAsync(f.Task.Id, default))!;
        Assert.AreEqual(taskRoot, reloaded.PlanHistory.Last().Plan.DeliveryLayout!.RootDirectory);
        Assert.AreNotEqual(taskRoot, reloaded.PlanHistory.First().Plan.DeliveryLayout?.RootDirectory);
        Assert.AreEqual(f.Run.Id, reloaded.PlanHistory.Last().PreviousRunId);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        CollectionAssert.AreEqual(new[] {AcquisitionPlanContinueFilesActivity.ActivityKind, "fs.publishArtifacts"},
            compiled.Nodes.Select(node => node.Kind).ToArray());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => plans.ReplanAsync(f.Task.Id, input, default));
        var payload = AcquisitionPlanCompiler.Workset(f.Task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var next = new WorkflowRunDbModel {StartedAt = DateTime.UtcNow, Status = WorkflowRunStatus.Running,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = f.Task.Id.ToString()}]};
        Db.WorkflowRuns.Add(next);
        await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int index) => new() {RunId = next.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[index].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var originalProcessing = await Artifacts.GetOperationAsync(f.Processed.OperationId);
        Assert.AreEqual(originalProcessing!.IntentKey, ((DownloadWorkset)attached.Replacement!).Targets.Single().ProcessingIntentKey);
        var delivered = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(1), attached.Replacement!, default);
        Assert.IsNull(delivered.Suspension, delivered.Suspension?.PromptJson);
        var output = ((DownloadWorkset)delivered.Replacement!).Targets.Single();
        Assert.AreEqual(revised.Plan!.DeliveryLayout!.TaskDirectory, output.PublishedDirectory);
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(output.PublishedFiles!.Single()));
        Assert.IsTrue(File.Exists(f.Processed.Files.Single()));
        Assert.IsFalse(Directory.Exists(defaultRoot));
        Assert.AreEqual(0, _continuationArchives.Extractions);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Completed_delivery_retry_accepts_an_unchanged_projection_in_a_separate_directory()
    {
        var (task, run, _, published, _) = await CompletedInput(true);
        var layout = new ArtifactDeliveryPolicy();
        var preview = FileArtifactDeliveryLayout.Preview(published, layout);
        Assert.IsFalse(preview.Changed);
        var item = new AcquisitionPlanProcessingReplacement("main", new() {Requirement = "notRequired"},
            [published.Id], published.Id, preview.ProjectionFingerprint);
        var input = new AcquisitionPlanReplanInput(task.Task.PlanRevision, run.Id, "Repeat delivery", [item],
            DeliveryContentLayout: layout, RetryDeliveryOnly: true);
        var revised = await Plans.ReplanAsync(task.Id, input, default);
        Assert.AreEqual(2, revised.Steps.Count);
        Assert.IsNull((await RetirementState(task.Id)).PendingDeliveryRetirements);
        Assert.AreNotEqual(published.RootDirectory, revised.Plan!.DeliveryLayout!.TaskDirectory);
        Assert.IsTrue(File.Exists(published.Files.Single()));
    }

    [TestMethod]
    public async Task Delivery_retry_cannot_consume_a_processing_checkpoint_as_a_final_output()
    {
        var (task, run, download) = await Failed();
        var input = new AcquisitionPlanReplanInput(task.Task.PlanRevision, run.Id, "Repeat delivery",
            [new("main", new() {Requirement = "notRequired"}, [download.Id], download.Id)], RetryDeliveryOnly: true);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, input, default));
        Assert.AreEqual(run.Id, (await Plans.GetAsync(task.Id, default))!.Task.WorkflowRunId);
    }
}

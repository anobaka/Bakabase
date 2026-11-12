using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Projected_completed_publication_reaches_requested_adaptive_assessment_without_source_bytes(bool cleaned)
    {
        var (task, previous, _, _, download) = await CompletedInput(true);
        var directory = Directory.CreateDirectory(Path.Combine(_root, "projected-input")).FullName;
        var wrapper = Directory.CreateDirectory(Path.Combine(directory, "wrapper")).FullName;
        var file = Path.Combine(wrapper, "inner.zip");
        await File.WriteAllTextAsync(file, "remaining archive");
        var source = await Artifacts.AdoptAsync(new("projected-input", "projected-input", "files.process", download.Fingerprint),
            new(directory, [file], "processedFiles", true, [download.Id], JsonSerializer.Serialize(
                new FileArtifactProcessingMetadata(download.Id, JsonSerializer.Serialize(new FileProcessingPlan
                    {Requirement = "required", Steps = [new() {Id = "s1", Op = "extractArchive"}]}, WorkflowJson.Options), null),
                WorkflowJson.Options)), "test");
        var publication = (await _services.GetRequiredService<FileArtifactPublisher>().PublishAsync("projected-input", source,
            Path.Combine(_root, "projected-delivery"), "test", deliveryPolicy: new() {CollapseSingleRootDirectory = true})).Artifact!;
        Assert.AreNotEqual(source.Fingerprint, publication.Fingerprint);
        // Replace the fixture's synthetic identity publication with the real projected receipt.
        await Db.Set<ArtifactOperationBindingDbModel>().Where(b => b.OwnerKind == "workflow" &&
            b.OwnerId == previous.Id.ToString() && b.Slot == "main:artifact.publish").ExecuteDeleteAsync();
        Assert.IsTrue(await new ArtifactOperationBindingService(Db).BindAsync("workflow", previous.Id.ToString(),
            "main:artifact.publish", publication.OperationId));
        if (cleaned) File.Delete(file);
        task = (await Plans.GetAsync(task.Id, default))!;
        Assert.IsTrue(task.ReplanArtifacts.Any(a => a.ArtifactId == publication.Id));
        var request = ContinueInput(task, previous.Id, publication.Id);
        request = request with {EnableAdaptiveAssessment = true, Items = [request.Items[0] with
            {Plan = new() {Requirement = "unknown"}, UseAdaptiveAssessment = true}]};
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int index, IServiceProvider? services = null) => new()
            {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = payload,
                ActivityConfigJson = compiled.Nodes[index].ConfigJson, Services = services ?? _services, Logger = NullLogger.Instance};
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var prepared = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), attached.Replacement!, default);
        Assert.IsNull(prepared.Suspension, prepared.Suspension?.PromptJson);
        Assert.AreEqual(publication.Id, ((DownloadWorkset)prepared.Replacement!).Targets.Single().ProcessedArtifactId);
        using var offline = new ServiceCollection().AddSingleton(Artifacts).BuildServiceProvider();
        var assessment = await new AssessFileArtifactDeliveryActivity().ProcessItemAsync(Context(2, offline), prepared.Replacement!, default);
        using var prompt = JsonDocument.Parse(assessment.Suspension!.PromptJson!);
        Assert.AreEqual(publication.Id, prompt.RootElement.GetProperty("artifactId").GetString());
        Assert.IsTrue(prompt.RootElement.GetProperty("adaptiveAssessment").GetBoolean(),
            "The requested adaptive route must reach its decision boundary, not reject a valid projected receipt.");
        Assert.AreEqual(0, _continuationArchives.Extractions); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.IsTrue(publication.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Explicit_processing_choice_reuses_processed_files_and_only_waits_for_a_requested_decision(bool askModel)
    {
        var (task, previous, input, publication, _) = await CompletedInput(true);
        var request = ContinueInput(task, previous.Id, input.Id);
        // The overall setting is on even for the keep part (as in a mixed-part retry).
        request = request with {EnableAdaptiveAssessment = true, Items = [request.Items[0] with
            {Plan = new() {Requirement = askModel ? "unknown" : "notRequired"}, UseAdaptiveAssessment = askModel}]};
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        var plan = (await Plans.GetAsync(task.Id, default))!.Plan!;
        Assert.AreEqual(askModel, plan.Parts.Single().UseAdaptiveAssessment);
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        Assert.AreEqual(askModel, compiled.Nodes.Any(n => n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind));
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, plan, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.Add(run);
        await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int index, IServiceProvider? services = null) => new()
            {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = payload,
                ActivityConfigJson = compiled.Nodes[index].ConfigJson, Services = services ?? _services, Logger = NullLogger.Instance};
        var before = await Db.Set<ArtifactOperationDbModel>().CountAsync();
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var prepared = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), attached.Replacement!, default);
        Assert.IsNull(prepared.Suspension, prepared.Suspension?.PromptJson);
        var target = ((DownloadWorkset)prepared.Replacement!).Targets.Single();
        Assert.AreEqual(input.Id, target.ProcessedArtifactId);
        Assert.AreEqual(before, await Db.Set<ArtifactOperationDbModel>().CountAsync(), "No intermediate copy is required.");
        if (askModel)
        {
            using var offline = new ServiceCollection().AddSingleton(Artifacts).BuildServiceProvider();
            var assessment = await new AssessFileArtifactDeliveryActivity().ProcessItemAsync(Context(2, offline), prepared.Replacement!, default);
            using var prompt = JsonDocument.Parse(assessment.Suspension!.PromptJson!);
            Assert.AreEqual(FileArtifactDeliveryAssessment.ResultUncertain, prompt.RootElement.GetProperty("reason").GetString());
            Assert.AreEqual(input.Id, prompt.RootElement.GetProperty("artifactId").GetString());
            Assert.IsTrue(prompt.RootElement.GetProperty("adaptiveAssessment").GetBoolean());
        }
        else
        {
            var delivered = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(2), prepared.Replacement!, default);
            Assert.IsNull(delivered.Suspension, delivered.Suspension?.PromptJson);
            var file = ((DownloadWorkset)delivered.Replacement!).Targets.Single().PublishedFiles.Single();
            Assert.AreEqual(await File.ReadAllTextAsync(input.Files.Single()), await File.ReadAllTextAsync(file));
            Assert.IsNull((await RetirementState(task.Id)).PendingDeliveryRetirements);
        }
        Assert.IsTrue(File.Exists(publication.Files.Single()));
        Assert.AreEqual(0, _continuationArchives.Extractions);
    }
}

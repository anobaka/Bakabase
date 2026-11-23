using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
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
    [DataRow("missingStage")]
    [DataRow("emptyStage")]
    [DataRow("confirmedInstructions")]
    [DataRow("populatedStage")]
    [DataRow("populatedOwnedStage")]
    [DataRow("existingReceipt")]
    [DataRow("modifiedDownload")]
    [DataRow("newStageFiles")]
    public async Task Registered_download_waits_for_instructions_before_creating_a_processing_copy(string scenario)
    {
        var (task, run, download) = await InitialUnknownInput();
        var stage = Path.Combine(_root, "review-stage");
        if (scenario != "missingStage") Directory.CreateDirectory(stage);
        task = await BindCurrentStage(task, "processing", stage, taskAllocation: scenario != "populatedStage");
        var binding = task.Plan!.StageDirectories!.Parts["main"];
        var payload = (DownloadWorkset)WorkflowItemSnapshot.Restore(run.CurrentItemJson!);
        payload = payload with {Targets = [payload.Targets.Single() with
            {StageDirectories = binding, ProcessingWaitReason = null, ProcessingDirectoryScope = binding.Processing!.Allocation}]};
        var manual = Path.Combine(stage, "manual.txt");
        var locations = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var manualInput = scenario is "populatedStage" or "populatedOwnedStage";
        if (scenario == "populatedOwnedStage") await locations.EnsureScopedDirectoryAsync(binding.Processing!.Allocation!, default);
        if (manualInput) await File.WriteAllTextAsync(manual, "user prepared");
        ArtifactRecord? existing = null;
        if (scenario == "existingReceipt")
            existing = await AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts, new CompositeStorage(), locations,
                task.Id, run.Id, "main", binding.Processing!, download, new StageInputMaterializer(),
                _services.GetRequiredService<IServiceScopeFactory>(), default);
        var config = WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes[run.CurrentStepIndex!.Value].ConfigJson;
        run.Status = WorkflowRunStatus.Running; run.PendingSignalJson = null; await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(string? prompt = null) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = config, ResumePromptJson = prompt,
            Services = _services, Logger = NullLogger.Instance};
        var operations = await Db.ArtifactOperations.CountAsync();
        var activity = new AcquisitionPlanProcessingActivity();
        var waiting = await activity.ProcessItemAsync(Context(), payload, default);
        Assert.IsNotNull(waiting.Suspension);
        using var prompt = JsonDocument.Parse(waiting.Suspension.PromptJson!);
        Assert.AreEqual("cloud.fileProcessing", waiting.Suspension.Reason);
        Assert.AreEqual("ExtractionPlanUnknown", prompt.RootElement.GetProperty("reason").GetString());
        Assert.IsTrue(prompt.RootElement.GetProperty("canEditPlan").GetBoolean());
        var observed = (await Artifacts.GetArtifactAsync(prompt.RootElement.GetProperty("artifactId").GetString()!))!;
        Assert.AreEqual(observed.Fingerprint, prompt.RootElement.GetProperty("fingerprint").GetString());
        if (manualInput)
        {
            Assert.AreEqual("currentFiles", observed.Kind);
            CollectionAssert.AreEqual(new[] {manual}, observed.Files.ToArray());
        }
        else
        {
            Assert.AreEqual(existing?.Id ?? download.Id, observed.Id);
            Assert.AreEqual(operations, await Db.ArtifactOperations.CountAsync());
            if (existing == null)
                Assert.IsNull(await AcquisitionProcessingStageInput.ReadPreparedAsync(Db, Artifacts, locations, task.Id, run.Id,
                    payload.Targets.Single(), download, default));
        }
        if (scenario == "missingStage") Assert.IsFalse(Directory.Exists(stage));
        if (existing == null) Assert.IsFalse(File.Exists(Path.Combine(stage, "original.bin")));
        Assert.IsTrue(await Artifacts.HasActiveConsumersAsync(download.OperationId));
        var answer = scenario == "confirmedInstructions" ? new CloudFileProcessingSignal {TargetId = "main", ExtractionPlanJson =
            JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps = [
                new() {Id = "rename", Op = "renameFile", Selector = "original.bin", TargetName = "confirmed.bin"}]}, WorkflowJson.Options)} :
            new CloudFileProcessingSignal {TargetId = "main", ArchivePolicy = "keep"};
        Task<WorkflowItemOutcome> Resume() => activity.ResumeAsync(Context(waiting.Suspension.PromptJson),
            waiting.Suspension.Item, JsonSerializer.Serialize(answer, WorkflowJson.Options), default);
        if (scenario == "newStageFiles")
        {
            await File.WriteAllTextAsync(manual, "new input");
            var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(Resume);
            Assert.AreEqual("STAGE_COMMIT_TARGET_CONFLICT", error.Message);
            Assert.AreEqual("new input", await File.ReadAllTextAsync(manual));
            Assert.AreEqual(operations, await Db.ArtifactOperations.CountAsync()); return;
        }
        if (scenario == "modifiedDownload")
        {
            await File.WriteAllTextAsync(download.Files.Single(), "changed original bytes");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(Resume);
            Assert.AreEqual(operations, await Db.ArtifactOperations.CountAsync()); return;
        }
        var completed = await Resume();
        Assert.IsNull(completed.Suspension, completed.Suspension?.PromptJson);
        var output = ((DownloadWorkset)completed.Replacement!).Targets.Single();
        Assert.AreEqual(stage, output.ProcessedDirectory);
        Assert.AreEqual(manualInput ? "user prepared" : "original bytes",
            await File.ReadAllTextAsync(output.ProcessedFiles.Single()));
        if (scenario == "confirmedInstructions") Assert.AreEqual("confirmed.bin", Path.GetFileName(output.ProcessedFiles.Single()));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(download.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Completed_files_can_wait_for_manual_plan_without_payload_work_and_reuse_the_confirmation_form(bool keep)
    {
        var (task, old, input, published, _) = await CompletedInput();
        var oldSnapshot = old.PlanSnapshotJson; var oldOutput = old.OutputItemsJson;
        var request = ContinueInput(task, old.Id, input.Id);
        request = request with {Items = [request.Items[0] with {Plan = new() {Requirement = "unknown"}}]};
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        Assert.AreEqual(AcquisitionStatus.Waiting, revised.Task.Status);
        Assert.IsNull(revised.Task.WorkflowRunId);
        Assert.AreEqual(2, revised.Task.PlanRevision);
        Assert.AreEqual("unknown", revised.Plan!.Parts.Single().Processing!.Requirement);
        Assert.AreEqual(input.Id, revised.Plan.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.AreEqual(task.Plan!.Parts.Single().StoragePolicy?.SourceCleanup, revised.Plan.Parts.Single().StoragePolicy?.SourceCleanup);
        Assert.AreEqual(old.Id, revised.PlanHistory.Last().PreviousRunId);
        Assert.IsFalse(Directory.Exists(revised.Plan.DeliveryLayout!.TaskDirectory));
        var preflight = await Plans.PreflightAsync(task.Id, default);
        Assert.IsFalse(preflight.Issues.Any(issue => issue.Blocking), "An explicit unknown local plan must reach its confirmation node.");
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan);
        CollectionAssert.AreEqual(new[] {AcquisitionPlanContinueFilesActivity.ActivityKind,
            AcquisitionPlanProcessingActivity.ActivityKind, AssessFileArtifactDeliveryActivity.ActivityKind,
            "fs.publishArtifacts"}, compiled.Nodes.Select(n => n.Kind).ToArray());
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var beforeOperations = await Db.Set<ArtifactOperationDbModel>().CountAsync();
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var activity = new AcquisitionPlanProcessingActivity();
        var waiting = await activity.ProcessItemAsync(Context(1), continued.Replacement!, default);
        Assert.IsNotNull(waiting.Suspension);
        Assert.AreEqual("cloud.fileProcessing", waiting.Suspension.Reason);
        using (var prompt = JsonDocument.Parse(waiting.Suspension.PromptJson!))
        {
            Assert.AreEqual("ExtractionPlanUnknown", prompt.RootElement.GetProperty("reason").GetString());
            Assert.IsTrue(prompt.RootElement.GetProperty("canEditPlan").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, prompt.RootElement.GetProperty("knownStepId").ValueKind);
            CollectionAssert.AreEquivalent(new[] {"instructions", "keep"},
                prompt.RootElement.GetProperty("allowedArchivePolicies").EnumerateArray().Select(x => x.GetString()).ToArray());
        }
        Assert.AreEqual(beforeOperations, await Db.Set<ArtifactOperationDbModel>().CountAsync(), "Waiting must not claim a processing operation.");
        Assert.AreEqual(0, _continuationArchives.Extractions);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.IsFalse(Directory.EnumerateDirectories(_root, ".bakabase-work", SearchOption.AllDirectories).Any(), "Unconfirmed input must not be copied.");
        _continuationArchives.OutputFileName = "unclassified.payload";
        var answer = keep ? new CloudFileProcessingSignal {TargetId = "main", ArchivePolicy = "keep"} :
            new CloudFileProcessingSignal {TargetId = "main", ExtractionPlanJson =
                JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps =
                    [new() {Id = "open", Op = "extractArchive", Selector = "*.r"}]}, WorkflowJson.Options)};
        var processed = await activity.ResumeAsync(Context(1), waiting.Suspension.Item,
            JsonSerializer.Serialize(answer, WorkflowJson.Options), default);
        Assert.IsNull(processed.Suspension, processed.Suspension?.PromptJson);
        var assessed = await new AssessFileArtifactDeliveryActivity().ProcessItemAsync(Context(2), processed.Replacement!, default);
        if (keep)
        {
            Assert.IsNull(assessed.Suspension, assessed.Suspension?.PromptJson);
            Assert.AreEqual(0, _continuationArchives.Extractions);
            var output = ((DownloadWorkset)processed.Replacement!).Targets.Single();
            Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(output.ProcessedFiles!.Single()));
        }
        else
        {
            // Fixture extraction yields an unclassified payload. The new dynamic answer must
            // reach assessment and wait for a human, rather than bypassing it and publishing.
            Assert.AreEqual(1, _continuationArchives.Extractions);
            Assert.IsNotNull(assessed.Suspension);
            StringAssert.Contains(assessed.Suspension.PromptJson!, FileArtifactDeliveryAssessment.ResultUncertain);
        }
        Assert.IsFalse(Directory.Exists(revised.Plan.DeliveryLayout.TaskDirectory));
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(published.Files.Single()));
        Assert.AreEqual(WorkflowRunStatus.Success, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == old.Id)).Status);
        Assert.AreEqual(oldSnapshot, old.PlanSnapshotJson); Assert.AreEqual(oldOutput, old.OutputItemsJson);
    }
}

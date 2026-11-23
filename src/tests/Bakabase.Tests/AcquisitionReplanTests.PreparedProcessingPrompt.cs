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
    private async Task<(WorkflowRunDbModel Run, ArtifactRecord Input, DownloadWorkset Workset,
        Func<string?, WorkflowExecutionContext> Context)> ConnectedManualProcessingInput()
    {
        var (task, previous, input) = await DownloadedInput();
        var request = ContinueDownload(task, previous.Id, input.Id);
        var ready = await Plans.ReplanAsync(task.Id, request with {Items = [request.Items.Single() with
            {Plan = new() {Requirement = "unknown"}, UseAdaptiveAssessment = false}]}, default);
        var plan = AcquisitionPlanCompiler.Compile(ready.Plan!);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, ready.Task.PlanRevision, ready.Plan!, ready.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            CurrentStepIndex = 0, PlanSnapshotJson = WorkflowRunPlans.Serialize(plan),
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(),
                Revision = ready.Task.PlanRevision.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(t => t.WorkflowRunId, run.Id).SetProperty(t => t.Status, AcquisitionStatus.Running));
        WorkflowExecutionContext Context(int node, string? prompt = null) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = plan.Nodes[node].ConfigJson,
            ResumePromptJson = prompt, Services = _services, Logger = NullLogger.Instance};
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), workset, default);
        var connected = (DownloadWorkset)attached.Replacement!;
        run.CurrentStepIndex = 1; run.CurrentItemJson = WorkflowItemSnapshot.Capture(connected);
        await Db.SaveChangesAsync();
        return (run, input, connected, prompt => Context(1, prompt));
    }

    [DataTestMethod]
    [DataRow("confirmed")]
    [DataRow("changed-in-place")]
    [DataRow("wrong-part-pin")]
    [DataRow("released-pin")]
    [DataRow("missing-slot")]
    [DataRow("stopped-run")]
    [DataRow("owner-revised")]
    [DataRow("owner-deleted")]
    [DataRow("stage-bound")]
    [DataRow("existing-step")]
    [DataRow("missing")]
    [DataRow("truncated")]
    [DataRow("symbolic-link")]
    [DataRow("wrong-digest")]
    public async Task Prepared_manual_prompt_reads_content_before_execution_and_preserves_existing_guards(string scenario)
    {
        var (run, registered, workset, context) = await ConnectedManualProcessingInput();
        var target = workset.Targets.Single();
        var storeCount = await Db.ArtifactOperations.CountAsync();
        var source = registered.Files.Single();
        var original = await File.ReadAllBytesAsync(source);
        Assert.IsNull(target.StageDirectories?.Processing?.BindingId);
        if (scenario == "missing") File.Delete(source);
        else if (scenario == "truncated") await File.WriteAllBytesAsync(source, original[..^1]);
        else if (scenario == "symbolic-link")
        {
            var alternate = Path.Combine(_root, "alternate-input");
            File.Move(source, alternate); File.CreateSymbolicLink(source, alternate);
        }
        else if (scenario is not ("confirmed" or "wrong-digest"))
        {
            var modified = File.GetLastWriteTimeUtc(source);
            var changed = original.ToArray(); changed[0] ^= 1;
            await File.WriteAllBytesAsync(source, changed);
            File.SetLastWriteTimeUtc(source, modified);
        }
        var pin = await Db.ArtifactConsumers.SingleAsync(c => c.OperationId == registered.OperationId &&
            c.ConsumerKey == AcquisitionPlanArtifactInputGuard.Consumer(run.Id, target.Id));
        if (scenario == "wrong-part-pin") pin.ConsumerKey = AcquisitionPlanArtifactInputGuard.Consumer(run.Id, "other");
        if (scenario == "released-pin") pin.ReleasedAt = DateTime.UtcNow;
        if (scenario == "stopped-run") run.Status = WorkflowRunStatus.Failed;
        if (scenario is "owner-revised" or "owner-deleted")
        {
            var owner = await Db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
            if (scenario == "owner-deleted") owner.DeletedAt = DateTime.UtcNow;
            else owner.WorkflowRunId = await Db.WorkflowRuns.Where(r => r.Id != run.Id).Select(r => r.Id).SingleAsync();
        }
        if (scenario == "missing-slot")
            await Db.ArtifactOperationBindings.Where(b => b.OwnerId == run.Id.ToString() &&
                b.Slot == target.Id + ":artifact.continuation").ExecuteDeleteAsync();
        if (scenario == "stage-bound")
        {
            var stage = new Bakabase.Modules.Acquisition.Abstractions.Models.Domain.AcquisitionStageDirectory(
                Path.Combine(_root, "existing-processing"), "exclusive", "taskAllocation") {BindingId = "existing-binding"};
            target = target with {StageDirectories = new() {Processing = stage}};
        }
        if (scenario == "existing-step") target = target with {ProcessingStepArtifacts = new() {["s1"] = registered.Id}};
        await Db.SaveChangesAsync();
        workset = workset with {Targets = [target]};
        var activity = new AcquisitionPlanProcessingActivity();
        if (scenario == "wrong-digest")
        {
            var cfg = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(context(null).ActivityConfigJson, WorkflowJson.Options)!;
            var persisted = context(null).ForPlannedActivity(JsonSerializer.Serialize(cfg with
                {ContinueFrom = cfg.ContinueFrom! with {Fingerprint = new string('0', 64)}}, WorkflowJson.Options), []);
            var error = await Assert.ThrowsAsync<Exception>(() => activity.ProcessItemAsync(persisted, workset, default));
            Assert.IsTrue(error is ArgumentException or InvalidOperationException);
            StringAssert.Contains(error.Message, "PLAN_REPLAN_EVIDENCE_");
            return;
        }
        Task<WorkflowItemOutcome> Read() => activity.ProcessItemAsync(context(null), workset, default);
        if (scenario is not ("confirmed" or "changed-in-place"))
        {
            var error = await Assert.ThrowsAsync<Exception>(Read);
            Assert.IsTrue(error is ArgumentException or InvalidOperationException or InvalidDataException or IOException, error.ToString());
            Assert.AreEqual(storeCount, await Db.ArtifactOperations.CountAsync());
            return;
        }
        var wait = await Read();
        Assert.IsNotNull(wait.Suspension);
        using var json = JsonDocument.Parse(wait.Suspension.PromptJson!);
        Assert.AreEqual("ExtractionPlanUnknown", json.RootElement.GetProperty("reason").GetString());
        Assert.AreEqual(target.Id, json.RootElement.GetProperty("targetId").GetString());
        Assert.AreEqual(registered.Id, json.RootElement.GetProperty("artifactId").GetString());
        Assert.AreEqual(storeCount, await Db.ArtifactOperations.CountAsync());
        var answer = JsonSerializer.Serialize(new CloudFileProcessingSignal {TargetId = target.Id, ArchivePolicy = "keep"}, WorkflowJson.Options);
        Task<WorkflowItemOutcome> Confirm() => activity.ResumeAsync(context(wait.Suspension.PromptJson), wait.Suspension.Item, answer, default);
        if (scenario == "changed-in-place")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(Confirm);
            Assert.AreEqual(storeCount, await Db.ArtifactOperations.CountAsync());
            return;
        }
        var final = await Confirm();
        Assert.IsNull(final.Suspension, final.Suspension?.PromptJson);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Prepared_manual_prompt_reuses_completed_processing_before_considering_the_source()
    {
        var (_, registered, workset, context) = await ConnectedManualProcessingInput();
        var target = workset.Targets.Single();
        var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(context(null).ActivityConfigJson, WorkflowJson.Options)!;
        var intent = AcquisitionPlanProcessingActivity.Intent(workset, target, config.Plan, null,
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), config.ContinueFrom);
        var finished = Directory.CreateDirectory(Path.Combine(_root, "cached-output")).FullName;
        var file = Path.Combine(finished, "result.jpg"); await File.WriteAllTextAsync(file, "verified processed result");
        var cached = await Artifacts.AdoptAsync(new("prepared-prompt-completed", intent, "files.process", registered.Fingerprint),
            new(finished, [file], "processedFiles", true, [registered.Id]), "fixture");
        File.Delete(registered.Files.Single());
        var outcome = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(context(null), workset, default);
        Assert.IsNull(outcome.Suspension);
        var result = ((DownloadWorkset)outcome.Replacement!).Targets.Single();
        Assert.AreEqual(cached.Id, result.ProcessedArtifactId);
        Assert.AreEqual("reused", result.ExecutionStatus);
        Assert.AreEqual("verified processed result", await File.ReadAllTextAsync(result.ProcessedFiles.Single()));
    }
}

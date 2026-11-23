using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [TestMethod]
    public async Task Prepared_local_parts_defer_separately_with_their_verified_inputs_and_destinations()
    {
        var f = await LocalFilesFixture(dependencyMissing: true);
        var oldRun = await _db.WorkflowRuns.SingleAsync();
        var task = await _db.AcquisitionTasks.SingleAsync();
        var workset = (DownloadWorkset)WorkflowItemSnapshot.Restore(oldRun.CurrentItemJson!);
        var first = workset.Targets.Single();
        var secondSource = first.SelectedSource! with {Id = "second-source", Url = "https://pan.baidu.com/s/second"};
        var second = first with {Id = "second", SelectedSourceId = secondSource.Id, Sources = [secondSource]};
        var (secondDownload, _) = await RecordFixtureDownloadPart(oldRun.Id, second, workset.OutputDirectory, "second");
        oldRun.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [first, second with {DownloadTaskId = secondDownload.Id}]});
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, WorkflowJson.Options)!;
        var part = plan.Parts.Single();
        plan = plan with {Parts = [part, part with {Id = second.Id, SelectedSourceId = secondSource.Id, Sources = [part.Sources.Single() with {Id = secondSource.Id, Url = secondSource.Url}]}]};
        task.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        task.PlanHistoryJson = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, plan, [], DateTime.UtcNow)}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        Assert.IsTrue(preview.Rows.Single().CanContinue, preview.Rows.Single().ReasonCode);
        await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], PrepareOnly: true));
        var ready = (await f.Plans.GetAsync(f.TaskId, default))!;
        var deferred = await f.Plans.DeferPreparedProcessingAsync(f.TaskId, ready.Task.PlanRevision, default);
        Assert.IsTrue(deferred.CanDeferPreparedProcessing);
        Assert.IsNull(deferred.Task.WorkflowRunId);
        Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual("processing.deferPreparedFiles", deferred.PlanHistory.Last().Reason);
        var inputs = deferred.Plan!.Parts.Select(p => p.ContinueFrom).ToArray();
        CollectionAssert.AreEqual(ready.Plan!.Parts.Select(p => p.ContinueFrom).ToArray(), inputs);
        Assert.AreEqual(2, inputs.Select(p => p!.ArtifactId).Distinct().Count());
        Assert.AreEqual(JsonSerializer.Serialize(ready.StageDirectories), JsonSerializer.Serialize(deferred.StageDirectories));
        Assert.AreEqual(JsonSerializer.Serialize(ready.Plan.StoragePolicy), JsonSerializer.Serialize(deferred.Plan.StoragePolicy));
        Assert.AreEqual(JsonSerializer.Serialize(ready.PlanHistory), JsonSerializer.Serialize(deferred.PlanHistory.SkipLast(1)));
        Assert.IsTrue(ready.Plan.Parts.All(p => (int?)p.Processing!.Steps.Single().ZipPasswordCodePage == 936));

        var replay = await f.Plans.DeferPreparedProcessingAsync(f.TaskId, ready.Task.PlanRevision, default);
        Assert.AreEqual(deferred.Task.PlanRevision, replay.Task.PlanRevision);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Plans.KeepPreparedFilesAsync(f.TaskId, new(ready.Task.PlanRevision), default));
        var running = await f.Plans.RunAsync(f.TaskId, replay.Task.PlanRevision, default);
        Assert.AreEqual(1, f.Host.Calls);
        Assert.IsNull(running.CanDeferPreparedProcessing);
        var run = await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == running.Task.WorkflowRunId);
        var nodes = WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes;
        CollectionAssert.AreEqual(new[] {"acquisition.plan.continueFiles", "acquisition.plan.process", "acquisition.plan.process",
            "fs.assessDelivery", "fs.assessDelivery", "fs.publishArtifacts"}, nodes.Select(n => n.Kind).ToArray());
        for (var index = 0; index < inputs.Length; index++)
        {
            var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(nodes[index + 1].ConfigJson!, WorkflowJson.Options)!;
            Assert.AreEqual(deferred.Plan.Parts[index].Id, config.TargetId);
            Assert.AreEqual(inputs[index], config.ContinueFrom);
            Assert.AreEqual("unknown", config.Plan.Requirement);
            Assert.AreEqual(0, config.Plan.Steps.Count);
            Assert.IsNull(config.StopAfterStepId);
            Assert.IsFalse(config.UseVerifiedInput || config.ExplicitlyKeepFiles);
        }
        var payload = AcquisitionPlanCompiler.Workset(f.TaskId, deferred.Task.PlanRevision, deferred.Plan, deferred.SourceRefs);
        WorkflowExecutionContext Context(int node, string? prompt = null) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, Services = _services, ActivityConfigJson = nodes[node].ConfigJson,
            ResumePromptJson = prompt, Logger = NullLogger.Instance};
        var connected = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var activity = new AcquisitionPlanProcessingActivity();
        var firstWait = await activity.ProcessItemAsync(Context(1), connected.Replacement!, default);
        Assert.IsNotNull(firstWait.Suspension);
        StringAssert.Contains(firstWait.Suspension.PromptJson!, "ExtractionPlanUnknown");
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(f.Options.Value.ProcessingWorkspaceDirectory!).Any());
        var instructions = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "first", Op = "renameExtension", Extension = ".dat", Selector = "official.bin"},
            new() {Id = "second", Op = "renameExtension", Input = "first", Extension = ".jpg", Selector = "*.dat"}
        ]};
        var answer = new CloudFileProcessingSignal {TargetId = first.Id,
            ExtractionPlanJson = JsonSerializer.Serialize(instructions, WorkflowJson.Options)};
        var finished = await activity.ResumeAsync(Context(1, firstWait.Suspension.PromptJson), firstWait.Suspension.Item,
            JsonSerializer.Serialize(answer, WorkflowJson.Options), default);
        Assert.IsNull(finished.Suspension, finished.Suspension?.PromptJson);
        var firstResult = ((DownloadWorkset)finished.Replacement!).Targets.Single(t => t.Id == first.Id);
        Assert.AreEqual("official.jpg", Path.GetFileName(firstResult.ProcessedFiles.Single()));
        Assert.AreEqual("verified existing bytes", await File.ReadAllTextAsync(firstResult.ProcessedFiles.Single()));
        var secondWait = await activity.ProcessItemAsync(Context(2), finished.Replacement!, default);
        Assert.IsNotNull(secondWait.Suspension);
        using var nextPrompt = JsonDocument.Parse(secondWait.Suspension.PromptJson!);
        Assert.AreEqual("ExtractionPlanUnknown", nextPrompt.RootElement.GetProperty("reason").GetString());
        Assert.AreEqual(second.Id, nextPrompt.RootElement.GetProperty("targetId").GetString());
        Assert.AreEqual(inputs[1]!.ArtifactId, nextPrompt.RootElement.GetProperty("artifactId").GetString());
        Assert.IsTrue((await _db.DownloadResults.AsNoTracking().ToListAsync())
            .All(r => JsonSerializer.Deserialize<string[]>(r.FilesJson)!.All(File.Exists)));
    }

    [DataTestMethod]
    [DataRow("Accepted the nine available original archives; the tenth is missing.")]
    [DataRow(null)]
    public async Task LocalFiles_keep_prepared_saves_only_the_task_scope_and_runs_only_after_confirmation(string? note)
    {
        var f = await LocalFilesFixture(dependencyMissing: true);
        var first = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
        await f.Recovery.ApplyAsync(new(first.PreviewToken, [f.TaskId], PrepareOnly: true));
        var previous = (await f.Plans.GetAsync(f.TaskId, default))!;
        Assert.AreEqual(true, previous.CanKeepPreparedFiles);
        var download = JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync());
        var receipt = (await _db.DownloadResults.AsNoTracking().SingleAsync()).Fingerprint;
        var originalFile = JsonSerializer.Deserialize<string[]>((await _db.DownloadResults.AsNoTracking().SingleAsync()).FilesJson)!.Single();
        var input = new AcquisitionKeepPreparedFilesInput(2, note);
        var guards = new List<string?>();
        var kept = await f.Plans.KeepPreparedFilesAsync(f.TaskId, input, default, p => {guards.Add(p); return Task.CompletedTask;});
        CollectionAssert.Contains(guards.ToArray(), Path.GetDirectoryName(originalFile));
        Assert.IsNull(kept.Task.WorkflowRunId);
        Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual(3, kept.Task.PlanRevision);
        Assert.AreEqual(note ?? "delivery.keepPreparedFiles", kept.PlanHistory.Last().Reason);
        Assert.AreEqual(JsonSerializer.Serialize(previous.PlanHistory), JsonSerializer.Serialize(kept.PlanHistory.Take(2)));
        var plan = kept.Plan!;
        Assert.AreEqual(previous.Plan!.Parts.Single().ContinueFrom, plan.Parts.Single().ContinueFrom);
        Assert.AreEqual(previous.Plan.DeliveryLayout!.TaskDirectory, plan.DeliveryLayout!.TaskDirectory);
        Assert.AreEqual("extractArchive", kept.PlanHistory[1].Plan.Parts.Single().Processing!.Steps.Single().Op);
        Assert.IsFalse(plan.PublishRetainedFiles || plan.AdaptiveFileProcessing);
        Assert.AreEqual(false, plan.Parts.Single().UseAdaptiveAssessment);
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        CollectionAssert.AreEqual(new[] {"acquisition.plan.continueFiles", "acquisition.plan.process", "fs.publishArtifacts"},
            compiled.Nodes.Select(n => n.Kind).ToArray());
        var process = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(compiled.Nodes[1].ConfigJson!, WorkflowJson.Options)!;
        Assert.IsTrue(process.UseVerifiedInput);
        Assert.AreEqual("notRequired", process.Plan.Requirement);
        Assert.AreEqual(0, process.Plan.Steps.Count);
        Assert.AreEqual(3, (await f.Plans.KeepPreparedFilesAsync(f.TaskId, input, default)).Task.PlanRevision);
        Assert.AreEqual(3, (await f.Plans.KeepPreparedFilesAsync(f.TaskId, input with {ExpectedRevision = 3}, default)).Task.PlanRevision);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            f.Plans.KeepPreparedFilesAsync(f.TaskId, new(2, "Different scope"), default));
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
        Assert.IsFalse(Directory.Exists(plan.DeliveryLayout.TaskDirectory));
        var running = await f.Plans.RunAsync(f.TaskId, 3, default);
        Assert.AreEqual(1, f.Host.Calls);
        Assert.IsNotNull(running.Task.WorkflowRunId);
        Assert.IsNull(running.CanKeepPreparedFiles);
        Assert.AreEqual(download, JsonSerializer.Serialize(await _db.DownloadTasks.AsNoTracking().SingleAsync()));
        Assert.AreEqual(receipt, (await _db.DownloadResults.AsNoTracking().SingleAsync()).Fingerprint);
        Assert.AreEqual("verified existing bytes", await File.ReadAllTextAsync(originalFile));
    }

    [DataTestMethod]
    [DataRow("originalRun")]
    [DataRow("missing")]
    [DataRow("scope")]
    [DataRow("note")]
    public async Task LocalFiles_keep_prepared_rejects_unavailable_foreign_or_unapproved_inputs(string fault)
    {
        var f = await LocalFilesFixture();
        if (fault != "originalRun")
        {
            var preview = await f.Recovery.PreviewAsync(new([f.TaskId], "localFiles"));
            await f.Recovery.ApplyAsync(new(preview.PreviewToken, [f.TaskId], PrepareOnly: true));
        }
        var row = await _db.AcquisitionTasks.SingleAsync();
        if (fault == "missing")
        {
            var result = await _db.DownloadResults.AsNoTracking().SingleAsync();
            File.Delete(JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single());
        }
        if (fault == "scope")
        {
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
            var p = plan.Parts.Single();
            row.PlanJson = JsonSerializer.Serialize(plan with {Parts = [p with {ContinueFrom = p.ContinueFrom! with {TaskId = row.Id + 1}}]}, WorkflowJson.Options);
            await _db.SaveChangesAsync();
        }
        var before = row.PlanHistoryJson;
        if (fault == "note")
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Plans.KeepPreparedFilesAsync(f.TaskId, new(row.PlanRevision, new string('x', 1001)), default));
        else
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Plans.KeepPreparedFilesAsync(f.TaskId, new(row.PlanRevision), default));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Plans.DeferPreparedProcessingAsync(f.TaskId, row.PlanRevision, default));
        }
        Assert.AreEqual(before, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanHistoryJson);
        Assert.AreEqual(0, f.Host.Calls);
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
    }
}

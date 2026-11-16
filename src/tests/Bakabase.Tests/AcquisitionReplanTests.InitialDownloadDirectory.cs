using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Input)> MissingInitialDownloadDirectory()
    {
        var (task, originalRun, input) = await CurrentDirectoryInput();
        originalRun.Status = WorkflowRunStatus.Cancelled;
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        var original = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
        var continuation = new AcquisitionPlanArtifactInput(task.Id, originalRun.Id, input.Id, input.OperationId, input.Fingerprint);
        var plan = original with {Parts = original.Parts.Select(p => p with {ContinueFrom = continuation}).ToList(),
            StageDirectories = new() {TaskId = task.Id, Parts = new() {["main"] = new() {Delivery = original.StageDirectories?.Parts["main"].Delivery}}}};
        row.PlanRevision = 2; row.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        row.PlanHistoryJson = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, original, [], DateTime.UtcNow.AddDays(-1)),
            new AcquisitionPlanRevision(2, plan, [], DateTime.UtcNow, "continueFiles", originalRun.Id)}, WorkflowJson.Options);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Failed, StartedAt = DateTime.UtcNow,
            PlanSnapshotJson = WorkflowRunPlans.Serialize(AcquisitionPlanCompiler.Compile(plan)),
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        row.WorkflowRunId = run.Id; row.Status = AcquisitionStatus.Failed;
        await Db.SaveChangesAsync();
        return ((await Plans.GetAsync(task.Id, default))!, run, input);
    }

    [TestMethod]
    public async Task Initial_download_directory_projection_reads_exact_continuation_without_saving_or_creating_files()
    {
        var (task, _, input) = await MissingInitialDownloadDirectory();
        var before = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        var records = await Db.Artifacts.CountAsync();
        var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);

        var detail = (await Plans.GetAsync(task.Id, default))!;
        var snapshot = await Plans.GetStageDirectoriesAsync(task.Id, default);

        var download = detail.StageDirectories!.Parts["main"].Download!;
        Assert.AreEqual(input.RootDirectory, download.Directory);
        Assert.AreEqual(input.RootDirectory, snapshot.Parts.Single().DownloadDirectory);
        Assert.AreEqual("borrowed", download.Ownership); Assert.IsNull(download.BindingId);
        Assert.IsNull(detail.StageDirectories.Parts["main"].Processing);
        var after = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.AreEqual((before.PlanJson, before.PlanHistoryJson, before.BuilderStateJson, before.PlanRevision),
            (after.PlanJson, after.PlanHistoryJson, after.BuilderStateJson, after.PlanRevision));
        Assert.AreEqual(records, await Db.Artifacts.CountAsync());
        CollectionAssert.AreEquivalent(directories, Directory.GetDirectories(_root, "*", SearchOption.AllDirectories));
    }

    [TestMethod]
    public async Task Initial_download_directory_replan_retains_input_and_initializes_separate_processing_without_download()
    {
        var (task, prior, input) = await MissingInitialDownloadDirectory();
        var history = (prior.PlanSnapshotJson, prior.CurrentItemJson);
        var added = Path.Combine(input.RootDirectory, "user-added.key"); await File.WriteAllTextAsync(added, "retain");
        var revised = await Plans.ReplanAsync(task.Id, StageRequest(task, prior.Id, "download"), default);
        var plan = revised.Plan!; var stages = plan.StageDirectories!.Parts["main"];
        Assert.AreEqual(input.RootDirectory, stages.Download!.Directory);
        Assert.IsNotNull(stages.Processing?.BindingId);
        Assert.AreNotEqual(input.RootDirectory, stages.Processing!.Directory);
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        Assert.IsFalse(compiled.Nodes.Any(n => n.Kind.StartsWith("download.") || n.Kind == "baidu.transfer"));
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, plan, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, Sources = [new() {Module = "acquisition",
            EntityKind = "task", EntityId = task.Id.ToString(), Revision = revised.Task.PlanRevision.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id));
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var processed = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), attached.Replacement!, default);
        Assert.IsNull(processed.Suspension, processed.Suspension?.PromptJson);
        var target = ((DownloadWorkset)processed.Replacement!).Targets.Single();
        Assert.AreEqual(stages.Processing.Directory, target.ProcessedDirectory);
        Assert.AreEqual(2, target.ProcessedFiles!.Count);
        Assert.IsTrue(File.Exists(added)); Assert.IsTrue(File.Exists(input.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync()); Assert.AreEqual(0, _continuationArchives.Extractions);
        var saved = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == prior.Id);
        Assert.AreEqual(history, (saved.PlanSnapshotJson, saved.CurrentItemJson));
    }

    [DataTestMethod]
    [DataRow("bound-empty-processing")]
    [DataRow("bound-missing-processing")]
    [DataRow("bound-other-download")]
    [DataRow("processed-output")]
    [DataRow("processing-operation")]
    [DataRow("published-output")]
    [DataRow("other-current-download")]
    [DataRow("missing-files")]
    [DataRow("linked-directory")]
    [DataRow("foreign-owner")]
    [DataRow("wrong-fingerprint")]
    [DataRow("running")]
    [DataRow("pending-signal")]
    [DataRow("completed")]
    public async Task Initial_download_directory_projection_never_replaces_current_or_unproven_input(string fault)
    {
        var (task, run, input) = await MissingInitialDownloadDirectory();
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
        if (fault.StartsWith("bound-"))
        {
            var directory = Path.Combine(_root, fault);
            if (fault == "bound-empty-processing") Directory.CreateDirectory(directory);
            task = await BindCurrentStage(task, fault == "bound-other-download" ? "download" : "processing", directory);
        }
        if (fault is "processed-output" or "published-output" or "other-current-download")
        {
            var payload = AcquisitionPlanCompiler.Workset(task.Id, task.Task.PlanRevision, plan, task.SourceRefs);
            var target = payload.Targets.Single();
            target = fault switch {"processed-output" => target with {ProcessedArtifactId = "new-output"},
                "published-output" => target with {PublishedArtifactId = "published-output"},
                _ => target with {DownloadArtifactId = "different-download"}};
            run.CurrentItemJson = WorkflowItemSnapshot.Capture(payload with {Targets = [target]});
        }
        if (fault == "processing-operation")
        {
            var file = Path.Combine(_root, "partial.bin"); await File.WriteAllTextAsync(file, "processed");
            var output = await Artifacts.AdoptAsync(new("processed-current", "processed-current", "files.process", "current"),
                new(_root, [file], "processedFiles", true), "test");
            await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:process:step", output.OperationId);
        }
        if (fault == "missing-files") Directory.Move(input.RootDirectory, input.RootDirectory + "-offline");
        if (fault == "linked-directory")
        {
            Directory.Move(input.RootDirectory, input.RootDirectory + "-target");
            Directory.CreateSymbolicLink(input.RootDirectory, input.RootDirectory + "-target");
        }
        if (fault == "foreign-owner") await new ArtifactOperationBindingService(Db).BindAsync("external", "foreign", "input", input.OperationId);
        if (fault == "wrong-fingerprint")
            row.PlanJson = JsonSerializer.Serialize(plan with {Parts = plan.Parts.Select(p => p with
                {ContinueFrom = p.ContinueFrom! with {Fingerprint = "different"}}).ToList()}, WorkflowJson.Options);
        if (fault == "running") run.Status = WorkflowRunStatus.Running;
        if (fault == "pending-signal") run.PendingSignalJson = "{}";
        if (fault == "completed") {row.Status = AcquisitionStatus.Completed; run.Status = WorkflowRunStatus.Success;}
        await Db.SaveChangesAsync();

        var detail = (await Plans.GetAsync(task.Id, default))!;
        var stage = detail.StageDirectories!.Parts["main"];
        if (fault == "bound-other-download") Assert.AreEqual(Path.Combine(_root, fault), stage.Download!.Directory);
        else Assert.IsNull(stage.Download, fault);
        if (fault is "bound-empty-processing" or "bound-missing-processing") Assert.AreEqual(Path.Combine(_root, fault), stage.Processing!.Directory);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Initial_download_directory_projection_resolves_parts_independently_without_replacing_processing()
    {
        var (task, run, input) = await MissingInitialDownloadDirectory();
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
        var originalRun = await Db.WorkflowRuns.SingleAsync(r => r.Id == plan.Parts.Single().ContinueFrom!.PreviousRunId);
        var originalSet = AcquisitionEffectiveSourceProjection.Read(originalRun.CurrentItemJson)!;
        var directory = Directory.CreateDirectory(Path.Combine(_root, "second-download")).FullName;
        var file = Path.Combine(directory, "second.bin"); await File.WriteAllTextAsync(file, "other part");
        var target = originalSet.Targets.Single() with {Id = "other"};
        var second = await Artifacts.AdoptAsync(new("other-completed-download", "other-download", "download", DownloadArtifactCoordinator.Source(target)),
            new(directory, [file], "download", false), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", originalRun.Id.ToString(), "other:artifact.download", second.OperationId);
        originalRun.CurrentItemJson = WorkflowItemSnapshot.Capture(originalSet with {Targets = [originalSet.Targets.Single(), target with
            {DownloadArtifactId = second.Id, DownloadOperationId = second.OperationId, DownloadDirectory = directory, Files = [file]}]});
        var processing = Directory.CreateDirectory(Path.Combine(_root, "existing-processing")).FullName;
        plan = plan with {Parts = [plan.Parts.Single(), plan.Parts.Single() with {Id = "other", SelectedSourceId = "other-source",
            Sources = plan.Parts.Single().Sources.Select(s => s with {Id = "other-" + s.Id}).ToList(), ContinueFrom =
            new(task.Id, originalRun.Id, second.Id, second.OperationId, second.Fingerprint)}],
            StageDirectories = new() {TaskId = task.Id, Parts = new()
            {
                ["main"] = new() {Processing = new(processing, "borrowed", "userDirectoryRevision") {BindingId = Guid.NewGuid().ToString("N")}},
                ["other"] = new()
            }}, DeliveryLayout = null};
        row.PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options); await Db.SaveChangesAsync();

        var detail = (await Plans.GetAsync(task.Id, default))!;

        Assert.IsNull(detail.StageDirectories!.Parts["main"].Download);
        Assert.AreEqual(processing, detail.StageDirectories.Parts["main"].Processing!.Directory);
        Assert.AreEqual(second.RootDirectory, detail.StageDirectories.Parts["other"].Download!.Directory);
        Assert.AreNotEqual(input.RootDirectory, detail.StageDirectories.Parts["other"].Download!.Directory);
        Assert.IsNull(detail.StageDirectories.Parts["other"].Processing);
    }
}

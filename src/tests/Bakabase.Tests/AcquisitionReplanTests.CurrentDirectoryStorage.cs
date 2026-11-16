using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow("inherit")]
    [DataRow("task")]
    [DataRow("part")]
    [DataRow("current-defaults")]
    public async Task Current_directory_legacy_revision_allocates_processing_without_moving_inputs_or_changing_retention(string placement)
    {
        var (task, run, input) = await CurrentDirectoryInput();
        var current = Directory.CreateDirectory(Path.Combine(_root, "current-processing")).FullName;
        var explicitRoot = Directory.CreateDirectory(Path.Combine(_root, "explicit-processing")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "delivery")).FullName;
        var retired = Path.Combine(_root, "retired-download-volume");
        var policy = new ArtifactStoragePolicy {SourceCleanup = "quarantine", CleanupIntermediates = false,
            Materialization = "copy", ProcessingWorkspaceDirectory = placement is "task" or "current-defaults" ? explicitRoot : ""};
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        var legacy = task.Plan! with {StageDirectories = null, StoragePolicy = placement == "inherit" ? null : policy,
            OutputDirectory = retired, Parts = task.Plan!.Parts.Select(p => p with
                {StoragePolicy = placement is "part" or "current-defaults"
                    ? new ArtifactStoragePolicyOverride {ProcessingWorkspaceDirectory = explicitRoot} : null}).ToList()};
        row.PlanJson = JsonSerializer.Serialize(legacy, WorkflowJson.Options);
        var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(row.BuilderStateJson!, WorkflowJson.Options)!;
        row.BuilderStateJson = JsonSerializer.Serialize(state with {StoragePolicy = legacy.StoragePolicy}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        var oldRun = (run.PlanSnapshotJson, run.CurrentItemJson, run.WaitPromptJson);
        var oldHistory = row.PlanHistoryJson;
        var storage = new PublicationStoragePolicy(retired);
        var plans = PublicationStoragePlans(current, delivery, storage);
        var revised = await plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, input.Id) with
            {UseCurrentStorageDefaults = placement == "current-defaults"}, default);
        var stage = revised.Plan!.StageDirectories!.Parts["main"];
        var expected = placement is "task" or "part" ? explicitRoot : current;
        Assert.AreEqual(expected, stage.Processing!.Allocation!.StorageRoot);
        Assert.AreEqual("exclusive", stage.Processing.Ownership);
        Assert.AreEqual(input.RootDirectory, stage.Download!.Directory);
        Assert.AreEqual("borrowed", stage.Download.Ownership);
        Assert.IsNull(stage.Download.Allocation, "Historical input binding never grants ownership of its parent.");
        Assert.IsFalse(Directory.Exists(stage.Processing.Directory), "Planning allocates no payload files.");
        var retainedPolicy = revised.Plan.StoragePolicy!;
        Assert.AreEqual(placement == "inherit" ? "keep" : "quarantine", retainedPolicy.SourceCleanup);
        Assert.AreEqual(placement == "inherit", retainedPolicy.CleanupIntermediates);
        Assert.AreEqual(placement == "inherit" ? "auto" : "copy", retainedPolicy.Materialization);
        var target = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan, revised.SourceRefs).Targets.Single();
        Assert.AreEqual(stage.Processing.Allocation, target.ProcessingDirectoryScope);
        Assert.IsTrue(AcquisitionPlanCompiler.Compile(revised.Plan).Nodes.All(n => !n.Kind.StartsWith("download.") && n.Kind != "baidu.transfer"));
        Assert.IsTrue(storage.Checked.All(p => !p.StartsWith(retired, StringComparison.Ordinal)));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.IsFalse(Directory.Exists(Path.Combine(input.RootDirectory, ".bakabase-work")));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        var savedRun = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(oldRun, (savedRun.PlanSnapshotJson, savedRun.CurrentItemJson, savedRun.WaitPromptJson));
        Assert.AreEqual(oldHistory, JsonSerializer.Serialize(revised.PlanHistory.Take(revised.PlanHistory.Count - 1), WorkflowJson.Options));
    }

    [TestMethod]
    public async Task Current_directory_legacy_execution_writes_only_to_processing_allocation_and_keeps_input_in_place()
    {
        var (task, old, input) = await CurrentDirectoryInput();
        var current = Directory.CreateDirectory(Path.Combine(_root, "current-processing")).FullName;
        var delivery = Directory.CreateDirectory(Path.Combine(_root, "delivery")).FullName;
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        row.PlanJson = JsonSerializer.Serialize(task.Plan! with {StageDirectories = null, StoragePolicy = null}, WorkflowJson.Options);
        var state = JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(row.BuilderStateJson!, WorkflowJson.Options)!;
        row.BuilderStateJson = JsonSerializer.Serialize(state with {StoragePolicy = null}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        var plans = PublicationStoragePlans(current, delivery, new CompositeStorage());
        var request = FreshRequest(task, old.Id, input.Id);
        var revised = await plans.ReplanAsync(task.Id, request with {EnableAdaptiveAssessment = false, Items =
            [request.Items.Single() with {UseAdaptiveAssessment = false, Plan = new() {Requirement = "required", Steps =
                [new() {Id = "extract", Op = "extractArchive", Selector = "original.bin"}]}}]}, default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id));
        WorkflowExecutionContext Context(int n) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = compiled.Nodes[n].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        _continuationArchives.ExpectedInput = "original bytes";
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), workset, default);
        var result = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), attached.Replacement!, default);
        Assert.IsNull(result.Suspension, result.Suspension?.PromptJson);
        var output = ((DownloadWorkset) result.Replacement!).Targets.Single();
        var allocated = revised.Plan!.StageDirectories!.Parts["main"].Processing!.Directory;
        Assert.AreEqual(allocated, output.ProcessedDirectory, "Complete output is handed back to the visible processing root.");
        Assert.AreEqual("remaining extraction", await File.ReadAllTextAsync(output.ProcessedFiles!.Single()));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.IsFalse(Directory.Exists(Path.Combine(input.RootDirectory, ".bakabase-work")));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}

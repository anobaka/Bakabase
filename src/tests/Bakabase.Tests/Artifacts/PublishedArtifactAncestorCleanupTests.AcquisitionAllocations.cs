using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private AcquisitionPlan AllocationPlan() => new() {Title = "Another task", OutputDirectory = root,
        Parts = [new() {Id = "part"}], StoragePolicy = new() {ProcessingWorkspaceDirectory = root}};

    [TestMethod]
    [DataRow("valid")]
    [DataRow("unknown-field")]
    [DataRow("nested-input")]
    [DataRow("candidate-root")]
    public async Task Plan_output_allocation_is_location_scoped_and_never_masks_unknown_or_concrete_paths(string variant)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var node = JsonSerializer.SerializeToNode(AllocationPlan(), WorkflowJson.Options)!;
        if (variant == "unknown-field") node["unknown"] = true;
        if (variant == "nested-input") node["parts"]![0]!["sources"] = new JsonArray(new JsonObject {["id"] = "input", ["url"] = root});
        if (variant == "candidate-root") node["outputDirectory"] = source.RootDirectory;
        db.AcquisitionTasks.Add(new() {Title = "Another", WorkflowRunId = 2, PlanRevision = 1, PlanJson = node.ToJsonString()});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.AreEqual(variant == "valid", (await Preview(final)).Single().Eligible);
        Assert.IsTrue(File.Exists(source.Files.Single()));
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("unbound")]
    [DataRow("wrong-type")]
    [DataRow("unknown-field")]
    [DataRow("concrete-input")]
    [DataRow("wrong-root")]
    [DataRow("deleted")]
    [DataRow("superseded-deleted")]
    [DataRow("superseded-concrete-input")]
    [DataRow("superseded-unbound-history")]
    [DataRow("ambiguous-owner")]
    public async Task Workset_allocation_requires_its_owned_revision_and_never_masks_concrete_inputs(string variant)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var plan = AllocationPlan(); if (variant == "wrong-root") plan = plan with {OutputDirectory = Path.Combine(root, "other")};
        var task = new AcquisitionTaskDbModel {Title = "Another", WorkflowRunId = 3, PlanRevision = 1,
            PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options)};
        if (variant == "deleted" || variant.StartsWith("superseded")) task.DeletedAt = DateTime.UtcNow;
        if (variant.StartsWith("superseded"))
        {
            var next = plan with {OutputDirectory = Path.Combine(root, "new-allocation")};
            task.PlanRevision = 2;
            task.WorkflowRunId = 2;
            task.PlanJson = JsonSerializer.Serialize(next, WorkflowJson.Options);
            AcquisitionPlanRevision[] history = [new(1, plan, [], DateTime.UtcNow.AddDays(-1)),
                new(2, next, [], DateTime.UtcNow, "recovery", variant == "superseded-unbound-history" ? 99 : 3)];
            task.PlanHistoryJson = JsonSerializer.Serialize(history, WorkflowJson.Options);
        }
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (variant != "unbound")
        { db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 3, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"}); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }
        if (variant == "ambiguous-owner")
        {
            db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 3, Module = "acquisition", EntityKind = "task",
                EntityId = (task.Id + 1).ToString(), Revision = "1"});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        var workset = new DownloadWorkset {Id = "workset", OutputDirectory = root, Targets = [new() {Id = "part",
            Files = variant is "concrete-input" or "superseded-concrete-input" ? [source.Files.Single()] : []}]};
        var payload = JsonSerializer.SerializeToNode(workset, WorkflowJson.Options)!;
        if (variant == "unknown-field") payload["unknown"] = true;
        var current = new JsonObject {["type"] = variant == "wrong-type" ? "unknown.DownloadWorkset" :
            typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name, ["item"] = payload.DeepClone()};
        var payloadJson = payload.ToJsonString(); var currentJson = current.ToJsonString();
        var promptJson = JsonSerializer.Serialize(new {workset.Targets, Reason = "attention", workset.OutputDirectory}, WorkflowJson.Options);
        await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(u => u.SetProperty(r => r.PayloadJson, payloadJson)
            .SetProperty(r => r.CurrentItemJson, currentJson).SetProperty(r => r.WaitPromptJson, promptJson));
        Assert.AreEqual(variant is "valid" or "deleted" or "superseded-deleted", (await Preview(final)).Single().Eligible);
        Assert.IsTrue(File.Exists(source.Files.Single()));
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("unknown-field")]
    [DataRow("mismatched-policy")]
    [DataRow("concrete-delivery")]
    public async Task Queue_working_root_is_typed_allocation_while_delivery_and_unknown_data_stay_protected(string variant)
    {
        var source = await Artifact("processedFiles"); var final = await Artifact("publishedFiles", source);
        var plan = AllocationPlan(); if (variant == "mismatched-policy") plan = plan with {StoragePolicy = new() {ProcessingWorkspaceDirectory = Path.Combine(root, "other")}};
        var queue = JsonSerializer.SerializeToNode(new AcquisitionLocalRecoveryQueueEntry {BatchId = Guid.NewGuid().ToString("N"),
            Version = new('a', 64), StorageStamp = new('b', 64), Signal = "{}", RecoveryScope = "workflow", PlanRevision = 1,
            WorkflowRunId = 2, WorkingDirectories = [root], DeliveryDirectories = variant == "concrete-delivery" ? [source.RootDirectory] : []}, WorkflowJson.Options)!;
        if (variant == "unknown-field") queue["unknown"] = true;
        var task = new AcquisitionTaskDbModel {Title = "Queued", WorkflowRunId = 2, PlanRevision = 1,
            PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options), BuilderStateJson = new JsonObject {["localRecoveryQueue"] = queue}.ToJsonString()};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 2, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.AreEqual(variant == "valid", (await Preview(final)).Single().Eligible);
    }
}

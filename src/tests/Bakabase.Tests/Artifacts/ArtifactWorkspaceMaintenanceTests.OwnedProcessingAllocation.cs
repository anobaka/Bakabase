using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactWorkspaceMaintenanceTests
{
    [TestMethod]
    [DataRow("allocation")]
    [DataRow("leaf-reference")]
    [DataRow("unknown-field")]
    [DataRow("another-task")]
    [DataRow("running")]
    [DataRow("pending")]
    [DataRow("waiting")]
    [DataRow("ambiguous-owner")]
    [DataRow("missing-owner")]
    [DataRow("shared-binding")]
    [DataRow("missing-locator")]
    [DataRow("changed-current-run")]
    public async Task Private_cleanup_recognizes_only_its_stopped_task_processing_allocation(string variant)
    {
        var storage = Directory.CreateDirectory(Path.Combine(_root, "processing")).FullName;
        var scope = new ArtifactDirectoryScope("acquisition", "12", "processing-main", storage, Path.Combine(storage, "task"));
        var policy = new ArtifactStoragePolicy {ProcessingWorkspaceDirectory = storage, CleanupIntermediates = true,
            SourceCleanup = "keep", Materialization = "copy"};
        var key = ArtifactManifestFiles.Hash("owned-processing-fixture");
        var resolver = new FileProcessingWorkspaceResolver(_root);
        var location = await resolver.ResolveOperationAsync(key, 1, _root, storage, scope, true, policy, "Task", default);
        var original = Path.Combine(_root, "input.bin"); await File.WriteAllTextAsync(original, "keep original");
        var recipe = new FileProcessingPlan {Requirement = "notRequired"};
        var result = await new FileProcessingPlanExecutor(new NoExtraction()).ExecuteAsync(recipe, _root, [original],
            Path.Combine(location, "plan"), storagePolicy: policy);
        Assert.IsTrue(result.Completed, result.Message);
        var stateFile = Path.Combine(Path.GetDirectoryName(result.OutputDirectory!)!, "state.json");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        var intermediate = state.Outputs["download"].Single().Path;
        var unknown = Path.Combine(Path.GetDirectoryName(intermediate)!, "user.key");
        await File.WriteAllTextAsync(unknown, "unknown files are not cleanup candidates");
        var metadata = new FileArtifactProcessingMetadata(null, JsonSerializer.Serialize(recipe, Json), null,
            StoragePolicy: policy, WorkspaceDirectoryScope: scope);
        var artifact = await _store.AdoptAsync(new(key, "owned-processing-fixture", "files.process", "input"),
            new(result.OutputDirectory!, result.Files, "processedFiles", true, MetadataJson: JsonSerializer.Serialize(metadata, Json)), "fixture");
        var stages = new AcquisitionStageDirectories {TaskId = 12, Parts = new()
        {
            ["main"] = new() {Processing = new(scope.Directory, "exclusive", "taskAllocation") {Allocation = scope}}
        }};
        var plan = new AcquisitionPlan {Title = "Task", OutputDirectory = _root, Parts = [new() {Id = "main"}],
            StoragePolicy = policy, StageDirectories = stages};
        var builder = new JsonObject { ["stageDirectories"] = JsonSerializer.SerializeToNode(stages, Json) };
        if (variant == "leaf-reference") builder["actualInput"] = intermediate;
        if (variant == "unknown-field") builder["stageDirectories"]!["parts"]!["main"]!["processing"]!["unknown"] = scope.Directory;
        var status = variant switch {"running" => WorkflowRunStatus.Running, "pending" => WorkflowRunStatus.Pending,
            "waiting" => WorkflowRunStatus.Waiting, _ => WorkflowRunStatus.Success};
        _db.WorkflowRuns.AddRange(new WorkflowRunDbModel {Id = 70, Status = status}, new() {Id = 71, Status = WorkflowRunStatus.Success});
        _db.AcquisitionTasks.Add(new AcquisitionTaskDbModel {Id = 12, Title = "Task", WorkflowRunId = variant == "changed-current-run" ? 71 : 70,
            PlanRevision = 1, PlanJson = JsonSerializer.Serialize(plan, Json), BuilderStateJson = builder.ToJsonString()});
        if (variant != "missing-owner")
            _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 70, Module = "acquisition", EntityKind = "task", EntityId = "12", Revision = "1"});
        if (variant == "ambiguous-owner")
            _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 70, Module = "acquisition", EntityKind = "task", EntityId = "13", Revision = "1"});
        if (variant == "another-task")
            _db.AcquisitionTasks.Add(new() {Id = 13, Title = "Other", WorkflowRunId = 71, PlanRevision = 1,
                PlanJson = JsonSerializer.Serialize(plan, Json)});
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        Assert.IsTrue(await new ArtifactOperationBindingService(_db).BindAsync("workflow", "70", "processing", artifact.OperationId));
        if (variant == "shared-binding")
            Assert.IsTrue(await new ArtifactOperationBindingService(_db).BindAsync("another-module", "71", "processing", artifact.OperationId));
        if (variant == "missing-locator")
            File.Delete(Path.Combine(_root, "artifact-workspaces", "locations", $"operations-{key}-1.json"));

        var expected = variant == "allocation";
        var preview = (await _maintenance.PreviewAsync([artifact.Id], default)).Single();
        var referenceEvidence = new List<string>();
        if (expected && !preview.Eligible)
            await new ArtifactCleanupReferenceGuard(_db, resolver, referenceFound: referenceEvidence.Add)
                .HasReferencesAsync(artifact, [], default, [intermediate]);
        Assert.AreEqual(expected, preview.Eligible, preview.Reason + ": " + string.Join(", ", referenceEvidence));
        var outcome = (await _maintenance.ReclaimAsync([artifact.Id], default)).Single();
        Assert.AreEqual(expected, outcome.Completed, outcome.Reason);
        Assert.AreEqual(!expected, File.Exists(intermediate));
        Assert.IsTrue(File.Exists(original), "Keeping the original download remains independent of its private copy.");
        Assert.IsTrue(File.Exists(artifact.Files.Single()));
        Assert.IsTrue(File.Exists(unknown));
        Assert.IsTrue(File.Exists(stateFile));
    }
}

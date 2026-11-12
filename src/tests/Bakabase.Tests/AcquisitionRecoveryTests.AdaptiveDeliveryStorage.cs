using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("ready")]
    [DataRow("partial-pending")]
    [DataRow("second-iteration-processed")]
    [DataRow("changed-file")]
    [DataRow("added-file")]
    [DataRow("forged-current-owner")]
    [DataRow("other-run")]
    [DataRow("other-target")]
    [DataRow("iteration-limit")]
    [DataRow("missing-adaptive-binding")]
    [DataRow("missing-checkpoint")]
    [DataRow("checkpoint-input-changed")]
    [DataRow("adaptive-disabled")]
    [DataRow("changed-decision")]
    public async Task AdaptiveCurrentDirectoryStorageResumeRequiresExactSavedOwnerPlanAndInput(string scenario)
    {
        var (task, run, workset) = await Seed(AssessFileArtifactDeliveryActivity.ActivityKind, WorkflowRunStatus.Success, null);
        task.PlanRevision = 2;
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var storage = new UserStoragePolicy(false);
        var inputRoot = Path.Combine(_root, "input"); Directory.CreateDirectory(inputRoot);
        var file = Path.Combine(inputRoot, "one.zip"); await File.WriteAllTextAsync(file, "existing input");
        var original = await store.AdoptAsync(new("original", "original", "files.process", "fixture"),
            new(inputRoot, [file], "processedFiles", true), "fixture");
        var bind = new ArtifactOperationBindingService(_db);
        Assert.IsTrue(await bind.BindAsync("workflow", run.Id.ToString(), "part:artifact.continuation", original.OperationId));
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"});
        await _db.SaveChangesAsync();
        var input = await AcquisitionCurrentDirectoryFiles.CaptureAsync(_db, store, storage, null,
            task.Id, 2, run.Id, "part", original, null, default);
        await _db.ArtifactOperationBindings.Where(b => b.OwnerKind == "workflow" && b.OwnerId == run.Id.ToString() && b.Slot == "part:artifact.continuation").ExecuteDeleteAsync();
        Assert.IsTrue(await bind.BindAsync("workflow", run.Id.ToString(), "part:artifact.continuation", input.OperationId));
        if (scenario == "second-iteration-processed")
        {
            input = original;
            await _db.ArtifactOperationBindings.Where(b => b.OwnerKind == "workflow" && b.OwnerId == run.Id.ToString() && b.Slot == "part:artifact.continuation").ExecuteDeleteAsync();
        }
        var iteration = scenario == "second-iteration-processed" ? 1 : 0;
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "adaptive-1", Op = "extractArchive", Input = "download", Selector = "one.zip"}],
            Evidence = ["adaptive-file-processing", "saved-model-decision"]};
        var normalized = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        var processingConfig = new FileArtifactProcessingConfig();
        var intent = FileArtifactProcessor.Intent("adaptive-file-processing-v1:" + input.OperationId + ":" + input.Fingerprint + ":" + plan.Evidence[1],
            normalized, null, processingConfig);
        var review = new FileArtifactDeliveryReview(input.Id, input.Fingerprint, normalized, intent, "StorageUnavailable");
        var target = workset.Targets.Single() with {ProcessedArtifactId = input.Id, ProcessedDirectory = input.RootDirectory,
            ProcessedFiles = input.Files.ToList(), ProcessingOperationId = input.OperationId,
            DeliveryReview = review, ProcessingWaitReason = review.WaitReason, Problem = review.WaitReason};
        var config = new FileArtifactDeliveryAssessmentConfig("part", EnableAdaptiveAssessment: scenario != "adaptive-disabled");
        var key = FileArtifactProcessor.OperationKey(intent, input, plan, null, processingConfig);
        var consumer = $"workflow:{(scenario == "other-run" ? run.Id + 100 : run.Id)}:adaptive:{(scenario == "other-target" ? "wrong" : "part")}:{(scenario == "iteration-limit" ? 3 : iteration)}";
        await store.ClaimAsync(new(key, intent, "files.process", input.Fingerprint), consumer, TimeSpan.FromMinutes(2));
        if (scenario != "missing-adaptive-binding")
            Assert.IsTrue(await bind.BindAsync("workflow", run.Id.ToString(), "part:adaptive:input:" + input.Id, input.OperationId));
        var processingRoot = Path.Combine(_root, "processing"); Directory.CreateDirectory(processingRoot);
        var locations = new FileProcessingWorkspaceResolver(Path.Combine(_root, "metadata"), storage);
        var operationDirectory = await locations.ResolveAsync("operations", key, 1, input.RootDirectory, processingRoot, default);
        var checkpoint = Path.Combine(operationDirectory, "plan", FileProcessingPlanExecutor.CheckpointIdentity(plan,
            input.Files.OrderBy(path => path, ArtifactManifestFiles.PathComparer).ToArray(), locations.IdentityPath));
        Directory.CreateDirectory(checkpoint);
        if (scenario != "missing-checkpoint") await File.WriteAllTextAsync(Path.Combine(checkpoint, "state.json"),
            JsonSerializer.Serialize(new {inputManifest = scenario == "checkpoint-input-changed"
                ? input.Manifest with {Fingerprint = "wrong"} : input.Manifest}, WorkflowJson.Options));
        if (scenario == "partial-pending")
        {
            Directory.CreateDirectory(Path.Combine(checkpoint, "result.pending"));
            await File.WriteAllTextAsync(Path.Combine(checkpoint, "result.pending", "partial.wav"), "");
        }
        if (scenario == "forged-current-owner")
        {
            var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(input.MetadataJson!, WorkflowJson.Options)!;
            var forged = JsonSerializer.Serialize(receipt with {TaskId = task.Id + 1}, WorkflowJson.Options);
            await _db.Artifacts.Where(a => a.Id == input.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.MetadataJson, forged));
        }
        if (scenario == "changed-decision")
            review = review with {PlanJson = JsonSerializer.Serialize(plan with {Evidence = ["adaptive-file-processing", "different-decision"]}, WorkflowJson.Options)};
        target = target with {DeliveryReview = review};
        workset = workset with {Targets = [target], SourceRefs = [new("acquisition", "task", task.Id.ToString(), "2")]};
        run.Status = WorkflowRunStatus.Waiting;
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Adaptive retained review", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "review", Kind = AssessFileArtifactDeliveryActivity.ActivityKind, ConfigJson = JsonSerializer.Serialize(config, WorkflowJson.Options)},
                new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]});
        run.WaitReason = "cloud.fileProcessing";
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = target.Id, reason = review.WaitReason,
            message = "LOCAL_STORAGE_RESERVE_LOW", artifactId = input.Id, fingerprint = input.Fingerprint,
            reviewToken = AssessFileArtifactDeliveryActivity.ReviewToken(target.Id, review), deliveryReview = true}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        if (scenario == "changed-file") await File.WriteAllTextAsync(file, "modified input");
        if (scenario == "added-file") await File.WriteAllTextAsync(Path.Combine(inputRoot, "manual.key"), "new input");
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, store, workspaces: locations);
        var before = run.CurrentItemJson;
        var outcome = await diagnostics.DiagnoseAsync(task, run, default);
        var allowed = scenario is "ready" or "partial-pending" or "second-iteration-processed";
        Assert.AreEqual(allowed, outcome.Row.CanContinue, outcome.Row.ReasonCode);
        Assert.AreEqual(before, run.CurrentItemJson);
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
        Assert.AreEqual(1, (await store.GetOperationAsync(key))!.Attempt);
        if (allowed)
        {
            var selected = await FileArtifactDeliveryStorageContinuation.ReadAsync(_db, store, locations, run.Id,
                target, input, review, config, plan, processingConfig, default);
            Assert.IsNotNull(selected); Assert.AreEqual(consumer, selected.Consumer); Assert.AreEqual(checkpoint, selected.Directory);
            Assert.AreEqual(iteration, selected.Iteration);
            var signal = JsonSerializer.Deserialize<FileArtifactDeliverySignal>(outcome.SignalJson, WorkflowJson.Options)!;
            Assert.IsNull(signal.Processing.UserContext); Assert.IsNull(signal.Processing.ExtractionPlanJson);
            var same = await store.ClaimSameAttemptAsync(new(key, intent, "files.process", input.Fingerprint), selected.Attempt,
                selected.Consumer, TimeSpan.FromMinutes(2));
            Assert.AreEqual(ArtifactClaimStatus.Acquired, same.Status, "The original adaptive lease can be resumed immediately.");
            Assert.AreEqual(1, (await store.GetOperationAsync(key))!.Attempt);
        }
    }
}

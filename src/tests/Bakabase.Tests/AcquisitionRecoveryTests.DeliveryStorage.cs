using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow(null)]
    [DataRow("token")]
    [DataRow("fingerprint")]
    [DataRow("manualPlan")]
    [DataRow("otherOwner")]
    [DataRow("missingBinding")]
    [DataRow("unclaimedAttempt")]
    [DataRow("changedFile")]
    [DataRow("passwordWait")]
    [DataRow("storageUnavailable")]
    [DataRow("untypedStorage")]
    public async Task DeliveryStorageWaitResumesOnlyExactOwnedReviewWithoutChangingAnyBudget(string? damage)
    {
        var (task, run, workset) = await Seed(AssessFileArtifactDeliveryActivity.ActivityKind, WorkflowRunStatus.Waiting, null);
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var file = Path.Combine(_root, "retained.zip"); await File.WriteAllTextAsync(file, "retained archive bytes");
        var artifact = await store.AdoptAsync(new("processed-input", "processed-intent", "files.process", "input"),
            new(_root, [file], "processedFiles", true), "input-owner");
        var plan = new FileProcessingPlan {Requirement = damage == "manualPlan" ? "unknown" : "notRequired"};
        var limits = new RecursiveArchiveOptions {Enabled = true, MaxExpandedBytes = 20L * 1024 * 1024 * 1024};
        var config = new FileArtifactProcessingConfig {ArchivePolicy = "autoDetect", Recursive = limits};
        var waitReason = damage == "passwordWait" ? "PasswordUnknown" :
            damage is "storageUnavailable" or "untypedStorage" ? "StorageUnavailable" : "DiskSpaceLimit";
        var review = new FileArtifactDeliveryReview(artifact.Id, artifact.Fingerprint, JsonSerializer.Serialize(plan, WorkflowJson.Options),
            "same-retained-intent", waitReason, AutomaticOuter: true, Limits: limits);
        var target = workset.Targets[0] with {ProcessedArtifactId = artifact.Id, ProcessingOperationId = artifact.OperationId,
            ProcessedDirectory = artifact.RootDirectory, ProcessedFiles = artifact.Files.ToList(), DeliveryReview = review,
            ProcessingWaitReason = review.WaitReason, Problem = review.WaitReason};
        workset = workset with {Targets = [target], SourceRefs = [new("acquisition", "task", task.Id.ToString(), "1")]};
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = (damage == "otherOwner" ? task.Id + 1 : task.Id).ToString(), Revision = "1"});
        if (damage != "missingBinding") _db.ArtifactOperationBindings.Add(new() {OwnerKind = "workflow", OwnerId = run.Id.ToString(),
            Slot = "part:process:delivery-input:" + artifact.Id, OperationId = artifact.OperationId});
        if (damage != "unclaimedAttempt")
            await store.ClaimAsync(new(FileArtifactProcessor.OperationKey(review.Intent!, artifact, plan, null, config),
                review.Intent!, "files.process", artifact.Fingerprint), $"workflow:{run.Id}:delivery-review:part", TimeSpan.FromMinutes(2));
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Retained delivery review", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "review", Kind = AssessFileArtifactDeliveryActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(new FileArtifactDeliveryAssessmentConfig("part"), WorkflowJson.Options)},
                new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]});
        run.WaitReason = "cloud.fileProcessing";
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = target.Id, reason = review.WaitReason,
            artifactId = artifact.Id, fingerprint = damage == "fingerprint" ? "different" : artifact.Fingerprint,
            reviewToken = damage == "token" ? "changed" : AssessFileArtifactDeliveryActivity.ReviewToken(target.Id, review),
            deliveryReview = true, automaticOuterPackaging = true, recursiveOptions = limits,
            message = damage == "storageUnavailable" ? "LOCAL_STORAGE_RESERVE_LOW" : null}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        if (damage == "changedFile") await File.WriteAllTextAsync(file, "changed");
        var before = run.CurrentItemJson;
        var metadata = await _diagnostics.DiagnoseAsync(task, run, default, verifyFiles: false);
        Assert.IsFalse(metadata.Row.CanContinue);
        if (damage == null) Assert.AreEqual("RECOVERY_VERIFY_FILES", metadata.Row.ReasonCode);
        var diagnosis = await _diagnostics.DiagnoseAsync(task, run, default);
        Assert.AreEqual(damage is null or "storageUnavailable", diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        Assert.AreEqual(before, run.CurrentItemJson);
        if (damage is null or "storageUnavailable")
        {
            var signal = JsonSerializer.Deserialize<FileArtifactDeliverySignal>(diagnosis.SignalJson, WorkflowJson.Options)!;
            Assert.AreEqual(artifact.Id, signal.ArtifactId); Assert.AreEqual("part", signal.Processing.TargetId);
            Assert.IsNull(signal.Processing.RecursiveOptions); Assert.IsNull(signal.Processing.ExtractionPlanJson);
            Assert.IsNull(signal.Processing.StorageRecovery); Assert.IsNull(signal.Processing.Password);
        }
        Assert.AreEqual(0, _db.DownloadTasks.Count()); Assert.AreEqual(0, _db.CloudDownloadOperations.Count());
    }
}

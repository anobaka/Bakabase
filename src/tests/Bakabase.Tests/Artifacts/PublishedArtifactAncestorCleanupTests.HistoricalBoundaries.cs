using System.Runtime.InteropServices;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow("failed-no-prompt")]
    [DataRow("cancelled-legacy-prompt")]
    [DataRow("purged")]
    [DataRow("missing-source")]
    [DataRow("task-id")]
    [DataRow("previous-run")]
    [DataRow("artifact-id")]
    [DataRow("operation-id")]
    [DataRow("fingerprint")]
    [DataRow("wrong-continuation-target")]
    [DataRow("ambiguous-predecessor")]
    [DataRow("failed-predecessor")]
    [DataRow("truncated-predecessor")]
    [DataRow("wrong-receipt-root")]
    [DataRow("missing-binding")]
    [DataRow("missing-purge-receipt")]
    [DataRow("foreign-sibling")]
    [DataRow("duplicate-output-key")]
    [DataRow("conflicting-receipt")]
    [DataRow("foreign-source-binding")]
    [DataRow("unexpected-wait-reason")]
    [DataRow("non-exclusive-previous-run")]
    [DataRow("non-exclusive-current-run")]
    [DataRow("wrong-reason-without-prompt")]
    [DataRow("replaced-receipt-after-preview")]
    [DataRow("still-waiting")]
    [DataRow("changed-receipt-after-preview")]
    [DataRow("renewed-lease-after-preview")]
    public async Task Frozen_continuation_of_a_deleted_task_reclaims_only_its_verified_historical_boundary(string scenario)
    {
        const string part = "part-fixture";
        var input = await Artifact("download", owner: 2, filename: "archive.zip", contents: "archive bytes");
        var published = await Artifact("publishedFiles", input, filename: "archive.zip", contents: "archive bytes");
        var processingRoot = Directory.CreateDirectory(Path.Combine(root, "processing")).FullName;
        var task = new AcquisitionTaskDbModel {Title = "Retired processing fixture", Status = AcquisitionStatus.Completed,
            PlanRevision = 2, WorkflowRunId = 1, DeletedAt = DateTime.UtcNow};
        db.AcquisitionTasks.Add(task);
        await db.SaveChangesAsync();
        var userFolder = Path.Combine(root, "historical-stage-directory");
        var storagePlan = new AcquisitionPlan {Parts = [new() {Id = part}], StageDirectories = new() {TaskId = task.Id,
            Parts = new() {[part] = new() {Processing = new(userFolder, "borrowed", "userDirectoryRevision") {BindingId = Guid.NewGuid().ToString("N")}}}}};
        task.PlanJson = JsonSerializer.Serialize(storagePlan, Json);
        foreach (var run in new[] {1,2})
            db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = run == 1 ? "2" : "1"});
        if (scenario is "non-exclusive-previous-run" or "non-exclusive-current-run")
            db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = scenario == "non-exclusive-previous-run" ? 2 : 1,
                Module = "acquisition", EntityKind = "task", EntityId = (task.Id + 10).ToString(), Revision = "1"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var target = new CloudDownloadTarget {Id = part, Title = "Fixture", DownloadArtifactId = input.Id,
            StoragePolicy = new() {ProcessingWorkspaceDirectory = processingRoot}};
        var workset = new DownloadWorkset {Id = $"acquisition:{task.Id}:2", Targets = [target]};
        var artifactInput = new AcquisitionPlanArtifactInput(task.Id, 2, input.Id, input.OperationId, input.Fingerprint);
        var declaredPlan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "outer", Op = "extractArchive", Selector = "archive.zip"},
            new() {Id = "inner", Op = "extractArchive", Input = "outer", Selector = "nested.zip"}]};
        var config = new AcquisitionPlanProcessingConfig(part, declaredPlan, "inner", ContinueFrom: artifactInput, StoragePolicy: target.StoragePolicy);
        var processing = new FileArtifactProcessingConfig {StorageTitle = target.Title, StoragePolicy = target.StoragePolicy};
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var effective = AcquisitionPlanProcessingActivity.EffectivePlan(config, target);
        var intent = AcquisitionPlanProcessingActivity.Intent(workset, target, effective, config.StopAfterStepId, resolver, artifactInput);
        var recipe = FileExtractionLayouts.Apply(FileArtifactProcessor.WithDefaultPasswords(effective, null), processing.ExtractionLayout);
        var key = FileArtifactProcessor.OperationKey(intent, input, recipe, null, processing);
        var claim = await store.ClaimAsync(new(key, intent, "files.process", input.Fingerprint), "workflow:1:" + part, TimeSpan.FromMinutes(2));
        Assert.IsNotNull(claim.Lease);
        await resolver.ResolveOperationAsync(key, 1, input.RootDirectory, processingRoot, null, true, null, target.Title, default);
        var boundary = await resolver.ResolveCheckpointAsync(FileArtifactProcessor.CheckpointKey(input, recipe), key, 1,
            input, recipe, null, processingRoot, null, target.Title, default);
        var extraction = new FailedNestedExtraction();
        var executor = new FileProcessingPlanExecutor(extraction);
        var failure = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => executor.ExecuteAsync(recipe,
            input.RootDirectory, input.Files, boundary, stopAfterStepId: "inner", identityPath: resolver.IdentityPath));
        Assert.AreEqual("Second extraction stopped.", failure.Message);
        var stateFile = Directory.GetFiles(boundary, "state.json", SearchOption.AllDirectories).Single();
        var stateText = await File.ReadAllTextAsync(stateFile);
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(stateText, Json)!;
        var ownFiles = new[] {state.Outputs["download"].Single().Path, state.Outputs["outer"].Single().Path};
        Assert.IsTrue(ownFiles.All(File.Exists));
        var binds = new ArtifactOperationBindingService(db);
        Assert.IsTrue(await binds.BindAsync("workflow", "2", part + ":artifact.download", input.OperationId));
        if (scenario == "foreign-source-binding")
            Assert.IsTrue(await binds.BindAsync("workflow", "3", "foreign-download", input.OperationId));
        if (scenario != "missing-binding")
            Assert.IsTrue(await binds.BindAsync("workflow", "1", part + ":artifact.continuation", input.OperationId));
        Assert.IsTrue(await binds.BindAsync("workflow", "1", part + ":artifact.publish", published.OperationId));
        var continuation = new AcquisitionPlanContinueFilesConfig([new(target, artifactInput)]);
        var recorded = artifactInput;
        if (scenario == "task-id") recorded = recorded with {TaskId = task.Id + 1};
        if (scenario == "previous-run") recorded = recorded with {PreviousRunId = 3};
        if (scenario == "artifact-id") recorded = recorded with {ArtifactId = published.Id};
        if (scenario == "operation-id") recorded = recorded with {OperationId = published.OperationId};
        if (scenario == "fingerprint") recorded = recorded with {Fingerprint = new string('f', 64)};
        var snapshot = WorkflowRunPlans.Serialize(new() {Name = "fixture", TriggerKind = "manual", Nodes = [
            new() {NodeId = "files.continue", Kind = AcquisitionPlanContinueFilesActivity.ActivityKind, ConfigJson = JsonSerializer.Serialize(continuation, Json)},
            new() {NodeId = "plan.process", Kind = AcquisitionPlanProcessingActivity.ActivityKind, ConfigJson = JsonSerializer.Serialize(config with {ContinueFrom = recorded}, Json)}]});
        var block = new WorkflowOutputBlock {Kind = "artifact", LabelKey = "acquisition.plan.continueFiles",
            Key = (scenario == "wrong-continuation-target" ? "other" : part) + ":continuation", ArtifactId = scenario == "conflicting-receipt" ? published.Id : input.Id, OperationId = input.OperationId,
            Directory = scenario == "wrong-receipt-root" ? userFolder : input.RootDirectory, State = "reused",
            TotalBytes = input.Manifest.Entries.Sum(x => x.Length), FileCount = input.Manifest.Entries.Count};
        var blocks = JsonSerializer.Serialize(scenario == "duplicate-output-key" ? new[] {block, block} : [block], Json);
        for (var i = 1; i <= (scenario == "ambiguous-predecessor" ? 2 : 1); i++)
            db.Set<WorkflowNodeExecutionDbModel>().Add(new() {WorkflowRunId = 1, NodeId = "files.continue",
                Kind = AcquisitionPlanContinueFilesActivity.ActivityKind, StepIndex = 0, Attempt = i, Status = scenario == "failed-predecessor" ? "failed" : "succeeded",
                InputCount = 1, OutputCount = 1, OutputsJson = blocks, OutputsTruncated = scenario == "truncated-predecessor", StartedAt = DateTime.UtcNow.AddMinutes(-4), CompletedAt = DateTime.UtcNow.AddMinutes(-3)});
        db.Set<WorkflowNodeExecutionDbModel>().Add(new() {WorkflowRunId = 1, NodeId = "plan.process", Kind = AcquisitionPlanProcessingActivity.ActivityKind,
            StepIndex = 1, Attempt = 1, Status = "failed", InputCount = 1, OutputCount = 0, FailedCount = 1,
            StartedAt = DateTime.UtcNow.AddMinutes(-3), CompletedAt = DateTime.UtcNow.AddMinutes(-2)});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var wrapped = JsonSerializer.Serialize(new {type = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name, item = workset}, Json);
        var legacy = scenario is "cancelled-legacy-prompt" or "unexpected-wait-reason" or "still-waiting";
        var prompt = legacy ? JsonSerializer.Serialize(new {targetId = part, knownStepId = "inner", reason = "PasswordUnknown"}) : null;
        var waitReason = scenario is "unexpected-wait-reason" or "wrong-reason-without-prompt" ? "foreign.wait" : legacy ? "cloud.fileProcessing" : null;
        await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, scenario == "still-waiting" ? WorkflowRunStatus.Waiting : legacy ? WorkflowRunStatus.Cancelled : WorkflowRunStatus.Failed)
            .SetProperty(r => r.PlanSnapshotJson, snapshot).SetProperty(r => r.PayloadJson, JsonSerializer.Serialize(workset, Json)).SetProperty(r => r.CurrentItemJson, wrapped)
            .SetProperty(r => r.CurrentStepIndex, 1).SetProperty(r => r.WaitReason, waitReason).SetProperty(r => r.WaitPromptJson, prompt));
        await db.ArtifactOperations.Where(o => o.Id == key).ExecuteUpdateAsync(q => q.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddHours(-3)));
        if (scenario == "foreign-sibling")
            await store.ClaimAsync(new(ArtifactManifestFiles.Hash("other-operation"), "other", "files.process", input.Fingerprint), "workflow:3:other", TimeSpan.FromMinutes(10));
        if (scenario is "purged" or "missing-purge-receipt")
        {
            var purge = await new ArchiveQuarantineService(store, root, new Storage()).DeleteAfterDeliveryAsync(input.Id, [published.Id]);
            Assert.IsTrue(purge.Completed, purge.Message);
            Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(input.Id))!.State);
            if (scenario == "missing-purge-receipt") File.Delete(Path.Combine(root, "file-processing", "quarantine-receipts", purge.ReceiptId + ".json"));
        }
        if (scenario == "missing-source") File.Delete(input.Files.Single());
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity([1,2]), new(),
            new(db, store, new(db, store, executor, new(extraction), root, resolver), cleanup, new Storage(), workspaces: resolver));
        var preview = await service.PreviewAsync(new([task.Id], CleanupFiles: true, RetryRetainedFiles: true));
        Assert.AreNotEqual("CLEANUP_PREVIEW_FAILED", preview.Rows.Single().ReasonCode, scenario);
        var covered = (preview.Rows.Single().Cleanup?.Groups ?? []).Where(g => g.Eligible && ownFiles.Any(p => g.Files.Any(f => f.Path == p))).ToArray();
        var canVerifyPrivateFiles = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() &&
            RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 or Architecture.X86 or Architecture.X64;
        if (canVerifyPrivateFiles && scenario is ("failed-no-prompt" or "cancelled-legacy-prompt" or "purged" or "missing-source" or "renewed-lease-after-preview" or "changed-receipt-after-preview" or "replaced-receipt-after-preview"))
        {
            Assert.AreEqual(1, covered.Length, scenario);
            var group = covered[0];
            CollectionAssert.AreEquivalent(ownFiles, group.Files.Select(f => f.Path).ToArray());
            if (scenario == "renewed-lease-after-preview")
                await db.ArtifactOperations.Where(o => o.Id == key).ExecuteUpdateAsync(q => q.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddHours(1)));
            if (scenario == "changed-receipt-after-preview")
                await db.WorkflowNodeExecutions.Where(n => n.WorkflowRunId == 1 && n.StepIndex == 0).ExecuteUpdateAsync(q => q.SetProperty(n => n.OutputsTruncated, true));
            if (scenario == "replaced-receipt-after-preview")
            {
                var evidence = await db.WorkflowNodeExecutions.AsNoTracking().SingleAsync(n => n.WorkflowRunId == 1 && n.StepIndex == 0);
                await db.WorkflowNodeExecutions.Where(n => n.Id == evidence.Id).ExecuteDeleteAsync();
                evidence.Id = 0;
                db.WorkflowNodeExecutions.Add(evidence);
                await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            }
            await service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true, SelectedCleanupDirectories:
                new Dictionary<int,IReadOnlyList<string>> {[task.Id] = [group.Directory]}));
            Assert.AreEqual(scenario is "renewed-lease-after-preview" or "changed-receipt-after-preview" or "replaced-receipt-after-preview", ownFiles.Any(File.Exists));
        }
        else
        {
            Assert.AreEqual(0, covered.Length, scenario);
            Assert.IsTrue(ownFiles.All(File.Exists));
        }
        Assert.AreEqual(stateText, await File.ReadAllTextAsync(stateFile));
        Assert.IsTrue(published.Files.All(File.Exists));
    }
}

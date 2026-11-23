using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
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
    private sealed record ProcessingFixture(
        Bakabase.Modules.Acquisition.Abstractions.Models.Db.AcquisitionTaskDbModel Task,
        WorkflowRunDbModel Run, DownloadWorkset Workset, ArtifactRecord Input, FileProcessingPlan Plan,
        string Intent, string OperationId, string Checkpoint, string AppData, FileProcessingWorkspaceResolver Workspaces);

    private async Task<ProcessingFixture> SeedProcessingStorage(string? sharingFile = null, bool localContinuation = false, bool includeDirectories = false, string[]? volumes = null)
    {
        var (task, run, workset) = await Seed(AcquisitionPlanProcessingActivity.ActivityKind, WorkflowRunStatus.Waiting, null);
        var appData = Path.Combine(_root, "processing-metadata"); Directory.CreateDirectory(appData);
        var workspaces = new FileProcessingWorkspaceResolver(appData, new UserStoragePolicy(false));
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var file = Path.Combine(_root, "original.bin"); await File.WriteAllTextAsync(file, "unchanged original bytes");
        var download = DownloadArtifactCoordinator.DownloadIntent(workset.Targets[0], workset.OutputDirectory, workspaces);
        var directories = includeDirectories ? new[] {Directory.CreateDirectory(Path.Combine(_root, "empty-input-directory")).FullName} : [];
        var input = await store.AdoptAsync(new(download, download, "download", DownloadArtifactCoordinator.Source(workset.Targets[0])),
            new(_root, volumes ?? [file], "download", true, Directories: includeDirectories ? directories : null), "download-owner");
        var plan = new FileProcessingPlan {Requirement = "required", Steps =
            [new() {Id = "rename", Op = "renameFile", Selector = "original.bin", TargetName = "final.bin"}]};
        if (volumes != null) plan = plan with {Steps = [new() {Id = "rename", Op = "extractArchive"}]};
        var serialized = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        var target = workset.Targets[0] with {DownloadArtifactId = input.Id, DownloadOperationId = input.OperationId,
            DownloadDirectory = _root, Files = [.. volumes ?? [file]], ProcessingPlanJson = serialized,
            ProcessingWaitReason = "StorageUnavailable", Problem = "StorageUnavailable"};
        workset = workset with {Targets = [target], SourceRefs = [new("acquisition", "task", task.Id.ToString(), "1")]};
        Bakabase.Modules.Acquisition.Abstractions.Models.Domain.AcquisitionPlanArtifactInput? continuation = null;
        if (localContinuation)
        {
            var previous = new WorkflowRunDbModel {Status = WorkflowRunStatus.Cancelled,
                StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
                CurrentItemJson = WorkflowItemSnapshot.Capture(workset)};
            _db.WorkflowRuns.Add(previous); await _db.SaveChangesAsync();
            _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = previous.Id, Module = "acquisition", EntityKind = "task",
                EntityId = task.Id.ToString(), Revision = "1"});
            _db.ArtifactOperationBindings.Add(new() {Id = Guid.NewGuid().ToString("N"), OwnerKind = "workflow",
                OwnerId = previous.Id.ToString(), Slot = "part:artifact.download", OperationId = input.OperationId});
            continuation = new(task.Id, previous.Id, input.Id, input.OperationId, input.Fingerprint);
        }
        var config = new AcquisitionPlanProcessingConfig(target.Id, plan, "rename") {ContinueFrom = continuation};
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "Local recovery", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [.. (localContinuation ? new WorkflowRunPlanNode[] {new() {NodeId = "continue", Kind = AcquisitionPlanContinueFilesActivity.ActivityKind}} : []),
                new() {NodeId = "process", Kind = AcquisitionPlanProcessingActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(config, WorkflowJson.Options)}, new() {NodeId = "deliver", Kind = "fs.publishArtifacts"}]});
        run.CurrentStepIndex = localContinuation ? 1 : 0;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        run.WaitReason = "cloud.fileProcessing"; run.WaitingSince = DateTime.UtcNow; run.CompletedAt = null;
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString(), Revision = "1"});
        _db.ArtifactOperationBindings.Add(new() {Id = Guid.NewGuid().ToString("N"), OwnerKind = "workflow",
            OwnerId = run.Id.ToString(), Slot = localContinuation ? "part:artifact.continuation" : "part:artifact.download", OperationId = input.OperationId});
        await _db.SaveChangesAsync();
        var intent = AcquisitionPlanProcessingActivity.Intent(workset, target, plan, "rename", workspaces, continuation);
        var operation = FileArtifactProcessor.OperationKey(intent, input, plan, null, new());
        var claim = await store.ClaimAsync(new(operation, intent, "files.process", input.Fingerprint), $"workflow:{run.Id}:part", TimeSpan.FromMinutes(2));
        Assert.AreEqual(ArtifactClaimStatus.Acquired, claim.Status);
        _ = await workspaces.ResolveAsync("operations", operation, 1, _root, null, default);
        var parent = await workspaces.ResolveAsync("checkpoints", FileArtifactProcessor.CheckpointKey(input, plan), 1, _root, null, default);
        var checkpointIdentity = FileProcessingPlanExecutor.CheckpointIdentity(plan, input.Files, workspaces.IdentityPath);
        if (directories.Length > 0)
            checkpointIdentity = ArtifactManifestFiles.Hash(checkpointIdentity + "\ndirectories-v1\n" + JsonSerializer.Serialize(
                directories.OrderBy(p => p, StringComparer.Ordinal).Select(p => Path.GetRelativePath(_root, p).Replace('\\', '/')).ToArray()));
        var checkpoint = Path.Combine(parent, checkpointIdentity);
        Directory.CreateDirectory(checkpoint);
        await File.WriteAllTextAsync(Path.Combine(checkpoint, "state.json"), JsonSerializer.Serialize(new FileProcessingPlanExecutor.State(), WorkflowJson.Options));
        var message = "No space left on device";
        if (sharingFile != null)
        {
            var path = Path.Combine(checkpoint, sharingFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "retained checkpoint");
            message = $"The process cannot access the file '{path}' because it is being used by another process.";
        }
        run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = "part", reason = "StorageUnavailable", message,
            extractionPlanJson = serialized, knownStepId = "rename"}, WorkflowJson.Options);
        await _db.SaveChangesAsync();
        _diagnostics = new(_db, new UserStoragePolicy(false), store, workspaces: workspaces);
        return new(task, run, workset, input, plan, intent, operation, checkpoint, appData, workspaces);
    }

    [TestMethod]
    [DataRow("missingParts", true)]
    [DataRow("unopenedGroup", true)]
    [DataRow("incorrectBinding", false)]
    [DataRow("startedStaging", false)]
    [DataRow("existingOutput", false)]
    public async Task FailedRegisteredMultipartStepCanRetryTheSameAttemptOnlyWithVerifiedPrivateInputs(string scenario, bool eligible)
    {
        var files = Artifacts.FileProcessingMaskedSevenZipTests.MakeVolumes(_root);
        var f = await SeedProcessingStorage(volumes: files);
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var current = (await store.GetOperationAsync(f.OperationId))!;
        var error = scenario == "missingParts" ? "The archive volume set for bundle.7z.002 has missing parts." :
            "Archive structure discovery requires a regular file.";
        Assert.IsTrue(await store.FailAsync(new(current.Id, current.LeaseToken!), error));
        f.Run.ErrorMessage = error; f.Run.Status = WorkflowRunStatus.Failed; f.Run.WaitPromptJson = null;
        f.Task.Status = Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionStatus.Failed;
        var workset = f.Workset with {Targets = [f.Workset.Targets[0] with {Problem = null, ProcessingWaitReason = null}]};
        f.Run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset); await _db.SaveChangesAsync();
        var inputs = Path.Combine(f.Checkpoint, "inputs");
        foreach (var path in files)
        {
            var dest = Path.Combine(inputs, Path.GetRelativePath(_root, path));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(path, dest);
        }
        var state = new FileProcessingPlanExecutor.State {InputManifest = f.Input.Manifest};
        if (scenario != "missingParts")
        {
            var key = FileProcessingPlanExecutor.StepKey("rename") + "-0";
            var output = Path.Combine(f.Checkpoint, "outputs", key);
            var binding = files.Select(path => "inputs/" + Path.GetRelativePath(_root, path).Replace('\\', '/')).ToList();
            if (scenario == "incorrectBinding") binding.Reverse();
            state.Operations[key] = new() {OutputDirectory = output, ArchiveInputs = binding};
            if (scenario == "existingOutput") Directory.CreateDirectory(output);
            if (scenario == "startedStaging") Directory.CreateDirectory(output + ".pending");
        }
        await File.WriteAllTextAsync(Path.Combine(f.Checkpoint, "state.json"), JsonSerializer.Serialize(state, WorkflowJson.Options));
        var diagnosis = await _diagnostics.DiagnoseAsync(f.Task, f.Run, default);
        Assert.AreEqual(eligible, diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        if (!eligible) return;
        Assert.AreEqual("retry", diagnosis.Row.Action);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(diagnosis.SignalJson, WorkflowJson.Options);
        Assert.IsTrue(signal!.StorageRecovery!.RepairArchiveVolumes);
        Assert.AreEqual(1, signal.StorageRecovery.Attempt);
        var tail = Path.Combine(inputs, Path.GetRelativePath(_root, files[^1]));
        await using (var writer = File.OpenWrite(tail)) {writer.Seek(-1, SeekOrigin.End); writer.WriteByte(0xff);}
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row.CanContinue);
        Assert.AreEqual(1, (await store.GetOperationAsync(f.OperationId))!.Attempt);
    }

    [DataTestMethod]
    [DataRow("automatic", "resume")]
    [DataRow("localOnly", "resume")]
    [DataRow("automatic", "locatorChanged")]
    [DataRow("localOnly", "inputChanged")]
    [DataRow("automatic", "unknownOtherWorkspace")]
    [DataRow("automatic", "boundNextPart")]
    [DataRow("localOnly", "unboundNextPart")]
    [DataRow("localOnly", "excludedOtherPart")]
    [DataRow("automatic", "unboundDelivery")]
    public async Task BatchResumesReviewedLocalAttemptUsingPinnedWorkspacesAndFrozenDelivery(string mode, string scenario)
    {
        var f = await SeedProcessingStorage();
        var unknown = new FileProcessingPlan {Requirement = "unknown"};
        var plans = new AcquisitionPlanService(_db, [], null!, null!, null!, new UserStoragePolicy(false));
        f.Task.PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Title = "Task", Parts =
            [new() {Id = "part", Processing = unknown}, new() {Id = "other"}]}, WorkflowJson.Options);
        var destination = Path.Combine(_root, "frozen-delivery");
        var other = Directory.CreateDirectory(Path.Combine(_root, "other-volume")).FullName;
        var end = new FileArtifactPublicationConfig {TargetDirectories = scenario == "unboundDelivery" ? null :
            new Dictionary<string, string> {{"part", destination}, {"other", Path.Combine(destination, "other")}}};
        var frozen = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
        var node = frozen.Nodes.First();
        f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(frozen with {Nodes = [
            node with {ConfigJson = JsonSerializer.Serialize(new AcquisitionPlanProcessingConfig("part", unknown), WorkflowJson.Options)},
            new() {NodeId = "other-assess", Kind = AssessFileArtifactDeliveryActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(new FileArtifactDeliveryAssessmentConfig("other"), WorkflowJson.Options)},
            new() {NodeId = "delivery", Kind = "fs.publishArtifacts", ConfigJson = JsonSerializer.Serialize(end, WorkflowJson.Options)}]});
        f.Run.CurrentItemJson = WorkflowItemSnapshot.Capture(f.Workset with {Targets = [f.Workset.Targets.Single(),
            f.Workset.Targets.Single() with {Id = "other", Excluded = scenario == "excludedOtherPart",
                StoragePolicy = scenario is "unknownOtherWorkspace" or "excludedOtherPart" or "boundNextPart" or "unboundNextPart" ? null :
                    new ArtifactStoragePolicy {ProcessingWorkspaceDirectory = other}}]});
        string? otherCheckpointRoot = null;
        if (scenario is "boundNextPart" or "unboundNextPart")
        {
            var store = _services.GetRequiredService<IArtifactOperationStore>();
            var inputRoot = Directory.CreateDirectory(Path.Combine(other, ".bakabase-work", "fixture", "files")).FullName;
            var file = Path.Combine(inputRoot, "second.bin");
            await File.WriteAllTextAsync(file, "other part");
            var input = await store.AdoptAsync(new("other-download", "other-download", "download", "other-source"),
                new(inputRoot, [file], "download"), "seed");
            var previous = new WorkflowRunDbModel {Status = WorkflowRunStatus.Cancelled};
            _db.WorkflowRuns.Add(previous); await _db.SaveChangesAsync();
            _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = previous.Id, Module = "acquisition", EntityKind = "task",
                EntityId = f.Task.Id.ToString(), Revision = "1"});
            foreach (var ownerId in scenario == "boundNextPart" ? new[] {previous.Id, f.Run.Id} : new[] {previous.Id})
                _db.ArtifactOperationBindings.Add(new() {Id = Guid.NewGuid().ToString("N"), OwnerKind = "workflow",
                    OwnerId = ownerId.ToString(), OperationId = input.OperationId, Slot = "other:artifact.continuation"});
            var config = new AcquisitionPlanProcessingConfig("other", f.Plan, "rename")
                {ContinueFrom = new(f.Task.Id, previous.Id, input.Id, input.OperationId, input.Fingerprint)};
            var snapshot = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
            f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(snapshot with {Nodes = snapshot.Nodes.Take(1)
                .Append(new() {NodeId = "other-process", Kind = AcquisitionPlanProcessingActivity.ActivityKind,
                    ConfigJson = JsonSerializer.Serialize(config, WorkflowJson.Options)}).Concat(snapshot.Nodes.Skip(1)).ToArray()});
            otherCheckpointRoot = Directory.CreateDirectory(Path.Combine(_root, "previous-checkpoint-volume")).FullName;
            await f.Workspaces.ResolveAsync("checkpoints", FileArtifactProcessor.CheckpointKey(input, f.Plan), 1,
                input.RootDirectory, otherCheckpointRoot, default);
        }
        var prompt = JsonNode.Parse(f.Run.WaitPromptJson!)!;
        prompt["knownStepId"] = null;
        prompt["message"] = StorageWriteGuard.ReserveLowCode;
        f.Run.WaitPromptJson = prompt.ToJsonString();
        await _db.SaveChangesAsync();
        var host = new ProcessingRecoveryHost();
        var options = _services.GetRequiredService<IBOptions<AcquisitionOptions>>();
        var service = new AcquisitionLocalRecoveryBatchService(_db, new(_db, _diagnostics, new(), host),
            _diagnostics, null!, plans, options, new UserStoragePolicy(false), new PinnedBatchCapacity(), new());
        var preview = await service.PreviewAsync(new([f.Task.Id], mode));
        var row = preview.Rows.Single();
        if (scenario is "unknownOtherWorkspace" or "unboundNextPart" or "unboundDelivery")
        {
            Assert.IsFalse(row.CanQueue);
            Assert.AreEqual(scenario != "unboundDelivery" ? "RECOVERY_LOCAL_STORAGE_REQUIRED" : "LOCAL_BATCH_STORAGE_REVIEW_REQUIRED", row.ReasonCode);
            return;
        }
        Assert.IsTrue(row.CanQueue, row.ReasonCode);
        Assert.AreEqual("workflow", row.RecoveryScope);
        Assert.AreEqual(destination, row.DeliveryDirectory);
        var paths = preview.Volumes.SelectMany(v => v.Paths).ToArray();
        Assert.IsTrue(paths.Contains(_root)); Assert.AreEqual(scenario != "excludedOtherPart", paths.Contains(other));
        if (otherCheckpointRoot != null) Assert.IsTrue(paths.Contains(otherCheckpointRoot));
        if (scenario == "inputChanged")
        {
            // Even a full local preview reads only metadata. Dispatch verifies the original bytes.
            await File.WriteAllTextAsync(f.Input.Files[0], new string('x', (int)new FileInfo(f.Input.Files[0]).Length));
            Assert.IsTrue((await service.PreviewAsync(new([f.Task.Id], mode))).Rows.Single().CanQueue);
        }
        var batch = await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        if (scenario == "locatorChanged")
        {
            var chosen = Directory.GetFiles(f.AppData, "operations-" + f.OperationId + "-1.json", SearchOption.AllDirectories).Single();
            File.Move(chosen, chosen + ".saved");
        }
        await service.TickAsync();
        if (scenario is "locatorChanged" or "inputChanged")
        {
            Assert.AreEqual(0, host.Calls);
            var stopped = (await service.GetAsync(batch.BatchId)).Rows.Single();
            Assert.AreEqual("attention", stopped.Status);
            Assert.AreEqual(scenario == "locatorChanged" ? "RECOVERY_CHECKPOINT_UNSUPPORTED" : "RECOVERY_FILES_UNVERIFIED", stopped.ReasonCode);
        }
        else
        {
            Assert.AreEqual(1, host.Calls);
            Assert.AreEqual(f.Run.Id, host.RunId);
            var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(host.Signal!, WorkflowJson.Options)!;
            Assert.AreEqual(new FileArtifactProcessingRecovery(f.OperationId, 1), signal.StorageRecovery);
            Assert.IsNull(signal.ExtractionPlanJson);
        }
        Assert.AreEqual(f.Task.PlanJson, (await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanJson);
        Assert.AreEqual(1, (await _db.ArtifactOperations.AsNoTracking().SingleAsync(o => o.Id == f.OperationId)).Attempt);
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    private sealed class PinnedBatchCapacity : IAcquisitionBatchCapacityReader
    {
        public StorageVolumeCapacity Measure(string path) => new(path, long.MaxValue / 2, long.MaxValue);
        public string Identity(StorageVolumeCapacity capacity) => capacity.VolumePath;
    }

    [TestMethod]
    public async Task ProcessingStoragePreviewAndApplyPreserveAttemptAndOnlyResumeTheTypedCurrentTarget()
    {
        var f = await SeedProcessingStorage();
        var before = JsonSerializer.Serialize(await _db.ArtifactOperations.AsNoTracking().OrderBy(o => o.Id).ToListAsync());
        var history = f.Task.PlanHistoryJson; var purchases = f.Task.PurchaseRecordJson;
        var host = new ProcessingRecoveryHost();
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([f.Task.Id]));
        Assert.AreEqual(1, preview.ContinuableCount);
        Assert.AreEqual("acquisition.plan.process", preview.Rows.Single().CurrentStepKind);
        Assert.AreEqual("RECOVERY_FILES_VERIFIED", preview.Rows.Single().ReasonCode);
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.ArtifactOperations.AsNoTracking().OrderBy(o => o.Id).ToListAsync()));
        var applied = await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, applied.ContinuedTaskIds.ToArray());
        Assert.AreEqual(f.Run.Id, host.RunId); Assert.IsTrue(host.Waiting);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.AreEqual("part", signal.TargetId); Assert.IsNull(signal.Password); Assert.IsNull(signal.ExtractionPlanJson); Assert.IsNull(signal.ArchivePolicy);
        Assert.AreEqual(f.OperationId, signal.StorageRecovery?.OperationId); Assert.AreEqual(1, signal.StorageRecovery?.Attempt);
        Assert.AreEqual(history, f.Task.PlanHistoryJson); Assert.AreEqual(purchases, f.Task.PurchaseRecordJson);
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync()); Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
        await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id])); Assert.AreEqual(1, host.Calls);
        Assert.AreEqual(1, (await _db.ArtifactOperations.AsNoTracking().SingleAsync(o => o.Id == f.OperationId)).Attempt);
    }

    [TestMethod]
    public async Task ProcessingStoragePreviewIsMetadataOnlyButApplyRejectsSameLengthChangedContent()
    {
        var f = await SeedProcessingStorage();
        await File.WriteAllTextAsync(f.Input.Files[0], new string('x', (int)new FileInfo(f.Input.Files[0]).Length));
        var host = new ProcessingRecoveryHost(); var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([f.Task.Id]));
        Assert.AreEqual(1, preview.ContinuableCount, "Preview must not hash file payloads.");
        var result = await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", result.Skipped.Single().ReasonCode);
        Assert.AreEqual(ArtifactLocationState.Available, (await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync(a => a.Id == f.Input.Id)).State,
            "Read-only validation must not mutate artifact state through ResolveById.");
    }

    [TestMethod]
    public async Task ProcessingStorageSharingViolationStaysWaitingUntilTheExactCheckpointFileUnlocks()
    {
        var f = await SeedProcessingStorage("outputs/held.zip");
        using (var held = new FileStream(Path.Combine(f.Checkpoint, "outputs/held.zip"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.IsFalse((await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row.CanContinue);
        Assert.IsTrue((await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row.CanContinue);
        Assert.AreEqual(WorkflowRunStatus.Waiting, f.Run.Status);
    }

    [DataTestMethod]
    [DataRow("checkpoint", true)]
    [DataRow("outside", false)]
    [DataRow("unmapped", false)]
    public async Task ProcessingStorageSharingViolationResolvesHistoricalPathsOnlyInsideTheCurrentCheckpoint(
        string destination, bool canContinue)
    {
        var f = await SeedProcessingStorage("outputs/held.zip");
        var oldRoot = Path.Combine(_root, "retired-volume");
        var currentRoot = destination == "outside" ? Path.Combine(_root, "foreign-checkpoint") : f.Checkpoint;
        var mappedFile = Path.Combine(currentRoot, "outputs/held.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(mappedFile)!);
        if (destination == "outside") await File.WriteAllTextAsync(mappedFile, "unrelated retained file");
        if (destination != "unmapped")
            await File.WriteAllTextAsync(Path.Combine(f.AppData, "artifact-workspaces", "relocations.json"),
                JsonSerializer.Serialize(new FileProcessingWorkspaceResolver.Relocations(1,
                    [new(oldRoot, currentRoot)]), WorkflowJson.Options));
        f.Run.WaitPromptJson = f.Run.WaitPromptJson!.Replace(
            JsonSerializer.Serialize(Path.Combine(f.Checkpoint, "outputs/held.zip"))[1..^1],
            JsonSerializer.Serialize(Path.Combine(oldRoot, "outputs/held.zip"))[1..^1]);
        await _db.SaveChangesAsync();
        var historicalPrompt = f.Run.WaitPromptJson;
        Assert.AreEqual(canContinue, (await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row.CanContinue);
        if (canContinue)
        {
            using var held = new FileStream(mappedFile, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.IsFalse((await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row.CanContinue,
                "A relocated path must still pass the exclusive-access check.");
        }
        Assert.AreEqual(historicalPrompt, f.Run.WaitPromptJson);
        Assert.AreEqual(WorkflowRunStatus.Waiting, f.Run.Status);
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("PasswordUnknown")]
    [DataRow("ExtractionPlanUnknown")]
    [DataRow("MissingVolume")]
    [DataRow("WaitingShared")]
    public async Task ProcessingStorageRecoveryNeverApprovesOtherWaitingReasons(string reason)
    {
        var f = await SeedProcessingStorage();
        f.Run.WaitPromptJson = f.Run.WaitPromptJson!.Replace("StorageUnavailable", reason);
        var row = (await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row;
        Assert.IsFalse(row.CanContinue);
        Assert.IsNull(row.CurrentProblemCode, "Other waits must not be presented as storage errors.");
    }

    [TestMethod]
    public async Task ProcessingStorageRecoveryAllowsLaterLocalStepsOfOtherDynamicParts()
    {
        var f = await SeedProcessingStorage();
        var plan = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
        f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with {Nodes = [.. plan.Nodes,
            new() {NodeId = "another-part-process", Kind = AcquisitionPlanProcessingActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(new AcquisitionPlanProcessingConfig("another-part", f.Plan, "rename"), WorkflowJson.Options)},
            new() {NodeId = "another-part-deliver", Kind = "fs.publishArtifacts"}]});
        Assert.IsTrue((await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row.CanContinue);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProcessingStorageRecoveryKeepsLaterAssessmentAndResumesTheSameOriginalAttempt(bool localContinuation)
    {
        var f = await SeedProcessingStorage(localContinuation: localContinuation);
        var plan = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
        f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with {Nodes = [.. plan.Nodes.SkipLast(1),
            new() {NodeId = "assess", Kind = AssessFileArtifactDeliveryActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(new FileArtifactDeliveryAssessmentConfig("part"), WorkflowJson.Options)},
            plan.Nodes.Last()]});
        await _db.SaveChangesAsync();
        var beforeSnapshot = f.Run.PlanSnapshotJson;
        var beforeHistory = f.Task.PlanHistoryJson;
        var operationCount = await _db.ArtifactOperations.CountAsync();
        var host = new ProcessingRecoveryHost();
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([f.Task.Id]));
        Assert.AreEqual(1, preview.ContinuableCount, preview.Rows.Single().ReasonCode);
        Assert.AreEqual("RECOVERY_FILES_VERIFIED", preview.Rows.Single().ReasonCode);
        var applied = await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, applied.ContinuedTaskIds.ToArray(),
            JsonSerializer.Serialize(applied));
        Assert.AreEqual(f.Run.Id, host.RunId);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.AreEqual(new FileArtifactProcessingRecovery(f.OperationId, 1), signal.StorageRecovery);
        Assert.IsNull(signal.ExtractionPlanJson); Assert.IsNull(signal.Password);
        Assert.AreEqual(beforeSnapshot, f.Run.PlanSnapshotJson); Assert.AreEqual(beforeHistory, f.Task.PlanHistoryJson);
        Assert.AreEqual(operationCount, await _db.ArtifactOperations.CountAsync());
        Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync()); Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("download.enqueueSupported")]
    [DataRow("baidu.transfer")]
    [DataRow("unknown.activity")]
    public async Task ProcessingStorageRecoveryAssessmentDoesNotPermitLaterSourceOrUnknownActions(string kind)
    {
        var f = await SeedProcessingStorage(localContinuation: true);
        var plan = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
        f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with {Nodes = [.. plan.Nodes,
            new() {NodeId = "assess", Kind = AssessFileArtifactDeliveryActivity.ActivityKind},
            new() {NodeId = "unapproved", Kind = kind}]});
        var row = (await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row;
        Assert.IsFalse(row.CanContinue); Assert.AreEqual("RECOVERY_NODE_UNSUPPORTED", row.ReasonCode);
        Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync()); Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("promptTarget")]
    [DataRow("promptStep")]
    [DataRow("promptPlan")]
    [DataRow("taskRevision")]
    [DataRow("owner")]
    [DataRow("binding")]
    [DataRow("operationOwner")]
    [DataRow("failedOperation")]
    [DataRow("attempt")]
    [DataRow("inputMissing")]
    [DataRow("inputLength")]
    [DataRow("checkpointMissing")]
    [DataRow("foreignLockPath")]
    [DataRow("futureDownload")]
    public async Task ProcessingStorageRecoveryRejectsUnprovenOrChangedCheckpointIdentity(string change)
    {
        var f = await SeedProcessingStorage();
        switch (change)
        {
            case "promptTarget": f.Run.WaitPromptJson = f.Run.WaitPromptJson!.Replace("\"targetId\":\"part\"", "\"targetId\":\"other\""); break;
            case "promptStep": f.Run.WaitPromptJson = f.Run.WaitPromptJson!.Replace("\"knownStepId\":\"rename\"", "\"knownStepId\":\"other\""); break;
            case "promptPlan": f.Run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = "part", reason = "StorageUnavailable", knownStepId = "rename", extractionPlanJson = "{}"}); break;
            case "taskRevision": f.Task.PlanRevision++; break;
            case "owner": _db.Set<WorkflowRunSourceDbModel>().RemoveRange(_db.Set<WorkflowRunSourceDbModel>()); break;
            case "binding": _db.ArtifactOperationBindings.RemoveRange(_db.ArtifactOperationBindings); break;
            case "operationOwner": (await _db.ArtifactOperations.SingleAsync(o => o.Id == f.OperationId)).LeaseOwner = "workflow:999:part"; break;
            case "failedOperation": (await _db.ArtifactOperations.SingleAsync(o => o.Id == f.OperationId)).State = ArtifactOperationState.Failed; break;
            case "attempt": (await _db.ArtifactOperations.SingleAsync(o => o.Id == f.OperationId)).Attempt++; break;
            case "inputMissing": File.Delete(f.Input.Files[0]); break;
            case "inputLength": await File.AppendAllTextAsync(f.Input.Files[0], "changed"); break;
            case "checkpointMissing": File.Delete(Path.Combine(f.Checkpoint, "state.json")); break;
            case "foreignLockPath": f.Run.WaitPromptJson = JsonSerializer.Serialize(new {targetId = "part", reason = "StorageUnavailable", knownStepId = "rename", extractionPlanJson = JsonSerializer.Serialize(f.Plan, WorkflowJson.Options), message = $"The process cannot access the file '{f.Input.Files[0]}' because it is being used by another process."}); break;
            case "futureDownload": var plan = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!; f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with {Nodes = [.. plan.Nodes, new() {NodeId = "bad", Kind = "download.enqueueSupported"}]}); break;
        }
        await _db.SaveChangesAsync();
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(f.Task, f.Run, default)).Row.CanContinue, change);
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task ProcessingStorageApplyRechecksThePlanAndTheLeaseInsideTheRunGate()
    {
        var f = await SeedProcessingStorage();
        var host = new ProcessingRecoveryHost {BeforeValidation = async () =>
        {
            await _db.ArtifactOperations.Where(o => o.Id == f.OperationId).ExecuteUpdateAsync(s => s.SetProperty(o => o.State, ArtifactOperationState.Failed));
        }};
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([f.Task.Id])); Assert.AreEqual(1, preview.ContinuableCount);
        var result = await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        Assert.AreEqual(0, host.Calls); Assert.AreEqual("RECOVERY_CHECKPOINT_UNSUPPORTED", result.Skipped.Single().ReasonCode);
    }

    [TestMethod]
    public async Task ProcessingStorageFenceResumesTheOriginalCheckpointAndExpiredAttemptWithoutArchiveOrDownloadCalls()
    {
        var f = await SeedProcessingStorage();
        var diagnosis = await _diagnostics.DiagnoseAsync(f.Task, f.Run, default, verifyContent: true);
        Assert.IsTrue(diagnosis.Row.CanContinue);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(diagnosis.SignalJson, WorkflowJson.Options)!;
        await _db.ArtifactOperations.Where(o => o.Id == f.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var archive = new NoArchiveForStorageRecovery();
        var processor = new FileArtifactProcessor(store, new FileProcessingPlanExecutor(archive), new RecursiveArchiveExecutor(archive),
            () => f.AppData, _services.GetRequiredService<IServiceScopeFactory>(), storage: new UserStoragePolicy(false));
        var result = await processor.ExecuteAsync(f.Intent, f.Input, JsonSerializer.Serialize(f.Plan, WorkflowJson.Options),
            null, new(), $"workflow:{f.Run.Id}:part", null, null, null, default, false, "rename", signal.StorageRecovery);
        Assert.IsNotNull(result.Artifact, result.Message);
        Assert.AreEqual(f.OperationId, result.Artifact.OperationId);
        Assert.AreEqual(1, (await store.GetOperationAsync(f.OperationId))!.Attempt);
        Assert.IsTrue(result.Artifact.RootDirectory.StartsWith(f.Checkpoint + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.AreEqual("unchanged original bytes", await File.ReadAllTextAsync(f.Input.Files.Single()));
        Assert.AreEqual("unchanged original bytes", await File.ReadAllTextAsync(result.Artifact.Files.Single()));
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync()); Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
    }

    [DataTestMethod]
    [DataRow("failed")]
    [DataRow("unknown")]
    [DataRow("newAttempt")]
    [DataRow("newOwner")]
    [DataRow("missing")]
    public async Task ProcessingStorageExecutionFenceCannotStartAnotherGenerationAfterEnqueue(string change)
    {
        var f = await SeedProcessingStorage();
        var diagnosis = await _diagnostics.DiagnoseAsync(f.Task, f.Run, default);
        Assert.IsTrue(diagnosis.Row.CanContinue);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(diagnosis.SignalJson, WorkflowJson.Options)!;
        var operation = await _db.ArtifactOperations.SingleAsync(o => o.Id == f.OperationId);
        switch (change)
        {
            case "failed": operation.State = ArtifactOperationState.Failed; break;
            case "unknown": operation.State = ArtifactOperationState.Unknown; break;
            case "newAttempt": operation.Attempt++; break;
            case "newOwner": operation.LeaseOwner = "workflow:999:part"; break;
            case "missing": _db.ArtifactOperations.Remove(operation); break;
        }
        await _db.SaveChangesAsync();
        var before = JsonSerializer.Serialize(await _db.ArtifactOperations.AsNoTracking().OrderBy(o => o.Id).ToListAsync());
        var archive = new NoArchiveForStorageRecovery();
        var processor = new FileArtifactProcessor(_services.GetRequiredService<IArtifactOperationStore>(),
            new FileProcessingPlanExecutor(archive), new RecursiveArchiveExecutor(archive), () => f.AppData,
            _services.GetRequiredService<IServiceScopeFactory>(), storage: new UserStoragePolicy(false));
        var result = await processor.ExecuteAsync(f.Intent, f.Input, JsonSerializer.Serialize(f.Plan, WorkflowJson.Options),
            null, new(), $"workflow:{f.Run.Id}:part", null, null, null, default, false, "rename", signal.StorageRecovery);
        Assert.IsNull(result.Artifact); Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.AreEqual(before, JsonSerializer.Serialize(await _db.ArtifactOperations.AsNoTracking().OrderBy(o => o.Id).ToListAsync()));
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    public async Task ProcessingStorageInitialReserveRejectionResumesOnlyOriginalAttempt(bool localContinuation, bool entirePlan, bool includeDirectories)
    {
        var f = await SeedProcessingStorage(localContinuation: localContinuation, includeDirectories: includeDirectories);
        Directory.Delete(f.Checkpoint, true);
        if (entirePlan)
        {
            var plan = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
            var node = plan.Nodes[f.Run.CurrentStepIndex!.Value];
            var config = JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(node.ConfigJson, WorkflowJson.Options)!;
            var revisedNode = node with {ConfigJson = JsonSerializer.Serialize(config with {StopAfterStepId = null}, WorkflowJson.Options)};
            f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with
                {Nodes = plan.Nodes.Select(n => n.NodeId == node.NodeId ? revisedNode : n).ToArray()});
        }
        var archive = new NoArchiveForStorageRecovery();
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var guard = new StorageWriteGuard(new InitialReservePolicy(), _ => ("test", 0L), TimeSpan.FromHours(1));
        var processor = new FileArtifactProcessor(store, new FileProcessingPlanExecutor(archive, writeGuard: guard),
            new RecursiveArchiveExecutor(archive), () => f.AppData, _services.GetRequiredService<IServiceScopeFactory>(),
            storage: new UserStoragePolicy(false));
        await _db.ArtifactOperations.Where(o => o.Id == f.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        var blocked = await processor.ExecuteAsync(f.Intent, f.Input, JsonSerializer.Serialize(f.Plan, WorkflowJson.Options),
            null, new(), $"workflow:{f.Run.Id}:part", null, null, null, default, false, entirePlan ? null : "rename",
            new(f.OperationId, 1));
        Assert.AreEqual("StorageUnavailable", blocked.WaitReason);
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, blocked.Message);
        Assert.IsFalse(Directory.Exists(f.Checkpoint), "Admission stops before the first executor checkpoint is created.");
        var prompt = JsonNode.Parse(f.Run.WaitPromptJson!)!;
        prompt["message"] = blocked.Message;
        if (entirePlan) prompt["knownStepId"] = null;
        f.Run.WaitPromptJson = prompt.ToJsonString(); await _db.SaveChangesAsync();
        var diagnosis = await _diagnostics.DiagnoseAsync(f.Task, f.Run, default, verifyContent: true);
        Assert.IsTrue(diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(diagnosis.SignalJson, WorkflowJson.Options)!;
        Assert.AreEqual(new FileArtifactProcessingRecovery(f.OperationId, 1), signal.StorageRecovery);
        Assert.AreEqual(f.OperationId, FileArtifactProcessor.OperationKey(f.Intent, f.Input, f.Plan, null,
            new() {StorageTitle = "Different placement title", StoragePolicy = new() {ProcessingWorkspaceDirectory = _root}}));
        processor = new FileArtifactProcessor(store, new FileProcessingPlanExecutor(archive), new RecursiveArchiveExecutor(archive),
            () => f.AppData, _services.GetRequiredService<IServiceScopeFactory>(), storage: new UserStoragePolicy(false));
        await _db.ArtifactOperations.Where(o => o.Id == f.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        var resumed = await processor.ExecuteAsync(f.Intent, f.Input, JsonSerializer.Serialize(f.Plan, WorkflowJson.Options),
            null, new(), $"workflow:{f.Run.Id}:part", null, null, null, default, false, entirePlan ? null : "rename", signal.StorageRecovery);
        Assert.IsNotNull(resumed.Artifact, resumed.Message);
        Assert.AreEqual(f.OperationId, resumed.Artifact.OperationId);
        Assert.AreEqual(1, (await store.GetOperationAsync(f.OperationId))!.Attempt);
        Assert.AreEqual("unchanged original bytes", await File.ReadAllTextAsync(resumed.Artifact.Files.Single()));
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync()); Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
    }

    [DataTestMethod]
    [DataRow("partialFile")]
    [DataRow("unknownDirectory")]
    [DataRow("missingOwner")]
    [DataRow("untypedReason")]
    [DataRow("changedInput")]
    [DataRow("recordedProgress")]
    public async Task ProcessingStorageInitialReserveRecoveryRejectsUnknownOrPartialEvidence(string change)
    {
        var f = await SeedProcessingStorage(localContinuation: true);
        Directory.Delete(f.Checkpoint, true);
        var prompt = JsonNode.Parse(f.Run.WaitPromptJson!)!;
        prompt["message"] = change == "untypedReason" ? "text mentioning LOCAL_STORAGE_RESERVE_LOW" : StorageWriteGuard.ReserveLowCode;
        f.Run.WaitPromptJson = prompt.ToJsonString();
        var root = Path.GetDirectoryName(f.Checkpoint)!;
        switch (change)
        {
            case "partialFile": await File.WriteAllTextAsync(Path.Combine(root, "state.json.tmp"), "partial"); break;
            case "unknownDirectory": Directory.CreateDirectory(Path.Combine(root, "unknown")); break;
            case "missingOwner": File.Delete(Directory.EnumerateFiles(_root, "workspace-owner.json", SearchOption.AllDirectories).Single()); break;
            case "changedInput": await File.WriteAllTextAsync(f.Input.Files[0], new string('x', (int)new FileInfo(f.Input.Files[0]).Length)); break;
            case "recordedProgress": f.Run.CurrentItemJson = WorkflowItemSnapshot.Capture(f.Workset with
                {Targets = [f.Workset.Targets[0] with {ProcessingStepArtifacts = new() {{"earlier", "retained-artifact"}}}]}); break;
        }
        await _db.SaveChangesAsync();
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(f.Task, f.Run, default, verifyContent: true)).Row.CanContinue, change);
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task ProcessingStorageInitialReserveApplyRechecksEmptyWorkspaceUnderRunGate()
    {
        var f = await SeedProcessingStorage(localContinuation: true);
        Directory.Delete(f.Checkpoint, true);
        var prompt = JsonNode.Parse(f.Run.WaitPromptJson!)!; prompt["message"] = StorageWriteGuard.ReserveLowCode;
        f.Run.WaitPromptJson = prompt.ToJsonString(); await _db.SaveChangesAsync();
        var host = new ProcessingRecoveryHost {BeforeValidation = () => File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(f.Checkpoint)!, "unexpected.bin"), "")};
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([f.Task.Id]));
        Assert.AreEqual(1, preview.ContinuableCount);
        var applied = await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        Assert.AreEqual(0, host.Calls);
        Assert.AreEqual("RECOVERY_CHECKPOINT_UNSUPPORTED", applied.Skipped.Single().ReasonCode);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProcessingStorageLocalContinuationDoesNotInspectDisconnectedHistoricalDownloadRoot(bool unstarted)
    {
        var f = await SeedProcessingStorage(localContinuation: true);
        if (unstarted)
        {
            Directory.Delete(f.Checkpoint, true);
            var prompt = JsonNode.Parse(f.Run.WaitPromptJson!)!; prompt["message"] = StorageWriteGuard.ReserveLowCode;
            f.Run.WaitPromptJson = prompt.ToJsonString();
        }
        var historical = Path.Combine(_root, "disconnected-nas", "Downloads");
        var destination = Path.Combine(_root, "Acquired", "item");
        ConfigureContinuationPaths(f, historical, _root, destination);
        await _db.SaveChangesAsync();
        var policy = new RecordingProcessingStoragePolicy(historical);
        _diagnostics = new(_db, policy, _services.GetRequiredService<IArtifactOperationStore>(), workspaces: f.Workspaces);
        var host = new ProcessingRecoveryHost();
        var service = new AcquisitionRecoveryService(_db, _diagnostics, new(), host);
        var preview = await service.PreviewAsync(new([f.Task.Id]));
        Assert.AreEqual(1, preview.ContinuableCount, preview.Rows.Single().ReasonCode);
        var result = await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id]));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, result.ContinuedTaskIds.ToArray());
        Assert.IsFalse(policy.Inspected.Any(p => p == historical || p.StartsWith(historical + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
        Assert.IsTrue(policy.Inspected.Contains(f.Input.RootDirectory));
        Assert.IsTrue(policy.Inspected.Contains(destination));
        Assert.IsFalse(Directory.Exists(historical)); Assert.IsFalse(Directory.Exists(destination));
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(host.Signal!, WorkflowJson.Options)!;
        Assert.AreEqual(new FileArtifactProcessingRecovery(f.OperationId, 1), signal.StorageRecovery);
        await service.ApplyAsync(new(preview.PreviewToken, [f.Task.Id])); Assert.AreEqual(1, host.Calls);
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync()); Assert.AreEqual(0, await _db.CloudDownloadOperations.CountAsync());
    }

    [DataTestMethod]
    [DataRow("processingUnavailable")]
    [DataRow("deliveryForbidden")]
    [DataRow("deliveryParentForbidden")]
    [DataRow("legacyDeliveryMissing")]
    [DataRow("inputForbidden")]
    public async Task ProcessingStorageLocalContinuationStillRequiresActualStorage(string problem)
    {
        var f = await SeedProcessingStorage(localContinuation: true);
        var historical = Path.Combine(_root, "disconnected-nas", "Downloads");
        var deliveryRoot = Directory.CreateDirectory(Path.Combine(_root, "Acquired")).FullName;
        var destination = Path.Combine(deliveryRoot, "item");
        ConfigureContinuationPaths(f, historical,
            problem == "processingUnavailable" ? Path.Combine(_root, "missing-processing") : _root,
            problem == "legacyDeliveryMissing" ? null : destination);
        await _db.SaveChangesAsync();
        var denied = problem switch
        {
            "deliveryForbidden" => destination,
            "deliveryParentForbidden" => deliveryRoot,
            "inputForbidden" => f.Input.RootDirectory,
            _ => historical
        };
        var policy = new RecordingProcessingStoragePolicy(denied);
        _diagnostics = new(_db, policy, _services.GetRequiredService<IArtifactOperationStore>(), workspaces: f.Workspaces);
        Assert.IsFalse((await _diagnostics.DiagnoseAsync(f.Task, f.Run, default, verifyContent: true)).Row.CanContinue, problem);
        Assert.IsFalse(Directory.Exists(destination));
        Assert.AreEqual(0, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task ProcessingStorageRecoveryUsesTheExecutorDirectoryManifestCheckpointIdentity()
    {
        var f = await SeedProcessingStorage(localContinuation: true, includeDirectories: true);
        var diagnosis = await _diagnostics.DiagnoseAsync(f.Task, f.Run, default, verifyContent: true);
        Assert.IsTrue(diagnosis.Row.CanContinue, diagnosis.Row.ReasonCode);
        Assert.IsTrue(File.Exists(Path.Combine(f.Checkpoint, "state.json")));
        var signal = JsonSerializer.Deserialize<CloudFileProcessingSignal>(diagnosis.SignalJson, WorkflowJson.Options)!;
        Assert.AreEqual(new FileArtifactProcessingRecovery(f.OperationId, 1), signal.StorageRecovery);
    }

    private static void ConfigureContinuationPaths(ProcessingFixture f, string historical, string processingRoot, string? destination)
    {
        var plan = WorkflowRunPlans.Read(f.Run.PlanSnapshotJson)!;
        var nodes = plan.Nodes.Select(n => n.Kind switch
        {
            AcquisitionPlanProcessingActivity.ActivityKind => n with {ConfigJson = JsonSerializer.Serialize(
                JsonSerializer.Deserialize<AcquisitionPlanProcessingConfig>(n.ConfigJson, WorkflowJson.Options)! with
                    {StoragePolicy = new() {ProcessingWorkspaceDirectory = processingRoot}}, WorkflowJson.Options)},
            "fs.publishArtifacts" => n with {ConfigJson = JsonSerializer.Serialize(new FileArtifactPublicationConfig
                {TargetDirectories = destination == null ? null : new() {{"part", destination}}}, WorkflowJson.Options)},
            _ => n
        }).ToArray();
        f.Run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with {Nodes = nodes});
        f.Run.CurrentItemJson = WorkflowItemSnapshot.Capture(f.Workset with {OutputDirectory = historical});
    }

    private sealed class RecordingProcessingStoragePolicy(string denied) : IUserStoragePolicy
    {
        internal List<string> Inspected { get; } = [];
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => path != denied;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            Inspected.Add(path);
            if (!IsPathAllowed(path, purpose)) throw new UserStoragePathException("Unavailable test storage.");
        }
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }

    private sealed class InitialReservePolicy : IStorageWritePolicyProvider
    {
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) =>
            Task.FromResult(1024L * 1024 * 1024);
    }

    private sealed class NoArchiveForStorageRecovery : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates, Action<string>? onOutput, CancellationToken ct) =>
            throw new AssertFailedException("Storage continuation must not introduce an archive step.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? onProgress, CancellationToken ct) =>
            throw new AssertFailedException("Storage continuation must not introduce an archive step.");
    }

    private sealed class ProcessingRecoveryHost : IWorkflowRunResumer
    {
        public int Calls; public int RunId; public bool Waiting; public string? Signal; public Func<Task>? BeforeValidation;
        public Task ResumeAsync(int runId, string signalJson, CancellationToken ct = default) => throw new AssertFailedException();
        public Task RequeueAsync(int runId, CancellationToken ct = default) => throw new AssertFailedException();
        public Task UpdateWaitAsync(int runId, string reason, string? promptJson, CancellationToken ct = default) => throw new AssertFailedException();
        public async Task ContinueConditionallyAsync(int runId, bool waiting, string signalJson, Func<CancellationToken, Task> validate, CancellationToken ct = default)
        {
            var gate = WorkflowRunMutationGate.For(runId); await gate.WaitAsync(ct);
            try {if (BeforeValidation != null) await BeforeValidation(); await validate(ct); Calls++; RunId = runId; Waiting = waiting; Signal = signalJson;}
            finally {gate.Release();}
        }
    }
}

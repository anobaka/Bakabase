using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    [DataTestMethod]
    [DataRow("noDefault")]
    [DataRow("failedOriginalDownload")]
    [DataRow("changedBytesBeforeDispatch")]
    [DataRow("accessRevoked")]
    [DataRow("changedDefault")]
    [DataRow("frozenWorkspace")]
    [DataRow("existingCheckpoint")]
    [DataRow("changedCheckpoint")]
    [DataRow("changedReceipt")]
    [DataRow("accessDenied")]
    public async Task BatchForCompletedDownloadUsesTheProcessorsRecordedInputAndFrozenDestination(string scenario)
    {
        var (task, run, workset, download, _) = await SeedRevisedCompletedDownload("download.awaitCompletion");
        if (scenario is "failedOriginalDownload" or "changedBytesBeforeDispatch")
        {
            run.Status = Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants.WorkflowRunStatus.Failed;
            run.ErrorMessage = "Cannot access a disposed object. Object name: 'IServiceProvider'.";
        }
        var result = await _db.DownloadResults.SingleAsync(r => r.DownloadTaskId == download.Id);
        var inputRoot = Directory.CreateDirectory(Path.Combine(download.DownloadPath, "contents")).FullName;
        var file = Path.Combine(inputRoot, "official.bin");
        File.Move(Path.Combine(download.DownloadPath, "official.bin"), file);
        result.DownloadDirectory = inputRoot;
        result.FilesJson = JsonSerializer.Serialize(new[] {file});
        var target = workset.Targets.Single();
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new()
            {Id = "s1", Op = "extractArchive", Input = "download"}, new() {Id = "s2", Op = "extractArchive", Input = "s1"}]};
        var output = Directory.CreateDirectory(Path.Combine(_root, "frozen-output")).FullName;
        var configured = Directory.CreateDirectory(Path.Combine(_root, "different-default")).FullName;
        var frozen = Directory.CreateDirectory(Path.Combine(_root, "frozen-workspace")).FullName;
        var checkpoint = Directory.CreateDirectory(Path.Combine(_root, "old-checkpoint")).FullName;
        var storage = new UserStoragePolicy(false);
        var locations = new FileProcessingWorkspaceResolver(Path.Combine(_root, "private-appdata"), storage);
        var publication = new FileArtifactPublicationConfig {TargetDirectories = new() {{target.Id, output}}};
        var frozenPlan = WorkflowRunPlans.Read(run.PlanSnapshotJson)!;
        var policy = scenario == "frozenWorkspace" ? new ArtifactStoragePolicy {ProcessingWorkspaceDirectory = frozen} : null;
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(frozenPlan with {Nodes = frozenPlan.Nodes.Concat(
            plan.Steps.Select(s => new WorkflowRunPlanNode {NodeId = s.Id, Kind = AcquisitionPlanProcessingActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(new AcquisitionPlanProcessingConfig(target.Id, plan, s.Id, StoragePolicy: policy),
                    WorkflowJson.Options)})).Append(new() {NodeId = "publish", Kind = "fs.publishArtifacts",
                        ConfigJson = JsonSerializer.Serialize(publication, WorkflowJson.Options)}).ToArray()});
        task.PlanJson = JsonSerializer.Serialize(new AcquisitionPlan {Title = "Legacy task", Parts = [new()
            {Id = target.Id, Processing = plan}], DeliveryLayout = new() {TaskId = task.Id, RootDirectory = _root,
                TaskDirectory = output, PartDirectories = new() {{target.Id, output}}}}, WorkflowJson.Options);
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition",
            EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        await _db.SaveChangesAsync();
        var checkpointKey = FileArtifactProcessor.CheckpointKey(result.Fingerprint, plan);
        if (scenario == "existingCheckpoint")
            await locations.ResolveAsync("checkpoints", checkpointKey, 1, inputRoot, checkpoint, default);
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, store, workspaces: locations);
        var host = new RecordingHost();
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), host);
        var options = new TestBOptionsManager<AcquisitionOptions>(new()
            {ProcessingWorkspaceDirectory = scenario == "noDefault" ? null : configured});
        var batches = new AcquisitionLocalRecoveryBatchService(_db, recovery, diagnostics, null!, null!,
            options, storage, new PinnedBatchCapacity(), new());
        Task Guard(string? path) => scenario == "accessDenied" && path == inputRoot
            ? throw new UnauthorizedAccessException() : Task.CompletedTask;
        var preview = await batches.PreviewAsync(new([task.Id], "automatic"), guardDirectory: Guard);
        var row = preview.Rows.Single();
        if (scenario == "accessDenied")
        {
            Assert.IsFalse(row.CanQueue);
            Assert.AreEqual("LOCAL_BATCH_PATH_FORBIDDEN", row.ReasonCode);
            return;
        }
        Assert.IsTrue(row.CanQueue, row.ReasonCode);
        Assert.AreEqual(scenario is "failedOriginalDownload" or "changedBytesBeforeDispatch" ? "LOCAL_BATCH_READY" : "RECOVERY_COMPLETED_DOWNLOAD_READY", row.ReasonCode);
        var paths = preview.Volumes.SelectMany(v => v.Paths).ToHashSet();
        Assert.IsTrue(paths.Contains(scenario == "frozenWorkspace" ? frozen : inputRoot));
        Assert.IsTrue(paths.Contains(output));
        Assert.AreEqual(scenario == "existingCheckpoint", paths.Contains(checkpoint));
        Assert.IsFalse(paths.Contains(configured));
        Assert.IsFalse(Directory.Exists(Path.Combine(inputRoot, ".bakabase-work")), "Preview must not allocate an operation.");
        if (scenario == "changedCheckpoint")
            await locations.ResolveAsync("checkpoints", checkpointKey, 1, inputRoot, checkpoint, default);
        if (scenario == "changedReceipt")
        {
            result.Fingerprint = new string('f', 64);
            await _db.SaveChangesAsync();
        }
        if (scenario is "changedCheckpoint" or "changedReceipt")
        {
            var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => batches.ApplyAsync(new(preview.PreviewToken, [task.Id])));
            Assert.AreEqual("ACQUISITION_TASK_CHANGED", error.Message);
            return;
        }
        if (scenario == "accessRevoked")
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => batches.ApplyAsync(
                new(preview.PreviewToken, [task.Id]), guardDirectory: path =>
                    path == inputRoot ? throw new UnauthorizedAccessException() : Task.CompletedTask));
            Assert.AreEqual(0, host.Calls);
            return;
        }
        if (scenario == "changedDefault") options.Value.ProcessingWorkspaceDirectory = frozen;
        var batch = await batches.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(task.Id, batch.Rows.Single().TaskId);
        if (scenario == "changedBytesBeforeDispatch") await File.WriteAllTextAsync(file, "changed after confirmation");
        await batches.TickAsync();
        var dispatched = (await batches.GetAsync(batch.BatchId)).Rows.Single();
        Assert.AreEqual(scenario == "changedBytesBeforeDispatch" ? 0 : 1, host.Calls, dispatched.ReasonCode);
        if (scenario == "changedBytesBeforeDispatch") Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", dispatched.ReasonCode);
        Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }
}

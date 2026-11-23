using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task Explicit_stage_cleanup_never_treats_an_unavailable_parent_as_already_deleted()
    {
        var f = await StageDeletion();
        var parent = Path.GetDirectoryName(f.Processing)!;
        var offline = parent + "-offline";
        Directory.Move(parent, offline);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var processing = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing);
        Assert.IsFalse(processing.Eligible);
        Assert.AreEqual("WorkspaceUnavailable", processing.ReasonCode);
        Assert.IsTrue(File.Exists(Path.Combine(offline, Path.GetFileName(f.Processing), "manual.key")));
    }

    private async Task<ArtifactRecord> StageReferenceArtifact(StageDeletionFixture f, string variant)
    {
        var directory = variant == "failed-ancestor" ? Path.GetDirectoryName(f.Processing)! : f.Processing;
        var file = Path.Combine(f.Processing, "manual.key");
        var artifact = await store.AdoptAsync(new("stage-history", "stage-history", "files.process", "history"),
            new(directory, [file], "processedFiles", true), "fixture");
        if (variant != "unbound")
            Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "older.part:artifact", artifact.OperationId));
        if (variant is "quarantined" or "foreign-quarantine") Assert.IsTrue(await store.TryQuarantineAsync(artifact.Id));
        if (variant is "failed" or "failed-ancestor")
            await db.ArtifactOperations.Where(o => o.Id == artifact.OperationId).ExecuteUpdateAsync(s => s.SetProperty(o => o.State, ArtifactOperationState.Failed));
        if (variant is "foreign" or "foreign-quarantine")
            Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "foreign:input", artifact.OperationId));
        if (variant == "consumer")
            Assert.IsTrue(await store.AttachConsumerAsync(artifact.OperationId, "active-reader", TimeSpan.FromHours(1)));
        if (variant is "pinned-own-stopped" or "pinned-foreign-active")
        {
            Assert.IsTrue(await store.AttachConsumerAsync(artifact.OperationId, "pinned-reader", TimeSpan.FromHours(1)));
            if (variant == "pinned-own-stopped")
                await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
            Assert.IsTrue(await store.PinConsumerAsync(artifact.OperationId, "pinned-reader", "workflow",
                variant == "pinned-own-stopped" ? "1" : "3"));
            if (variant == "pinned-own-stopped")
                await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
        }
        if (variant == "operation")
            await db.ArtifactOperations.Where(o => o.Id == artifact.OperationId).ExecuteUpdateAsync(s =>
                s.SetProperty(o => o.State, ArtifactOperationState.Running).SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddHours(1)));
        return artifact;
    }

    [DataTestMethod]
    [DataRow("old-part")]
    [DataRow("failed")]
    [DataRow("failed-ancestor")]
    [DataRow("quarantined")]
    [DataRow("pinned-own-stopped")]
    public async Task Explicit_stage_cleanup_releases_own_historical_views_without_rewriting_their_records(string variant)
    {
        var f = await StageDeletion();
        var artifact = await StageReferenceArtifact(f, variant);
        var original = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == artifact.Id);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var processing = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing);
        Assert.IsTrue(processing.Eligible, processing.ReasonCode);

        var result = await f.Service.DeleteAsync(new(preview.PreviewId));

        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(f.Processing));
        var history = await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == artifact.Id);
        Assert.AreEqual(original.ManifestJson, history.ManifestJson);
        Assert.AreEqual(original.State, history.State);
        Assert.IsTrue(await db.ArtifactOperationBindings.AnyAsync(b => b.OperationId == artifact.OperationId));
    }

    [DataTestMethod]
    [DataRow("foreign")]
    [DataRow("foreign-quarantine")]
    [DataRow("unbound")]
    [DataRow("consumer")]
    [DataRow("operation")]
    [DataRow("pinned-foreign-active")]
    public async Task Explicit_stage_cleanup_keeps_external_unknown_and_running_views(string variant)
    {
        var f = await StageDeletion();
        await StageReferenceArtifact(f, variant);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        Assert.IsFalse(preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing).Eligible);
        Assert.IsTrue(File.Exists(Path.Combine(f.Processing, "manual.key")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Explicit_stage_cleanup_logs_only_the_reference_category_and_ids(bool sharedArtifact)
    {
        var logger = new CleanupDiagnosticLogger();
        var f = await StageDeletion(logger);
        var reference = "shared-artifact-owner:run:2";
        if (sharedArtifact) await StageReferenceArtifact(f, "foreign");
        else
        {
            var task = new AcquisitionTaskDbModel
            {
                Title = "Foreign task",
                Status = AcquisitionStatus.Completed,
                TargetDirectory = f.Processing
            };
            db.AcquisitionTasks.Add(task);
            await db.SaveChangesAsync();
            reference = $"task:{task.Id}:target";
        }

        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var processing = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing);

        Assert.AreEqual("ReferencedPath", processing.ReasonCode);
        CollectionAssert.AreEqual(new[] {$"Acquisition cleanup retained task {f.Task.Id} stage processing due to reference {reference}"},
            logger.Messages);
        Assert.IsTrue(File.Exists(Path.Combine(f.Processing, "manual.key")));
    }

    [TestMethod]
    public async Task Task_cleanup_exception_does_not_change_stage_rebinding_ownership_rules()
    {
        var f = await StageDeletion();
        await StageReferenceArtifact(f, "old-part");
        var guard = new AcquisitionStageDirectoryReferenceGuard(db, store, new Storage(), new(root, new Storage()));
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            guard.RequireAsync(f.Task, [1], "nested.part", f.Processing, default));
        Assert.AreEqual("STAGE_DIRECTORY_SHARED", error.Message);
        await guard.RequireAsync(f.Task, [1], "nested.part", f.Processing, default, explicitTaskCleanup: true);
    }

    [DataTestMethod]
    [DataRow("owned")]
    [DataRow("foreign-transport")]
    [DataRow("foreign-result-owner")]
    [DataRow("foreign-record-run")]
    [DataRow("failed-transport")]
    [DataRow("unbound-transport")]
    public async Task Explicit_stage_cleanup_releases_only_completed_task_owned_download_receipts(string variant)
    {
        var f = await StageDeletion();
        var file = Path.Combine(f.Processing, "manual.key");
        db.DownloadTasks.Add(new() {Id = 88, DownloadPath = f.Processing,
            Status = variant == "failed-transport" ? DownloadTaskDbModelStatus.Failed : DownloadTaskDbModelStatus.Complete});
        db.DownloadTaskFiles.Add(new() {DownloadTaskId = 88, Path = file, Size = new FileInfo(file).Length});
        db.DownloadResults.Add(new() {DownloadTaskId = 88, Path = f.Processing, FilesJson = JsonSerializer.Serialize(new[] {file})});
        if (variant != "unbound-transport") db.CloudDownloadOperations.Add(new() {Id = "stage-reference-transport",
            Kind = "download", State = "completed", WorkflowRunId = variant == "foreign-transport" ? 2 : 1, DownloadTaskId = 88});
        db.DownloadResultOwners.Add(new() {DownloadTaskId = 88, AcquisitionTaskId = variant == "foreign-result-owner" ? 999 : f.Task.Id,
            WorkflowRunId = variant == "foreign-record-run" ? 2 : 1});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var processing = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing);
        Assert.AreEqual(variant == "owned", processing.Eligible, processing.ReasonCode);
        if (variant == "owned")
        {
            // A directory rebind must still preserve this exact completed download receipt.
            var guard = new AcquisitionStageDirectoryReferenceGuard(db, store, new Storage(), new(root, new Storage()));
            var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                guard.RequireAsync(f.Task, [1], "nested.part", f.Processing, default));
            Assert.AreEqual("STAGE_DIRECTORY_REFERENCED", error.Message);
            var result = await f.Service.DeleteAsync(new(preview.PreviewId));
            Assert.IsTrue(result.CleanupResults!.Single().Completed);
            Assert.IsFalse(Directory.Exists(f.Processing));
            Assert.IsTrue(await db.DownloadResults.AnyAsync(r => r.DownloadTaskId == 88));
        }
        else Assert.IsTrue(File.Exists(file));
    }

    [DataTestMethod]
    [DataRow("cancelled", true)]
    [DataRow("running", false)]
    [DataRow("changed-settings", false)]
    [DataRow("foreign-file", false)]
    [DataRow("foreign-destination", false)]
    [DataRow("unbound-original-run", false)]
    public async Task Historical_deletion_rechecks_a_sibling_capacity_root_against_the_frozen_storage(string scenario, bool eligible)
    {
        var f = await StageDeletion();
        const int siblingId = 73;
        var working = Path.GetDirectoryName(f.Processing)!;
        var delivery = Path.Combine(root, "other-delivery");
        var destination = Path.Combine(delivery, "sibling");
        var legacy = new AcquisitionPlan {Title = "Sibling", OutputDirectory = Path.Combine(root, "other-download"),
            Parts = [new() {Id = "part"}]};
        var settings = new AcquisitionDeliverySettings {RootDirectory = delivery};
        var current = legacy with {StoragePolicy = new() {ProcessingWorkspaceDirectory = working},
            DeliveryLayout = new() {TaskId = siblingId, RootDirectory = delivery, TaskDirectory = destination,
                Settings = settings, PartDirectories = new() {["part"] = destination}}};
        var history = new[] {new AcquisitionPlanRevision(1, legacy, [], DateTime.UtcNow.AddDays(-1)),
            new AcquisitionPlanRevision(2, current, [], DateTime.UtcNow, "continueFiles", 2)};
        var storage = new AcquisitionLocalFilesStorage(current.StoragePolicy!, settings);
        var queue = new AcquisitionLocalRecoveryQueueEntry {BatchId = Guid.NewGuid().ToString("N"), Signal = new string('a', 64),
            Version = new string('b', 64), RecoveryScope = "localFiles", ActionCategory = "localProcessing", WorkflowRunId = 2,
            PlanRevision = 1, WorkingDirectories = [working], DeliveryDirectories = [destination], Status = "running",
            StorageStamp = AcquisitionLocalFilesRecoveryReceipt.Hash(storage)};
        db.AcquisitionTasks.Add(new() {Id = siblingId, Title = "Sibling", WorkflowRunId = 3, PlanRevision = 2,
            PlanJson = JsonSerializer.Serialize(current, Json), PlanHistoryJson = JsonSerializer.Serialize(history, Json),
            BuilderStateJson = AcquisitionLocalRecoveryQueueEntry.Write(null, queue)});
        var owners = db.Set<WorkflowRunSourceDbModel>();
        if (scenario != "unbound-original-run")
            owners.Add(new() {WorkflowRunId = 2, EntityId = siblingId.ToString(), EntityKind = "task", Module = "acquisition", Revision = "1"});
        owners.Add(new() {WorkflowRunId = 3, EntityId = siblingId.ToString(), EntityKind = "task", Module = "acquisition", Revision = "2"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var initial = await f.Service.PreviewAsync(new([f.Task.Id], true, SkipFileVerification: true, DeleteDeliveredFiles: true));
        Assert.AreEqual("ReferencedPath", initial.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing).ReasonCode);
        await f.Service.DeleteAsync(new(initial.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Delivery));
        Assert.IsTrue(File.Exists(Path.Combine(f.Processing, "manual.key")));

        queue = queue with {Status = scenario == "running" ? "running" : "cancelled",
            StorageStamp = scenario == "changed-settings" ? AcquisitionLocalFilesRecoveryReceipt.Hash(storage with
                {Delivery = settings with {RootDirectory = Path.Combine(root, "new-root")}}) : queue.StorageStamp,
            DeliveryDirectories = scenario == "foreign-destination" ? [f.Processing] : [destination]};
        var sibling = AcquisitionLocalRecoveryQueueEntry.Write(scenario == "foreign-file" ?
            JsonSerializer.Serialize(new {input = Path.Combine(f.Processing, "manual.key")}, Json) : null, queue);
        await db.AcquisitionTasks.Where(t => t.Id == siblingId).ExecuteUpdateAsync(u => u.SetProperty(t => t.BuilderStateJson, sibling));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, RetryRetainedFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing);
        Assert.AreEqual(eligible, group.Eligible, group.ReasonCode);
        if (eligible)
        {
            var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
            Assert.IsTrue(result.CleanupResults!.Single().Completed);
            Assert.IsFalse(Directory.Exists(f.Processing));
        }
        else Assert.IsTrue(File.Exists(Path.Combine(f.Processing, "manual.key")));
        Assert.IsTrue(Directory.Exists(working));
        Assert.AreEqual(sibling, await db.AcquisitionTasks.Where(t => t.Id == siblingId).Select(t => t.BuilderStateJson).SingleAsync());
    }

    [DataTestMethod]
    [DataRow("configured-root", false)]
    [DataRow("actual-input", true)]
    [DataRow("same-run-other-task", true)]
    [DataRow("running-own-history", true)]
    [DataRow("waiting-own-history", false)]
    [DataRow("pending-signal-own-history", true)]
    public async Task Task_cleanup_distinguishes_configuration_roots_from_inputs_and_live_history(string variant, bool blocked)
    {
        var f = await StageDeletion();
        var service = f.Service;
        if (variant.EndsWith("own-history", StringComparison.Ordinal))
        {
            db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 3, Module = "acquisition", EntityKind = "task",
                EntityId = f.Task.Id.ToString(), Revision = "1"});
            var current = JsonSerializer.Serialize(new {input = f.Processing});
            var history = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, f.Plan, [], DateTime.UtcNow.AddDays(-1)),
                new AcquisitionPlanRevision(2, f.Plan, [], DateTime.UtcNow, "continueFiles", 3)}, Json);
            await db.Set<WorkflowRunSourceDbModel>().Where(s => s.WorkflowRunId == 1 && s.Module == "acquisition")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Revision, "2"));
            await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.PlanRevision, 2).SetProperty(t => t.PlanHistoryJson, history));
            await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.Status, variant == "running-own-history" ? WorkflowRunStatus.Running : WorkflowRunStatus.Waiting)
                 .SetProperty(r => r.CurrentItemJson, current));
            if (variant == "pending-signal-own-history")
                await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(s => s.SetProperty(r => r.PendingSignalJson, "{}"));
            var pinned = await StageReferenceArtifact(f, "old-part");
            Assert.IsTrue(await store.AttachConsumerAsync(pinned.OperationId, "superseded-reader", TimeSpan.FromHours(1)));
            Assert.IsTrue(await store.PinConsumerAsync(pinned.OperationId, "superseded-reader", "workflow", "3"));
            var activity = new FinishedDeletionActivity([1, 3]);
            var extraction = new NoDeletionExtraction();
            var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
            var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, resolver);
            var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage(),
                new(db, activity, root), resolver);
            service = new(db, activity, new(), files);
        }
        else db.AcquisitionTasks.Add(new() {Title = "Other task", Status = AcquisitionStatus.Completed,
            WorkflowRunId = variant == "same-run-other-task" ? 1 : 2,
            PlanJson = variant == "configured-root"
                ? JsonSerializer.Serialize(new {storagePolicy = new {processingWorkspaceDirectory = Path.GetDirectoryName(f.Processing)}})
                : JsonSerializer.Serialize(new {input = f.Processing})});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var preview = await service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var groups = preview.Rows.Single().Cleanup?.Groups;
        Assert.AreEqual(blocked, groups?.SingleOrDefault(g => g.Directory == f.Processing)?.Eligible != true,
            string.Join(",", groups?.Select(g => g.ReasonCode) ?? []));
        Assert.IsTrue(Directory.Exists(f.Processing));
        if (variant == "waiting-own-history")
        {
            var result = await service.DeleteAsync(new(preview.PreviewId));
            Assert.IsTrue(result.CleanupResults!.Single().Completed);
            Assert.IsFalse(Directory.Exists(f.Processing));
            Assert.AreEqual(WorkflowRunStatus.Cancelled, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
            Assert.IsTrue(await db.ArtifactConsumers.AnyAsync(c => c.ConsumerKey == "superseded-reader"));
        }
    }
}

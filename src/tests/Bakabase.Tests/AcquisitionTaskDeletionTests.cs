using System.Reflection;
using Bootstrap.Models.Constants;
using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Microsoft.Extensions.Localization;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // Independent fixtures reuse IDs; deletion intentionally skips busy process-wide task/run gates.
public sealed class AcquisitionTaskDeletionTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private AcquisitionTaskDeletionService _delete = null!;
    private AcquisitionDeletionActivityReader _activity = null!;
    private SnapshotProxy _bt = null!;
    private SnapshotProxy _http = null!;
    [TestInitialize]
    public async Task Setup()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(s => s.AddTransient(provider =>
            new BakabaseLocalizer(provider.GetRequiredService<IStringLocalizer<SharedResource>>())));
        _db = _services.GetRequiredService<BakabaseDbContext>();
        var bt = DispatchProxy.Create<IBtDownloadManager, SnapshotProxy>(); _bt = (SnapshotProxy)(object)bt;
        var http = DispatchProxy.Create<IHttpDownloadManager, SnapshotProxy>(); _http = (SnapshotProxy)(object)http;
        _activity = new(_db, _services.GetRequiredService<DownloadTaskService>(), bt, http);
        _delete = new(_db, _activity, new());
    }
    [TestMethod]
    public async Task DeletedTaskHistoryIncludesOlderReceiptsAndKeepsPageOrderStable()
    {
        var timestamp = DateTime.UtcNow;
        var recent = new AcquisitionTaskDbModel {DeletedAt = timestamp, DeletedIntermediateCleanup = "incomplete", DeletedDeliveryCleanup = "completed",
            DeletedCleanupReasons = "ReferencedPath,CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED"};
        var historical = new AcquisitionTaskDbModel {DeletedAt = timestamp};
        _db.AcquisitionTasks.AddRange(new AcquisitionTaskDbModel(), recent, historical);
        await _db.SaveChangesAsync();
        var page = await _delete.GetDeletedTasksAsync(1, 1, "all", default);
        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(historical.Id, page.Items.Single().TaskId);
        Assert.AreEqual(recent.Id, (await _delete.GetDeletedTasksAsync(2, 1, "all", default)).Items.Single().TaskId);
        var incomplete = (await _delete.GetDeletedTasksAsync(1, 25, "incomplete", default)).Items.Single();
        CollectionAssert.AreEqual(new[] {"ReferencedPath", "CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED"}, incomplete.LastReasonCodes.ToArray());
        Assert.AreEqual("unknown", (await _delete.GetDeletedTasksAsync(1, 25, "unknown", default)).Items.Single().DeliveryCleanup);
    }

    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    [TestMethod]
    public async Task DeletedHistoryFiltersAndPagesOnlyScopesAwaitingFileRelease()
    {
        var at = DateTime.UtcNow;
        var receipt = Guid.NewGuid().ToString("N");
        var waiting = DeferredStageCleanup.Encode([receipt], false);
        var manual = DeferredStageCleanup.Encode([receipt], true);
        AcquisitionTaskDbModel Deleted(string? intermediate, string? delivery) =>
            new() {DeletedAt = at, DeletedIntermediateCleanup = intermediate, DeletedDeliveryCleanup = delivery};
        var download = Deleted(waiting, "completed");
        var delivery = Deleted("incomplete", waiting);
        var both = Deleted(waiting, waiting);
        _db.AcquisitionTasks.AddRange(download, delivery, both, Deleted(manual, "completed"),
            Deleted("smbPending:invalid", null), new AcquisitionTaskDbModel {DeletedIntermediateCleanup = waiting});
        await _db.SaveChangesAsync();

        var first = await _delete.GetDeletedTasksAsync(1, 2, "awaitingFileRelease", default);
        var next = await _delete.GetDeletedTasksAsync(2, 2, "awaitingFileRelease", default);
        Assert.AreEqual(3, first.TotalCount);
        CollectionAssert.AreEqual(new[] {both.Id, delivery.Id}, first.Items.Select(t => t.TaskId).ToArray());
        CollectionAssert.AreEqual(new[] {"intermediate", "delivery"}, first.Items[0].AwaitingFileReleaseScopes.ToArray());
        CollectionAssert.AreEqual(new[] {"delivery"}, first.Items[1].AwaitingFileReleaseScopes.ToArray());
        Assert.AreEqual(download.Id, next.Items.Single().TaskId);
        Assert.AreEqual("incomplete", next.Items.Single().IntermediateCleanup);
        CollectionAssert.AreEqual(new[] {"intermediate"}, next.Items.Single().AwaitingFileReleaseScopes.ToArray());

        download.DeletedIntermediateCleanup = "completed";
        await _db.SaveChangesAsync();
        var refreshed = await _delete.GetDeletedTasksAsync(2, 2, "awaitingFileRelease", default);
        Assert.AreEqual(2, refreshed.TotalCount);
        Assert.AreEqual(0, refreshed.Items.Count);
    }

    private async Task<(AcquisitionTaskDbModel Task, WorkflowRunDbModel Run)> Seed(WorkflowRunStatus status = WorkflowRunStatus.Waiting)
    {
        var run = new WorkflowRunDbModel {Status = status, StartedAt = DateTime.Now, WaitingSince = DateTime.Now,
            CurrentStepIndex = 1, CurrentItemJson = "preserve private checkpoint", WaitPromptJson = "preserve prompt"};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync();
        var task = new AcquisitionTaskDbModel {Title = "Fixture", BuilderId = "prepared", Phase = "attention", Status = AcquisitionStatus.Waiting,
            WorkflowRunId = run.Id, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, PlanHistoryJson = "[]", PurchaseRecordJson = "[]",
            SourceRefsJson = "[{\"module\":\"postParser\",\"entityKind\":\"task\",\"entityId\":\"fixture\",\"revision\":\"1\"}]"};
        _db.AcquisitionTasks.Add(task); await _db.SaveChangesAsync();
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()});
        _db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = run.Id, NodeId = "wait", StepIndex = 1, Attempt = 1, Status = "waiting"});
        await _db.SaveChangesAsync(); return (task, run);
    }
    private async Task<AcquisitionTaskDeletePreview> Preview(params int[] ids) => await _delete.PreviewAsync(new(ids));

    [TestMethod]
    public async Task DirectBatchConfirmationAcceptsFortyFourFixedTasksWithoutAnyPreview()
    {
        var ids = new List<int>();
        for (var index = 0; index < 44; index++) ids.Add((await Seed()).Task.Id);
        var request = new AcquisitionTaskDeleteInput(Guid.NewGuid().ToString("N"), TaskIds: ids);
        var result = await _delete.DeleteAsync(request);
        CollectionAssert.AreEquivalent(ids.ToArray(), result.DeletedTaskIds.ToArray());
        Assert.AreEqual(0, result.Skipped.Count);
        Assert.AreEqual(44, _delete.GetProgress(request.PreviewId).FinishedTasks);
        Assert.AreEqual(44, await _db.WorkflowRuns.CountAsync());
        Assert.AreEqual(44, await _db.WorkflowNodeExecutions.CountAsync());
        Assert.AreEqual(result, await _delete.DeleteAsync(request));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _delete.DeleteAsync(request with {TaskIds = [ids[0]]}));
    }

    [TestMethod]
    public async Task ConfirmationUsesFixedIdsAndOnlyAddsTombstonePreservingFilesHistoryAndDeduplication()
    {
        var (task, run) = await Seed(); var before = JsonSerializer.Serialize(await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)); var checkpoint = run.CurrentItemJson;
        var file = Path.Combine(Path.GetTempPath(), "acquisition-delete-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, "official file");
        try
        {
            var preview = await Preview(task.Id, task.Id, 999999);
            Assert.AreEqual(2, preview.TotalCount); Assert.AreEqual(1, preview.DeletableCount);
            Assert.IsNull(task.DeletedAt, "Preview must not mutate any task.");
            var (later, _) = await Seed();
            Assert.AreEqual("preview", _delete.GetProgress(preview.PreviewId).State);
            var result = await _delete.DeleteAsync(new(preview.PreviewId));
            var progress = _delete.GetProgress(preview.PreviewId);
            Assert.AreEqual("completed", progress.State);
            Assert.AreEqual(2, progress.FinishedTasks, "Skipped rows are handled, not reported as successfully deleted.");
            Assert.AreEqual(0, progress.CompletedFiles);
            Assert.IsNull(progress.CurrentTaskId);
            CollectionAssert.AreEqual(new[] {task.Id}, result.DeletedTaskIds.ToArray(),
                string.Join(", ", result.Skipped.Select(row => $"{row.TaskId}: {row.ReasonCode}")));
            Assert.AreEqual("ACQUISITION_TASK_NOT_FOUND", result.Skipped.Single().ReasonCode);
            await _db.Entry(task).ReloadAsync(); Assert.IsNotNull(task.DeletedAt);
            var persisted = await _db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            Assert.AreEqual(before, JsonSerializer.Serialize(persisted with {DeletedAt = null, DeletedIntermediateCleanup = null, DeletedDeliveryCleanup = null}));
            var historic = (await _delete.GetDeletedTasksAsync(1, 25, "all", default)).Items.Single();
            Assert.AreEqual(task.Id, historic.TaskId);
            Assert.AreEqual("notRequested", historic.IntermediateCleanup);
            Assert.AreEqual("notRequested", historic.DeliveryCleanup);
            Assert.IsNull(later.DeletedAt); Assert.AreEqual(checkpoint, run.CurrentItemJson);
            Assert.AreEqual(2, await _db.WorkflowNodeExecutions.CountAsync());
            Assert.AreEqual(2, await _db.Set<WorkflowRunSourceDbModel>().CountAsync());
            Assert.AreEqual("official file", await File.ReadAllTextAsync(file));
            var replay = await _delete.DeleteAsync(new(preview.PreviewId));
            CollectionAssert.AreEqual(result.DeletedTaskIds.ToArray(), replay.DeletedTaskIds.ToArray());
            var acquisitions = _services.GetRequiredService<IAcquisitionService>();
            Assert.IsTrue((await acquisitions.GetAsync(task.Id))!.IsDeleted);
            Assert.IsFalse((await acquisitions.SearchAsync()).Any(t => t.Id == task.Id));
            Assert.AreEqual(0, (await acquisitions.GetByIdsAsync([task.Id])).Count);
            var page = await new AcquisitionTaskSearchService(_db, acquisitions).SearchAsync(new());
            Assert.AreEqual(1, page.TotalTasks); Assert.IsFalse(page.FilteredTaskIds.Contains(task.Id));
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task InterruptedDeletionRemainsObservableAndCannotBeAutomaticallyReplayed()
    {
        var (task, _) = await Seed();
        var preview = await Preview(task.Id);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _delete.DeleteAsync(new(preview.PreviewId), cancelled.Token));
        var progress = _delete.GetProgress(preview.PreviewId);
        Assert.AreEqual("interrupted", progress.State);
        Assert.AreEqual(0, progress.FinishedTasks);
        Assert.IsNull(task.DeletedAt);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _delete.DeleteAsync(new(preview.PreviewId)));
    }

    [TestMethod]
    public async Task ChangedTaskAndNewlyRunningWorkflowAreSkippedAtConfirmation()
    {
        var (changed, _) = await Seed(); var (active, run) = await Seed();
        var preview = await Preview(changed.Id, active.Id);
        changed.Title = "Reviewed after preview"; changed.PlanRevision++; run.Status = WorkflowRunStatus.Pending;
        await _db.SaveChangesAsync();
        var result = await _delete.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.AreEqual("ACQUISITION_TASK_CHANGED", result.Skipped.Single(s => s.TaskId == changed.Id).ReasonCode);
        Assert.AreEqual("ACQUISITION_TASK_ACTIVE", result.Skipped.Single(s => s.TaskId == active.Id).ReasonCode);
    }

    [TestMethod]
    public async Task BusyTaskAndRunGatesAreSkippedWithoutWaitingOrCancelling()
    {
        var (busy, _) = await Seed(); var (other, run) = await Seed(); var preview = await Preview(busy.Id, other.Id);
        var taskGate = AcquisitionTaskGate.For(busy.Id); var runGate = WorkflowRunMutationGate.For(run.Id);
        await taskGate.WaitAsync(); await runGate.WaitAsync();
        try
        {
            var result = await _delete.DeleteAsync(new(preview.PreviewId));
            Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.AreEqual(2, result.Skipped.Count);
            Assert.IsTrue(result.Skipped.All(s => s.ReasonCode == "ACQUISITION_TASK_OPERATION_BUSY"));
        }
        finally { taskGate.Release(); runGate.Release(); }
    }

    [TestMethod]
    public async Task Concurrent_cleanup_skips_only_its_task_with_a_specific_reason_and_preserves_the_confirmed_receipt()
    {
        var (maintained, _) = await Seed(WorkflowRunStatus.Success);
        var (idle, _) = await Seed(WorkflowRunStatus.Success);
        var (executing, run) = await Seed();
        var preview = await Preview(maintained.Id, idle.Id, executing.Id);
        var request = new AcquisitionTaskDeleteInput(preview.PreviewId);
        AcquisitionTaskDeleteResult result;
        using (var cleanup = AcquisitionTaskGate.TryEnterIntermediateCleanup(maintained.Id, default))
        {
            Assert.IsNotNull(cleanup);
            Assert.AreEqual("ACQUISITION_INTERMEDIATE_CLEANUP_ACTIVE", (await Preview(maintained.Id)).Rows.Single().ReasonCode);
            var editing = AcquisitionTaskGate.For(executing.Id);
            await editing.WaitAsync();
            try
            {
                run.Status = WorkflowRunStatus.Running;
                await _db.SaveChangesAsync();
                result = await _delete.DeleteAsync(request).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { editing.Release(); }
            CollectionAssert.AreEqual(new[] {idle.Id}, result.DeletedTaskIds.ToArray());
            Assert.AreEqual("ACQUISITION_INTERMEDIATE_CLEANUP_ACTIVE", result.Skipped.Single(s => s.TaskId == maintained.Id).ReasonCode);
            Assert.AreEqual("ACQUISITION_TASK_ACTIVE", result.Skipped.Single(s => s.TaskId == executing.Id).ReasonCode);
        }
        Assert.AreEqual(result, await _delete.DeleteAsync(request));
        var fresh = await Preview(maintained.Id);
        Assert.IsTrue(fresh.Rows.Single().CanDelete);
        CollectionAssert.AreEqual(new[] {maintained.Id}, (await _delete.DeleteAsync(new(fresh.PreviewId))).DeletedTaskIds.ToArray());
    }

    [TestMethod]
    public async Task DeletedTaskCannotResumeRetryCancelOrMutateHistoricalWorkflow()
    {
        var (task, run) = await Seed(); var preview = await Preview(task.Id);
        await _delete.DeleteAsync(new(preview.PreviewId));
        var service = _services.GetRequiredService<IAcquisitionService>();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ResumeAsync(task.Id, "{}"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.RetryAsync(task.Id));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.CancelAsync(task.Id));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new AcquisitionWorkflowRunMutationGuard(_db).EnsureCanMutateAsync(run.Id));
        await _db.Entry(run).ReloadAsync(); Assert.AreEqual(WorkflowRunStatus.Waiting, run.Status);
    }

    [TestMethod]
    public async Task HistoricalRunningRunAndDamagedHistoryFailClosed()
    {
        var (task, old) = await Seed(WorkflowRunStatus.Running);
        task.WorkflowRunId = null; await _db.SaveChangesAsync();
        Assert.AreEqual("ACQUISITION_TASK_ACTIVE", (await Preview(task.Id)).Rows.Single().ReasonCode);
        old.Status = WorkflowRunStatus.Success; task.PlanHistoryJson = "invalid historical associations"; await _db.SaveChangesAsync();
        Assert.AreEqual("ACQUISITION_EXECUTION_STATE_UNKNOWN", (await Preview(task.Id)).Rows.Single().ReasonCode);
    }

    private AcquisitionTaskDeletionService AllowConfirmedStop()
    {
        var activity = new AcquisitionDeletionActivityReader(_db, _services.GetRequiredService<DownloadTaskService>(),
            (IBtDownloadManager)(object)_bt, (IHttpDownloadManager)(object)_http,
            _services.GetRequiredService<Bakabase.Abstractions.Components.Tasks.BTaskManager>());
        return new(_db, activity, new());
    }

    [TestMethod]
    public async Task ConfirmedDeletionStopsOnlyItsOwnRunAndRetainsNodeHistory()
    {
        var (owner, run) = await Seed(WorkflowRunStatus.Running);
        var (_, other) = await Seed(WorkflowRunStatus.Running);
        var service = AllowConfirmedStop();
        var preview = await service.PreviewAsync(new([owner.Id]));
        Assert.IsTrue(preview.Rows.Single().WillStopExecution);
        await _db.Entry(run).ReloadAsync();
        Assert.AreEqual(WorkflowRunStatus.Running, run.Status);
        var result = await service.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {owner.Id}, result.DeletedTaskIds.ToArray());
        await _db.Entry(run).ReloadAsync(); await _db.Entry(other).ReloadAsync();
        Assert.AreEqual(WorkflowRunStatus.Cancelled, run.Status);
        Assert.AreEqual("preserve private checkpoint", run.CurrentItemJson);
        Assert.AreEqual(WorkflowRunStatus.Running, other.Status);
        Assert.AreEqual(1, await _db.WorkflowNodeExecutions.CountAsync(n => n.WorkflowRunId == run.Id));
    }

    [TestMethod]
    public async Task NewlySharedExecutionIsNotStoppedByOriginalDeletionConfirmation()
    {
        var (owner, run) = await Seed(WorkflowRunStatus.Running);
        var (other, _) = await Seed();
        var service = AllowConfirmedStop();
        var preview = await service.PreviewAsync(new([owner.Id]));
        Assert.IsTrue(preview.Rows.Single().WillStopExecution);
        _db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition",
            EntityKind = "task", EntityId = other.Id.ToString()});
        await _db.SaveChangesAsync();
        var result = await service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        var next = await service.PreviewAsync(new([owner.Id]));
        Assert.IsFalse(next.Rows.Single().CanDelete);
        await _db.Entry(run).ReloadAsync();
        Assert.AreEqual(WorkflowRunStatus.Running, run.Status);
    }

    [TestMethod]
    public async Task ConfirmingStopsOnlyTheSelectedAcquisitionsNativeDownload()
    {
        var (owner, _) = await Seed();
        var (other, _) = await Seed();
        _bt.AllowConfirmedStop = true;
        _bt.Bt = [new() {Id = "selected", Name = "A", OutputDirectory = "/fixture/a", SourceKind = "acquisition",
                SourceId = owner.Id.ToString(), Status = BtDownloadStatus.Running},
            new() {Id = "unrelated", Name = "B", OutputDirectory = "/fixture/b", SourceKind = "acquisition",
                SourceId = other.Id.ToString(), Status = BtDownloadStatus.Running}];
        var service = AllowConfirmedStop();
        var preview = await service.PreviewAsync(new([owner.Id]));
        Assert.IsTrue(preview.Rows.Single().WillStopExecution);
        Assert.IsTrue(_bt.Bt.All(j => j.Status == BtDownloadStatus.Running));
        var result = await service.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {owner.Id}, result.DeletedTaskIds.ToArray());
        Assert.AreEqual(BtDownloadStatus.Stopped, _bt.Bt[0].Status);
        Assert.AreEqual(BtDownloadStatus.Running, _bt.Bt[1].Status);
    }

    [TestMethod]
    public async Task NativeIntentWithoutRunIdOrParentStillProtectsTask()
    {
        var (task, run) = await Seed(); const string intent = "shared-download-intent";
        _db.ArtifactOperations.Add(new() {Id = "operation", IntentKey = intent, State = ArtifactOperationState.Completed});
        _db.ArtifactOperationBindings.Add(new() {Id = "binding", OwnerKind = "workflow", OwnerId = run.Id.ToString(), OperationId = "operation"});
        await _db.SaveChangesAsync();
        _bt.Bt = [new() {Id = "native", Name = "fixture", OutputDirectory = "/fixture", SourceKind = "workflow", SourceId = intent, Status = BtDownloadStatus.Running}];
        Assert.AreEqual("ACQUISITION_DOWNLOAD_ACTIVE", (await Preview(task.Id)).Rows.Single().ReasonCode);
        _bt.Bt = [_bt.Bt[0] with {Status = BtDownloadStatus.Completed}];
        Assert.IsTrue((await Preview(task.Id)).Rows.Single().CanDelete);
        Assert.IsTrue(_bt.Reads > 0); Assert.IsTrue(_http.Reads > 0);
    }

    [TestMethod]
    public async Task PendingNativeFromAnOldSiteResultGenerationIsNeverIgnored()
    {
        var (task, run) = await Seed();
        var site = new DownloadTaskDbModel {Key = "fixture", Status = DownloadTaskDbModelStatus.Complete, DownloadPath = "/fixture"};
        _db.DownloadTasks.Add(site); await _db.SaveChangesAsync();
        _db.DownloadResultOwners.Add(new() {DownloadTaskId = site.Id, AcquisitionTaskId = task.Id, WorkflowRunId = run.Id});
        var old = new DownloadResultDbModel {DownloadTaskId = site.Id, SourceKey = "same", DeduplicationKey = "old", Path = "/fixture"};
        _db.DownloadResults.Add(old); await _db.SaveChangesAsync();
        _db.DownloadResults.Add(new() {DownloadTaskId = site.Id, SourceKey = "same", DeduplicationKey = "new", Path = "/fixture"});
        await _db.SaveChangesAsync();
        _http.Http = [new() {Id = "old-child", Name = "fixture", OutputDirectory = "/fixture", SourceKind = "download-result", SourceId = old.Id.ToString(), Status = HttpDownloadStatus.Queued}];
        Assert.AreEqual("ACQUISITION_DOWNLOAD_ACTIVE", (await Preview(task.Id)).Rows.Single().ReasonCode);
    }

    [TestMethod]
    public async Task UnloadedNativeRegistryAndUnobservedLeaseFailClosedWithoutStartingEngines()
    {
        var (task, run) = await Seed();
        _db.ManagedDownloadTasks.Add(new() {Id = "bt:unloaded", Category = "bt", Provider = "bt", ProviderTaskId = "unloaded", WorkflowRunId = run.Id});
        await _db.SaveChangesAsync();
        Assert.AreEqual("ACQUISITION_DOWNLOAD_STATE_UNKNOWN", (await Preview(task.Id)).Rows.Single().ReasonCode);
        var (leased, leasedRun) = await Seed();
        _db.ArtifactOperations.Add(new() {Id = "unregistered", IntentKey = "native-unregistered", State = ArtifactOperationState.Running,
            LeaseOwner = $"workflow:{leasedRun.Id}:target", LeaseUntil = DateTime.UtcNow.AddMinutes(-1)});
        await _db.SaveChangesAsync();
        Assert.AreEqual("ACQUISITION_DOWNLOAD_STATE_UNKNOWN", (await Preview(leased.Id)).Rows.Single().ReasonCode);
    }

    [DataTestMethod]
    [DataRow("files.process")]
    [DataRow("files.publish")]
    public async Task ExpiredLocalClaimDoesNotRequireAnUnrelatedDownloadEngine(string kind)
    {
        var (task, run) = await Seed(WorkflowRunStatus.Success);
        task.Status = AcquisitionStatus.Completed;
        var claim = new ArtifactOperationDbModel {Id = "local-claim", Kind = kind, IntentKey = "local-intent",
            State = ArtifactOperationState.Running, LeaseOwner = $"workflow:{run.Id}:files",
            LeaseUntil = DateTime.UtcNow.AddMinutes(-1)};
        _db.ArtifactOperations.Add(claim); await _db.SaveChangesAsync();
        var before = JsonSerializer.Serialize(claim);
        var preview = await Preview(task.Id);
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        var result = await _delete.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {task.Id}, result.DeletedTaskIds.ToArray());
        await _db.Entry(claim).ReloadAsync();
        Assert.AreEqual(before, JsonSerializer.Serialize(claim), "Deleting the task must retain the abandoned operation history.");
    }

    [DataTestMethod]
    [DataRow("files.process")]
    [DataRow("files.publish")]
    public async Task LiveLocalClaimStillBlocksDeletionAndIsRecheckedAtConfirmation(string kind)
    {
        var (task, run) = await Seed(WorkflowRunStatus.Success);
        var claim = new ArtifactOperationDbModel {Id = "local-claim", Kind = kind, IntentKey = "local-intent",
            State = ArtifactOperationState.Running, LeaseOwner = $"workflow:{run.Id}:files",
            LeaseUntil = DateTime.UtcNow.AddMinutes(-1)};
        _db.ArtifactOperations.Add(claim); await _db.SaveChangesAsync();
        var preview = await Preview(task.Id);
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        claim.LeaseUntil = DateTime.UtcNow.AddMinutes(5); await _db.SaveChangesAsync();
        Assert.AreEqual("ACQUISITION_DOWNLOAD_ACTIVE", (await Preview(task.Id)).Rows.Single().ReasonCode);
        var result = await _delete.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.AreEqual("ACQUISITION_DOWNLOAD_ACTIVE", result.Skipped.Single().ReasonCode);
    }

    [TestMethod]
    public async Task LocalClaimWithoutLeaseExpiryStillFailsClosed()
    {
        var (task, run) = await Seed(WorkflowRunStatus.Success);
        _db.ArtifactOperations.Add(new() {Id = "local-claim", Kind = "files.publish", IntentKey = "local-intent",
            State = ArtifactOperationState.Running, LeaseOwner = $"workflow:{run.Id}:files", LeaseUntil = null});
        await _db.SaveChangesAsync();
        Assert.AreEqual("ACQUISITION_DOWNLOAD_STATE_UNKNOWN", (await Preview(task.Id)).Rows.Single().ReasonCode);
    }

    [TestMethod]
    public async Task InvalidPreviewCannotDeleteOrExpandSelection()
    {
        var (task, _) = await Seed();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _delete.DeleteAsync(new(Guid.NewGuid().ToString("N"))));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => Preview());
        Assert.IsNull(task.DeletedAt);
    }

    [TestMethod]
    public async Task Waiting_run_with_an_accepted_signal_protects_the_task_even_before_the_scheduler_claims_it()
    {
        var (task, run) = await Seed(WorkflowRunStatus.Waiting);
        var initial = await Preview(task.Id);
        Assert.IsTrue(initial.Rows.Single().CanDelete);
        await _db.WorkflowRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(r => r.PendingSignalJson, "{}"));
        var updated = (await Preview(task.Id)).Rows.Single();
        Assert.AreEqual("ACQUISITION_TASK_ACTIVE", updated.ReasonCode);
        Assert.IsFalse(updated.CanRemoveTaskIfCleanupFails);
        var result = await _delete.DeleteAsync(new(initial.PreviewId));
        Assert.AreEqual("ACQUISITION_TASK_ACTIVE", result.Skipped.Single().ReasonCode);
        Assert.IsNull(await _db.AcquisitionTasks.Where(t => t.Id == task.Id).Select(t => t.DeletedAt).SingleAsync());
    }

    [DataTestMethod]
    [DataRow(DownloadTaskDbModelStatus.Disabled)]
    [DataRow(DownloadTaskDbModelStatus.Failed)]
    [DataRow(DownloadTaskDbModelStatus.Complete)]
    public async Task EverySiteStartRejectsDeletedOwnerBeforeChangingStatusOrDoingPrechecks(DownloadTaskDbModelStatus status)
    {
        var (task, run) = await Seed(WorkflowRunStatus.Success);
        task.DeletedAt = DateTime.UtcNow;
        var site = new DownloadTaskDbModel {Key = "fixture", Status = status, DownloadPath = "/fixture", Message = "original", Options = "{}"};
        _db.DownloadTasks.Add(site); await _db.SaveChangesAsync();
        _db.CloudDownloadOperations.Add(new() {Id = "download", WorkflowRunId = run.Id, DownloadTaskId = site.Id, Kind = "download"});
        await _db.SaveChangesAsync();
        var response = await _services.GetRequiredService<DownloadTaskService>().Start(t => t.Id == site.Id, targeted: true);
        Assert.AreNotEqual((int)ResponseCode.Success, response.Code);
        StringAssert.Contains(response.Message!, "ACQUISITION_TASK_DELETED");
        await _db.Entry(site).ReloadAsync();
        Assert.AreEqual(status, site.Status); Assert.AreEqual("original", site.Message); Assert.AreEqual("{}", site.Options);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    [TestMethod]
    public async Task DownloadStartLeaseAndDeletionAreMutuallyExclusive()
    {
        var (task, run) = await Seed(WorkflowRunStatus.Success);
        var site = new DownloadTaskDbModel {Key = "fixture", Status = DownloadTaskDbModelStatus.Complete, DownloadPath = "/fixture"};
        _db.DownloadTasks.Add(site); await _db.SaveChangesAsync();
        _db.CloudDownloadOperations.Add(new() {Id = "download", WorkflowRunId = run.Id, DownloadTaskId = site.Id, Kind = "download"});
        await _db.SaveChangesAsync();
        var startGuard = new WorkflowOwnedDownloadStartGuard(_db, [new AcquisitionWorkflowRunMutationGuard(_db)]);
        var first = await Preview(task.Id);
        await using (await startGuard.AcquireAsync(site.Id))
        {
            var skipped = await _delete.DeleteAsync(new(first.PreviewId));
            Assert.AreEqual("ACQUISITION_TASK_OPERATION_BUSY", skipped.Skipped.Single().ReasonCode);
            Assert.AreEqual(0, skipped.DeletedTaskIds.Count);
        }
        var second = await Preview(task.Id);
        Assert.AreEqual(1, (await _delete.DeleteAsync(new(second.PreviewId))).DeletedTaskIds.Count);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () => { await using var lease = await startGuard.AcquireAsync(site.Id); });
    }

    [TestMethod]
    public async Task SchedulerPolicyRejectionDoesNotRewriteRetainedCompletionAsFailure()
    {
        var (task, run) = await Seed(WorkflowRunStatus.Success); task.DeletedAt = DateTime.UtcNow;
        var site = new DownloadTaskDbModel {Key = "fixture", Status = DownloadTaskDbModelStatus.Complete,
            DownloadPath = Path.GetTempPath(), Message = "retained completion"};
        _db.DownloadTasks.Add(site); await _db.SaveChangesAsync();
        _db.CloudDownloadOperations.Add(new() {Id = "download", WorkflowRunId = run.Id, DownloadTaskId = site.Id, Kind = "download"});
        await _db.SaveChangesAsync();
        await _services.GetRequiredService<DownloadTaskService>().TryStartAllTasks(DownloadTaskStartMode.ManualStart,
            [site.Id], DownloadTaskActionOnConflict.Ignore, skipSatisfiedTasks: false);
        await _db.Entry(site).ReloadAsync();
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, site.Status);
        Assert.AreEqual("retained completion", site.Message);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
    }

    public class SnapshotProxy : DispatchProxy
    {
        public IReadOnlyList<BtDownloadJob> Bt = []; public IReadOnlyList<HttpDownloadJob> Http = []; public int Reads;
        public bool AllowConfirmedStop;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (AllowConfirmedStop && method?.Name == "StopAsync" && method.DeclaringType == typeof(IBtDownloadManager))
            {
                var id = (string)args![0]!;
                Bt = Bt.Select(j => j.Id == id ? j with {Status = BtDownloadStatus.Stopped} : j).ToArray();
                return Task.FromResult(Bt.Single(j => j.Id == id));
            }
            if (method?.Name != "ReadSnapshotAsync") throw new AssertFailedException("Deletion must never initialize or mutate download engines: " + method?.Name);
            Reads++;
            return method.DeclaringType == typeof(IBtDownloadManager) ? Task.FromResult(Bt) : Task.FromResult(Http);
        }
    }
}

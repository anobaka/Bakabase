using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // Separate in-memory databases intentionally reuse IDs guarded by process-wide task/run gates.
public sealed partial class AcquisitionLocalRecoveryBatchTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [TestMethod]
    public async Task CapacityOnlyReadsLiveMeasurementsWithoutIssuingAnyQueueApproval()
    {
        using var f = await Fixture.CreateAsync();
        f.Options.Value.UseEmergencySpaceReserve = true;
        f.Capacity.Available = GiB - 100;
        var preview = await f.Service.PreviewAsync(new([1], Mode: "capacityOnly"));
        Assert.AreEqual("", preview.PreviewToken);
        Assert.IsFalse(preview.Rows.Single().CanQueue);
        Assert.AreEqual(200, preview.Volumes.Single().EstimatedAdditionalBytes);
        Assert.AreEqual(GiB, preview.Volumes.Single().MinimumFreeBytes);
        Assert.AreEqual(GiB - 100, preview.Volumes.Single().AvailableBytes);
        Assert.IsTrue(preview.Volumes.Single().ExceedsUsable);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.ApplyAsync(new(preview.PreviewToken, [1])));
        f.Capacity.Available = 10 * GiB;
        Assert.IsFalse((await f.Service.PreviewAsync(new([1], Mode: "capacityOnly"))).Volumes.Single().ExceedsUsable);
        Assert.AreEqual(0, (await f.Service.ListAsync()).Count);
        Assert.AreEqual(0, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task PreviewOrdersMeasuredSizesUnknownLastAndDoesNotEnqueue()
    {
        using var f = await Fixture.CreateAsync();
        var preview = await f.Service.PreviewAsync(new([1, 2, 3]));
        CollectionAssert.AreEqual(new[] {2, 1, 3}, preview.Rows.Select(r => r.TaskId).ToArray());
        Assert.IsTrue(preview.Rows.All(r => r.CanQueue));
        Assert.IsNull(preview.Rows.Last().EstimatedBytes);
        Assert.IsTrue(preview.EstimateIncomplete);
        Assert.AreEqual(0, f.Host.Calls.Count);
        Assert.IsTrue((await f.Service.ListAsync()).Count == 0);
    }

    [TestMethod]
    public async Task OneActiveTaskRemainsSerialUntilSuccessIncludingPendingSignal()
    {
        using var f = await Fixture.CreateAsync();
        var preview = await f.Service.PreviewAsync(new([1, 2]));
        var batch = await f.Service.ApplyAsync(new(preview.PreviewToken, [1, 2]));
        await f.Service.TickAsync();
        CollectionAssert.AreEqual(new[] {22}, f.Host.Calls.ToArray());
        await f.Service.TickAsync();
        Assert.AreEqual(1, f.Host.Calls.Count);
        await f.Db.WorkflowRuns.Where(r => r.Id == 22).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Waiting)
            .SetProperty(r => r.PendingSignalJson, "{}"));
        await f.Service.TickAsync();
        Assert.AreEqual(1, f.Host.Calls.Count, "Waiting with a persisted answer is still pending execution.");
        await f.CompleteAsync(2);
        await f.Service.TickAsync();
        await f.Service.TickAsync();
        CollectionAssert.AreEqual(new[] {22, 11}, f.Host.Calls.ToArray());
        Assert.AreEqual("running", (await f.Service.GetAsync(batch.BatchId)).Status);
    }

    [TestMethod]
    public async Task QueueInheritsLiveReserveAndChecksExplicitConfiguredModeAtAdmissionAndWrite()
    {
        using var f = await Fixture.CreateAsync();
        f.Options.Value.UseEmergencySpaceReserve = true;
        f.Capacity.Available = 5 * GiB;
        var preview = await f.Service.PreviewAsync(new([1]));
        Assert.AreEqual(GiB, preview.Volumes.Single().MinimumFreeBytes);
        Assert.AreEqual(50 * GiB, preview.ConfiguredMinimumFreeBytes);
        var strict = await f.Service.PreviewCapacityAsync(new(preview.PreviewToken, [1], "configured"));
        Assert.IsTrue(strict.RequiresSpaceConfirmation);
        Assert.AreEqual(preview.ConfiguredMinimumFreeBytes, strict.Volumes.Single().MinimumFreeBytes);
        var batch = await f.Service.ApplyAsync(new(preview.PreviewToken, [1], ReserveMode: "configured"));
        await f.Service.TickAsync();
        Assert.AreEqual("spaceBlocked", (await f.Service.GetAsync(batch.BatchId)).Status);
        f.Capacity.Available = 100 * GiB;
        await f.Service.CommandAsync(batch.BatchId, "resume");
        await f.Service.TickAsync();
        Assert.AreEqual("configured", AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1))!.ReserveMode);

        var changed = await f.Service.PreviewAsync(new([2]));
        f.Options.Value.UseEmergencySpaceReserve = false;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.ApplyAsync(new(changed.PreviewToken, [2])));
    }

    [TestMethod]
    public async Task ReserveBlocksWithoutApprovalAndExplicitApprovalIsBoundAndExpiring()
    {
        using var f = await Fixture.CreateAsync();
        f.Capacity.Available = 50 * GiB + 50;
        var preview = await f.Service.PreviewAsync(new([1]));
        Assert.IsTrue(preview.RequiresSpaceConfirmation);
        var batch = await f.Service.ApplyAsync(new(preview.PreviewToken, [1]));
        await f.Service.TickAsync();
        Assert.AreEqual("spaceBlocked", (await f.Service.GetAsync(batch.BatchId)).Status);
        Assert.AreEqual(0, f.Host.Calls.Count);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1)));
        await f.Service.CommandAsync(batch.BatchId, "cancel");
        preview = await f.Service.PreviewAsync(new([1]));
        var permitted = await f.Service.ApplyAsync(new(preview.PreviewToken, [1], true));
        await f.Service.TickAsync();
        Assert.AreEqual(1, f.Host.Calls.Count);
        var grant = AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1));
        Assert.IsNotNull(grant);
        Assert.AreEqual(permitted.OverrideExpiresAt, grant.ExpiresAt);
        Assert.AreEqual(50 * GiB, f.Options.Value.MinimumFreeSpaceBytes);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(2)));
    }

    [TestMethod]
    public async Task ActualFullAndMissingVolumePauseWholeBatch()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2], true));
        await f.Service.TickAsync();
        await f.Db.WorkflowRuns.Where(r => r.Id == 22).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Waiting)
            .SetProperty(r => r.PendingSignalJson, (string?)null).SetProperty(r => r.WaitPromptJson, "{\"message\":\"LOCAL_STORAGE_FULL\"}"));
        await f.Service.TickAsync(); await f.Service.TickAsync();
        Assert.AreEqual("spaceBlocked", (await f.Service.GetAsync(b.BatchId)).Status);
        Assert.AreEqual(1, f.Host.Calls.Count);
        Assert.AreEqual("queued", (await f.Service.GetAsync(b.BatchId)).Rows.Single(r => r.TaskId == 1).Status);
    }

    [TestMethod]
    public async Task DuplicateApplyCannotBroadenOrRepeatApprovalAndRepreviewRejectsQueuedTasks()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var a = await f.Service.ApplyAsync(new(p.PreviewToken, [1]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1]));
        Assert.AreEqual(a.BatchId, b.BatchId);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.ApplyAsync(new(p.PreviewToken, [1], true)));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.ApplyAsync(new(p.PreviewToken, [1, 2])));
        Assert.IsFalse((await f.Service.PreviewAsync(new([1]))).Rows.Single().CanQueue);
        Assert.AreEqual(0, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task PlanOrDestinationChangeAfterApprovalIsSkippedWithoutEnqueue()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1], true));
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanRevision, 2));
        await f.Service.TickAsync();
        Assert.AreEqual("skipped", (await f.Service.GetAsync(b.BatchId)).Rows.Single().Status);
        Assert.AreEqual(0, f.Host.Calls.Count);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1)));
    }

    [TestMethod]
    public async Task PauseAndCancelAffectOnlyUnstartedItemsAndPreserveUnknownStateAndHistory()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2]));
        await f.Service.TickAsync();
        await f.Service.CommandAsync(b.BatchId, "pause");
        var paused = await f.Service.GetAsync(b.BatchId);
        Assert.AreEqual("running", paused.Rows.Single(r => r.TaskId == 2).Status);
        Assert.AreEqual("paused", paused.Rows.Single(r => r.TaskId == 1).Status);
        await f.Service.CommandAsync(b.BatchId, "cancel");
        Assert.AreEqual(WorkflowRunStatus.Pending, (await f.Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 22)).Status);
        Assert.AreEqual("cancelled", (await f.Service.GetAsync(b.BatchId)).Rows.Single(r => r.TaskId == 1).Status);
        Assert.AreEqual("keep", JsonNode.Parse((await f.TaskAsync(1)).BuilderStateJson!)!["unknown"]!.GetValue<string>());
        var again = await f.Service.PreviewAsync(new([1]));
        await f.Service.ApplyAsync(new(again.PreviewToken, [1]));
        Assert.AreEqual("cancelled", (await f.Service.GetAsync(b.BatchId)).Rows.Single(r => r.TaskId == 1).Status);
    }

    [TestMethod]
    public async Task LostEnqueueResponseAndServiceRestartDoNotDispatchTwice()
    {
        using var f = await Fixture.CreateAsync();
        f.Host.ThrowAfterAccept = true;
        var p = await f.Service.PreviewAsync(new([1, 2]));
        await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2]));
        await f.Service.TickAsync();
        var restarted = f.NewService();
        await restarted.TickAsync(); await restarted.TickAsync();
        Assert.AreEqual(1, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task InterruptedRunWithStalePendingAnswerCannotWedgeTheWholeQueueAfterRestart()
    {
        using var f = await Fixture.CreateAsync();
        f.Host.ThrowAfterAccept = true;
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var batch = await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2]));
        await f.Service.TickAsync();
        await f.Db.WorkflowRuns.Where(r => r.Id == 22).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Interrupted));
        f.Host.ThrowAfterAccept = false;
        var restarted = f.NewService();
        await restarted.TickAsync();
        Assert.AreEqual("attention", (await restarted.GetAsync(batch.BatchId)).Rows.Single(r => r.TaskId == 2).Status);
        await restarted.TickAsync();
        CollectionAssert.AreEqual(new[] {22, 11}, f.Host.Calls.ToArray());
    }

    [TestMethod]
    public async Task RejectedContinuationRevokesOnlyItsRunBoundGrant()
    {
        using var f = await Fixture.CreateAsync();
        f.Host.RejectBeforeAccept = true;
        var p = await f.Service.PreviewAsync(new([1]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1], true));
        await f.Service.TickAsync();
        Assert.AreEqual("attention", (await f.Service.GetAsync(b.BatchId)).Rows.Single().Status);
        Assert.AreEqual("revoked", AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1))!.Status);
        Assert.AreEqual(0, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task CancellingSpaceBlockedQueueRevokesGrantButLostAcceptedResponseDoesNot()
    {
        using var f = await Fixture.CreateAsync();
        f.Host.ThrowAfterAccept = true;
        var p = await f.Service.PreviewAsync(new([1]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1], true));
        await f.Service.TickAsync();
        Assert.AreEqual("applied", AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1))!.Status);
        await f.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Waiting)
            .SetProperty(r => r.PendingSignalJson, (string?)null).SetProperty(r => r.WaitPromptJson, "{\"message\":\"LOCAL_STORAGE_FULL\"}"));
        await f.Service.TickAsync();
        await f.Service.CommandAsync(b.BatchId, "cancel");
        Assert.AreEqual("revoked", AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1))!.Status);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await f.Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 11)).Status);
    }

    [TestMethod]
    public async Task ExpiredBatchApprovalCannotBeRenewedByResume()
    {
        using var f = await Fixture.CreateAsync();
        f.Capacity.Available = 2 * GiB;
        var p = await f.Service.PreviewAsync(new([1]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1], true));
        var task = await f.TaskAsync(1);
        var entry = AcquisitionLocalRecoveryQueueEntry.Read(task)! with {Status = "paused", OverrideExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)};
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson,
            AcquisitionLocalRecoveryQueueEntry.Write(task.BuilderStateJson, entry)));
        await f.Service.CommandAsync(b.BatchId, "resume");
        await f.Service.TickAsync();
        Assert.AreEqual("spaceBlocked", (await f.Service.GetAsync(b.BatchId)).Status);
        Assert.AreEqual(0, f.Host.Calls.Count);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1)));
    }

    [TestMethod]
    public async Task LostLocalAdoptionResponseAtContinueFilesRemainsSerialAndBindsOnlyMatchingReceipt()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2], true));
        await f.InstallAdoptionAsync(2, matching: true);
        await f.NewService().TickAsync();
        var row = (await f.Service.GetAsync(b.BatchId)).Rows.Single(r => r.TaskId == 2);
        Assert.AreEqual("running", row.Status);
        Assert.AreEqual(222, row.WorkflowRunId);
        Assert.AreEqual(0, f.Host.Calls.Count);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(2)), "Continue-files observation does not grant a new writer.");
        await f.Db.WorkflowRuns.Where(r => r.Id == 222).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success)
            .SetProperty(r => r.PendingSignalJson, (string?)null));
        await f.Service.TickAsync(); await f.Service.TickAsync();
        CollectionAssert.AreEqual(new[] {11}, f.Host.Calls.ToArray());
    }

    [TestMethod]
    public async Task UnrelatedAdoptionCannotInheritApprovalOrOverlapAnotherRunningLocalTask()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2], true));
        await f.InstallAdoptionAsync(2, matching: false);
        await f.Service.TickAsync(); await f.Service.TickAsync();
        var row = (await f.Service.GetAsync(b.BatchId)).Rows.Single(r => r.TaskId == 2);
        Assert.AreEqual("running", row.Status);
        Assert.AreEqual(22, row.WorkflowRunId, "Do not bind the unrelated new revision.");
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(2)));
        Assert.AreEqual(0, f.Host.Calls.Count);
        await f.Db.WorkflowRuns.Where(r => r.Id == 222).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Interrupted));
        await f.Service.TickAsync();
        Assert.AreEqual("attention", (await f.Service.GetAsync(b.BatchId)).Rows.Single(r => r.TaskId == 2).Status);
        await f.Service.TickAsync();
        CollectionAssert.AreEqual(new[] {11}, f.Host.Calls.ToArray());
    }

    [TestMethod]
    public async Task AcceptedResponseCannotBindAnUnrelatedRevisionInstalledBeforeItsReceiptIsSaved()
    {
        using var f = await Fixture.CreateAsync();
        f.Host.AfterAccept = () => f.InstallAdoptionAsync(2, matching: false);
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2], true));
        await f.Service.TickAsync(); await f.Service.TickAsync();
        var row = (await f.Service.GetAsync(b.BatchId)).Rows.Single(r => r.TaskId == 2);
        Assert.AreEqual("running", row.Status);
        Assert.AreEqual(22, row.WorkflowRunId);
        Assert.AreEqual(22, AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(2))!.WorkflowRunId);
        Assert.AreEqual(1, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task RejectedResponseWhileRunAlreadyPendingCannotReleaseTheSerialQueue()
    {
        using var f = await Fixture.CreateAsync();
        f.Host.RejectAfterAccept = true;
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2], true));
        await f.Service.TickAsync(); await f.Service.TickAsync();
        Assert.AreEqual("running", (await f.Service.GetAsync(b.BatchId)).Rows.Single(r => r.TaskId == 2).Status);
        Assert.AreEqual("applied", AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(2))!.Status);
        Assert.AreEqual(1, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task RestrictedResumePreflightsAllCapturedPathsBeforeChangingAnyQueueOrGrant()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1, 2]));
        var b = await f.Service.ApplyAsync(new(p.PreviewToken, [1, 2], true));
        await f.Service.CommandAsync(b.BatchId, "pause");
        var before = new[] {(await f.TaskAsync(1)).BuilderStateJson, (await f.TaskAsync(2)).BuilderStateJson};
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => f.Service.CommandAsync(b.BatchId, "resume",
            guardDirectory: path => path == p.Rows.Single(r => r.TaskId == 2).DeliveryDirectory
                ? throw new UnauthorizedAccessException() : Task.CompletedTask));
        CollectionAssert.AreEqual(before, new[] {(await f.TaskAsync(1)).BuilderStateJson, (await f.TaskAsync(2)).BuilderStateJson});
        Assert.IsTrue((await f.Service.GetAsync(b.BatchId)).Rows.All(r => r.Status == "paused"));
        Assert.AreEqual(0, f.Host.Calls.Count);
    }

    [TestMethod]
    public void LocalBoundaryRejectsAnyFutureNetworkAndAllowsOnlyExistingCompletionObservation()
    {
        static WorkflowRunDbModel Run(params string[] kinds) => new() {CurrentStepIndex = 0,
            PlanSnapshotJson = JsonSerializer.Serialize(new WorkflowRunPlan {Name = "test", TriggerKind = "cloud.downloadRequested",
                Nodes = kinds.Select((kind, n) => new WorkflowRunPlanNode {NodeId = n.ToString(), Kind = kind}).ToArray()}, WorkflowJson.Options)};
        Assert.IsTrue(AcquisitionLocalRecoveryBatchService.IsLocalRemainder(Run("acquisition.plan.process", "fs.assessDelivery", "fs.publishArtifacts", "download.cleanupArtifacts")));
        Assert.IsTrue(AcquisitionLocalRecoveryBatchService.IsLocalRemainder(Run("download.awaitCompletion", "acquisition.plan.process", "fs.publishArtifacts")));
        Assert.IsFalse(AcquisitionLocalRecoveryBatchService.IsLocalRemainder(Run("acquisition.plan.process", "download.enqueueSupported")));
        Assert.IsFalse(AcquisitionLocalRecoveryBatchService.IsLocalRemainder(Run("baidu.transfer", "fs.publishArtifacts")));
        Assert.IsFalse(AcquisitionLocalRecoveryBatchService.IsLocalRemainder(Run("download.cleanupArtifacts")));
        Assert.AreEqual("LOCAL_STORAGE_FULL", AcquisitionLocalRecoveryBatchService.StorageProblem(new() {WaitPromptJson = "{\"message\":\"LOCAL_STORAGE_FULL\"}"}));
        Assert.IsNull(AcquisitionLocalRecoveryBatchService.StorageProblem(new() {ErrorMessage = "An unrelated message mentioning LOCAL_STORAGE_FULL"}));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection;
        internal BakabaseDbContext Db { get; }
        internal PolicyOptions Options { get; } = new();
        internal CapacityReader Capacity { get; } = new();
        internal Diagnostics Diagnostics { get; } = new();
        internal Host Host { get; }
        internal AcquisitionLocalRecoveryBatchPreviewStore Previews { get; } = new();
        internal AcquisitionLocalRecoveryBatchService Service { get; }
        private Fixture(SqliteConnection connection)
        {
            _connection = connection;
            Db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(connection).Options);
            Host = new(Db);
            Service = NewService();
        }
        internal AcquisitionLocalRecoveryBatchService NewService() => new(Db,
            new(Db, Diagnostics, new(), Host), Diagnostics, null!, null!, Options, new StoragePolicy(), Capacity, Previews);
        internal static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var f = new Fixture(connection); await f.Db.Database.EnsureCreatedAsync();
            for (var id = 1; id <= 3; id++)
            {
                var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "batch-final", id.ToString()));
                var plan = new AcquisitionPlan {Title = "Task " + id, Parts = [new() {Id = "p"}],
                    DeliveryLayout = new() {TaskId = id, RootDirectory = path, TaskDirectory = path, PartDirectories = new() {{"p", path}}}};
                f.Db.AcquisitionTasks.Add(new() {Id = id, Title = "Task " + id, BuilderId = "prepared", PlanRevision = 1,
                    WorkflowRunId = id * 11, Status = AcquisitionStatus.Waiting, BuilderStateJson = "{\"unknown\":\"keep\"}",
                    PlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options)});
                f.Db.WorkflowRuns.Add(new() {Id = id * 11, Status = WorkflowRunStatus.Waiting, CurrentStepIndex = 0,
                    PlanSnapshotJson = JsonSerializer.Serialize(new WorkflowRunPlan {Name = "local", TriggerKind = "cloud.downloadRequested",
                        Nodes = [new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]}, WorkflowJson.Options)});
                if (id == 3) continue;
                var artifact = "artifact" + id;
                f.Db.Artifacts.Add(new() {Id = artifact, OperationId = artifact, RootPath = path,
                    ManifestJson = JsonSerializer.Serialize(new ArtifactManifest([new("file", id == 1 ? 200 : 100, "hash")], "fingerprint"), WorkflowJson.Options)});
                f.Db.ArtifactOperations.Add(new() {Id = artifact, OutputArtifactId = artifact, State = ArtifactOperationState.Completed});
                f.Db.ArtifactOperationBindings.Add(new() {Id = artifact, OwnerKind = "workflow", OwnerId = (id * 11).ToString(), Slot = "p:artifact.process", OperationId = artifact});
            }
            await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear(); return f;
        }
        internal Task<AcquisitionTaskDbModel> TaskAsync(int id) => Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        internal async Task InstallAdoptionAsync(int id, bool matching)
        {
            var task = await TaskAsync(id);
            var original = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == task.WorkflowRunId);
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, WorkflowJson.Options)!;
            var path = plan.DeliveryLayout!.TaskDirectory;
            var settings = new AcquisitionLocalFilesStorage(new() {ProcessingWorkspaceDirectory = path}, new() {RootDirectory = path});
            var preview = new AcquisitionLocalFilesRecoveryPreview([new("p", 1, 1, "fingerprint", 1, path, path, path, path, path)]);
            IReadOnlyList<string>? downloadEvidence = ["proof"];
            var entry = AcquisitionLocalRecoveryQueueEntry.Read(task)! with {Status = "starting", RecoveryScope = "localFiles",
                WorkingDirectories = [path], DeliveryDirectories = [path], StorageStamp = AcquisitionLocalFilesRecoveryReceipt.Hash(settings),
                Signal = AcquisitionLocalFilesRecoveryReceipt.Hash(new {settings, preview, downloadEvidence})};
            var receipt = new AcquisitionLocalFilesRecoveryReceipt(original.Id, task.PlanRevision,
                AcquisitionLocalFilesRecoveryReceipt.Hash(original), settings,
                [new("p", 1, 1, "intent", "proof", "fingerprint", "artifact", "operation")],
                new([preview.Parts[0] with {ArtifactId = "artifact"}]), task.PlanRevision + 1);
            if (!matching) receipt = receipt with {PreviousRunId = 999};
            var builder = JsonNode.Parse(AcquisitionLocalRecoveryQueueEntry.Write(task.BuilderStateJson, entry))!;
            builder["localFilesRecovery"] = JsonSerializer.SerializeToNode(receipt, WorkflowJson.Options);
            var updatedPlan = plan with {StoragePolicy = settings.Policy,
                Parts = [new() {Id = "p", ContinueFrom = new(id, original.Id, "artifact", "operation", "fingerprint")}]};
            Db.WorkflowRuns.Add(new() {Id = 222, Status = WorkflowRunStatus.Pending, PendingSignalJson = "{}", CurrentStepIndex = 0,
                PlanSnapshotJson = JsonSerializer.Serialize(new WorkflowRunPlan {Name = "local", TriggerKind = "cloud.downloadRequested",
                    Nodes = [new() {NodeId = "continue", Kind = AcquisitionPlanContinueFilesActivity.ActivityKind},
                        new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]}, WorkflowJson.Options)});
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
            await Db.AcquisitionTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, 222)
                .SetProperty(t => t.PlanRevision, task.PlanRevision + 1).SetProperty(t => t.PlanJson, JsonSerializer.Serialize(updatedPlan, WorkflowJson.Options))
                .SetProperty(t => t.BuilderStateJson, builder.ToJsonString(WorkflowJson.Options)));
        }
        internal async Task CompleteAsync(int id)
        {
            await Db.WorkflowRuns.Where(r => r.Id == id * 11).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success)
                .SetProperty(r => r.PendingSignalJson, (string?)null));
            await Db.AcquisitionTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AcquisitionStatus.Completed));
        }
        public void Dispose() {Db.Dispose(); _connection.Dispose();}
    }
    private sealed class PolicyOptions : IBOptions<AcquisitionOptions>
    {public AcquisitionOptions Value { get; } = new() {MinimumFreeSpaceBytes = 50 * GiB};}
    private sealed class CapacityReader : IAcquisitionBatchCapacityReader
    {
        internal long Available = 100 * GiB;
        public StorageVolumeCapacity Measure(string path) => new("volume", Available, 200 * GiB);
        public string Identity(StorageVolumeCapacity capacity) => "volume";
    }
    private sealed class StoragePolicy : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
    private sealed class Diagnostics : IAcquisitionRecoveryDiagnostics
    {
        internal bool RequireVerification;
        internal List<bool> Verifications { get; } = [];
        public Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
            CancellationToken ct, bool verifyFiles = true, bool verifyContent = false)
        {
            Verifications.Add(verifyFiles);
            return Task.FromResult(new AcquisitionRecoveryDiagnosis(new(task.Id, task.Title, run?.Id,
                !RequireVerification || verifyFiles, !RequireVerification || verifyFiles ? "resume" : null,
                !RequireVerification || verifyFiles ? "RECOVERY_FILES_VERIFIED" : "RECOVERY_VERIFY_FILES")));
        }
    }
    private sealed class Host(BakabaseDbContext db) : IWorkflowRunResumer
    {
        internal readonly List<int> Calls = [];
        internal bool ThrowAfterAccept;
        internal bool RejectBeforeAccept;
        internal bool RejectAfterAccept;
        internal Func<Task>? AfterAccept;
        public Task ResumeAsync(int id, string signal, CancellationToken ct = default) => throw new AssertFailedException();
        public Task RequeueAsync(int id, CancellationToken ct = default) => throw new AssertFailedException();
        public Task UpdateWaitAsync(int id, string reason, string? prompt, CancellationToken ct = default) => throw new AssertFailedException();
        public async Task ContinueConditionallyAsync(int id, bool waiting, string signal, Func<CancellationToken, Task> validate,
            CancellationToken ct = default)
        {
            await validate(ct);
            if (RejectBeforeAccept) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            Calls.Add(id);
            await db.WorkflowRuns.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Pending)
                .SetProperty(r => r.PendingSignalJson, signal), ct);
            if (AfterAccept != null) await AfterAccept();
            if (RejectAfterAccept) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            if (ThrowAfterAccept) throw new InvalidOperationException("lost response");
        }
    }
}

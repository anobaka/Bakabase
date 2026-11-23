using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionLocalRecoveryBatchTests
{
    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task An_indeterminate_transfer_handoff_requires_an_unsubmitted_original_or_a_later_checkpoint(bool recorded, bool newWait)
    {
        using var f = await Fixture.CreateAsync();
        var task = await f.TaskAsync(1);
        var created = DateTimeOffset.UtcNow.AddHours(-1);
        var run = await f.Db.WorkflowRuns.SingleAsync(r => r.Id == task.WorkflowRunId);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(new() {Name = "save share", TriggerKind = "download.sources",
            Nodes = [new() {NodeId = "save", Kind = "baidu.transfer"}]});
        run.WaitingSince = created.AddMinutes(-10).LocalDateTime;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        run = await f.Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == task.WorkflowRunId);
        var version = AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run);
        var entry = new AcquisitionLocalRecoveryQueueEntry {BatchId = "old", Version = version, AttemptVersion = version,
            Signal = "{}", StorageStamp = "old", Status = "attention", ReasonCode = "LOCAL_BATCH_OUTCOME_UNKNOWN",
            CreatedAt = created, UpdatedAt = created.AddMinutes(2), RecoveryScope = "workflow", Mode = "automatic",
            WorkflowRunId = run.Id, PlanRevision = task.PlanRevision};
        var state = AcquisitionLocalRecoveryQueueEntry.Write(task.BuilderStateJson, entry);
        await f.Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.BuilderStateJson, state));
        if (recorded)
        {
            f.Db.CloudDownloadOperations.Add(new() {Id = "isolated", State = "planned", Kind = "transfer-recovery",
                WorkflowRunId = run.Id, UpdatedAt = created.AddMinutes(1).UtcDateTime});
            await f.Db.SaveChangesAsync();
        }
        if (newWait)
            await f.Db.WorkflowRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(r => r.WaitingSince, created.AddMinutes(3).LocalDateTime));
        var preview = await f.Service.PreviewAsync(new([task.Id], "automatic"));
        Assert.AreEqual(recorded && !newWait, preview.Rows.Single().ReasonCode == "LOCAL_BATCH_OUTCOME_UNKNOWN");
        Assert.HasCount(0, f.Host.Calls);
    }

    [TestMethod]
    public async Task CurrentTransferTakesDefaultContinueAndPastTransferRequiresItsOwnSelectedOption()
    {
        using var f = await Fixture.CreateAsync();
        var recovery = new AcquisitionRecoveryService(f.Db, f.Diagnostics, new(), f.Host);
        var continuation = new AcquisitionContinuationService(f.Db, recovery, new(), null!, null!, null!);
        var service = new AcquisitionLocalRecoveryBatchService(f.Db, recovery, f.Diagnostics, null!, null!,
            f.Options, new StoragePolicy(), f.Capacity, f.Previews, continuations: continuation,
            actionProviders: [new AcquisitionTransferReplayAction(f.Diagnostics)]);
        f.Diagnostics.OptionalTransferTask = 1;
        var preview = await service.PreviewAsync(new([1, 2], "automatic"));
        var one = preview.Rows.Single(r => r.TaskId == 1);
        var two = preview.Rows.Single(r => r.TaskId == 2);
        CollectionAssert.AreEqual(new[] {"continue", "retryTransfer"}, one.AvailableActions!.ToArray());
        CollectionAssert.AreEqual(new[] {"continue"}, two.AvailableActions!.ToArray());
        Assert.AreEqual("continue", one.DefaultAction);
        Assert.AreEqual("continue", two.DefaultAction);
        AcquisitionBatchActionSelection[] picks = [new(1, "retryTransfer"), new(2, "continue")];
        var capacity = await service.PreviewCapacityAsync(new(preview.PreviewToken, Selections: picks));
        CollectionAssert.AreEqual(picks, capacity.Selections!.ToArray());
        foreach (var invalid in new AcquisitionBatchActionSelection[][] {
                     [new(2, "retryTransfer")], [new(1, "reanalyze")], [new(1, "continue"), new(1, "retryTransfer")]})
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(new(preview.PreviewToken,
                ConfirmTransferRetry: true, Selections: invalid)));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(new(preview.PreviewToken, [1], Selections: picks)));
        Assert.IsNull(AcquisitionLocalRecoveryQueueEntry.Read(await f.TaskAsync(1)));
        var accepted = await service.ApplyAsync(new(preview.PreviewToken, Selections: picks));
        var retry = AcquisitionLocalRecoveryQueueEntry.Read(await f.TaskAsync(1))!;
        Assert.AreEqual("retryTransfer", retry.SelectedAction);
        Assert.IsTrue(retry.TransferRetryConfirmed);
        var normal = AcquisitionLocalRecoveryQueueEntry.Read(await f.TaskAsync(2))!;
        Assert.AreEqual("continue", normal.SelectedAction);
        Assert.IsFalse(normal.TransferRetryConfirmed);
        Assert.AreEqual(accepted.BatchId, (await service.ApplyAsync(new(preview.PreviewToken,
            Selections: picks.Reverse().ToArray()))).BatchId);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ApplyAsync(new(preview.PreviewToken, [1, 2])));
        Assert.AreEqual(0, f.Host.Calls.Count);

        // The provider can withdraw a replay after admission. The dispatcher must neither
        // silently switch to ordinary continue nor send the stale replay to the provider.
        f.Diagnostics.OptionalTransferTask = null;
        await service.TickAsync();
        await service.TickAsync();
        var rows = (await service.GetAsync(accepted.BatchId)).Rows;
        Assert.AreEqual("attention", rows.Single(r => r.TaskId == 1).Status);
        Assert.AreEqual("RECOVERY_CHECKPOINT_UNSUPPORTED", rows.Single(r => r.TaskId == 1).ReasonCode);
        CollectionAssert.AreEqual(new[] {22}, f.Host.Calls.ToArray());
    }

    [TestMethod]
    public async Task SelectedReanalysisRunsTheAssessmentWithItsPinnedIterationCountAndDoesNotRepeatAtTheNextTick()
    {
        using var f = await Fixture.CreateAsync();
        f.Options.Value.UseEmergencySpaceReserve = true;
        var recovery = new AcquisitionRecoveryService(f.Db, f.Diagnostics, new(), f.Host);
        var continuation = new AcquisitionContinuationService(f.Db, recovery, new(), null!, null!, null!);
        var adaptive = new AcquisitionAdaptiveMigrationService(f.Db, f.Host);
        var service = new AcquisitionLocalRecoveryBatchService(f.Db, recovery, f.Diagnostics, null!, null!, f.Options,
            new StoragePolicy(), f.Capacity, f.Previews, continuations: continuation, actionProviders: [new AcquisitionReanalysisAction(adaptive)]);
        var plan = WorkflowRunPlans.Serialize(new() {Name = "review", TriggerKind = "manual", Nodes = [
            new() {NodeId = "assess", Kind = AssessFileArtifactDeliveryActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(new FileArtifactDeliveryAssessmentConfig("p"), WorkflowJson.Options)}]});
        var prompt = JsonSerializer.Serialize(new {artifactId = "artifact1", reason = "FILE_DELIVERY_RESULT_UNCERTAIN"});
        await f.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(s => s.SetProperty(r => r.PlanSnapshotJson, plan)
            .SetProperty(r => r.WaitReason, "cloud.fileProcessing").SetProperty(r => r.WaitPromptJson, prompt));
        var queuedTask = await f.TaskAsync(1);
        var previous = new AcquisitionContinuationEntry {BatchId = "prior", PreviewToken = "prior", Version = "prior",
            Scope = "workflow", Settings = "{}", Paths = [], WorkflowRunId = 11, PlanRevision = 1};
        var previousState = AcquisitionContinuationEntry.Write(queuedTask.BuilderStateJson, previous);
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, previousState));
        var preview = await service.PreviewAsync(new([1, 2], "automatic", MaxAdaptiveIterations: 4));
        CollectionAssert.Contains(preview.Rows.Single(r => r.TaskId == 1).AvailableActions!.ToArray(), "reanalyze");
        CollectionAssert.DoesNotContain(preview.Rows.Single(r => r.TaskId == 2).AvailableActions!.ToArray(), "reanalyze");
        var analysis = new AcquisitionBatchActionSelection(1, "reanalyze");
        f.Capacity.Available = 5 * GiB;
        var capacity = await service.PreviewCapacityAsync(new(preview.PreviewToken, ReserveMode: "configured", Selections: [analysis]));
        Assert.IsTrue(capacity.RequiresSpaceConfirmation);
        Assert.AreEqual(50 * GiB, capacity.Volumes.Single().MinimumFreeBytes);
        var batch = await service.ApplyAsync(new(preview.PreviewToken, ReserveMode: "configured", Selections: [analysis, new(2, "continue")]));
        await service.TickAsync();
        Assert.AreEqual("spaceBlocked", (await service.GetAsync(batch.BatchId)).Status);
        Assert.AreEqual(0, f.Host.Reassessments.Count);
        f.Capacity.Available = 100 * GiB;
        await service.CommandAsync(batch.BatchId, "resume");
        for (var i = 0; i < 3; i++) await service.TickAsync();
        var run = await f.Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 11);
        var config = JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(WorkflowRunPlans.Read(run.PlanSnapshotJson)!
            .Nodes.Single().ConfigJson, WorkflowJson.Options)!;
        Assert.IsTrue(config.EnableAdaptiveAssessment);
        Assert.AreEqual(4, config.MaxAdaptiveIterations);
        CollectionAssert.AreEqual(new[] {11}, f.Host.Reassessments.ToArray());
        CollectionAssert.AreEqual(new[] {22}, f.Host.Calls.ToArray());
        Assert.IsTrue((await service.GetAsync(batch.BatchId)).Rows.All(r => r.Status == "running"));
        var grant = AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1));
        Assert.AreEqual("configured", grant?.ReserveMode);
        using var services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(
            new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).Options)).BuildServiceProvider();
        var writes = new AcquisitionStorageWritePolicyProvider(services.GetRequiredService<IServiceScopeFactory>(), f.Options);
        Assert.AreEqual(50 * GiB, await writes.GetMinimumFreeBytesAsync(new("workflow", "11")));
    }

    [TestMethod]
    public async Task LegacyTaskIdsNeverSelectAlternativeEvenWhenOldClientSendsBlanketConfirmation()
    {
        using var f = await Fixture.CreateAsync();
        var preview = await f.Service.PreviewAsync(new([1]));
        await f.Service.ApplyAsync(new(preview.PreviewToken, [1], ConfirmTransferRetry: true));
        var saved = AcquisitionLocalRecoveryQueueEntry.Read(await f.TaskAsync(1))!;
        Assert.AreEqual("continue", saved.SelectedAction);
        Assert.IsFalse(saved.TransferRetryConfirmed);
    }

    [TestMethod]
    public async Task AutomaticPreviewExplainsWholeBatchBlockAndMovesOnlySelectedIdleRows()
    {
        using var f = await Fixture.CreateAsync();
        f.Capacity.Available = 50 * GiB;
        var initial = await f.Service.PreviewAsync(new([1, 2]));
        var old = await f.Service.ApplyAsync(new(initial.PreviewToken, [1, 2]));
        await f.Service.TickAsync();
        var preview = await f.Service.PreviewAsync(new([1, 2], "automatic"));
        Assert.IsTrue(preview.Rows.All(r => r.CanQueue && r.Queue?.CanRequeue == true && r.Queue.Status == "spaceBlocked"));
        var fresh = await f.Service.ApplyAsync(new(preview.PreviewToken, [1]));
        Assert.AreNotEqual(old.BatchId, fresh.BatchId);
        var previous = await f.Service.GetAsync(old.BatchId);
        Assert.AreEqual("cancelled", previous.Rows.Single(r => r.TaskId == 1).Status);
        Assert.AreEqual("LOCAL_BATCH_REQUEUED", previous.Rows.Single(r => r.TaskId == 1).ReasonCode);
        Assert.AreEqual("spaceBlocked", previous.Rows.Single(r => r.TaskId == 2).Status);
        Assert.AreEqual(0, f.Host.Calls.Count);
        Assert.IsFalse(fresh.AllowStorageOverride);
        Assert.AreEqual(fresh.BatchId, (await f.Service.ApplyAsync(new(preview.PreviewToken, [1]))).BatchId);
    }

    [TestMethod]
    public async Task QueueChangedAfterPreviewRejectsWholeTakeoverWithoutPartialCancellation()
    {
        using var f = await Fixture.CreateAsync();
        var first = await f.Service.PreviewAsync(new([1, 2]));
        var batch = await f.Service.ApplyAsync(new(first.PreviewToken, [1, 2]));
        var next = await f.Service.PreviewAsync(new([1, 2], "automatic"));
        await f.Service.CommandAsync(batch.BatchId, "pause");
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.ApplyAsync(new(next.PreviewToken, [1, 2])));
        Assert.IsTrue((await f.Service.GetAsync(batch.BatchId)).Rows.All(r => r.Status == "paused"));
        Assert.AreEqual(1, (await f.Service.ListAsync()).Count);
    }

    [TestMethod]
    public async Task AutomaticDoesNotTakeOverAnAcceptedOrIndeterminateExecution()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1]));
        await f.Service.ApplyAsync(new(p.PreviewToken, [1]));
        await f.Service.TickAsync();
        var active = (await f.Service.PreviewAsync(new([1], "automatic"))).Rows.Single();
        Assert.IsFalse(active.CanQueue); Assert.IsFalse(active.Queue!.CanRequeue);
        Assert.AreEqual("running", active.Queue.RowStatus);
        Assert.AreEqual(1, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task MetadataPreviewAndSubsetCapacityDoNotHashOrQueueAndCapacityPrecedesVerification()
    {
        using var f = await Fixture.CreateAsync();
        f.Diagnostics.RequireVerification = true;
        var preview = await f.Service.PreviewAsync(new([1, 2], "automatic"));
        Assert.IsTrue(preview.Rows.All(r => r.CanQueue && r.CanOverrideStorage && r.ActionCategory == "localProcessing"));
        Assert.IsTrue(f.Diagnostics.Verifications.All(v => !v));
        var count = f.Diagnostics.Verifications.Count;
        var selected = await f.Service.PreviewCapacityAsync(new(preview.PreviewToken, [2]));
        CollectionAssert.AreEqual(new[] {2}, selected.TaskIds.ToArray());
        Assert.AreEqual(100L, selected.Volumes.Single().EstimatedAdditionalBytes);
        Assert.AreEqual(count, f.Diagnostics.Verifications.Count);
        var batch = await f.Service.ApplyAsync(new(preview.PreviewToken, [2]));
        f.Capacity.Available = 50 * GiB;
        await f.Service.TickAsync();
        Assert.AreEqual("spaceBlocked", (await f.Service.GetAsync(batch.BatchId)).Status);
        Assert.AreEqual(count, f.Diagnostics.Verifications.Count, "A known capacity stop precedes expensive hashing.");
        Assert.AreEqual(0, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task ExplicitRequeueRevokesOldGrantWithoutImplicitlyRenewingOrCopyingIt()
    {
        using var f = await Fixture.CreateAsync();
        var p = await f.Service.PreviewAsync(new([1]));
        var old = await f.Service.ApplyAsync(new(p.PreviewToken, [1], true));
        await f.Service.TickAsync();
        await f.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Waiting)
            .SetProperty(r => r.PendingSignalJson, (string?)null).SetProperty(r => r.WaitPromptJson, "{\"message\":\"LOCAL_STORAGE_FULL\"}"));
        await f.Service.TickAsync();
        var preview = await f.Service.PreviewAsync(new([1], "automatic"));
        var fresh = await f.Service.ApplyAsync(new(preview.PreviewToken, [1]));
        Assert.IsFalse(fresh.AllowStorageOverride); Assert.IsNull(fresh.OverrideExpiresAt);
        Assert.AreEqual("revoked", AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1))!.Status);
        Assert.AreEqual(old.OverrideExpiresAt, AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1))!.ExpiresAt);
        await f.Service.TickAsync();
        Assert.AreEqual(2, f.Host.Calls.Count, "The new frozen version includes the retained revoked old grant.");
    }

    [DataTestMethod]
    [DataRow("cloud.fileProcessing", "DiskSpaceLimit", "LOCAL_STORAGE_FULL")]
    [DataRow("cloud.fileProcessing", "LOCAL_STORAGE_FULL", "LOCAL_STORAGE_FULL")]
    [DataRow("cloud.fileProcessing", "ArchiveStorageUnavailable", null)]
    [DataRow("other", "DiskSpaceLimit", null)]
    public void TypedSpaceWaitMatchesFactsWithoutGuessingFromMessages(string wait, string reason, string? expected)
    {
        Assert.AreEqual(expected, AcquisitionLocalRecoveryBatchService.StorageProblem(new()
            {WaitReason = wait, WaitPromptJson = JsonSerializer.Serialize(new {reason, message = "unclassified explanatory text"})}));
    }
}

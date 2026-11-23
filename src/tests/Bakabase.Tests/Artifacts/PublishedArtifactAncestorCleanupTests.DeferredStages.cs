using Bakabase.Modules.Acquisition.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    // The local test filesystem unlinks open files immediately. Simulate only the SMB
    // client's marker retention; persist and consume the same verified stage receipt as the service.
    private async Task<(StageDeletionFixture Fixture, string Marker)> PendingStage(string otherStatus = "notRequested",
        bool unrelatedFailure = false, bool useDelivery = false, bool live = false)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Assert.Inconclusive("Automatic empty-directory cleanup requires supported directory handles.");
        var f = await StageDeletion();
        var plan = await f.Cleanup.PreviewAsync(f.Task, [f.Task.WorkflowRunId!.Value], true, true,
            default, cleanupIntermediates: !useDelivery, deleteDeliveredFiles: useDelivery, directDirectoryDeletion: true);
        var stage = plan.StageDirectories!.Single(s => s.RootDirectory == (useDelivery ? f.Delivery : f.Download));
        Assert.IsTrue(stage.Group.Eligible, stage.Group.ReasonCode);
        // All normal files in the selected stage were removed during the user's own confirmation.
        foreach (var file in Directory.EnumerateFiles(stage.RootDirectory, "*", SearchOption.AllDirectories)) File.Delete(file);
        var marker = Path.Combine(Directory.CreateDirectory(Path.Combine(stage.RootDirectory, "nested")).FullName, ".smbdelete01.1");
        await File.WriteAllTextAsync(marker, "open handle");
        var receipt = new AcquisitionStageDirectoryCleanupReceipt(Guid.NewGuid().ToString("N"), f.Task.Id,
            f.Task.PlanRevision, stage.PartId, stage.Stage, stage.RootDirectory, stage.BindingFingerprint,
            DateTime.UtcNow, "Prepared", [], plan.Consent! with {ConfirmedAt = DateTimeOffset.UtcNow}) {LiveTaskVersion = live ? plan.TaskVersion : null};
        var (reason, id) = await f.Cleanup.RecordRetainedStageAsync(stage, receipt, "Partial", true, 1, 1);
        Assert.AreEqual(DeferredStageCleanup.PendingReason, reason);
        Assert.AreEqual(receipt.Id, id);
        var outcome = new AcquisitionTaskDeleteCleanupOutcome(f.Task.Id, false, 1, 1, [reason])
        {
            UnconfirmedScopes = stage.Group.Scopes,
            ManualReviewScopes = unrelatedFailure ? stage.Group.Scopes : AcquisitionCleanupScopes.None,
            DeferredReceipts = [(id!, stage.Group.Scopes)]
        };
        var status = DeferredStageCleanup.Status(outcome, stage.Group.Scopes);
        if (live)
        {
            Assert.IsTrue(await f.Service.RecordPendingTaskRemovalAsync(f.Task, outcome,
                new(!useDelivery, true, true, false, false, useDelivery), default));
            return (f, marker);
        }
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.DeletedAt, DateTime.UtcNow)
            .SetProperty(t => t.DeletedIntermediateCleanup, useDelivery ? otherStatus : status)
            .SetProperty(t => t.DeletedDeliveryCleanup, useDelivery ? status : otherStatus)
            .SetProperty(t => t.DeletedCleanupReasons, unrelatedFailure ? reason + ",WorkspaceUnavailable" : reason));
        return (f, marker);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Default_delete_keeps_record_until_confirmed_empty_stage_is_gone(bool editAfterConfirmation)
    {
        var (f, marker) = await PendingStage(useDelivery: true, live: true);
        await f.Cleanup.FinishPendingSmbBatchAsync(0, default);
        Assert.IsNull((await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id)).DeletedAt);
        if (editAfterConfirmation)
            await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.UpdatedAt, f.Task.UpdatedAt.AddMinutes(1)));
        File.Delete(marker);
        await f.Cleanup.FinishPendingSmbBatchAsync(0, default);
        var row = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        Assert.AreEqual(editAfterConfirmation, row.DeletedAt == null);
        Assert.AreEqual(editAfterConfirmation, Directory.Exists(f.Delivery));
        Assert.IsTrue(File.Exists(Path.Combine(f.Download, "original.bin")));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Released_SMB_marker_retires_only_confirmed_stage_after_gates_are_available(bool useDelivery)
    {
        var (f, marker) = await PendingStage(useDelivery: useDelivery);
        // A long-lived SMB handle must not wait for unrelated artifact writers.
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        await using (await store.AcquireMaintenanceAsync(timeout.Token))
            await f.Cleanup.FinishPendingSmbBatchAsync(0, timeout.Token);
        var saved = (await f.Service.GetDeletedTasksAsync(1, 25, "incomplete", default)).Items.Single();
        CollectionAssert.Contains(saved.LastReasonCodes.ToArray(), "CLEANUP_SMB_AUTO_PENDING");
        Assert.IsTrue(File.Exists(marker));

        File.Delete(marker);
        var gate = AcquisitionTaskGate.For(f.Task.Id);
        await gate.WaitAsync();
        try { await f.Cleanup.FinishPendingSmbBatchAsync(0, default); }
        finally { gate.Release(); }
        var stage = useDelivery ? f.Delivery : f.Download;
        Assert.IsTrue(Directory.Exists(stage));
        await f.Cleanup.FinishPendingSmbBatchAsync(0, default);
        await f.Cleanup.FinishPendingSmbBatchAsync(0, default);
        Assert.IsFalse(Directory.Exists(stage));
        Assert.IsTrue(useDelivery ? File.Exists(Path.Combine(f.Download, "original.bin")) : f.Publication.Files.All(File.Exists));
        var row = (await f.Service.GetDeletedTasksAsync(1, 25, "all", default)).Items.Single();
        Assert.AreEqual("completed", useDelivery ? row.DeliveryCleanup : row.IntermediateCleanup);
        Assert.AreEqual("notRequested", useDelivery ? row.IntermediateCleanup : row.DeliveryCleanup);
        Assert.AreEqual(0, row.LastReasonCodes.Count);
    }

    [TestMethod]
    public async Task New_regular_file_revokes_automatic_cleanup_and_preserves_both_scopes()
    {
        var (f, marker) = await PendingStage(otherStatus: "completed");
        File.Delete(marker);
        var incoming = Path.Combine(f.Download, "new.bin");
        await File.WriteAllTextAsync(incoming, "new user content");
        await f.Cleanup.FinishPendingSmbBatchAsync(0, default);
        Assert.AreEqual("new user content", await File.ReadAllTextAsync(incoming));
        var row = (await f.Service.GetDeletedTasksAsync(1, 25, "incomplete", default)).Items.Single();
        Assert.AreEqual("completed", row.DeliveryCleanup);
        CollectionAssert.AreEquivalent(new[] {"CLEANUP_FILES_RETAINED"}, row.LastReasonCodes.ToArray());
        File.Delete(incoming);
        await f.Cleanup.FinishPendingSmbBatchAsync(0, default);
        Assert.IsTrue(Directory.Exists(f.Download)); // No fresh confirmation.
    }

    [TestMethod]
    public async Task Another_failure_in_same_scope_remains_visible_after_empty_tree_finishes()
    {
        var (f, marker) = await PendingStage(unrelatedFailure: true);
        File.Delete(marker);
        await f.Cleanup.FinishPendingSmbBatchAsync(0, default);
        Assert.IsFalse(Directory.Exists(f.Download));
        var row = (await f.Service.GetDeletedTasksAsync(1, 25, "incomplete", default)).Items.Single();
        Assert.AreEqual("incomplete", row.IntermediateCleanup);
        CollectionAssert.AreEquivalent(new[] {"WorkspaceUnavailable"}, row.LastReasonCodes.ToArray());
    }

    [TestMethod]
    public async Task Changed_task_revision_never_permits_directory_removal()
    {
        var (f, marker) = await PendingStage();
        File.Delete(marker);
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.PlanRevision, t => t.PlanRevision + 1));
        await f.Cleanup.FinishPendingSmbBatchAsync(0, default);
        Assert.IsTrue(Directory.Exists(f.Download));
        var row = (await f.Service.GetDeletedTasksAsync(1, 25, "incomplete", default)).Items.Single();
        CollectionAssert.AreEquivalent(new[] {"CLEANUP_FILES_RETAINED"}, row.LastReasonCodes.ToArray());
    }
}

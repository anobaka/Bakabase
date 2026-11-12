using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionLocalRecoveryBatchTests
{
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

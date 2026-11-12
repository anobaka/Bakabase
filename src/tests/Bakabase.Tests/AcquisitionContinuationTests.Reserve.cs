using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionContinuationTests
{
    [TestMethod]
    public void InvalidSavedPlanDoesNotAdvertiseReserveOverrideOrBreakSiblingPreviewRows()
    {
        Assert.IsFalse(AcquisitionContinuationReserve.Supports(new() {CurrentStepIndex = 0, PlanSnapshotJson = "{}"}));
        Assert.IsFalse(AcquisitionContinuationReserve.Supports(new() {CurrentStepIndex = 0, PlanSnapshotJson = "invalid"}));
    }

    [TestMethod]
    public async Task OmittedDownloadChoiceReturnsToGlobalInsteadOfRevivingAnOldAttemptGrant()
    {
        using var f = await Fixture.Create();
        var plan = WorkflowRunPlans.Serialize(new() {Name = "download", TriggerKind = "manual",
            Nodes = [new() {NodeId = "download", Kind = "download.awaitCompletion"}]});
        await f.Db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.CurrentStepIndex, 0).SetProperty(r => r.PlanSnapshotJson, plan));
        var now = DateTimeOffset.UtcNow;
        var json = AcquisitionStorageOverrideGrant.WithState("{}", new(1, 1, "old-attempt", now, now.AddHours(24)));
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json));
        var p = await f.Service.PreviewAsync(new([1], "workflow"), false);
        await f.Service.ApplyAsync(new(p.PreviewToken, [1]), false); await f.Service.TickAsync(default);
        var grant = AcquisitionStorageOverrideGrant.Read(await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1))!;
        Assert.AreEqual("inherit", grant.ReserveMode); Assert.AreEqual(1, f.Host.Calls);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeferredDownloadChoiceIsStoredUntilOneVerifiedDispatchAndBoundToReplay(bool emergency)
    {
        using var f = await Fixture.Create();
        var plan = WorkflowRunPlans.Serialize(new() {Name = "download", TriggerKind = "manual",
            Nodes = [new() {NodeId = "download", Kind = "download.awaitCompletion"}]});
        await f.Db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.CurrentStepIndex, 0).SetProperty(r => r.PlanSnapshotJson, plan));
        var p = await f.Service.PreviewAsync(new([1], "workflow"), false);
        Assert.IsTrue(p.Rows.Single().CanOverrideStorage);
        var input = new AcquisitionContinuationApplyInput(p.PreviewToken, [1], emergency);
        var job = await f.Service.ApplyAsync(input, false);
        var task = await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        Assert.AreEqual(emergency, AcquisitionContinuationEntry.Read(task)!.UseEmergencySpaceReserve);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(task));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.ApplyAsync(input with {UseEmergencySpaceReserve = !emergency}, false));
        await f.Service.TickAsync(default);
        task = await f.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        var grant = AcquisitionStorageOverrideGrant.Read(task)!;
        Assert.IsNotNull(grant); Assert.AreEqual(emergency ? "emergency" : "configured", grant.ReserveMode);
        Assert.AreEqual(1, grant.WorkflowRunId); Assert.AreEqual(1, grant.PlanRevision);
        Assert.AreEqual("submitted", (await f.Service.GetAsync(job.BatchId)).Rows.Single().Status);
        await f.Service.TickAsync(default); Assert.AreEqual(1, f.Host.Calls);
        Assert.AreEqual(0, await f.Db.DownloadTasks.CountAsync());
    }
}

using System.Text.Json;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.InsideWorld.Business;

namespace Bakabase.Tests;

public sealed partial class AcquisitionLocalRecoveryBatchTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BatchInheritDoesNotCarryAnOlderOppositeReserveIntoTheNextContinuation(bool emergency)
    {
        using var f = await Fixture.CreateAsync();
        f.Options.Value.UseEmergencySpaceReserve = emergency;
        var task = await f.TaskAsync(1); var now = DateTimeOffset.UtcNow;
        var old = new AcquisitionStorageOverrideGrant(11, 1, "previous-single", now, now.AddHours(24),
            ReserveMode: emergency ? "configured" : "emergency");
        var state = AcquisitionStorageOverrideGrant.WithState(task.BuilderStateJson, old);
        await f.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, state));
        using var services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(
            new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).Options)).BuildServiceProvider();
        var provider = new AcquisitionStorageWritePolicyProvider(services.GetRequiredService<IServiceScopeFactory>(), f.Options);
        Assert.AreEqual(emergency ? 50 * GiB : GiB, await provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        var preview = await f.Service.PreviewAsync(new([1]));
        var batch = await f.Service.ApplyAsync(new(preview.PreviewToken, [1], ReserveMode: "inherit"));
        await f.Service.TickAsync();
        Assert.AreEqual(1, f.Host.Calls.Count);
        var grant = AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1))!;
        Assert.AreEqual("inherit", grant.ReserveMode); Assert.AreEqual("batch:" + batch.BatchId, grant.Token);
        Assert.IsFalse(batch.AllowStorageOverride); Assert.IsNull(batch.OverrideExpiresAt);
        Assert.AreEqual(emergency ? GiB : 50 * GiB, await provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AutomaticDownloadQueueUsesTheSelectedReserveWithoutRenewingItsGrant(bool emergency)
    {
        using var f = await Fixture.CreateAsync();
        f.Options.Value.UseEmergencySpaceReserve = !emergency;
        var recovery = new AcquisitionRecoveryService(f.Db, f.Diagnostics, new(), f.Host);
        var continuation = new AcquisitionContinuationService(f.Db, recovery, new(), null!, null!, null!);
        var service = new AcquisitionLocalRecoveryBatchService(f.Db, recovery, f.Diagnostics, null!, null!,
            f.Options, new StoragePolicy(), f.Capacity, f.Previews, continuations: continuation);
        var physical = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "batch-download"));
        var plan = WorkflowRunPlans.Serialize(new() {Name = "download", TriggerKind = "manual",
            Nodes = [new() {NodeId = "download", Kind = "download.awaitCompletion"}, new() {NodeId = "publish", Kind = "fs.publishArtifacts"}]});
        var snapshot = WorkflowItemSnapshot.Capture(new DownloadWorkset {OutputDirectory = physical,
            Targets = [new() {Id = "target", DownloadDirectory = physical}]});
        await f.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.PlanSnapshotJson, plan).SetProperty(r => r.CurrentItemJson, snapshot));
        var p = await service.PreviewAsync(new([1], "automatic"));
        Assert.IsTrue(p.Rows.Single().CanQueue, p.Rows.Single().ReasonCode);
        Assert.IsTrue(p.Rows.Single().CanOverrideStorage); Assert.AreEqual("workflow", p.Rows.Single().ActionCategory);
        var mode = emergency ? "emergency" : "configured";
        var capacity = await service.PreviewCapacityAsync(new(p.PreviewToken, [1], mode));
        Assert.AreEqual(emergency ? GiB : 50 * GiB, capacity.Volumes.Single().MinimumFreeBytes);
        var batch = await service.ApplyAsync(new(p.PreviewToken, [1], ReserveMode: mode));
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1)));
        await service.TickAsync();
        Assert.AreEqual(1, f.Host.Calls.Count, (await service.GetAsync(batch.BatchId)).Rows.Single().ReasonCode);
        var grant = AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1))!;
        Assert.AreEqual(mode, grant.ReserveMode); Assert.AreEqual("batch:" + batch.BatchId, grant.Token);
        Assert.AreEqual(AcquisitionLocalRecoveryQueueEntry.Read(await f.TaskAsync(1))!.OverrideExpiresAt, grant.ExpiresAt);
        Assert.AreEqual(!emergency, f.Options.Value.UseEmergencySpaceReserve);
        await service.TickAsync(); Assert.AreEqual(1, f.Host.Calls.Count);
    }

    [TestMethod]
    public async Task DownloadCapacityUsesActualRetainedTargetAndNeverHistoricPromptOrFutureDelivery()
    {
        using var f = await Fixture.CreateAsync();
        var physical = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "download-capacity-ssd"));
        var old = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "old-nas"));
        f.Db.DownloadTasks.Add(new() {Id = 100, DownloadPath = physical}); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var plan = WorkflowRunPlans.Serialize(new() {Name = "download", TriggerKind = "manual",
            Nodes = [new() {NodeId = "download", Kind = "download.awaitCompletion"}]});
        var snapshot = WorkflowItemSnapshot.Capture(new DownloadWorkset {OutputDirectory = old,
            Targets = [new() {Id = "target", DownloadTaskId = 100, DownloadDirectory = old}]});
        var prompt = JsonSerializer.Serialize(new {outputDirectory = old});
        await f.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.PlanSnapshotJson, plan).SetProperty(r => r.CurrentItemJson, snapshot)
            .SetProperty(r => r.WaitPromptJson, prompt));
        var preview = await f.Service.PreviewAsync(new([1], "capacityOnly"));
        Assert.AreEqual("", preview.PreviewToken); Assert.IsFalse(preview.Rows.Single().CanQueue);
        Assert.AreEqual(0, f.Diagnostics.Verifications.Count);
        CollectionAssert.AreEqual(new[] {physical}, preview.Volumes.Single().Paths.ToArray());
        Assert.AreEqual(0, f.Host.Calls.Count); Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await f.TaskAsync(1)));
        await f.Db.DownloadTasks.Where(d => d.Id == 100).ExecuteDeleteAsync();
        var missing = await f.Service.PreviewAsync(new([1], "capacityOnly"));
        Assert.AreEqual(0, missing.Volumes.Count); Assert.AreEqual("LOCAL_BATCH_CAPACITY_UNKNOWN", missing.Rows.Single().ReasonCode);
    }
}

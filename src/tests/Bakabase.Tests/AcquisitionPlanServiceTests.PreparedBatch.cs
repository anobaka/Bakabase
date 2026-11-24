using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionPlanServiceTests
{
    private readonly BatchCapacityReader _batchCapacity = new();
    private AcquisitionLocalRecoveryBatchService Batch()
    {
        var storage = _services.GetRequiredService<IUserStoragePolicy>();
        var artifacts = _services.GetRequiredService<IArtifactOperationStore>();
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, storage, artifacts, plans: _plans);
        var local = new AcquisitionLocalFilesRecoveryService(_db, _plans, storage, artifacts, null!, _services);
        var recovery = new AcquisitionRecoveryService(_db, diagnostics, new(), _services.GetRequiredService<IWorkflowRunResumer>(),
            _plans, localFiles: local);
        var continuation = new AcquisitionContinuationService(_db, recovery, new(), _plans, null!, artifacts);
        var options = new TestBOptions<AcquisitionOptions>(new() {MinimumFreeSpaceBytes = 50L << 30});
        return new(_db, recovery, diagnostics, local, _plans, options, storage, _batchCapacity, new(), continuations: continuation);
    }

    [TestMethod]
    public async Task AutomaticBatchQueuesPreparedPlansAndBindsExactlyOneRunToEachSelectedTask()
    {
        var batch = Batch();
        var first = await CreateBatchPlan("one");
        var second = await CreateBatchPlan("two");
        var ids = new[] {first.Id, second.Id};
        // Older prepared tasks predate per-stage bindings. Their saved publication map still applies.
        var oldPlan = second.Plan! with {StageDirectories = null};
        await _db.AcquisitionTasks.Where(t => t.Id == second.Id).ExecuteUpdateAsync(u =>
            u.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(oldPlan, JsonSerializerOptions.Web)));

        var onlyLocal = await batch.PreviewAsync(new(ids, "localOnly"));
        Assert.IsTrue(onlyLocal.Rows.All(r => !r.CanQueue));
        var preview = await batch.PreviewAsync(new(ids, "automatic"));
        Assert.AreEqual(2, preview.Rows.Count);
        foreach (var row in preview.Rows)
        {
            Assert.IsTrue(row.CanQueue, row.ReasonCode);
            Assert.AreEqual("RECOVERY_PREPARED_PLAN_READY", row.ReasonCode);
            Assert.AreEqual("start", row.RecoveryAction);
            Assert.AreEqual("continue", row.DefaultAction);
            Assert.IsNull(row.WorkflowRunId);
        }
        var paths = preview.Volumes.SelectMany(v => v.Paths).ToArray();
        var stages = first.Plan!.StageDirectories!.Parts["main"];
        foreach (var directory in new[] {stages.Download!.Directory, stages.Processing!.Directory, stages.Delivery!.Directory,
                     oldPlan.DeliveryLayout!.PartDirectories["main"]})
            CollectionAssert.Contains(paths, directory);
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
        var request = new AcquisitionLocalRecoveryBatchApplyInput(preview.PreviewToken, ids, ReserveMode: "configured");
        var admitted = await batch.ApplyAsync(request);
        Assert.IsTrue(admitted.Rows.All(r => r.Status == "queued" && r.WorkflowRunId == null));
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
        await batch.TickAsync();
        await batch.TickAsync();
        await batch.TickAsync();
        Assert.AreEqual(admitted.BatchId, (await batch.ApplyAsync(request)).BatchId);
        var current = await _db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync();
        Assert.AreEqual(2, await _db.WorkflowRuns.CountAsync());
        Assert.AreEqual(2, current.Select(t => t.WorkflowRunId).Distinct().Count());
        foreach (var task in current)
        {
            var queue = AcquisitionLocalRecoveryQueueEntry.Read(task)!;
            Assert.IsNotNull(task.WorkflowRunId, queue.ReasonCode);
            Assert.AreEqual(task.WorkflowRunId, queue.WorkflowRunId);
            Assert.AreEqual(admitted.BatchId, queue.BatchId);
            Assert.AreEqual(task.PlanRevision, queue.PlanRevision);
            Assert.AreEqual("batch:" + admitted.BatchId, AcquisitionStorageOverrideGrant.Read(task)?.Token);
        }
    }

    [TestMethod]
    public async Task AutomaticBatchRejectsEditedPreparedPlanAtApplyAndDispatch()
    {
        var batch = Batch();
        var plan = await CreateBatchPlan("stale");
        var preview = await batch.PreviewAsync(new([plan.Id], "automatic"));
        Assert.IsTrue(preview.Rows.Single().CanQueue, preview.Rows.Single().ReasonCode);
        await BumpRevision();
        var stale = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            batch.ApplyAsync(new(preview.PreviewToken, [plan.Id])));
        Assert.AreEqual("ACQUISITION_TASK_CHANGED", stale.Message);
        var fresh = await batch.PreviewAsync(new([plan.Id], "automatic"));
        var accepted = await batch.ApplyAsync(new(fresh.PreviewToken, [plan.Id]));
        await BumpRevision();
        await batch.TickAsync();
        var row = (await batch.GetAsync(accepted.BatchId)).Rows.Single();
        Assert.AreEqual("skipped", row.Status);
        Assert.AreEqual("ACQUISITION_TASK_CHANGED", row.ReasonCode);
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());

        Task BumpRevision() => _db.AcquisitionTasks.Where(t => t.Id == plan.Id).ExecuteUpdateAsync(u =>
            u.SetProperty(t => t.PlanRevision, t => t.PlanRevision + 1));
    }

    [TestMethod]
    public async Task AutomaticBatchRechecksUnavailablePreparedSourceBeforeStarting()
    {
        var batch = Batch();
        var prepared = await CreateBatchPlan("unavailable");
        var preview = await batch.PreviewAsync(new([prepared.Id], "automatic"));
        Assert.IsTrue(preview.Rows.Single().CanQueue, preview.Rows.Single().ReasonCode);
        var accepted = await batch.ApplyAsync(new(preview.PreviewToken, [prepared.Id]));
        var part = prepared.Plan!.Parts.Single();
        var source = AcquisitionPlanCompiler.ToSource(part.Sources.Single(), part);
        _availabilityProbe.Unavailable.Add(source.Url);
        await _services.GetRequiredService<SourceAvailabilityService>().CheckAsync(source, true, default);
        await batch.TickAsync();
        var row = (await batch.GetAsync(accepted.BatchId)).Rows.Single();
        Assert.AreEqual("attention", row.Status);
        Assert.AreEqual("SOURCE_CONFIRMED_UNAVAILABLE", row.ReasonCode);
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task AutomaticBatchCannotClaimAnotherStartWhenItsWorkflowAssociationIsMissing()
    {
        var batch = Batch();
        var task = await CreateBatchPlan("other-start");
        var preview = await batch.PreviewAsync(new([task.Id], "automatic"));
        var admitted = await batch.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        _db.WorkflowRuns.Add(new WorkflowRunDbModel {StartedAt = DateTime.Now, Sources = [new()
            {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = task.Task.PlanRevision.ToString()}]});
        await _db.SaveChangesAsync();

        await batch.TickAsync();

        var row = (await batch.GetAsync(admitted.BatchId)).Rows.Single();
        Assert.AreEqual("attention", row.Status);
        Assert.AreEqual("RECOVERY_CHECKPOINT_UNSUPPORTED", row.ReasonCode);
        Assert.IsNull(row.WorkflowRunId);
        Assert.IsNull((await _db.AcquisitionTasks.AsNoTracking().SingleAsync()).WorkflowRunId);
        Assert.AreEqual(1, await _db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task AutomaticBatchGuardsPreparedPathsAndRechecksTheirCapacityAtDispatch()
    {
        var batch = Batch();
        var prepared = await CreateBatchPlan("space");
        var selected = new AcquisitionLocalRecoveryBatchPreviewInput([prepared.Id], "automatic");
        var download = prepared.Plan!.StageDirectories!.Parts["main"].Download!.Directory;
        var forbidden = await batch.PreviewAsync(selected, guardDirectory:
            p => p == download ? throw new UnauthorizedAccessException() : Task.CompletedTask);
        Assert.AreEqual("LOCAL_BATCH_PATH_FORBIDDEN", forbidden.Rows.Single().ReasonCode);
        var preview = await batch.PreviewAsync(selected);
        var accepted = await batch.ApplyAsync(new(preview.PreviewToken, [prepared.Id]));
        _batchCapacity.Available = 0;
        await batch.TickAsync();
        var row = (await batch.GetAsync(accepted.BatchId)).Rows.Single();
        Assert.AreEqual("spaceBlocked", row.Status);
        Assert.AreEqual(0, await _db.WorkflowRuns.CountAsync());
    }

    private sealed class BatchCapacityReader : IAcquisitionBatchCapacityReader
    {
        internal long Available = 1L << 40;
        public StorageVolumeCapacity Measure(string path) => new("fixture", Available, 1L << 41);
        public string Identity(StorageVolumeCapacity capacity) => capacity.VolumePath;
    }

    private Task<AcquisitionPlanTaskDetail> CreateBatchPlan(string name) => _plans.CreatePreparedPlanAsync(
        _builder.BuildPlan(new(name, name, "https://example.org/" + name + ".zip"), name, Path.GetTempPath()), []);
}

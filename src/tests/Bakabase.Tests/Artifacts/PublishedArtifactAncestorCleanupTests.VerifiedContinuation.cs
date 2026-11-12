using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

[DoNotParallelize] // Process-wide task/run gates are shared by the isolated database fixtures.
public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private sealed class ContinuationActivity : IAcquisitionDeletionActivityReader
    {
        internal string? BlockReason;
        internal Func<int, Task>? OnRead;
        private int reads;
        public async Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(IReadOnlyList<AcquisitionTaskDbModel> tasks, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (OnRead != null) await OnRead(++reads);
            return tasks.ToDictionary(t => t.Id, _ => new AcquisitionDeletionActivity([1, 3], "fixture", BlockReason));
        }
    }

    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Source, ArtifactRecord Final,
        AcquisitionVerifiedDeliveryRunRetirement Retirement, ContinuationActivity Activity)> VerifiedContinuation()
    {
        var source = await Artifact("processedFiles", owner: 3);
        var final = await Artifact("publishedFiles", source);
        // This fixture must model the actual one-slot-per-part publication contract.
        await db.ArtifactOperationBindings.Where(b => b.OperationId == final.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Slot, "main:artifact.publish"));
        var old = new AcquisitionPlan {Title = "old", Parts = [new() {Id = "main"}]};
        var current = old with {Title = "current"};
        var history = new[] {new AcquisitionPlanRevision(1, old, [], DateTime.UtcNow.AddDays(-1)),
            new AcquisitionPlanRevision(2, current, [], DateTime.UtcNow, "continueFiles", 3)};
        var task = new AcquisitionTaskDbModel {Title = "Verified continuation", Status = AcquisitionStatus.Completed,
            PlanRevision = 2, WorkflowRunId = 1, PlanJson = JsonSerializer.Serialize(current, Json),
            PlanHistoryJson = JsonSerializer.Serialize(history, Json)};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Set<WorkflowRunSourceDbModel>().AddRange(
            new() {WorkflowRunId = 1, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"},
            new() {WorkflowRunId = 3, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        db.WorkflowNodeExecutions.Add(new() {WorkflowRunId = 3, NodeId = "download.wait", Kind = "cloud.await", Status = "waiting",
            OutputsJson = "[{\"label\":\"old output\"}]"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.IsTrue(await store.PinConsumerAsync(source.OperationId, "old-owner", "workflow", "3"));
        var activity = new ContinuationActivity();
        var retirement = new AcquisitionVerifiedDeliveryRunRetirement(db, store, activity, new(db, activity, root));
        cleanup = new(db, store, new ArchiveQuarantineService(store, root, new Storage()),
            [new AcquisitionArtifactCleanupOwnerScope(db)], ownerPreparations: [retirement]);
        return (task, source, final, retirement, activity);
    }

    [TestMethod]
    public async Task Background_cleanup_rechecks_after_progress_and_settles_after_all_processed_layers_retire()
    {
        var raw = await Artifact("download");
        var layers = new List<ArtifactRecord>(); var parent = raw;
        for (var index = 0; index < 3; index++)
        { parent = await Artifact("processedFiles", parent); layers.Add(parent); }
        var final = await Artifact("publishedFiles", parent);
        await new PublishedArtifactCleanupCompletionObserver(db).StageAsync(new() {Id = 1, Status = WorkflowRunStatus.Success}, new object(), default);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await cleanup.ReclaimRequestedAsync(final.Id, default);
        Assert.AreEqual(1, (await cleanup.GetRequestedIdsAsync(default)).Count);
        Assert.AreEqual(1, layers.Count(a => File.Exists(a.Files.Single())));
        await cleanup.ReclaimRequestedAsync(final.Id, default);
        Assert.IsTrue(layers.All(a => !File.Exists(a.Files.Single())));
        Assert.AreEqual(1, (await cleanup.GetRequestedIdsAsync(default)).Count,
            "The final successful retirement receives one follow-up to reevaluate released ancestors.");
        await cleanup.ReclaimRequestedAsync(final.Id, default);
        Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count);
        Assert.IsTrue(File.Exists(raw.Files.Single()), "The frozen raw-input keep policy is not weakened.");
        Assert.IsTrue(File.Exists(final.Files.Single()));
    }

    [TestMethod]
    public async Task Verified_success_retires_only_the_superseded_wait_before_normal_publication_cleanup()
    {
        var fixture = await VerifiedContinuation();
        Assert.AreEqual("ActiveOwner", (await Preview(fixture.Final, 1, 3)).Single().Reason);
        Assert.AreEqual(WorkflowRunStatus.Waiting, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync(),
            "Cleanup preview remains read-only.");
        var oldHistory = fixture.Task.PlanHistoryJson;
        await new PublishedArtifactCleanupCompletionObserver(db).StageAsync(new() {Id = 1, Status = WorkflowRunStatus.Success}, new object(), default);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await cleanup.ReclaimRequestedAsync(fixture.Final.Id, default);
        Assert.AreEqual(WorkflowRunStatus.Cancelled, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
        Assert.AreEqual(WorkflowRunStatus.Success, await db.WorkflowRuns.Where(r => r.Id == 1).Select(r => r.Status).SingleAsync());
        Assert.IsFalse(File.Exists(fixture.Source.Files.Single()));
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(fixture.Final.RootDirectory, fixture.Final.Manifest)).Available);
        Assert.AreEqual(oldHistory, await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).Select(t => t.PlanHistoryJson).SingleAsync());
        Assert.IsTrue(await db.ArtifactConsumers.AnyAsync(c => c.OwnerId == "3"));
        Assert.AreEqual("[{\"label\":\"old output\"}]", await db.WorkflowNodeExecutions.Where(n => n.WorkflowRunId == 3).Select(n => n.OutputsJson).SingleAsync());
        var receipts = Directory.GetFiles(Path.Combine(root, "file-processing", "superseded-run-cleanup-receipts"), "*.json");
        Assert.AreEqual(1, receipts.Length);
        var receipt = JsonNode.Parse(await File.ReadAllTextAsync(receipts.Single()))!;
        Assert.AreEqual(AcquisitionSupersededRunCleanup.VerifiedDeliveryReason, receipt["reason"]!.GetValue<string>());
        Assert.AreEqual("Completed", receipt["status"]!.GetValue<string>());
        Assert.AreEqual(fixture.Final.Id, receipt["verifiedPublications"]![0]!["artifactId"]!.GetValue<string>());
        Assert.IsTrue((await fixture.Retirement.PrepareAsync(fixture.Final.Id, default))!.Ready);
        Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(receipts.Single())!, "*.json").Length);
    }

    [TestMethod]
    [DataRow("missing-final")]
    [DataRow("changed-same-size")]
    [DataRow("missing-part")]
    [DataRow("extra-slot")]
    [DataRow("signal")]
    [DataRow("running")]
    [DataRow("worker")]
    [DataRow("transport")]
    [DataRow("deleted-task-reference")]
    [DataRow("other-continuation")]
    [DataRow("task-gate")]
    [DataRow("run-gate")]
    [DataRow("changed-after-verification")]
    public async Task Unverified_delivery_or_any_current_owner_never_retires_an_old_wait(string change)
    {
        var fixture = await VerifiedContinuation();
        SemaphoreSlim? held = null;
        if (change == "missing-final") File.Delete(fixture.Final.Files.Single());
        if (change == "changed-same-size")
        {
            var timestamp = File.GetLastWriteTimeUtc(fixture.Final.Files.Single());
            await File.WriteAllTextAsync(fixture.Final.Files.Single(), "fake bytes");
            File.SetLastWriteTimeUtc(fixture.Final.Files.Single(), timestamp);
        }
        if (change == "missing-part")
        {
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(fixture.Task.PlanJson!, Json)!;
            plan = plan with {Parts = [..plan.Parts, new() {Id = "second"}]};
            var history = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(fixture.Task.PlanHistoryJson!, Json)!;
            history[^1] = history[^1] with {Plan = plan};
            await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json)).SetProperty(t => t.PlanHistoryJson, JsonSerializer.Serialize(history, Json)));
        }
        if (change == "extra-slot") Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync(
            "workflow", "1", "extra:artifact.publish", fixture.Final.OperationId));
        if (change == "signal") await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(u => u.SetProperty(r => r.PendingSignalJson, "{}"));
        if (change == "running") await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        if (change == "worker") fixture.Activity.BlockReason = "ACQUISITION_TASK_ACTIVE";
        if (change == "transport") fixture.Activity.BlockReason = "ACQUISITION_DOWNLOAD_ACTIVE";
        if (change == "deleted-task-reference")
        {
            db.AcquisitionTasks.Add(new() {Title = "Historical owner", PlanHistoryJson = fixture.Task.PlanHistoryJson, DeletedAt = DateTime.UtcNow});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        if (change == "other-continuation")
        {
            var plan = new AcquisitionPlan {Title = "Other pending plan", Parts = [new() {Id = "other", ContinueFrom =
                new(fixture.Task.Id, 3, fixture.Source.Id, fixture.Source.OperationId, fixture.Source.Fingerprint)}]};
            db.AcquisitionTasks.Add(new() {Title = plan.Title, PlanJson = JsonSerializer.Serialize(plan, Json)});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        if (change == "task-gate") held = AcquisitionTaskGate.For(fixture.Task.Id);
        if (change == "run-gate") held = WorkflowRunMutationGate.For(3);
        if (held != null) await held.WaitAsync();
        if (change == "changed-after-verification") fixture.Activity.OnRead = async count =>
        {
            if (count == 3) await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(u => u.SetProperty(r => r.PendingSignalJson, "{}"));
        };
        try
        {
            var result = await fixture.Retirement.PrepareAsync(fixture.Final.Id, default);
            Assert.IsNotNull(result); Assert.IsFalse(result.Ready, change);
            Assert.AreEqual(change == "running" ? WorkflowRunStatus.Running : WorkflowRunStatus.Waiting,
                await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
            Assert.AreEqual(WorkflowRunStatus.Success, await db.WorkflowRuns.Where(r => r.Id == 1).Select(r => r.Status).SingleAsync());
            Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        }
        finally { held?.Release(); }
    }

    [TestMethod]
    public async Task A_prepared_unstarted_continuation_keeps_its_exact_artifact_without_a_synthetic_keep_policy()
    {
        var fixture = await VerifiedContinuation();
        // A different terminal source revision still references this exact immutable artifact.
        // The pending plan has no run/consumer lease yet, so only durable plan references protect it.
        var plan = new AcquisitionPlan {Title = "Prepared consumer", Parts = [new() {Id = "pending", ContinueFrom =
            new(fixture.Task.Id, 2, fixture.Source.Id, fixture.Source.OperationId, fixture.Source.Fingerprint)}]};
        db.AcquisitionTasks.Add(new() {Title = plan.Title, Status = AcquisitionStatus.Waiting,
            PlanJson = JsonSerializer.Serialize(plan, Json), WorkflowRunId = null});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result = await fixture.Retirement.PrepareAsync(fixture.Final.Id, default);
        Assert.IsTrue(result!.Ready, result.Reason);
        var preview = (await Preview(fixture.Final, 1, 3)).Single();
        Assert.AreEqual("ReferencedPath", preview.Reason);
        Assert.IsFalse(preview.Eligible);
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
    }
}

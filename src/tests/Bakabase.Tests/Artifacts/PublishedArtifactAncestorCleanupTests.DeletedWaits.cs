using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    // Only the integration with application-wide runtime registries is replaced. The owned run
    // graph, mutation gates, plan/lease validation, durable receipts and filesystem use the production services.
    private sealed class HistoricalWaitActivity(BakabaseDbContext db) : IAcquisitionDeletionActivityReader
    {
        public async Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(IReadOnlyList<AcquisitionTaskDbModel> tasks,
            CancellationToken ct)
        {
            var runs = await db.WorkflowRuns.AsNoTracking().Where(r => r.Id == 1 || r.Id == 3).OrderBy(r => r.Id)
                .Select(r => new AcquisitionDeletionRun(r.Id, r.Status, r.CurrentStepIndex, r.WaitReason, r.WaitingSince, r.PendingSignalJson, r.CompletedAt))
                .ToArrayAsync(ct);
            var blocked = runs.Any(r => r.PendingSignalJson != null || r.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Pending);
            return tasks.ToDictionary(t => t.Id, _ => new AcquisitionDeletionActivity([1, 3], JsonSerializer.Serialize(runs),
                blocked ? "ACQUISITION_TASK_ACTIVE" : null) {ObservedRuns = runs});
        }
    }

    [DataTestMethod]
    [DataRow("success")]
    [DataRow("direct")]
    [DataRow("foreign")]
    [DataRow("outside-consumer")]
    [DataRow("late-answer")]
    [DataRow("running")]
    [DataRow("deletion-changed")]
    public async Task Deleted_exclusive_waits_require_review_of_exact_runs_before_confirming_file_cleanup(string scenario)
    {
        var f = await StageDeletion();
        var history = new[] {new AcquisitionPlanRevision(1, f.Plan, [], DateTime.UtcNow.AddDays(-1)),
            new AcquisitionPlanRevision(2, f.Plan, [], DateTime.UtcNow, "continueFiles", 3)};
        await db.Set<WorkflowRunSourceDbModel>().Where(s => s.WorkflowRunId == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Revision, "2"));
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 3, Module = "acquisition",
            EntityKind = "task", EntityId = f.Task.Id.ToString(), Revision = "1"});
        await db.SaveChangesAsync();
        var deleted = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc);
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.DeletedAt, deleted).SetProperty(t => t.Status, AcquisitionStatus.Waiting)
            .SetProperty(t => t.PlanRevision, 2).SetProperty(t => t.PlanHistoryJson, JsonSerializer.Serialize(history, Json)));
        await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status,
            scenario == "running" ? WorkflowRunStatus.Running : WorkflowRunStatus.Waiting));
        var activity = new HistoricalWaitActivity(db);
        var extraction = new NoDeletionExtraction();
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, resolver);
        var service = new AcquisitionTaskDeletionService(db, activity, new(),
            new(db, store, maintenance, cleanup, new Storage(), new(db, activity, root), resolver));
        if (scenario == "foreign")
        {
            db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 3, Module = "acquisition",
                EntityKind = "task", EntityId = "901", Revision = "1"});
            await db.SaveChangesAsync();
        }
        if (scenario == "outside-consumer")
            Assert.IsTrue(await store.AttachConsumerAsync(f.Publication.OperationId, "outside-reader", TimeSpan.FromHours(1)));
        var page = await service.GetDeletedTasksAsync(1, 10, "all", default);
        CollectionAssert.AreEquivalent(scenario == "running" ? new[] {3} : new[] {1, 3}, page.Items.Single().WaitingRunIds.ToArray());
        if (scenario == "direct")
        {
            var direct = await service.DeleteAsync(new(Guid.NewGuid().ToString("N"), CleanupFiles: true,
                TaskIds: [f.Task.Id], RetryRetainedFiles: true));
            Assert.IsTrue(direct.CleanupResults!.SelectMany(row => row.ReasonCodes).Concat(direct.Skipped.Select(s => s.ReasonCode))
                .Contains("CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW"));
        }
        else
        {
            var preview = await service.PreviewAsync(new([f.Task.Id], CleanupFiles: true, RetryRetainedFiles: true));
            var row = preview.Rows.Single();
            if (scenario is "foreign" or "running" or "outside-consumer") Assert.IsFalse(row.CanDelete);
            else
            {
                Assert.IsTrue(row.CanDelete, row.ReasonCode + " " + string.Join(",", row.Cleanup?.Groups.Select(g => g.ReasonCode) ?? []));
                var waits = row.Cleanup!.SupersededRuns!;
                CollectionAssert.AreEquivalent(new[] {1, 3}, waits.Select(r => r.RunId).ToArray());
                Assert.AreEqual(waits.Single(r => r.RunId == 1).Revision, waits.Single(r => r.RunId == 1).ReplacedByRevision);
                Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1 && r.Status == WorkflowRunStatus.Waiting));
                if (scenario == "late-answer") await db.WorkflowRuns.Where(r => r.Id == 1)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.PendingSignalJson, "{}"));
                if (scenario == "deletion-changed") await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, deleted.AddHours(1)));
                var applied = await service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
                if (scenario == "success")
                {
                    var outcome = applied.CleanupResults!.Single();
                    Assert.IsTrue(outcome.Completed, string.Join(",", outcome.ReasonCodes));
                    CollectionAssert.AreEquivalent(new[] {1, 3}, outcome.ClosedRunIds!.ToArray());
                    Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(root, "file-processing", "superseded-run-cleanup-receipts")).Any());
                }
            }
        }
        var finalId = await db.Artifacts.Where(a => a.Kind == "publishedFiles").Select(a => a.Id).SingleAsync();
        Assert.IsNotNull(await store.ResolveByIdAsync(finalId));
        Assert.AreEqual(scenario == "success" ? 2 : 0, await db.WorkflowRuns.CountAsync(r => (r.Id == 1 || r.Id == 3) &&
            r.Status == WorkflowRunStatus.Cancelled));
        Assert.AreEqual(scenario == "deletion-changed" ? deleted.AddHours(1) : deleted,
            await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).Select(t => t.DeletedAt).SingleAsync());
    }
}

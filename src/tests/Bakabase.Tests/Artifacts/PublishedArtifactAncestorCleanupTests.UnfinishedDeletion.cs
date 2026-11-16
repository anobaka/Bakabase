using System.Text.Json;
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
    private sealed class WaitingDeletionActivity(Bakabase.InsideWorld.Business.BakabaseDbContext database) : IAcquisitionDeletionActivityReader
    {
        public async Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(IReadOnlyList<AcquisitionTaskDbModel> tasks, CancellationToken ct)
        {
            var result = new Dictionary<int, AcquisitionDeletionActivity>();
            foreach (var task in tasks)
            {
                var owners = AcquisitionDeletionActivityReader.HistoryRuns(task.PlanHistoryJson).Append(task.WorkflowRunId!.Value).Distinct().ToArray();
                var runs = await database.WorkflowRuns.AsNoTracking().Where(r => owners.Contains(r.Id)).OrderBy(r => r.Id)
                    .Select(r => new AcquisitionDeletionRun(r.Id, r.Status, r.CurrentStepIndex, r.WaitReason, r.WaitingSince, r.PendingSignalJson, r.CompletedAt))
                    .ToArrayAsync(ct);
                var blocked = runs.Any(r => r.Status == WorkflowRunStatus.Running || r.PendingSignalJson != null) ? "ACQUISITION_TASK_ACTIVE" : null;
                result[task.Id] = new(runs.Select(r => r.Id).ToArray(), JsonSerializer.Serialize(runs), blocked) {ObservedRuns = runs};
            }
            return result;
        }
    }

    private async Task<StageDeletionFixture> UnfinishedStageDeletion()
    {
        var f = await StageDeletion();
        var history = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, f.Plan, [], DateTime.UtcNow)}, Json);
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, AcquisitionStatus.Waiting).SetProperty(t => t.PlanHistoryJson, history));
        await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, WorkflowRunStatus.Waiting));
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var engine = new NoDeletionExtraction();
        var activity = new WaitingDeletionActivity(db);
        var coordinator = new AcquisitionTaskDeletionCleanupService(db, store, new(db, store, new(engine), new(engine), root, resolver),
            cleanup, new Storage(), new(db, activity, root), resolver);
        return f with {Service = new(db, activity, new(), coordinator)};
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Abandon_requires_explicit_inventory_and_preserves_all_deliveries_and_history(bool savedHistory)
    {
        var f = await UnfinishedStageDeletion();
        if (!savedHistory) await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.PlanHistoryJson, (string?)null));
        var initial = await f.Service.PreviewAsync(new([f.Task.Id]));
        Assert.IsTrue(initial.Rows.Single().CanDelete);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanDelete, row.ReasonCode);
        var groups = row.Cleanup!.Groups;
        Assert.IsTrue(groups.Any(g => g.Directory == f.Processing && g.Eligible), string.Join(",", groups.Select(g => g.ReasonCode)));
        Assert.IsTrue(groups.Any(g => g.Directory == f.Download && g.Eligible));
        Assert.IsFalse(groups.Any(g => g.Kind == "publishedFiles"));
        Assert.AreEqual(WorkflowRunStatus.Waiting, await db.WorkflowRuns.Where(r => r.Id == 1).Select(r => r.Status).SingleAsync());
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, result.DeletedTaskIds.ToArray());
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        Assert.AreEqual(WorkflowRunStatus.Cancelled, await db.WorkflowRuns.Where(r => r.Id == 1).Select(r => r.Status).SingleAsync());
        Assert.AreEqual(savedHistory, await db.AcquisitionTasks.AnyAsync(t => t.Id == f.Task.Id && t.PlanHistoryJson != null));
        Assert.AreEqual(4, result.RemovedFiles);
        Assert.IsFalse(f.HistoricalSource.Files.Any(File.Exists));
        Assert.AreEqual(result, await f.Service.DeleteAsync(new(preview.PreviewId)));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Abandon_checks_ownership_of_both_waiting_revisions(bool foreignOwner)
    {
        var f = await UnfinishedStageDeletion();
        var histories = new[] {new AcquisitionPlanRevision(1, f.Plan, [], DateTime.UtcNow),
            new AcquisitionPlanRevision(2, f.Plan, [], DateTime.UtcNow, PreviousRunId: 3)};
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(t => t.PlanRevision, 2).SetProperty(t => t.PlanHistoryJson, JsonSerializer.Serialize(histories, Json)));
        await db.Set<WorkflowRunSourceDbModel>().Where(s => s.WorkflowRunId == 1 && s.Module == "acquisition")
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Revision, "2"));
        await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, WorkflowRunStatus.Waiting));
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 3, Module = "acquisition", EntityKind = "task", EntityId = f.Task.Id.ToString(), Revision = "1"});
        if (foreignOwner)
            db.AcquisitionTasks.Add(new() {Title = "Also references historical run", WorkflowRunId = 3, Status = AcquisitionStatus.Waiting});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true));
        Assert.AreEqual(!foreignOwner, preview.Rows.Single().CanDelete, preview.Rows.Single().ReasonCode);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(foreignOwner ? 0 : 1, result.DeletedTaskIds.Count);
        Assert.AreEqual(foreignOwner, Directory.Exists(f.Download));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        Assert.IsTrue(await db.WorkflowRuns.Where(r => r.Id == 1 || r.Id == 3)
            .AllAsync(r => r.Status == (foreignOwner ? WorkflowRunStatus.Waiting : WorkflowRunStatus.Cancelled)));
    }

    [TestMethod]
    public async Task Abandon_rechecks_the_wait_and_pending_answer_before_cancelling_or_deleting_files()
    {
        var f = await UnfinishedStageDeletion();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true));
        Assert.IsTrue(preview.Rows.Single().CanDelete, preview.Rows.Single().ReasonCode);
        await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(set => set.SetProperty(r => r.PendingSignalJson, "{}"));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.AreEqual(WorkflowRunStatus.Waiting, await db.WorkflowRuns.Where(r => r.Id == 1).Select(r => r.Status).SingleAsync());
        Assert.IsTrue(Directory.Exists(f.Processing));
        Assert.IsTrue(Directory.Exists(f.Download));
    }
}

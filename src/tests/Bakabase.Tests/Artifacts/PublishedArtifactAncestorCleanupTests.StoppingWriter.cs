using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private sealed class DrainingWriter(BakabaseDbContext db, string partialFile) :
        IAcquisitionDeletionActivityReader, IAcquisitionDeletionWriterControl
    {
        public async Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(
            IReadOnlyList<AcquisitionTaskDbModel> tasks, CancellationToken ct)
        {
            var run = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 1, ct);
            var active = run.Status == WorkflowRunStatus.Running;
            return tasks.ToDictionary(t => t.Id, _ => new AcquisitionDeletionActivity([run.Id],
                run.Status.ToString(), active ? "ACQUISITION_TASK_ACTIVE" : null)
                { Writers = active ? new([run.Id], [], []) : null });
        }

        public async Task StopWritersAsync(AcquisitionDeletionWriters writers, CancellationToken ct)
        {
            // The transport flushes its final partial after the user has approved deletion.
            await File.WriteAllTextAsync(partialFile, "last buffered bytes", ct);
            await db.WorkflowRuns.Where(r => writers.RunIds.Contains(r.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Cancelled), ct);
        }
    }

    [TestMethod]
    public async Task RunningDeleteInventoriesAfterFlushAndCleansAllThreeOwnedDirectories()
    {
        var f = await UnfinishedStageDeletion();
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AcquisitionStatus.Running));
        await db.WorkflowRuns.Where(r => r.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        var partialFile = Path.Combine(f.Download, "last.part");
        var activity = new DrainingWriter(db, partialFile);
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var engine = new NoDeletionExtraction();
        var files = new AcquisitionTaskDeletionCleanupService(db, store, new(db, store, new(engine), new(engine), root, resolver),
            cleanup, new Storage(), new(db, activity, root), resolver);
        var deletion = new AcquisitionTaskDeletionService(db, activity, new(), files);
        var preview = await deletion.PreviewAsync(new([f.Task.Id], CleanupFiles: true,
            DeleteDeliveredFiles: true, SkipFileVerification: true));
        Assert.IsTrue(preview.Rows.Single().WillStopExecution);
        Assert.IsFalse(File.Exists(partialFile));
        var result = await deletion.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, result.DeletedTaskIds.ToArray(), System.Text.Json.JsonSerializer.Serialize(result));
        foreach (var directory in new[] {f.Download, f.Processing, f.Delivery})
            Assert.IsFalse(Directory.Exists(directory), directory);
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
    }
}

using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow("stopped")]
    [DataRow("operation-lease")]
    [DataRow("running-workflow")]
    [DataRow("pending-workflow")]
    [DataRow("pending-signal")]
    public async Task Cancelled_task_can_replan_current_processing_only_after_actual_activity_stops(string activity)
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var processing = Directory.CreateDirectory(Path.Combine(_root, "cancelled-processing")).FullName;
        var currentFile = Path.Combine(processing, "current.zip");
        await File.WriteAllTextAsync(currentFile, "existing current bytes");
        task = await BindCurrentStage(task, "processing", processing);
        var row = await Db.AcquisitionTasks.SingleAsync(t => t.Id == task.Id);
        row.Status = AcquisitionStatus.Cancelled;
        row.Phase = "cancelled";
        run.Status = WorkflowRunStatus.Cancelled;
        run.CompletedAt = DateTime.UtcNow;
        if (activity == "operation-lease")
            (await Db.ArtifactOperations.SingleAsync(o => o.Id == original.OperationId)).LeaseUntil = DateTime.UtcNow.AddMinutes(5);
        else if (activity == "running-workflow") run.Status = WorkflowRunStatus.Running;
        else if (activity == "pending-workflow") run.Status = WorkflowRunStatus.Pending;
        else if (activity == "pending-signal") run.PendingSignalJson = "{}";
        await Db.SaveChangesAsync();
        var savedHistory = row.PlanHistoryJson;
        var runHistory = (run.Status, run.PlanSnapshotJson, run.CurrentItemJson, run.CompletedAt);
        var request = StageRequest(task, run.Id, "processing");
        if (activity == "stopped")
        {
            var revised = await Plans.ReplanAsync(task.Id, request, default);
            Assert.AreEqual(task.Task.PlanRevision + 1, revised.Task.PlanRevision);
            var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
            Assert.AreEqual(processing, captured.RootDirectory);
            CollectionAssert.AreEqual(new[] {currentFile}, captured.Files.ToArray());
            Assert.IsTrue(revised.PlanHistory.Any(h => h.PreviousRunId == run.Id));
        }
        else
        {
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ReplanAsync(task.Id, request, default));
            // Earlier replan validation can also reject an actually running workflow.
            Assert.AreEqual(activity is "running-workflow" or "pending-workflow"
                ? "PLAN_REPLAN_NOT_STOPPED" : "STAGE_DIRECTORY_TASK_ACTIVE", error.Message);
            Assert.AreEqual(savedHistory, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).PlanHistoryJson);
        }
        var savedRun = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(runHistory, (savedRun.Status, savedRun.PlanSnapshotJson, savedRun.CurrentItemJson, savedRun.CompletedAt));
        Assert.IsTrue(File.Exists(currentFile)); Assert.IsTrue(File.Exists(original.Files.Single()));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}

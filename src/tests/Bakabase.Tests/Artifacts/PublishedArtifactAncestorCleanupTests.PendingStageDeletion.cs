using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow("processingStageInputId")]
    [DataRow("processingStageCommitId")]
    [DataRow("directoryChange")]
    public async Task Explicit_task_deletion_can_abandon_an_inactive_stage_journal_without_rewriting_history(string field)
    {
        var f = await StageDeletion();
        var history = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [field] = field == "directoryChange" ? new {Id = "unfinished-directory-change"} : new string('a', 64)
        });
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, history));
        var originalManifest = await db.Artifacts.Where(a => a.Id == f.Publication.Id).Select(a => a.ManifestJson).SingleAsync();

        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));

        CollectionAssert.AreEqual(new[] {f.Task.Id}, result.DeletedTaskIds.ToArray());
        Assert.IsTrue(result.CleanupResults!.Single().Completed, JsonSerializer.Serialize(result.CleanupResults));
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsFalse(Directory.Exists(f.Delivery));
        var task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        Assert.IsNotNull(task.DeletedAt);
        Assert.AreEqual(history, task.BuilderStateJson);
        Assert.AreEqual(originalManifest, await db.Artifacts.Where(a => a.Id == f.Publication.Id).Select(a => a.ManifestJson).SingleAsync());
    }

    [DataTestMethod]
    [DataRow("workflow")]
    [DataRow("operation")]
    [DataRow("consumer")]
    public async Task Pending_stage_journal_cleanup_still_preserves_files_used_by_a_live_writer(string active)
    {
        var f = await StageDeletion();
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(t => t.BuilderStateJson, "{\"processingStageInputId\":\"unfinished\",\"processingStageCommitId\":\"unfinished\"}"));
        if (active == "workflow")
            await db.WorkflowRuns.Where(r => r.Id == f.Task.WorkflowRunId).ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        else if (active == "operation")
            await db.ArtifactOperations.Where(o => o.Id == f.Publication.OperationId).ExecuteUpdateAsync(s =>
                s.SetProperty(o => o.State, ArtifactOperationState.Running)
                    .SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(10)));
        else
            Assert.IsTrue(await store.AttachConsumerAsync(f.Publication.OperationId, "outside-reader", TimeSpan.FromMinutes(10)));

        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));

        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
        Assert.AreEqual(active != "consumer", Directory.Exists(f.Download));
        Assert.AreEqual(active != "consumer", Directory.Exists(f.Processing));
        Assert.IsTrue(Directory.Exists(f.Delivery));
        Assert.IsNull(await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).Select(t => t.DeletedAt).SingleAsync());
    }
}

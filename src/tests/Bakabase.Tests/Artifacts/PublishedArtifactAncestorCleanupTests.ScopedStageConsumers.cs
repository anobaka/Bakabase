using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task PinOutsideWorkflowAsync(ArtifactRecord artifact, bool expired = true)
    {
        Assert.IsTrue(await store.AttachConsumerAsync(artifact.OperationId, "outside-stage-reader", TimeSpan.FromHours(1)));
        Assert.IsTrue(await store.PinConsumerAsync(artifact.OperationId, "outside-stage-reader", "workflow", "3"));
        if (expired)
            await db.ArtifactConsumers.Where(c => c.OperationId == artifact.OperationId && c.OwnerId == "3")
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntil, DateTime.UtcNow.AddDays(-1)));
    }

    [TestMethod]
    public async Task Whole_task_deletion_keeps_unrelated_borrowed_source_pins_without_blocking_its_three_stage_directories()
    {
        var f = await StageDeletion();
        var directory = Directory.CreateDirectory(Path.Combine(root, "external-source")).FullName;
        var sourceFile = Path.Combine(directory, "original.zip");
        await File.WriteAllTextAsync(sourceFile, "still owned by another workflow");
        var source = await store.AdoptAsync(new("borrowed-source", "borrowed-source", "download", "source"),
            new(directory, [sourceFile], "download", false), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:artifact.continuation", source.OperationId));
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "3", "download:artifact", source.OperationId));
        await PinOutsideWorkflowAsync(source);
        Assert.IsTrue(await store.HasActiveConsumersAsync(source.OperationId));
        var original = await db.ArtifactConsumers.AsNoTracking().SingleAsync(c => c.OperationId == source.OperationId);

        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));

        CollectionAssert.AreEqual(new[] {f.Task.Id}, result.DeletedTaskIds.ToArray());
        Assert.IsTrue(result.CleanupResults!.Single().Completed, JsonSerializer.Serialize(result.CleanupResults));
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsFalse(Directory.Exists(f.Delivery));
        Assert.IsTrue(File.Exists(sourceFile));
        Assert.AreEqual(WorkflowRunStatus.Waiting, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(await db.ArtifactConsumers.AsNoTracking().SingleAsync(c => c.Id == original.Id)));
        Assert.IsTrue(await store.HasActiveConsumersAsync(source.OperationId));
    }

    [DataTestMethod]
    [DataRow(WorkflowRunStatus.Pending)]
    [DataRow(WorkflowRunStatus.Running)]
    [DataRow(WorkflowRunStatus.Waiting)]
    public async Task Whole_task_deletion_preserves_only_the_stage_with_an_expired_durable_external_pin(WorkflowRunStatus status)
    {
        var f = await StageDeletion();
        await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status));
        var protectedArtifact = await StageReferenceArtifact(f, "old-part");
        await PinOutsideWorkflowAsync(protectedArtifact);
        // The foreign workflow payload need not contain a path: its durable pin alone
        // protects the live stage even after the renewable lease has expired.
        var guard = new AcquisitionStageDirectoryReferenceGuard(db, store, new Storage(), new(root, new Storage()));
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => guard.RequireAsync(
            f.Task, [1], "nested.part", f.Processing, default, explicitTaskCleanup: true));
        Assert.AreEqual("STAGE_DIRECTORY_TASK_ACTIVE", error.Message);

        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));

        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsTrue(File.Exists(Path.Combine(f.Processing, "manual.key")));
        Assert.IsFalse(Directory.Exists(f.Delivery));
        Assert.IsTrue(await store.HasActiveConsumersAsync(protectedArtifact.OperationId));
        Assert.AreEqual(status, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
    }
}

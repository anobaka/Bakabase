using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Task_deletion_automatically_includes_owned_stages_and_manual_additions(bool completed, bool includeDelivery)
    {
        var f = completed ? await StageDeletion() : await UnfinishedStageDeletion();
        var manualDelivery = Path.Combine(f.Delivery, "extra image.jpg");
        await File.WriteAllTextAsync(manualDelivery, "image");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: true, DeleteDeliveredFiles: includeDelivery));
        var row = preview.Rows.Single();
        Assert.IsTrue(row.CanDelete, row.ReasonCode);
        Assert.IsTrue(row.Cleanup!.Groups.Any(g => g.Directory == f.Download && g.Eligible));
        Assert.IsTrue(row.Cleanup.Groups.Any(g => g.Directory == f.Processing && g.Eligible));
        Assert.AreEqual(includeDelivery, row.Cleanup.Groups.Any(g => g.Directory == f.Delivery && g.Eligible));
        var deleted = await f.Service.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, deleted.DeletedTaskIds.ToArray(), JsonSerializer.Serialize(deleted.CleanupResults));
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.AreEqual(!includeDelivery, File.Exists(manualDelivery));
        Assert.AreEqual(!includeDelivery, f.Publication.Files.All(File.Exists));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
    }

    [TestMethod]
    public async Task Deleting_a_task_with_only_a_registered_download_retires_its_files_and_preserves_its_unowned_siblings()
    {
        var f = await UnfinishedStageDeletion();
        var raw = await Artifact("download", filename: "registered.bin", contents: "original");
        var other = Path.Combine(raw.RootDirectory, "another-user.key");
        await File.WriteAllTextAsync(other, "keep");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: true, SkipFileVerification: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Any(g => g.Files.Any(file => file.Path == raw.Files.Single()) && g.Eligible));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(result.DeletedTaskIds.Contains(f.Task.Id), JsonSerializer.Serialize(result.CleanupResults));
        Assert.IsFalse(File.Exists(raw.Files.Single()));
        Assert.IsTrue(File.Exists(other));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Idle_task_without_current_run_can_remove_its_private_directories(bool hasStoppedHistory)
    {
        var f = await StageDeletion();
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(t => t.Status, AcquisitionStatus.Pending).SetProperty(t => t.WorkflowRunId, (int?)null));
        if (!hasStoppedHistory)
            await db.Set<Bakabase.Modules.Workflow.Abstractions.Models.Db.WorkflowRunSourceDbModel>()
                .Where(s => s.Module == "acquisition" && s.EntityId == f.Task.Id.ToString()).ExecuteDeleteAsync();
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity(hasStoppedHistory ? [1] : []), new(), f.Cleanup);
        var preview = await service.PreviewAsync(new([f.Task.Id], CleanupFiles: true, SkipFileVerification: true));
        Assert.IsTrue(preview.Rows.Single().CanDelete, preview.Rows.Single().ReasonCode);
        var result = await service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(result.DeletedTaskIds.Contains(f.Task.Id), JsonSerializer.Serialize(result.CleanupResults));
        Assert.IsFalse(Directory.Exists(f.Download)); Assert.IsFalse(Directory.Exists(f.Processing)); Assert.IsTrue(Directory.Exists(f.Delivery));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
    }

    [TestMethod]
    public async Task Same_length_modification_of_a_borrowed_registered_input_is_protected_when_content_verification_is_enabled()
    {
        var f = await DeletionFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: true));
        var path = f.Source.Files.Single();
        var originalTime = File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path, "fake bytes");
        File.SetLastWriteTimeUtc(path, originalTime);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.IsTrue(File.Exists(path)); Assert.IsTrue(f.Final.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Ordinary_record_only_confirmation_cannot_add_task_files_after_preview()
    {
        var f = await UnfinishedStageDeletion();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id]));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => f.Service.DeleteAsync(new(preview.PreviewId, CleanupFiles: true)));
        Assert.IsTrue(Directory.Exists(f.Download)); Assert.IsTrue(Directory.Exists(f.Processing));
    }

}

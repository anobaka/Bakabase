using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [TestMethod]
    public async Task Confirmed_absent_registered_files_delete_only_the_task_and_record_zero_actual_removal()
    {
        var fixture = await DeletionFixture();
        File.Delete(fixture.Source.Files.Single()); File.Delete(fixture.Final.Files.Single());
        var unknown = Path.Combine(fixture.Source.RootDirectory, "unregistered.key"); await File.WriteAllTextAsync(unknown, "retain");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "shared-history", fixture.Source.OperationId));
        var before = await store.GetArtifactAsync(fixture.Source.Id);
        var bindings = await db.ArtifactOperationBindings.CountAsync();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.IsTrue(group.Eligible); Assert.AreEqual("AlreadyAbsent", group.ReasonCode);
        Assert.AreEqual(0L, preview.CleanupBytes); Assert.AreEqual(0, preview.CleanupFileCount);
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.DeletedTaskIds.Count); Assert.AreEqual(0, result.RemovedFiles); Assert.AreEqual(0L, result.RemovedBytes);
        var after = await store.GetArtifactAsync(fixture.Source.Id);
        Assert.AreEqual(before!.State, after!.State); Assert.AreEqual(before.Fingerprint, after.Fingerprint);
        Assert.AreEqual(before.RootDirectory, after.RootDirectory); Assert.AreEqual(before.OperationId, after.OperationId);
        Assert.AreEqual(bindings, await db.ArtifactOperationBindings.CountAsync());
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 2));
        Assert.AreEqual("retain", await File.ReadAllTextAsync(unknown));
        var path = Directory.GetFiles(Path.Combine(root, "file-processing", "workspace-cleanup-receipts"), "*.json").Single();
        var receipt = JsonSerializer.Deserialize<ArtifactWorkspaceCleanupJournal.Receipt>(await File.ReadAllTextAsync(path), Json)!;
        Assert.AreEqual("AlreadyAbsent", receipt.Status); Assert.AreEqual(0, receipt.RemovedFiles); Assert.AreEqual(0L, receipt.RemovedBytes);
        Assert.AreEqual(1, receipt.AlreadyAbsentPaths!.Count); Assert.IsTrue(receipt.Consent!.DeliveryAccepted);
        Assert.AreEqual(result, await fixture.Service.DeleteAsync(new(preview.PreviewId)));
    }

    [TestMethod]
    public async Task Reappeared_file_after_absence_preview_never_falls_back_to_deleting_it()
    {
        var fixture = await DeletionFixture(); File.Delete(fixture.Source.Files.Single());
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        await File.WriteAllTextAsync(fixture.Source.Files.Single(), "new user bytes");
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.AreEqual(0, result.RemovedFiles);
        Assert.AreEqual("new user bytes", await File.ReadAllTextAsync(fixture.Source.Files.Single()));
        Assert.IsNull(await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).Select(t => t.DeletedAt).SingleAsync());
        Assert.AreEqual(ArtifactLocationState.Available, (await store.GetArtifactAsync(fixture.Source.Id))!.State);
    }

    [TestMethod]
    public async Task Unavailable_root_after_absence_preview_keeps_task_and_history()
    {
        var fixture = await DeletionFixture(); File.Delete(fixture.Source.Files.Single());
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        Directory.Delete(fixture.Source.RootDirectory);
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.AreEqual(0L, result.RemovedBytes);
        Assert.IsNotNull(await store.GetArtifactAsync(fixture.Source.Id));
        Assert.IsTrue(File.Exists(fixture.Final.Files.Single()));
    }

    [TestMethod]
    public async Task Missing_final_before_preview_does_not_block_explicit_task_file_cleanup()
    {
        var fixture = await DeletionFixture(); File.Delete(fixture.Source.Files.Single()); File.Delete(fixture.Final.Files.Single());
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, false));
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        Assert.AreEqual("AlreadyAbsent", preview.Rows.Single().Cleanup!.Groups.Single().ReasonCode);
    }

    [TestMethod]
    public async Task Missing_unselected_final_after_preview_does_not_block_the_absence_noop()
    {
        var fixture = await DeletionFixture(); File.Delete(fixture.Source.Files.Single());
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, false));
        Assert.AreEqual("AlreadyAbsent", preview.Rows.Single().Cleanup!.Groups.Single().ReasonCode);
        File.Delete(fixture.Final.Files.Single());
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.DeletedTaskIds.Count); Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(result.CleanupResults!.Single().Completed);
    }
}

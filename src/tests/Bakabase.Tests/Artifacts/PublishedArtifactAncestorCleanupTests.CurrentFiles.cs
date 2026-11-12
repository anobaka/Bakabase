using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private sealed record CurrentFilesDeletionFixture(AcquisitionTaskDbModel Task, ArtifactRecord Raw,
        ArtifactRecord Processed, ArtifactRecord Final, ArtifactRecord[] Views, string KeyFile,
        AcquisitionTaskDeletionService Service);

    private async Task<CurrentFilesDeletionFixture> CreateCurrentFilesDeletionFixture()
    {
        var raw = await Artifact("download");
        var processed = await Artifact("processedFiles", raw, cleanupPolicy: false);
        var final = await Artifact("publishedFiles", processed);
        var task = new AcquisitionTaskDbModel
        {
            Title = "Current-directory cleanup fixture", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 2,
        };
        db.AcquisitionTasks.Add(task);
        await db.SaveChangesAsync();
        db.Set<WorkflowRunSourceDbModel>().Add(new()
        {
            WorkflowRunId = 1, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Replace the generic helper's bindings with real part-scoped producer receipts.
        // The raw file was already admitted by a continuation: no new download is needed.
        var operationIds = new[] {raw.OperationId, processed.OperationId, final.OperationId};
        await db.ArtifactOperationBindings.Where(b => operationIds.Contains(b.OperationId)).ExecuteDeleteAsync();
        var bindings = new ArtifactOperationBindingService(db);
        Assert.IsTrue(await bindings.BindAsync("workflow", "1", "part:artifact.continuation", raw.OperationId));
        Assert.IsTrue(await bindings.BindAsync("workflow", "1", "part:process:fixture", processed.OperationId));
        Assert.IsTrue(await bindings.BindAsync("workflow", "1", "part:artifact.publish", final.OperationId));
        var keyFile = Path.Combine(processed.RootDirectory, "manual.key");
        await File.WriteAllTextAsync(keyFile, "user supplied key must survive");
        var views = new List<ArtifactRecord>();
        foreach (var source in new[] {raw, processed})
        {
            var view = await AcquisitionCurrentDirectoryFiles.CaptureAsync(db, store, new Storage(), null,
                task.Id, 2, 1, "part", source, null, default);
            Assert.IsFalse(view.Managed);
            Assert.IsTrue(await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(db, store, view, task.Id, 1, "part", default));
            views.Add(view);
        }
        Assert.IsTrue(views[1].Files.Contains(keyFile));
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root);
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity(), new(),
            new(db, store, maintenance, cleanup, new Storage()));
        return new(task, raw, processed, final, views.ToArray(), keyFile, service);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Explicit_deletion_ignores_owned_current_directory_views_without_expanding_the_cleanup_selection(bool retryRetainedFiles)
    {
        var f = await CreateCurrentFilesDeletionFixture();
        if (retryRetainedFiles)
            await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u =>
                u.SetProperty(t => t.DeletedAt, (DateTime?)DateTime.UtcNow));
        var originalDeletion = await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .Select(t => t.DeletedAt).SingleAsync();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, RetryRetainedFiles: retryRetainedFiles));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsTrue(groups.Any(g => g.ArtifactId == f.Raw.Id && g.Eligible),
            string.Join(",", groups.Select(g => g.ArtifactId + ":" + g.ReasonCode)));
        Assert.IsTrue(groups.Any(g => g.ArtifactId == f.Processed.Id && g.Eligible));
        Assert.IsFalse(groups.SelectMany(g => g.Files).Any(file => file.Path == f.KeyFile || f.Final.Files.Contains(file.Path)));
        Assert.AreEqual(2, preview.CleanupFileCount);

        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: retryRetainedFiles));
        CollectionAssert.AreEqual(retryRetainedFiles ? Array.Empty<int>() : [f.Task.Id], result.DeletedTaskIds.ToArray());
        Assert.AreEqual(2, result.RemovedFiles);
        Assert.IsFalse(File.Exists(f.Raw.Files.Single()));
        Assert.IsFalse(File.Exists(f.Processed.Files.Single()));
        Assert.AreEqual("same bytes", await File.ReadAllTextAsync(f.Final.Files.Single()));
        Assert.AreEqual("user supplied key must survive", await File.ReadAllTextAsync(f.KeyFile));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
        var retainedDeletion = await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .Select(t => t.DeletedAt).SingleAsync();
        Assert.IsNotNull(retainedDeletion);
        if (retryRetainedFiles) Assert.AreEqual(originalDeletion, retainedDeletion);
        foreach (var view in f.Views)
        {
            var retained = await store.GetArtifactAsync(view.Id);
            Assert.IsNotNull(retained);
            Assert.AreEqual(view.MetadataJson, retained.MetadataJson);
            Assert.AreEqual(view.Fingerprint, retained.Fingerprint);
        }
    }

    [TestMethod]
    [DataRow("foreign-binding")]
    [DataRow("foreign-descendant")]
    [DataRow("active-consumer")]
    [DataRow("forged-receipt")]
    public async Task Current_directory_views_with_untrusted_ownership_or_active_users_still_protect_their_files(string scenario)
    {
        var f = await CreateCurrentFilesDeletionFixture();
        foreach (var view in f.Views)
        {
            if (scenario == "foreign-binding")
                Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2",
                    "part:borrowed:" + view.Id, view.OperationId));
            if (scenario == "foreign-descendant")
                await Artifact("processedFiles", view, owner: 2);
            if (scenario == "active-consumer")
                Assert.IsTrue(await store.AttachConsumerAsync(view.OperationId, "another-reader", TimeSpan.FromHours(1)));
            if (scenario == "forged-receipt")
            {
                var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(view.MetadataJson!, Json)!;
                var forged = JsonSerializer.Serialize(receipt with {TaskId = f.Task.Id + 999}, Json);
                await db.Artifacts.Where(a => a.Id == view.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.MetadataJson, forged));
            }
        }
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsFalse(groups.Any(g => g.Eligible && (g.ArtifactId == f.Raw.Id || g.ArtifactId == f.Processed.Id)), scenario);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles, scenario);
        Assert.AreEqual(0, result.DeletedTaskIds.Count, scenario);
        Assert.IsTrue(File.Exists(f.Raw.Files.Single()));
        Assert.IsTrue(File.Exists(f.Processed.Files.Single()));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        Assert.IsTrue(File.Exists(f.KeyFile));
    }

    [TestMethod]
    public async Task New_foreign_binding_after_current_directory_cleanup_preview_is_rechecked_before_unlink()
    {
        var f = await CreateCurrentFilesDeletionFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true));
        Assert.AreEqual(2, preview.CleanupFileCount);
        foreach (var view in f.Views)
            Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2",
                "part:late-borrower:" + view.Id, view.OperationId));

        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
        Assert.IsTrue(File.Exists(f.Raw.Files.Single()));
        Assert.IsTrue(File.Exists(f.Processed.Files.Single()));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        Assert.IsTrue(File.Exists(f.KeyFile));
    }
}

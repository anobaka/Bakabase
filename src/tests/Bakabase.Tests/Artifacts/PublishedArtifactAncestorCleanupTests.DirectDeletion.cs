using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private static AcquisitionTaskDeleteInput DirectDelete(int id, bool finalFiles = true) =>
        new(Guid.NewGuid().ToString("N"), CleanupFiles: true, DeleteDeliveredFiles: finalFiles, TaskIds: [id]);

    private sealed class DirectBatchActivity : IAcquisitionDeletionActivityReader
    {
        public Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(IReadOnlyList<AcquisitionTaskDbModel> tasks,
            CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<int, AcquisitionDeletionActivity>>(
            tasks.ToDictionary(t => t.Id, t => new AcquisitionDeletionActivity([t.WorkflowRunId!.Value], "finished", null)));
    }

    [TestMethod]
    public async Task Direct_deletion_reports_only_directories_it_removed()
    {
        var f = await StageDeletion();
        Directory.Delete(f.Processing, recursive: true);

        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));

        Assert.IsTrue(result.CleanupResults!.Single().Completed,
            string.Join(",", result.CleanupResults.Single().ReasonCodes));
        CollectionAssert.AreEquivalent(new[] {f.Download, f.Delivery}, result.RemovedDirectories!.ToArray());
        Assert.IsFalse(Directory.Exists(f.Processing));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Direct_deletion_validates_foreign_continuation_paths_at_all_three_stages(bool concreteReference)
    {
        var f = await StageDeletion();
        var other = new AcquisitionTaskDbModel { Title = "Another task", Status = AcquisitionStatus.Completed,
            PlanRevision = 1, WorkflowRunId = 2 };
        db.AcquisitionTasks.Add(other);
        await db.SaveChangesAsync();
        db.Set<WorkflowRunSourceDbModel>().Add(new() { WorkflowRunId = 2, Module = "acquisition",
            EntityKind = "task", EntityId = other.Id.ToString(), Revision = "1" });
        var layout = f.Plan.DeliveryLayout!;
        var sibling = Path.Combine(layout.RootDirectory, "different-task");
        other.PlanJson = JsonSerializer.Serialize(f.Plan with { Title = other.Title!, StageDirectories = null,
            DeliveryLayout = layout with { TaskId = other.Id, TaskDirectory = sibling,
                PartDirectories = new() { ["nested.part"] = sibling } } }, Json);
        var now = DateTimeOffset.UtcNow;
        var paths = new List<string> { f.Plan.OutputDirectory, f.Plan.StoragePolicy!.ProcessingWorkspaceDirectory!, layout.RootDirectory };
        var protectedFile = Path.Combine(f.Processing, "manual.key");
        if (concreteReference) paths.Add(protectedFile);
        other.BuilderStateJson = AcquisitionContinuationEntry.Write(null, new()
        {
            BatchId = Guid.NewGuid().ToString("N"), PreviewToken = Guid.NewGuid().ToString("N"),
            Scope = "workflow", Version = new string('a', 64), Settings = "{}", WorkflowRunId = 2,
            PlanRevision = 1, Paths = paths.Order(StringComparer.Ordinal).ToArray(), CreatedAt = now, UpdatedAt = now,
            Status = "attention"
        });
        await db.SaveChangesAsync();

        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));

        Assert.AreEqual(!concreteReference, result.CleanupResults!.Single().Completed,
            string.Join(",", result.CleanupResults.Single().ReasonCodes));
        Assert.AreEqual(concreteReference, await db.AcquisitionTasks.AnyAsync(t => t.Id == f.Task.Id && t.DeletedAt == null));
        Assert.AreEqual(concreteReference, File.Exists(protectedFile));
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Delivery));
    }

    [TestMethod]
    public async Task Direct_deletion_removes_forty_four_tasks_three_roots_each_without_inventory_round_trips()
    {
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var tasks = new List<AcquisitionTaskDbModel>();
        var roots = new List<string>();
        var basePlan = new AcquisitionPlan {Title = "Batch", OutputDirectory = root,
            StoragePolicy = new() {ProcessingWorkspaceDirectory = root},
            Parts = [new() {Id = "main", SelectedSourceId = "source", Sources = [new()
                {Id = "source", Url = "https://example.test/file.zip", ProviderId = "http"}]}]};
        for (var index = 0; index < 44; index++)
        {
            var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success};
            db.WorkflowRuns.Add(run);
            await db.SaveChangesAsync();
            var task = new AcquisitionTaskDbModel {Title = "Batch " + index, Status = AcquisitionStatus.Completed,
                WorkflowRunId = run.Id, PlanRevision = 1};
            db.AcquisitionTasks.Add(task);
            await db.SaveChangesAsync();
            db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = run.Id, Module = "acquisition",
                EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
            var plan = basePlan with {StageDirectories = AcquisitionStageDirectoryResolver.Allocate(task.Id, basePlan, new(new Storage()))};
            var main = plan.StageDirectories.Parts["main"];
            var delivery = main.Delivery! with {Ownership = "exclusive", Evidence = "taskAllocation",
                Allocation = new ArtifactDirectoryScope("acquisition", task.Id.ToString(), "delivery-main", root, main.Delivery!.Directory)};
            plan = plan with {StageDirectories = plan.StageDirectories with
                {Parts = new(plan.StageDirectories.Parts) {["main"] = main with {Delivery = delivery}}}};
            foreach (var part in plan.StageDirectories.Parts.Values)
            foreach (var directory in new[] {part.Download!, part.Processing!, part.Delivery!})
            {
                Directory.CreateDirectory(directory.Allocation!.StorageRoot);
                await resolver.EnsureScopedDirectoryAsync(directory.Allocation, default);
                await File.WriteAllTextAsync(Path.Combine(directory.Directory, "user-current-file.txt"), "contents");
                roots.Add(directory.Directory);
            }
            task.PlanJson = JsonSerializer.Serialize(plan, Json);
            tasks.Add(task);
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, resolver);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage(), workspaces: resolver);
        var service = new AcquisitionTaskDeletionService(db, new DirectBatchActivity(), new(), files);
        var result = await service.DeleteAsync(new(Guid.NewGuid().ToString("N"), CleanupFiles: true,
            DeleteDeliveredFiles: true, TaskIds: tasks.Select(t => t.Id).ToArray()));
        Assert.AreEqual(44, result.DeletedTaskIds.Count, JsonSerializer.Serialize(result.Skipped));
        Assert.IsTrue(result.CleanupResults!.All(r => r.Completed), JsonSerializer.Serialize(result.CleanupResults));
        Assert.AreEqual(132, result.RemovedDirectories!.Count);
        Assert.IsTrue(roots.All(path => !Directory.Exists(path)));
        Assert.IsTrue(Directory.Exists(root));
    }

    [TestMethod]
    public async Task Direct_deletion_uses_current_directory_contents_without_a_preview_or_file_identity_check()
    {
        var f = await StageDeletion();
        File.Move(f.Publication.Files.Single(), Path.Combine(f.Delivery, "renamed-video.bin"));
        await File.WriteAllTextAsync(Path.Combine(f.Delivery, "manual-new-file.key"), "user added");
        var manifest = await db.Artifacts.Where(a => a.Id == f.Publication.Id).Select(a => a.ManifestJson).SingleAsync();
        var runs = await db.WorkflowRuns.CountAsync();
        var direct = await f.Cleanup.PreviewAsync(f.Task, [1], true, true, default,
            deleteDeliveredFiles: true, directDirectoryDeletion: true);
        Assert.IsTrue(direct.StageDirectories!.Where(c => c.ManifestArtifactId == null)
            .All(c => c.DirectDeletion && c.Files.Count == 0 && c.FileIdentities.Count == 0 && c.Directories.Count == 0));
        // The task selects this root. Files added after metadata discovery still belong
        // to the user's explicit directory deletion, without a new preview round-trip.
        await File.WriteAllTextAsync(Path.Combine(f.Processing, "added-after-discovery.txt"), "current content");
        var request = DirectDelete(f.Task.Id);
        var result = await f.Service.DeleteAsync(request);
        CollectionAssert.AreEqual(new[] {f.Task.Id}, result.DeletedTaskIds.ToArray());
        Assert.IsTrue(result.CleanupResults!.Single().Completed,
            string.Join(",", result.CleanupResults.Single().ReasonCodes));
        Assert.IsTrue(result.FileStatisticsIncomplete);
        CollectionAssert.AreEquivalent(new[] {f.Download, f.Processing, f.Delivery}, result.RemovedDirectories!.ToArray());
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsFalse(Directory.Exists(f.Delivery));
        Assert.AreEqual(runs, await db.WorkflowRuns.CountAsync());
        Assert.AreEqual(manifest, await db.Artifacts.Where(a => a.Id == f.Publication.Id).Select(a => a.ManifestJson).SingleAsync());
        Assert.AreEqual(result, await f.Service.DeleteAsync(request), "A lost HTTP response reuses the same operation result.");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.DeleteAsync(request with {DeleteDeliveredFiles = false}));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.DeleteAsync(request with {TaskIds = [999]}));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.DeleteAsync(request with {TaskIds = null}));
    }

    [TestMethod]
    public async Task Direct_deletion_preserves_unselected_delivery_and_does_not_follow_links_in_selected_roots()
    {
        var f = await StageDeletion();
        var outside = Directory.CreateDirectory(Path.Combine(root, "unrelated-files")).FullName;
        var kept = Path.Combine(outside, "keep.bin");
        await File.WriteAllTextAsync(kept, "unrelated data");
        Directory.CreateSymbolicLink(Path.Combine(f.Processing, "directory-link"), outside);
        File.CreateSymbolicLink(Path.Combine(f.Processing, "file-link"), kept);
        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id, finalFiles: false));
        Assert.IsTrue(result.CleanupResults!.Single().Completed, string.Join(",", result.CleanupResults.Single().ReasonCodes));
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        Assert.AreEqual("unrelated data", await File.ReadAllTextAsync(kept));
    }

    [TestMethod]
    public async Task Direct_deletion_treats_absent_roots_as_completed_and_retained_retry_is_idempotent()
    {
        var f = await StageDeletion();
        Directory.Delete(f.Processing, true);
        Directory.Delete(f.Delivery, true);
        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));
        Assert.IsTrue(result.CleanupResults!.Single().Completed, string.Join(",", result.CleanupResults.Single().ReasonCodes));
        var retry = DirectDelete(f.Task.Id) with {RetryRetainedFiles = true};
        var repeated = await f.Service.DeleteAsync(retry);
        Assert.IsTrue(repeated.CleanupResults!.Single().Completed, string.Join(",", repeated.CleanupResults.Single().ReasonCodes));
        Assert.AreEqual(0, repeated.DeletedTaskIds.Count);
        Assert.IsFalse(Directory.Exists(f.Download));
    }

    [TestMethod]
    public async Task Direct_deletion_accepts_missing_historical_manifest_roots_without_a_stage_map()
    {
        var f = await DeletionFixture();
        Directory.Delete(f.Source.RootDirectory, true);
        Directory.Delete(f.Final.RootDirectory, true);
        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));
        Assert.AreEqual(f.Task.Id, result.DeletedTaskIds.Single());
        Assert.IsTrue(result.CleanupResults!.Single().Completed, string.Join(",", result.CleanupResults.Single().ReasonCodes));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.AreEqual(0L, result.RemovedBytes);
    }

    [TestMethod]
    public async Task Direct_deletion_retains_a_directory_claimed_by_another_task()
    {
        var f = await StageDeletion();
        db.AcquisitionTasks.Add(new AcquisitionTaskDbModel {Title = "Other task", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 2, PlanJson = JsonSerializer.Serialize(new {input = f.Delivery})});
        await db.SaveChangesAsync();
        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id) with {RemoveTaskIfCleanupFails = true});
        Assert.AreEqual(f.Task.Id, result.DeletedTaskIds.Single());
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
        CollectionAssert.Contains(result.CleanupResults.Single().RetainedDirectories!.ToArray(), f.Delivery);
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }
}

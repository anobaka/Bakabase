using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private sealed record StageDeletionFixture(AcquisitionTaskDbModel Task, ArtifactRecord Publication, ArtifactRecord HistoricalSource,
        AcquisitionPlan Plan, string Download, string Processing, string Delivery,
        AcquisitionTaskDeletionCleanupService Cleanup, AcquisitionTaskDeletionService Service);

    private async Task<StageDeletionFixture> StageDeletion()
    {
        var f = await OwnedDeliveryFixture(emptyDirectories: true);
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var downloadRoot = Directory.CreateDirectory(Path.Combine(root, "Downloads")).FullName;
        var processingRoot = Directory.CreateDirectory(Path.Combine(root, "Processing")).FullName;
        async Task<AcquisitionStageDirectory> Allocate(string role, string parent)
        {
            var allocation = new ArtifactDirectoryScope("acquisition", f.Task.Id.ToString(), role + "-nested.part",
                parent, Path.Combine(parent, "task-payload"));
            await resolver.EnsureScopedDirectoryAsync(allocation, default);
            return new(allocation.Directory, "exclusive", "taskAllocation") {Allocation = allocation};
        }
        var download = await Allocate("download", downloadRoot);
        var processing = await Allocate("processing", processingRoot);
        await File.WriteAllTextAsync(Path.Combine(download.Directory, "original.bin"), "download payload");
        await File.WriteAllTextAsync(Path.Combine(processing.Directory, "manual.key"), "user modified processing payload");
        var privateDirectory = Directory.CreateDirectory(Path.Combine(processing.Directory, ".bakabase-work", "operation", "result")).FullName;
        await File.WriteAllTextAsync(Path.Combine(privateDirectory, "private-copy.bin"), "old operation payload");
        var directories = f.Plan.StageDirectories!;
        var plan = f.Plan with
        {
            OutputDirectory = downloadRoot,
            StoragePolicy = new() {ProcessingWorkspaceDirectory = processingRoot},
            StageDirectories = directories with {Parts = new(directories.Parts)
                {["nested.part"] = directories.Parts["nested.part"] with {Download = download, Processing = processing}}}
        };
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json)));
        var task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, resolver);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage(), workspaces: resolver);
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity(), new(), files);
        return new(task, f.Final, f.Source, plan, download.Directory, processing.Directory, f.Final.RootDirectory, files, service);
    }

    [TestMethod]
    public async Task Accepted_stage_deletion_uses_three_current_trees_and_keeps_execution_history()
    {
        var f = await StageDeletion();
        var changed = Path.Combine(f.Delivery, "manually-renamed.bin");
        File.Move(f.Publication.Files.Single(), changed);
        await File.WriteAllTextAsync(changed, "changed length and content after acceptance");
        await File.WriteAllTextAsync(Path.Combine(f.Delivery, "new-final.key"), "manual key");
        var manifest = (await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == f.Publication.Id)).ManifestJson;
        var runs = await db.WorkflowRuns.CountAsync();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.AreEqual(3, groups.Count);
        Assert.IsTrue(groups.All(g => g.Eligible), string.Join(",", groups.Select(g => g.ReasonCode)));
        Assert.IsTrue(groups.All(g => g.ArtifactId.StartsWith("stage-directory:", StringComparison.Ordinal)));
        Assert.AreEqual(5, preview.CleanupFileCount);

        var result = await f.Service.DeleteAsync(new(preview.PreviewId));

        CollectionAssert.AreEqual(new[] {f.Task.Id}, result.DeletedTaskIds.ToArray());
        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.AreEqual(5, result.RemovedFiles);
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsFalse(Directory.Exists(f.Delivery));
        Assert.IsTrue(Directory.Exists(f.Plan.OutputDirectory));
        Assert.IsTrue(Directory.Exists(f.Plan.StoragePolicy!.ProcessingWorkspaceDirectory));
        Assert.IsTrue(Directory.Exists(f.Plan.DeliveryLayout!.RootDirectory));
        // Explicit stage cleanup authorizes the roots shown in this preview. Older
        // artifact locations outside them remain history, not an expanded deletion scope.
        Assert.IsTrue(f.HistoricalSource.Files.All(File.Exists));
        Assert.AreEqual(runs, await db.WorkflowRuns.CountAsync());
        Assert.AreEqual(manifest, (await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == f.Publication.Id)).ManifestJson);
    }

    [TestMethod]
    public async Task Stage_intermediate_cleanup_preserves_unselected_delivery_tree()
    {
        var f = await StageDeletion();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        Assert.AreEqual(2, preview.Rows.Single().Cleanup!.Groups.Count);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Already_removed_stage_roots_are_idempotent_in_public_delete_and_retry()
    {
        var f = await StageDeletion();
        Directory.Delete(f.Processing, true);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var processing = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing);
        Assert.IsTrue(processing.Eligible, processing.ReasonCode);
        Assert.AreEqual("AlreadyAbsent", processing.ReasonCode);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(result.CleanupResults!.Single().Completed);

        var retry = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true,
            RetryRetainedFiles: true, DeleteDeliveredFiles: true));
        var again = await f.Service.DeleteAsync(new(retry.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(again.CleanupResults!.Single().Completed);
        Assert.AreEqual(0, again.RemovedFiles);
        Assert.AreEqual(0, again.DeletedTaskIds.Count);
    }

    [DataTestMethod]
    [DataRow("new-file")]
    [DataRow("foreign-task")]
    [DataRow("active-consumer")]
    [DataRow("symlink")]
    public async Task Stage_deletion_rechecks_current_selection_and_external_use_before_unlinking(string change)
    {
        var f = await StageDeletion();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single().Eligible);
        if (change == "new-file") await File.WriteAllTextAsync(Path.Combine(f.Delivery, "new.key"), "not in confirmation");
        if (change == "foreign-task")
        {
            db.AcquisitionTasks.Add(new() {Title = "Other task", Status = AcquisitionStatus.Completed, WorkflowRunId = 2,
                PlanJson = JsonSerializer.Serialize(new {input = f.Delivery})});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        if (change == "active-consumer") await store.AttachConsumerAsync(f.Publication.OperationId, "outside-user", TimeSpan.FromHours(1));
        var outside = Path.Combine(root, "outside.bin");
        if (change == "symlink")
        {
            await File.WriteAllTextAsync(outside, "outside content");
            File.CreateSymbolicLink(Path.Combine(f.Delivery, "link.bin"), outside);
        }

        var result = await f.Service.DeleteAsync(new(preview.PreviewId));

        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        Assert.IsTrue(Directory.Exists(f.Download));
        Assert.IsTrue(Directory.Exists(f.Processing));
        if (change == "symlink") Assert.AreEqual("outside content", await File.ReadAllTextAsync(outside));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stage_root_deletion_requires_acceptance_and_skip_content_checks(bool accepted)
    {
        var f = await StageDeletion();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, accepted, false, DeleteDeliveredFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.All(g => !g.ArtifactId.StartsWith("stage-directory:", StringComparison.Ordinal)));
        Assert.IsTrue(Directory.Exists(f.Download));
        Assert.IsTrue(Directory.Exists(f.Processing));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Apply_never_executes_stage_candidate_disabled_in_final_summary()
    {
        var f = await StageDeletion();
        var plan = await f.Cleanup.PreviewAsync(f.Task, [1], true, true, default,
            cleanupIntermediates: false, deleteDeliveredFiles: true);
        Assert.AreEqual(1, plan.StageDirectories!.Count);
        Assert.IsTrue(plan.StageDirectories.Single().Group.Eligible);
        plan = plan with {Summary = plan.Summary with {Groups = plan.Summary.Groups.Select(g => g with
            {Eligible = false, ReasonCode = "SharedOwner", Shared = true}).ToArray()}};
        var result = await f.Cleanup.ApplyAsync(f.Task, plan, default);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Overlapping_cross_task_stage_selections_remove_executable_candidates()
    {
        var f = await StageDeletion();
        var plan = await f.Cleanup.PreviewAsync(f.Task, [1], true, true, default,
            cleanupIntermediates: false, deleteDeliveredFiles: true);
        var candidate = plan.StageDirectories!.Single();
        var other = candidate with {Id = candidate.Id + ":other", TaskId = 99,
            Group = candidate.Group with {ArtifactId = candidate.Id + ":other"}};
        var plans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>
        {
            [f.Task.Id] = plan,
            [99] = plan with {Summary = plan.Summary with {Groups = [other.Group]}, StageDirectories = [other]}
        };

        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);

        Assert.IsTrue(plans.Values.All(p => p.StageDirectories!.Count == 0));
        Assert.IsTrue(plans.Values.All(p => p.Summary.Groups.All(g => !g.Eligible && g.Shared)));
        var result = await f.Cleanup.ApplyAsync(f.Task, plans[f.Task.Id], default);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("valid")]
    [DataRow("missing-options")]
    [DataRow("wrong-operation")]
    [DataRow("foreign-owner")]
    [DataRow("wrong-destination")]
    public async Task Borrowed_download_root_requires_its_completed_transport_and_current_destination(string situation)
    {
        var f = await StageDeletion();
        var oldRoot = Path.Combine(root, "old-nas");
        var oldDirectory = Path.Combine(oldRoot, "old-download");
        var plan = f.Plan with
        {
            OutputDirectory = oldRoot,
            StageDirectories = f.Plan.StageDirectories! with {Parts = new(f.Plan.StageDirectories.Parts)
                {["nested.part"] = f.Plan.StageDirectories.Parts["nested.part"] with
                    {Download = new(f.Download, "borrowed", "downloadRecord")}}}
        };
        var revision = new CloudDownloadDestinationRevision(Guid.NewGuid().ToString("N"), oldDirectory,
            situation == "wrong-destination" ? Path.Combine(f.Plan.OutputDirectory, "another-task") : f.Download,
            f.Plan.OutputDirectory, DateTime.UtcNow, new('a', 64));
        var request = new CloudDownloadRequest(situation == "wrong-operation" ? "another-dispatch" : "stage-dispatch",
            new() {Id = "source", ProviderId = "baidu", Url = "https://example.test/share"}, null, null, oldDirectory)
            {DestinationRevisions = [revision]};
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json)));
        db.DownloadTasks.Add(new() {Id = 77, DownloadPath = f.Download, Status = DownloadTaskDbModelStatus.Complete,
            Options = situation == "missing-options" ? null : JsonSerializer.Serialize(request, Json)});
        db.CloudDownloadOperations.Add(new() {Id = "stage-dispatch", Kind = "download", State = "completed",
            WorkflowRunId = situation == "foreign-owner" ? 2 : 1, TargetId = "nested.part", DownloadTaskId = 77});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var selected = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Download);
        Assert.AreEqual(situation == "valid", selected.Eligible, selected.ReasonCode);
        if (situation == "valid")
        {
            var result = await f.Service.DeleteAsync(new(preview.PreviewId));
            Assert.IsTrue(result.CleanupResults!.Single().Completed);
            Assert.IsFalse(Directory.Exists(f.Download));
            Assert.AreEqual(DownloadTaskDbModelStatus.Complete, (await db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == 77)).Status);
            Assert.IsTrue(await db.CloudDownloadOperations.AnyAsync(o => o.Id == "stage-dispatch"));
        }
        else Assert.IsTrue(Directory.Exists(f.Download));
    }
}

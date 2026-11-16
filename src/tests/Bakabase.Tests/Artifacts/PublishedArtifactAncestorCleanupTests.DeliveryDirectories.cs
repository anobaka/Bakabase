using System.Buffers.Binary;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Source, ArtifactRecord Final,
        AcquisitionPlan Plan, AcquisitionTaskDeletionService Service)> OwnedDeliveryFixture(bool emptyDirectories = false)
    {
        var source = await Artifact("processedFiles", cleanupPolicy: false);
        var task = new AcquisitionTaskDbModel {Title = "Owned delivery", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 1};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync();
        var parts = new List<AcquisitionPlanPart> {new() {Id = "nested.part"}};
        var layout = new AcquisitionDeliveryDirectoryResolver(new Storage()).Resolve(task.Id, task.Title, null, parts,
            new() {RootDirectory = Path.Combine(root, "Acquired")});
        var plan = new AcquisitionPlan {Title = task.Title, Parts = parts, DeliveryLayout = layout,
            StageDirectories = new() {TaskId = task.Id, Parts = new() { ["nested.part"] = new()
                {Delivery = new(layout.TaskDirectory, "exclusive", "taskLayout")} }}};
        task.PlanJson = JsonSerializer.Serialize(plan, Json);
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 1, Module = "acquisition", EntityKind = "task",
            EntityId = task.Id.ToString(), Revision = "1"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var directory = Path.Combine(layout.TaskDirectory, "nested", "inner"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "result.bin"); await File.WriteAllTextAsync(file, "same bytes");
        var empty = Path.Combine(layout.TaskDirectory, "empty", "leaf");
        if (emptyDirectories) Directory.CreateDirectory(empty);
        var final = await store.AdoptAsync(new("owned-publication", "owned-publication", "files.publish", source.Fingerprint),
            new(layout.TaskDirectory, [file], "publishedFiles", false, [source.Id], Directories: emptyDirectories ? [empty] : []), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:artifact.publish", final.OperationId));
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, null);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage());
        return (task, source, final, plan, new(db, new FinishedDeletionActivity(), new(), files));
    }

    private static byte[] DeliveryFinderMetadata()
    {
        var bytes = new byte[6148]; "\0\0\0\u0001Bud1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 4096);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12, 4), 2048);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 4096);
        return bytes;
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Explicit_final_deletion_prunes_only_proven_task_root_and_registered_empty_directories(bool moved, bool skip)
    {
        var f = await OwnedDeliveryFixture(emptyDirectories: true);
        if (moved) File.Delete(f.Final.Files.Single());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, skip, DeleteDeliveredFiles: true));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(moved ? 0 : 1, result.RemovedFiles);
        Assert.IsTrue(result.DeletedTaskIds.Contains(f.Task.Id));
        Assert.IsFalse(Directory.Exists(f.Final.RootDirectory));
        Assert.IsTrue(Directory.Exists(f.Plan.DeliveryLayout!.RootDirectory));
        Assert.IsTrue(File.Exists(f.Source.Files.Single()));
        Assert.IsNotNull(await store.GetArtifactAsync(f.Final.Id));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
        var retry = await f.Service.PreviewAsync(new([f.Task.Id], false, true, skip,
            RetryRetainedFiles: true, DeleteDeliveredFiles: true));
        var retried = await f.Service.DeleteAsync(new(retry.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(0, retried.RemovedFiles);
        Assert.IsTrue(retried.CleanupResults!.Single().Completed);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Explicit_owned_root_cleanup_counts_and_journals_bounded_finder_metadata(bool skip, bool moved)
    {
        var f = await OwnedDeliveryFixture(emptyDirectories: true);
        var directories = new[] {f.Final.RootDirectory, Path.GetDirectoryName(f.Final.Files.Single())!,
            Path.Combine(f.Final.RootDirectory, "empty", "leaf")};
        foreach (var directory in directories)
            await File.WriteAllBytesAsync(Path.Combine(directory, ".DS_Store"), DeliveryFinderMetadata());
        if (moved) File.Delete(f.Final.Files.Single());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, skip, DeleteDeliveredFiles: true));
        Assert.AreEqual(moved ? 3 : 4, preview.CleanupFileCount);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(moved ? 3 : 4, result.RemovedFiles);
        Assert.AreEqual((moved ? 0 : f.Final.Manifest.Entries.Sum(e => e.Length)) + 3 * DeliveryFinderMetadata().Length, result.RemovedBytes);
        Assert.IsFalse(Directory.Exists(f.Final.RootDirectory));
        var receipts = Directory.GetFiles(Path.Combine(root, "file-processing", "workspace-cleanup-receipts"), "*.json");
        var receipt = JsonSerializer.Deserialize<ArtifactWorkspaceCleanupJournal.Receipt>(await File.ReadAllTextAsync(receipts.Single()), Json)!;
        Assert.AreEqual(moved ? 3 : 4, receipt.RemovedFiles);
        Assert.AreEqual("Completed", receipt.Status);
    }

    [DataTestMethod]
    [DataRow("file")]
    [DataRow("empty-directory")]
    [DataRow("invalid-finder")]
    [DataRow("unknown-directory-finder")]
    public async Task Verified_owned_root_cleanup_never_recurses_into_unknown_content(string scenario)
    {
        var f = await OwnedDeliveryFixture(emptyDirectories: true);
        var unknown = Path.Combine(f.Final.RootDirectory, scenario == "invalid-finder" ? ".DS_Store" : "manual");
        if (scenario is "empty-directory" or "unknown-directory-finder") Directory.CreateDirectory(unknown);
        else await File.WriteAllTextAsync(unknown, "unregistered user content");
        if (scenario == "unknown-directory-finder")
            await File.WriteAllBytesAsync(Path.Combine(unknown, ".DS_Store"), DeliveryFinderMetadata());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, false, DeleteDeliveredFiles: true));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.IsTrue(File.Exists(unknown) || Directory.Exists(unknown));
        Assert.IsTrue(Directory.Exists(f.Final.RootDirectory));
        Assert.IsFalse(Directory.Exists(Path.Combine(f.Final.RootDirectory, "empty")));
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
        CollectionAssert.Contains(result.CleanupResults.Single().ReasonCodes.ToArray(), "CLEANUP_FILES_RETAINED");
        CollectionAssert.Contains(result.CleanupResults.Single().RetainedDirectories!.ToArray(), f.Final.RootDirectory);
        if (scenario == "unknown-directory-finder") Assert.IsTrue(File.Exists(Path.Combine(unknown, ".DS_Store")));
    }

    [DataTestMethod]
    [DataRow("wrong-task")]
    [DataRow("wrong-revision")]
    [DataRow("wrong-part")]
    [DataRow("borrowed")]
    [DataRow("shared-configured-root")]
    [DataRow("foreign-empty-registration")]
    [DataRow("foreign-ancestor-registration")]
    [DataRow("no-final-consent")]
    public async Task Unproven_or_shared_allocation_does_not_authorize_root_or_finder_cleanup(string scenario)
    {
        var f = await OwnedDeliveryFixture();
        var plan = f.Plan;
        if (scenario == "wrong-task") plan = plan with {DeliveryLayout = plan.DeliveryLayout! with {TaskId = f.Task.Id + 1}};
        if (scenario == "wrong-part") plan = plan with {Parts = [new() {Id = "other"}]};
        if (scenario == "borrowed") plan = plan with {StageDirectories = plan.StageDirectories! with
            {Parts = new() { ["nested.part"] = new() {Delivery = new(f.Final.RootDirectory, "borrowed", "deliveryRecord")} }}};
        if (scenario == "shared-configured-root") plan = plan with {DeliveryLayout = plan.DeliveryLayout! with
            {RootDirectory = f.Final.RootDirectory}};
        if (scenario == "wrong-revision")
            await db.Set<WorkflowRunSourceDbModel>().ExecuteUpdateAsync(u => u.SetProperty(s => s.Revision, "99"));
        if (scenario is "foreign-empty-registration" or "foreign-ancestor-registration")
        {
            var foreignRoot = scenario == "foreign-empty-registration" ? f.Final.RootDirectory : f.Plan.DeliveryLayout!.RootDirectory;
            var foreignFile = Path.Combine(foreignRoot, "foreign-moved.bin"); await File.WriteAllTextAsync(foreignFile, "foreign");
            await store.AdoptAsync(new("foreign-root", "foreign-root", "files.process", "fixture"),
                new(foreignRoot, [foreignFile], "processedFiles", true,
                    Directories: scenario == "foreign-ancestor-registration" ? [f.Final.RootDirectory] : []), "foreign-owner");
            File.Delete(foreignFile);
        }
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u =>
            u.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json)));
        var finder = Path.Combine(f.Final.RootDirectory, ".DS_Store");
        await File.WriteAllBytesAsync(finder, DeliveryFinderMetadata());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false,
            scenario != "no-final-consent", scenario != "no-final-consent",
            DeleteDeliveredFiles: scenario != "no-final-consent"));
        await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(Directory.Exists(f.Final.RootDirectory)); Assert.IsTrue(File.Exists(finder));
        if (scenario == "no-final-consent") Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    public async Task Historical_owned_delivery_uses_its_bound_plan_revision_for_empty_root_cleanup()
    {
        var f = await OwnedDeliveryFixture();
        var history = JsonSerializer.Serialize(new[] {new AcquisitionPlanRevision(1, f.Plan, [], DateTime.UtcNow)}, Json);
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.PlanRevision, 2).SetProperty(t => t.PlanJson, "null").SetProperty(t => t.PlanHistoryJson, history));
        File.Delete(f.Final.Files.Single());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsFalse(Directory.Exists(f.Final.RootDirectory));
    }

    [DataTestMethod]
    [DataRow("active-consumer")]
    [DataRow("foreign-task")]
    [DataRow("symlink")]
    public async Task Owned_delivery_directory_guards_still_apply_after_preview(string scenario)
    {
        if (scenario == "symlink" && OperatingSystem.IsWindows()) return;
        var f = await OwnedDeliveryFixture();
        var finder = Path.Combine(f.Final.RootDirectory, ".DS_Store");
        await File.WriteAllBytesAsync(finder, DeliveryFinderMetadata());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        string? moved = null;
        if (scenario == "active-consumer")
            Assert.IsTrue(await store.AttachConsumerAsync(f.Final.OperationId, "external-reader", TimeSpan.FromHours(1)));
        if (scenario == "foreign-task")
        {
            db.AcquisitionTasks.Add(new() {Title = "Foreign task", Status = AcquisitionStatus.Waiting,
                PreparedContextJson = JsonSerializer.Serialize(new {Input = f.Final.RootDirectory})});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        if (scenario == "symlink")
        {
            moved = f.Final.RootDirectory + "-manual"; Directory.Move(f.Final.RootDirectory, moved);
            Directory.CreateSymbolicLink(f.Final.RootDirectory, moved);
        }
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(finder));
        if (moved != null) Directory.Delete(f.Final.RootDirectory);
    }

    [TestMethod]
    public async Task Changed_finder_metadata_is_preserved_without_expanding_the_confirmation()
    {
        var f = await OwnedDeliveryFixture();
        var finder = Path.Combine(f.Final.RootDirectory, ".DS_Store");
        await File.WriteAllBytesAsync(finder, DeliveryFinderMetadata());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], false, true, true, DeleteDeliveredFiles: true));
        await File.WriteAllTextAsync(finder, "replacement user content");
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.RemovedFiles, "The fixed current-directory selection changed after confirmation.");
        Assert.AreEqual("replacement user content", await File.ReadAllTextAsync(finder));
        Assert.IsTrue(Directory.Exists(f.Final.RootDirectory));
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
    }
}

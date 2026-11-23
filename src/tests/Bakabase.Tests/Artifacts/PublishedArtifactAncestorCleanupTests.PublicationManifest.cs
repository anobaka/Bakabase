using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task<StageDeletionFixture> HistoricalPublicationWithoutLayout(bool deleted)
    {
        var f = await StageDeletion();
        var saved = f.Plan with {DeliveryLayout = null, StageDirectories = null};
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(saved, Json)));
        if (deleted) await DeleteStageRecordOnlyAsync(f);
        return f;
    }

    [DataTestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public async Task Historical_publication_with_unowned_directory_deletes_only_registered_leaves(bool history, bool skip)
    {
        var f = await HistoricalPublicationWithoutLayout(history);
        var unknown = Path.Combine(f.Delivery, ".DS_Store");
        await File.WriteAllBytesAsync(unknown, DeliveryFinderMetadata());
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: false, AcceptDeliveredFiles: skip,
            SkipFileVerification: skip, RetryRetainedFiles: history, DeleteDeliveredFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.IsTrue(group.Eligible, group.ReasonCode);
        Assert.AreEqual("task-artifact:" + f.Publication.Id, group.ArtifactId);
        Assert.IsTrue(group.RegisteredFilesOnly);
        CollectionAssert.AreEqual(f.Publication.Files.ToArray(), group.Files.Select(v => v.Path).ToArray());
        var select = history ? new Dictionary<int, IReadOnlyList<string>> {[f.Task.Id] = [f.Delivery]} : null;
        var input = new AcquisitionTaskDeleteInput(preview.PreviewId, RetryRetainedFiles: history,
            DeleteDeliveredFiles: true, SelectedCleanupDirectories: select);
        var result = await f.Service.DeleteAsync(input);
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.AreEqual(group.Bytes, result.RemovedBytes);
        Assert.IsFalse(File.Exists(f.Publication.Files.Single()));
        Assert.IsTrue(File.Exists(unknown)); Assert.IsTrue(Directory.Exists(Path.Combine(f.Delivery, "empty", "leaf")));
        Assert.IsTrue(f.HistoricalSource.Files.All(File.Exists));
        Assert.AreEqual(result.RemovedBytes, (await f.Service.DeleteAsync(input)).RemovedBytes);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Historical_publication_files_are_selectable_per_root_and_ambiguous_publications_stay_protected(bool sameRoot)
    {
        var f = await HistoricalPublicationWithoutLayout(true);
        var otherDirectory = sameRoot ? f.Delivery : Directory.CreateDirectory(Path.Combine(root, "second-volume")).FullName;
        var otherFile = Path.Combine(otherDirectory, "second.bin");
        await File.WriteAllTextAsync(otherFile, "a different publication");
        var other = await store.AdoptAsync(new("second-publication", "second-publication", "files.publish", f.HistoricalSource.Fingerprint),
            new(otherDirectory, [otherFile], "publishedFiles", false, [f.HistoricalSource.Id]), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:second:artifact.publish", other.OperationId));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], RetryRetainedFiles: true, DeleteDeliveredFiles: true));
        var row = preview.Rows.Single();
        if (sameRoot)
        {
            Assert.IsFalse(row.CanDelete);
            Assert.AreEqual("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED", row.Cleanup!.Groups.Single().ReasonCode);
        }
        else
        {
            Assert.AreEqual(2, row.Cleanup!.Groups.Count(g => g.Eligible && g.RegisteredFilesOnly));
            var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true, DeleteDeliveredFiles: true,
                SelectedCleanupDirectories: new Dictionary<int, IReadOnlyList<string>> {[f.Task.Id] = [f.Delivery]}));
            Assert.AreEqual(1, result.RemovedFiles);
            Assert.IsFalse(File.Exists(f.Publication.Files.Single()));
            CollectionAssert.Contains(result.CleanupResults!.Single().UnselectedDirectories!.ToArray(), otherDirectory);
        }
        Assert.IsTrue(File.Exists(otherFile));
    }

    [TestMethod]
    public async Task Historical_publication_manifest_does_not_claim_that_a_shared_intermediate_scope_was_cleaned()
    {
        var f = await HistoricalPublicationWithoutLayout(true);
        var cached = Path.Combine(f.Delivery, "unrelated-stage-output.bin");
        await File.WriteAllTextAsync(cached, "processing result");
        var processed = await store.AdoptAsync(new("extra-stage", "extra-stage", "files.process", f.HistoricalSource.Fingerprint),
            new(f.Delivery, [cached], "processedFiles", true, [f.HistoricalSource.Id]), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:additional-output", processed.OperationId));
        var mixed = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: true, RetryRetainedFiles: true, DeleteDeliveredFiles: true));
        Assert.IsFalse(mixed.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Delivery).Eligible);
        var final = await f.Service.PreviewAsync(new([f.Task.Id], RetryRetainedFiles: true, DeleteDeliveredFiles: true));
        Assert.IsTrue(final.Rows.Single().Cleanup!.Groups.Single().RegisteredFilesOnly);
        var result = await f.Service.DeleteAsync(new(final.PreviewId, RetryRetainedFiles: true, DeleteDeliveredFiles: true));
        Assert.AreEqual(1, result.RemovedFiles); Assert.IsTrue(File.Exists(cached));
    }

    [DataTestMethod]
    [DataRow("capacity", true)]
    [DataRow("exact-file", false)]
    [DataRow("untyped-parent", false)]
    [DataRow("other-working-directory", false)]
    [DataRow("unknown-receipt", false)]
    [DataRow("unproven-owner", false)]
    public async Task Historical_publication_manifest_preserves_real_references_in_another_tasks_recovery(string scenario, bool eligible)
    {
        var f = await HistoricalPublicationWithoutLayout(true);
        var capacity = Path.GetDirectoryName(f.Delivery)!;
        var file = f.Publication.Files.Single();
        var legacy = new AcquisitionPlan {Title = "Other", OutputDirectory = capacity, Parts = [new() {Id = "part"}]};
        var history = new[] {new AcquisitionPlanRevision(1, legacy, [], DateTime.UtcNow.AddDays(-1)),
            new AcquisitionPlanRevision(2, legacy, [], DateTime.UtcNow, "continueFiles", 2)};
        var queue = JsonSerializer.SerializeToNode(new AcquisitionLocalRecoveryQueueEntry {BatchId = Guid.NewGuid().ToString("N"),
            Version = new('a', 64), StorageStamp = new('b', 64), Signal = "{\"observeExistingOnly\":true}",
            Status = "attention", RecoveryScope = "workflow", ActionCategory = "workflow", WorkflowRunId = 2, PlanRevision = 1,
            WorkingDirectories = scenario == "other-working-directory" ? [capacity, Path.GetDirectoryName(file)!] : [capacity]}, Json)!;
        var builder = new JsonObject { ["localRecoveryQueue"] = queue };
        if (scenario == "exact-file") builder["selectedInput"] = file;
        if (scenario == "untyped-parent") builder["selectedInput"] = capacity;
        if (scenario == "unknown-receipt") queue["unrecognized"] = true;
        var other = new AcquisitionTaskDbModel {Title = "Unrelated acquisition", WorkflowRunId = 3, PlanRevision = 2,
            PlanJson = JsonSerializer.Serialize(legacy, Json), PlanHistoryJson = JsonSerializer.Serialize(history, Json), BuilderStateJson = builder.ToJsonString()};
        db.AcquisitionTasks.Add(other); await db.SaveChangesAsync();
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 3, Module = "acquisition", EntityKind = "task", EntityId = other.Id.ToString(), Revision = "2"});
        if (scenario != "unproven-owner") db.Set<WorkflowRunSourceDbModel>().Add(new()
            {WorkflowRunId = 2, Module = "acquisition", EntityKind = "task", EntityId = other.Id.ToString(), Revision = "1"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], RetryRetainedFiles: true, DeleteDeliveredFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual(eligible, group.Eligible, group.ReasonCode);
        if (eligible)
        {
            Assert.IsTrue(group.RegisteredFilesOnly);
            Assert.AreEqual(1, (await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true, DeleteDeliveredFiles: true))).RemovedFiles);
        }
        Assert.AreEqual(!eligible, File.Exists(file));
    }

    [DataTestMethod]
    [DataRow("modified")]
    [DataRow("shared")]
    [DataRow("resource")]
    [DataRow("consumer")]
    [DataRow("overlap")]
    public async Task Historical_publication_manifest_rechecks_bytes_and_references_even_if_verification_was_skipped(string change)
    {
        var f = await HistoricalPublicationWithoutLayout(true);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], CleanupFiles: false, AcceptDeliveredFiles: true,
            SkipFileVerification: true, RetryRetainedFiles: true, DeleteDeliveredFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.IsTrue(group.Eligible, group.ReasonCode);
        var file = f.Publication.Files.Single();
        switch (change)
        {
            case "modified":
                var mtime = File.GetLastWriteTimeUtc(file);
                await File.WriteAllBytesAsync(file, new byte[new FileInfo(file).Length]);
                File.SetLastWriteTimeUtc(file, mtime);
                break;
            case "shared":
                await new ArtifactOperationBindingService(db).BindAsync("workflow", "3", "also-uses-publication", f.Publication.OperationId);
                break;
            case "resource":
                db.ResourcesV2.Add(new() {Path = file, IsFile = true}); await db.SaveChangesAsync(); break;
            case "consumer":
                Assert.IsTrue(await store.AttachConsumerAsync(f.Publication.OperationId, "active-reader", TimeSpan.FromHours(1))); break;
            case "overlap":
                await store.AdoptAsync(new("another-file", "another-file", "files.process", "other"),
                    new(Path.GetDirectoryName(file)!, [file], "processedFiles", true), "other-owner"); break;
        }
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true, DeleteDeliveredFiles: true,
            SelectedCleanupDirectories: new Dictionary<int, IReadOnlyList<string>> {[f.Task.Id] = [f.Delivery]}));
        Assert.AreEqual(0, result.RemovedFiles); Assert.IsTrue(File.Exists(file)); Assert.IsTrue(f.HistoricalSource.Files.All(File.Exists));
    }
}

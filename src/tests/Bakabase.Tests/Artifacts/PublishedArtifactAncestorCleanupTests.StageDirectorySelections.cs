using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task SaveStageSelectionPlan(StageDeletionFixture fixture, AcquisitionPlan plan,
        params AcquisitionPlan[] previous)
    {
        var history = previous.Select((p, i) => new AcquisitionPlanRevision(i + 1, p, [], DateTime.UtcNow,
            "directories.previous", 1)).ToArray();
        await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json))
                .SetProperty(t => t.PlanHistoryJson, JsonSerializer.Serialize(history, Json)));
    }

    private static AcquisitionStageDirectory ExplicitStage(string path) =>
        new(path, "borrowed", "userDirectoryRevision") {BindingId = Guid.NewGuid().ToString("N")};

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Whole_task_deletion_includes_retained_download_revision_only_with_equal_dispatch_records(bool changedDispatch)
    {
        var fixture = await StageDeletion();
        var nativeDirectory = Directory.CreateDirectory(Path.Combine(fixture.Plan.OutputDirectory, "current-download")).FullName;
        var oldDirectory = Directory.CreateDirectory(Path.Combine(fixture.Plan.OutputDirectory, "former-download")).FullName;
        await File.WriteAllTextAsync(Path.Combine(nativeDirectory, "file"), "downloaded");
        await File.WriteAllTextAsync(Path.Combine(oldDirectory, "file"), "retained source");
        var revision = new CloudDownloadDestinationRevision(Guid.NewGuid().ToString("N"), oldDirectory, nativeDirectory,
            fixture.Plan.OutputDirectory, DateTime.UtcNow, new('b',64)) {ReuseInventoryFingerprint = new('c',64)};
        var request = new CloudDownloadRequest("native-download", new() {ProviderId = "baidu", Id = "source", Url = "fixture"},
            null, null, oldDirectory) {DestinationRevisions = [revision]};
        var options = JsonSerializer.Serialize(request, Json);
        db.DownloadTasks.Add(new() {Id = 77, Status = DownloadTaskDbModelStatus.Complete, DownloadPath = nativeDirectory, Options = options});
        db.CloudDownloadOperations.Add(new() {Id = request.OperationId, Kind = "download", TargetId = "nested.part", WorkflowRunId = 1,
            State = "started", DownloadTaskId = 77, RequestJson = changedDispatch ? "{}" : options});
        await db.SaveChangesAsync();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var history = preview.Rows.Single().Cleanup!.Groups.Where(g => g.Directory == oldDirectory).ToArray();
        Assert.AreEqual(changedDispatch ? 0 : 1, history.Length);
        if (!changedDispatch) Assert.IsTrue(history[0].Eligible, history[0].ReasonCode);
        var response = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(response.CleanupResults!.Single().Completed);
        Assert.AreEqual(changedDispatch, Directory.Exists(oldDirectory));
        Assert.IsFalse(Directory.Exists(nativeDirectory));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Retry_deleted_legacy_task_removes_native_tree_nested_processing_and_recorded_delivery(
        bool recordedInnerDownload)
    {
        var f = await StageDeletion();
        var transportDirectory = Directory.CreateDirectory(Path.Combine(f.Plan.OutputDirectory, "allocated-download")).FullName;
        var resultDirectory = Directory.CreateDirectory(Path.Combine(transportDirectory, "provider-result")).FullName;
        var original = Path.Combine(resultDirectory, "original.bin");
        await File.WriteAllTextAsync(original, "download");
        var request = new CloudDownloadRequest("native-download", new()
            {ProviderId = "baidu", Id = "source", Url = "https://pan.baidu.com/s/example"}, null, null, transportDirectory);
        var options = JsonSerializer.Serialize(request, Json);
        db.DownloadTasks.Add(new() {Id = 77, Status = DownloadTaskDbModelStatus.Complete, DownloadPath = transportDirectory, Options = options});
        db.CloudDownloadOperations.Add(new() {Id = request.OperationId, Kind = "download", TargetId = "nested.part",
            WorkflowRunId = 1, State = "started", DownloadTaskId = 77, RequestJson = options});
        db.DownloadResults.Add(new() {DownloadTaskId = 77, Path = resultDirectory, FilesJson = JsonSerializer.Serialize(new[] {original})});
        await db.SaveChangesAsync();

        var source = await store.AdoptAsync(new("legacy-native", "legacy-native", "download", "download"),
            new(resultDirectory, [original], "download", true, MetadataJson: JsonSerializer.Serialize(new DownloadArtifactMetadata(77, null), Json)), "fixture");
        var bindings = new ArtifactOperationBindingService(db);
        Assert.IsTrue(await bindings.BindAsync("workflow", "1", "nested.part:download:legacy", source.OperationId));
        var operationId = new string('c', 64);
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var workspace = await resolver.ResolveAsync("operations", operationId, 1, resultDirectory, resultDirectory, default, newAttempt: true);
        var result = Directory.CreateDirectory(Path.Combine(workspace, "result")).FullName;
        var processed = Path.Combine(result, "processed.bin");
        await File.WriteAllTextAsync(processed, "processed");
        await File.WriteAllTextAsync(Path.Combine(workspace, "staged-copy.bin"), "intermediate");
        var output = await store.AdoptAsync(new(operationId, operationId, "files.process", source.Fingerprint),
            new(result, [processed], "processedFiles", true, [source.Id]), "fixture");
        Assert.IsTrue(await bindings.BindAsync("workflow", "1", "nested.part:process:legacy", output.OperationId));
        var legacy = f.Plan with {StageDirectories = null};
        var current = recordedInnerDownload ? f.Plan with {StageDirectories = f.Plan.StageDirectories! with
        {
            Parts = new() {["nested.part"] = f.Plan.StageDirectories.Parts["nested.part"] with
                {Download = new(resultDirectory, "borrowed", "downloadRecord")}}
        }} : legacy;
        await SaveStageSelectionPlan(f, current, legacy);
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(t => t.DeletedAt, (DateTime?)DateTime.UtcNow));
        var unrelated = Path.Combine(f.Plan.OutputDirectory, "other-task.bin");
        await File.WriteAllTextAsync(unrelated, "keep");

        var deletion = await f.Service.DeleteAsync(DirectDelete(f.Task.Id) with {RetryRetainedFiles = true});

        var outcome = deletion.CleanupResults!.Single();
        Assert.IsTrue(outcome.Completed, string.Join(",", outcome.ReasonCodes));
        Assert.IsFalse(Directory.Exists(transportDirectory));
        Assert.IsFalse(Directory.Exists(f.Delivery));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(unrelated));
    }

    [TestMethod]
    public async Task Accepted_explicit_borrowed_roots_and_recorded_delivery_are_deleted_without_old_exclusive_flags()
    {
        var f = await StageDeletion();
        var part = f.Plan.StageDirectories!.Parts["nested.part"];
        var plan = f.Plan with {StageDirectories = f.Plan.StageDirectories with {Parts = new(f.Plan.StageDirectories.Parts)
            {["nested.part"] = part with {Download = ExplicitStage(f.Download), Processing = ExplicitStage(f.Processing),
                Delivery = new(f.Delivery, "borrowed", "plannedDelivery")}}}};
        await SaveStageSelectionPlan(f, plan);

        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        Assert.AreEqual(3, preview.Rows.Single().Cleanup!.Groups.Count);
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.All(g => g.Eligible));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));

        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(f.Download));
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsFalse(Directory.Exists(f.Delivery));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
    }

    [TestMethod]
    public async Task Old_allocated_processing_parent_is_selected_after_current_binding_moves_to_child()
    {
        var f = await StageDeletion();
        var child = Directory.CreateDirectory(Path.Combine(f.Processing, "encoding-repair")).FullName;
        await File.WriteAllTextAsync(Path.Combine(child, "fixed.jpg"), "accepted output");
        var plan = f.Plan with {StageDirectories = f.Plan.StageDirectories! with {Parts = new(f.Plan.StageDirectories.Parts)
            {["nested.part"] = f.Plan.StageDirectories.Parts["nested.part"] with {Processing = ExplicitStage(child)}}}};
        await SaveStageSelectionPlan(f, plan, f.Plan);

        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsTrue(groups.Any(g => g.Directory == f.Processing && g.Eligible));
        Assert.IsFalse(groups.Any(g => g.Directory == child));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));

        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsTrue(await db.AcquisitionTasks.AnyAsync(t => t.Id == f.Task.Id && t.PlanHistoryJson != null));
    }

    [TestMethod]
    public async Task Explicit_selected_nested_stages_are_deduplicated_but_unrecorded_parent_is_never_inferred()
    {
        var f = await StageDeletion();
        var child = Directory.CreateDirectory(Path.Combine(f.Processing, "download-input")).FullName;
        await File.WriteAllTextAsync(Path.Combine(child, "input.zip"), "archive");
        var plan = f.Plan with {StageDirectories = f.Plan.StageDirectories! with {Parts = new(f.Plan.StageDirectories.Parts)
            {["nested.part"] = f.Plan.StageDirectories.Parts["nested.part"] with {Download = ExplicitStage(child)}}}};
        await SaveStageSelectionPlan(f, plan);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        Assert.AreEqual(2, preview.Rows.Single().Cleanup!.Groups.Count);
        Assert.IsFalse(preview.Rows.Single().Cleanup!.Groups.Any(g => g.Directory == child));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(f.Processing));
        Assert.IsTrue(Directory.Exists(f.Download), "No current or historical binding selected this old folder.");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_directory_covering_processing_and_delivery_records_both_scopes(bool sameDirectory)
    {
        var f = await StageDeletion();
        var nested = sameDirectory ? f.Processing : Directory.CreateDirectory(Path.Combine(f.Processing, "delivery")).FullName;
        var part = f.Plan.StageDirectories!.Parts["nested.part"] with {Delivery = ExplicitStage(nested)};
        var plan = f.Plan with {StageDirectories = f.Plan.StageDirectories with {Parts = new(f.Plan.StageDirectories.Parts)
            {["nested.part"] = part}}};
        await SaveStageSelectionPlan(f, plan);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing);
        Assert.IsTrue(group.Eligible, group.ReasonCode);
        var lateFile = Path.Combine(nested, "arrived-after-preview.bin");
        await File.WriteAllTextAsync(lateFile, "changed");
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.IsTrue(result.DeletedTaskIds.Contains(f.Task.Id));
        Assert.IsTrue(File.Exists(lateFile));
        Assert.IsFalse(Directory.Exists(f.Download));
        var saved = (await f.Service.GetDeletedTasksAsync(1, 25, "incomplete", default)).Items.Single();
        Assert.AreEqual("incomplete", saved.IntermediateCleanup);
        Assert.AreEqual("incomplete", saved.DeliveryCleanup);
    }

    [TestMethod]
    public async Task Historical_explicit_binding_is_authorized_exactly_not_its_arbitrary_parent()
    {
        var f = await StageDeletion();
        var oldParent = Directory.CreateDirectory(Path.Combine(root, "manual-parent")).FullName;
        var old = Directory.CreateDirectory(Path.Combine(oldParent, "selected-old-stage")).FullName;
        var unrelated = Path.Combine(oldParent, "unrelated.key");
        await File.WriteAllTextAsync(unrelated, "keep outside explicit selection");
        await File.WriteAllTextAsync(Path.Combine(old, "old.bin"), "old processing");
        var prior = f.Plan with {StageDirectories = f.Plan.StageDirectories! with {Parts = new(f.Plan.StageDirectories.Parts)
            {["nested.part"] = f.Plan.StageDirectories.Parts["nested.part"] with {Processing = ExplicitStage(old)}}}};
        await SaveStageSelectionPlan(f, f.Plan, prior);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Any(g => g.Directory == old && g.Eligible));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(old));
        Assert.IsTrue(File.Exists(unrelated));
    }

    [TestMethod]
    public async Task Unselected_earlier_publication_is_protected_inside_selected_processing_parent()
    {
        var f = await StageDeletion();
        var oldDelivery = Directory.CreateDirectory(Path.Combine(f.Processing, "old-final")).FullName;
        var file = Path.Combine(oldDelivery, "keep.jpg"); await File.WriteAllTextAsync(file, "formal file");
        var id = Guid.NewGuid().ToString("N");
        var old = await store.AdoptAsync(new(id, id, "files.publish", "old"), new(oldDelivery, [file], "publishedFiles", false), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:old:artifact.publish", old.OperationId));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        var row = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Processing);
        Assert.IsFalse(row.Eligible);
        Assert.AreEqual("ReferencedPath", row.ReasonCode);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
        Assert.IsTrue(File.Exists(file));
        Assert.IsTrue(Directory.Exists(f.Delivery));
        Assert.IsFalse(Directory.Exists(f.Download));
    }

    [TestMethod]
    public async Task User_binding_cannot_authorize_deleting_shared_configuration_root()
    {
        var logger = new CleanupDiagnosticLogger();
        var f = await StageDeletion(logger);
        var plan = f.Plan with {StageDirectories = f.Plan.StageDirectories! with {Parts = new(f.Plan.StageDirectories.Parts)
            {["nested.part"] = f.Plan.StageDirectories.Parts["nested.part"] with {Processing = ExplicitStage(f.Plan.StoragePolicy!.ProcessingWorkspaceDirectory!)}}}};
        await SaveStageSelectionPlan(f, plan);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        var row = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Plan.StoragePolicy!.ProcessingWorkspaceDirectory);
        Assert.IsFalse(row.Eligible);
        Assert.AreEqual("ReferencedPath", row.ReasonCode);
        CollectionAssert.AreEqual(new[] {$"Acquisition cleanup retained task {f.Task.Id} stage processing due to reference configured-root"},
            logger.Messages);
        Assert.IsTrue(Directory.Exists(f.Processing));
    }

    [TestMethod]
    public async Task Historical_unproven_borrowed_parent_is_not_promoted_from_directory_name()
    {
        var f = await StageDeletion();
        var unknown = Directory.CreateDirectory(Path.Combine(root, "unproven-parent")).FullName;
        await File.WriteAllTextAsync(Path.Combine(unknown, "other-file.key"), "keep");
        var prior = f.Plan with {StageDirectories = f.Plan.StageDirectories! with {Parts = new(f.Plan.StageDirectories.Parts)
            {["nested.part"] = f.Plan.StageDirectories.Parts["nested.part"] with {Processing = new(unknown, "borrowed", "processingRecord")}}}};
        await SaveStageSelectionPlan(f, f.Plan, prior);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        Assert.IsFalse(preview.Rows.Single().Cleanup!.Groups.Any(g => g.Directory == unknown));
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.IsTrue(Directory.Exists(unknown));
    }

    [DataTestMethod]
    [DataRow("absent", "AlreadyAbsent")]
    [DataRow("present", "CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED")]
    [DataRow("unavailable", "WorkspaceUnavailable")]
    [DataRow("different-layout", "CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED")]
    public async Task Historical_delivery_without_publication_requires_exact_layout_and_proven_absence(string scenario,
        string reason)
    {
        var f = await StageDeletion();
        var layout = f.Plan.DeliveryLayout!;
        var oldParent = scenario == "unavailable" ? Path.Combine(layout.RootDirectory, "missing-parent") : layout.RootDirectory;
        var oldDirectory = Path.Combine(oldParent, "old-delivery");
        var oldLayoutDirectory = scenario == "different-layout" ? Path.Combine(oldParent, "another-directory") : oldDirectory;
        var oldPayload = Path.Combine(oldDirectory, "untouched.bin");
        if (scenario == "present")
        {
            Directory.CreateDirectory(oldDirectory);
            await File.WriteAllTextAsync(oldPayload, "unverified file");
        }
        var stages = f.Plan.StageDirectories!;
        var old = f.Plan with
        {
            DeliveryLayout = layout with {TaskDirectory = oldLayoutDirectory,
                PartDirectories = new() {["nested.part"] = oldLayoutDirectory}},
            StageDirectories = stages with {Parts = new(stages.Parts)
                {["nested.part"] = stages.Parts["nested.part"] with
                    {Delivery = new(oldDirectory, "exclusive", "taskLayout")}}}
        };
        await SaveStageSelectionPlan(f, f.Plan, old);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == oldDirectory);
        Assert.AreEqual(reason, group.ReasonCode);
        Assert.AreEqual(scenario == "absent", group.Eligible);

        var result = await f.Service.DeleteAsync(DirectDelete(f.Task.Id));

        Assert.AreEqual(scenario == "absent", result.CleanupResults!.Single().Completed);
        Assert.AreEqual(scenario == "present", File.Exists(oldPayload));
        Assert.IsFalse(Directory.Exists(f.Delivery));
    }

    [TestMethod]
    public async Task Authenticated_private_operation_outside_current_stage_is_selected_without_expanding_storage_root()
    {
        var f = await StageDeletion();
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var id = new string('c', 64);
        var directory = await resolver.ResolveAsync("operations", id, 1, f.Download,
            f.Plan.StoragePolicy!.ProcessingWorkspaceDirectory, default, newAttempt: true);
        var result = Directory.CreateDirectory(Path.Combine(directory, "result")).FullName;
        var file = Path.Combine(result, "historical.bin"); await File.WriteAllTextAsync(file, "retained processed payload");
        var artifact = await store.AdoptAsync(new(id, id, "files.process", "historical"),
            new(result, [file], "processedFiles", true), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:process:old", artifact.OperationId));
        Assert.AreEqual(id, artifact.OperationId);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, DeleteDeliveredFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Any(g => g.Directory == directory && g.Eligible),
            string.Join(",", preview.Rows.Single().Cleanup!.Groups.Select(g => g.Directory + "=" + g.ReasonCode)));
        var deletion = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.IsTrue(deletion.CleanupResults!.Single().Completed);
        Assert.IsFalse(Directory.Exists(directory));
        Assert.IsTrue(Directory.Exists(f.Plan.StoragePolicy.ProcessingWorkspaceDirectory));
        Assert.IsNotNull(await store.GetArtifactAsync(artifact.Id));
    }
}

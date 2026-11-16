using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
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
        var f = await StageDeletion();
        var plan = f.Plan with {StageDirectories = f.Plan.StageDirectories! with {Parts = new(f.Plan.StageDirectories.Parts)
            {["nested.part"] = f.Plan.StageDirectories.Parts["nested.part"] with {Processing = ExplicitStage(f.Plan.StoragePolicy!.ProcessingWorkspaceDirectory!)}}}};
        await SaveStageSelectionPlan(f, plan);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true));
        var row = preview.Rows.Single().Cleanup!.Groups.Single(g => g.Directory == f.Plan.StoragePolicy!.ProcessingWorkspaceDirectory);
        Assert.IsFalse(row.Eligible);
        Assert.AreEqual("ReferencedPath", row.ReasonCode);
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

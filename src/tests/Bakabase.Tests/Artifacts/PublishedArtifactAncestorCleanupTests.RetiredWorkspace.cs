using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Source, ArtifactRecord Final,
        AcquisitionTaskDeletionService Service, string OperationRoot, string StateFile, string ReceiptFile)>
        EmptyRetiredWorkspaceFixture(bool boundary = false)
    {
        var inputRoot = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
        var inputFile = Path.Combine(inputRoot, "content.bin");
        await File.WriteAllTextAsync(inputFile, "preserved content");
        var workRoot = Directory.CreateDirectory(Path.Combine(root, "processing")).FullName;
        var workspaces = new FileProcessingWorkspaceResolver(root);
        var key = ArtifactManifestFiles.Hash("retired-workspace-fixture");
        var executor = new FileProcessingPlanExecutor(new NoDeletionExtraction());
        var recipe = boundary ? new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "first", Op = "renameFile", TargetName = "content.bin"},
            new() {Id = "second", Op = "renameFile", TargetName = "content.bin", Input = "first"}]} :
            new FileProcessingPlan {Requirement = "notRequired"};
        var operationRoot = await workspaces.ResolveAsync(boundary ? "checkpoints" : "operations",
            boundary ? FileArtifactProcessor.CheckpointKey("input", recipe) : key, 1, inputRoot, workRoot, default);
        var output = await executor.ExecuteAsync(recipe, inputRoot, [inputFile], Path.Combine(operationRoot, "plan"),
            stopAfterStepId: boundary ? "first" : null);
        Assert.IsTrue(output.Completed, output.Message);
        var completed = boundary ? await executor.ExecuteAsync(recipe, inputRoot, [inputFile],
            Path.Combine(operationRoot, "plan")) : output;
        Assert.IsTrue(completed.Completed, completed.Message);
        var source = await store.AdoptAsync(new(key, "retired-workspace", "files.process", "input"),
            new(output.OutputDirectory!, output.Files, "processedFiles", true,
                MetadataJson: JsonSerializer.Serialize(new FileArtifactProcessingMetadata(null,
                    JsonSerializer.Serialize(recipe, Json), null, CompletedThroughStepId: boundary ? "first" : null,
                    StoragePolicy: new()), Json)), "fixture");
        await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "main:process", source.OperationId);
        ArtifactRecord? completedResult = null;
        if (boundary)
        {
            var completedKey = ArtifactManifestFiles.Hash("retired-workspace-final-fixture");
            completedResult = await store.AdoptAsync(new(completedKey, "retired-workspace-final", "files.process", "input"),
                new(completed.OutputDirectory!, completed.Files, "processedFiles", true, [source.Id]), "fixture");
            await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "main:final-process", completedResult.OperationId);
        }
        var final = await Artifact("publishedFiles", completedResult ?? source, contents: "preserved content");
        await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "main:artifact.publish", final.OperationId);
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, executor, new(new NoDeletionExtraction()), root, workspaces);
        var reclaim = (await maintenance.ReclaimAsync([source.Id], default)).Single();
        Assert.IsTrue(reclaim.Completed, reclaim.Reason);
        var quarantine = new ArchiveQuarantineService(store, root, new Storage());
        if (completedResult != null)
        {
            var retired = await quarantine.DeleteManagedAncestorAfterDeliveryAsync(completedResult.Id, [final.Id],
                (_, _) => Task.FromResult(true), default);
            Assert.IsTrue(retired.Completed, retired.Message);
            // This fixture exercises the first boundary's retained record. Its later
            // result's independent cleanup was already proven above, so do not include
            // the unrelated result registration in the deletion preview under test.
            await db.ArtifactOperationBindings.Where(b => b.OperationId == completedResult.OperationId).ExecuteDeleteAsync();
        }
        var purge = await quarantine.DeleteManagedAncestorAfterDeliveryAsync(source.Id, [final.Id],
            (_, _) => Task.FromResult(true), default);
        Assert.IsTrue(purge.Completed, purge.Message);
        var task = new AcquisitionTaskDbModel {Title = "Purged processing workspace", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 1};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var ancestors = new PublishedArtifactAncestorCleanup(db, store, quarantine);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, ancestors, new Storage(), workspaces: workspaces);
        return (task, source, final, new(db, new FinishedDeletionActivity(), new(), files), operationRoot,
            Path.Combine(Path.GetDirectoryName(completed.OutputDirectory!)!, "state.json"),
            Path.Combine(root, "file-processing", "quarantine-receipts", purge.ReceiptId + ".json"));
    }

    [TestMethod]
    public async Task Purged_processing_result_with_proven_empty_owned_workspace_is_an_idempotent_cleanup_noop()
    {
        var f = await EmptyRetiredWorkspaceFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true));
        Assert.IsTrue(preview.Rows.Single().CanDelete, preview.Rows.Single().ReasonCode);
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual("AlreadyAbsent", group.ReasonCode);
        Assert.AreEqual(f.Source.RootDirectory, group.Directory);
        Assert.IsTrue(group.Eligible); Assert.IsFalse(group.SizeUnknown);
        Assert.AreEqual(0, preview.CleanupFileCount); Assert.AreEqual(0, preview.CleanupBytes);
        var sourceBefore = await store.GetArtifactAsync(f.Source.Id);
        var stateBefore = await File.ReadAllTextAsync(f.StateFile);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        CollectionAssert.AreEqual(new[] {f.Task.Id}, result.DeletedTaskIds.ToArray());
        Assert.AreEqual(0, result.RemovedFiles); Assert.AreEqual(0, result.RemovedBytes);
        Assert.AreEqual(sourceBefore!.State, (await store.GetArtifactAsync(f.Source.Id))!.State);
        Assert.AreEqual(sourceBefore.RootDirectory, (await store.GetArtifactAsync(f.Source.Id))!.RootDirectory);
        Assert.AreEqual(stateBefore, await File.ReadAllTextAsync(f.StateFile));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
        Assert.IsTrue(Directory.Exists(f.Source.RootDirectory), "Empty source directories and metadata are not recursively deleted.");
    }

    [DataTestMethod]
    [DataRow("unknown-payload")]
    [DataRow("returned-result")]
    [DataRow("missing-receipt")]
    [DataRow("wrong-receipt")]
    [DataRow("missing-state")]
    [DataRow("incomplete-state")]
    [DataRow("missing-owner")]
    [DataRow("shared-owner")]
    [DataRow("offline-storage")]
    public async Task Retired_workspace_still_requires_review_without_complete_identity_and_absence_proof(string fault)
    {
        var f = await EmptyRetiredWorkspaceFixture();
        if (fault == "unknown-payload") await File.WriteAllTextAsync(Path.Combine(f.OperationRoot, "unknown.key"), "retain");
        if (fault == "returned-result") await File.WriteAllTextAsync(f.Source.Files.Single(), "manually restored");
        if (fault == "missing-receipt") File.Delete(f.ReceiptFile);
        if (fault == "wrong-receipt")
        {
            var receipt = JsonNode.Parse(await File.ReadAllTextAsync(f.ReceiptFile))!;
            receipt["artifactId"] = "unrelated";
            await File.WriteAllTextAsync(f.ReceiptFile, receipt.ToJsonString());
        }
        if (fault == "missing-state") File.Delete(f.StateFile);
        if (fault == "incomplete-state")
        {
            var state = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
            state["completed"] = false;
            await File.WriteAllTextAsync(f.StateFile, state.ToJsonString());
        }
        if (fault == "missing-owner")
            foreach (var file in Directory.GetFiles(Path.Combine(root, "processing"), "workspace-owner.json", SearchOption.AllDirectories)) File.Delete(file);
        if (fault == "shared-owner")
            await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed", f.Source.OperationId);
        if (fault == "offline-storage") Directory.Move(Path.Combine(root, "processing"), Path.Combine(root, "offline"));
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.DeletedAt, DateTime.Now));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true, RetryRetainedFiles: true));
        Assert.IsFalse(preview.Rows.Single().CanDelete);
        Assert.AreEqual("CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW", preview.Rows.Single().Cleanup!.Groups.Single().ReasonCode);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [TestMethod]
    public async Task Retired_workspace_payload_appearing_after_preview_is_retained_and_not_reported_as_cleaned()
    {
        var f = await EmptyRetiredWorkspaceFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true));
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        var manual = Path.Combine(f.OperationRoot, "new-user-file.bin");
        await File.WriteAllTextAsync(manual, "keep");
        var result = await f.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(manual)); Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Purged_empty_result_or_step_boundary_does_not_require_a_private_cleanup_timestamp(bool boundary)
    {
        var f = await EmptyRetiredWorkspaceFixture(boundary);
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
        state["intermediatesReclaimedAt"] = null;
        await File.WriteAllTextAsync(f.StateFile, state.ToJsonString());
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.DeletedAt, DateTime.Now));

        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true, RetryRetainedFiles: true));
        Assert.IsTrue(preview.Rows.Single().CanDelete, preview.Rows.Single().ReasonCode);
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual("AlreadyAbsent", group.ReasonCode);
        Assert.AreEqual(f.Source.RootDirectory, group.Directory);
        Assert.IsFalse(group.SizeUnknown);
        Assert.AreEqual(0, group.Files.Count);
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Purged_result_or_boundary_with_retained_private_payload_is_still_visible_for_review(bool boundary)
    {
        var f = await EmptyRetiredWorkspaceFixture(boundary);
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
        state["intermediatesReclaimedAt"] = null;
        await File.WriteAllTextAsync(f.StateFile, state.ToJsonString());
        var retained = Path.Combine(f.OperationRoot, "manual-key.bin");
        await File.WriteAllTextAsync(retained, "retain this unrelated key");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, SkipFileVerification: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.AreEqual("CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW", group.ReasonCode);
        Assert.IsFalse(group.Eligible);
        Assert.IsTrue(File.Exists(retained));
        Assert.IsTrue(File.Exists(f.Final.Files.Single()));
    }
}

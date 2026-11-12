using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private sealed class FinishedDeletionActivity(IReadOnlyList<int>? owners = null) : IAcquisitionDeletionActivityReader
    {
        public Task<IReadOnlyDictionary<int, AcquisitionDeletionActivity>> ReadAsync(IReadOnlyList<AcquisitionTaskDbModel> tasks,
            CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<int, AcquisitionDeletionActivity>>(
            tasks.ToDictionary(t => t.Id, _ => new AcquisitionDeletionActivity(owners ?? [1], "finished", null)));
    }

    private sealed class NoDeletionExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct) => throw new AssertFailedException("Deletion cannot extract or download.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? onProgress,
            CancellationToken ct) => throw new AssertFailedException("Deletion cannot extract or download.");
    }

    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Source, ArtifactRecord Final, AcquisitionTaskDeletionService Service)> DeletionFixture()
    {
        var source = await Artifact("processedFiles", cleanupPolicy: false);
        var final = await Artifact("publishedFiles", source);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:artifact.publish", final.OperationId));
        var task = new AcquisitionTaskDbModel {Title = "Delivered fixture", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 1};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, null);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage());
        return (task, source, final, new(db, new FinishedDeletionActivity(), new(), files));
    }

    [TestMethod]
    public void Retained_preview_distinguishes_empty_retired_records_from_other_file_protections()
    {
        var retired = new AcquisitionTaskDeleteCleanupGroup("artifact", "intermediate", "/work/retired", false,
            "CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW", [], [], 0, 0, SizeUnknown: true);
        var snapshot = new AcquisitionTaskDeleteCleanup(0, 0, [retired], SizeIncomplete: true);
        Assert.AreEqual("CLEANUP_ONLY_RETIRED_WORKSPACE_RECORDS", AcquisitionTaskDeletionService.RetainedPreviewReason(snapshot));
        Assert.AreEqual("CLEANUP_FILES_RETAINED", AcquisitionTaskDeletionService.RetainedPreviewReason(snapshot with
            {Groups = [retired, retired with {ReasonCode = "ReferencedArtifact"}]}));
        Assert.AreEqual("CLEANUP_FILES_RETAINED", AcquisitionTaskDeletionService.RetainedPreviewReason(snapshot with {IdentifiedBytes = 1}));
        Assert.AreEqual("CLEANUP_FILES_RETAINED", AcquisitionTaskDeletionService.RetainedPreviewReason(snapshot with
            {Groups = [retired with {Files = [new("/work/retired/empty", 0)], FileCount = 1}]}));
        Assert.IsNull(AcquisitionTaskDeletionService.RetainedPreviewReason(snapshot with {Groups = [retired with {Eligible = true}]}));
    }

    [TestMethod]
    public async Task Retained_retry_rechecks_a_soft_deleted_completed_task_without_rewriting_its_deletion_or_final_files()
    {
        var fixture = await DeletionFixture();
        var id = fixture.Task.Id;
        var service = fixture.Service;
        var review = new AcquisitionTaskDeletePreviewInput([id], true, RetryRetainedFiles: true);
        Assert.AreEqual("CLEANUP_TASK_NOT_DELETED", (await service.PreviewAsync(review)).Rows.Single().ReasonCode);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed", fixture.Source.OperationId));
        var first = await service.PreviewAsync(new([id], true));
        var partial = await service.DeleteAsync(new(first.PreviewId, RemoveTaskIfCleanupFails: true));
        CollectionAssert.AreEqual(new[] { id }, partial.DeletedTaskIds.ToArray());
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        var deletedAt = await db.AcquisitionTasks.Where(t => t.Id == id).Select(t => t.DeletedAt).SingleAsync();
        Assert.IsNotNull(deletedAt);
        Assert.AreEqual("ACQUISITION_TASK_DELETED", (await service.PreviewAsync(new([id], true))).Rows.Single().ReasonCode);
        var protectedFiles = await service.PreviewAsync(review);
        Assert.IsFalse(protectedFiles.Rows.Single().CanDelete);
        Assert.IsFalse(protectedFiles.Rows.Single().CanRemoveTaskIfCleanupFails);
        Assert.AreEqual("SharedOwner", protectedFiles.Rows.Single().Cleanup!.Groups.Single().ReasonCode);

        await db.ArtifactOperationBindings.Where(b => b.OwnerKind == "workflow" && b.OwnerId == "2" &&
            b.Slot == "borrowed" && b.OperationId == fixture.Source.OperationId).ExecuteDeleteAsync();
        var ready = await service.PreviewAsync(review);
        Assert.IsTrue(ready.RetryRetainedFiles);
        Assert.IsTrue(ready.Rows.Single().CanDelete);
        Assert.AreEqual(1, ready.CleanupFileCount);
        var submitted = new AcquisitionTaskDeleteInput(ready.PreviewId, RetryRetainedFiles: true);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.DeleteAsync(new(ready.PreviewId)));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.DeleteAsync(submitted with {RemoveTaskIfCleanupFails = true}));
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        var result = await service.DeleteAsync(submitted);
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsFalse(File.Exists(fixture.Source.Files.Single()));
        Assert.IsTrue(File.Exists(fixture.Final.Files.Single()));
        Assert.AreEqual(deletedAt, await db.AcquisitionTasks.Where(t => t.Id == id).Select(t => t.DeletedAt).SingleAsync());
        Assert.AreEqual(result, await service.DeleteAsync(submitted));
        var repeated = await service.PreviewAsync(review);
        Assert.AreEqual(0, (await service.DeleteAsync(new(repeated.PreviewId, RetryRetainedFiles: true))).RemovedFiles);
    }

    [TestMethod]
    public async Task Retained_retry_reauthorizes_paths_and_rejects_changed_deletion_evidence()
    {
        var fixture = await DeletionFixture();
        var task = db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id);
        var at = DateTime.UtcNow;
        await task.ExecuteUpdateAsync(u => u.SetProperty(t => t.DeletedAt, (DateTime?)at));
        var review = new AcquisitionTaskDeletePreviewInput([fixture.Task.Id], true, RetryRetainedFiles: true);
        var deniedPreview = await fixture.Service.PreviewAsync(review, guardDirectory: _ => throw new UnauthorizedAccessException());
        Assert.AreEqual("CLEANUP_PATH_NOT_AUTHORIZED", deniedPreview.Rows.Single().ReasonCode);
        var valid = await fixture.Service.PreviewAsync(review);
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => fixture.Service.DeleteAsync(
            new(valid.PreviewId, RetryRetainedFiles: true), guardDirectory: _ => throw new UnauthorizedAccessException()));
        var stale = await fixture.Service.PreviewAsync(review);
        await task.ExecuteUpdateAsync(u => u.SetProperty(t => t.DeletedAt, (DateTime?)at.AddMinutes(-1)));
        var rejected = await fixture.Service.DeleteAsync(new(stale.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual("ACQUISITION_TASK_CHANGED", rejected.Skipped.Single().ReasonCode);
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        await task.ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, AcquisitionStatus.Waiting));
        Assert.AreEqual("CLEANUP_TASK_NOT_COMPLETED", (await fixture.Service.PreviewAsync(review)).Rows.Single().ReasonCode);
    }

    [TestMethod]
    public async Task Deletion_default_is_task_only_and_does_not_inspect_missing_delivery()
    {
        var fixture = await DeletionFixture();
        File.Delete(fixture.Final.Files.Single());
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id]));
        Assert.IsFalse(preview.CleanupFiles); Assert.IsNull(preview.Rows.Single().Cleanup);
        Assert.AreEqual(1, (await fixture.Service.DeleteAsync(new(preview.PreviewId))).DeletedTaskIds.Count);
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        Assert.AreEqual(ArtifactLocationState.Available, (await store.GetArtifactAsync(fixture.Final.Id))!.State);
    }

    [TestMethod]
    public async Task Cleanup_preview_checks_remote_scope_before_inspecting_final_files()
    {
        var fixture = await DeletionFixture(); File.Delete(fixture.Final.Files.Single());
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true), guardDirectory: _ =>
            throw new UnauthorizedAccessException());
        Assert.AreEqual("CLEANUP_PATH_NOT_AUTHORIZED", preview.Rows.Single().ReasonCode);
        Assert.AreEqual(ArtifactLocationState.Available, (await store.GetArtifactAsync(fixture.Final.Id))!.State,
            "Denied media scope must not run ResolveById and update its file-observation state.");
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Accepted_moved_delivery_cleans_exact_inputs_overrides_old_keep_and_retains_unknown_files_and_history()
    {
        var fixture = await DeletionFixture();
        var kept = Path.Combine(root, "user-kept.bin"); File.Move(fixture.Final.Files.Single(), kept);
        var unknown = Path.Combine(fixture.Source.RootDirectory, "unknown.key"); await File.WriteAllTextAsync(unknown, "keep");
        var unaccepted = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, false));
        Assert.IsFalse(unaccepted.Rows.Single().CanDelete);
        Assert.AreEqual("CLEANUP_DELIVERY_ACCEPTANCE_REQUIRED", unaccepted.Rows.Single().ReasonCode);
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        Assert.AreEqual(1, preview.CleanupFileCount);
        Assert.IsFalse(preview.Rows.Single().Cleanup!.Groups.SelectMany(g => g.Files).Any(f => f.Path == kept || f.Path == unknown));
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.DeletedTaskIds.Count);
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.IsFalse(File.Exists(fixture.Source.Files.Single()));
        Assert.IsTrue(File.Exists(unknown)); Assert.IsTrue(File.Exists(kept));
        Assert.IsNotNull(await db.WorkflowRuns.SingleOrDefaultAsync(r => r.Id == 1));
        Assert.IsNotNull(await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).Select(t => t.DeletedAt).SingleAsync());
        var journal = Directory.GetFiles(Path.Combine(root, "file-processing", "quarantine-receipts"), "*.json").Single();
        var receipt = JsonSerializer.Deserialize<ArchiveQuarantineService.Receipt>(await File.ReadAllTextAsync(journal), Json)!;
        Assert.AreEqual("Purged", receipt.Status);
        Assert.IsTrue(receipt.Consents.Single().DeliveryAccepted);
        Assert.AreEqual(fixture.Final.Fingerprint, receipt.Consents.Single().Publications.Single().Fingerprint);
        Assert.AreEqual(result, await fixture.Service.DeleteAsync(new(preview.PreviewId)));
    }

    [TestMethod]
    public async Task Explicit_skip_verification_keeps_lineage_guards_but_skips_the_second_file_pass()
    {
        var fixture = await DeletionFixture();
        // The user has already checked and moved the final delivery. The registered
        // publication remains the immutable proof used for ownership and path guards.
        File.Delete(fixture.Final.Files.Single());
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true, true));
        Assert.IsTrue(preview.SkipFileVerification);
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.DeletedTaskIds.Count);
        Assert.IsFalse(File.Exists(fixture.Source.Files.Single()));
        Assert.IsNotNull(await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).Select(t => t.DeletedAt).SingleAsync());
    }

    [TestMethod]
    public async Task Accepted_delivery_does_not_override_another_task_owner_and_keeps_task_visible()
    {
        var fixture = await DeletionFixture();
        File.Delete(fixture.Final.Files.Single());
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed", fixture.Source.OperationId));
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single();
        Assert.IsFalse(group.Eligible); Assert.IsTrue(group.Shared); Assert.AreEqual("SharedOwner", group.ReasonCode);
        Assert.AreEqual(fixture.Source.Manifest.Entries.Sum(e => e.Length), preview.IdentifiedBytes);
        Assert.AreEqual(preview.IdentifiedBytes, preview.Rows.Single().Cleanup!.IdentifiedBytes);
        Assert.AreEqual(1, preview.IdentifiedFileCount); Assert.AreEqual(0L, preview.CleanupBytes);
        Assert.AreEqual(0L, preview.Rows.Single().Cleanup!.Bytes);
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        Assert.IsNull(await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).Select(t => t.DeletedAt).SingleAsync());
    }

    [TestMethod]
    public async Task Task_preview_with_execution_cleanup_options_reclaims_once_and_rejects_a_changed_replay()
    {
        var fixture = await DeletionFixture();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id]));
        File.Delete(fixture.Final.Files.Single()); // already accepted and moved
        var input = new AcquisitionTaskDeleteInput(preview.PreviewId, true, true, true, true);
        var result = await fixture.Service.DeleteAsync(input);
        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        CollectionAssert.AreEqual(new[] { fixture.Task.Id }, result.DeletedTaskIds.ToArray());
        Assert.AreEqual(1, result.RemovedFiles);
        Assert.AreEqual(result, await fixture.Service.DeleteAsync(input));
        var error = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            fixture.Service.DeleteAsync(input with { SkipFileVerification = false }));
        Assert.AreEqual("ACQUISITION_DELETE_OPTIONS_CHANGED", error.Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Opted_in_record_removal_preserves_a_shared_source_reports_its_directory_and_history(bool inventory)
    {
        var fixture = await DeletionFixture();
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "other", fixture.Source.OperationId));
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], inventory, inventory, inventory));
        var input = new AcquisitionTaskDeleteInput(preview.PreviewId, true, true, true, true);
        var result = await fixture.Service.DeleteAsync(input);
        Assert.AreEqual(fixture.Task.Id, result.DeletedTaskIds.Single());
        Assert.AreEqual(0, result.Skipped.Count);
        var outcome = result.CleanupResults!.Single();
        Assert.IsFalse(outcome.Completed);
        CollectionAssert.Contains(outcome.ReasonCodes.ToArray(), "SharedOwner");
        CollectionAssert.Contains(outcome.RetainedDirectories!.ToArray(), fixture.Source.RootDirectory);
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        Assert.IsTrue(File.Exists(fixture.Final.Files.Single()));
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
        Assert.IsNotNull(await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).Select(t => t.DeletedAt).SingleAsync());
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => fixture.Service.DeleteAsync(input,
            guardDirectory: _ => throw new UnauthorizedAccessException()));
    }

    [TestMethod]
    public async Task Detailed_preview_preserves_record_eligibility_when_delivery_verification_fails()
    {
        var fixture = await DeletionFixture();
        File.Delete(fixture.Final.Files.Single());
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true));
        Assert.IsFalse(preview.Rows.Single().CanDelete);
        Assert.IsTrue(preview.Rows.Single().CanRemoveTaskIfCleanupFails);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => fixture.Service.DeleteAsync(
            new(preview.PreviewId, true, true, true, true)));
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId, RemoveTaskIfCleanupFails: true));
        Assert.AreEqual(fixture.Task.Id, result.DeletedTaskIds.Single());
        Assert.AreEqual("CLEANUP_DELIVERY_ACCEPTANCE_REQUIRED", result.CleanupResults!.Single().ReasonCodes.Single());
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Deferred_cleanup_fails_closed_for_a_denied_path_even_if_record_removal_is_requested()
    {
        var fixture = await DeletionFixture();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id]));
        var input = new AcquisitionTaskDeleteInput(preview.PreviewId, true, true, true, true);
        await Assert.ThrowsExceptionAsync<Bakabase.Abstractions.Exceptions.UserStoragePathException>(() =>
            fixture.Service.DeleteAsync(input, guardDirectory: _ => throw new Bakabase.Abstractions.Exceptions.UserStoragePathException("denied")));
        Assert.IsNull(await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).Select(t => t.DeletedAt).SingleAsync());
        Assert.IsTrue(File.Exists(fixture.Final.Files.Single()));
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Deferred_cleanup_never_hides_a_changed_task_with_the_fallback_enabled()
    {
        var fixture = await DeletionFixture();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id]));
        await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.PlanRevision, t => t.PlanRevision + 1));
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId, true, true, true, true));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.AreEqual("ACQUISITION_TASK_CHANGED", result.Skipped.Single().ReasonCode);
        Assert.IsTrue(File.Exists(fixture.Final.Files.Single()));
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Changed_source_after_cleanup_preview_is_not_deleted_and_task_remains()
    {
        var fixture = await DeletionFixture();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        await File.WriteAllTextAsync(fixture.Source.Files.Single(), "changed after confirmation preview");
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        Assert.IsTrue(File.Exists(fixture.Final.Files.Single()));
        Assert.IsFalse(result.CleanupResults!.Single().Completed);
    }

    [TestMethod]
    public async Task Acceptance_never_authorizes_a_registered_key_file_without_preservation_or_replacement_proof()
    {
        var directory = Path.Combine(root, "registered-input"); Directory.CreateDirectory(directory);
        var content = Path.Combine(directory, "content.bin"); var key = Path.Combine(directory, "activation.key");
        await File.WriteAllTextAsync(content, "same bytes"); await File.WriteAllTextAsync(key, "must retain");
        var source = await store.AdoptAsync(new("registered-key", "registered-key", "files.process", "fixture"),
            new(directory, [content, key], "processedFiles", true), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "process:artifact.process", source.OperationId));
        var final = await Artifact("publishedFiles", source);
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "publish:artifact.publish", final.OperationId));
        var task = new AcquisitionTaskDbModel {Title = "Unproven key", Status = AcquisitionStatus.Completed, WorkflowRunId = 1, PlanRevision = 1};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root);
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity(), new(),
            new(db, store, maintenance, cleanup, new Storage()));
        var preview = await service.PreviewAsync(new([task.Id], true, true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Any(g => g.ReasonCode == ArtifactFileCoverage.IncompleteCode));
        var result = await service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count); Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(key)); Assert.IsTrue(File.Exists(content)); Assert.IsTrue(File.Exists(final.Files.Single()));
    }

    [TestMethod]
    public void Cleanup_summary_counts_a_shared_path_once_and_does_not_claim_physical_bytes()
    {
        var files = AcquisitionTaskDeletionCleanupService.Unique([
            new(Path.Combine(root, "nested", "..", "same.bin"), 12), new(Path.Combine(root, "same.bin"), 12)]);
        Assert.AreEqual(1, files.Count); Assert.AreEqual(12L, files.Single().Bytes);
    }

    [TestMethod]
    public void Cleanup_summary_deduplicates_validated_relocation_aliases_without_traversing_files()
    {
        var old = Path.Combine(root, "old", "file.bin"); var current = Path.Combine(root, "current", "file.bin");
        var files = AcquisitionTaskDeletionCleanupService.Unique([new(old, 12), new(current, 12)], p => p == old ? current : p);
        Assert.AreEqual(1, files.Count); Assert.AreEqual(current, files.Single().Path);
    }

    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Source, AcquisitionTaskDeletionService Service)> ObsoleteDeletionFixture()
    {
        var fixture = await DeletionFixture();
        var old = new AcquisitionPlan {Title = "old"}; var current = new AcquisitionPlan {Title = "current"};
        var history = new List<AcquisitionPlanRevision> {new(1, old, [], DateTime.UtcNow.AddDays(-1)),
            new(2, current, [], DateTime.UtcNow, "continueFiles", 3)};
        await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).ExecuteUpdateAsync(update => update
            .SetProperty(t => t.PlanRevision, 2).SetProperty(t => t.PlanJson, JsonSerializer.Serialize(current, Json))
            .SetProperty(t => t.PlanHistoryJson, JsonSerializer.Serialize(history, Json)));
        var task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == fixture.Task.Id);
        db.Set<WorkflowRunSourceDbModel>().AddRange(
            new() {WorkflowRunId = 1, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"},
            new() {WorkflowRunId = 3, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "1"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "3", "part:artifact.download", fixture.Source.OperationId));
        Assert.IsTrue(await store.AttachConsumerAsync(fixture.Source.OperationId, "old-run", TimeSpan.FromHours(1)));
        Assert.IsTrue(await store.PinConsumerAsync(fixture.Source.OperationId, "old-run", "workflow", "3"));
        var activity = new FinishedDeletionActivity([1, 3]);
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root);
        var files = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage(), new(db, activity, root));
        return (task, fixture.Source, new(db, activity, new(), files));
    }

    [TestMethod]
    public async Task Cleanup_can_close_exclusively_owned_superseded_wait_then_reclaim_without_erasing_its_history()
    {
        var fixture = await ObsoleteDeletionFixture();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        Assert.IsTrue(preview.Rows.Single().CanDelete);
        Assert.AreEqual(3, preview.Rows.Single().Cleanup!.SupersededRuns!.Single().RunId);
        Assert.AreEqual(WorkflowRunStatus.Waiting, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(1, result.DeletedTaskIds.Count);
        Assert.AreEqual(3, result.CleanupResults!.Single().ClosedRunIds!.Single());
        Assert.AreEqual(WorkflowRunStatus.Cancelled, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
        Assert.IsTrue(await db.ArtifactConsumers.AnyAsync(c => c.OwnerId == "3"));
        Assert.IsFalse(File.Exists(fixture.Source.Files.Single()));
        var receipts = Directory.GetFiles(Path.Combine(root, "file-processing", "superseded-run-cleanup-receipts"), "*.json");
        StringAssert.Contains(await File.ReadAllTextAsync(receipts.Single()), AcquisitionSupersededRunCleanup.Reason);
    }

    [TestMethod]
    public async Task Cleanup_does_not_close_old_wait_when_answer_arrived_after_preview()
    {
        var fixture = await ObsoleteDeletionFixture();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        await db.WorkflowRuns.Where(r => r.Id == 3).ExecuteUpdateAsync(u => u.SetProperty(r => r.PendingSignalJson, "{}"));
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.AreEqual(WorkflowRunStatus.Waiting, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
    }

    [TestMethod]
    public async Task Apply_rechecks_remote_scope_before_cancelling_any_old_run_or_cleaning_files()
    {
        var fixture = await ObsoleteDeletionFixture();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => fixture.Service.DeleteAsync(new(preview.PreviewId),
            guardDirectory: _ => throw new UnauthorizedAccessException()));
        Assert.AreEqual(WorkflowRunStatus.Waiting, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
        Assert.IsNull(await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).Select(t => t.DeletedAt).SingleAsync());
    }

    [TestMethod]
    public async Task Cleanup_does_not_close_a_waiting_run_referenced_by_another_task_history()
    {
        var fixture = await ObsoleteDeletionFixture();
        db.AcquisitionTasks.Add(new() {Title = "Another owner", PlanHistoryJson = fixture.Task.PlanHistoryJson});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        Assert.IsFalse(preview.Rows.Single().CanDelete);
        Assert.AreEqual("CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW", preview.Rows.Single().ReasonCode);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Producer_provenance_is_not_a_second_owner_only_when_the_task_records_it(bool recorded)
    {
        var fixture = await ObsoleteDeletionFixture();
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 3, Module = "postParser", EntityKind = "task", EntityId = "900", Revision = "4"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (recorded)
        {
            var references = JsonSerializer.Serialize(new[] {new WorkflowSourceRef("postParser", "task", "900", "4")}, Json);
            await db.AcquisitionTasks.Where(t => t.Id == fixture.Task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.SourceRefsJson,
                references));
        }
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        Assert.AreEqual(recorded, preview.Rows.Single().CanDelete);
        if (!recorded) Assert.AreEqual("CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW", preview.Rows.Single().ReasonCode);
        Assert.AreEqual(WorkflowRunStatus.Waiting, await db.WorkflowRuns.Where(r => r.Id == 3).Select(r => r.Status).SingleAsync());
    }

    [TestMethod]
    public async Task Retired_processed_result_does_not_hide_unverified_private_workspace_by_claiming_success()
    {
        var fixture = await DeletionFixture();
        Assert.IsTrue(await store.TryQuarantineAsync(fixture.Source.Id));
        await store.SetLocationAsync(fixture.Source.Id, fixture.Source.RootDirectory, ArtifactLocationState.Retired);
        var preview = await fixture.Service.PreviewAsync(new([fixture.Task.Id], true, true));
        Assert.AreEqual("CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW", preview.Rows.Single().Cleanup!.Groups.Single().ReasonCode);
        var result = await fixture.Service.DeleteAsync(new(preview.PreviewId));
        Assert.AreEqual(0, result.DeletedTaskIds.Count);
        Assert.IsTrue(File.Exists(fixture.Source.Files.Single()));
    }
}

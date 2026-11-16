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
    private sealed record CapturedAdditionFixture(AcquisitionTaskDbModel Task, ArtifactRecord Source,
        ArtifactRecord Capture, ArtifactRecord Publication, string AddedFile,
        AcquisitionTaskDeletionCleanupService Cleanup, ArtifactDeliveryCleanupAuthorization Authorization);

    private async Task<CapturedAdditionFixture> CapturedAddition(bool owned = true, bool published = true,
        bool skip = true, bool accepted = true, bool checkpoint = false)
    {
        var task = new AcquisitionTaskDbModel {Title = "Manually extracted private files", PlanRevision = 2,
            WorkflowRunId = 1, Status = AcquisitionStatus.Completed};
        db.AcquisitionTasks.Add(task); await db.SaveChangesAsync();
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 1, Module = "acquisition",
            EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var operationKey = ArtifactManifestFiles.Hash("captured-addition-source");
        var working = Path.Combine(root, "working"); Directory.CreateDirectory(working);
        var recipe = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "first", Op = "renameFile", TargetName = "original.bin"}]};
        var key = checkpoint ? FileArtifactProcessor.CheckpointKey("original-input", recipe) : operationKey;
        var attempt = owned ? await resolver.ResolveAsync(checkpoint ? "checkpoints" : "operations", key, 1, working, working, default)
            : working;
        var directory = Path.Combine(attempt, "result"); Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "original.bin"); await File.WriteAllTextAsync(original, "original");
        var source = await store.AdoptAsync(new(operationKey, "captured-source", "files.process", "original-input"),
            new(directory, [original], "processedFiles", true, MetadataJson: checkpoint
                ? JsonSerializer.Serialize(new FileArtifactProcessingMetadata(null, JsonSerializer.Serialize(recipe, Json), null,
                    CompletedThroughStepId: "first", StoragePolicy: new() {ProcessingWorkspaceDirectory = working}), Json) : null), "fixture");
        await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "part:process:source", source.OperationId);
        var added = Path.Combine(directory, "manual-output.bin"); await File.WriteAllTextAsync(added, "new manual output");
        var capture = await AcquisitionCurrentDirectoryFiles.CaptureAsync(db, store, new Storage(), resolver,
            task.Id, 2, 1, "part", source, null, default);
        var delivery = Path.Combine(root, "delivery"); Directory.CreateDirectory(delivery);
        foreach (var file in published ? capture.Files : source.Files) File.Copy(file, Path.Combine(delivery, Path.GetFileName(file)));
        var final = await store.AdoptAsync(new(ArtifactManifestFiles.Hash("captured-addition-publish"), "captured-publish",
                "files.publish", capture.Fingerprint),
            new(delivery, Directory.GetFiles(delivery), "publishedFiles", false, [capture.Id]), "fixture");
        await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "part:artifact.publish", final.OperationId);
        var consent = new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            task.Id, 2, 1, accepted, [1], [new(final.Id, final.OperationId, final.Fingerprint, final.RootDirectory)], skip, true);
        var authorization = new ArtifactDeliveryCleanupAuthorization(consent, _ => Task.FromResult(true), store,
            releasableViews: ct => AcquisitionTaskCleanupViews.ReadAsync(db, store, consent, ct));
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, resolver);
        var service = new AcquisitionTaskDeletionCleanupService(db, store, maintenance, cleanup, new Storage(), workspaces: resolver);
        return new(task, source, capture, final, added, service, authorization);
    }

    private Task<IReadOnlyList<AcquisitionTaskDeletionCleanupService.CapturedInputCandidate>> CapturedPreview(CapturedAdditionFixture f) =>
        f.Cleanup.PreviewCapturedInputsAsync(f.Authorization, new ArtifactCleanupReferenceGuard(db, new(root, new Storage())), default);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Published_capture_additions_in_owned_workspace_use_exact_cleanup_and_keep_history(bool skip)
    {
        var f = await CapturedAddition(skip: skip);
        var unknown = Path.Combine(f.Source.RootDirectory, "not-captured.key"); await File.WriteAllTextAsync(unknown, "preserve");
        var selected = (await CapturedPreview(f)).Single();
        Assert.IsTrue(selected.Group.Eligible, selected.Group.ReasonCode);
        CollectionAssert.AreEqual(new[] {f.AddedFile}, selected.Files.Select(x => x.Path).ToArray());
        var result = await f.Cleanup.DeleteCapturedInputAsync(selected, f.Authorization, default);
        Assert.IsTrue(result.Completed, result.Reason); Assert.AreEqual(1, result.RemovedFiles);
        Assert.IsFalse(File.Exists(f.AddedFile)); Assert.IsTrue(File.Exists(f.Source.Files.Single()));
        Assert.IsTrue(File.Exists(unknown)); Assert.IsTrue(f.Publication.Files.All(File.Exists));
        var capture = await store.GetArtifactAsync(f.Capture.Id);
        Assert.AreEqual(f.Capture.MetadataJson, capture!.MetadataJson); Assert.IsFalse(capture.Managed);
        Assert.AreEqual(f.Capture.Fingerprint, capture.Fingerprint);
        var retry = await f.Cleanup.DeleteCapturedInputAsync(selected, f.Authorization, default);
        Assert.IsTrue(retry.Completed, retry.Reason); Assert.AreEqual(0, retry.RemovedFiles);
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(root, "file-processing", "workspace-cleanup-receipts"), "*.json").Length);
    }

    [TestMethod]
    [DataRow("external-root")]
    [DataRow("unaccepted")]
    [DataRow("unpublished-addition")]
    public async Task Capture_alone_never_authorizes_external_or_unpublished_manual_files(string scenario)
    {
        var f = await CapturedAddition(owned: scenario != "external-root", published: scenario != "unpublished-addition",
            accepted: scenario != "unaccepted");
        var preview = await CapturedPreview(f);
        Assert.IsFalse(preview.Any(c => c.Group.Eligible), scenario);
        Assert.IsTrue(File.Exists(f.AddedFile));
    }

    [TestMethod]
    [DataRow("capture-owner")]
    [DataRow("source-owner")]
    [DataRow("capture-consumer")]
    [DataRow("source-consumer")]
    [DataRow("external-reference")]
    public async Task Published_capture_additions_recheck_live_ownership_and_references_after_preview(string scenario)
    {
        var f = await CapturedAddition(); var selected = (await CapturedPreview(f)).Single();
        Assert.IsTrue(selected.Group.Eligible, selected.Group.ReasonCode);
        if (scenario.EndsWith("owner"))
            await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "borrowed",
                scenario.StartsWith("capture") ? f.Capture.OperationId : f.Source.OperationId);
        if (scenario.EndsWith("consumer"))
            await store.AttachConsumerAsync(scenario.StartsWith("capture") ? f.Capture.OperationId : f.Source.OperationId,
                "active-reader", TimeSpan.FromHours(1));
        if (scenario == "external-reference")
        {
            db.AcquisitionTasks.Add(new() {Title = "Outside reader", Status = AcquisitionStatus.Completed, WorkflowRunId = 2,
                PlanJson = JsonSerializer.Serialize(new {file = f.AddedFile})});
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        var result = await f.Cleanup.DeleteCapturedInputAsync(selected, f.Authorization, default);
        Assert.IsFalse(result.Completed, scenario); Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.AddedFile)); Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [TestMethod]
    [DataRow("changed")]
    [DataRow("symlink")]
    [DataRow("removed-root")]
    public async Task Captured_cleanup_rejects_replaced_files_links_and_missing_storage(string scenario)
    {
        var f = await CapturedAddition(); var selected = (await CapturedPreview(f)).Single();
        Assert.IsTrue(selected.Group.Eligible, selected.Group.ReasonCode);
        if (scenario == "changed") await File.WriteAllTextAsync(f.AddedFile, "different output bytes");
        if (scenario == "symlink")
        {
            File.Delete(f.AddedFile);
            File.CreateSymbolicLink(f.AddedFile, f.Publication.Files.First());
        }
        var moved = f.Source.RootDirectory + "-moved";
        if (scenario == "removed-root") Directory.Move(f.Source.RootDirectory, moved);
        var result = await f.Cleanup.DeleteCapturedInputAsync(selected, f.Authorization, default);
        Assert.IsFalse(result.Completed, scenario); Assert.AreEqual(0, result.RemovedFiles);
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        Assert.IsTrue((await CapturedPreview(f)).Any(c => !c.Group.Eligible), "Known owned residuals must remain visible after a failed stat.");
    }

    [TestMethod]
    public async Task Published_capture_additions_resolve_the_recorded_completed_checkpoint_family()
    {
        var f = await CapturedAddition(checkpoint: true);
        var candidate = (await CapturedPreview(f)).Single();
        Assert.IsTrue(candidate.Group.Eligible, candidate.Group.ReasonCode);
        var result = await f.Cleanup.DeleteCapturedInputAsync(candidate, f.Authorization, default);
        Assert.IsTrue(result.Completed, result.Reason); Assert.AreEqual(1, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.Source.Files.Single())); Assert.IsTrue(f.Publication.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Published_capture_additions_use_the_purged_parent_receipt_without_reviving_it()
    {
        var f = await CapturedAddition(checkpoint: true);
        var quarantine = new ArchiveQuarantineService(store, root, new Storage());
        var purge = await quarantine.DeleteManagedAncestorAfterDeliveryAsync(f.Source.Id, [f.Publication.Id],
            (_, _) => Task.FromResult(true), default, f.Authorization);
        Assert.IsTrue(purge.Completed, purge.Message);
        var retired = (await store.GetArtifactAsync(f.Source.Id))!;
        Assert.AreEqual(ArtifactLocationState.Retired, retired.State);
        Assert.IsFalse(File.Exists(f.Source.Files.Single())); Assert.IsTrue(File.Exists(f.AddedFile));
        var candidate = (await CapturedPreview(f)).Single();
        Assert.IsTrue(candidate.Group.Eligible, candidate.Group.ReasonCode);
        var result = await f.Cleanup.DeleteCapturedInputAsync(candidate, f.Authorization, default);
        Assert.IsTrue(result.Completed, result.Reason); Assert.AreEqual(1, result.RemovedFiles);
        var after = (await store.GetArtifactAsync(f.Source.Id))!;
        Assert.AreEqual(retired.State, after.State); Assert.AreEqual(retired.RootDirectory, after.RootDirectory);
        Assert.AreEqual(retired.Fingerprint, after.Fingerprint);
        var again = (await CapturedPreview(f)).Single();
        Assert.IsTrue(again.Group.Eligible, again.Group.ReasonCode); Assert.AreEqual(0, again.Files.Count);
        Assert.IsTrue((await f.Cleanup.DeleteCapturedInputAsync(again, f.Authorization, default)).Completed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Captured_cleanup_skips_only_content_hashes_when_explicitly_requested(bool skip)
    {
        var f = await CapturedAddition(skip: skip);
        await File.WriteAllTextAsync(f.AddedFile, new string('X', (int) new FileInfo(f.AddedFile).Length));
        var candidate = (await CapturedPreview(f)).Single();
        Assert.IsTrue(candidate.Group.Eligible, candidate.Group.ReasonCode);
        var result = await f.Cleanup.DeleteCapturedInputAsync(candidate, f.Authorization, default);
        Assert.AreEqual(skip, result.Completed, result.Reason);
        Assert.AreEqual(!skip, File.Exists(f.AddedFile));
    }

    [TestMethod]
    public async Task Cross_task_overlap_removes_captured_and_abandoned_execution_candidates()
    {
        var f = await CapturedAddition(); var captured = (await CapturedPreview(f)).Single();
        var group = captured.Group with {ArtifactId = "abandoned-input:fixture"};
        var abandoned = new AcquisitionTaskDeletionCleanupService.AbandonedInputCandidate(group.ArtifactId,
            f.Source.OperationId, 1, f.Source.Id, f.Source.RootDirectory, "state", "claim", captured.Files,
            captured.FileIdentities, group);
        var plans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>
        {
            [1] = new(new(captured.Files.Count, captured.Files.Sum(x => x.Bytes), [captured.Group]), "first", null, [], CapturedInputs: [captured]),
            [2] = new(new(abandoned.Files.Count, abandoned.Files.Sum(x => x.Bytes), [group]), "second", null, [], AbandonedInputs: [abandoned])
        };
        AcquisitionTaskDeletionService.ProtectOverlappingSelections(plans);
        Assert.AreEqual(0, plans[1].CapturedInputs!.Count); Assert.AreEqual(0, plans[2].AbandonedInputs!.Count);
        Assert.IsTrue(plans.Values.All(p => p.Summary.Groups.All(g => !g.Eligible && g.Shared)));
        Assert.IsTrue(File.Exists(f.AddedFile));
    }
}

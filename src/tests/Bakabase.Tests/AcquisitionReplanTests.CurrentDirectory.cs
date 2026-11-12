using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, WorkflowRunDbModel Run, ArtifactRecord Artifact)> CurrentDirectoryInput()
    {
        var (task, run, input) = await InitialUnknownInput();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "owned-download")).FullName;
        var file = Path.Combine(directory, "original.bin"); File.Move(input.Files.Single(), file);
        (await Db.Artifacts.SingleAsync(a => a.Id == input.Id)).RootPath = directory;
        var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)!;
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets.Single() with
            {DownloadDirectory = directory, Files = [file]}]});
        await Db.SaveChangesAsync();
        return (task, run, (await Artifacts.GetArtifactAsync(input.Id))!);
    }

    private static AcquisitionPlanReplanInput FreshRequest(AcquisitionPlanTaskDetail task, int runId, string artifactId) =>
        new(task.Task.PlanRevision, runId, "Read the current files as a new local input.",
            [new("main", new() {Requirement = "unknown"}, [artifactId], artifactId,
                UseAdaptiveAssessment: true, InputMode: "currentDirectory")], EnableAdaptiveAssessment: true);

    [TestMethod]
    public async Task Current_directory_captures_renamed_added_and_removed_files_without_rewriting_history_or_downloading()
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var originalMetadata = (await Db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == original.Id)).ManifestJson;
        var runHistory = (run.PlanSnapshotJson, run.CurrentItemJson, run.WaitPromptJson);
        File.Delete(original.Files.Single());
        var image = Path.Combine(original.RootDirectory, "manually-extracted.jpg");
        var key = Path.Combine(original.RootDirectory, "license.key");
        await File.WriteAllTextAsync(image, "current image"); await File.WriteAllTextAsync(key, "retain this key");
        // GET eligibility must not verify the obsolete byte manifest.
        Assert.IsTrue((await Plans.GetAsync(task.Id, default))!.ReplanArtifacts.Single().CanReadCurrentDirectory);
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default);
        var input = revised.Plan!.Parts.Single().ContinueFrom!;
        var captured = (await Artifacts.GetArtifactAsync(input.ArtifactId))!;
        Assert.AreNotEqual(original.Id, captured.Id); Assert.AreEqual("currentFiles", captured.Kind);
        Assert.IsFalse(captured.Managed, "User additions never gain an automatic source-deletion permission.");
        Assert.AreEqual(original.RootDirectory, captured.RootDirectory);
        CollectionAssert.AreEquivalent(new[] {image, key}, captured.Files.ToArray());
        Assert.AreEqual(originalMetadata, (await Db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == original.Id)).ManifestJson);
        var savedRun = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(runHistory, (savedRun.PlanSnapshotJson, savedRun.CurrentItemJson, savedRun.WaitPromptJson));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.IsTrue(AcquisitionPlanCompiler.Compile(revised.Plan).Nodes.All(n => !n.Kind.StartsWith("download.") && n.Kind != "baidu.transfer"));
        Assert.AreEqual(captured.Id, (await AcquisitionPlanArtifactInputGuard.RequireAsync(Db, Artifacts, task.Id, "main", input, default)).Id);
        Assert.IsFalse((await new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), new CompositeStorage())
            .QuarantineAsync(captured.Id, [])).Completed);
        Assert.IsTrue(File.Exists(key));
    }

    [TestMethod]
    public async Task Current_directory_AI_continuation_attaches_fresh_input_in_place_without_processing_copy()
    {
        var (task, old, original) = await CurrentDirectoryInput();
        await File.WriteAllTextAsync(original.Files.Single(), "manually updated content");
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, old.Id, original.Id), default);
        var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int n) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = compiled.Nodes[n].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var attached = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), workset, default);
        var processed = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), attached.Replacement!, default);
        Assert.IsNull(processed.Suspension, processed.Suspension?.PromptJson);
        var target = ((DownloadWorkset) processed.Replacement!).Targets.Single();
        Assert.AreEqual(captured.Id, target.ProcessedArtifactId);
        Assert.AreEqual(original.RootDirectory, target.ProcessedDirectory);
        Assert.IsFalse(Directory.Exists(Path.Combine(original.RootDirectory, ".bakabase-work")));
        var assessment = JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(compiled.Nodes[2].ConfigJson, WorkflowJson.Options)!;
        Assert.IsTrue(assessment.EnableAdaptiveAssessment);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Current_directory_batch_AI_preview_and_replan_accept_legacy_unknown_without_AI_node_or_prompt_artifact()
    {
        var (task, old, original) = await CurrentDirectoryInput();
        var frozen = WorkflowRunPlans.Read(old.PlanSnapshotJson)!;
        old.PlanSnapshotJson = WorkflowRunPlans.Serialize(frozen with
            {Nodes = frozen.Nodes.Where(n => n.Kind != AssessFileArtifactDeliveryActivity.ActivityKind).ToArray()});
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        var oldPlan = JsonSerializer.Deserialize<Bakabase.Modules.Acquisition.Abstractions.Models.Domain.AcquisitionPlan>(row.PlanJson!, WorkflowJson.Options)!;
        row.PlanJson = JsonSerializer.Serialize(oldPlan with {AdaptiveFileProcessing = false, StageDirectories = null}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        var preserved = old.PlanSnapshotJson;
        Assert.IsFalse(old.WaitPromptJson!.Contains("artifactId"));
        var service = new AcquisitionAdaptiveMigrationService(Db, null!, Plans);
        var preview = await service.PreviewAsync(new([task.Id]));
        Assert.IsTrue(preview.Rows.Single().CanMigrate, preview.Rows.Single().ReasonCode);
        var detail = (await Plans.GetAsync(task.Id, default))!;
        var request = AcquisitionAdaptiveMigrationService.CurrentDirectoryInput(detail, 3)!;
        Assert.AreEqual("currentDirectory", request.Items.Single().InputMode);
        Assert.AreEqual(original.Id, request.Items.Single().ContinueFromArtifactId);
        Assert.IsTrue(request.Items.Single().UseAdaptiveAssessment);
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        Assert.AreEqual("currentFiles", (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!.Kind);
        Assert.IsTrue(revised.Plan.AdaptiveFileProcessing);
        Assert.AreEqual(preserved, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == old.Id)).PlanSnapshotJson);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("FutureProcessingCondition")]
    [DataRow("PasswordUnknown")]
    public async Task Current_directory_local_wait_eligibility_does_not_depend_on_reason_allowlist(string reason)
    {
        var (task, run, original) = await CurrentDirectoryInput();
        run.WaitPromptJson = JsonSerializer.Serialize(new {reason, targetId = "main"});
        await Db.SaveChangesAsync();
        Assert.IsTrue((await Plans.GetAsync(task.Id, default))!.ReplanArtifacts.Single().CanReadCurrentDirectory);
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default);
        Assert.AreEqual("currentFiles", (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!.Kind);
    }

    [DataTestMethod]
    [DataRow("pending")]
    [DataRow("running")]
    [DataRow("download-wait")]
    [DataRow("newer-input")]
    [DataRow("foreign-owner")]
    [DataRow("different-part")]
    [DataRow("sibling-reference")]
    [DataRow("symlink")]
    public async Task Current_directory_rejects_shared_or_noncurrent_inputs_and_running_or_network_steps(string fault)
    {
        var (task, run, original) = await CurrentDirectoryInput();
        if (fault == "pending") run.PendingSignalJson = "{}";
        if (fault == "running") run.Status = WorkflowRunStatus.Running;
        if (fault == "download-wait") run.WaitReason = "cloud.cloudAttention";
        if (fault == "newer-input")
        {
            var workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson)!;
            run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [workset.Targets.Single() with
                {ProcessedArtifactId = "latest", ProcessingOperationId = "latest-op"}]});
        }
        if (fault is "foreign-owner" or "different-part")
            await new ArtifactOperationBindingService(Db).BindAsync("workflow", fault == "foreign-owner" ? "9999" : run.Id.ToString(),
                fault == "different-part" ? "other:artifact.download" : "main:artifact.download", original.OperationId);
        if (fault == "sibling-reference")
        {
            var extra = Path.Combine(original.RootDirectory, "other-task.txt"); await File.WriteAllTextAsync(extra, "other");
            var shared = await Artifacts.AdoptAsync(new("shared-current-directory", "shared-current-directory", "download", "another-source"),
                new(original.RootDirectory, [extra], "download", true), "test");
            await new ArtifactOperationBindingService(Db).BindAsync("workflow", "9999", "main:artifact.download", shared.OperationId);
        }
        if (fault == "symlink") File.CreateSymbolicLink(Path.Combine(original.RootDirectory, "linked-file"), original.Files.Single());
        await Db.SaveChangesAsync();
        await Assert.ThrowsAsync<Exception>(() => Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default));
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync()); Assert.IsTrue(File.Exists(original.Files.Single()));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Current_directory_freezes_new_input_and_rejects_later_membership_or_byte_changes(bool membership)
    {
        var (task, old, original) = await CurrentDirectoryInput();
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, old.Id, original.Id), default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        if (membership) await File.WriteAllTextAsync(Path.Combine(original.RootDirectory, "new.txt"), "after capture");
        else await File.WriteAllTextAsync(original.Files.Single(), "changed after capture");
        var ctx = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = workset, ActivityConfigJson = compiled.Nodes[0].ConfigJson, Services = _services, Logger = NullLogger.Instance};
        await Assert.ThrowsAsync<Exception>(() => new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(ctx, workset, default));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Current_directory_excludes_only_proven_system_owner_directory_and_preserves_author_named_siblings()
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var resolver = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var system = await resolver.ResolveAsync("checkpoints", new string('a', 64), 1, original.RootDirectory, null, default);
        await File.WriteAllTextAsync(Path.Combine(system, "temporary.bin"), "system private");
        var author = Directory.CreateDirectory(Path.Combine(original.RootDirectory, ".bakabase-work", "author")).FullName;
        var retained = Path.Combine(author, "important.key"); await File.WriteAllTextAsync(retained, "author file");
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default);
        var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        CollectionAssert.AreEquivalent(new[] {original.Files.Single(), retained}, captured.Files.ToArray());
        Assert.IsTrue(File.Exists(Path.Combine(system, "temporary.bin")));
        await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(captured, resolver, default);
    }

    [TestMethod]
    public async Task Current_directory_receipt_is_task_part_bound_and_own_history_does_not_create_foreign_cleanup_reference()
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default);
        var input = revised.Plan!.Parts.Single().ContinueFrom!;
        var captured = (await Artifacts.GetArtifactAsync(input.ArtifactId))!;
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        row.DeletedAt = DateTime.Now; run.Status = WorkflowRunStatus.Success; await Db.SaveChangesAsync();
        Assert.IsFalse(await new ArtifactCleanupReferenceGuard(Db).HasReferencesAsync(captured, [run.Id], default));
        var proof = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(captured.MetadataJson!, WorkflowJson.Options)!;
        var metadata = await Db.Artifacts.SingleAsync(a => a.Id == captured.Id);
        metadata.MetadataJson = JsonSerializer.Serialize(proof with {PartId = "other"}, WorkflowJson.Options); await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => AcquisitionPlanArtifactInputGuard.RequireAsync(Db, Artifacts, task.Id, "main", input, default));
    }
    [DataTestMethod]
    [DataRow("valid")]
    [DataRow("missing-history")]
    [DataRow("pending-old-answer")]
    [DataRow("running-old")]
    public async Task Current_directory_second_attempt_accepts_only_proven_superseded_waiting_history(string boundary)
    {
        var (task, old, original) = await CurrentDirectoryInput();
        var first = await Plans.ReplanAsync(task.Id, FreshRequest(task, old.Id, original.Id), default);
        var firstInput = first.Plan!.Parts.Single().ContinueFrom!;
        var attempt = new WorkflowRunDbModel {Status = WorkflowRunStatus.Failed,
            PlanSnapshotJson = WorkflowRunPlans.Serialize(AcquisitionPlanCompiler.Compile(first.Plan)),
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = "2"}]};
        Db.WorkflowRuns.Add(attempt); await Db.SaveChangesAsync();
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", attempt.Id.ToString(), "main:artifact.continuation", firstInput.OperationId);
        var row = await Db.AcquisitionTasks.SingleAsync(t => t.Id == task.Id);
        row.WorkflowRunId = attempt.Id; row.Status = AcquisitionStatus.Failed;
        if (boundary == "missing-history") row.PlanHistoryJson = "[]";
        if (boundary == "pending-old-answer") old.PendingSignalJson = "{\"confirm\":true}";
        if (boundary == "running-old") old.Status = WorkflowRunStatus.Running;
        await Db.SaveChangesAsync();
        await File.WriteAllTextAsync(original.Files.Single(), "manually changed between attempts");
        var detail = (await Plans.GetAsync(task.Id, default))!;
        var request = FreshRequest(detail, attempt.Id, firstInput.ArtifactId);
        if (boundary != "valid")
        {
            Assert.IsFalse(detail.ReplanArtifacts.Any(a => a.ArtifactId == firstInput.ArtifactId && a.CanReadCurrentDirectory == true));
            await Assert.ThrowsAsync<Exception>(() => Plans.ReplanAsync(task.Id, request, default));
            Assert.AreEqual(2, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
            return;
        }
        Assert.IsTrue(detail.ReplanArtifacts.Single(a => a.ArtifactId == firstInput.ArtifactId).CanReadCurrentDirectory);
        var second = await Plans.ReplanAsync(task.Id, request, default);
        var input = second.Plan!.Parts.Single().ContinueFrom!;
        Assert.AreEqual(3, second.Task.PlanRevision); Assert.AreNotEqual(firstInput.ArtifactId, input.ArtifactId);
        Assert.AreEqual(attempt.Id, input.PreviousRunId);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == old.Id)).Status);
        Assert.AreEqual(WorkflowRunStatus.Failed, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == attempt.Id)).Status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new AcquisitionWorkflowRunMutationGuard(Db).EnsureCanMutateAsync(old.Id));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("disjoint")]
    [DataRow("intersecting-file")]
    [DataRow("intersecting-empty-directory")]
    [DataRow("malformed-manifest")]
    [DataRow("wrong-fingerprint")]
    [DataRow("unfinished-producer")]
    public async Task Current_directory_shared_manifest_base_claims_only_valid_exact_leaves(string boundary)
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var file = Path.Combine(boundary == "intersecting-file" ? original.RootDirectory : _root, "another-task.bin");
        await File.WriteAllTextAsync(file, "another task's immutable content");
        var directories = boundary == "intersecting-empty-directory"
            ? new[] {Directory.CreateDirectory(Path.Combine(original.RootDirectory, "other-task-empty")).FullName} : [];
        var other = await Artifacts.AdoptAsync(new("ancestor-artifact", "ancestor-artifact", "files.compose", "another-input"),
            new(_root, [file], "compositeFiles", false, Directories: directories), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", "999999", "other:artifact.continuation", other.OperationId);
        if (boundary == "malformed-manifest")
            (await Db.Artifacts.SingleAsync(a => a.Id == other.Id)).ManifestJson = "{\"entries\":[],\"fingerprint\":\"invalid\"}";
        if (boundary == "wrong-fingerprint")
            (await Db.Artifacts.SingleAsync(a => a.Id == other.Id)).Fingerprint = "wrong";
        if (boundary == "unfinished-producer")
            (await Db.ArtifactOperations.SingleAsync(o => o.Id == other.OperationId)).State = ArtifactOperationState.Running;
        await Db.SaveChangesAsync();
        var detail = (await Plans.GetAsync(task.Id, default))!;
        var canRead = detail.ReplanArtifacts.Single(a => a.ArtifactId == original.Id).CanReadCurrentDirectory;
        if (boundary == "disjoint")
        {
            Assert.IsTrue(canRead);
            var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default);
            var capture = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
            CollectionAssert.AreEqual(original.Files.ToArray(), capture.Files.ToArray());
        }
        else
        {
            Assert.IsFalse(canRead);
            await Assert.ThrowsAsync<Exception>(() => Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default));
            Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
        }
        Assert.IsTrue(File.Exists(file)); Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [DataTestMethod]
    [DataRow("owned")]
    [DataRow("wrong-marker")]
    [DataRow("missing-locator")]
    [DataRow("author-sibling")]
    public async Task Current_directory_snapshot_tolerates_only_proven_new_system_workspace(string boundary)
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default);
        var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        var receipt = captured.MetadataJson;
        var resolver = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var key = new string('c', 64);
        var workspace = await resolver.ResolveAsync("operations", key, 1, original.RootDirectory, null, default);
        await File.WriteAllTextAsync(Path.Combine(workspace, "state.json"), "new private state");
        if (boundary == "wrong-marker")
        {
            var marker = Directory.GetFiles(Path.Combine(original.RootDirectory, ".bakabase-work"), "workspace-owner.json", SearchOption.AllDirectories).Single();
            await File.WriteAllTextAsync(marker, new string('d', 32));
        }
        if (boundary == "missing-locator")
            File.Delete(Path.Combine(_root, "appdata", "artifact-workspaces", "locations", "operations-" + key + "-1.json"));
        if (boundary == "author-sibling")
        {
            var author = Directory.CreateDirectory(Path.Combine(original.RootDirectory, ".bakabase-work", "author")).FullName;
            await File.WriteAllTextAsync(Path.Combine(author, "keep.key"), "unknown user file");
        }
        if (boundary == "owned") await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(captured, resolver, default);
        else await Assert.ThrowsAsync<Exception>(() => AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(captured, resolver, default));
        Assert.AreEqual(receipt, (await Artifacts.GetArtifactAsync(captured.Id))!.MetadataJson, "Discovery never rewrites the immutable snapshot.");
        Assert.IsTrue(File.Exists(original.Files.Single())); Assert.IsTrue(File.Exists(Path.Combine(workspace, "state.json")));
    }

    [TestMethod]
    public async Task Current_directory_new_workspace_proof_cannot_hide_previously_captured_files()
    {
        var (task, run, original) = await CurrentDirectoryInput();
        var resolver = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "another-storage")).FullName;
        _ = await resolver.ResolveAsync("operations", new string('d', 64), 1, elsewhere, null, default);
        var owner = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(Path.Combine(_root, "appdata", "artifact-workspaces", "owner.json")))!;
        var userDirectory = Directory.CreateDirectory(Path.Combine(original.RootDirectory, ".bakabase-work", owner)).FullName;
        await File.WriteAllTextAsync(Path.Combine(userDirectory, "workspace-owner.json"), owner);
        var userFile = Path.Combine(userDirectory, "previously-captured.key"); await File.WriteAllTextAsync(userFile, "must not disappear");
        var revised = await Plans.ReplanAsync(task.Id, FreshRequest(task, run.Id, original.Id), default);
        var captured = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        Assert.IsTrue(captured.Files.Contains(userFile));
        var workspace = await resolver.ResolveAsync("operations", new string('e', 64), 1, original.RootDirectory, null, default);
        await File.WriteAllTextAsync(Path.Combine(workspace, "new-system-state.json"), "added later");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(captured, resolver, default));
        Assert.IsTrue(File.Exists(userFile));
    }

}

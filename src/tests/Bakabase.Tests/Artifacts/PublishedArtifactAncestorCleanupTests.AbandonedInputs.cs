using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private sealed class AbandonedExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? progress, CancellationToken ct) =>
            Task.FromResult(new ArchiveExtractionResult(false, request.Directory, "Fixture extraction failed."));
    }

    private async Task<(AcquisitionTaskDbModel Task, ArtifactRecord Raw, ArtifactRecord Final,
        string OperationId, string Private, string StateFile, AcquisitionTaskDeletionService Service)>
        AbandonedDeletionFixture(string? unboundLeaseOwner = null, IReadOnlyList<int>? ownerRuns = null,
            string? currentInputScenario = null, bool completedNoTransform = false, bool stagedOnly = false)
    {
        ArtifactRecord raw;
        if (currentInputScenario == null)
            raw = await Artifact(stagedOnly ? "processedFiles" : "download", filename: stagedOnly ? "image.png" : "archive.zip", contents: "archive bytes");
        else
        {
            var currentDirectory = Path.Combine(root, "manual-current-files");
            Directory.CreateDirectory(currentDirectory);
            var currentFile = Path.Combine(currentDirectory, "archive.zip");
            await File.WriteAllTextAsync(currentFile, "archive bytes");
            raw = await store.AdoptAsync(new(ArtifactManifestFiles.Hash("capture-current-files"), "capture-current-files",
                    currentInputScenario == "wrong-producer" ? "manual" : "files.captureCurrentDirectory", "current-input"),
                new(currentDirectory, [currentFile], "currentFiles", false), "fixture");
            var inputBinding = currentInputScenario == "wrong-binding"
                ? "part-fixture:adaptive:input:another-artifact"
                : "part-fixture:adaptive:input:" + raw.Id;
            Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow",
                currentInputScenario == "foreign-owner" ? "2" : "1", inputBinding, raw.OperationId));
        }
        var final = await Artifact("publishedFiles", raw, filename: stagedOnly ? "image.png" : "archive.zip", contents: "archive bytes");
        var operationId = ArtifactManifestFiles.Hash("failed-private-input");
        var claim = await store.ClaimAsync(new(operationId, "failed-private-input", "files.process", raw.Fingerprint),
            unboundLeaseOwner ?? "fixture", TimeSpan.FromMinutes(2));
        Assert.IsNotNull(claim.Lease);
        var workspaces = new FileProcessingWorkspaceResolver(root);
        var attempt = await workspaces.ResolveAsync("operations", operationId, 1, raw.RootDirectory,
            raw.RootDirectory, default, newAttempt: true);
        var extraction = new AbandonedExtraction();
        var executor = new FileProcessingPlanExecutor(extraction);
        if (completedNoTransform)
        {
            var done = await executor.ExecuteAsync(new() {Requirement = "notRequired", Steps = []},
                raw.RootDirectory, raw.Files, Path.Combine(attempt, "plan"));
            Assert.IsTrue(done.Completed);
            // Simulate failure between the executor's complete journal and the operation's
            // durable output registration, leaving its staged and result copies unregistered.
        }
        else if (stagedOnly)
        {
            var failure = await executor.ExecuteAsync(new() {Requirement = "required", Steps =
                [new() {Id = "extract", Op = "extractArchive", Selector = "image.png"}]},
                raw.RootDirectory, raw.Files, Path.Combine(attempt, "plan"));
            Assert.IsFalse(failure.Completed); Assert.AreEqual("ArchiveInputRequired", failure.FailureCode);
        }
        else
        {
            var failure = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => executor.ExecuteAsync(new()
            {
                Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Selector = "archive.zip"}],
            }, raw.RootDirectory, raw.Files, Path.Combine(attempt, "plan")));
            Assert.AreEqual("Fixture extraction failed.", failure.Message);
        }
        if (unboundLeaseOwner == null)
            Assert.IsTrue(await store.FailAsync(claim.Lease!, "Fixture extraction failed."));
        var stateFile = Directory.GetFiles(attempt, "state.json", SearchOption.AllDirectories).Single();
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        Assert.AreEqual(raw.Fingerprint, state.InputManifest!.Fingerprint);
        Assert.AreEqual(1, state.Outputs.Count);
        var privateFile = state.Outputs["download"].Single().Path;
        Assert.IsTrue(File.Exists(privateFile));
        var bindings = new ArtifactOperationBindingService(db);
        if (unboundLeaseOwner == null)
            Assert.IsTrue(await bindings.BindAsync("workflow", "1", "part:failed-process", operationId));
        Assert.IsTrue(await bindings.BindAsync("workflow", "1", "part:artifact.publish", final.OperationId));
        var task = new AcquisitionTaskDbModel
        {
            Title = "Accepted task with a failed private input", Status = AcquisitionStatus.Completed,
            WorkflowRunId = 1, PlanRevision = 2, DeletedAt = DateTime.UtcNow,
        };
        db.AcquisitionTasks.Add(task);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, executor, new(extraction), root, workspaces);
        var service = new AcquisitionTaskDeletionService(db, new FinishedDeletionActivity(ownerRuns), new(),
            new(db, store, maintenance, cleanup, new Storage(), workspaces: workspaces));
        return (task, raw, final, operationId, privateFile, stateFile, service);
    }

    [TestMethod]
    public async Task Abandoned_owned_failed_input_is_reclaimed_idempotently_without_rewriting_execution_history()
    {
        var f = await AbandonedDeletionFixture();
        var state = await File.ReadAllTextAsync(f.StateFile);
        var operation = await store.GetOperationAsync(f.OperationId);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        var group = preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:"));
        Assert.IsNull(group.ReasonCode);
        Assert.AreEqual(new FileInfo(f.Private).Length, group.Bytes);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(result.RemovedFiles > 0);
        Assert.IsFalse(File.Exists(f.Private));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreEqual(state, await File.ReadAllTextAsync(f.StateFile));
        var after = await store.GetOperationAsync(f.OperationId);
        Assert.AreEqual(operation!.State, after!.State);
        Assert.AreEqual(operation.Attempt, after.Attempt);
        Assert.AreEqual(operation.Error, after.Error);
        Assert.IsNull(after.OutputArtifactId);
        Assert.AreEqual(f.Task.DeletedAt, await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id).Select(t => t.DeletedAt).SingleAsync());
        Assert.IsTrue(await db.WorkflowRuns.AnyAsync(r => r.Id == 1));
        var again = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        var repeated = await f.Service.DeleteAsync(new(again.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(0, repeated.RemovedFiles);
        Assert.IsTrue(f.Final.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stage_cleanup_includes_only_proven_unbound_copies_outside_the_selected_roots(bool extraFile)
    {
        var f = await AbandonedDeletionFixture("workflow:1:adaptive:part:0");
        await db.ArtifactOperations.Where(o => o.Id == f.OperationId).ExecuteUpdateAsync(s =>
            s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddDays(-1)));
        var otherStage = Directory.CreateDirectory(Path.Combine(root, "processing")).FullName;
        await File.WriteAllTextAsync(Path.Combine(otherStage, "working.bin"), "stage output");
        var plan = new AcquisitionPlan {Parts = [new() {Id = "part"}], StageDirectories = new() {TaskId = f.Task.Id,
            Parts = new() {["part"] = new() {Processing = new(otherStage, "borrowed", "userDirectoryRevision")
                {BindingId = Guid.NewGuid().ToString("N")}}}}};
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json)));
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 1, Module = "acquisition", EntityKind = "task",
            EntityId = f.Task.Id.ToString(), Revision = "2"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var state = await File.ReadAllTextAsync(f.StateFile);
        var extra = Path.Combine(Path.GetDirectoryName(f.Private)!, "manual.key");
        if (extraFile) await File.WriteAllTextAsync(extra, "manual file");

        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        var groups = preview.Rows.Single().Cleanup!.Groups;
        Assert.IsTrue(groups.Single(g => g.Directory == otherStage).Eligible);
        Assert.AreEqual(!extraFile, groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).Eligible);
        var result = await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));

        Assert.AreEqual(!extraFile, result.CleanupResults!.Single().Completed);
        Assert.AreEqual(extraFile, File.Exists(f.Private));
        Assert.AreEqual(extraFile, File.Exists(extra));
        Assert.IsFalse(Directory.Exists(otherStage));
        Assert.AreEqual(state, await File.ReadAllTextAsync(f.StateFile));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
    }

    [TestMethod]
    [DataRow("manual-file")]
    [DataRow("foreign-binding")]
    [DataRow("active-consumer")]
    [DataRow("registered-file")]
    public async Task Abandoned_input_cleanup_preserves_unknown_files_and_other_owners(string protection)
    {
        var f = await AbandonedDeletionFixture();
        var unknown = Path.Combine(Path.GetDirectoryName(f.Private)!, "manual.key");
        if (protection == "manual-file") await File.WriteAllTextAsync(unknown, "user supplied key");
        if (protection == "foreign-binding")
            Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "foreign", f.OperationId));
        if (protection == "active-consumer")
            Assert.IsTrue(await store.AttachConsumerAsync(f.OperationId, "active-reader", TimeSpan.FromMinutes(5)));
        if (protection == "registered-file")
            await store.AdoptAsync(new("registered-private", "registered-private", "manual", "private"),
                new(Path.GetDirectoryName(f.Private)!, [f.Private]), "other-owner");
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        if (protection == "manual-file")
        {
            var blocked = preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:"));
            Assert.AreEqual("CLEANUP_FILES_RETAINED", blocked.ReasonCode);
            Assert.IsTrue(blocked.SizeUnknown);
        }
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Private));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.IsTrue(File.Exists(f.StateFile));
        if (protection == "manual-file") Assert.AreEqual("user supplied key", await File.ReadAllTextAsync(unknown));
    }

    [TestMethod]
    [DataRow("state")]
    [DataRow("private-file")]
    public async Task Abandoned_input_cleanup_rejects_changes_after_fixed_preview(string change)
    {
        var f = await AbandonedDeletionFixture();
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        Assert.IsNull(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).ReasonCode);
        if (change == "state") await File.AppendAllTextAsync(f.StateFile, "\n ");
        else File.SetLastWriteTimeUtc(f.Private, File.GetLastWriteTimeUtc(f.Private).AddMinutes(1));
        var state = await File.ReadAllTextAsync(f.StateFile);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Private));
        Assert.AreEqual(state, await File.ReadAllTextAsync(f.StateFile));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
    }

    [TestMethod]
    [DataRow("expired")]
    [DataRow("interrupted-owner")]
    [DataRow("valid-lease")]
    [DataRow("running-owner")]
    [DataRow("foreign-owner")]
    [DataRow("unparseable-owner")]
    [DataRow("active-consumer")]
    public async Task Abandoned_unbound_claim_requires_expired_lease_and_exact_inactive_workflow_owner(string scenario)
    {
        var owner = scenario switch
        {
            "foreign-owner" or "interrupted-owner" => "workflow:2:adaptive:part-fixture:0",
            "unparseable-owner" => "unexpected-worker:1",
            _ => "workflow:1:adaptive:part-fixture:0",
        };
        var f = await AbandonedDeletionFixture(owner, scenario == "interrupted-owner" ? [1, 2] : null);
        var eligible = scenario is "expired" or "interrupted-owner";
        if (scenario != "valid-lease")
            await db.ArtifactOperations.Where(o => o.Id == f.OperationId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-5)));
        if (scenario == "running-owner")
            await db.WorkflowRuns.Where(r => r.Id == 1)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        if (scenario == "interrupted-owner")
            await db.WorkflowRuns.Where(r => r.Id == 2)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Interrupted));
        if (scenario == "active-consumer")
            Assert.IsTrue(await store.AttachConsumerAsync(f.OperationId, "active-reader", TimeSpan.FromMinutes(5)));
        Assert.AreEqual(0, await db.ArtifactOperationBindings.CountAsync(b => b.OperationId == f.OperationId));
        var before = await store.GetOperationAsync(f.OperationId);
        Assert.AreEqual(ArtifactOperationState.Running, before!.State);
        var state = await File.ReadAllTextAsync(f.StateFile);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        if (eligible)
            Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).Eligible);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(!eligible, File.Exists(f.Private), scenario);
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreEqual(state, await File.ReadAllTextAsync(f.StateFile));
        var after = await store.GetOperationAsync(f.OperationId);
        Assert.AreEqual(before.State, after!.State);
        Assert.AreEqual(before.Attempt, after.Attempt);
        Assert.AreEqual(before.LeaseOwner, after.LeaseOwner);
        Assert.AreEqual(before.LeaseUntil, after.LeaseUntil);
        Assert.IsNull(after.OutputArtifactId);
        Assert.AreEqual(0, await db.ArtifactOperationBindings.CountAsync(b => b.OperationId == f.OperationId));
    }

    [TestMethod]
    [DataRow("renewed")]
    [DataRow("new-token")]
    public async Task Abandoned_unbound_claim_is_rechecked_after_preview_before_deleting_private_inputs(string change)
    {
        var f = await AbandonedDeletionFixture("workflow:1:adaptive:part-fixture:0");
        await db.ArtifactOperations.Where(o => o.Id == f.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-5)));
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).Eligible);
        if (change == "renewed")
            await db.ArtifactOperations.Where(o => o.Id == f.OperationId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(5)));
        else
            await db.ArtifactOperations.Where(o => o.Id == f.OperationId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseToken, "another-claim-token"));
        var before = await store.GetOperationAsync(f.OperationId);
        var state = await File.ReadAllTextAsync(f.StateFile);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.IsTrue(File.Exists(f.Private));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreEqual(state, await File.ReadAllTextAsync(f.StateFile));
        var after = await store.GetOperationAsync(f.OperationId);
        Assert.AreEqual(before!.LeaseToken, after!.LeaseToken);
        Assert.AreEqual(before.LeaseUntil, after.LeaseUntil);
        Assert.AreEqual(before.State, after.State);
    }

    [TestMethod]
    [DataRow("captured")]
    [DataRow("wrong-binding")]
    [DataRow("wrong-producer")]
    [DataRow("foreign-owner")]
    public async Task Abandoned_current_file_snapshot_requires_exact_capture_binding_and_never_deletes_manual_source(string scenario)
    {
        var f = await AbandonedDeletionFixture("workflow:1:adaptive:part-fixture:0", currentInputScenario: scenario);
        await db.ArtifactOperations.Where(o => o.Id == f.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-5)));
        Assert.AreEqual("currentFiles", f.Raw.Kind);
        Assert.IsFalse(f.Raw.Managed);
        var state = await File.ReadAllTextAsync(f.StateFile);
        var sourceBefore = await store.GetArtifactAsync(f.Raw.Id);
        var producerBefore = await store.GetOperationAsync(f.Raw.OperationId);
        var preview = await f.Service.PreviewAsync(new([f.Task.Id], true, true, true, RetryRetainedFiles: true));
        if (scenario == "captured")
            Assert.IsTrue(preview.Rows.Single().Cleanup!.Groups.Single(g => g.ArtifactId.StartsWith("abandoned-input:")).Eligible);
        await f.Service.DeleteAsync(new(preview.PreviewId, RetryRetainedFiles: true));
        Assert.AreEqual(scenario != "captured", File.Exists(f.Private), scenario);
        Assert.AreEqual("archive bytes", await File.ReadAllTextAsync(f.Raw.Files.Single()));
        Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreEqual(state, await File.ReadAllTextAsync(f.StateFile));
        var sourceAfter = await store.GetArtifactAsync(f.Raw.Id);
        Assert.AreEqual(sourceBefore!.State, sourceAfter!.State);
        Assert.AreEqual(sourceBefore.Fingerprint, sourceAfter.Fingerprint);
        Assert.AreEqual(sourceBefore.RootDirectory, sourceAfter.RootDirectory);
        CollectionAssert.AreEqual(sourceBefore.Files.ToArray(), sourceAfter.Files.ToArray());
        var producerAfter = await store.GetOperationAsync(f.Raw.OperationId);
        Assert.AreEqual(producerBefore!.State, producerAfter!.State);
        Assert.AreEqual(producerBefore.OutputArtifactId, producerAfter.OutputArtifactId);
        Assert.AreEqual(ArtifactOperationState.Running, (await store.GetOperationAsync(f.OperationId))!.State);
    }
}

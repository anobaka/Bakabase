using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private sealed class PrivateResultExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "final.txt"), "actual final contents", ct);
            return new(true, request.Directory, "");
        }
    }

    private async Task<(ArtifactRecord Raw, ArtifactRecord Source, ArtifactRecord Final, string Private,
        string PlanState, string RecursiveState, ArtifactWorkspaceMaintenance Maintenance)> PrivateResultFixture(bool retired)
    {
        var raw = await Artifact("download", filename: "archive.zip", contents: "archive bytes");
        var archive = new PrivateResultExtraction();
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, SourceCleanup = "keep"};
        var attempt = Path.Combine(root, "private-operation", "attempt-1");
        var planned = await new FileProcessingPlanExecutor(archive).ExecuteAsync(new() {Requirement = "notRequired"},
            raw.RootDirectory, raw.Files, Path.Combine(attempt, "plan"), storagePolicy: policy);
        Assert.IsTrue(planned.Completed, planned.Message);
        var recursive = await new RecursiveArchiveExecutor(archive).ExecuteAsync(planned.OutputDirectory!, planned.Files,
            Path.Combine(attempt, "recursive"), new() {MinimumFreeBytes = 0}, storagePolicy: policy,
            inputDirectories: planned.Directories);
        Assert.IsTrue(recursive.Completed, recursive.Message);
        var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(raw.Id,
            "{\"requirement\":\"notRequired\",\"steps\":[]}", null, StoragePolicy: policy), Json);
        var source = await store.AdoptAsync(new("private-result", "private-result", "files.process", raw.Fingerprint),
            new(recursive.OutputDirectory!, recursive.Files, "processedFiles", true, [raw.Id], metadata,
                Directories: recursive.Directories), "fixture");
        var finalRoot = Path.Combine(root, "final-delivery"); Directory.CreateDirectory(finalRoot);
        var finals = new List<string>();
        foreach (var file in source.Files)
        {
            var target = Path.Combine(finalRoot, Path.GetRelativePath(source.RootDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); finals.Add(target);
        }
        var final = await store.AdoptAsync(new("private-publication", "private-publication", "files.publish", source.Fingerprint),
            new(finalRoot, finals, "publishedFiles", false, [source.Id]), "fixture");
        var bindings = new ArtifactOperationBindingService(db);
        Assert.IsTrue(await bindings.BindAsync("workflow", "1", "private-result", source.OperationId));
        Assert.IsTrue(await bindings.BindAsync("workflow", "1", "private-publication", final.OperationId));
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(archive), new(archive), root);
        if (retired)
        {
            var candidate = (await Preview(final)).Single(r => r.ArtifactId == source.Id);
            Assert.IsTrue((await cleanup.ReclaimAsync(candidate, [1], default)).Completed);
        }
        cleanup = new(db, store, new ArchiveQuarantineService(store, root, new Storage()), maintenance: maintenance);
        return (raw, source, final, planned.Files.Single(),
            Path.Combine(Path.GetDirectoryName(planned.OutputDirectory!)!, "state.json"),
            Path.Combine(attempt, "recursive", "recursive-state.json"), maintenance);
    }

    [TestMethod]
    public async Task Current_recursive_cleanup_reclaims_replaced_private_plan_result_but_keeps_raw_and_delivery()
    {
        var f = await PrivateResultFixture(false);
        var preview = (await f.Maintenance.PreviewAsync([f.Source.Id], default)).Single();
        Assert.IsTrue(preview.PreviewFiles!.Any(p => p.Path == f.Private));
        var result = (await f.Maintenance.ReclaimAsync([f.Source.Id], default)).Single();
        Assert.IsTrue(result.Completed, result.Reason);
        Assert.IsFalse(File.Exists(f.Private));
        Assert.IsTrue(f.Raw.Files.All(File.Exists)); Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.IsTrue(File.Exists(f.PlanState)); Assert.IsTrue(File.Exists(f.RecursiveState));
    }

    [TestMethod]
    public async Task Retired_recursive_private_result_is_available_through_existing_preview_apply_and_is_idempotent()
    {
        var f = await PrivateResultFixture(true);
        var preview = (await Preview(f.Final)).Single(p => p.ArtifactId == f.Source.Id);
        Assert.IsTrue(preview.Eligible, preview.Reason);
        Assert.AreEqual(new FileInfo(f.Private).Length, preview.Bytes);
        var result = await cleanup.ReclaimAsync(preview, [1], default);
        Assert.IsTrue(result.Completed, result.Reason); Assert.AreEqual(1, result.RemovedFiles);
        Assert.IsFalse(File.Exists(f.Private));
        Assert.IsTrue(f.Raw.Files.All(File.Exists)); Assert.IsTrue(f.Final.Files.All(File.Exists));
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(f.Source.Id))!.State);
        Assert.AreEqual("AlreadyRetired", (await Preview(f.Final)).Single(p => p.ArtifactId == f.Source.Id).Reason);
        var receipts = Directory.GetFiles(Path.Combine(root, "file-processing", "workspace-cleanup-receipts"), "*.json");
        var receipt = JsonSerializer.Deserialize<ArtifactWorkspaceCleanupJournal.Receipt>(await File.ReadAllTextAsync(receipts.Single()), Json)!;
        Assert.AreEqual(f.Source.Id, receipt.RelatedArtifactId); Assert.AreEqual(f.Final.Id, receipt.ArtifactId);
        Assert.AreEqual("Completed", receipt.Status);
    }

    [TestMethod]
    [DataRow("accepted")]
    [DataRow("unaccepted")]
    [DataRow("changed-payload")]
    [DataRow("revised")]
    [DataRow("wrong-run")]
    public async Task Retired_private_cleanup_honors_current_delivery_acceptance_and_keeps_source_integrity_checks(string scenario)
    {
        var f = await PrivateResultFixture(true);
        var consent = new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            42, 1, 1, scenario != "unaccepted", [1],
            [new(f.Final.Id, f.Final.OperationId, f.Final.Fingerprint, f.Final.RootDirectory)], scenario != "changed-payload");
        var revision = 1;
        ArtifactDeliveryCleanupAuthorization Authorize(ArtifactDeliveryCleanupConsent proof) =>
            new(proof, _ => Task.FromResult(proof.PlanRevision == revision && proof.CompletedRunId == 1), store);
        var authorization = Authorize(consent);
        var preview = (await cleanup.PreviewForDeletionAsync([f.Final.Id], [1], authorization, default))
            .Single(p => p.ArtifactId == f.Source.Id);
        Assert.IsTrue(preview.Eligible);
        var acceptedDirectory = Path.Combine(root, "user-accepted");
        Directory.Move(f.Final.RootDirectory, acceptedDirectory);
        if (scenario == "changed-payload")
        {
            var stamp = File.GetLastWriteTimeUtc(f.Private);
            await File.WriteAllTextAsync(f.Private, new string('x', (int)new FileInfo(f.Private).Length));
            File.SetLastWriteTimeUtc(f.Private, stamp);
        }
        if (scenario == "revised") revision++;
        if (scenario == "wrong-run") authorization = Authorize(consent with {CompletedRunId = 2});
        var outcome = await cleanup.ReclaimForDeletionAsync(preview, [1], authorization, default);
        Assert.AreEqual(scenario == "accepted", outcome.Completed, outcome.Reason);
        Assert.AreEqual(scenario != "accepted", File.Exists(f.Private));
        Assert.IsTrue(f.Raw.Files.All(File.Exists));
        Assert.AreEqual("actual final contents", await File.ReadAllTextAsync(Path.Combine(acceptedDirectory,
            Path.GetRelativePath(f.Final.RootDirectory, f.Final.Files.Single()))));
        Assert.IsTrue(File.Exists(f.PlanState)); Assert.IsTrue(File.Exists(f.RecursiveState));
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(f.Source.Id))!.State);
        if (scenario == "accepted")
        {
            var path = Directory.GetFiles(Path.Combine(root, "file-processing", "workspace-cleanup-receipts"), "*.json").Single();
            var receipt = JsonSerializer.Deserialize<ArtifactWorkspaceCleanupJournal.Receipt>(await File.ReadAllTextAsync(path), Json)!;
            Assert.AreEqual(consent.Id, receipt.Consent!.Id);
            Assert.AreEqual("Completed", receipt.Status);
            Assert.AreEqual(1, receipt.RemovedFiles);
            Assert.AreEqual(f.Private, receipt.Files.Single().Path.StartsWith("appdata:")
                ? Path.Combine(root, receipt.Files.Single().Path["appdata:".Length..]) : receipt.Files.Single().Path);
        }
    }

    [TestMethod]
    [DataRow("empty-private-parent")]
    [DataRow("checkpoint-unavailable")]
    [DataRow("directory-replacement")]
    [DataRow("checkpoint-changed")]
    public async Task Accepted_cleanup_receipt_requires_its_retained_checkpoint_and_an_absent_file(string scenario)
    {
        var f = await PrivateResultFixture(true);
        var consent = new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            42, 1, 1, true, [1], [new(f.Final.Id, f.Final.OperationId, f.Final.Fingerprint, f.Final.RootDirectory)], true);
        var journal = new ArtifactWorkspaceCleanupJournal(root);
        var boundary = Path.GetDirectoryName(f.PlanState)!;
        var receipt = await journal.BeginAsync(f.Final,
            new Dictionary<string, long> {[f.Private] = new FileInfo(f.Private).Length}, default, consent,
            relatedArtifactId: f.Source.Id, checkpointDirectory: boundary);
        Directory.Move(f.Final.RootDirectory, Path.Combine(root, "accepted-delivery"));
        File.Delete(f.Private);
        if (scenario == "empty-private-parent") Directory.Delete(Path.GetDirectoryName(f.Private)!);
        if (scenario == "directory-replacement") Directory.CreateDirectory(f.Private);
        if (scenario == "checkpoint-unavailable") Directory.Move(boundary, boundary + "-disconnected");
        if (scenario == "checkpoint-changed") await File.AppendAllTextAsync(f.PlanState, "\n ");
        if (scenario == "checkpoint-unavailable")
        {
            await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(() => journal.FinishAsync(receipt, "Completed", default));
            return;
        }
        if (scenario == "checkpoint-changed")
        {
            await Assert.ThrowsExceptionAsync<IOException>(() => journal.FinishAsync(receipt, "Completed", default));
            return;
        }
        var result = await journal.FinishAsync(receipt, "Completed", default);
        Assert.AreEqual(scenario == "empty-private-parent" ? "Completed" : "Partial", result.Status);
        Assert.AreEqual(scenario == "empty-private-parent" ? 1 : 0, result.RemovedFiles);
        Assert.IsTrue(File.Exists(f.PlanState));
    }

    [TestMethod]
    [DataRow("keep")]
    [DataRow("unknownPolicy")]
    [DataRow("retainedFile")]
    [DataRow("boundary")]
    [DataRow("partialLineage")]
    [DataRow("extraFile")]
    [DataRow("registered")]
    [DataRow("changedPayload")]
    [DataRow("changedDelivery")]
    [DataRow("runBusy")]
    [DataRow("sharedOwner")]
    public async Task Retired_private_cleanup_refuses_missing_proofs_retention_references_changes_and_active_owners(string scenario)
    {
        var f = await PrivateResultFixture(true);
        var preview = (await Preview(f.Final)).Single(p => p.ArtifactId == f.Source.Id);
        Assert.IsTrue(preview.Eligible);
        var plan = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(f.PlanState), Json)!;
        var recursive = JsonSerializer.Deserialize<RecursiveArchiveExecutor.State>(await File.ReadAllTextAsync(f.RecursiveState), Json)!;
        SemaphoreSlim? held = null;
        if (scenario == "keep") plan.StoragePolicy = plan.StoragePolicy! with {CleanupIntermediates = false};
        if (scenario == "unknownPolicy") plan.StoragePolicy = null;
        if (scenario == "retainedFile") plan.RetainedFiles.Add(f.Private);
        if (scenario == "boundary") plan.BoundaryResults["kept"] = new();
        if (scenario == "partialLineage") recursive.Nodes.Single().OutputLinks = [];
        if (scenario == "extraFile") await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(f.Private)!, "unknown.key"), "keep");
        if (scenario == "registered") await store.AdoptAsync(new("retained-private", "retained-private", "manual", "private"),
            new(Path.GetDirectoryName(f.Private)!, [f.Private]), "another-owner");
        if (scenario == "changedPayload")
        {
            var stamp = File.GetLastWriteTimeUtc(f.Private);
            await File.WriteAllTextAsync(f.Private, new string('x', (int)new FileInfo(f.Private).Length));
            File.SetLastWriteTimeUtc(f.Private, stamp);
        }
        if (scenario == "changedDelivery") await File.WriteAllTextAsync(f.Final.Files.Single(), "changed");
        if (scenario == "runBusy") {held = WorkflowRunMutationGate.For(1); await held.WaitAsync();}
        if (scenario == "sharedOwner") Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "shared", f.Source.OperationId));
        await File.WriteAllTextAsync(f.PlanState, JsonSerializer.Serialize(plan, Json));
        await File.WriteAllTextAsync(f.RecursiveState, JsonSerializer.Serialize(recursive, Json));
        try
        {
            var result = await cleanup.ReclaimAsync(preview, [1], default);
            Assert.IsFalse(result.Completed, scenario + ": " + result.Reason);
            Assert.IsTrue(File.Exists(f.Private)); Assert.IsTrue(f.Raw.Files.All(File.Exists));
        }
        finally {held?.Release();}
    }
}

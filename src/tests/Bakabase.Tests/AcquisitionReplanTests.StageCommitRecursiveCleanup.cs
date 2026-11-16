using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Moved_recursive_and_checkpoint_results_keep_exact_cleanup_validation(bool recursive)
    {
        var (task, run, original) = await Failed();
        var stage = Directory.CreateDirectory(Path.Combine(_root, "cleanup-current")).FullName;
        task = await BindCurrentStage(task, "processing", stage);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, Materialization = "copy", SourceCleanup = "keep"};
        if (recursive)
        {
            var archiveRoot = Directory.CreateDirectory(Path.Combine(_root, "recursive-origin")).FullName;
            var archive = Path.Combine(archiveRoot, "archive.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(zip.CreateEntry("payload.txt").Open());
                writer.Write("small real archive signature for deterministic admission");
            }
            original = await Artifacts.AdoptAsync(new("recursive-original", "recursive-original", "download", "source"),
                new(archiveRoot, [archive], "download", true), "test");
            await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:download", original.OperationId);
        }
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var key = ArtifactManifestFiles.Hash("relocated-cleanup-" + recursive);
        var operationRoot = await workspaces.ResolveOperationAsync(key, 1, original.RootDirectory, _root, null, true, policy, "Task", default);
        var recipe = new FileProcessingPlan {Requirement = "required", Steps =
        [new() {Id = "first", Op = "renameFile", Selector = "original.bin", TargetName = "renamed.bin"},
         new() {Id = "second", Op = "renameFile", Input = "first", Selector = "renamed.bin", TargetName = "final.bin"}]};
        string resultRoot; IReadOnlyList<string> resultFiles;
        if (recursive)
        {
            var result = await new RecursiveArchiveExecutor(new RelocationArchives()).ExecuteAsync(original.RootDirectory,
                original.Files, Path.Combine(operationRoot, "recursive"), new() {Enabled = true, MinimumFreeBytes = 0},
                storagePolicy: policy, onlyInitialLayer: true);
            Assert.IsTrue(result.Completed, result.Message);
            resultRoot = result.OutputDirectory!; resultFiles = result.Files;
        }
        else
        {
            var result = await new FileProcessingPlanExecutor(_continuationArchives).ExecuteAsync(recipe, original.RootDirectory,
                original.Files, Path.Combine(operationRoot, "plan"), stopAfterStepId: "first", storagePolicy: policy);
            Assert.IsTrue(result.Completed, result.Message);
            resultRoot = result.OutputDirectory!; resultFiles = result.Files;
        }
        var source = await Artifacts.AdoptAsync(new(key, "relocated-" + recursive, "files.process", original.Fingerprint),
            new(resultRoot, resultFiles, "processedFiles", true, [original.Id], MetadataJson: JsonSerializer.Serialize(
                new FileArtifactProcessingMetadata(original.Id, JsonSerializer.Serialize(recipe, WorkflowJson.Options), null,
                    CompletedThroughStepId: recursive ? null : "first", StoragePolicy: policy), WorkflowJson.Options)), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:process:raw", source.OperationId);
        var preview = (await _services.GetRequiredService<ArtifactWorkspaceMaintenance>().PreviewAsync([source.Id], default)).Single();
        Assert.IsTrue(preview.Eligible, preview.Reason);
        Assert.IsTrue(preview.FileCount > 0);
        var current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, run.Id, "main", binding, source, default);
        Assert.IsFalse(source.Files.Any(File.Exists));
        var resultCleanup = (await _services.GetRequiredService<ArtifactWorkspaceMaintenance>().ReclaimAsync([source.Id], default)).Single();
        Assert.IsTrue(resultCleanup.Completed, resultCleanup.Reason);
        Assert.AreEqual(preview.FileCount, resultCleanup.RemovedFiles);
        Assert.IsTrue(current.Files.All(File.Exists)); Assert.IsTrue(original.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("changed-final")]
    [DataRow("wrong-original")]
    [DataRow("wrong-manifest")]
    public async Task Relocated_final_is_reverified_under_the_executor_gate_without_widening_private_selection(string fault)
    {
        var (task, run, original) = await Failed();
        task = await BindCurrentStage(task, "processing", Directory.CreateDirectory(Path.Combine(_root, "relocated-result")).FullName);
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, Materialization = "copy"};
        var recipe = new FileProcessingPlan {Requirement = "notRequired"};
        var executor = new FileProcessingPlanExecutor(_continuationArchives);
        var raw = await executor.ExecuteAsync(recipe, original.RootDirectory, original.Files, Path.Combine(_root, "verify-before-delete"), storagePolicy: policy);
        var source = await Artifacts.AdoptAsync(new("relocated-protected", "relocated-protected", "files.process", original.Fingerprint),
            new(raw.OutputDirectory!, raw.Files, "processedFiles", true, [original.Id], MetadataJson: JsonSerializer.Serialize(
                new FileArtifactProcessingMetadata(original.Id, JsonSerializer.Serialize(recipe, WorkflowJson.Options), null, StoragePolicy: policy), WorkflowJson.Options)), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:process:raw", source.OperationId);
        var workspace = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspace,
            task.Id, run.Id, "main", task.Plan!.StageDirectories!.Parts["main"].Processing!, source, default);
        var proof = await AcquisitionProcessingStageCommit.ResolveMovedResultAsync(Db, Artifacts, workspace, source, default);
        Assert.IsNotNull(proof);
        var paths = (await FileProcessingPlanExecutor.GetReclaimableFilesAsync(source.RootDirectory)).ToHashSet(FileProcessingFiles.PathComparer);
        Assert.IsTrue(paths.Count > 0);
        if (fault == "changed-final") await File.WriteAllTextAsync(current.Files.Single(), new string('x', (int)current.Manifest.Entries.Single().Length));
        if (fault == "wrong-original") proof = proof with {OriginalRoot = Path.Combine(_root, "unrelated")};
        if (fault == "wrong-manifest") proof = proof with {Manifest = proof.Manifest with
            {Entries = proof.Manifest.Entries.Select(e => e with {Sha256 = new string('0', 64)}).ToArray()}};
        await executor.ReclaimCompletedIntermediatesAsync(source.RootDirectory, source.Manifest, paths, default,
            overrideRetention: false, snapshot: null, relocatedResult: proof);
        Assert.IsTrue(paths.All(File.Exists)); Assert.IsTrue(original.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("available")]
    [DataRow("retired")]
    [DataRow("modified-journal")]
    [DataRow("foreign-owner")]
    [DataRow("active-owner")]
    [DataRow("active-consumer")]
    public async Task Durable_stage_move_absence_proof_covers_only_real_moved_files_and_survives_canonical_retirement(string variant)
    {
        var (task, runId, source) = await StageCommitInput(variant != "retired");
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, runId, "main", task.Plan!.StageDirectories!.Parts["main"].Processing!, source, default);
        if (variant == "retired")
        {
            Assert.IsTrue(await Artifacts.TryQuarantineAsync(current.Id));
            await Artifacts.SetLocationAsync(current.Id, Path.Combine(_root, "retired-quarantine"), ArtifactLocationState.Retired);
        }
        if (variant == "modified-journal")
        {
            var path = Directory.GetFiles(workspaces.StageCommitMetadataDirectory, "*.json").Single();
            var receipt = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            receipt["move"]!["files"]![0]!["relativePath"] = "license.key";
            await File.WriteAllTextAsync(path, receipt.ToJsonString());
        }
        if (variant == "foreign-owner") await new ArtifactOperationBindingService(Db).BindAsync("other-module", "owner", "main:process", current.OperationId);
        if (variant == "active-owner") await Db.WorkflowRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Running));
        if (variant == "active-consumer") await Artifacts.AttachConsumerAsync(source.OperationId, "foreign-reader", TimeSpan.FromHours(1));
        var proof = await AcquisitionProcessingStageCommit.ReadMovedEntriesAsync(Db, Artifacts, workspaces, source, default);
        if (variant is "available" or "retired")
        {
            Assert.IsNotNull(proof);
            CollectionAssert.AreEqual(new[] {"images/one.jpg"}, proof.RelativePaths.ToArray());
            Assert.AreEqual(source.Id, proof.SourceArtifactId); Assert.AreEqual(source.Fingerprint, proof.SourceFingerprint);
            if (variant != "retired") Assert.IsTrue(File.Exists(source.Files.Single(f => f.EndsWith("license.key"))));
        }
        else Assert.IsNull(proof);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Released_mixed_stage_view_keeps_its_exact_current_verification_but_never_accepts_changed_bytes(bool changed)
    {
        var (task, runId, source) = await StageCommitInput(true);
        var workspace = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspace,
            task.Id, runId, "main", task.Plan!.StageDirectories!.Parts["main"].Processing!, source, default);
        Assert.IsFalse(current.Managed);
        // Mixed views release a ledger reference; production does not quarantine or unlink borrowed bytes.
        await Db.Artifacts.Where(a => a.Id == current.Id && !a.Managed && a.State == ArtifactLocationState.Available)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Retired));
        if (changed) await File.WriteAllTextAsync(current.Files[0], new string('x', (int)current.Manifest.Entries[0].Length));
        var proof = await AcquisitionProcessingStageCommit.ResolveMovedResultAsync(Db, Artifacts, workspace, source, default);
        Assert.AreEqual(!changed, proof != null);
        var subset = (await Artifacts.GetArtifactAsync(current.ParentArtifactIds.Single(id => id != source.Id)))!;
        Assert.AreEqual(!changed, await AcquisitionProcessingStageCommit.IsCommittedStageOutputAsync(Db, Artifacts, workspace, subset, default));
    }

    [DataTestMethod]
    [DataRow("Moving")]
    [DataRow("Quarantined")]
    [DataRow("Deleting")]
    [DataRow("Purged")]
    [DataRow("missing")]
    [DataRow("wrong-manifest")]
    [DataRow("Restored")]
    public async Task Released_stage_view_preserves_exact_subset_reference_ownership_through_verified_quarantine_only(string phase)
    {
        var (task, runId, source) = await StageCommitInput(true);
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, runId, "main", task.Plan!.StageDirectories!.Parts["main"].Processing!, source, default);
        var subset = (await Artifacts.GetArtifactAsync(current.ParentArtifactIds.Single(id => id != source.Id)))!;
        Assert.IsTrue(await Artifacts.TryQuarantineAsync(subset.Id));
        var id = Guid.NewGuid().ToString("N");
        var quarantined = Path.Combine(subset.RootDirectory, ".bakabase-quarantine", id);
        foreach (var entry in subset.Manifest.Entries)
        {
            var destination = Path.Combine(quarantined, entry.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(Path.Combine(subset.RootDirectory, entry.RelativePath), destination);
        }
        await Artifacts.SetLocationAsync(subset.Id, quarantined,
            phase == "Purged" ? ArtifactLocationState.Retired : ArtifactLocationState.Quarantined);
        var receipt = new ArchiveQuarantineService.Receipt {Id = id, ArtifactId = subset.Id,
            OriginalRoot = "absolute:" + subset.RootDirectory, QuarantineRoot = "absolute:" + quarantined,
            Managed = true, Status = phase, Manifest = subset.Manifest};
        if (phase == "wrong-manifest") receipt.Manifest = receipt.Manifest with
            {Entries = receipt.Manifest.Entries.Select(e => e with {Length = e.Length + 1}).ToArray()};
        if (phase != "missing")
        {
            var receipts = Directory.CreateDirectory(Path.Combine(_root, "appdata", "file-processing", "quarantine-receipts")).FullName;
            await File.WriteAllTextAsync(Path.Combine(receipts, id + ".json"), JsonSerializer.Serialize(receipt, WorkflowJson.Options));
        }
        Assert.AreEqual(phase is "Moving" or "Quarantined" or "Deleting" or "Purged",
            await AcquisitionProcessingStageCommit.IsOwnedViewAsync(Db, Artifacts, workspaces, current, task.Id, [runId], default));
        Assert.IsTrue(File.Exists(Path.Combine(current.RootDirectory, "license.key")));
    }

    private sealed class RelocationArchives : IArchiveExtractionService
    {
        public Task<string?> ProbeFormatAsync(string file, CancellationToken ct) =>
            Task.FromResult<string?>(file.EndsWith(".bin", StringComparison.Ordinal) ? ".zip" : null);
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "first.txt"), "first", ct);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "second.txt"), "second", ct);
            return new(true, request.Directory, "");
        }
    }
}

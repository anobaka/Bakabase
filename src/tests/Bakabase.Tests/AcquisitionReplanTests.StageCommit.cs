using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Microsoft.EntityFrameworkCore;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private async Task<(AcquisitionPlanTaskDetail Task, int RunId, ArtifactRecord Source)> StageCommitInput(bool reused, bool extracted = false, bool emptyDirectory = false)
    {
        var (task, run, _) = await Failed();
        var stage = Directory.CreateDirectory(Path.Combine(_root, "canonical-processing")).FullName;
        task = await BindCurrentStage(task, "processing", stage);
        var source = Directory.CreateDirectory(Path.Combine(_root, "private-committed-result")).FullName;
        var image = Path.Combine(source, "images", "one.jpg"); Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        await File.WriteAllTextAsync(image, "image content");
        var files = new List<string> {image};
        if (extracted)
        {
            foreach (var name in Enumerable.Range(1, 10).Select(n => n + ".mp4").Append("another.rar删除"))
            {
                var item = Path.Combine(source, name);
                await File.WriteAllTextAsync(item, name);
                files.Add(item);
            }
        }
        if (reused)
        {
            var key = Path.Combine(source, "license.key"); await File.WriteAllTextAsync(key, "keep original borrowed key"); files.Add(key);
            await File.WriteAllTextAsync(Path.Combine(stage, "license.key"), "keep original borrowed key");
        }
        await File.WriteAllTextAsync(Path.Combine(stage, "unknown-extra.txt"), "not selected by processing");
        var directories = emptyDirectory ? new[] {Directory.CreateDirectory(Path.Combine(source, "extras", "empty")).FullName} : [];
        var artifact = await Artifacts.AdoptAsync(new("processed-for-stage", "stage-original-intent", "files.process", "source-fingerprint"),
            new(source, files, "processedFiles", true, MetadataJson: JsonSerializer.Serialize(
                new FileArtifactProcessingMetadata(null, "{\"requirement\":\"notRequired\"}", null, ArchiveCount: extracted ? 1 : 0,
                    StoragePolicy: new() {CleanupIntermediates = true}), WorkflowJson.Options), Directories: directories), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:process:complete", artifact.OperationId);
        return (task, run.Id, artifact);
    }

    [TestMethod]
    public async Task Stage_handoff_and_publication_keep_extraction_evidence_for_mixed_results()
    {
        var (task, runId, source) = await StageCommitInput(reused: true, extracted: true);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, runId, "main", binding, source, default);
        var publisher = new FileArtifactPublisher(Artifacts, new CompositeStorage(), () => Path.Combine(_root, "appdata"),
            _services.GetRequiredService<IServiceScopeFactory>());
        var publication = await publisher.PublishAsync("after-stage", current, Path.Combine(_root, "publication"), "test");
        foreach (var view in new[] {current, publication.Artifact!})
        {
            var extracted = await FileArtifactDeliveryAssessment.WasExtractedBeforeDeliveryAsync(view, Artifacts, default);
            Assert.IsTrue(extracted);
            var assessment = await FileArtifactDeliveryAssessment.AssessAsync(view, default, extractionWasReviewed: extracted);
            CollectionAssert.Contains(assessment.Files.ToArray(), "another.rar删除");
            Assert.IsFalse(assessment.Automatic);
        }
        var corrupted = current with {ParentArtifactIds = []};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            FileArtifactDeliveryAssessment.WasExtractedBeforeDeliveryAsync(corrupted, Artifacts, default));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stage_commit_moves_exact_outputs_preserves_borrowed_and_unknown_files_and_reuses_intent(bool reused)
    {
        var (task, runId, source) = await StageCommitInput(reused);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var result = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, runId, "main", binding, source, default);
        Assert.AreEqual(binding.Directory, result.RootDirectory);
        Assert.IsTrue(File.Exists(Path.Combine(binding.Directory, "images", "one.jpg")));
        Assert.IsFalse(File.Exists(source.Files.First(f => f.EndsWith("one.jpg"))));
        Assert.IsTrue(File.Exists(Path.Combine(binding.Directory, "unknown-extra.txt")));
        Assert.IsFalse(result.Manifest.Entries.Any(e => e.RelativePath == "unknown-extra.txt"));
        Assert.AreEqual(!reused, result.Managed);
        Assert.AreEqual(result.Id, (await Artifacts.ResolveAsync("stage-original-intent"))!.Id);
        var repeated = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, runId, "main", binding, source, default);
        Assert.AreEqual(result.Id, repeated.Id);
        if (reused)
        {
            Assert.IsTrue(await AcquisitionProcessingStageCommit.IsOwnedViewAsync(Db, Artifacts, workspaces, result, task.Id, [runId], default));
            var owned = await Artifacts.GetArtifactAsync(result.ParentArtifactIds.Single(id => id != source.Id));
            Assert.IsTrue(owned!.Managed);
            Assert.IsTrue(owned.Manifest.Entries.All(e => e.RelativePath != "license.key"));
            Assert.IsTrue(File.Exists(Path.Combine(binding.Directory, "license.key")));
            var consent = new ArtifactDeliveryCleanupConsent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
                task.Id, task.Task.PlanRevision, runId, true, [runId], [], true);
            CollectionAssert.Contains((await AcquisitionTaskCleanupViews.ReadAsync(Db, Artifacts, consent, default, workspaces)).ToArray(), result.Id);
        }
    }

    [TestMethod]
    public async Task Stage_commit_conflicting_destination_preserves_both_files_before_any_move()
    {
        var (task, runId, source) = await StageCommitInput(false);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var conflicting = Path.Combine(binding.Directory, "images", "one.jpg"); Directory.CreateDirectory(Path.GetDirectoryName(conflicting)!);
        await File.WriteAllTextAsync(conflicting, "other data");
        var failure = await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts,
            new CompositeStorage(), _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, runId,
            "main", binding, source, default));
        Assert.AreEqual("STAGE_COMMIT_TARGET_CONFLICT", failure.Message);
        Assert.AreEqual("other data", await File.ReadAllTextAsync(conflicting));
        Assert.IsTrue(source.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Stage_view_receipt_with_version_but_missing_fields_does_not_throw_or_authorize_cleanup()
    {
        var (task, runId, source) = await StageCommitInput(false);
        var invalid = source with {Managed = false, MetadataJson = "{\"stageCommit\":{\"version\":1}}"};
        Assert.IsFalse(await AcquisitionProcessingStageCommit.IsOwnedViewAsync(Db, Artifacts,
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), invalid, task.Id, [runId], default));
    }
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stage_commit_excludes_only_the_verified_current_run_and_keeps_foreign_run_references(bool foreign)
    {
        var (task, runId, source) = await StageCommitInput(false);
        var currentInput = JsonSerializer.Serialize(new {input = source.RootDirectory});
        await Db.WorkflowRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, WorkflowRunStatus.Running)
            .SetProperty(r => r.CurrentItemJson, currentInput));
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, runId));
        if (foreign)
        {
            Db.WorkflowRuns.Add(new() {Status = WorkflowRunStatus.Running,
                CurrentItemJson = JsonSerializer.Serialize(new {input = source.RootDirectory})});
            await Db.SaveChangesAsync();
        }
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var operation = AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, runId, "main", binding, source, default);
        if (foreign)
        {
            Assert.AreEqual("STAGE_COMMIT_REFERENCED", (await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => operation)).Message);
            Assert.IsTrue(source.Files.All(File.Exists));
        }
        else Assert.AreEqual(binding.Directory, (await operation).RootDirectory);
    }

    [DataTestMethod]
    [DataRow("owned")]
    [DataRow("other-part")]
    [DataRow("other-native-owner")]
    [DataRow("still-downloading")]
    [DataRow("real-file")]
    public async Task Stage_commit_distinguishes_its_download_workspace_from_external_or_concrete_references(string scenario)
    {
        var (task, runId, source) = await StageCommitInput(false);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var output = Path.Combine(_root, "original-download");
        var request = new CloudDownloadRequest("stage-transport", new() {Id = "share", ProviderId = "baidu", Url = "https://pan.baidu.com/s/test"},
            "account", null, output) {WorkspaceDirectory = binding.Directory};
        Db.DownloadTasks.Add(new() {Id = 88, Key = "download", ThirdPartyId = ThirdPartyId.BaiduNetdisk, DownloadPath = output,
            Options = JsonSerializer.Serialize(request, WorkflowJson.Options),
            Status = scenario == "still-downloading" ? DownloadTaskDbModelStatus.InProgress : DownloadTaskDbModelStatus.Complete});
        Db.CloudDownloadOperations.Add(new() {Id = request.OperationId, WorkflowRunId = runId, DownloadTaskId = 88,
            Kind = "download", State = "started", TargetId = scenario == "other-part" ? "second" : "main"});
        if (scenario == "other-native-owner")
            Db.DownloadResultOwners.Add(new() {DownloadTaskId = 88, AcquisitionTaskId = task.Id + 1, WorkflowRunId = runId});
        if (scenario == "real-file")
            Db.DownloadTaskFiles.Add(new() {DownloadTaskId = 88, Path = Path.Combine(binding.Directory, "other.txt")});
        await Db.SaveChangesAsync();
        var operation = AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, runId, "main", binding, source, default);
        if (scenario == "owned")
            Assert.AreEqual(binding.Directory, (await operation).RootDirectory);
        else
        {
            Assert.AreEqual("STAGE_COMMIT_REFERENCED", (await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => operation)).Message);
            Assert.IsTrue(source.Files.All(File.Exists));
        }
    }

    [TestMethod]
    public async Task Runtime_directory_scope_never_excludes_an_unproven_current_run()
    {
        var (task, runId, source) = await StageCommitInput(false);
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, (int?) null));
        var reasons = new List<string>();
        var guard = new ArtifactCleanupReferenceGuard(Db, referenceFound: reasons.Add);
        Assert.IsTrue(await guard.HasReferencesAsync(source, [runId], default,
            directoryOwnerTaskId: task.Id, directoryOwnerRunId: runId));
        CollectionAssert.Contains(reasons, "invalid-directory-run-scope");
    }

    [DataTestMethod]
    [DataRow(false, "quarantine")]
    [DataRow(true, "keep")]
    public async Task Stage_commit_retains_borrowed_inputs_and_explicit_source_keep_even_after_proven_replacement(bool managed, string cleanup)
    {
        var (task, runId, source) = await StageCommitInput(false);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var original = Path.Combine(binding.Directory, "archive.zip");
        await File.WriteAllTextAsync(original, "original archive is retained");
        var input = await Artifacts.AdoptAsync(new("stage-original", "stage-original", "files.process", "original"),
            new(binding.Directory, [original], managed ? "processedFiles" : "currentFiles", managed), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", runId.ToString(), "main:process:input", input.OperationId);
        var disposition = FileDispositionProofs.Create(input, source.Manifest,
            new([new("archive.zip", source.Manifest.Entries.Select(e => e.RelativePath).ToArray())], input.Manifest, source.Manifest));
        var metadata = new FileArtifactProcessingMetadata(input.Id, "{\"requirement\":\"required\"}", null,
            StoragePolicy: new() {CleanupIntermediates = true, SourceCleanup = cleanup}, FileDisposition: disposition);
        await Db.Artifacts.Where(a => a.Id == source.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson,
            JsonSerializer.Serialize(metadata, WorkflowJson.Options)));
        source = (await Artifacts.GetArtifactAsync(source.Id))!;
        await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, runId, "main", binding, source, default);
        Assert.IsTrue(File.Exists(original), "Processing success never expands deletion permission on borrowed or explicitly kept inputs.");
    }

}

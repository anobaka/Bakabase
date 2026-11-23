using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Normal_reclaim_of_committed_stage_rechecks_external_references_and_delivery_before_unlinking_confirmed_files(
        bool replacedPriorInput, bool damagedPrivateCheckpoint)
    {
        var (task, run, raw) = await Failed();
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var stage = Path.Combine(_root, "exclusive-processing");
        task = await BindCurrentStage(task, "download", Path.Combine(_root, "download-stage"));
        task = await BindCurrentStage(task, "processing", stage, taskAllocation: true);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        await workspaces.EnsureScopedDirectoryAsync(binding.Allocation!, default);
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, SourceCleanup = "delete"};
        var privateRoot = Directory.CreateDirectory(Path.Combine(_root, "private-result")).FullName;
        var copy = Path.Combine(privateRoot, "original.bin");
        File.Copy(raw.Files.Single(), copy);
        var key = ArtifactManifestFiles.Hash("reclaim-producer");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]};
        string? damagedState = null;
        if (damagedPrivateCheckpoint)
        {
            await workspaces.ResolveOperationAsync(key, 1, raw.RootDirectory, stage, binding.Allocation, true, policy, "test", default);
            var locator = await workspaces.ResolveCheckpointAsync(FileArtifactProcessor.CheckpointKey(raw, plan), key, 1,
                raw, plan, stage, stage, policy, "test", default);
            var checkpoint = FileProcessingPlanExecutor.CheckpointInputDirectory(locator, plan, raw, workspaces.IdentityPath);
            Directory.CreateDirectory(checkpoint);
            damagedState = Path.Combine(checkpoint, "state.json");
            await File.WriteAllTextAsync(damagedState, "{");
        }
        var producer = await Artifacts.AdoptAsync(new(key, "reclaim-producer", "files.process", raw.Fingerprint),
            new(privateRoot, [copy], "processedFiles", true, [raw.Id], CreateMetadata: manifest =>
                JsonSerializer.Serialize(new FileArtifactProcessingMetadata(raw.Id,
                    damagedPrivateCheckpoint ? JsonSerializer.Serialize(plan, WorkflowJson.Options) : "{}", null,
                    CompletedThroughStepId: damagedPrivateCheckpoint ? "extract" : null,
                    WorkspaceDirectoryScope: damagedPrivateCheckpoint ? binding.Allocation : null, StoragePolicy: policy,
                    FileDisposition: FileDispositionProofs.Preserve(raw, manifest)), WorkflowJson.Options)), "fixture");
        var bindings = new ArtifactOperationBindingService(Db);
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:process:complete", producer.OperationId);
        var current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, run.Id, "main", binding, producer, default);
        var firstStage = current;
        if (replacedPriorInput)
        {
            var outputDirectory = Directory.CreateDirectory(Path.Combine(_root, "replacement-result")).FullName;
            var payload = Path.Combine(outputDirectory, "result.bin");
            await File.WriteAllTextAsync(payload, "distinct verified replacement");
            var replacement = await Artifacts.AdoptAsync(new("reclaim-second-producer", "reclaim-second", "files.process",
                firstStage.Fingerprint), new(outputDirectory, [payload], "processedFiles", true, [firstStage.Id], CreateMetadata: manifest =>
                    JsonSerializer.Serialize(new FileArtifactProcessingMetadata(firstStage.Id, "{}", null, StoragePolicy: policy,
                        FileDisposition: FileDispositionProofs.Create(firstStage, manifest, new FileLineage(
                            [new("original.bin", ["result.bin"], Replaced: true)], firstStage.Manifest, manifest))), WorkflowJson.Options)), "fixture");
            await bindings.BindAsync("workflow", run.Id.ToString(), "main:process:replacement", replacement.OperationId);
            current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
                task.Id, run.Id, "main", binding, replacement, default);
            Assert.IsFalse(File.Exists(firstStage.Files.Single()), "The stage commit recorded removal of this exact earlier artifact member.");
        }
        var publishedDirectory = Directory.CreateDirectory(Path.Combine(_root, "official")).FullName;
        var official = Path.Combine(publishedDirectory, Path.GetFileName(current.Files.Single()));
        File.Copy(current.Files.Single(), official);
        var final = await Artifacts.AdoptAsync(new("reclaim-pub", "reclaim-pub", "files.publish", current.Fingerprint),
            new(publishedDirectory, [official], "publishedFiles", false, [current.Id], CreateMetadata: manifest =>
                JsonSerializer.Serialize(new FileArtifactPublicationMetadata("reclaim-pub", current.Id, current.OperationId, current.MetadataJson,
                    FileDispositionProofs.Preserve(current, manifest)), WorkflowJson.Options)), "fixture");
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:artifact.publish", final.OperationId);
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        row.PlanJson = JsonSerializer.Serialize(task.Plan with {StoragePolicy = policy}, WorkflowJson.Options);
        row.Status = AcquisitionStatus.Completed;
        run.Status = WorkflowRunStatus.Success;
        run.CurrentItemJson = null;
        run.OutputItemsJson = null;
        run.FailedItemCount = 0;
        await Db.SaveChangesAsync();
        await Db.WorkflowRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success)
            .SetProperty(r => r.CurrentItemJson, (string?)null).SetProperty(r => r.OutputItemsJson, (string?)null));
        var userFile = Path.Combine(stage, "user.txt");
        await File.WriteAllTextAsync(userFile, "keep me");
        var maintenance = _services.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var ancestors = new PublishedArtifactAncestorCleanup(Db, Artifacts,
            new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), new CompositeStorage()),
            workspaces: workspaces, maintenance: maintenance);
        var previews = new AcquisitionStoragePolicyPreviewStore();
        async Task<AcquisitionWorkspaceCleanupPreview> Preview() =>
            await Plans.PreviewWorkspaceCleanupAsync(new([task.Id]), previews, maintenance, _ => Task.CompletedTask, default, ancestors);
        async Task<AcquisitionWorkspaceCleanupResult> Apply(AcquisitionWorkspaceCleanupPreview preview) =>
            await Plans.ReclaimWorkspacesAsync(new(preview.PreviewId, [task.Id]), previews, maintenance, _ => Task.CompletedTask, default, ancestors);
        var selection = await Preview();
        var selected = selection.Rows.Single();
        Assert.IsTrue(selected.ArtifactIds.Contains(current.Id), selected.ReasonCode ?? string.Join(", ", selected.ArtifactIds));
        Assert.AreEqual(1, selected.FileCount, $"raw {raw.Id}, private {producer.Id}, canonical {current.Id}, selected {string.Join(",", selected.ArtifactIds)}");
        if (damagedPrivateCheckpoint)
        {
            var result = await Apply(selection);
            Assert.AreEqual(1, result.RemovedFiles, string.Join(",", result.Skipped.Select(s => s.ReasonCode)));
            Assert.IsFalse(File.Exists(current.Files.Single()));
            Assert.IsTrue(File.Exists(official));
            Assert.IsTrue(File.Exists(damagedState));
            Assert.IsTrue(File.Exists(raw.Files.Single()));
            return;
        }
        if (replacedPriorInput)
        {
            // An unrelated file reusing a retired input name must survive even when its bytes match.
            File.Copy(raw.Files.Single(), firstStage.Files.Single());
            var result = await Apply(selection);
            Assert.AreEqual(1, result.RemovedFiles, string.Join(",", result.Skipped.Select(s => s.ReasonCode)));
            Assert.AreEqual(new FileInfo(official).Length, result.RemovedBytes);
            Assert.IsFalse(File.Exists(current.Files.Single()));
            Assert.IsTrue(File.Exists(firstStage.Files.Single()));
            Assert.IsTrue(File.Exists(userFile));
            Assert.IsNotNull(await Artifacts.ResolveByIdAsync(final.Id));
            var again = await Preview();
            Assert.IsFalse(again.Rows.Single().ArtifactIds.Contains(current.Id));
            Assert.IsFalse(again.Rows.Single().ArtifactIds.Contains(producer.Id), "The empty original registration is outside future selections.");
            return;
        }
        await using (var reference = await Artifacts.AcquireMaintenanceAsync())
        {
            Db.ResourcesV2.Add(new() {Path = current.Files.Single(), IsFile = true});
            await Db.SaveChangesAsync();
        }
        Assert.AreEqual(0L, (await Apply(selection)).RemovedBytes);
        Assert.IsTrue(File.Exists(current.Files.Single()));
        await Db.ResourcesV2.Where(r => r.Path == current.Files.Single()).ExecuteDeleteAsync();
        selection = await Preview();
        await File.WriteAllTextAsync(official, "changed final");
        Assert.AreEqual(0L, (await Apply(selection)).RemovedBytes);
        Assert.IsTrue(File.Exists(current.Files.Single()));
        File.Copy(raw.Files.Single(), official, true);
        selection = await Preview();
        // A file recreated at the vacated private result is outside the stage confirmation.
        File.Copy(raw.Files.Single(), copy);
        Assert.AreEqual(0L, (await Apply(selection)).RemovedBytes);
        Assert.IsTrue(File.Exists(copy));
        Assert.IsTrue(File.Exists(current.Files.Single()));
        File.Delete(copy);
        var finished = await Apply(await Preview());
        Assert.AreEqual(1, finished.RemovedFiles, string.Join(", ", finished.Skipped.Select(s => s.ReasonCode)));
        Assert.AreEqual(new FileInfo(official).Length, finished.RemovedBytes);
        Assert.IsFalse(File.Exists(current.Files.Single()));
        Assert.AreEqual("keep me", await File.ReadAllTextAsync(userFile));
        Assert.IsTrue(File.Exists(raw.Files.Single()));
        Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(current.Id))!.State);
        Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(producer.Id))!.State);
        Assert.IsNotNull(await Artifacts.ResolveByIdAsync(final.Id));
        Assert.AreEqual(AcquisitionStatus.Completed, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Status);
    }
}

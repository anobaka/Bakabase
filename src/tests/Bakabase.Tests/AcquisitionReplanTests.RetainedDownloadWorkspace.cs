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
    [DataRow("complete")]
    [DataRow("previous-version-purged-stage")]
    [DataRow("changed-original")]
    [DataRow("replaced-after-hash")]
    [DataRow("missing-original")]
    [DataRow("unpublished")]
    [DataRow("foreign-reference")]
    [DataRow("changed-private-input")]
    [DataRow("changed-official")]
    [DataRow("explicit-step-keep")]
    [DataRow("retain-intermediates")]
    public async Task Default_cleanup_of_retained_download_requires_an_independent_original_and_verified_delivery(string scenario)
    {
        var (task, run, raw) = await Failed(passwordProtectedDownload: true);
        var original = await File.ReadAllBytesAsync(raw.Files.Single());
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var stageRoot = Path.Combine(_root, "processing");
        var deliveryRoot = Path.Combine(_root, "official");
        task = await BindCurrentStage(task, "download", raw.RootDirectory);
        task = await BindCurrentStage(task, "delivery", deliveryRoot);
        task = await BindCurrentStage(task, "processing", stageRoot, taskAllocation: true);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, SourceCleanup = "keep"};
        var canonical = await AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts, new CompositeStorage(),
            workspaces, task.Id, run.Id, "main", binding, raw, new StageInputMaterializer(),
            _services.GetRequiredService<IServiceScopeFactory>(), default, policy);
        var recipe = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive",
            StoragePolicy = scenario == "explicit-step-keep" ? new() {SourceCleanup = "keep"} : null}]};
        var tool = new SelectedArchivePasswordTool("fixture-password");
        var processor = new FileArtifactProcessor(Artifacts, new(tool), new(tool), () => Path.Combine(_root, "appdata"),
            _services.GetRequiredService<IServiceScopeFactory>());
        var processing = await processor.ExecuteAsync("retained-processing", canonical, JsonSerializer.Serialize(recipe, WorkflowJson.Options),
            "fixture-password", new() {WorkspaceRootDirectory = stageRoot, BoundInputDirectory = stageRoot, WorkspaceDirectoryScope = binding.Allocation,
                StoragePolicy = policy}, "fixture", null, null, null, default, stopAfterStepId: "extract");
        Assert.IsNotNull(processing.Artifact, processing.Message);
        var processed = processing.Artifact!;
        var publisher = new FileArtifactPublisher(Artifacts, new CompositeStorage(), () => Path.Combine(_root, "appdata"),
            _services.GetRequiredService<IServiceScopeFactory>());
        var output = (await publisher.PublishAsync("retained-original-delivery", processed, deliveryRoot, "test")).Artifact!;
        var bindings = new ArtifactOperationBindingService(Db);
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:process:complete", processed.OperationId);
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:artifact.publish", output.OperationId);
        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        row.PlanJson = JsonSerializer.Serialize(task.Plan with {StoragePolicy = policy with
            { CleanupIntermediates = scenario != "retain-intermediates" }}, WorkflowJson.Options);
        if (scenario != "unpublished")
        {
            row.Status = AcquisitionStatus.Completed;
            run.Status = WorkflowRunStatus.Success;
            run.CurrentItemJson = null;
            run.OutputItemsJson = null;
            run.FailedItemCount = 0;
        }
        if (scenario == "foreign-reference") Db.ResourcesV2.Add(new() {Path = canonical.Files.Single(), IsFile = true});
        await Db.SaveChangesAsync();
        var manual = Path.Combine(stageRoot, "user-added.txt");
        await File.WriteAllTextAsync(manual, "user file");
        var maintenance = _services.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var ancestors = new PublishedArtifactAncestorCleanup(Db, Artifacts,
            new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), new CompositeStorage()),
            workspaces: workspaces, maintenance: maintenance);
        if (scenario == "previous-version-purged-stage")
        {
            var quarantine = new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), new CompositeStorage());
            var retired = await quarantine.DeleteManagedAncestorAfterDeliveryAsync(canonical.Id, [output.Id], (_, _) => Task.FromResult(true), default);
            Assert.IsTrue(retired.Completed, retired.Message);
            File.Copy(raw.Files.Single(), canonical.Files.Single());
        }
        var previews = new AcquisitionStoragePolicyPreviewStore();
        Task<AcquisitionWorkspaceCleanupPreview> Preview() =>
            Plans.PreviewWorkspaceCleanupAsync(new([task.Id]), previews, maintenance, _ => Task.CompletedTask, default, ancestors);
        var before = await Preview();
        if (scenario is "changed-original" or "missing-original")
        {
            Assert.IsTrue(before.Rows.Single().ArtifactIds.Contains(canonical.Id));
            if (scenario == "missing-original") File.Delete(raw.Files.Single());
            else await File.WriteAllBytesAsync(raw.Files.Single(), original.Select(b => (byte)(b ^ 1)).ToArray());
            var refused = await Plans.ReclaimWorkspacesAsync(new(before.PreviewId, [task.Id]), previews, maintenance,
                _ => Task.CompletedTask, default, ancestors);
            Assert.AreEqual(0, refused.RemovedFiles);
            Assert.IsFalse((await Preview()).Rows.Single().ArtifactIds.Contains(canonical.Id));
        }
        else if (scenario == "replaced-after-hash")
        {
            var checkedStage = false;
            var replaced = false;
            Task ReplaceOriginal(string path)
            {
                if (path == stageRoot) checkedStage = true;
                else if (!replaced && checkedStage && path == output.RootDirectory)
                {
                    // After the application has rehashed the retained original, replace it with
                    // identical bytes, length and mtime. Only its filesystem identity differs.
                    var originalPath = raw.Files.Single();
                    var replacement = originalPath + ".replacement";
                    File.Copy(originalPath, replacement);
                    File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(originalPath));
                    File.Move(replacement, originalPath, true);
                    replaced = true;
                }
                return Task.CompletedTask;
            }
            var result = await Plans.ReclaimWorkspacesAsync(new(before.PreviewId, [task.Id]), previews, maintenance,
                ReplaceOriginal, default, ancestors);
            Assert.IsTrue(replaced);
            Assert.AreEqual(0, result.RemovedFiles);
            Assert.IsTrue(result.Skipped.Any(s => s.ReasonCode == "files_changed"));
            var resumed = await Preview();
            Assert.IsTrue(resumed.Rows.Single().Eligible);
            var completed = await Plans.ReclaimWorkspacesAsync(new(resumed.PreviewId, [task.Id]), previews, maintenance,
                _ => Task.CompletedTask, default, ancestors);
            Assert.IsTrue(completed.RemovedFiles > 0);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(raw.Files.Single()));
        }
        else if (scenario is "changed-private-input" or "changed-official")
        {
            var modified = scenario == "changed-private-input"
                ? Directory.EnumerateFiles(stageRoot, "*", SearchOption.AllDirectories)
                    .Single(p => Path.GetFileName(Path.GetDirectoryName(p)) == "inputs") : output.Files.Single();
            var bytes = await File.ReadAllBytesAsync(modified);
            await File.WriteAllBytesAsync(modified, bytes.Select(b => (byte)(b ^ 1)).ToArray());
            var result = await Plans.ReclaimWorkspacesAsync(new(before.PreviewId, [task.Id]), previews, maintenance,
                _ => Task.CompletedTask, default, ancestors);
            Assert.AreEqual(0, result.RemovedFiles);
            Assert.IsFalse((await Preview()).Rows.Single().ArtifactIds.Contains(canonical.Id));
            await File.WriteAllBytesAsync(modified, bytes);
        }
        else if (scenario is "complete" or "previous-version-purged-stage")
        {
            var selected = before.Rows.Single();
            Assert.IsTrue(selected.ArtifactIds.Contains(canonical.Id), selected.ReasonCode);
            Assert.IsFalse(selected.ArtifactIds.Contains(raw.Id) || selected.ArtifactIds.Contains(output.Id));
            Assert.IsTrue(selected.FileCount >= 1);
            var result = await Plans.ReclaimWorkspacesAsync(new(before.PreviewId, [task.Id]), previews, maintenance,
                _ => Task.CompletedTask, default, ancestors);
            Assert.IsTrue(result.RemovedFiles >= 1, string.Join(",", result.Skipped.Select(s => s.ReasonCode)));
            Assert.AreEqual(scenario == "previous-version-purged-stage", File.Exists(canonical.Files.Single()));
            Assert.IsFalse(Directory.EnumerateFiles(stageRoot, "*", SearchOption.AllDirectories)
                .Any(p => Path.GetFileName(Path.GetDirectoryName(p)) == "inputs"));
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(raw.Files.Single()));
            Assert.IsFalse((await Preview()).Rows.Single().ArtifactIds.Contains(canonical.Id));
        }
        else Assert.IsFalse(before.Rows.Single().ArtifactIds.Contains(canonical.Id));
        if (scenario is not ("complete" or "replaced-after-hash")) Assert.IsTrue(File.Exists(canonical.Files.Single()));
        Assert.AreEqual("archive payload", await File.ReadAllTextAsync(output.Files.Single()));
        Assert.AreEqual("user file", await File.ReadAllTextAsync(manual));
    }

    [DataTestMethod]
    [DataRow("default")]
    [DataRow("keep-downloads")]
    [DataRow("include-downloads")]
    public async Task Cleanup_reclaims_private_input_after_prior_stage_cleanup_and_delivery_alias_retirement(string mode)
    {
        var (task, run, download) = await Failed(passwordProtectedDownload: true);
        var original = await File.ReadAllBytesAsync(download.Files.Single());
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var stageRoot = Path.Combine(_root, "processing");
        var deliveryRoot = Path.Combine(_root, "official");
        task = await BindCurrentStage(task, "download", download.RootDirectory);
        task = await BindCurrentStage(task, "delivery", deliveryRoot);
        task = await BindCurrentStage(task, "processing", stageRoot, taskAllocation: true);
        var stage = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, SourceCleanup = "keep"};
        var storage = new CompositeStorage();
        var scope = _services.GetRequiredService<IServiceScopeFactory>();
        var canonicalInput = await AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts, storage,
            workspaces, task.Id, run.Id, "main", stage, download, new StageInputMaterializer(), scope, default, policy);
        var movedInputId = canonicalInput.ParentArtifactIds.Single();
        var movedInputRoot = (await Artifacts.GetArtifactAsync(movedInputId))!.RootDirectory;
        var recipe = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]};
        var tool = new SelectedArchivePasswordTool("fixture-password");
        var processor = new FileArtifactProcessor(Artifacts, new(tool), new(tool), () => Path.Combine(_root, "appdata"), scope);
        var processing = await processor.ExecuteAsync("retired-input-processing", canonicalInput,
            JsonSerializer.Serialize(recipe, WorkflowJson.Options), "fixture-password", new()
            {
                WorkspaceRootDirectory = stageRoot, BoundInputDirectory = stageRoot,
                WorkspaceDirectoryScope = stage.Allocation, StoragePolicy = policy
            }, "fixture", null, null, null, default, stopAfterStepId: "extract");
        Assert.IsNotNull(processing.Artifact, processing.Message);
        var produced = processing.Artifact!;
        var bindings = new ArtifactOperationBindingService(Db);
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:process:complete", produced.OperationId);
        var stagedResult = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, storage, workspaces,
            task.Id, run.Id, "main", stage, produced, default);
        Assert.AreEqual(produced.Id, stagedResult.ParentArtifactIds.Single());
        CollectionAssert.AreEqual(new[] {canonicalInput.Id}, produced.ParentArtifactIds.ToArray());
        var publisher = new FileArtifactPublisher(Artifacts, storage, () => Path.Combine(_root, "appdata"), scope);
        var publication = (await publisher.PublishAsync("retired-input-delivery", stagedResult, deliveryRoot, "test")).Artifact!;
        Assert.AreEqual(stagedResult.Id, publication.ParentArtifactIds.Single());
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:artifact.publish", publication.OperationId);

        var row = (await Db.AcquisitionTasks.FindAsync(task.Id))!;
        row.PlanJson = JsonSerializer.Serialize(task.Plan with {StoragePolicy = policy}, WorkflowJson.Options);
        row.Status = AcquisitionStatus.Completed;
        run.Status = WorkflowRunStatus.Success;
        run.CurrentItemJson = null;
        run.OutputItemsJson = null;
        run.FailedItemCount = 0;
        await Db.SaveChangesAsync();

        var snapshot = Directory.EnumerateFiles(stageRoot, "*", SearchOption.AllDirectories)
            .Single(p => Path.GetFileName(Path.GetDirectoryName(p)) == "inputs");
        var checkpoint = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(snapshot))!, "state.json");
        var ownerMarker = Directory.EnumerateFiles(stageRoot, "workspace-owner.json", SearchOption.AllDirectories).Single();
        var manual = Path.Combine(stageRoot, "user-added.txt");
        await File.WriteAllTextAsync(manual, "user file");
        var published = await File.ReadAllBytesAsync(publication.Files.Single());
        var maintenance = _services.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var quarantine = new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), storage);
        var ancestors = new PublishedArtifactAncestorCleanup(Db, Artifacts, quarantine, workspaces: workspaces, maintenance: maintenance);
        var previews = new AcquisitionStoragePolicyPreviewStore();
        Task<AcquisitionWorkspaceCleanupPreview> Preview(string scope = "default") =>
            Plans.PreviewWorkspaceCleanupAsync(new([task.Id], OverrideRetention: scope != "default", PreserveOriginalDownloads: scope == "keep-downloads"),
                previews, maintenance, _ => Task.CompletedTask, default, ancestors);
        Task<AcquisitionWorkspaceCleanupResult> Apply(AcquisitionWorkspaceCleanupPreview preview) =>
            Plans.ReclaimWorkspacesAsync(new(preview.PreviewId, [task.Id]), previews, maintenance, _ => Task.CompletedTask, default, ancestors);

        // Model the earlier cleanup that could see the current stage but could not find this checkpoint.
        var savedCheckpoint = Path.Combine(_root, "saved-checkpoint.json");
        File.Move(checkpoint, savedCheckpoint);
        try
        {
            var first = await Preview();
            CollectionAssert.AreEqual(new[] {canonicalInput.Id}, first.Rows.Single().ArtifactIds.ToArray());
            Assert.AreEqual(1, (await Apply(first)).RemovedFiles);
            Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(canonicalInput.Id))!.State);
            Assert.AreEqual(canonicalInput.RootDirectory, (await Artifacts.GetArtifactAsync(canonicalInput.Id))!.RootDirectory);
            var retiredInput = (await Artifacts.GetArtifactAsync(movedInputId))!;
            Assert.AreEqual(ArtifactLocationState.Retired, retiredInput.State);
            Assert.AreEqual(movedInputRoot, retiredInput.RootDirectory);

            var second = await Preview();
            CollectionAssert.Contains(second.Rows.Single().ArtifactIds.ToArray(), stagedResult.Id);
            Assert.AreEqual(1, (await Apply(second)).RemovedFiles);
            Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(stagedResult.Id))!.State);
            Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(produced.Id))!.State);
        }
        finally { File.Move(savedCheckpoint, checkpoint); }
        Assert.IsTrue(File.Exists(snapshot));
        Assert.IsFalse(File.Exists(canonicalInput.Files.Single()));
        var originalReceipt = Path.Combine(workspaces.StageCleanupMetadataDirectory, "confirmed-processing-files", stage.BindingId + ".json");
        var receiptBytes = await File.ReadAllBytesAsync(originalReceipt);
        using (var receipt = JsonDocument.Parse(receiptBytes))
        {
            Assert.AreEqual("done", receipt.RootElement.GetProperty("phase").GetString());
            Assert.AreEqual(1, receipt.RootElement.GetProperty("files").GetArrayLength());
        }
        var originalCheckpoint = await File.ReadAllBytesAsync(checkpoint);
        using (var state = JsonDocument.Parse(originalCheckpoint))
            Assert.AreEqual(canonicalInput.Id, state.RootElement.GetProperty("snapshotSourceArtifactId").GetString());
        var originalMarker = await File.ReadAllBytesAsync(ownerMarker);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(snapshot));

        var preview = await Preview(mode);
        var selection = preview.Rows.Single();
        Assert.IsTrue(selection.Eligible, selection.ReasonCode);
        var includesDownload = mode == "include-downloads";
        var expectedFiles = includesDownload ? 2 : 1;
        Assert.AreEqual(expectedFiles, selection.FileCount);
        Assert.AreEqual(expectedFiles * original.LongLength, selection.Bytes);
        Assert.AreEqual(includesDownload, selection.ArtifactIds.Contains(download.Id));
        Assert.IsFalse(selection.ArtifactIds.Contains(publication.Id));

        var modified = original.ToArray();
        modified[0] ^= 1;
        await File.WriteAllBytesAsync(snapshot, modified);
        var refused = await Apply(preview);
        Assert.AreEqual(0, refused.RemovedFiles);
        CollectionAssert.AreEqual(modified, await File.ReadAllBytesAsync(snapshot));
        await File.WriteAllBytesAsync(snapshot, original);

        var verifiedDelivery = await Preview(mode);
        var changedDelivery = published.ToArray();
        changedDelivery[0] ^= 1;
        await File.WriteAllBytesAsync(publication.Files.Single(), changedDelivery);
        var rejectedDelivery = await Apply(verifiedDelivery);
        Assert.AreEqual(0, rejectedDelivery.RemovedFiles);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(snapshot));
        Assert.AreEqual(ArtifactLocationState.Available, (await Artifacts.GetArtifactAsync(publication.Id))!.State);
        await File.WriteAllBytesAsync(publication.Files.Single(), published);

        var readyAgain = await Preview(mode);
        Assert.IsTrue(readyAgain.Rows.Single().Eligible, readyAgain.Rows.Single().ReasonCode);
        var cleanup = await Apply(readyAgain);
        CollectionAssert.Contains(cleanup.CompletedTaskIds.ToArray(), task.Id);
        Assert.AreEqual(expectedFiles, cleanup.RemovedFiles);
        Assert.AreEqual(expectedFiles * original.LongLength, cleanup.RemovedBytes);
        Assert.IsFalse(File.Exists(snapshot));
        Assert.IsFalse((await Preview(mode)).Rows.Single().Eligible);
        if (includesDownload) Assert.IsFalse(File.Exists(download.Files.Single()));
        else CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(download.Files.Single()));
        CollectionAssert.AreEqual(published, await File.ReadAllBytesAsync(publication.Files.Single()));
        CollectionAssert.AreEqual(receiptBytes, await File.ReadAllBytesAsync(originalReceipt));
        CollectionAssert.AreEqual(originalCheckpoint, await File.ReadAllBytesAsync(checkpoint));
        CollectionAssert.AreEqual(originalMarker, await File.ReadAllBytesAsync(ownerMarker));
        Assert.AreEqual("user file", await File.ReadAllTextAsync(manual));
        Assert.AreEqual(AcquisitionStatus.Completed, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync(x => x.Id == task.Id)).Status);
    }
}

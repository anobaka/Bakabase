using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow("valid")]
    [DataRow("publication")]
    [DataRow("publication-zero-retirement")]
    [DataRow("publication-mixed")]
    [DataRow("publication-mixed-foreign")]
    [DataRow("final-extra")]
    [DataRow("final-changed")]
    [DataRow("receipt-missing")]
    [DataRow("receipt-incomplete")]
    [DataRow("foreign-reference")]
    [DataRow("active-consumer")]
    [DataRow("keep")]
    public async Task Stage_commit_reclaims_verified_private_inputs_without_requiring_the_moved_raw_result(string variant)
    {
        var (task, run, original) = await Failed();
        var mixed = variant.StartsWith("publication-mixed", StringComparison.Ordinal);
        if (mixed)
        {
            var originalKey = Path.Combine(original.RootDirectory, "license.key");
            await File.WriteAllTextAsync(originalKey, "borrowed license key");
            original = await Artifacts.AdoptAsync(new("input-with-key", "input-with-key", "download", "input"),
                new(original.RootDirectory, original.Files.Concat([originalKey]).ToArray(), "download", true), "fixture");
            await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:download:with-key", original.OperationId);
        }
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage-current")).FullName;
        task = await BindCurrentStage(task, "processing", stage);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        if (mixed) await File.WriteAllTextAsync(Path.Combine(stage, "license.key"), "borrowed license key");
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = variant != "keep", Materialization = "copy", SourceCleanup = "keep"};
        var recipe = new FileProcessingPlan {Requirement = "notRequired"};
        var key = ArtifactManifestFiles.Hash("stage-cleanup-input:" + task.Id);
        var location = await workspaces.ResolveOperationAsync(key, 1, original.RootDirectory, _root, null, true, policy, "Task", default);
        var execution = await new FileProcessingPlanExecutor(_continuationArchives).ExecuteAsync(recipe,
            original.RootDirectory, original.Files, Path.Combine(location, "plan"), storagePolicy: policy);
        Assert.IsTrue(execution.Completed, execution.Message);
        var stateFile = Path.Combine(Path.GetDirectoryName(execution.OutputDirectory!)!, "state.json");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), WorkflowJson.Options)!;
        var privateInput = state.Outputs["download"].First(f => Path.GetFileName(f.Path) != "license.key").Path;
        var unknown = Path.Combine(Path.GetDirectoryName(privateInput)!, "manual.key");
        await File.WriteAllTextAsync(unknown, "unknown private additions are preserved");
        var source = await Artifacts.AdoptAsync(new(key, "stage-cleanup-source", "files.process", original.Fingerprint),
            new(execution.OutputDirectory!, execution.Files, "processedFiles", true, [original.Id], CreateMetadata: manifest =>
                JsonSerializer.Serialize(new FileArtifactProcessingMetadata(original.Id, JsonSerializer.Serialize(recipe, WorkflowJson.Options), null,
                    StoragePolicy: policy, FileDisposition: FileDispositionProofs.Preserve(original, manifest)), WorkflowJson.Options)), "fixture");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:process:raw", source.OperationId);
        var current = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, run.Id, "main", binding, source, default);
        Assert.IsTrue(File.Exists(privateInput)); Assert.IsFalse(source.Files.Where(f => Path.GetFileName(f) != "license.key").Any(File.Exists));
        var receipt = Directory.GetFiles(workspaces.StageCommitMetadataDirectory, "*.json").Single();
        if (variant == "receipt-missing") File.Delete(receipt);
        if (variant == "receipt-incomplete")
        {
            var data = JsonNode.Parse(await File.ReadAllTextAsync(receipt))!;
            data["artifactId"] = null; await File.WriteAllTextAsync(receipt, data.ToJsonString());
        }
        if (variant == "final-changed") await File.WriteAllTextAsync(current.Files.Single(), new string('x', (int)current.Manifest.Entries.Single().Length));
        var extra = Path.Combine(stage, "another-user-file.key");
        if (variant == "final-extra") await File.WriteAllTextAsync(extra, "preserve");
        if (variant == "foreign-reference") {Db.ResourcesV2.Add(new() {Path = privateInput, IsFile = true}); await Db.SaveChangesAsync();}
        if (variant == "active-consumer") await Artifacts.AttachConsumerAsync(source.OperationId, "reader", TimeSpan.FromHours(1));
        if (variant.StartsWith("publication", StringComparison.Ordinal))
        {
            var publicationDirectory = Directory.CreateDirectory(Path.Combine(_root, "final-delivery")).FullName;
            foreach (var entry in current.Manifest.Entries)
            {
                var destination = Path.Combine(publicationDirectory, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(current.RootDirectory, entry.RelativePath), destination);
            }
            await File.WriteAllTextAsync(extra, "unknown stage key remains");
            var publication = await Artifacts.AdoptAsync(new("stage-cleanup-publication", "stage-cleanup-publication", "files.publish", current.Fingerprint),
                new(publicationDirectory, ArtifactManifestFiles.ResolveFiles(publicationDirectory, current.Manifest), "publishedFiles", false, [current.Id],
                    CreateMetadata: manifest => JsonSerializer.Serialize(new FileArtifactPublicationMetadata("stage-cleanup", current.Id, current.OperationId,
                        current.MetadataJson, FileDispositionProofs.Preserve(current, manifest)), WorkflowJson.Options)), "fixture");
            await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:artifact.publish", publication.OperationId);
            await Db.WorkflowRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
            var cleanup = new PublishedArtifactAncestorCleanup(Db, Artifacts,
                new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), new CompositeStorage()),
                workspaces: workspaces, maintenance: _services.GetRequiredService<ArtifactWorkspaceMaintenance>());
            if (variant == "publication-zero-retirement")
            {
                // Finish the private and canonical payload first. Only the raw, now
                // empty registration remains eligible in the requested cleanup pass.
                var preparation = await cleanup.PrepareAutomaticPrivateCleanupAsync(publication.Id, [run.Id], default);
                var canonical = (await cleanup.PreviewAsync([publication.Id], [run.Id], default)).Single(c => c.ArtifactId == current.Id);
                Assert.IsTrue((await cleanup.ReclaimAutomaticPreparedAsync(canonical, [run.Id], preparation, default)).Completed);
                Assert.AreEqual(ArtifactLocationState.Available, (await Artifacts.GetArtifactAsync(source.Id))!.State);
                await new PublishedArtifactCleanupCompletionObserver(Db).StageAsync(
                    new() {Id = run.Id, Status = WorkflowRunStatus.Success}, new object(), default);
                await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
                await cleanup.ReclaimRequestedAsync(publication.Id, default);
                Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(source.Id))!.State);
                var progress = JsonNode.Parse((await Artifacts.GetArtifactAsync(publication.Id))!.MetadataJson!)!;
                Assert.AreEqual(0, progress[PublishedArtifactCleanupCompletionObserver.ProgressKey]!["removedFiles"]!.GetValue<int>());
                Assert.IsTrue(progress[PublishedArtifactCleanupCompletionObserver.PendingKey]!.GetValue<bool>(),
                    "Zero-byte retirement still schedules reevaluation of newly released ancestors.");
                await cleanup.ReclaimRequestedAsync(publication.Id, default);
                Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count,
                    "Already-retired registrations must not perpetuate the queue.");
                Assert.IsTrue(publication.Files.All(File.Exists)); Assert.IsTrue(original.Files.All(File.Exists));
                Assert.IsTrue(File.Exists(extra)); Assert.IsTrue(File.Exists(unknown)); Assert.IsTrue(File.Exists(stateFile));
                return;
            }
            if (variant == "publication-mixed-foreign")
            {
                var foreign = new Bakabase.Modules.Workflow.Abstractions.Models.Db.WorkflowRunDbModel {Status = WorkflowRunStatus.Success};
                Db.WorkflowRuns.Add(foreign); await Db.SaveChangesAsync();
                Assert.IsTrue(await new ArtifactOperationBindingService(Db).BindAsync("workflow", foreign.Id.ToString(), "foreign:input", current.OperationId));
                await cleanup.PrepareAutomaticPrivateCleanupAsync(publication.Id, [run.Id], default);
                Assert.AreEqual(ArtifactLocationState.Available, (await Artifacts.GetArtifactAsync(current.Id))!.State);
                Assert.IsTrue(File.Exists(privateInput)); Assert.IsTrue(current.Files.All(File.Exists)); Assert.IsTrue(publication.Files.All(File.Exists));
                return;
            }
            var owned = mixed ? (await Artifacts.GetArtifactAsync(current.ParentArtifactIds.Single(id => id != source.Id)))! : current;
            IReadOnlyList<ArtifactWorkspaceCleanupResult>? prepared = mixed
                ? await cleanup.PrepareAutomaticPrivateCleanupAsync(publication.Id, [run.Id], default) : null;
            if (mixed) Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(current.Id))!.State);
            var candidates = await cleanup.PreviewAsync([publication.Id], [run.Id], default);
            var selected = candidates.Single(c => c.ArtifactId == owned.Id);
            Assert.IsTrue(selected.Eligible, selected.Reason);
            var outcome = prepared == null ? await cleanup.ReclaimAutomaticAsync(selected, [run.Id], default)
                : await cleanup.ReclaimAutomaticPreparedAsync(selected, [run.Id], prepared, default);
            Assert.IsTrue(outcome.Completed, outcome.Reason);
            Assert.IsFalse(File.Exists(privateInput)); Assert.IsFalse(owned.Files.Any(File.Exists));
            if (mixed) Assert.IsTrue(File.Exists(Path.Combine(stage, "license.key")));
            Assert.IsTrue(publication.Files.All(File.Exists)); Assert.IsTrue(original.Files.All(File.Exists));
            Assert.IsTrue(File.Exists(extra)); Assert.IsTrue(File.Exists(unknown)); Assert.IsTrue(File.Exists(stateFile));
            Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(owned.Id))!.State);
            var rawCandidate = (await cleanup.PreviewAsync([publication.Id], [run.Id], default)).Single(c => c.ArtifactId == source.Id);
            Assert.IsTrue(rawCandidate.Eligible, rawCandidate.Reason);
            var rawOutcome = await cleanup.ReclaimAutomaticAsync(rawCandidate, [run.Id], default);
            Assert.IsTrue(rawOutcome.Completed, rawOutcome.Reason);
            Assert.AreEqual(ArtifactLocationState.Retired, (await Artifacts.GetArtifactAsync(source.Id))!.State);
            Assert.IsTrue(publication.Files.All(File.Exists)); Assert.IsTrue(File.Exists(stateFile)); Assert.IsTrue(File.Exists(unknown));
            return;
        }
        var result = (await _services.GetRequiredService<ArtifactWorkspaceMaintenance>().ReclaimAsync([source.Id], default)).Single();
        var eligible = variant is "valid" or "final-extra";
        Assert.AreEqual(eligible, result.Completed, result.Reason);
        Assert.AreEqual(!eligible, File.Exists(privateInput));
        Assert.AreEqual(eligible ? 1 : 0, result.RemovedFiles);
        Assert.IsTrue(current.Files.All(File.Exists)); Assert.IsTrue(original.Files.All(File.Exists));
        Assert.IsTrue(File.Exists(stateFile)); Assert.IsTrue(File.Exists(unknown));
        if (variant == "final-extra") Assert.IsTrue(File.Exists(extra));
        if (eligible) Assert.AreEqual(ArtifactLocationState.Available, (await Artifacts.GetArtifactAsync(source.Id))!.State);
    }
}

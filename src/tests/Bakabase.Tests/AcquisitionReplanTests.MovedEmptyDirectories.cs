using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow("empty")]
    [DataRow("foreign-file")]
    [DataRow("same-content-reappears")]
    [DataRow("different-content-reappears")]
    [DataRow("changed-journal")]
    [DataRow("canonical-in-quarantine")]
    public async Task Available_moved_source_has_zero_physical_bytes_and_refuses_reappeared_paths(string scenario)
    {
        var (task, runId, source) = await StageCommitInput(false);
        var workspace = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var stage = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspace,
            task.Id, runId, "main", task.Plan!.StageDirectories!.Parts["main"].Processing!, source, default);
        var publisher = new FileArtifactPublisher(Artifacts, new CompositeStorage(), () => Path.Combine(_root, "appdata"),
            _services.GetRequiredService<IServiceScopeFactory>());
        var publication = (await publisher.PublishAsync("after-stage", stage, Path.Combine(_root, "delivery"), "test")).Artifact!;
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", runId.ToString(), "main:artifact.publish", publication.OperationId);
        var quarantine = new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), new CompositeStorage());
        var cleanup = new PublishedArtifactAncestorCleanup(Db, Artifacts, quarantine, workspaces: workspace);
        var extra = Path.Combine(source.RootDirectory, "user.txt");
        if (scenario == "foreign-file") await File.WriteAllTextAsync(extra, "mine");
        var preview = await cleanup.PreviewAsync([publication.Id], [runId], default);
        var old = preview.Single(r => r.ArtifactId == source.Id);
        Assert.IsTrue(old.Eligible, old.Reason);
        Assert.AreEqual(0, old.FileCount); Assert.AreEqual(0L, old.Bytes);
        var canonical = preview.Single(r => r.ArtifactId == stage.Id);
        Assert.IsTrue(canonical.Eligible, canonical.Reason);
        Assert.AreEqual(stage.Manifest.Entries.Count, canonical.FileCount);
        Assert.AreEqual(stage.Manifest.Entries.Sum(e => e.Length), canonical.Bytes);
        if (scenario == "canonical-in-quarantine")
        {
            var checks = 0;
            var paused = await quarantine.DeleteManagedAncestorAfterDeliveryAsync(stage.Id, [publication.Id],
                (_, _) => Task.FromResult(++checks == 1), default);
            Assert.IsFalse(paused.Completed);
            var pending = (await cleanup.PreviewAsync([publication.Id], [runId], default)).Single(c => c.ArtifactId == source.Id);
            Assert.IsFalse(pending.Eligible); Assert.AreEqual("ReferencedDescendant", pending.Reason);
            Assert.IsFalse((await cleanup.ReclaimAsync(old, [runId], default)).Completed);
            Assert.IsTrue(publication.Files.All(File.Exists));
            return;
        }
        var path = source.Files.Single();
        var recreated = scenario.EndsWith("reappears");
        if (recreated)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, scenario == "same-content-reappears" ? await File.ReadAllTextAsync(stage.Files.Single()) : "changed bytes");
            var changed = (await cleanup.PreviewAsync([publication.Id], [runId], default)).Single(r => r.ArtifactId == source.Id);
            Assert.IsFalse(changed.Eligible); Assert.AreEqual("SelectionChanged", changed.Reason);
        }
        if (scenario == "changed-journal")
        {
            var journal = Directory.GetFiles(workspace.StageCommitMetadataDirectory, "*.json").Single();
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(journal))!;
            saved["move"]!["files"]![0]!["relativePath"] = "other.jpg";
            await File.WriteAllTextAsync(journal, saved.ToJsonString());
        }
        var outcome = await cleanup.ReclaimAsync(old, [runId], default);
        Assert.AreEqual(!recreated && scenario != "changed-journal", outcome.Completed, outcome.Reason);
        Assert.AreEqual(0, outcome.RemovedFiles); Assert.AreEqual(0L, outcome.RemovedBytes);
        Assert.AreEqual(recreated, File.Exists(path)); Assert.AreEqual(scenario == "foreign-file", File.Exists(extra));
        Assert.IsTrue(stage.Files.All(File.Exists)); Assert.IsTrue(publication.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("complete")]
    [DataRow("resume-empty-quarantine")]
    [DataRow("changed-journal")]
    [DataRow("file-at-original")]
    [DataRow("missing-at-delivery")]
    [DataRow("borrowed-stage")]
    public async Task Ancestor_cleanup_accepts_missing_empty_directories_only_when_the_owned_stage_move_and_delivery_match(string scenario)
    {
        var (task, runId, source) = await StageCommitInput(scenario == "borrowed-stage", emptyDirectory: true);
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var stage = await AcquisitionProcessingStageCommit.CommitCoreAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, runId, "main", task.Plan!.StageDirectories!.Parts["main"].Processing!, source, default);
        var originalDirectory = ArtifactManifestFiles.ResolveDirectories(source.RootDirectory, source.Manifest).Single();
        Assert.IsFalse(Directory.Exists(originalDirectory));
        if (scenario == "borrowed-stage")
        {
            var proof = await AcquisitionProcessingStageCommit.ReadMovedEntriesAsync(Db, Artifacts, workspaces, source, default);
            Assert.IsNotNull(proof); Assert.AreEqual(0, proof.EmptyDirectories.Count);
            return;
        }
        var publisher = new FileArtifactPublisher(Artifacts, new CompositeStorage(), () => Path.Combine(_root, "appdata"),
            _services.GetRequiredService<IServiceScopeFactory>());
        var publication = (await publisher.PublishAsync("after-stage", stage, Path.Combine(_root, "delivery"), "test")).Artifact!;
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", runId.ToString(), "main:artifact.publish", publication.OperationId);
        var finalDirectory = ArtifactManifestFiles.ResolveDirectories(publication.RootDirectory, publication.Manifest).Single();
        var quarantine = new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), new CompositeStorage());
        var cleanup = new PublishedArtifactAncestorCleanup(Db, Artifacts, quarantine, workspaces: workspaces);
        async Task<ArtifactWorkspaceCleanupResult> Reclaim(string id)
        {
            var selected = (await cleanup.PreviewAsync([publication.Id], [runId], default)).Single(a => a.ArtifactId == id);
            Assert.IsTrue(selected.Eligible, selected.Reason);
            if (scenario == "resume-empty-quarantine" && id == source.Id)
            {
                Assert.AreEqual(0, selected.FileCount);
                Assert.AreEqual(0L, selected.Bytes);
            }
            return await cleanup.ReclaimAsync(selected, [runId], default);
        }
        var canonical = await Reclaim(stage.Id);
        Assert.IsTrue(canonical.Completed, canonical.Reason); Assert.AreEqual(1, canonical.RemovedFiles);
        if (scenario == "changed-journal")
        {
            var path = Directory.GetFiles(workspaces.StageCommitMetadataDirectory, "*.json").Single();
            var receipt = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            receipt["move"]!["emptyDirectories"] = new JsonArray("other");
            await File.WriteAllTextAsync(path, receipt.ToJsonString());
        }
        if (scenario == "file-at-original")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(originalDirectory)!);
            await File.WriteAllTextAsync(originalDirectory, "user file");
        }
        if (scenario == "missing-at-delivery") Directory.Delete(finalDirectory);
        if (scenario == "resume-empty-quarantine")
        {
            var checks = 0;
            var stopped = await quarantine.DeleteManagedAncestorAfterDeliveryAsync(source.Id, [publication.Id],
                (_, _) => Task.FromResult(++checks == 1), default, movedEntries: async (original, ct) =>
                {
                    var moved = await AcquisitionProcessingStageCommit.ReadMovedEntriesAsync(Db, Artifacts, workspaces, original, ct);
                    return moved!.RelativePaths.Concat(moved.EmptyDirectories).ToHashSet(StringComparer.Ordinal);
                });
            Assert.IsFalse(stopped.Completed);
            Assert.AreEqual(ArtifactLocationState.Quarantined, (await Artifacts.GetArtifactAsync(source.Id))!.State);
        }
        var outcome = await Reclaim(source.Id);
        Assert.AreEqual(scenario is "complete" or "resume-empty-quarantine", outcome.Completed, outcome.Reason);
        Assert.AreEqual(0, outcome.RemovedFiles); Assert.AreEqual(0L, outcome.RemovedBytes);
        Assert.AreEqual(outcome.Completed ? ArtifactLocationState.Retired : ArtifactLocationState.Available,
            (await Artifacts.GetArtifactAsync(source.Id))!.State);
        Assert.IsTrue(publication.Files.All(File.Exists));
        Assert.AreEqual(scenario != "missing-at-delivery", Directory.Exists(finalDirectory));
        if (scenario == "file-at-original") Assert.AreEqual("user file", await File.ReadAllTextAsync(originalDirectory));
    }
}

using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    [DataTestMethod]
    [DataRow("operations", "absent")]
    [DataRow("checkpoints", "absent")]
    [DataRow("operations", "missing-parent")]
    [DataRow("operations", "wrong-owner")]
    [DataRow("operations", "reappeared")]
    public async Task Direct_cleanup_requires_a_proven_absent_pinned_attempt_to_cover_its_nested_artifacts(string kind,
        string scenario)
    {
        var f = await StageDeletion();
        var resolver = new FileProcessingWorkspaceResolver(root, new Storage());
        var storageRoot = Directory.CreateDirectory(Path.Combine(root, "legacy-processing")).FullName;
        var operationId = new string('d', 64);
        const string inputFingerprint = "historical-input";
        var recipe = new FileProcessingPlan {Steps = [new() {Id = "rename", Op = "rename"}]};
        var recipeJson = JsonSerializer.Serialize(recipe, Json);
        var key = kind == "operations" ? operationId : ArtifactManifestFiles.Hash(inputFingerprint + "\n" + recipeJson);
        var attempt = await resolver.ResolveAsync(kind, key, 1, f.Download, storageRoot, default, newAttempt: true);
        var artifactRoot = Directory.CreateDirectory(Path.Combine(attempt, "plan", "result")).FullName;
        var file = Path.Combine(artifactRoot, "processed.bin");
        await File.WriteAllTextAsync(file, "registered output");
        var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(null, recipeJson, null,
            CompletedThroughStepId: "rename", StoragePolicy: new() {ProcessingWorkspaceDirectory = storageRoot}), Json);
        var artifact = await store.AdoptAsync(new(operationId, operationId, "files.process", inputFingerprint),
            new(artifactRoot, [file], "processedFiles", true, MetadataJson: metadata), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "1", "nested.part:process:history", artifact.OperationId));
        Directory.Delete(scenario == "missing-parent" ? Path.GetDirectoryName(attempt)! : attempt, recursive: true);
        if (scenario == "wrong-owner")
        {
            var owner = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(
                Path.Combine(root, "artifact-workspaces", "owner.json")));
            await File.WriteAllTextAsync(Path.Combine(storageRoot, ".bakabase-work", owner!, "workspace-owner.json"), new string('0', 32));
        }

        var plan = await f.Cleanup.PreviewAsync(f.Task, [1], true, true, default, deleteDeliveredFiles: true,
            directDirectoryDeletion: true);
        var confirmedAbsent = scenario is "absent" or "reappeared";
        var group = plan.Summary.Groups.Single(g => g.Directory == (confirmedAbsent ? attempt : artifactRoot));
        Assert.AreEqual(confirmedAbsent ? "AlreadyAbsent" : "WorkspaceUnavailable", group.ReasonCode);
        Assert.AreEqual(confirmedAbsent, group.Eligible);
        Assert.AreEqual(0, group.FileCount);
        Assert.AreEqual(confirmedAbsent, !plan.Summary.Groups.Any(g => g.Directory == artifactRoot));
        if (scenario == "reappeared")
        {
            Directory.CreateDirectory(artifactRoot);
            await File.WriteAllTextAsync(file, "new payload after preview");
        }

        var outcome = await f.Cleanup.ApplyAsync(f.Task, plan, default);

        Assert.AreEqual(scenario == "absent", outcome.Completed, string.Join(",", outcome.ReasonCodes));
        Assert.IsFalse(outcome.RemovedDirectories!.Contains(attempt));
        if (scenario == "reappeared")
        {
            CollectionAssert.Contains(outcome.ReasonCodes.ToArray(), "SelectionChanged");
            Assert.AreEqual("new payload after preview", await File.ReadAllTextAsync(file));
        }
        Assert.IsTrue(Directory.Exists(storageRoot));
        Assert.IsNotNull(await store.GetArtifactAsync(artifact.Id));
    }
}

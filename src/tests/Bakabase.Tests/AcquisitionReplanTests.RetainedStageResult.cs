using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow("valid", null)]
    [DataRow("modified-input", "STAGE_COMMIT_TARGET_CHANGED")]
    [DataRow("modified-result", "STAGE_COMMIT_SOURCE_CHANGED")]
    [DataRow("foreign-reference", "STAGE_COMMIT_REFERENCED")]
    [DataRow("wrong-result-directory", "STAGE_COMMIT_TARGET_CONFLICT")]
    [DataRow("nonfinal", "STAGE_COMMIT_TARGET_CONFLICT")]
    [DataRow("no-lineage", "STAGE_COMMIT_TARGET_CONFLICT")]
    [DataRow("unproven-child", "STAGE_COMMIT_TARGET_CONFLICT")]
    public async Task Retained_archive_blocking_a_result_directory_preserves_its_path_and_publishes_only_a_proven_checkpoint(
        string scenario, string? expectedError)
    {
        var (initial, run, _) = await Failed();
        var stage = Path.Combine(_root, "retained-stage");
        var task = await BindCurrentStage(initial, "processing", stage, taskAllocation: true);
        var directories = task.Plan!.StageDirectories!.Parts["main"];
        var binding = directories.Processing!;
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        await workspaces.EnsureScopedDirectoryAsync(binding.Allocation!, default);
        var archive = Path.Combine(stage, "images");
        await File.WriteAllTextAsync(archive, "archive");
        var extra = Path.Combine(stage, "notes.txt");
        await File.WriteAllTextAsync(extra, "user-owned");
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, SourceCleanup = "keep"};
        var input = await Artifacts.AdoptAsync(new("retained-input", "retained-input", "files.process", "source"),
            new(stage, [archive], "processedFiles", true, MetadataJson: JsonSerializer.Serialize(
                new FileArtifactProcessingMetadata(null, "{}", null, StoragePolicy: policy), WorkflowJson.Options)), "fixture");
        var bindings = new ArtifactOperationBindingService(Db);
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:process:input", input.OperationId);

        var recipe = new FileProcessingPlan
        {
            Requirement = "required",
            Steps = [new() {Id = "outer", Op = "extractArchive"}, new() {Id = "inner", Op = "extractArchive", Input = "outer"}]
        };
        var key = ArtifactManifestFiles.Hash("retained-result");
        await workspaces.ResolveOperationAsync(key, 1, stage, stage, binding.Allocation, true, policy, "part", default);
        var checkpoint = await workspaces.ResolveCheckpointAsync(FileArtifactProcessor.CheckpointKey(input, recipe),
            key, 1, input, recipe, stage, stage, policy, "part", default, stage);
        var location = Path.Combine(FileProcessingPlanExecutor.CheckpointInputDirectory(checkpoint, recipe, input, workspaces.IdentityPath),
            scenario == "wrong-result-directory" ? "other-result" : "result");
        var leaf = Path.Combine(location, "images", "one.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(leaf)!);
        await File.WriteAllTextAsync(leaf, "image");
        var outputFiles = new List<string> {leaf};
        var mappedOutput = "images/one.jpg";
        if (scenario == "unproven-child")
        {
            mappedOutput = "other.jpg";
            var other = Path.Combine(location, mappedOutput);
            await File.WriteAllTextAsync(other, "unrelated");
            outputFiles.Add(other);
        }
        var output = await Artifacts.AdoptAsync(new(key, "retained-plan", "files.process", input.Fingerprint),
            new(location, outputFiles, "processedFiles", true, scenario == "no-lineage" ? [] : [input.Id],
                CreateMetadata: manifest => JsonSerializer.Serialize(new FileArtifactProcessingMetadata(
                    input.Id, JsonSerializer.Serialize(recipe, WorkflowJson.Options), null,
                    CompletedThroughStepId: scenario == "nonfinal" ? "outer" : "inner", StoragePolicy: policy,
                    FileDisposition: FileDispositionProofs.Create(input, manifest,
                        new([new("images", [mappedOutput], Replaced: true)], input.Manifest, manifest)),
                    WorkspaceDirectoryScope: binding.Allocation), WorkflowJson.Options)), "fixture");
        if (scenario == "modified-input") await File.WriteAllTextAsync(archive, "altered");
        if (scenario == "modified-result") await File.WriteAllTextAsync(leaf, "other");
        if (scenario == "foreign-reference")
        {
            Db.ResourcesV2.Add(new() {Path = archive, IsFile = true});
            await Db.SaveChangesAsync();
        }
        var target = new CloudDownloadTarget {Id = "main", StageDirectories = directories};
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = target, ActivityConfigJson = "{}", Services = _services, Logger = NullLogger.Instance};
        Task<ArtifactRecord> Commit() => AcquisitionProcessingStageCommit.CommitAsync(context, target, output, default);

        if (expectedError == null)
        {
            var returned = await Commit();
            Assert.AreEqual(output.Id, returned.Id);
            Assert.AreEqual(output.Id, (await Commit()).Id);
            var publisher = new FileArtifactPublisher(Artifacts, new CompositeStorage(), () => Path.Combine(_root, "appdata"),
                _services.GetRequiredService<IServiceScopeFactory>());
            var published = await publisher.PublishAsync("keep-archive-publish", returned, Path.Combine(_root, "delivery"), "test");
            Assert.AreEqual("images/one.jpg", published.Artifact!.Manifest.Entries.Single().RelativePath.Replace('\\', '/'));
            Assert.AreEqual("image", await File.ReadAllTextAsync(published.Artifact.Files.Single()));
            Assert.IsTrue(await Db.ArtifactOperationBindings.AnyAsync(b => b.OwnerId == run.Id.ToString() &&
                b.Slot == "main:process:stage:" + output.Id && b.OperationId == output.OperationId));
        }
        else
        {
            var error = await Assert.ThrowsAsync<Exception>(() => Commit());
            StringAssert.Contains(error.Message, expectedError);
        }
        Assert.AreEqual(scenario == "modified-input" ? "altered" : "archive", await File.ReadAllTextAsync(archive));
        Assert.AreEqual(scenario == "modified-result" ? "other" : "image", await File.ReadAllTextAsync(leaf));
        Assert.AreEqual("user-owned", await File.ReadAllTextAsync(extra));
    }
}

using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactStorageExecutionTests
{
    private string root = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    [TestInitialize] public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "storage-execution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [TestMethod]
    public async Task New_readable_placement_keeps_existing_locator_when_policy_and_title_change()
    {
        var app = Path.Combine(root, "app");
        var resolver = new FileProcessingWorkspaceResolver(app);
        var policy = new ArtifactStoragePolicy();
        var first = await resolver.ResolveAsync("operations", Key, 1, root, null, default, policy, "A readable title");
        StringAssert.Contains(first, Path.Combine("operations", "A readable title", "operation-" + Key, "attempt-1"));
        var changed = policy with {ProcessingDirectoryTemplate = "changed/{OperationId}/{Attempt}"};
        Assert.AreEqual(first, await resolver.ResolveAsync("operations", Key, 1, root, null, default, changed, "Different"));
        var legacy = await resolver.ResolveAsync("operations", Key, 2, root, null, default);
        Assert.AreEqual(legacy, await resolver.ResolveAsync("operations", Key, 2, root, null, default, policy, "A readable title"));
        var noKind = policy with {ProcessingDirectoryTemplate = "{OperationId}/{Attempt}"};
        var operation = await resolver.ResolveAsync("operations", Key, 3, root, null, default, noKind, "Title");
        var checkpoint = await resolver.ResolveAsync("checkpoints", Key, 3, root, null, default, noKind, "Title");
        Assert.AreNotEqual(operation, checkpoint, "System-owned namespaces cannot be removed by a user template.");
    }

    [TestMethod]
    public void Policy_overrides_never_change_legacy_checkpoint_or_intent_identity()
    {
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "name", Op = "renameFile", TargetName = "new.txt"}]};
        var revised = plan with {Steps = [plan.Steps[0] with {StoragePolicy = new() {CleanupIntermediates = false, ProcessingDirectoryTemplate = "custom/{OperationId}/{Attempt}"}}]};
        Assert.AreEqual(JsonSerializer.Serialize(plan, Json), FileProcessingPlanExecutor.IdentityJson(revised));
        Assert.AreEqual(FileProcessingPlanExecutor.CheckpointIdentity(plan, ["/input/file"]),
            FileProcessingPlanExecutor.CheckpointIdentity(revised, ["/input/file"]));
        Assert.AreEqual(FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(plan, Json), null, new()),
            FileArtifactProcessor.Intent("source", JsonSerializer.Serialize(revised, Json), null,
                new() {StoragePolicy = new() {CleanupIntermediates = false}, StorageTitle = "Changed"}));
    }

    [TestMethod]
    public async Task Step_keep_override_is_frozen_and_inherited_by_later_dynamic_children()
    {
        var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
        var original = Path.Combine(input, "original.txt"); await File.WriteAllTextAsync(original, "payload");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "rename", Op = "renameFile", TargetName = "renamed.txt", StoragePolicy = new() {CleanupIntermediates = false}},
            new() {Id = "move", Op = "moveFile", Input = "rename", TargetDirectory = "nested"}]};
        var executor = new FileProcessingPlanExecutor(new NoExtraction());
        var result = await executor.ExecuteAsync(plan, input, [original], Path.Combine(root, "state"),
            storagePolicy: new(), storageTitle: "Resource");
        Assert.IsTrue(result.Completed);
        var stateFile = Path.Combine(Path.GetDirectoryName(result.OutputDirectory!)!, "state.json");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        Assert.IsFalse(state.StepPolicies["move"].CleanupIntermediates);
        Assert.IsTrue(result.FilePolicies!.Values.All(p => !p.CleanupIntermediates));
        Assert.AreEqual(0, (await FileProcessingPlanExecutor.GetReclaimableFilesAsync(result.OutputDirectory)).Count);
        var changed = plan with {Steps = plan.Steps.Select(s => s with {StoragePolicy = new() {CleanupIntermediates = true}}).ToList()};
        var replay = await executor.ExecuteAsync(changed, input, [original], Path.Combine(root, "state"),
            storagePolicy: new(), storageTitle: "Changed");
        Assert.AreEqual(result.OutputDirectory, replay.OutputDirectory);
        Assert.IsTrue(replay.FilePolicies!.Values.All(p => !p.CleanupIntermediates));
        Assert.AreEqual("payload", await File.ReadAllTextAsync(original));
    }

    [TestMethod]
    public async Task Explicit_child_cleanup_override_can_reclaim_only_its_owned_output_under_parent_keep()
    {
        var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
        var original = Path.Combine(input, "original.txt"); await File.WriteAllTextAsync(original, "payload");
        var executor = new FileProcessingPlanExecutor(new NoExtraction());
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "rename", Op = "renameFile", TargetName = "renamed.txt", StoragePolicy = new() {CleanupIntermediates = true}}]};
        var result = await executor.ExecuteAsync(plan, input, [original], Path.Combine(root, "state"),
            storagePolicy: new() {CleanupIntermediates = false}, storageTitle: "Resource");
        var paths = await FileProcessingPlanExecutor.GetReclaimableFilesAsync(result.OutputDirectory);
        Assert.AreEqual(1, paths.Count);
        StringAssert.Contains(paths[0], Path.DirectorySeparatorChar + "outputs" + Path.DirectorySeparatorChar);
        var manifest = await ArtifactManifestFiles.CaptureAsync(result.OutputDirectory!, result.Files);
        await executor.ReclaimCompletedIntermediatesAsync(result.OutputDirectory, manifest, paths.ToHashSet(FileProcessingFiles.PathComparer));
        Assert.IsFalse(File.Exists(paths[0]));
        Assert.IsTrue(File.Exists(original));
        Assert.IsTrue(File.Exists(result.Files[0]));
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
            Path.Combine(Path.GetDirectoryName(result.OutputDirectory!)!, "state.json")), Json)!;
        Assert.IsTrue(state.Outputs["download"].All(a => File.Exists(a.Path)));
    }

    private sealed class NoExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct) =>
            throw new AssertFailedException("Rename test must not extract.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct) =>
            throw new AssertFailedException("Rename test must not extract.");
    }
}

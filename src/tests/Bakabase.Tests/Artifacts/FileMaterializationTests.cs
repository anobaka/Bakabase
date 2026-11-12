using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileMaterializationTests
{
    private string root = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize] public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "file-materialization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private async Task<string> Input()
    {
        var input = Path.Combine(root, "downloads"); Directory.CreateDirectory(input);
        var file = Path.Combine(input, "original.bin"); await File.WriteAllTextAsync(file, "immutable original");
        return file;
    }
    private static FileProcessingPlan Plan(bool branch = false) => new() {Requirement = "required", Steps = [
        new() {Id = "rename", Op = "renameFile", TargetName = "renamed.bin"},
        new() {Id = "finish", Op = "renameFile", Input = branch ? "download" : "rename", TargetName = "finished.bin"}]};

    [TestMethod]
    public async Task Unsupported_clone_falls_back_without_changing_or_linking_the_original()
    {
        var source = await Input(); var target = Path.Combine(root, "copy.bin"); var calls = 0;
        var files = new FileArtifactMaterializer((_, _) => { calls++; return false; });
        var result = await files.MaterializeAsync(source, target, "auto", default);
        Assert.AreEqual("copy", result.Method); Assert.AreEqual(1, calls);
        await File.WriteAllTextAsync(target, "changed destination");
        Assert.AreEqual("immutable original", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public async Task Copy_policy_skips_clone_and_never_overwrites_an_existing_destination()
    {
        var source = await Input(); var target = Path.Combine(root, "copy.bin");
        var files = new FileArtifactMaterializer((_, _) => throw new AssertFailedException("Copy mode must not clone."));
        Assert.AreEqual("copy", (await files.MaterializeAsync(source, target, "copy", default)).Method);
        await File.WriteAllTextAsync(target, "existing user file");
        await Assert.ThrowsAsync<IOException>(() => files.MaterializeAsync(source, target, "copy", default));
        Assert.AreEqual("existing user file", await File.ReadAllTextAsync(target));
    }

    [TestMethod]
    public async Task Native_auto_materialization_keeps_writes_independent()
    {
        var source = await Input(); var target = Path.Combine(root, "native.bin");
        var result = await new FileArtifactMaterializer().MaterializeAsync(source, target, "auto", default);
        CollectionAssert.Contains(new[] {"copy", "clone"}, result.Method);
        Assert.AreEqual("immutable original", await File.ReadAllTextAsync(target));
        await File.WriteAllTextAsync(target, "changed destination");
        Assert.AreEqual("immutable original", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public async Task Auto_boundary_reuses_committed_rename_output_and_preserves_branch_history_after_cleanup()
    {
        var original = await Input(); var materializer = new CountingMaterializer();
        var executor = new FileProcessingPlanExecutor(new NoExtraction(), materializer); var plan = Plan(branch: true);
        var stateRoot = Path.Combine(root, "state");
        var first = await executor.ExecuteAsync(plan, Path.GetDirectoryName(original)!, [original], stateRoot,
            stopAfterStepId: "rename", storagePolicy: new());
        Assert.IsTrue(first.Completed); Assert.AreEqual(2, materializer.Calls.Count,
            "Only input isolation and the renamed output need materialization; publication must reuse the output.");
        StringAssert.Contains(first.OutputDirectory!, Path.DirectorySeparatorChar + "outputs" + Path.DirectorySeparatorChar);
        Assert.IsFalse(Directory.EnumerateDirectories(stateRoot, "result-step-*", SearchOption.AllDirectories).Any());
        var firstPath = first.Files.Single();
        var final = await executor.ExecuteAsync(plan, Path.GetDirectoryName(original)!, [original], stateRoot,
            stopAfterStepId: "finish", storagePolicy: new());
        CollectionAssert.AreEquivalent(new[] {"renamed.bin", "finished.bin"}, final.Files.Select(Path.GetFileName).ToArray());
        var candidates = await FileProcessingPlanExecutor.GetReclaimableFilesAsync(final.OutputDirectory);
        CollectionAssert.DoesNotContain(candidates.ToArray(), firstPath);
        var expected = await ArtifactManifestFiles.CaptureAsync(final.OutputDirectory!, final.Files);
        await executor.ReclaimCompletedIntermediatesAsync(final.OutputDirectory, expected,
            candidates.ToHashSet(FileProcessingFiles.PathComparer));
        Assert.AreEqual("immutable original", await File.ReadAllTextAsync(firstPath));
        Assert.AreEqual("immutable original", await File.ReadAllTextAsync(original));
        var copies = materializer.Calls.Count;
        var replay = await new FileProcessingPlanExecutor(new NoExtraction(), materializer).ExecuteAsync(plan,
            Path.GetDirectoryName(original)!, [original], stateRoot, stopAfterStepId: "rename", storagePolicy: new());
        Assert.AreEqual(first.OutputDirectory, replay.OutputDirectory);
        CollectionAssert.AreEqual(first.Files.ToArray(), replay.Files.ToArray());
        Assert.AreEqual(copies, materializer.Calls.Count, "Historical replay must not rematerialize input snapshots.");
    }

    [TestMethod]
    public async Task Copy_mode_retains_a_separate_boundary_and_old_states_keep_copy_behavior()
    {
        var original = await Input(); var materializer = new CountingMaterializer(); var plan = Plan();
        var stateRoot = Path.Combine(root, "state"); var executor = new FileProcessingPlanExecutor(new NoExtraction(), materializer);
        var first = await executor.ExecuteAsync(plan, Path.GetDirectoryName(original)!, [original], stateRoot,
            stopAfterStepId: "rename", storagePolicy: new() {Materialization = "copy"});
        Assert.AreEqual(3, materializer.Calls.Count);
        StringAssert.StartsWith(Path.GetFileName(first.OutputDirectory!), "result-step-");
        var statePath = Directory.EnumerateFiles(stateRoot, "state.json", SearchOption.AllDirectories).Single();
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(statePath))!.AsObject();
        legacy.Remove("materializationVersion"); legacy.Remove("boundaryResults");
        legacy["storagePolicy"]!.AsObject().Remove("materialization");
        foreach (var policy in legacy["stepPolicies"]!.AsObject()) policy.Value!.AsObject().Remove("materialization");
        await File.WriteAllTextAsync(statePath, legacy.ToJsonString());
        materializer.Calls.Clear();
        await executor.ExecuteAsync(plan, Path.GetDirectoryName(original)!, [original], stateRoot,
            stopAfterStepId: "finish", storagePolicy: new() {Materialization = "auto"});
        Assert.IsTrue(materializer.Calls.All(mode => mode == "copy"));
        var replay = await executor.ExecuteAsync(plan, Path.GetDirectoryName(original)!, [original], stateRoot, stopAfterStepId: "rename");
        Assert.AreEqual(first.OutputDirectory, replay.OutputDirectory);
    }

    [TestMethod]
    public async Task Mixed_active_locations_require_a_layout_snapshot_instead_of_reusing_only_a_subset()
    {
        var original = await Input(); var note = Path.Combine(Path.GetDirectoryName(original)!, "note.txt");
        await File.WriteAllTextAsync(note, "keep this unrelated file");
        var plan = Plan() with {Steps = [Plan().Steps[0] with {Selector = "original.bin"}, Plan().Steps[1]]};
        var result = await new FileProcessingPlanExecutor(new NoExtraction()).ExecuteAsync(plan,
            Path.GetDirectoryName(original)!, [original, note], Path.Combine(root, "state"), stopAfterStepId: "rename", storagePolicy: new());
        StringAssert.StartsWith(Path.GetFileName(result.OutputDirectory!), "result-step-");
        CollectionAssert.AreEquivalent(new[] {"renamed.bin", "note.txt"}, result.Files.Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public async Task Shared_branch_inputs_inherit_copy_when_any_parent_requires_it()
    {
        var original = await Input(); var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "copyBranch", Op = "renameFile", TargetName = "copy.bin", StoragePolicy = new() {Materialization = "copy"}},
            new() {Id = "autoBranch", Op = "renameFile", TargetName = "auto.bin"},
            new() {Id = "join", Op = "moveFile", Inputs = ["copyBranch", "autoBranch"], TargetDirectory = "joined"}]};
        var result = await new FileProcessingPlanExecutor(new NoExtraction()).ExecuteAsync(plan,
            Path.GetDirectoryName(original)!, [original], Path.Combine(root, "state"), storagePolicy: new());
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
            Path.Combine(Path.GetDirectoryName(result.OutputDirectory!)!, "state.json")), Json)!;
        Assert.AreEqual("copy", state.StepPolicies["join"].Materialization);
        Assert.IsTrue(result.FilePolicies!.Values.All(p => p.Materialization == "copy"));
    }

    private sealed class CountingMaterializer : IFileArtifactMaterializer
    {
        public List<string> Calls { get; } = [];
        public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {
            Calls.Add(mode);
            return await new FileArtifactMaterializer().MaterializeAsync(source, target, "copy", ct);
        }
    }
    private sealed class NoExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct) =>
            throw new AssertFailedException("A rename-only fixture must not open archives.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct) =>
            throw new AssertFailedException("A rename-only fixture must not extract archives.");
    }
}

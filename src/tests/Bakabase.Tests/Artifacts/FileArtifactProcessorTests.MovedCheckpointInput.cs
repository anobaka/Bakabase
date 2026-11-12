using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    private async Task<(ArtifactRecord Input, string Plan, FileArtifactProcessingConfig Config,
        ArtifactRecord Previous, string Boundary, FileArtifactCheckpointContinuation Proof)> MovedBoundary(
        bool companion = false, bool chain = false, bool emptyDirectory = false)
    {
        var input = await Input();
        if (companion || emptyDirectory)
        {
            var files = input.Files.ToList();
            if (companion)
            {
                var key = Path.Combine(input.RootDirectory, "key.dat"); await File.WriteAllTextAsync(key, "needed-key");
                files.Add(key);
            }
            var empty = Path.Combine(input.RootDirectory, "empty");
            if (emptyDirectory) Directory.CreateDirectory(empty);
            input = await _store.AdoptAsync(new("download-complete-baseline", "download-complete-baseline", "download", "with-key-or-directory"),
                new(input.RootDirectory, files, Managed: true, Directories: emptyDirectory ? [empty] : null), "download-owner");
        }
        var steps = new List<FileProcessingStep>
        {
            new() {Id = "move", Op = "renameFile", Selector = "single.zip", TargetName = chain ? "middle.zip" : "ready.zip"}
        };
        if (chain) steps.Add(new() {Id = "move-again", Input = "move", Op = "renameFile", Selector = "middle.zip", TargetName = "ready.zip"});
        var boundary = chain ? "move-again" : "move";
        steps.Add(new() {Id = "open", Input = boundary, Op = "extractArchive", Selector = "ready.zip"});
        var plan = JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "required", Steps = steps}, Json);
        var workspace = Path.Combine(_root, "local-work"); Directory.CreateDirectory(workspace);
        var config = Config with {WorkspaceRootDirectory = workspace};
        var first = await _processor.ExecuteAsync(Intent(plan) + ":boundary:" + boundary, input, plan, null,
            config, "first-node", null, null, null, default, stopAfterStepId: boundary);
        Assert.IsNotNull(first.Artifact, first.Message);
        var proof = await _processor.ReadCheckpointContinuationAsync(Intent(plan), input, plan, null,
            config, first.Artifact, boundary, default);
        Assert.IsNotNull(proof);
        Assert.IsNotNull(proof.Input.MovedInputs);
        Assert.IsFalse(File.Exists(Path.Combine(proof.Input.Directory, "inputs", "single.zip")));
        Assert.IsTrue(Directory.Exists(Path.Combine(proof.Input.Directory, "inputs")));
        return (input, plan, config, first.Artifact, boundary, proof);
    }

    [TestMethod]
    public async Task Committed_private_move_continues_with_complete_baseline_and_no_remote_original()
    {
        var fixture = await MovedBoundary(companion: true);
        Directory.Delete(fixture.Input.RootDirectory, true);
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "open",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNotNull(result.Artifact, result.Message);
        CollectionAssert.AreEquivalent(new[] {"payload.txt", "key.dat"}, result.Artifact.Files.Select(Path.GetFileName).ToArray());
        Assert.AreEqual("needed-key", await File.ReadAllTextAsync(result.Artifact.Files.Single(f => Path.GetFileName(f) == "key.dat")));
        Assert.AreEqual(1, _archives.Extractions);
        Assert.IsFalse(Directory.Exists(fixture.Input.RootDirectory));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Proof.Input.Directory, "inputs", "single.zip")));
    }

    [TestMethod]
    public async Task Multiple_committed_moves_trace_original_bytes_without_copying_intermediate_names_back()
    {
        var fixture = await MovedBoundary(chain: true);
        Assert.AreEqual("ready.zip", Path.GetFileName(fixture.Proof.Input.MovedInputs!.Single().CurrentPath));
        Directory.Delete(fixture.Input.RootDirectory, true);
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "open",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNotNull(result.Artifact, result.Message);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Artifact.Files.Single()));
        Assert.AreEqual(1, _archives.Extractions);
    }

    [TestMethod]
    public async Task Moved_baseline_hash_is_verified_even_if_length_and_mtime_are_unchanged()
    {
        var fixture = await MovedBoundary();
        var path = fixture.Proof.Input.MovedInputs!.Single().CurrentPath;
        var time = File.GetLastWriteTimeUtc(path); await File.WriteAllTextAsync(path, "broken"); File.SetLastWriteTimeUtc(path, time);
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "open",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNull(result.Artifact); Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.AreEqual(0, _archives.Extractions);
        Assert.AreEqual("broken", await File.ReadAllTextAsync(path));
        Assert.AreEqual("single", await File.ReadAllTextAsync(fixture.Input.Files.Single()));
    }

    [TestMethod]
    public async Task Valid_boundary_copy_does_not_replace_missing_original_companion_baseline()
    {
        var fixture = await MovedBoundary(companion: true);
        var key = fixture.Proof.Input.MovedInputs!.Single(e => e.InputRelativePath == "key.dat").CurrentPath;
        File.Delete(key);
        Assert.IsTrue(fixture.Previous.Files.Any(f => Path.GetFileName(f) == "key.dat" && File.Exists(f)));
        Assert.IsNull(await _processor.ReadCheckpointContinuationAsync(Intent(fixture.Plan), fixture.Input,
            fixture.Plan, null, fixture.Config, fixture.Previous, fixture.Boundary, default));
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "open",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNull(result.Artifact); Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.IsFalse(File.Exists(key)); Assert.AreEqual(0, _archives.Extractions);
    }

    [TestMethod]
    public async Task Uncommitted_move_and_missing_step_output_cannot_grant_baseline_trust()
    {
        var fixture = await MovedBoundary();
        var file = Path.Combine(fixture.Proof.Input.Directory, "state.json");
        var original = await File.ReadAllTextAsync(file);
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(original, Json)!;
        state.Operations.Values.Single(o => o.RenameFiles != null).Completed = false;
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(state, Json));
        Assert.IsNull(await _processor.ReadCheckpointContinuationAsync(Intent(fixture.Plan), fixture.Input,
            fixture.Plan, null, fixture.Config, fixture.Previous, fixture.Boundary, default));
        state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(original, Json)!;
        state.Outputs.Remove(fixture.Boundary);
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(state, Json));
        Assert.IsNull(await _processor.ReadCheckpointContinuationAsync(Intent(fixture.Plan), fixture.Input,
            fixture.Plan, null, fixture.Config, fixture.Previous, fixture.Boundary, default));
    }

    [TestMethod]
    public async Task Changed_move_receipt_after_read_is_rejected_under_executor_gate()
    {
        var fixture = await MovedBoundary();
        var file = Path.Combine(fixture.Proof.Input.Directory, "state.json");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(file), Json)!;
        var operation = state.Operations.Values.Single(o => o.RenameFiles != null);
        operation.RenameFiles![0] = operation.RenameFiles[0] with {Move = false};
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(state, Json));
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "open",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNull(result.Artifact); Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.AreEqual(0, _archives.Extractions);
    }

    [TestMethod]
    public async Task Unrecorded_private_input_cannot_be_hidden_by_a_valid_moved_boundary()
    {
        var fixture = await MovedBoundary();
        var extra = Path.Combine(fixture.Proof.Input.Directory, "inputs", "manual-key.dat");
        await File.WriteAllTextAsync(extra, "keep");
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "open",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNull(result.Artifact); Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(extra)); Assert.AreEqual(0, _archives.Extractions);
    }

    [TestMethod]
    public async Task Moved_baseline_still_requires_every_declared_empty_directory()
    {
        var fixture = await MovedBoundary(emptyDirectory: true);
        var empty = Path.Combine(fixture.Proof.Input.Directory, "inputs", "empty");
        Assert.IsTrue(Directory.Exists(empty)); Directory.Delete(empty);
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "open",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNull(result.Artifact); Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.IsFalse(Directory.Exists(empty)); Assert.AreEqual(0, _archives.Extractions);
    }

    [TestMethod]
    public async Task Legacy_or_unknown_move_journal_version_cannot_gain_snapshot_trust()
    {
        var fixture = await MovedBoundary();
        var file = Path.Combine(fixture.Proof.Input.Directory, "state.json");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(file), Json)!;
        foreach (var version in new[] {0, 1, 3})
        {
            state.MaterializationVersion = version;
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(state, Json));
            Assert.IsNull(await _processor.ReadCheckpointContinuationAsync(Intent(fixture.Plan), fixture.Input,
                fixture.Plan, null, fixture.Config, fixture.Previous, fixture.Boundary, default));
        }
    }
}

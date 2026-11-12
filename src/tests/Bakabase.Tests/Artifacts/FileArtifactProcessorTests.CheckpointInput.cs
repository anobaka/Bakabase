using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    private async Task<(ArtifactRecord Input, string Plan, FileArtifactProcessingConfig Config,
        ArtifactRecord Previous, FileArtifactCheckpointContinuation Proof)> PinnedBoundary()
    {
        var input = await Input(); var plan = Plan(rename: true);
        var workspace = Path.Combine(_root, "local-work"); Directory.CreateDirectory(workspace);
        var config = Config with {WorkspaceRootDirectory = workspace};
        var first = await _processor.ExecuteAsync(Intent(plan) + ":boundary:open", input, plan, null,
            config, "first-node", null, null, null, default, stopAfterStepId: "open");
        Assert.IsNotNull(first.Artifact);
        var proof = await _processor.ReadCheckpointContinuationAsync(Intent(plan), input, plan, null,
            config, first.Artifact, "open", default);
        Assert.IsNotNull(proof);
        return (input, plan, config, first.Artifact, proof);
    }

    [TestMethod]
    public async Task Verified_complete_private_snapshot_continues_without_remote_source_or_reextraction()
    {
        var fixture = await PinnedBoundary();
        Directory.Delete(fixture.Input.RootDirectory, true);
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "rename",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNotNull(result.Artifact, result.Message);
        Assert.AreEqual("renamed.txt", Path.GetFileName(result.Artifact.Files.Single()));
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Artifact.Files.Single()));
        Assert.AreEqual(1, _archives.Extractions);
        Assert.IsFalse(Directory.Exists(fixture.Input.RootDirectory));
        Assert.AreEqual(fixture.Previous.Id, (await _store.GetArtifactAsync(fixture.Previous.Id))!.Id);
    }

    [TestMethod]
    public async Task Private_snapshot_corruption_with_unchanged_size_and_time_blocks_instead_of_refilling()
    {
        var fixture = await PinnedBoundary();
        var snapshot = Path.Combine(fixture.Proof.Input.Directory, "inputs", "single.zip");
        var time = File.GetLastWriteTimeUtc(snapshot);
        await File.WriteAllTextAsync(snapshot, "broken"); File.SetLastWriteTimeUtc(snapshot, time);
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "rename",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNull(result.Artifact);
        Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.AreEqual("broken", await File.ReadAllTextAsync(snapshot));
        Assert.AreEqual("single", await File.ReadAllTextAsync(fixture.Input.Files.Single()));
        Assert.AreEqual(1, _archives.Extractions);
    }

    [TestMethod]
    public async Task Pinned_missing_private_inputs_are_not_recreated_from_originals()
    {
        var fixture = await PinnedBoundary();
        var inputs = Path.Combine(fixture.Proof.Input.Directory, "inputs"); Directory.Delete(inputs, true);
        var result = await _processor.ExecuteAsync(Intent(fixture.Plan), fixture.Input, fixture.Plan, null,
            fixture.Config, "next-node", null, null, null, default, replan: false, stopAfterStepId: "rename",
            recovery: null, checkpointContinuation: fixture.Proof);
        Assert.IsNull(result.Artifact); Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.IsFalse(Directory.Exists(inputs)); Assert.IsTrue(File.Exists(fixture.Input.Files.Single()));
    }

    [TestMethod]
    public async Task Legacy_checkpoint_without_input_proof_and_changed_input_cannot_gain_snapshot_trust()
    {
        var fixture = await PinnedBoundary();
        var wrongInput = fixture.Input with {Id = "another-artifact"};
        Assert.IsNull(await _processor.ReadCheckpointContinuationAsync(Intent(fixture.Plan), wrongInput,
            fixture.Plan, null, fixture.Config, fixture.Previous, "open", default));
        var changed = JsonSerializer.Deserialize<FileProcessingPlan>(fixture.Plan, Json)!;
        changed = changed with {Steps = changed.Steps.Select(s => s.Id == "rename" ? s with {TargetName = "different.txt"} : s).ToList()};
        Assert.IsNull(await _processor.ReadCheckpointContinuationAsync(Intent(fixture.Plan), fixture.Input,
            JsonSerializer.Serialize(changed, Json), null, fixture.Config, fixture.Previous, "open", default));
        var stateFile = Path.Combine(fixture.Proof.Input.Directory, "state.json");
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        state.LineageVersion = 0; state.InputManifest = null;
        await File.WriteAllTextAsync(stateFile, JsonSerializer.Serialize(state, Json));
        Assert.IsNull(await _processor.ReadCheckpointContinuationAsync(Intent(fixture.Plan), fixture.Input,
            fixture.Plan, null, fixture.Config, fixture.Previous, "open", default));
    }

    [TestMethod]
    public async Task Snapshot_replay_requires_exact_active_task_run_target_and_both_bindings()
    {
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = "123"}]};
        _db.Add(run); await _db.SaveChangesAsync();
        var owner = run.Id.ToString();
        var consumer = new ArtifactConsumerDbModel {Id = "snapshot-consumer", OperationId = "original",
            ConsumerKey = AcquisitionPlanArtifactInputGuard.Consumer(run.Id, "part"), OwnerKind = "workflow", OwnerId = owner};
        var previous = new ArtifactOperationBindingDbModel {Id = "previous", OwnerKind = "workflow", OwnerId = owner,
            Slot = "part:process:open", OperationId = "processed"};
        _db.AddRange(consumer, previous, new ArtifactOperationBindingDbModel {Id = "input", OwnerKind = "workflow", OwnerId = owner,
            Slot = "part:artifact.continuation", OperationId = "original"});
        await _db.SaveChangesAsync();
        Task<bool> Valid(int task = 123, string part = "part", string operation = "original") =>
            AcquisitionPlanProcessingActivity.OwnsCheckpointBindingsAsync(_db, run.Id, task, part, operation, "processed", "open", default);
        Assert.IsTrue(await Valid());
        Assert.IsFalse(await Valid(task: 456)); Assert.IsFalse(await Valid(part: "other"));
        Assert.IsFalse(await Valid(operation: "other"));
        consumer.ReleasedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); Assert.IsFalse(await Valid());
        consumer.ReleasedAt = null; previous.OwnerId = "other-run"; await _db.SaveChangesAsync(); Assert.IsFalse(await Valid());
        previous.OwnerId = owner; run.Status = WorkflowRunStatus.Interrupted; await _db.SaveChangesAsync(); Assert.IsFalse(await Valid());
    }
}

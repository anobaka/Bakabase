using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    private async Task<(ArtifactRecord Input, string Plan, FileProcessingPlan Resolved, string LegacyRoot,
        string CheckpointRoot, string StateFile, string BaseKey)> EmptyLegacyCheckpoint()
    {
        var input = await Input(); var plan = Plan(rename: true);
        var resolved = JsonSerializer.Deserialize<FileProcessingPlan>(plan, Json)!;
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "old-volume")).FullName;
        var resolver = new FileProcessingWorkspaceResolver(_appData);
        var key = FileArtifactProcessor.CheckpointKey(input, resolved);
        var checkpoint = await resolver.ResolveAsync("checkpoints", key, 1, input.RootDirectory, legacy, default);
        var directory = Directory.CreateDirectory(Path.Combine(checkpoint,
            FileProcessingPlanExecutor.CheckpointIdentity(resolved, input.Files, resolver.IdentityPath))).FullName;
        var state = new FileProcessingPlanExecutor.State {MaterializationVersion = 2, LineageVersion = 1,
            DirectoryVersion = 1, GeneratedDirectoryVersion = 1, InputManifest = input.Manifest};
        var file = Path.Combine(directory, "state.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(state, Json));
        return (input, plan, resolved, legacy, checkpoint, file, key);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Bound_stage_input_uses_its_own_checkpoint_and_cleanup_retains_other_owners(bool sameStorageRoot)
    {
        var f = await EmptyLegacyCheckpoint();
        var resolver = new FileProcessingWorkspaceResolver(_appData);
        var oldOperation = ArtifactManifestFiles.Hash("old-acquisition");
        await resolver.ResolveAsync("operations", oldOperation, 1, f.Input.RootDirectory, f.LegacyRoot, default, newAttempt: true);
        var oldCheckpoint = (await resolver.InspectOwnedCheckpointForStageCleanupAsync(f.BaseKey, oldOperation, 1, null, default))!;
        var previousRoot = Path.GetDirectoryName(f.StateFile)!;
        var evidence = Path.Combine(previousRoot, "existing-operation.receipt");
        await File.WriteAllTextAsync(evidence, "retain unrelated progress");
        var oldState = await File.ReadAllBytesAsync(f.StateFile);

        var stage = sameStorageRoot ? f.LegacyRoot : Directory.CreateDirectory(Path.Combine(_root, "declared-stage")).FullName;
        var files = f.Input.Files.Select(file =>
        {
            var target = Path.Combine(stage, Path.GetRelativePath(f.Input.RootDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            return target;
        }).ToArray();
        var input = f.Input with {RootDirectory = stage, Files = files};
        var currentOperation = ArtifactManifestFiles.Hash("current-acquisition");
        await resolver.ResolveAsync("operations", currentOperation, 1, stage, null, default, newAttempt: true);
        var before = await resolver.InspectOwnedCheckpointForStageCleanupAsync(f.BaseKey, currentOperation, 1, null, default);
        if (sameStorageRoot) Assert.AreEqual(oldCheckpoint.Directory, before?.Directory);
        else Assert.IsNull(before, "A foreign family's path alone cannot authorize stage cleanup.");
        var preview = await resolver.InspectCheckpointAsync(f.BaseKey, currentOperation, 1, input, f.Resolved, null, default, stage);
        Assert.AreEqual(stage, preview?.StorageRoot);
        var actual = await resolver.ResolveCheckpointAsync(f.BaseKey, currentOperation, 1, input, f.Resolved, null, stage, null, null, default, stage);
        Assert.AreEqual(actual, await resolver.ReadCheckpointAsync(f.BaseKey, currentOperation, 1, null, default));
        var currentCleanup = await resolver.InspectOwnedCheckpointForStageCleanupAsync(f.BaseKey, currentOperation, 1, null, default);
        Assert.AreEqual(actual, currentCleanup?.Directory);
        Assert.AreEqual(stage, currentCleanup?.StorageRoot);
        Assert.AreEqual(oldCheckpoint.Directory, (await resolver.InspectOwnedCheckpointForStageCleanupAsync(f.BaseKey, oldOperation, 1, null, default))?.Directory);
        if (sameStorageRoot) Assert.AreEqual(f.CheckpointRoot, actual);
        else
        {
            Assert.AreNotEqual(f.CheckpointRoot, actual);
            Directory.Delete(currentCleanup!.Directory, true);
        }
        CollectionAssert.AreEqual(oldState, await File.ReadAllBytesAsync(f.StateFile));
        Assert.AreEqual("retain unrelated progress", await File.ReadAllTextAsync(evidence));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Explicit_new_local_operation_uses_pinned_storage_without_changing_unstarted_legacy_checkpoint(bool changedAfterPin)
    {
        var f = await EmptyLegacyCheckpoint();
        var chosen = Directory.CreateDirectory(Path.Combine(_root, "chosen-volume")).FullName;
        var changed = Directory.CreateDirectory(Path.Combine(_root, "later-default")).FullName;
        var requested = changedAfterPin ? changed : chosen;
        var config = new FileArtifactProcessingConfig {StoragePolicy = new() {ProcessingWorkspaceDirectory = requested}};
        var intent = FileArtifactProcessor.Intent("verified-local-input", f.Plan, null, config);
        var firstIntent = intent + ":boundary:open";
        var firstKey = FileArtifactProcessor.OperationKey(firstIntent, f.Input, f.Resolved, null, config);
        var resolver = new FileProcessingWorkspaceResolver(_appData);
        await resolver.ResolveAsync("operations", firstKey, 1, f.Input.RootDirectory, chosen, default);
        var original = await File.ReadAllBytesAsync(f.StateFile);
        var locator = Path.Combine(_appData, "artifact-workspaces", "locations", $"checkpoints-{f.BaseKey}-1.json");
        var originalLocator = await File.ReadAllBytesAsync(locator);
        var before = Directory.EnumerateFiles(Path.GetDirectoryName(locator)!).Count();
        var preview = await resolver.InspectCheckpointAsync(f.BaseKey, firstKey, 1, f.Input, f.Resolved, requested, default);
        Assert.AreEqual(f.CheckpointRoot, preview!.Directory); Assert.AreEqual(chosen, preview.StorageRoot);
        Assert.AreEqual(before, Directory.EnumerateFiles(Path.GetDirectoryName(locator)!).Count(), "Recovery preview must remain read-only.");
        var first = await _processor.ExecuteAsync(firstIntent, f.Input, f.Plan, null, config,
            "workflow:1:part", null, null, null, default, stopAfterStepId: "open");
        Assert.IsNotNull(first.Artifact, first.Message); StringAssert.StartsWith(first.Artifact.RootDirectory, chosen);
        Assert.AreEqual(1, _archives.Extractions);
        // Later dynamic nodes must find the same new namespace through the previous
        // committed operation, even though each boundary has its own operation key.
        var nextConfig = config with {StoragePolicy = config.StoragePolicy! with {ProcessingWorkspaceDirectory = chosen}};
        var proof = await _processor.ReadCheckpointContinuationAsync(intent, f.Input, f.Plan, null, nextConfig,
            first.Artifact, "open", default);
        Assert.IsNotNull(proof);
        var final = await _processor.ExecuteAsync(intent, f.Input, f.Plan, null, nextConfig, "workflow:1:part",
            null, null, null, default, false, "rename", null, checkpointContinuation: proof);
        Assert.IsNotNull(final.Artifact, final.Message); StringAssert.StartsWith(final.Artifact.RootDirectory, chosen);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(final.Artifact.Files.Single()));
        Assert.AreEqual("renamed.txt", Path.GetFileName(final.Artifact.Files.Single()));
        Assert.AreEqual(1, _archives.Extractions, "Do not repeat the already completed extraction for later nodes.");
        var replay = await _processor.ExecuteAsync(intent, f.Input, f.Plan, null, nextConfig,
            "workflow:1:part", null, null, null, default, stopAfterStepId: "rename");
        Assert.AreEqual(final.Artifact.Id, replay.Artifact!.Id);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(f.StateFile));
        CollectionAssert.AreEqual(originalLocator, await File.ReadAllBytesAsync(locator));
        Assert.AreEqual(1, Directory.EnumerateFiles(f.CheckpointRoot, "*", SearchOption.AllDirectories).Count());
        Assert.IsTrue(File.Exists(f.Input.Files.Single()));
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(changed).Any(), "A later default must not move the pinned operation.");
    }

    [DataTestMethod]
    [DataRow("outputs")]
    [DataRow("active")]
    [DataRow("pending")]
    [DataRow("password")]
    [DataRow("retained")]
    [DataRow("boundary")]
    [DataRow("extra-file")]
    [DataRow("unknown-staging-directory")]
    [DataRow("zero-byte-staging-file")]
    [DataRow("staging-symlink")]
    [DataRow("unknown-field")]
    [DataRow("missing-field")]
    [DataRow("duplicate-field")]
    [DataRow("future-version")]
    [DataRow("different-manifest")]
    public async Task Different_storage_never_silently_restarts_partial_or_unknown_checkpoint(string defect)
    {
        var f = await EmptyLegacyCheckpoint();
        var json = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
        switch (defect)
        {
            case "outputs": json["outputs"]!["open"] = new JsonArray(); break;
            case "active": json["active"] = JsonSerializer.SerializeToNode(new[] {new FileProcessingPlanExecutor.Artifact("old", "old")}, Json); break;
            case "pending": json["pendingOperation"] = "old-operation"; break;
            case "password": json["passwords"]!["old-operation"] = "fixture"; break;
            case "retained": json["retainedFiles"] = new JsonArray("old"); break;
            case "boundary": json["boundaryResults"]!["open"] = JsonSerializer.SerializeToNode(new FileProcessingPlanExecutor.BoundaryResult(), Json); break;
            case "extra-file": await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(f.StateFile)!, "unknown.bin"), "payload"); break;
            case "unknown-staging-directory": Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(f.StateFile)!, "inputs.pending", "unknown")); break;
            case "zero-byte-staging-file":
                var staging = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(f.StateFile)!, "inputs.pending")).FullName;
                await File.WriteAllBytesAsync(Path.Combine(staging, "single.zip"), []); break;
            case "staging-symlink":
                Directory.CreateSymbolicLink(Path.Combine(Path.GetDirectoryName(f.StateFile)!, "inputs.pending"), Path.Combine(_root, "absent")); break;
            case "unknown-field": json["newFutureProgress"] = true; break;
            case "missing-field": json.AsObject().Remove("completed"); break;
            case "future-version": json["directoryVersion"] = 99; break;
            case "different-manifest": json["inputManifest"]!["entries"]![0]!["length"] = 999; break;
        }
        var text = json.ToJsonString(Json);
        if (defect == "duplicate-field") text = text[..^1] + ",\"completed\":false}";
        await File.WriteAllTextAsync(f.StateFile, text);
        var current = Directory.CreateDirectory(Path.Combine(_root, "current-volume")).FullName;
        var config = new FileArtifactProcessingConfig {StoragePolicy = new() {ProcessingWorkspaceDirectory = current}};
        var intent = FileArtifactProcessor.Intent("verified-local-input", f.Plan, null, config);
        var result = await _processor.ExecuteAsync(intent, f.Input, f.Plan, null, config,
            "workflow:1:part", null, null, null, default, stopAfterStepId: "open");
        Assert.IsNull(result.Artifact); Assert.AreEqual("NeedsReconciliation", result.WaitReason, result.Message);
        StringAssert.Contains(result.Message!, "previous processing checkpoint");
        Assert.AreEqual(0, _archives.Extractions); Assert.AreEqual(text, await File.ReadAllTextAsync(f.StateFile));
        Assert.IsTrue(File.Exists(f.Input.Files.Single()));
        Assert.AreEqual(1, Directory.EnumerateFiles(Path.Combine(_appData, "artifact-workspaces", "locations"), "checkpoints-*").Count());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Legacy_null_configuration_and_same_pinned_operation_keep_original_checkpoint(bool explicitButAlreadyPinned)
    {
        var f = await EmptyLegacyCheckpoint();
        var current = Directory.CreateDirectory(Path.Combine(_root, "current-volume")).FullName;
        var config = explicitButAlreadyPinned ? new FileArtifactProcessingConfig {WorkspaceRootDirectory = current} : new();
        var intent = Intent(f.Plan); var key = FileArtifactProcessor.OperationKey(intent, f.Input, f.Resolved, null, config);
        var resolver = new FileProcessingWorkspaceResolver(_appData);
        await resolver.ResolveAsync("operations", key, 1, f.Input.RootDirectory,
            explicitButAlreadyPinned ? f.LegacyRoot : current, default);
        var result = await _processor.ExecuteAsync(intent, f.Input, f.Plan, null, config,
            "workflow:1:part", null, null, null, default, stopAfterStepId: "rename");
        Assert.IsNotNull(result.Artifact, result.Message); StringAssert.StartsWith(result.Artifact.RootDirectory, f.LegacyRoot);
        Assert.AreEqual(1, Directory.EnumerateFiles(Path.Combine(_appData, "artifact-workspaces", "locations"), "checkpoints-*").Count());
        var inspected = await resolver.InspectCheckpointAsync(f.BaseKey, key, 1, f.Input, f.Resolved,
            explicitButAlreadyPinned ? current : null, default);
        Assert.AreEqual(f.CheckpointRoot, inspected!.Directory);
        Assert.AreEqual(f.LegacyRoot, inspected.StorageRoot, "The locator retains its original storage root, not the nested checkpoint directory.");
    }

    [TestMethod]
    public async Task Snapshot_admission_failure_creates_no_staging_and_new_local_placement_can_continue_safely()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var parent = Directory.CreateDirectory(Path.Combine(source, "source-parent")).FullName;
        var original = Path.Combine(parent, "single.zip"); await File.WriteAllTextAsync(original, "single");
        var input = await _store.AdoptAsync(new("nested-download", "nested-download", "download", "source"),
            new(source, [original], "download", true), "fixture");
        var oldRoot = Directory.CreateDirectory(Path.Combine(_root, "old-volume")).FullName;
        var newRoot = Directory.CreateDirectory(Path.Combine(_root, "new-volume")).FullName;
        var guard = new StorageWriteGuard(new PlacementReservePolicy(), path =>
            path.StartsWith(oldRoot, StringComparison.Ordinal) ? ("old-volume", 99L) : ("new-volume", 1024L * 1024),
            TimeSpan.FromMilliseconds(10));
        var processor = new FileArtifactProcessor(_store, new FileProcessingPlanExecutor(_archives, writeGuard: guard),
            new RecursiveArchiveExecutor(_archives), () => _appData, _services.GetRequiredService<IServiceScopeFactory>());
        var planJson = Plan(); var plan = JsonSerializer.Deserialize<FileProcessingPlan>(planJson, Json)!;
        var oldConfig = new FileArtifactProcessingConfig {WorkspaceRootDirectory = oldRoot};
        var oldIntent = FileArtifactProcessor.Intent("original-download", planJson, null, oldConfig);
        using var scope = StorageWriteScope.Enter(new("workflow", "old-owner"));
        var waiting = await processor.ExecuteAsync(oldIntent, input, planJson, null, oldConfig,
            "old-owner", null, null, null, default, stopAfterStepId: "open");
        Assert.AreEqual("StorageUnavailable", waiting.WaitReason);
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, waiting.Message); Assert.AreEqual(0, _archives.Extractions);
        var resolver = new FileProcessingWorkspaceResolver(_appData);
        var baseKey = FileArtifactProcessor.CheckpointKey(input, plan);
        var checkpoint = (await resolver.ReadExistingAsync("checkpoints", baseKey, 1, default))!;
        var entriesBefore = Directory.EnumerateFileSystemEntries(checkpoint, "*", SearchOption.AllDirectories).Order().ToArray();
        Assert.AreEqual(0, Directory.EnumerateFiles(checkpoint, "state.json", SearchOption.AllDirectories).Count());
        Assert.AreEqual(0, Directory.EnumerateDirectories(checkpoint, "inputs*", SearchOption.AllDirectories).Count());
        Assert.AreEqual("single", await File.ReadAllTextAsync(original));
        var next = new FileArtifactProcessingConfig {StoragePolicy = new() {ProcessingWorkspaceDirectory = newRoot}};
        var nextIntent = FileArtifactProcessor.Intent("verified-local-input", planJson, null, next);
        var key = FileArtifactProcessor.OperationKey(nextIntent, input, plan, null, next);
        await resolver.ResolveAsync("operations", key, 1, input.RootDirectory, newRoot, default);
        var locatorCount = Directory.EnumerateFiles(Path.Combine(_appData, "artifact-workspaces", "locations")).Count();
        var preview = await resolver.InspectCheckpointAsync(baseKey, key, 1, input, plan, newRoot, default);
        Assert.AreEqual(checkpoint, preview!.Directory); Assert.AreEqual(newRoot, preview.StorageRoot);
        Assert.AreEqual(locatorCount, Directory.EnumerateFiles(Path.Combine(_appData, "artifact-workspaces", "locations")).Count());
        var done = await processor.ExecuteAsync(nextIntent, input, planJson, null, next,
            "new-owner", null, null, null, default, stopAfterStepId: "open");
        Assert.IsNotNull(done.Artifact, done.Message); StringAssert.StartsWith(done.Artifact.RootDirectory, newRoot);
        Assert.AreEqual(1, _archives.Extractions); Assert.IsTrue(File.Exists(original));
        CollectionAssert.AreEqual(entriesBefore, Directory.EnumerateFileSystemEntries(checkpoint, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [DataTestMethod]
    [DataRow("missing-owner")]
    [DataRow("missing-marker")]
    [DataRow("wrong-marker")]
    [DataRow("arbitrary-empty-folder")]
    [DataRow("missing-checkpoint")]
    [DataRow("linked-checkpoint")]
    public async Task Empty_checkpoint_requires_its_existing_locator_and_installation_ownership(string defect)
    {
        var input = await Input(); var plan = JsonSerializer.Deserialize<FileProcessingPlan>(Plan(), Json)!;
        var oldRoot = Directory.CreateDirectory(Path.Combine(_root, "old-empty-volume")).FullName;
        var newRoot = Directory.CreateDirectory(Path.Combine(_root, "new-empty-volume")).FullName;
        var resolver = new FileProcessingWorkspaceResolver(_appData);
        var baseKey = FileArtifactProcessor.CheckpointKey(input, plan);
        var checkpoint = await resolver.ResolveAsync("checkpoints", baseKey, 1, input.RootDirectory, oldRoot, default);
        var key = new string('e', 64);
        await resolver.ResolveAsync("operations", key, 1, input.RootDirectory, newRoot, default);
        var metadata = Path.Combine(_appData, "artifact-workspaces");
        var ownerFile = Path.Combine(metadata, "owner.json");
        var owner = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(ownerFile), Json)!;
        var marker = Path.Combine(oldRoot, ".bakabase-work", owner, "workspace-owner.json");
        switch (defect)
        {
            case "missing-owner": File.Delete(ownerFile); break;
            case "missing-marker": File.Delete(marker); break;
            case "wrong-marker": await File.WriteAllTextAsync(marker, new string('f', 32)); break;
            case "arbitrary-empty-folder":
                var foreign = Directory.CreateDirectory(Path.Combine(oldRoot, "unowned-empty")).FullName;
                var locator = Path.Combine(metadata, "locations", $"checkpoints-{baseKey}-1.json");
                var location = JsonSerializer.Deserialize<FileProcessingWorkspaceResolver.Location>(await File.ReadAllTextAsync(locator), Json)!;
                await File.WriteAllTextAsync(locator, JsonSerializer.Serialize(location with {Directory = foreign}, Json));
                break;
            case "missing-checkpoint": Directory.Delete(checkpoint); break;
            case "linked-checkpoint":
                var linked = Directory.CreateDirectory(Path.Combine(oldRoot, "other-empty")).FullName;
                Directory.Delete(checkpoint); Directory.CreateSymbolicLink(checkpoint, linked); break;
        }
        var count = Directory.EnumerateFiles(Path.Combine(metadata, "locations"), "checkpoints-*").Count();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.InspectCheckpointAsync(baseKey, key, 1, input, plan, newRoot, default));
        Assert.AreEqual(count, Directory.EnumerateFiles(Path.Combine(metadata, "locations"), "checkpoints-*").Count());
        Assert.AreEqual(0, _archives.Extractions); Assert.IsTrue(File.Exists(input.Files.Single()));
    }

    private sealed class PlacementReservePolicy : IStorageWritePolicyProvider
    {
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default)
            => Task.FromResult(context == null ? 0L : 100L);
    }
}

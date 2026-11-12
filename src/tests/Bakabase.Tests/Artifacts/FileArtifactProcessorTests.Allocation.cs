using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    private async Task<string> AddUnavailableLegacyRelocations()
    {
        var unavailable = Path.Combine(_root, "disconnected-legacy-volume");
        var metadata = Directory.CreateDirectory(Path.Combine(_appData, "artifact-workspaces")).FullName;
        await File.WriteAllTextAsync(Path.Combine(metadata, "relocations.json"),
            JsonSerializer.Serialize(new FileProcessingWorkspaceResolver.Relocations(1,
                [new(Path.Combine(_appData, "artifact-operations"), Path.Combine(unavailable, "operations")),
                    new(Path.Combine(_appData, "artifact-plan-checkpoints"), Path.Combine(unavailable, "checkpoints"))]), Json));
        return unavailable;
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task New_processing_claim_uses_current_storage_without_probing_unrelated_legacy_relocations(bool boundary)
    {
        var unavailable = await AddUnavailableLegacyRelocations();
        var root = Directory.CreateDirectory(Path.Combine(_root, "current-processing")).FullName;
        var task = Path.Combine(root, "task");
        var input = await Input(); var plan = Plan(rename: boundary);
        var config = Config with {WorkspaceDirectoryScope = new("acquisition", "1", "processing-part", root, task)};
        var result = await _processor.ExecuteAsync(Intent(plan), input, plan, null, config,
            "workflow:1:part", null, null, null, default, stopAfterStepId: boundary ? "open" : null);
        Assert.IsNotNull(result.Artifact, result.Message);
        StringAssert.StartsWith(result.Artifact.RootDirectory, task);
        Assert.AreEqual(1, _archives.Extractions);
        Assert.IsFalse(Directory.Exists(unavailable), "A new operation must not recreate or depend on historical storage.");
        Assert.IsTrue(File.Exists(input.Files.Single()));
        var repeated = await _processor.ExecuteAsync(Intent(plan), input, plan, null, config,
            "workflow:1:part", null, null, null, default, stopAfterStepId: boundary ? "open" : null);
        Assert.AreEqual(result.Artifact.Id, repeated.Artifact?.Id);
        Assert.AreEqual(1, _archives.Extractions);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task New_allocation_survives_storage_failure_before_workspace_creation_and_pins_its_original_choice(bool scoped)
    {
        await AddUnavailableLegacyRelocations();
        var selected = Path.Combine(_root, "temporarily-offline-current-storage");
        var task = Path.Combine(selected, "task");
        var scope = scoped ? new ArtifactDirectoryScope("acquisition", "1", "processing-part", selected, task) : null;
        var config = Config with {WorkspaceRootDirectory = selected, WorkspaceDirectoryScope = scope};
        var input = await Input(); var plan = Plan(); var intent = Intent(plan);
        var waiting = await _processor.ExecuteAsync(intent, input, plan, null, config,
            "workflow:1:part", null, null, null, default);
        Assert.AreEqual("StorageUnavailable", waiting.WaitReason);
        Assert.AreEqual(0, _archives.Extractions);
        Assert.IsFalse(Directory.Exists(selected));
        var operation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.Kind == "files.process");
        Assert.AreEqual(1, operation.Attempt);
        Assert.IsTrue(File.Exists(Path.Combine(_appData, "artifact-workspaces", "allocations", $"operations-{operation.Id}-1.json")));

        Directory.CreateDirectory(selected);
        var newDefault = Directory.CreateDirectory(Path.Combine(_root, "changed-default")).FullName;
        var changed = config with {WorkspaceRootDirectory = newDefault, WorkspaceDirectoryScope = scoped
            ? new("acquisition", "1", "processing-part", newDefault, Path.Combine(newDefault, "task")) : null};
        var completed = await _processor.ExecuteAsync(intent, input, plan, null, changed,
            "workflow:1:part", null, null, null, default);
        Assert.IsNotNull(completed.Artifact, completed.Message);
        StringAssert.StartsWith(completed.Artifact.RootDirectory, scoped ? task : selected);
        Assert.AreEqual(1, _archives.Extractions);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(newDefault).Any(), "Changed defaults must not replace the durable choice.");
        Assert.AreEqual(1, (await _store.GetOperationAsync(operation.Id))!.Attempt);
    }

    [TestMethod]
    public async Task Existing_unknown_operation_without_locator_is_not_assumed_to_be_a_new_attempt()
    {
        await AddUnavailableLegacyRelocations();
        var current = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var input = await Input(); var plan = Plan(); var intent = Intent(plan);
        var config = Config with {WorkspaceRootDirectory = current};
        var resolved = JsonSerializer.Deserialize<FileProcessingPlan>(plan, Json)!;
        var key = FileArtifactProcessor.OperationKey(intent, input, resolved, null, config);
        await _store.ClaimAsync(new(key, intent, "files.process", input.Fingerprint), "workflow:1:part", TimeSpan.FromMinutes(2));
        var waiting = await _processor.ExecuteAsync(intent, input, plan, null, config,
            "workflow:1:part", null, null, null, default);
        Assert.AreEqual("StorageUnavailable", waiting.WaitReason);
        Assert.AreEqual(0, _archives.Extractions);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(current).Any());
        Assert.IsFalse(Directory.Exists(Path.Combine(_appData, "artifact-workspaces", "allocations")));
    }

    [TestMethod]
    public async Task Existing_locator_remains_authoritative_even_if_new_allocation_or_settings_point_elsewhere()
    {
        var input = await Input(nested: true); var plan = Plan(nested: true);
        var original = Directory.CreateDirectory(Path.Combine(_root, "original")).FullName;
        var config = Config with {WorkspaceRootDirectory = original};
        var intent = Intent(plan);
        var initial = await _processor.ExecuteAsync(intent, input, plan, null, config,
            "workflow:1:part", null, null, null, default);
        Assert.AreEqual("PasswordUnknown", initial.WaitReason);
        var before = _archives.Extractions;
        Directory.Move(original, original + ".offline");
        var current = Directory.CreateDirectory(Path.Combine(_root, "new-default")).FullName;
        var blocked = await _processor.ExecuteAsync(intent, input, plan, null,
            config with {WorkspaceRootDirectory = current}, "workflow:1:part", null, "correct", null, default);
        Assert.AreEqual("StorageUnavailable", blocked.WaitReason);
        Assert.AreEqual(before, _archives.Extractions);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(current).Any());
        Assert.IsFalse(Directory.Exists(original));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_attempt_retry_allocates_its_new_generation_on_current_storage_even_when_legacy_storage_is_offline(bool boundary)
    {
        var unavailable = await AddUnavailableLegacyRelocations();
        var current = Directory.CreateDirectory(Path.Combine(_root, "current")).FullName;
        var input = await Input(); var plan = Plan(rename: boundary); var intent = Intent(plan);
        var config = Config with {WorkspaceRootDirectory = current};
        var resolved = JsonSerializer.Deserialize<FileProcessingPlan>(plan, Json)!;
        var key = FileArtifactProcessor.OperationKey(intent, input, resolved, null, config);
        var failed = await _store.ClaimAsync(new(key, intent, "files.process", input.Fingerprint), "workflow:old:part", TimeSpan.FromMinutes(2));
        Assert.IsTrue(await _store.FailAsync(failed.Lease!, "An earlier non-storage failure"));
        var result = await _processor.ExecuteAsync(intent, input, plan, null, config,
            "workflow:1:part", null, null, null, default, stopAfterStepId: boundary ? "open" : null);
        Assert.IsNotNull(result.Artifact, result.Message);
        StringAssert.StartsWith(result.Artifact.RootDirectory, current);
        Assert.AreEqual(1, _archives.Extractions);
        Assert.AreEqual(2, (await _store.GetOperationAsync(key))!.Attempt);
        Assert.IsTrue(File.Exists(Path.Combine(_appData, "artifact-workspaces", "allocations", $"operations-{key}-2.json")));
        Assert.IsFalse(Directory.Exists(unavailable));
        Assert.IsTrue(File.Exists(input.Files.Single()));
    }
}

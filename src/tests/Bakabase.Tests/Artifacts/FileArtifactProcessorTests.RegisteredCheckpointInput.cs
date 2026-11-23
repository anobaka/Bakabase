using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    private sealed class OpenSourceAtCopy(string sourcePath, FileStream exclusive, bool alterCopy) : IFileArtifactMaterializer
    {
        internal int SourceCopies { get; private set; }
        private readonly FileArtifactMaterializer materializer = new();

        public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {
            if (!ArtifactManifestFiles.PathComparer.Equals(source, sourcePath))
                return await materializer.MaterializeAsync(source, target, mode, ct);
            SourceCopies++;
            exclusive.Dispose();
            var result = await materializer.MaterializeAsync(source, target, "copy", ct);
            if (alterCopy)
            {
                var contents = await File.ReadAllBytesAsync(target, ct);
                contents[0] ^= 1;
                await File.WriteAllBytesAsync(target, contents, ct);
            }
            return result;
        }
    }

    [DataTestMethod]
    [DataRow("intact")]
    [DataRow("source-id")]
    [DataRow("fingerprint")]
    [DataRow("missing-manifest")]
    public async Task Registered_snapshot_boundary_uses_its_verified_copy_and_rejects_damaged_receipts(string scenario)
    {
        var original = await Input();
        var work = Path.Combine(_root, "boundary-checkpoint");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "first", Op = "renameExtension", Input = "download", Extension = ".txt"},
            new() {Id = "second", Op = "renameExtension", Input = "first", Extension = ".jpg"}]};
        var executor = new FileProcessingPlanExecutor(_archives);
        var first = await executor.ExecuteAsync(plan, original.RootDirectory, original.Files, work,
            stopAfterStepId: "first", snapshotSource: new(original, _ => Task.FromResult(original.Manifest)));
        Assert.IsTrue(first.Completed);
        var statePath = Directory.GetFiles(work, "state.json", SearchOption.AllDirectories).Single();
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(statePath), Json)!;
        Assert.AreEqual(original.Id, state.SnapshotSourceArtifactId);
        if (scenario == "source-id") state.SnapshotSourceArtifactId = Guid.NewGuid().ToString("N");
        if (scenario == "fingerprint") state.InputManifest = original.Manifest with {Fingerprint = new string('0', 64)};
        if (scenario == "missing-manifest") state.InputManifest = null;
        if (scenario != "intact") await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state, Json));
        File.Delete(original.Files.Single());
        Task<FileProcessingPlanResult> Resume() => executor.ExecuteAsync(plan, original.RootDirectory, original.Files, work,
            stopAfterStepId: "second", snapshotSource: new(original, _ => throw new AssertFailedException("The existing snapshot reopened its origin.")));
        if (scenario == "intact")
        {
            var final = await Resume();
            Assert.AreEqual("single", await File.ReadAllTextAsync(final.Files.Single()));
        }
        else await Assert.ThrowsAsync<FileProcessingInputChangedException>(Resume);
    }

    [TestMethod]
    public async Task Unregistered_plan_still_captures_its_local_input_before_materialization()
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "unregistered")).FullName;
        var file = Path.Combine(root, "raw.bin");
        await File.WriteAllTextAsync(file, "unregistered source");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "rename",
            Op = "renameExtension", Input = "download", Extension = ".txt"}]};
        var work = Path.Combine(_root, "direct-plan-state");
        using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var copies = new OpenSourceAtCopy(file, exclusive, false);
        var executor = new FileProcessingPlanExecutor(_archives, copies);
        await Assert.ThrowsAsync<IOException>(() => executor.ExecuteAsync(plan, root, [file], work));
        Assert.AreEqual(0, copies.SourceCopies);
        exclusive.Dispose();
        var complete = await executor.ExecuteAsync(plan, root, [file], work);
        Assert.IsTrue(complete.Completed);
        Assert.AreEqual("unregistered source", await File.ReadAllTextAsync(complete.Files.Single()));
    }

    [DataTestMethod]
    [DataRow("unregistered-id")]
    [DataRow("different-files")]
    [DataRow("different-manifest")]
    public async Task Registered_checkpoint_rejects_a_record_that_differs_from_the_persisted_registration(string scenario)
    {
        var registered = await Input();
        var inconsistent = scenario switch
        {
            "unregistered-id" => registered with {Id = Guid.NewGuid().ToString("N")},
            "different-files" => registered with {Files = [await FileAtSibling()]},
            _ => registered with {Fingerprint = new string('a', 64)}
        };
        var result = await Execute("mismatched-source", inconsistent, Plan());
        Assert.IsNull(result.Artifact);
        Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        Assert.AreEqual(0, _archives.Extractions);

        async Task<string> FileAtSibling()
        {
            var path = Path.Combine(registered.RootDirectory, "different.zip");
            await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(registered.Files.Single()));
            return path;
        }
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Registered_snapshot_reads_source_only_for_materialization_and_fully_checks_the_local_copy(bool alterCopy)
    {
        var from = Directory.CreateDirectory(Path.Combine(_root, "origin")).FullName;
        var path = Path.Combine(from, "note.bin");
        await File.WriteAllTextAsync(path, "registered source");
        var empty = Directory.CreateDirectory(Path.Combine(from, "empty")).FullName;
        var registered = await _store.AdoptAsync(new("source-with-directory", "source", "download", "source"),
            new(from, [path], "download", Directories: [empty]), "source-owner");
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => File.OpenRead(path).Dispose());
        var files = new OpenSourceAtCopy(path, exclusive, alterCopy);
        var processor = new FileArtifactProcessor(_store, new(_archives, files), new(_archives),
            () => _appData, _services.GetRequiredService<IServiceScopeFactory>());
        const string plan = """{"requirement":"required","steps":[{"id":"rename","op":"renameExtension","input":"download","extension":".txt"}]}""";
        var config = Config with {StoragePolicy = new() {CleanupIntermediates = false}};
        var result = await processor.ExecuteAsync("registered-copy", registered, plan, null, config, "owner",
            null, null, null, default);
        Assert.AreEqual(1, files.SourceCopies, "Before the native copy the registered source could not be opened for hashing.");
        Assert.AreEqual("registered source", await File.ReadAllTextAsync(path));
        if (alterCopy)
        {
            Assert.IsNull(result.Artifact);
            Assert.AreEqual("NeedsReconciliation", result.WaitReason);
        }
        else
        {
            Assert.IsNotNull(result.Artifact);
            Assert.AreEqual("registered source", await File.ReadAllTextAsync(result.Artifact.Files.Single()));
            Assert.IsTrue(Directory.Exists(Path.Combine(result.Artifact.RootDirectory, "empty")));
            var stateDirectory = Path.GetDirectoryName(result.Artifact.RootDirectory)!;
            var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(Path.Combine(stateDirectory, "state.json")), Json)!;
            Assert.AreEqual(registered.Id, state.SnapshotSourceArtifactId);
            Assert.IsTrue(FileDispositionProofs.SameManifest(registered.Manifest, state.InputManifest));
        }
    }
}

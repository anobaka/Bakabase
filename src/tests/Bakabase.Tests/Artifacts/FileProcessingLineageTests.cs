using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingLineageTests
{
    private string root = null!;
    private string input = null!;
    private string state = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "processing-lineage-" + Guid.NewGuid().ToString("N"));
        input = Path.Combine(root, "input"); state = Path.Combine(root, "state");
        Directory.CreateDirectory(input);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private async Task<string> Input(string name, string value)
    {
        var file = Path.Combine(input, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, value); return file;
    }
    private static FileProcessingPlan Extract(string name = "*.zip") => new()
    {
        Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Selector = name}]
    };
    private async Task<(string Path, FileProcessingPlanExecutor.State State)> ReadState()
    {
        var path = Directory.EnumerateFiles(state, "state.json", SearchOption.AllDirectories).Single();
        return (path, JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(path), Json)!);
    }

    [TestMethod]
    public async Task Extraction_proves_archive_replacement_and_preserves_unknown_companions()
    {
        var archive = await Input("game.zip", "plain"); var key = await Input("license.key", "do not discard");
        var service = new Archives();
        var result = await new FileProcessingPlanExecutor(service).ExecuteAsync(Extract(), input, [archive, key], state);
        Assert.IsTrue(result.Completed); Assert.IsNotNull(result.FileLineage);
        var replaced = result.FileLineage.Entries.Single(e => e.InputRelativePath == "game.zip");
        Assert.IsTrue(replaced.Replaced);
        CollectionAssert.AreEqual(new[] {"game/game.bin"}, replaced.OutputRelativePaths.ToArray());
        var preserved = result.FileLineage.Entries.Single(e => e.InputRelativePath == "license.key");
        Assert.IsFalse(preserved.Replaced);
        CollectionAssert.AreEqual(new[] {"license.key"}, preserved.OutputRelativePaths.ToArray());
        Assert.AreEqual("do not discard", await File.ReadAllTextAsync(result.Files.Single(f => f.EndsWith("license.key"))));
        Assert.AreEqual((await ArtifactManifestFiles.CaptureAsync(input, [archive, key])).Fingerprint, result.FileLineage.InputManifest!.Fingerprint);
        Assert.AreEqual((await ArtifactManifestFiles.CaptureAsync(result.OutputDirectory!, result.Files.ToArray())).Fingerprint,
            result.FileLineage.OutputManifest!.Fingerprint);
    }

    [TestMethod]
    public async Task Dynamic_boundaries_preserve_their_own_lineage_after_later_steps_and_replay()
    {
        var archive = await Input("game.png", "plain"); var key = await Input("license.key", "key");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "rename", Op = "renameExtension", Selector = "game.png", Extension = ".zip"},
            new() {Id = "extract", Input = "rename", Op = "extractArchive"}]};
        var archives = new Archives(); var executor = new FileProcessingPlanExecutor(archives);
        var renamed = await executor.ExecuteAsync(plan, input, [archive, key], state, stopAfterStepId: "rename");
        var extracted = await executor.ExecuteAsync(plan, input, [archive, key], state, stopAfterStepId: "extract");
        var replay = await executor.ExecuteAsync(plan, input, [archive, key], state, stopAfterStepId: "rename");
        Assert.IsFalse(renamed.FileLineage!.Entries.Single(e => e.InputRelativePath == "game.png").Replaced);
        Assert.IsTrue(extracted.FileLineage!.Entries.Single(e => e.InputRelativePath == "game.png").Replaced);
        Assert.AreEqual(JsonSerializer.Serialize(renamed.FileLineage, Json), JsonSerializer.Serialize(replay.FileLineage, Json));
        Assert.AreEqual(1, archives.Count);
    }

    [TestMethod]
    public async Task Multiple_inputs_keep_separate_archive_sources_and_unselected_files()
    {
        var first = await Input("a.zip", "plain"); var second = await Input("b.zip", "plain"); var key = await Input("license.key", "key");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "first", Op = "renameExtension", Selector = "a.zip", Extension = ".7z"},
            new() {Id = "second", Op = "renameExtension", Selector = "b.zip", Extension = ".7z"},
            new() {Id = "extract", Inputs = ["first", "second"], Op = "extractArchive"}]};
        var result = await new FileProcessingPlanExecutor(new Archives()).ExecuteAsync(plan, input, [first, second, key], state);
        Assert.IsNotNull(result.FileLineage);
        CollectionAssert.AreEqual(new[] {"a/game.bin"}, result.FileLineage.Entries.Single(e => e.InputRelativePath == "a.zip").OutputRelativePaths.ToArray());
        CollectionAssert.AreEqual(new[] {"b/game.bin"}, result.FileLineage.Entries.Single(e => e.InputRelativePath == "b.zip").OutputRelativePaths.ToArray());
        Assert.IsFalse(result.FileLineage.Entries.Single(e => e.InputRelativePath == "license.key").Replaced);
    }

    [TestMethod]
    public async Task Empty_extraction_keeps_the_archive_without_claiming_replacement()
    {
        var archive = await Input("empty.zip", "empty");
        var result = await new FileProcessingPlanExecutor(new Archives()).ExecuteAsync(Extract(), input, [archive], state);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual("empty", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.IsFalse(result.FileLineage!.Entries.Single().Replaced);
    }

    [TestMethod]
    public async Task Changed_private_inputs_are_not_consumed_after_a_password_wait()
    {
        var archive = await Input("locked.zip", "locked"); var executor = new FileProcessingPlanExecutor(new Archives());
        Assert.IsFalse((await executor.ExecuteAsync(Extract(), input, [archive], state)).Completed);
        var (_, checkpoint) = await ReadState();
        await File.WriteAllTextAsync(checkpoint.Outputs["download"].Single().Path, "edited");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => executor.ExecuteAsync(Extract(), input, [archive], state, "ok"));
        Assert.AreEqual("locked", await File.ReadAllTextAsync(archive));
    }

    [TestMethod]
    public async Task Changed_committed_extraction_is_not_adopted_after_interruption()
    {
        var archive = await Input("game.zip", "plain"); var executor = new FileProcessingPlanExecutor(new Archives());
        await executor.ExecuteAsync(Extract(), input, [archive], state);
        var (path, checkpoint) = await ReadState();
        checkpoint.Completed = false; checkpoint.FinalFiles = []; checkpoint.FinalManifest = null;
        checkpoint.Outputs.Remove("extract"); checkpoint.Active = checkpoint.Outputs["download"].ToList();
        var operation = checkpoint.Operations.Values.Single(); operation.Completed = false;
        await File.WriteAllTextAsync(Path.Combine(operation.OutputDirectory!, "game.bin"), "modified after commit");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(checkpoint, Json));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => executor.ExecuteAsync(Extract(), input, [archive], state));
    }

    [TestMethod]
    public async Task Legacy_results_remain_reusable_but_cannot_authorize_cleanup()
    {
        var archive = await Input("game.zip", "plain"); var executor = new FileProcessingPlanExecutor(new Archives());
        var completed = await executor.ExecuteAsync(Extract(), input, [archive], state);
        var (path, checkpoint) = await ReadState(); checkpoint.LineageVersion = 0;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(checkpoint, Json));
        var reused = await executor.ExecuteAsync(Extract(), input, [archive], state);
        Assert.IsTrue(reused.Completed); Assert.IsNull(reused.FileLineage);
        Assert.AreEqual(0, (await FileProcessingPlanExecutor.GetReclaimableFilesAsync(completed.OutputDirectory)).Count);
    }

    [TestMethod]
    public async Task Private_cleanup_keeps_changed_files_and_requires_complete_replacement_leaves()
    {
        var archive = await Input("game.zip", "plain"); var key = await Input("license.key", "key");
        var completed = await new FileProcessingPlanExecutor(new Archives()).ExecuteAsync(Extract(), input, [archive, key], state);
        var (path, checkpoint) = await ReadState();
        var changedKey = checkpoint.Outputs["download"].Single(a => a.RelativePath == "license.key").Path;
        await File.WriteAllTextAsync(changedKey, "changed key");
        var original = checkpoint.Outputs["download"].Single(a => a.RelativePath == "game.zip").Path;
        Assert.IsTrue((await FileProcessingPlanExecutor.GetReclaimableFilesAsync(completed.OutputDirectory)).Contains(original));
        Assert.IsFalse((await FileProcessingPlanExecutor.GetReclaimableFilesAsync(completed.OutputDirectory)).Contains(changedKey));
        checkpoint.FileTransitions[Path.GetRelativePath(Path.GetDirectoryName(path)!, original).Replace('\\', '/')].Add("outputs/unaccounted.bin");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(checkpoint, Json));
        Assert.IsFalse((await FileProcessingPlanExecutor.GetReclaimableFilesAsync(completed.OutputDirectory)).Contains(original));
    }

    [TestMethod]
    public async Task Moving_a_checkpoint_preserves_relative_content_receipts_and_replay()
    {
        var archive = await Input("game.zip", "plain"); var archives = new Archives(); var executor = new FileProcessingPlanExecutor(archives);
        var result = await executor.ExecuteAsync(Extract(), input, [archive], state);
        var (_, checkpoint) = await ReadState();
        Assert.IsTrue(checkpoint.FileContents.Keys.All(p => !Path.IsPathRooted(p)));
        Assert.IsTrue(checkpoint.FileTransitions.All(p => !Path.IsPathRooted(p.Key) && p.Value.All(v => !Path.IsPathRooted(v))));
        var old = state; state = Path.Combine(root, "relocated"); Directory.Move(old, state);
        var path = Directory.EnumerateFiles(state, "state.json", SearchOption.AllDirectories).Single();
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        System.Text.Json.Nodes.JsonNode? Rebase(System.Text.Json.Nodes.JsonNode? value)
        {
            if (value is System.Text.Json.Nodes.JsonValue text && text.TryGetValue<string>(out var contents))
                return System.Text.Json.Nodes.JsonValue.Create(contents.Replace(old, state, StringComparison.Ordinal));
            if (value is System.Text.Json.Nodes.JsonObject obj)
                foreach (var key in obj.Select(pair => pair.Key).ToArray()) obj[key] = Rebase(obj[key]?.DeepClone());
            if (value is System.Text.Json.Nodes.JsonArray array)
                for (var i = 0; i < array.Count; i++) array[i] = Rebase(array[i]?.DeepClone());
            return value;
        }
        await File.WriteAllTextAsync(path, Rebase(node)!.ToJsonString());
        var repeated = await executor.ExecuteAsync(Extract(), input, [archive], state);
        Assert.AreEqual(JsonSerializer.Serialize(result.FileLineage, Json), JsonSerializer.Serialize(repeated.FileLineage, Json));
        Assert.AreEqual(1, archives.Count);
        Assert.IsTrue((await FileProcessingPlanExecutor.GetReclaimableFilesAsync(repeated.OutputDirectory)).Count > 0);
    }

    [TestMethod]
    public async Task A_new_manifest_for_edited_final_files_does_not_authorize_private_cleanup()
    {
        var archive = await Input("game.zip", "plain"); var executor = new FileProcessingPlanExecutor(new Archives());
        var result = await executor.ExecuteAsync(Extract(), input, [archive], state);
        var candidates = (await FileProcessingPlanExecutor.GetReclaimableFilesAsync(result.OutputDirectory)).ToHashSet();
        Assert.IsTrue(candidates.Count > 0);
        await File.WriteAllTextAsync(result.Files.Single(), "edited final content");
        var changedManifest = await ArtifactManifestFiles.CaptureAsync(result.OutputDirectory!, result.Files.ToArray());
        await executor.ReclaimCompletedIntermediatesAsync(result.OutputDirectory, changedManifest, candidates);
        Assert.IsTrue(candidates.All(File.Exists), "A newly captured manifest cannot replace the execution's content receipt.");
    }

    [TestMethod]
    public async Task Recursive_cleanup_rejects_a_new_manifest_for_edited_final_files()
    {
        var archive = await Input("outer.zip", "outer"); var executor = new RecursiveArchiveExecutor(new Archives());
        var result = await executor.ExecuteAsync(input, [archive], state, new() {MinimumFreeBytes = 0});
        var candidates = (await RecursiveArchiveExecutor.GetReclaimableFilesAsync(state)).ToHashSet();
        Assert.IsTrue(candidates.Count > 0);
        await File.WriteAllTextAsync(result.Files[0], "edited recursive final content");
        var changed = await ArtifactManifestFiles.CaptureAsync(result.OutputDirectory!, result.Files.ToArray());
        var record = new ArtifactRecord("result", "operation", "processedFiles", result.OutputDirectory!, result.Files,
            changed, changed.Fingerprint, ArtifactLocationState.Available, true, [], null);
        await executor.ReclaimCompletedIntermediatesAsync(state, record, candidates);
        Assert.IsTrue(candidates.All(File.Exists));
    }

    [TestMethod]
    public async Task Recursive_layers_keep_unknown_files_and_compose_actual_replacement_lineage()
    {
        var archive = await Input("outer.zip", "outer"); var key = await Input("license.key", "key");
        var archives = new Archives(); var executor = new RecursiveArchiveExecutor(archives);
        var result = await executor.ExecuteAsync(input, [archive, key], state, new() {MinimumFreeBytes = 0});
        Assert.IsTrue(result.Completed); Assert.IsNotNull(result.FileLineage);
        var entry = result.FileLineage.Entries.Single(e => e.InputRelativePath == "outer.zip");
        Assert.IsTrue(entry.Replaced);
        CollectionAssert.AreEquivalent(new[] {"outer/inner/game.bin", "outer/companion.key"}, entry.OutputRelativePaths.ToArray());
        Assert.IsFalse(result.FileLineage.Entries.Single(e => e.InputRelativePath == "license.key").Replaced);
        var replay = await executor.ExecuteAsync(input, [archive, key], state, new() {MinimumFreeBytes = 0});
        Assert.AreEqual(JsonSerializer.Serialize(result.FileLineage, Json), JsonSerializer.Serialize(replay.FileLineage, Json));
        Assert.AreEqual(2, archives.Count);
    }

    [TestMethod]
    public async Task Nested_processing_records_only_generated_ancestors_and_preserves_sidecar_provenance_on_replay()
    {
        var archive = await Input("outer.zip", "outer"); var key = await Input("license.key", "key");
        var executor = new RecursiveArchiveExecutor(new Archives());
        var result = await executor.ExecuteAsync(input, [archive, key], state, new() {MinimumFreeBytes = 0});
        Assert.IsTrue(result.Completed); var proof = result.DirectoryProvenance!;
        CollectionAssert.AreEqual(new[] {"outer", "outer/inner"},
            proof.Entries.Single(e => e.RelativePath == "outer/inner/game.bin").GeneratedAncestors.ToArray());
        CollectionAssert.AreEqual(new[] {"outer"},
            proof.Entries.Single(e => e.RelativePath == "outer/companion.key").GeneratedAncestors.ToArray());
        Assert.IsFalse(proof.Entries.Any(e => e.RelativePath == "license.key"));
        var replay = await executor.ExecuteAsync(input, [archive, key], state, new() {MinimumFreeBytes = 0});
        Assert.AreEqual(JsonSerializer.Serialize(proof, Json), JsonSerializer.Serialize(replay.DirectoryProvenance, Json));
    }

    [TestMethod]
    public async Task Directory_rebase_translates_extracted_prefix_in_dynamic_boundaries_and_does_not_rewrite_earlier_evidence()
    {
        var archive = await Input("author/game.zip", "plain"); var key = await Input("license.key", "key");
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "extract", Op = "extractArchive", Selector = "author/game.zip"},
            new() {Id = "rebase", Input = "extract", Op = "rebaseDirectory", SourceDirectory = "author", TargetDirectory = "new"}]};
        var executor = new FileProcessingPlanExecutor(new Archives());
        var first = await executor.ExecuteAsync(plan, input, [archive, key], state, stopAfterStepId: "extract");
        var last = await executor.ExecuteAsync(plan, input, [archive, key], state, stopAfterStepId: "rebase");
        Assert.IsTrue(first.Completed && last.Completed);
        CollectionAssert.AreEqual(new[] {"author/game"}, first.DirectoryProvenance!.Entries.Single().GeneratedAncestors.ToArray());
        CollectionAssert.AreEqual(new[] {"new/game"}, last.DirectoryProvenance!.Entries.Single().GeneratedAncestors.ToArray());
        Assert.AreEqual("new/game/game.bin", last.DirectoryProvenance.Entries.Single().RelativePath);
        var replay = await executor.ExecuteAsync(plan, input, [archive, key], state, stopAfterStepId: "extract");
        Assert.AreEqual(JsonSerializer.Serialize(first.DirectoryProvenance, Json), JsonSerializer.Serialize(replay.DirectoryProvenance, Json));
    }

    [TestMethod]
    public async Task Identical_recursive_siblings_reuse_bytes_without_stealing_each_others_lineage()
    {
        var first = await Input("a.zip", "plain"); var second = await Input("b.zip", "plain"); var archives = new Archives();
        var result = await new RecursiveArchiveExecutor(archives).ExecuteAsync(input, [first, second], state, new() {MinimumFreeBytes = 0});
        Assert.IsTrue(result.Completed); Assert.AreEqual(1, archives.Count);
        CollectionAssert.AreEqual(new[] {"a/game.bin"}, result.FileLineage!.Entries.Single(e => e.InputRelativePath == "a.zip").OutputRelativePaths.ToArray());
        CollectionAssert.AreEqual(new[] {"b/game.bin"}, result.FileLineage.Entries.Single(e => e.InputRelativePath == "b.zip").OutputRelativePaths.ToArray());
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public int Count { get; private set; }
        public async Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        {
            var content = await File.ReadAllTextAsync(entry, ct);
            return content != "locked" || candidates.Contains("ok") ? new(true, "ok", [], "", "") : new(false, null, [], "", "");
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Count++; var content = await File.ReadAllTextAsync(request.Files[0], ct);
            Directory.CreateDirectory(request.Directory);
            if (content == "outer")
            {
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "inner.zip"), "plain", ct);
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "companion.key"), "nested key", ct);
            }
            else if (content != "empty") await File.WriteAllTextAsync(Path.Combine(request.Directory, "game.bin"), "payload", ct);
            return new(true, request.Directory, "");
        }
    }
}

using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingExtractionOverlapTests
{
    private string root = null!;
    private string input = null!;
    private string checkpoint = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ArtifactStoragePolicy CopyPolicy = new() {Materialization = "copy", CleanupIntermediates = false};

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "extraction-overlap-" + Guid.NewGuid().ToString("N"));
        input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
        checkpoint = Path.Combine(root, "state");
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [TestMethod]
    public async Task One_multipart_group_reuses_identical_existing_files_and_preserves_both_origins()
    {
        var files = await Inputs(("bundle.7z.001", "one"), ("bundle.7z.002", "two"),
            ("bundle.7z.003", "three"), ("payload.bin", "same"), ("license.key", "keep"));
        var archives = new Archives {Files = new() {{"payload.bin", "same"}, {"new.bin", "new"}}};
        var executor = new FileProcessingPlanExecutor(archives);
        var result = await Execute(executor, files);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(1, archives.Requests.Count);
        CollectionAssert.AreEqual(new[] {"bundle.7z.001", "bundle.7z.002", "bundle.7z.003"},
            archives.Requests.Single().Files.Select(Path.GetFileName).ToArray());
        CollectionAssert.AreEquivalent(new[] {"payload.bin", "new.bin", "license.key"},
            result.Files.Select(Path.GetFileName).ToArray());
        foreach (var name in new[] {"bundle.7z.001", "bundle.7z.002", "bundle.7z.003"})
        {
            var replaced = result.FileLineage!.Entries.Single(e => e.InputRelativePath == name);
            Assert.IsTrue(replaced.Replaced);
            CollectionAssert.AreEquivalent(new[] {"payload.bin", "new.bin"}, replaced.OutputRelativePaths.ToArray());
        }
        var preserved = result.FileLineage!.Entries.Single(e => e.InputRelativePath == "payload.bin");
        Assert.IsFalse(preserved.Replaced);
        CollectionAssert.AreEqual(new[] {"payload.bin"}, preserved.OutputRelativePaths.ToArray());
        Assert.IsFalse(result.FileLineage.Entries.Single(e => e.InputRelativePath == "license.key").Replaced);
        Assert.AreEqual("same", await File.ReadAllTextAsync(files[3]));
        Assert.IsTrue(files.All(File.Exists), "Original companions and unrelated files remain untouched.");
        var (_, state) = await ReadState();
        Assert.AreEqual(3, state.Operations.Values.Single().ArchiveInputs!.Count);
        Assert.IsTrue(state.FileTransitions["inputs/payload.bin"].Count > 0);

        var replay = await Execute(executor, files);
        Assert.AreEqual(1, archives.Requests.Count);
        Assert.AreEqual(JsonSerializer.Serialize(result.FileLineage, Json), JsonSerializer.Serialize(replay.FileLineage, Json));
    }

    [TestMethod]
    public async Task One_different_overlap_rejects_the_whole_step_without_adopting_the_identical_overlap()
    {
        var files = await Inputs(("bundle.7z.001", "one"), ("bundle.7z.002", "two"),
            ("same.bin", "same"), ("different.bin", "user"));
        var archives = new Archives {Files = new() {{"same.bin", "same"}, {"different.bin", "else"}}};
        var executor = new FileProcessingPlanExecutor(archives);
        var result = await Execute(executor, files);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("ProcessingPathConflict", result.FailureCode);
        var (_, state) = await ReadState();
        Assert.IsFalse(state.Outputs.ContainsKey("extract"));
        Assert.IsFalse(state.FileTransitions.TryGetValue("inputs/same.bin", out var adopted) && adopted.Count > 0);
        Assert.AreEqual("user", await File.ReadAllTextAsync(files[3]));
        Assert.IsTrue(files.All(File.Exists));
        var replay = await Execute(executor, files);
        Assert.AreEqual("ProcessingPathConflict", replay.FailureCode);
        Assert.AreEqual(1, archives.Requests.Count, "A conflict retains the completed extraction checkpoint.");
    }

    [TestMethod]
    public async Task Portable_aliases_remain_conflicts_even_when_bytes_match()
    {
        var files = await Inputs(("bundle.7z.001", "one"), ("bundle.7z.002", "two"), ("Payload.bin", "same"));
        var result = await Execute(new(new Archives()), files);
        Assert.AreEqual("ProcessingPathConflict", result.FailureCode);
        Assert.AreEqual("PathAliasCollision", result.PathConflict?.Kind);
        Assert.IsTrue(files.All(File.Exists));
    }

    [TestMethod]
    public async Task Separate_archive_groups_with_the_same_target_still_require_explicit_review()
    {
        var files = await Inputs(("first.zip", "first"), ("second.zip", "second"), ("payload.bin", "same"));
        var result = await new FileProcessingPlanExecutor(new Archives()).ExecuteAsync(new()
        {
            Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Selector = "*.zip",
                ExtractionLayout = FileExtractionLayouts.CurrentDirectory}]
        }, input, files, checkpoint, storagePolicy: CopyPolicy);
        Assert.AreEqual("ProcessingPathConflict", result.FailureCode);
        Assert.IsTrue(files.All(File.Exists));
        var (_, state) = await ReadState();
        Assert.IsFalse(state.Outputs.ContainsKey("extract"));
        Assert.IsFalse(state.FileTransitions.TryGetValue("inputs/payload.bin", out var adopted) && adopted.Count > 0);
    }

    [TestMethod]
    public async Task A_retained_file_changed_during_extraction_is_not_reused_from_its_old_receipt()
    {
        var files = await Inputs(("bundle.7z.001", "one"), ("bundle.7z.002", "two"), ("payload.bin", "same"));
        var archives = new Archives {AfterExtraction = request =>
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(request.Files[0])!, "payload.bin"), "edit")};
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Execute(new(archives), files));
        var (_, state) = await ReadState();
        Assert.IsFalse(state.Completed);
        Assert.IsFalse(state.Outputs.ContainsKey("extract"));
        Assert.IsFalse(state.FileTransitions.TryGetValue("inputs/payload.bin", out var adopted) && adopted.Count > 0);
        Assert.AreEqual("same", await File.ReadAllTextAsync(files[2]));
    }

    [TestMethod]
    public async Task Committed_extraction_before_step_commit_replays_without_reextracting_and_keeps_lineage()
    {
        var files = await Inputs(("bundle.7z.001", "one"), ("bundle.7z.002", "two"), ("payload.bin", "same"));
        var archives = new Archives(); var executor = new FileProcessingPlanExecutor(archives);
        var completed = await Execute(executor, files);
        Assert.IsTrue(completed.Completed);
        var (path, state) = await ReadState();
        // Model an older wait or crash after its archive output was committed but before
        // the extracted and retained files could be merged into a final result.
        state.Completed = false; state.FinalManifest = null; state.FinalFiles = []; state.FinalExtractedFiles = [];
        state.FinalAssembly = null; state.Outputs.Remove("extract"); state.DirectoryOutputs.Remove("extract");
        state.Active = state.Outputs["download"].ToList();
        state.FileTransitions.Remove("inputs/payload.bin");
        Directory.Delete(completed.OutputDirectory!, true);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, Json));

        var resumed = await Execute(executor, files);
        Assert.IsTrue(resumed.Completed, resumed.Message);
        Assert.AreEqual(1, archives.Requests.Count);
        Assert.AreEqual(JsonSerializer.Serialize(completed.FileLineage, Json), JsonSerializer.Serialize(resumed.FileLineage, Json));
        Assert.IsTrue(files.All(File.Exists));
    }

    private Task<FileProcessingPlanResult> Execute(FileProcessingPlanExecutor executor, string[] files) =>
        executor.ExecuteAsync(new() {Requirement = "required", Steps = [new()
        {
            Id = "extract", Op = "extractArchive", Selector = "bundle.7z.00*", ExtractionLayout = FileExtractionLayouts.CurrentDirectory
        }]}, input, files, checkpoint, storagePolicy: CopyPolicy);

    private async Task<string[]> Inputs(params (string Name, string Content)[] entries)
    {
        var files = new List<string>();
        foreach (var (name, content) in entries)
        {
            var path = Path.Combine(input, name);
            await File.WriteAllTextAsync(path, content); files.Add(path);
        }
        return files.ToArray();
    }

    private async Task<(string, FileProcessingPlanExecutor.State)> ReadState()
    {
        var path = Directory.EnumerateFiles(checkpoint, "state.json", SearchOption.AllDirectories).Single();
        return (path, JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(path), Json)!);
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public Dictionary<string, string> Files { get; init; } = new() {{"payload.bin", "same"}};
        public Action<ArchiveExtractionRequest>? AfterExtraction { get; init; }
        public List<ArchiveExtractionRequest> Requests { get; } = [];
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress,
            CancellationToken ct)
        {
            Requests.Add(request); Directory.CreateDirectory(request.Directory);
            foreach (var (name, content) in Files) await File.WriteAllTextAsync(Path.Combine(request.Directory, name), content, ct);
            AfterExtraction?.Invoke(request);
            return new(true, request.Directory, "");
        }
    }
}

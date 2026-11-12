using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingPathConflictTests
{
    private string root = null!;
    private string input = null!;
    private string checkpoint = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize] public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "path-conflict-" + Guid.NewGuid().ToString("N"));
        input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
        checkpoint = Path.Combine(root, "state");
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private async Task<string> FileAt(string relative, string content)
    {
        var path = Path.Combine(input, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        return path;
    }
    private static FileProcessingPlan Rename(string? selector = null) => new() {Requirement = "required", Steps =
        [new() {Id = "rename", Op = "renameExtension", Extension = ".zip", Selector = selector}]};
    private Task<FileProcessingPlanResult> Execute(FileProcessingPlan plan, string[] files, TrackingFiles tracker,
        IArchiveExtractionService? archives = null) => new FileProcessingPlanExecutor(archives ?? new NoArchives(), tracker)
            .ExecuteAsync(plan, input, files, checkpoint, storagePolicy: new());

    [TestMethod]
    public async Task Broad_extension_rename_reports_both_sources_before_reading_or_copying_payloads()
    {
        // These manifest entries deliberately have no readable payload. A content read or input
        // snapshot would fail; the path-only plan conflict is already certain.
        var paths = new[] {Path.Combine(input, "collection.mp4"), Path.Combine(input, "collection.z01")};
        var tracker = new TrackingFiles();
        var result = await Execute(Rename(), paths, tracker);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("ProcessingPathConflict", result.FailureCode);
        Assert.AreEqual("rename", result.PathConflict?.StepId);
        Assert.AreEqual("PlannedTargetCollision", result.PathConflict?.Kind);
        Assert.AreEqual("collection.zip", result.PathConflict?.TargetPath);
        CollectionAssert.AreEquivalent(new[] {"collection.mp4", "collection.z01"},
            result.PathConflict!.Entries.Select(e => e.SourcePath).ToArray());
        Assert.AreEqual(0, tracker.Copies); Assert.AreEqual(0, tracker.Moves);
        Assert.IsFalse(Directory.Exists(checkpoint));
    }

    [TestMethod]
    public async Task A_later_known_move_conflict_is_found_before_the_first_snapshot()
    {
        var paths = new[] {Path.Combine(input, "one/data.bin"), Path.Combine(input, "two/data.bin")};
        var plan = Rename() with {Steps = [
            new() {Id = "rename", Op = "renameExtension", Extension = ".dat"},
            new() {Id = "gather", Op = "moveFile", Input = "rename", TargetDirectory = "ready"}]};
        var tracker = new TrackingFiles();
        var result = await Execute(plan, paths, tracker);
        Assert.AreEqual("gather", result.PathConflict?.StepId);
        Assert.AreEqual("ready/data.dat", result.PathConflict?.TargetPath);
        Assert.AreEqual(0, tracker.Copies); Assert.IsFalse(Directory.Exists(checkpoint));
    }

    [TestMethod]
    public async Task Narrow_extension_rename_retains_the_unselected_volume_and_unrelated_file()
    {
        var main = await FileAt("collection.mp4", "zip entry");
        var volume = await FileAt("collection.z01", "different volume");
        var unrelated = await FileAt("other/data.ziip", "independent");
        var result = await Execute(Rename("*.mp4"), [main, volume, unrelated], new());
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"collection.zip", "collection.z01", "other/data.ziip"},
            result.Files.Select(f => Path.GetRelativePath(result.OutputDirectory!, f).Replace('\\', '/')).ToArray());
        Assert.AreEqual("different volume", await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory!, "collection.z01")));
        Assert.AreEqual("zip entry", await File.ReadAllTextAsync(main));
        Assert.AreEqual("different volume", await File.ReadAllTextAsync(volume));
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
            Directory.GetFiles(checkpoint, "state.json", SearchOption.AllDirectories).Single()), Json)!;
        CollectionAssert.AreEquivalent(new[] {"collection.zip", "collection.z01"},
            state.Outputs["rename"].Select(a => a.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task Directory_required_by_an_unselected_sibling_cannot_be_replaced_by_a_file()
    {
        var paths = new[] {Path.Combine(input, "source.bin"), Path.Combine(input, "folder/keep.bin")};
        var plan = Rename() with {Steps = [new() {Id = "rename", Op = "renameFile", Selector = "source.bin", TargetName = "folder"}]};
        var result = await Execute(plan, paths, new());
        Assert.AreEqual("FileDirectoryConflict", result.PathConflict?.Kind);
        Assert.AreEqual("folder", result.PathConflict?.TargetPath);
        Assert.IsFalse(Directory.Exists(checkpoint));
    }

    [TestMethod]
    public async Task Untracked_output_is_reported_without_hashing_or_adopting_it()
    {
        var source = Path.Combine(input, "data.mp4");
        var plan = Rename();
        var identity = FileProcessingPlanExecutor.CheckpointIdentity(plan, [source]);
        var operationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("rename")))[..16] + "-rename";
        var occupied = Directory.CreateDirectory(Path.Combine(checkpoint, identity, "outputs", operationKey)).FullName;
        var existing = Path.Combine(occupied, "data.zip");
        await File.WriteAllTextAsync(existing, "unowned");
        var tracker = new TrackingFiles();
        var result = await new FileProcessingPlanExecutor(new NoArchives(), tracker).ExecuteAsync(plan, input, [source], checkpoint);
        Assert.AreEqual("ExistingTargetConflict", result.PathConflict?.Kind);
        Assert.AreEqual("unowned", await File.ReadAllTextAsync(existing));
        Assert.AreEqual(0, tracker.Copies); Assert.AreEqual(0, tracker.Moves);
        Assert.IsFalse(File.Exists(Path.Combine(checkpoint, identity, "state.json")));
    }

    [TestMethod]
    public async Task Dynamic_output_conflict_preserves_extraction_checkpoint_and_does_not_extract_again()
    {
        var archive = await FileAt("outer.zip", "archive");
        var plan = Rename() with {Steps = [new() {Id = "extract", Op = "extractArchive"},
            new() {Id = "rename", Op = "renameExtension", Input = "extract", Extension = ".zip"}]};
        var archives = new NestedArchives(); var tracker = new TrackingFiles();
        var result = await Execute(plan, [archive], tracker, archives);
        Assert.AreEqual("PlannedTargetCollision", result.PathConflict?.Kind);
        Assert.AreEqual("rename", result.PathConflict?.StepId);
        Assert.AreEqual(1, archives.Calls);
        var copies = tracker.Copies;
        var replay = await Execute(plan, [archive], tracker, archives);
        Assert.AreEqual("ProcessingPathConflict", replay.FailureCode);
        Assert.AreEqual(1, archives.Calls); Assert.AreEqual(copies, tracker.Copies); Assert.AreEqual(0, tracker.Moves);
        Assert.AreEqual("archive", await File.ReadAllTextAsync(archive));
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
            Directory.GetFiles(checkpoint, "state.json", SearchOption.AllDirectories).Single()), Json)!;
        Assert.IsTrue(state.Outputs.ContainsKey("extract")); Assert.IsFalse(state.Outputs.ContainsKey("rename"));
        Assert.IsTrue(state.Outputs["extract"].All(a => File.Exists(a.Path)));
    }

    [TestMethod]
    public void Native_projection_retains_case_sensitive_names_while_portable_projection_reports_aliases()
    {
        FileProcessingPathProjectionEntry[] entries = [new("first", "Folder/a.bin"), new("second", "folder/b.bin")];
        Assert.AreEqual("PathAliasCollision", FileProcessingPathProjection.Inspect("step", entries, portable: true)?.Kind);
        if (!OperatingSystem.IsWindows()) Assert.IsNull(FileProcessingPathProjection.Inspect("step", entries));
        entries = [new("first", "é.bin"), new("second", "e\u0301.bin")];
        Assert.AreEqual("PathAliasCollision", FileProcessingPathProjection.Inspect("step", entries, portable: true)?.Kind);
    }

    private sealed class TrackingFiles : IFileArtifactMaterializer
    {
        private readonly FileArtifactMaterializer actual = new();
        public int Copies; public int Moves;
        public Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        { Copies++; return actual.MaterializeAsync(source, target, "copy", ct); }
        public async Task<bool> TryMoveAsync(string source, string target, CancellationToken ct)
        { var moved = await actual.TryMoveAsync(source, target, ct); if (moved) Moves++; return moved; }
    }
    private sealed class NoArchives : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct) =>
            throw new AssertFailedException("A file rename must not probe archives.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct) =>
            throw new AssertFailedException("A file rename must not extract archives.");
    }
    private sealed class NestedArchives : IArchiveExtractionService
    {
        public int Calls;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct) =>
            Task.FromResult(new ArchivePasswordProbe(true, null, [], "", null));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Calls++; Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "collection.mp4"), "entry", ct);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "collection.z01"), "volume", ct);
            return new(true, request.Directory, "");
        }
    }
}

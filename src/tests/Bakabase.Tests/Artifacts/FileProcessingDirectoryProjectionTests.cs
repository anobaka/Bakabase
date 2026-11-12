using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed partial class FileProcessingDirectoryProjectionTests
{
    private string root = null!;
    private string input = null!;
    private string checkpoint = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestInitialize] public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "directory-projection-" + Guid.NewGuid().ToString("N"));
        input = Path.Combine(root, "input"); checkpoint = Path.Combine(root, "state"); Directory.CreateDirectory(input);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private async Task<string> FileAt(string relative, string content = "keep")
    {
        var path = Path.Combine(input, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content); return path;
    }
    private string DirectoryAt(string relative) => Directory.CreateDirectory(Path.Combine(input, relative)).FullName;
    private static FileProcessingPlan Plan(string source = "wrapper", string target = ".") => new()
    {Requirement = "required", Steps = [new() {Id = "project", Op = "rebaseDirectory", SourceDirectory = source, TargetDirectory = target}]};
    private Task<FileProcessingPlanResult> Execute(FileProcessingPlan plan, string[] paths, TrackingFiles? files = null,
        string[]? directories = null, string? boundary = null, IArchiveExtractionService? archives = null) =>
        new FileProcessingPlanExecutor(archives ?? new NoArchives(), files).ExecuteAsync(plan, input, paths, checkpoint,
            storagePolicy: new(), inputDirectories: directories, stopAfterStepId: boundary, extractionContentsAsRoot: true);
    private async Task<FileProcessingPlanExecutor.State> State() => JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(
        await File.ReadAllTextAsync(Directory.GetFiles(checkpoint, "state.json", SearchOption.AllDirectories).Single()), Json)!;
    private static string[] Paths(FileProcessingPlanResult result) => result.Files.Select(f =>
        Path.GetRelativePath(result.OutputDirectory!, f).Replace('\\', '/')).ToArray();

    [TestMethod]
    public async Task Exact_prefix_preserves_author_tree_unknown_siblings_empty_leaves_and_originals()
    {
        var files = new[] {await FileAt("wrapper/author/a.bin", "first"), await FileAt("wrapper/author/nested/b.bin", "second"),
            await FileAt("wrapper-other/unrelated.key", "license"), await FileAt("readme.txt", "note")};
        var empty = DirectoryAt("wrapper/author/save"); var sibling = DirectoryAt("empty-sibling");
        DirectoryAt("wrapper/unregistered"); var tracker = new TrackingFiles();
        var result = await Execute(Plan(), files, tracker, [empty, sibling]);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"author/a.bin", "author/nested/b.bin", "wrapper-other/unrelated.key", "readme.txt"}, Paths(result));
        CollectionAssert.AreEquivalent(new[] {"author/save", "empty-sibling"}, result.Directories!.Select(p =>
            Path.GetRelativePath(result.OutputDirectory!, p).Replace('\\', '/')).ToArray());
        Assert.IsFalse(Directory.Exists(Path.Combine(result.OutputDirectory!, "wrapper")));
        Assert.IsFalse(Directory.Exists(Path.Combine(result.OutputDirectory!, "unregistered")));
        Assert.AreEqual(2, tracker.ProjectionMoves);
        Assert.AreEqual(files.Length, tracker.Copies, "Only the original isolation snapshot materializes bytes.");
        Assert.AreEqual(files.Length, tracker.FinalMoves); Assert.AreEqual(0, tracker.FinalCopies);
        Assert.IsTrue(files.All(File.Exists));
        Assert.IsTrue(result.FileLineage!.Entries.All(e => !e.Replaced));
        var replay = await Execute(Plan(), files, tracker, [empty, sibling]);
        Assert.AreEqual(result.FileLineage.OutputManifest!.Fingerprint, replay.FileLineage!.OutputManifest!.Fingerprint);
        Assert.AreEqual(2, tracker.ProjectionMoves);
    }

    [TestMethod]
    public async Task Empty_directory_only_selection_is_projected_while_a_sibling_file_remains()
    {
        var file = await FileAt("readme.txt"); var empty = DirectoryAt("wrapper/save");
        var result = await Execute(Plan(), [file], directories: [empty]);
        Assert.IsTrue(result.Completed);
        CollectionAssert.AreEquivalent(new[] {"readme.txt"}, Paths(result));
        Assert.IsTrue(Directory.Exists(Path.Combine(result.OutputDirectory!, "save")));
    }

    [TestMethod]
    public async Task Dynamic_nested_extraction_then_projection_preserves_boundary_and_empty_author_directories()
    {
        var source = await FileAt("outer.zip", "outer"); var archives = new NestedArchives(); var files = new TrackingFiles();
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "outer", Op = "extractArchive"},
            new() {Id = "inner", Op = "extractArchive", Input = "outer", Selector = "inner.zip"},
            new() {Id = "project", Op = "rebaseDirectory", Input = "inner", SourceDirectory = "wrapper", TargetDirectory = "ready"}]};
        var first = await Execute(plan, [source], files, boundary: "inner", archives: archives);
        var result = await Execute(plan, [source], files, archives: archives);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"ready/game/data.bin", "readme.txt"}, Paths(result));
        Assert.IsTrue(Directory.Exists(Path.Combine(result.OutputDirectory!, "ready/game/Save")));
        Assert.IsTrue(Directory.Exists(Path.Combine(result.OutputDirectory!, "OuterSave")));
        Assert.IsTrue(File.Exists(Path.Combine(first.OutputDirectory!, "wrapper/game/data.bin")), "A previously exposed boundary is immutable.");
        var replay = await Execute(plan, [source], files, boundary: "inner", archives: archives);
        Assert.AreEqual(first.OutputDirectory, replay.OutputDirectory);
        Assert.AreEqual(2, archives.Calls);
    }

    [TestMethod]
    public async Task Two_directory_projections_can_chain_through_their_registered_directory_outputs()
    {
        var file = await FileAt("wrapper/game/data.bin"); var empty = DirectoryAt("wrapper/game/save");
        var plan = Plan() with {Steps = [Plan().Steps[0], new() {Id = "next", Op = "rebaseDirectory", Input = "project", SourceDirectory = "game", TargetDirectory = "ready"}]};
        var result = await Execute(plan, [file], directories: [empty]);
        CollectionAssert.AreEquivalent(new[] {"ready/data.bin"}, Paths(result));
        Assert.IsTrue(Directory.Exists(Path.Combine(result.OutputDirectory!, "ready/save")));
    }

    [TestMethod]
    public async Task Shared_input_branch_keeps_original_snapshot_and_both_directory_projections()
    {
        var file = await FileAt("wrapper/game.bin"); var empty = DirectoryAt("wrapper/save"); var tracker = new TrackingFiles();
        var plan = Plan() with {Steps = [Plan().Steps[0], new() {Id = "branch", Op = "rebaseDirectory", Input = "download", SourceDirectory = "wrapper", TargetDirectory = "backup"}]};
        var result = await Execute(plan, [file], tracker, [empty]);
        CollectionAssert.AreEquivalent(new[] {"game.bin", "backup/game.bin"}, Paths(result));
        CollectionAssert.AreEquivalent(new[] {"save", "backup/save"}, result.Directories!.Select(p =>
            Path.GetRelativePath(result.OutputDirectory!, p).Replace('\\', '/')).ToArray());
        Assert.AreEqual(0, tracker.ProjectionMoves);
        Assert.IsTrue(File.Exists((await State()).Outputs["download"].Single().Path));
    }

    [TestMethod]
    public async Task Explicit_step_retention_override_prevents_moving_private_input()
    {
        var file = await FileAt("wrapper/game.bin"); var tracker = new TrackingFiles();
        var plan = Plan() with {Steps = [Plan().Steps[0] with {StoragePolicy = new() {CleanupIntermediates = false}}]};
        var result = await Execute(plan, [file], tracker);
        Assert.IsTrue(result.Completed); Assert.AreEqual(0, tracker.ProjectionMoves);
        Assert.IsTrue(File.Exists((await State()).Outputs["download"].Single().Path));
    }

    [DataTestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Interrupted_partial_or_committed_rename_reuses_verified_bytes_and_empty_directories(bool committed)
    {
        var files = new[] {await FileAt("wrapper/a.bin", "a"), await FileAt("wrapper/b.bin", "b")};
        var empty = DirectoryAt("wrapper/save"); var tracker = new TrackingFiles {InterruptAfterMove = committed ? 2 : 1};
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Execute(Plan(), files, tracker, [empty]));
        var operation = (await State()).Operations.Values.Single();
        if (committed) Directory.Move(operation.OutputDirectory + ".pending", operation.OutputDirectory!);
        tracker = new(); var resumed = await Execute(Plan(), files, tracker, [empty]);
        Assert.IsTrue(resumed.Completed); Assert.AreEqual(committed ? 0 : 1, tracker.ProjectionMoves);
        Assert.AreEqual(0, tracker.Copies, "Neither the source snapshot nor final files need another copy.");
        Assert.AreEqual(files.Length, tracker.FinalMoves);
        Assert.IsTrue(Directory.Exists(Path.Combine(resumed.OutputDirectory!, "save")));
    }

    [DataTestMethod]
    [DataRow("modified-file")] [DataRow("extra-directory")] [DataRow("directory-link")]
    public async Task Tampered_pending_projection_stops_before_moving_remaining_files(string tamper)
    {
        if (OperatingSystem.IsWindows() && tamper == "directory-link") return;
        var files = new[] {await FileAt("wrapper/a.bin", "a"), await FileAt("wrapper/b.bin", "b")};
        var empty = DirectoryAt("wrapper/save"); var tracker = new TrackingFiles {InterruptAfterMove = 1};
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Execute(Plan(), files, tracker, [empty]));
        var operation = (await State()).Operations.Values.Single(); var pending = operation.OutputDirectory + ".pending";
        if (tamper == "modified-file") await File.WriteAllTextAsync(Path.Combine(pending, "a.bin"), "x");
        else if (tamper == "extra-directory") Directory.CreateDirectory(Path.Combine(pending, "unknown"));
        else { Directory.Delete(Path.Combine(pending, "save")); Directory.CreateSymbolicLink(Path.Combine(pending, "save"), input); }
        tracker = new();
        if (tamper == "directory-link")
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Execute(Plan(), files, tracker, [empty]));
        else await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Execute(Plan(), files, tracker, [empty]));
        Assert.AreEqual(0, tracker.ProjectionMoves); Assert.IsTrue(files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("same.bin", "same.bin")] [DataRow("Same.bin", "same.bin")]
    [DataRow("e\u0301.bin", "é.bin")]
    public async Task Exact_case_and_Unicode_collisions_fail_before_any_private_move(string projected, string occupied)
    {
        var files = new[] {await FileAt("wrapper/" + projected, "new"), await FileAt(occupied, "retain")};
        var tracker = new TrackingFiles();
        var result = await Execute(Plan(), files, tracker);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual(projected == occupied ? "PlannedTargetCollision" : "PathAliasCollision", result.PathConflict?.Kind);
        Assert.AreEqual(0, tracker.ProjectionMoves); Assert.AreEqual("retain", await File.ReadAllTextAsync(files[1]));
    }

    [TestMethod]
    public async Task Directory_file_conflict_rejects_projection_before_moves()
    {
        var files = new[] {await FileAt("wrapper/data.bin"), await FileAt("save", "keep")}; var empty = DirectoryAt("wrapper/save");
        var tracker = new TrackingFiles();
        var result = await Execute(Plan(), files, tracker, [empty]);
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("FileDirectoryConflict", result.PathConflict?.Kind);
        Assert.AreEqual(0, tracker.ProjectionMoves);
    }

    [TestMethod]
    public async Task Source_directory_links_are_rejected_without_following_them()
    {
        if (OperatingSystem.IsWindows()) return;
        var file = await FileAt("readme.txt"); var external = Directory.CreateDirectory(Path.Combine(root, "external")).FullName;
        var link = Path.Combine(input, "wrapper"); Directory.CreateSymbolicLink(link, external);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Execute(Plan(), [file], directories: [link]));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(external).Length);
    }

    [TestMethod]
    public async Task Prefix_lookalikes_do_not_match_and_do_not_copy_inputs()
    {
        var file = await FileAt("wrapper-other/a.bin"); var tracker = new TrackingFiles();
        var result = await Execute(Plan(), [file], tracker);
        Assert.IsFalse(result.Completed); Assert.AreEqual("ProcessingInputNotMatched", result.FailureCode);
        Assert.AreEqual(0, tracker.Copies); Assert.IsFalse(Directory.Exists(checkpoint));
    }

    [DataTestMethod]
    [DataRow("../wrapper", ".")] [DataRow("/wrapper", ".")] [DataRow(".", ".")]
    [DataRow("wrapper", "../escape")] [DataRow("wrapper", "/absolute")]
    [DataRow("a//b", ".")] [DataRow("wrapper*", ".")] [DataRow("wrapper", "c:drive")]
    public void Unsafe_or_ambiguous_prefixes_are_rejected(string source, string target) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(Plan(source, target)));

    [TestMethod]
    public void Explicit_source_and_target_are_semantic_and_old_null_shape_is_byte_identical()
    {
        const string legacy = "{\"requirement\":\"required\",\"steps\":[{\"id\":\"rename\",\"op\":\"renameExtension\",\"input\":\"download\",\"selector\":null,\"extension\":\".7z\",\"password\":null}],\"evidence\":[]}";
        var parsed = JsonSerializer.Deserialize<FileProcessingPlan>(legacy, Json)!;
        Assert.AreEqual(legacy, FileProcessingPlanExecutor.IdentityJson(parsed));
        Assert.IsFalse(Newtonsoft.Json.JsonConvert.SerializeObject(parsed).Contains("SourceDirectory"));
        var first = JsonSerializer.Serialize(Plan(), Json); var second = JsonSerializer.Serialize(Plan("other"), Json);
        Assert.AreNotEqual(FileArtifactProcessor.Intent("source", first, null, new()), FileArtifactProcessor.Intent("source", second, null, new()));
        Assert.AreNotEqual(FileProcessingPlanExecutor.IdentityJson(Plan()), FileProcessingPlanExecutor.IdentityJson(Plan(target: "ready")));
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(Plan() with
            {Steps = [Plan().Steps[0] with {Selector = "*.bin"}]}));
        Assert.ThrowsExactly<InvalidOperationException>(() => FileProcessingPlanValidator.Validate(Plan() with
            {Steps = [new() {Id = "x", Op = "extractArchive", SourceDirectory = "wrapper"}]}));
        Assert.IsTrue(FileProcessingPlanValidator.HasMissingRequiredParameters(Plan() with
            {Steps = [Plan().Steps[0] with {SourceDirectory = null}]}));
        Assert.IsFalse(FileProcessingPlanValidator.HasMissingRequiredParameters(Plan(target: "")));
    }

    private sealed class TrackingFiles : IFileArtifactMaterializer
    {
        private readonly FileArtifactMaterializer actual = new();
        public int ProjectionMoves; public int FinalMoves; public int Copies; public int FinalCopies; public int InterruptAfterMove;
        private static bool IsFinal(string target) => target.Contains("result.pending" + Path.DirectorySeparatorChar) ||
            target.Contains(Path.DirectorySeparatorChar + "final-copy-", StringComparison.Ordinal);
        public Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        { Copies++; if (IsFinal(target)) FinalCopies++; return actual.MaterializeAsync(source, target, "copy", ct); }
        public async Task<bool> TryMoveAsync(string source, string target, CancellationToken ct)
        {
            var moved = await actual.TryMoveAsync(source, target, ct); Assert.IsTrue(moved);
            if (IsFinal(target)) FinalMoves++; else ProjectionMoves++;
            if (!IsFinal(target) && ProjectionMoves == InterruptAfterMove) throw new OperationCanceledException("after atomic move");
            return moved;
        }
    }
    private sealed class NoArchives : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct) =>
            throw new AssertFailedException("Directory projection must not probe an archive.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct) =>
            throw new AssertFailedException("Directory projection must not extract an archive.");
    }
    private sealed class NestedArchives : IArchiveExtractionService
    {
        public int Calls;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct) =>
            Task.FromResult(new ArchivePasswordProbe(true, null, [], "", null));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            var outer = ++Calls == 1; var file = Path.Combine(request.Directory, outer ? "inner.zip" : "wrapper/game/data.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, outer ? "inner" : "payload", ct);
            Directory.CreateDirectory(Path.Combine(request.Directory, outer ? "OuterSave" : "wrapper/game/Save"));
            if (outer) await File.WriteAllTextAsync(Path.Combine(request.Directory, "readme.txt"), "retain", ct);
            return new(true, request.Directory, "");
        }
    }
}

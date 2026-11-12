using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingRenameRecoveryTests
{
    private string root = null!;
    private string input = null!;
    private string checkpoint = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "rename-recovery-" + Guid.NewGuid().ToString("N"));
        input = Path.Combine(root, "input");
        checkpoint = Path.Combine(root, "state");
        Directory.CreateDirectory(input);
    }

    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private async Task<string[]> Inputs(int count = 1)
    {
        var paths = Enumerable.Range(0, count).Select(i => Path.Combine(input, $"original-{i}.bin")).ToArray();
        foreach (var path in paths) await File.WriteAllTextAsync(path, "original:" + Path.GetFileName(path));
        return paths;
    }

    private static FileProcessingPlan Plan(bool branch = false) => new() {Requirement = "required", Steps = [
        new() {Id = "rename", Op = "renameLiteral", FindText = ".bin", ReplaceText = ".dat"},
        new() {Id = "finish", Op = "moveFile", Input = branch ? "download" : "rename", TargetDirectory = "ready"}]};

    private Task<FileProcessingPlanResult> Execute(FileProcessingPlan plan, string[] paths, TrackingFiles files,
        string? boundary = null, ArtifactStoragePolicy? policy = null) =>
        new FileProcessingPlanExecutor(new NoArchives(), files).ExecuteAsync(plan, input, paths, checkpoint,
            stopAfterStepId: boundary, storagePolicy: policy ?? new());

    private async Task<FileProcessingPlanExecutor.State> State() =>
        JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
            Directory.GetFiles(checkpoint, "state.json", SearchOption.AllDirectories).Single()), Json)!;

    [TestMethod]
    public async Task Sole_consumer_chain_moves_private_files_and_keeps_originals_unchanged()
    {
        var paths = await Inputs(); var files = new TrackingFiles();
        var result = await Execute(Plan(), paths, files);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(2, files.Moves);
        Assert.AreEqual(2, files.Copies, "Only the original isolation and final publication need materialization.");
        Assert.AreEqual("original:original-0.bin", await File.ReadAllTextAsync(paths[0]));
        Assert.AreEqual("original:original-0.bin", await File.ReadAllTextAsync(result.Files.Single()));
        var state = await State();
        Assert.IsFalse(File.Exists(state.Outputs["download"].Single().Path));
        Assert.IsFalse(File.Exists(state.Outputs["rename"].Single().Path));
        var replay = await Execute(Plan(), paths, files);
        CollectionAssert.AreEqual(result.Files.ToArray(), replay.Files.ToArray());
        Assert.AreEqual(2, files.Moves);
    }

    [TestMethod]
    public async Task Any_other_declared_consumer_preserves_the_shared_snapshot()
    {
        var paths = await Inputs(); var files = new TrackingFiles();
        var result = await Execute(Plan(branch: true), paths, files);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, files.Moves);
        Assert.IsTrue(File.Exists((await State()).Outputs["download"].Single().Path));
        Assert.AreEqual(2, result.Files.Count);
    }

    [TestMethod]
    public async Task Published_boundary_remains_immutable_for_later_steps_and_history()
    {
        var paths = await Inputs(); var files = new TrackingFiles();
        var first = await Execute(Plan(), paths, files, "rename");
        Assert.AreEqual(1, files.Moves);
        var final = await Execute(Plan(), paths, files);
        Assert.IsTrue(final.Completed);
        Assert.AreEqual(1, files.Moves, "An exposed operation output is no longer exclusively owned.");
        Assert.AreEqual("original:original-0.bin", await File.ReadAllTextAsync(first.Files.Single()));
        var replay = await Execute(Plan(), paths, files, "rename");
        CollectionAssert.AreEqual(first.Files.ToArray(), replay.Files.ToArray());
    }

    [DataTestMethod]
    [DataRow("copy", true)]
    [DataRow("auto", false)]
    public async Task Explicit_copy_and_retention_policies_prevent_moves(string mode, bool cleanup)
    {
        var paths = await Inputs(); var files = new TrackingFiles();
        await Execute(Plan(), paths, files, policy: new() {Materialization = mode, CleanupIntermediates = cleanup});
        Assert.AreEqual(0, files.Moves);
        Assert.IsTrue(File.Exists((await State()).Outputs["download"].Single().Path));
    }

    [TestMethod]
    public async Task Interrupted_partial_move_resumes_from_both_old_and_pending_paths()
    {
        var paths = await Inputs(2); var files = new TrackingFiles {InterruptAfterMove = 1};
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Execute(Plan(), paths, files));
        var interrupted = await State();
        Assert.AreEqual(1, interrupted.Outputs["download"].Count(a => File.Exists(a.Path)));
        Assert.AreEqual(1, interrupted.Operations.Values.Single().RenameFiles!.Count(entry =>
            File.Exists(Path.Combine(interrupted.Operations.Values.Single().OutputDirectory! + ".pending", entry.RelativePath))));
        var resumed = await Execute(Plan(), paths, new TrackingFiles());
        Assert.IsTrue(resumed.Completed);
        CollectionAssert.AreEquivalent(paths.Select(File.ReadAllText).ToArray(), resumed.Files.Select(File.ReadAllText).ToArray());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Restart_adopts_a_verified_pending_or_committed_move_without_repeating_it(bool committed)
    {
        var paths = await Inputs(); var files = new TrackingFiles {InterruptAfterMove = 1};
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Execute(Plan(), paths, files));
        var operation = (await State()).Operations.Values.Single();
        if (committed) Directory.Move(operation.OutputDirectory + ".pending", operation.OutputDirectory!);
        var retry = new TrackingFiles();
        var result = await Execute(Plan(), paths, retry);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(1, retry.Moves, "Only the following step still needs a move.");
        Assert.AreEqual(1, retry.Copies, "Existing input isolation and renamed bytes must be reused.");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Modified_pending_or_committed_output_stops_recovery_without_moving_more_files(bool committed)
    {
        var paths = await Inputs(committed ? 1 : 2); var files = new TrackingFiles {InterruptAfterMove = 1};
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Execute(Plan(), paths, files));
        var operation = (await State()).Operations.Values.Single();
        var pending = operation.OutputDirectory + ".pending";
        var moved = Directory.GetFiles(pending, "*", SearchOption.AllDirectories).Single();
        // Keep the byte count unchanged: length alone must not authorize adoption.
        await File.WriteAllTextAsync(moved, new string('x', (int)new FileInfo(moved).Length));
        if (committed) Directory.Move(pending, operation.OutputDirectory!);
        var retry = new TrackingFiles();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Execute(Plan(), paths, retry));
        Assert.AreEqual(0, retry.Moves);
        Assert.AreEqual(committed ? 0 : 1, (await State()).Outputs["download"].Count(a => File.Exists(a.Path)));
        Assert.IsTrue(paths.All(File.Exists));
    }

    [TestMethod]
    public async Task Unsupported_move_preserves_source_and_cancelled_copy_is_never_adopted()
    {
        var paths = await Inputs(); var files = new TrackingFiles {UnsupportedMove = true, InterruptCopy = true};
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Execute(Plan(), paths, files));
        var before = await State();
        Assert.IsTrue(File.Exists(before.Outputs["download"].Single().Path));
        var operation = before.Operations.Values.Single();
        Assert.AreEqual(0, Directory.GetFiles(operation.OutputDirectory + ".pending", "*", SearchOption.AllDirectories).Length);
        var retry = new TrackingFiles {UnsupportedMove = true};
        var result = await Execute(Plan(), paths, retry);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual("original:original-0.bin", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.IsTrue(File.Exists((await State()).Outputs["download"].Single().Path));
    }

    [TestMethod]
    public async Task A_source_changed_after_journaling_cannot_commit_a_different_content_receipt()
    {
        var paths = await Inputs(); var files = new TrackingFiles {MutateBeforeMove = true};
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Execute(Plan(), paths, files));
        var state = await State();
        var operation = state.Operations.Values.Single();
        Assert.IsFalse(operation.Completed);
        Assert.IsFalse(Directory.Exists(operation.OutputDirectory));
        Assert.AreEqual(1, Directory.GetFiles(operation.OutputDirectory + ".pending").Length);
        Assert.AreEqual("original:original-0.bin", await File.ReadAllTextAsync(paths[0]));
    }

    [TestMethod]
    public async Task A_destination_appearing_mid_batch_is_verified_without_overwriting_it()
    {
        var paths = await Inputs(2); var files = new TrackingFiles {InjectNextTarget = true};
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Execute(Plan(), paths, files));
        var state = await State();
        var operation = state.Operations.Values.Single();
        Assert.AreEqual(1, files.Moves);
        Assert.IsTrue(File.Exists(state.Outputs["download"][1].Path));
        Assert.AreEqual("unrelated file", await File.ReadAllTextAsync(
            Path.Combine(operation.OutputDirectory + ".pending", "original-1.dat")));
        Assert.IsFalse(operation.Completed);
    }

    [TestMethod]
    public async Task Native_move_rejects_collisions_and_symbolic_links_without_touching_source()
    {
        var paths = await Inputs(); var materializer = new FileArtifactMaterializer();
        var target = Path.Combine(root, "occupied.bin"); await File.WriteAllTextAsync(target, "existing");
        await Assert.ThrowsExactlyAsync<IOException>(() => materializer.TryMoveAsync(paths[0], target, default));
        Assert.AreEqual("existing", await File.ReadAllTextAsync(target));
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(root, "link.bin"); File.CreateSymbolicLink(link, paths[0]);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => materializer.TryMoveAsync(link, Path.Combine(root, "new.bin"), default));
        }
        Assert.IsTrue(File.Exists(paths[0]));
    }

    private sealed class TrackingFiles : IFileArtifactMaterializer
    {
        private readonly FileArtifactMaterializer actual = new();
        public int Copies;
        public int Moves;
        public int InterruptAfterMove;
        public bool UnsupportedMove;
        public bool InterruptCopy;
        public bool MutateBeforeMove;
        public bool InjectNextTarget;
        public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {
            Copies++;
            if (InterruptCopy && target.Contains(".copying"))
            {
                await File.WriteAllTextAsync(target, "partial", ct);
                throw new OperationCanceledException("Interrupted fallback copy.");
            }
            return await actual.MaterializeAsync(source, target, "copy", ct);
        }
        public async Task<bool> TryMoveAsync(string source, string target, CancellationToken ct)
        {
            if (UnsupportedMove) return false;
            if (MutateBeforeMove) await File.WriteAllTextAsync(source, new string('x', (int)new FileInfo(source).Length), ct);
            Assert.IsTrue(await actual.TryMoveAsync(source, target, ct), "The local test filesystem must support exclusive rename.");
            Moves++;
            if (InjectNextTarget && Moves == 1)
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(target)!, "original-1.dat"), "unrelated file", ct);
            if (Moves == InterruptAfterMove) throw new OperationCanceledException("Interrupted after atomic rename.");
            return true;
        }
    }

    private sealed class NoArchives : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => throw new AssertFailedException("Rename must not probe an archive.");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress,
            CancellationToken ct) => throw new AssertFailedException("Rename must not extract an archive.");
    }
}

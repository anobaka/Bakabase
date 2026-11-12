using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FinalAssemblyTests
{
    private string root = null!;
    private string input => Path.Combine(root, "input");
    private string stateRoot => Path.Combine(root, "state");
    private string[] files = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly FileProcessingPlan Plan = new() {Requirement = "required", Steps = [
        new() {Id = "rename", Op = "renameFile", Selector = "original.bin", TargetName = "payload.bin"}]};
    [TestInitialize] public async Task Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "final-assembly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(input);
        files = [Path.Combine(input, "original.bin"), Path.Combine(input, "activation.key")];
        await File.WriteAllTextAsync(files[0], "complete immutable payload");
        await File.WriteAllTextAsync(files[1], "keep this key");
    }
    [TestCleanup] public void Cleanup() {if (Directory.Exists(root)) Directory.Delete(root, true);}
    private string StatePath => Directory.EnumerateFiles(stateRoot, "state.json", SearchOption.AllDirectories).Single();
    private async Task<FileProcessingPlanExecutor.State> Read() =>
        JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(StatePath), Json)!;
    private Task Save(FileProcessingPlanExecutor.State state) => File.WriteAllTextAsync(StatePath, JsonSerializer.Serialize(state, Json));
    private Task<FileProcessingPlanResult> Run(Files materializer, ArtifactStoragePolicy? policy = null) =>
        new FileProcessingPlanExecutor(new Archives(), materializer).ExecuteAsync(Plan, input, files, stateRoot,
            storagePolicy: policy ?? new());

    [TestMethod]
    public async Task FinalPrivateFilesMoveWithoutAnotherPayloadCopyAndReplayKeepsOriginalDownloads()
    {
        var materializer = new Files(); var result = await Run(materializer);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(2, materializer.FinalMoves);
        Assert.AreEqual(0, materializer.FinalCopies);
        Assert.IsTrue(files.All(File.Exists));
        var state = await Read(); Assert.IsNotNull(state.FinalAssembly);
        Assert.IsTrue(state.FinalAssembly.Files.Where(f => f.Move).All(f => !File.Exists(f.Source)));
        Assert.IsTrue(state.FinalAssembly.Files.All(f => state.FileTransitions.ContainsKey(
            Path.GetRelativePath(Path.GetDirectoryName(StatePath)!, f.Source).Replace('\\', '/'))));
        var copies = materializer.Copies;
        Assert.IsTrue((await Run(materializer)).Completed);
        Assert.AreEqual(copies, materializer.Copies);
        Assert.AreEqual(2, materializer.FinalMoves);
    }

    [DataTestMethod]
    [DataRow(1, false)] [DataRow(1, true)] [DataRow(2, false)] [DataRow(2, true)]
    public async Task InterruptedFinalMemberResumesBeforeReadingMovedInputsOrCreatingANewSnapshot(int member, bool afterMove)
    {
        var interrupted = new Files {InterruptMember = member, InterruptAfterMove = afterMove};
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => Run(interrupted));
        var retained = await Read(); Assert.IsNotNull(retained.FinalAssembly); Assert.IsFalse(retained.Completed);
        var recovery = new Files(); var result = await Run(recovery);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, recovery.Copies, "Recovery only moves the remaining private files; it does not recreate input snapshots.");
        CollectionAssert.AreEquivalent(new[] {"activation.key", "payload.bin"}, result.Files.Select(Path.GetFileName).ToArray());
        Assert.IsTrue(files.All(File.Exists));
    }

    [TestMethod]
    public async Task CrashAfterAtomicDirectoryCommitFinishesTheSameJournalWithoutCopying()
    {
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => Run(new Files {InterruptMember = 2, InterruptAfterMove = true}));
        var checkpoint = Path.GetDirectoryName(StatePath)!;
        Directory.Move(Path.Combine(checkpoint, "result.pending"), Path.Combine(checkpoint, "result"));
        var recovery = new Files();
        Assert.IsTrue((await Run(recovery)).Completed);
        Assert.AreEqual(0, recovery.Copies); Assert.AreEqual(0, recovery.FinalMoves);
        Assert.IsTrue((await Read()).Completed);
    }

    [DataTestMethod]
    [DataRow("unknown-file")] [DataRow("unknown-directory")] [DataRow("changed-source")]
    public async Task EverySurvivorIsCheckedBeforeMovingAnotherFile(string change)
    {
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => Run(new Files {InterruptMember = 1}));
        var state = await Read(); var pending = Path.Combine(Path.GetDirectoryName(StatePath)!, "result.pending");
        if (change == "unknown-file") await File.WriteAllTextAsync(Path.Combine(pending, "user.key"), "unknown");
        else if (change == "unknown-directory") Directory.CreateDirectory(Path.Combine(pending, "user-empty"));
        else await File.WriteAllTextAsync(state.FinalAssembly!.Files[^1].Source, "changed private content");
        var recovery = new Files();
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Run(recovery));
        Assert.AreEqual(0, recovery.FinalMoves); Assert.AreEqual(0, recovery.Copies);
        Assert.IsTrue(state.FinalAssembly!.Files.All(f => File.Exists(f.Source)));
        if (change == "unknown-file") Assert.AreEqual("unknown", await File.ReadAllTextAsync(Path.Combine(pending, "user.key")));
    }

    [DataTestMethod]
    [DataRow(true)] [DataRow(false)]
    public async Task CopyAndRetentionPoliciesKeepTheirExistingIndependentFiles(bool explicitCopy)
    {
        var materializer = new Files();
        var result = await Run(materializer, new() {Materialization = explicitCopy ? "copy" : "auto", CleanupIntermediates = explicitCopy});
        Assert.IsTrue(result.Completed);
        Assert.IsNull((await Read()).FinalAssembly);
        Assert.AreEqual(0, materializer.FinalMoves); Assert.AreEqual(2, materializer.FinalCopies);
    }

    [TestMethod]
    public async Task OnePhysicalInputProjectedToMultipleNamesIsCopiedInsteadOfMovingItTwice()
    {
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => Run(new Files {InterruptMember = 1}));
        var state = await Read(); var shared = state.Active[0];
        state.Active.Add(shared with {RelativePath = "another-name.key"});
        state.FinalAssembly = null; await Save(state);
        var recovery = new Files(); var result = await Run(recovery);
        Assert.IsTrue(result.Completed); Assert.AreEqual(3, result.Files.Count);
        Assert.IsTrue(File.Exists(shared.Path));
        Assert.AreEqual(1, recovery.FinalMoves);
        Assert.IsTrue((await Read()).FinalAssembly!.Files.Where(f => f.Source == shared.Path).All(f => !f.Move));
        Assert.AreEqual(await File.ReadAllTextAsync(shared.Path),
            await File.ReadAllTextAsync(result.Files.Single(f => Path.GetFileName(f) == "another-name.key")));
    }

    [TestMethod]
    public async Task UnsupportedAtomicMoveCopiesOnceAndKeepsItsVerifiedSources()
    {
        var materializer = new Files {FinalMoveUnsupported = true};
        Assert.IsTrue((await Run(materializer)).Completed);
        Assert.AreEqual(2, materializer.FinalCopies);
        Assert.IsTrue((await Read()).FinalAssembly!.Files.All(f => File.Exists(f.Source)));
        var copies = materializer.Copies; await Run(materializer);
        Assert.AreEqual(copies, materializer.Copies);
    }

    [DataTestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task LegacyPendingFullAndExactPrefixCopiesAreRecoveredButChangedPrefixesAreRetained(bool corrupt)
    {
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => Run(new Files {InterruptMember = 1}));
        var state = await Read(); var entries = state.FinalAssembly!.Files;
        var pending = Path.Combine(Path.GetDirectoryName(StatePath)!, "result.pending");
        foreach (var entry in entries) Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(pending, entry.RelativePath))!);
        File.Copy(entries[0].Source, Path.Combine(pending, entries[0].RelativePath));
        var prefix = (await File.ReadAllBytesAsync(entries[1].Source)).Take(3).ToArray();
        if (corrupt) prefix[0] ^= 1;
        await File.WriteAllBytesAsync(Path.Combine(pending, entries[1].RelativePath), prefix);
        state.FinalAssembly = null; await Save(state); // Pre-upgrade interrupted final copy.
        var recovery = new Files();
        if (corrupt)
        {
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Run(recovery));
            CollectionAssert.AreEqual(prefix, await File.ReadAllBytesAsync(Path.Combine(pending, entries[1].RelativePath)));
            Assert.AreEqual(0, recovery.FinalMoves);
        }
        else
        {
            var result = await Run(recovery); Assert.IsTrue(result.Completed);
            Assert.AreEqual(0, recovery.Copies); Assert.AreEqual(1, recovery.FinalMoves);
            Assert.AreEqual(3L, (await Read()).FinalAssembly!.ReplacedPartialCopies.Single().Length);
        }
        Assert.IsTrue(files.All(File.Exists));
    }

    [TestMethod]
    public async Task PinnedDynamicBoundaryRemainsReadableAcrossInterruptedFinalAssembly()
    {
        var plan = Plan with {Steps = [Plan.Steps[0] with {TargetName = "archive.zip"},
            new() {Id = "extract", Op = "extractArchive", Input = "rename"}]};
        var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives, new Files());
        var boundary = await executor.ExecuteAsync(plan, input, files, stateRoot, stopAfterStepId: "rename", storagePolicy: new());
        var sourceManifest = await ArtifactManifestFiles.CaptureAsync(input, files);
        var original = new ArtifactRecord("original", "source-op", "download", input, files, sourceManifest,
            sourceManifest.Fingerprint, ArtifactLocationState.Available, true, [], null);
        var previousManifest = await ArtifactManifestFiles.CaptureAsync(boundary.OutputDirectory!, boundary.Files);
        var previous = new ArtifactRecord("previous", "previous-op", "processedFiles", boundary.OutputDirectory!, boundary.Files,
            previousManifest, previousManifest.Fingerprint, ArtifactLocationState.Available, true, [original.Id], null);
        var proof = await FileProcessingPlanExecutor.ReadPinnedInputAsync(stateRoot, plan, original, previous, "rename", null, default);
        Assert.IsNotNull(proof);
        var interrupted = new FileProcessingPlanExecutor(archives, new Files {InterruptMember = 1, InterruptAfterMove = true});
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => interrupted.ExecuteAsync(plan, input, files, stateRoot,
            stopAfterStepId: "extract", storagePolicy: new(), checkpointInput: proof));
        var recovery = new Files();
        var final = await new FileProcessingPlanExecutor(archives, recovery).ExecuteAsync(plan, input, files, stateRoot,
            stopAfterStepId: "extract", storagePolicy: new(), checkpointInput: proof);
        Assert.IsTrue(final.Completed); Assert.AreEqual(1, archives.Extractions);
        Assert.IsTrue(boundary.Files.All(File.Exists)); Assert.IsTrue(files.All(File.Exists));
        Assert.IsTrue((await Read()).FinalAssembly!.Files.Where(f => f.Source.Contains(Path.DirectorySeparatorChar + "inputs" + Path.DirectorySeparatorChar)).All(f => !f.Move));
        var replay = await executor.ExecuteAsync(plan, input, files, stateRoot, stopAfterStepId: "rename", storagePolicy: new());
        CollectionAssert.AreEqual(boundary.Files.ToArray(), replay.Files.ToArray());
    }

    private sealed class Files : IFileArtifactMaterializer
    {
        public int Copies; public int FinalCopies; public int FinalMoves;
        public int InterruptMember; public bool InterruptAfterMove; public bool FinalMoveUnsupported;
        private static bool Final(string target) => target.Contains("result.pending" + Path.DirectorySeparatorChar) ||
            target.Contains(Path.DirectorySeparatorChar + "final-copy-", StringComparison.Ordinal);
        public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {
            Copies++; if (Final(target)) FinalCopies++;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var read = File.OpenRead(source); await using var write = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            await read.CopyToAsync(write, ct); return new("copy", read.Length);
        }
        public Task<bool> TryMoveAsync(string source, string target, CancellationToken ct)
        {
            var final = Final(target);
            if (final)
            {
                FinalMoves++;
                if (FinalMoveUnsupported) return Task.FromResult(false);
                if (FinalMoves == InterruptMember && !InterruptAfterMove) throw new OperationCanceledException("before move");
            }
            File.Move(source, target);
            if (final && FinalMoves == InterruptMember && InterruptAfterMove) throw new OperationCanceledException("after move");
            return Task.FromResult(true);
        }
    }
    private sealed class Archives : IArchiveExtractionService
    {
        public int Extractions;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct) =>
            Task.FromResult(new ArchivePasswordProbe(true, null, [], "", null));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Extractions++;
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "media.txt"), "extracted media", ct);
            return new(true, request.Directory, "");
        }
    }
}

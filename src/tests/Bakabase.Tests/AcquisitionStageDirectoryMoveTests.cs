using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionStageDirectoryMoveTests
{
    private string root = null!;
    [TestInitialize] public void Setup() {root = Path.Combine(Path.GetTempPath(), "stage-move-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);}
    [TestCleanup] public void Cleanup() {if (Directory.Exists(root)) Directory.Delete(root, true);}
    private async Task<AcquisitionStageDirectoryMove> Prepare(string content = "original")
    {
        var source = Path.Combine(root, "old"); Directory.CreateDirectory(Path.Combine(source, "author"));
        await File.WriteAllTextAsync(Path.Combine(source, "author", "a.txt"), content);
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        var selection = AcquisitionStageDirectoryFileMove.Inspect(source, new HashSet<string>(), default);
        return new("part", "processing", source, Path.Combine(root, "new"), true, selection.Files, selection.EmptyDirectories);
    }

    [TestMethod]
    public async Task SameVolumeMovesPayloadAndPreservesAuthorDirectoriesAndUnrelatedDestinationFiles()
    {
        var move = await Prepare(); Directory.CreateDirectory(move.Directory);
        await File.WriteAllTextAsync(Path.Combine(move.Directory, "existing.txt"), "keep");
        AcquisitionStageDirectoryFileMove.CheckTargets(move);
        await AcquisitionStageDirectoryFileMove.ExecuteAsync(move, Guid.NewGuid().ToString("N"), new FileArtifactMaterializer(),
            () => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(move.Directory, "author", "a.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(move.SourceDirectory!, "author", "a.txt")));
        Assert.IsTrue(Directory.Exists(Path.Combine(move.Directory, "empty")));
        Assert.IsTrue(Directory.Exists(move.SourceDirectory));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(move.Directory, "existing.txt")));
    }

    [TestMethod]
    public async Task ExistingTargetNeverOverwritten()
    {
        var move = await Prepare(); Directory.CreateDirectory(Path.Combine(move.Directory, "author"));
        await File.WriteAllTextAsync(Path.Combine(move.Directory, "author", "a.txt"), "different");
        Assert.ThrowsExactly<IOException>(() => AcquisitionStageDirectoryFileMove.CheckTargets(move));
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move,
            Guid.NewGuid().ToString("N"), new FileArtifactMaterializer(), () => Task.CompletedTask, () => Task.CompletedTask, default));
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(move.SourceDirectory!, "author", "a.txt")));
        Assert.AreEqual("different", await File.ReadAllTextAsync(Path.Combine(move.Directory, "author", "a.txt")));
    }

    [TestMethod]
    public async Task OwnedMetadataExcludedWithoutTreatingPrefixAsEvidence()
    {
        var move = await Prepare(); var metadata = Path.Combine(move.SourceDirectory!, ".bakabase-work", "owned");
        Directory.CreateDirectory(metadata); await File.WriteAllTextAsync(Path.Combine(metadata, "state.json"), "history");
        var author = Path.Combine(move.SourceDirectory!, "bakabase-author"); Directory.CreateDirectory(author);
        await File.WriteAllTextAsync(Path.Combine(author, "key"), "key");
        var selection = AcquisitionStageDirectoryFileMove.Inspect(move.SourceDirectory!, new HashSet<string> {metadata}, default);
        Assert.AreEqual(2, selection.Files.Count);
        Assert.IsTrue(selection.Files.Any(f => f.RelativePath == Path.Combine("bakabase-author", "key")));
        Assert.IsFalse(selection.Files.Any(f => f.RelativePath.EndsWith("state.json")));
    }

    [TestMethod]
    public async Task AtomicMoveCanReconcileCrashBeforeMetadataSave()
    {
        var move = await Prepare(); var native = new FileArtifactMaterializer();
        Directory.CreateDirectory(Path.Combine(move.Directory, "author"));
        Assert.IsTrue(await native.TryMoveAsync(Path.Combine(move.SourceDirectory!, "author", "a.txt"), Path.Combine(move.Directory, "author", "a.txt"), default));
        await AcquisitionStageDirectoryFileMove.ExecuteAsync(move, Guid.NewGuid().ToString("N"), native,
            () => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.IsTrue(move.Files.Single().Completed);
    }

    [TestMethod]
    public async Task MissingSourceDoesNotAcceptUnrelatedTarget()
    {
        var move = await Prepare(); File.Delete(Path.Combine(move.SourceDirectory!, "author", "a.txt"));
        Directory.CreateDirectory(Path.Combine(move.Directory, "author"));
        await File.WriteAllTextAsync(Path.Combine(move.Directory, "author", "a.txt"), "unrelated");
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move,
            Guid.NewGuid().ToString("N"), new FileArtifactMaterializer(), () => Task.CompletedTask, () => Task.CompletedTask, default));
        Assert.AreEqual("unrelated", await File.ReadAllTextAsync(Path.Combine(move.Directory, "author", "a.txt")));
    }

    [TestMethod]
    public async Task CrossVolumeCopyVerifiesBeforeRemovingOriginal()
    {
        var move = await Prepare(); var saves = 0;
        await AcquisitionStageDirectoryFileMove.ExecuteAsync(move, Guid.NewGuid().ToString("N"), new CrossVolumeMaterializer(),
            () => {saves++; return Task.CompletedTask;}, () => Task.CompletedTask, default);
        Assert.IsNotNull(move.Files.Single().CopiedHash); Assert.IsTrue(saves >= 2);
        Assert.IsFalse(File.Exists(Path.Combine(move.SourceDirectory!, "author", "a.txt")));
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(move.Directory, "author", "a.txt")));
    }

    [TestMethod]
    public async Task CorruptCopyKeepsOriginal()
    {
        var move = await Prepare();
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move,
            Guid.NewGuid().ToString("N"), new CrossVolumeMaterializer(corrupt: true),
            () => Task.CompletedTask, () => Task.CompletedTask, default));
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(move.SourceDirectory!, "author", "a.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(move.Directory, "author", "a.txt")));
    }

    [TestMethod]
    public async Task InterruptedCrossVolumeCopyRestartsOnlyReceiptOwnedPartialPayload()
    {
        var move = await Prepare(); var id = Guid.NewGuid().ToString("N"); var interrupt = new InterruptedCopy();
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move, id,
            interrupt, () => Task.CompletedTask, () => Task.CompletedTask, default));
        Assert.IsTrue(File.Exists(Path.Combine(move.SourceDirectory!, "author", "a.txt")));
        Assert.IsNull(move.Files.Single().CopiedHash);
        await AcquisitionStageDirectoryFileMove.ExecuteAsync(move, id, new CrossVolumeMaterializer(),
            () => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(move.Directory, "author", "a.txt")));
        Assert.IsFalse(Directory.EnumerateDirectories(move.Directory, ".bakabase-directory-move-*").Any());
    }

    [TestMethod]
    public async Task InterruptedCopyCannotDiscardAnUnknownSiblingOrDifferentOwnerReceipt()
    {
        var move = await Prepare(); var id = Guid.NewGuid().ToString("N");
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move, id,
            new InterruptedCopy(), () => Task.CompletedTask, () => Task.CompletedTask, default));
        var temporary = Directory.GetDirectories(move.Directory, ".bakabase-directory-move-*").Single();
        var unknown = Path.Combine(temporary, "manual.key"); await File.WriteAllTextAsync(unknown, "preserve");
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move, id,
            new CrossVolumeMaterializer(), () => Task.CompletedTask, () => Task.CompletedTask, default));
        Assert.AreEqual("preserve", await File.ReadAllTextAsync(unknown)); Assert.IsTrue(File.Exists(Path.Combine(temporary, "payload")));
    }

    [TestMethod]
    public async Task InterruptedOwnerReceiptIsReportedWithoutDeletingUnprovenTemporaryFiles()
    {
        var move = await Prepare(); var id = Guid.NewGuid().ToString("N");
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move, id,
            new InterruptedCopy(), () => Task.CompletedTask, () => Task.CompletedTask, default));
        var temporary = Directory.GetDirectories(move.Directory, ".bakabase-directory-move-*").Single();
        await File.WriteAllTextAsync(Path.Combine(temporary, "owner.json"), "{unfinished");
        var error = await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move, id,
            new CrossVolumeMaterializer(), () => Task.CompletedTask, () => Task.CompletedTask, default));
        Assert.AreEqual("STAGE_DIRECTORY_MOVE_INTERRUPTED", error.Message);
        Assert.IsTrue(File.Exists(Path.Combine(temporary, "payload")));
        Assert.AreEqual("original", await File.ReadAllTextAsync(Path.Combine(move.SourceDirectory!, "author", "a.txt")));
    }

    [TestMethod]
    public async Task SourceChangeAfterPreviewRefusesMove()
    {
        var move = await Prepare(); await File.WriteAllTextAsync(Path.Combine(move.SourceDirectory!, "author", "a.txt"), "changed original");
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move,
            Guid.NewGuid().ToString("N"), new FileArtifactMaterializer(), () => Task.CompletedTask, () => Task.CompletedTask, default));
        Assert.IsFalse(File.Exists(Path.Combine(move.Directory, "author", "a.txt")));
    }

    [TestMethod]
    public async Task StorageRootDisappearingCannotBeTreatedAsCompletedMove()
    {
        var move = await Prepare(); Directory.CreateDirectory(Path.Combine(move.Directory, "author"));
        var item = move.Files.Single(); var target = Path.Combine(move.Directory, item.RelativePath);
        File.Copy(Path.Combine(move.SourceDirectory!, item.RelativePath), target);
        File.SetLastWriteTimeUtc(target, new DateTime(item.LastWriteUtcTicks, DateTimeKind.Utc));
        Directory.Delete(move.SourceDirectory!, true);
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move,
            Guid.NewGuid().ToString("N"), new FileArtifactMaterializer(), () => Task.CompletedTask, () => Task.CompletedTask, default));
        Assert.IsTrue(File.Exists(target)); Assert.IsFalse(move.Files.Single().Completed);
    }

    [TestMethod]
    public async Task CrossVolumeSourceChangedDuringFinalGuardIsPreserved()
    {
        var move = await Prepare(); var source = Path.Combine(move.SourceDirectory!, "author", "a.txt");
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionStageDirectoryFileMove.ExecuteAsync(move,
            Guid.NewGuid().ToString("N"), new CrossVolumeMaterializer(), () => Task.CompletedTask,
            () => File.WriteAllTextAsync(source, "changed during final guard"), default));
        Assert.AreEqual("changed during final guard", await File.ReadAllTextAsync(source));
    }

    [TestMethod]
    public async Task SymlinkIsNeverFollowed()
    {
        var move = await Prepare(); var outside = Path.Combine(root, "outside.txt"); await File.WriteAllTextAsync(outside, "keep");
        File.CreateSymbolicLink(Path.Combine(move.SourceDirectory!, "link"), outside);
        Assert.ThrowsExactly<InvalidDataException>(() => AcquisitionStageDirectoryFileMove.Inspect(move.SourceDirectory!, new HashSet<string>(), default));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(outside));
    }

    [TestMethod]
    public void RelativePathCannotEscapeEitherRoot() => Assert.ThrowsExactly<InvalidDataException>(() =>
        AcquisitionStageDirectoryFileMove.Resolve(root, "../outside"));

    private sealed class CrossVolumeMaterializer(bool corrupt = false) : IFileArtifactMaterializer
    {
        private readonly FileArtifactMaterializer native = new();
        public Task<bool> TryMoveAsync(string source, string target, CancellationToken ct) =>
            Path.GetFileName(Path.GetDirectoryName(source)!).StartsWith(".bakabase-directory-move-", StringComparison.Ordinal)
                ? native.TryMoveAsync(source, target, ct) : Task.FromResult(false);
        public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {
            File.Copy(source, target);
            if (corrupt) await File.WriteAllTextAsync(target, "corrupt!", ct);
            return new("copy", new FileInfo(target).Length);
        }
    }
    private sealed class InterruptedCopy : IFileArtifactMaterializer
    {
        public Task<bool> TryMoveAsync(string source, string target, CancellationToken ct) => Task.FromResult(false);
        public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {await File.WriteAllTextAsync(target, "partial", ct); throw new IOException("simulated disk full");}
    }
}

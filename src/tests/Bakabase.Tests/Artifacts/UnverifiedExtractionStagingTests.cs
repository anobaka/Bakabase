using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class UnverifiedExtractionStagingTests
{
    private string root = null!;
    private string input = null!;
    private string checkpoint = null!;
    private string[] files = [];
    private static readonly FileProcessingPlan Plan = new()
    {
        Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]
    };

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "unverified-archive-" + Guid.NewGuid().ToString("N"));
        input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
        checkpoint = Path.Combine(root, "checkpoint");
        files = new[] {"a.zip", "b.zip"}.Select(name => Path.Combine(input, name)).ToArray();
        foreach (var path in files) File.WriteAllText(path, Path.GetFileName(path));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Declined_or_throwing_extractors_release_only_their_uncommitted_group(bool throws)
    {
        var extractor = new Archives();
        var sentinel = Path.Combine(root, "unrelated");
        await File.WriteAllTextAsync(sentinel, "keep");
        extractor.Interrupt = (request, _) =>
        {
            Directory.CreateDirectory(request.Directory + ".another");
            File.WriteAllText(Path.Combine(request.Directory + ".another", "sibling.txt"), "another group");
            if (throws) throw new InvalidDataException("catalog read failed");
            return Task.FromResult(new ArchiveExtractionResult(false, request.Directory, "writer failed"));
        };
        var executor = new FileProcessingPlanExecutor(extractor);
        if (throws)
            Assert.AreEqual("catalog read failed", (await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => executor.ExecuteAsync(Plan, input, files, checkpoint))).Message);
        else
            Assert.AreEqual("writer failed", (await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => executor.ExecuteAsync(Plan, input, files, checkpoint))).Message);

        Assert.IsFalse(File.Exists(extractor.Partial));
        Assert.AreEqual("another group", await File.ReadAllTextAsync(Path.Combine(extractor.LastDirectory + ".another", "sibling.txt")));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(sentinel));
        Assert.IsTrue(Directory.EnumerateFiles(checkpoint, "a.done", SearchOption.AllDirectories).Any());
        foreach (var path in files) Assert.AreEqual(Path.GetFileName(path), await File.ReadAllTextAsync(path));
        var abandonedPath = extractor.LastDirectory;
        Directory.CreateDirectory(abandonedPath);
        var laterFile = Path.Combine(abandonedPath, "created-after-failure.txt");
        await File.WriteAllTextAsync(laterFile, "owned by a later operation");
        extractor.Interrupt = null;
        var recovered = await executor.ExecuteAsync(Plan, input, files, checkpoint);
        Assert.AreNotEqual(abandonedPath, extractor.LastDirectory);
        Assert.AreEqual("owned by a later operation", await File.ReadAllTextAsync(laterFile));
        Assert.IsTrue(recovered.Completed, recovered.Message);
        CollectionAssert.AreEqual(new[] {"a.zip", "b.zip", "b.zip"}, extractor.Attempts.ToArray());
        CollectionAssert.AreEquivalent(new[] {"a.done", "b.done"}, recovered.Files.Select(Path.GetFileName).ToArray());
    }

    [TestMethod, Timeout(10000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_drains_the_writer_before_discarding_its_bytes(bool reserveReached)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var draining = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var owner = StorageWriteScope.Enter(new("workflow", "47"));
        long free = 1000;
        var guard = new StorageWriteGuard(new Floor(), _ => ("fixture", Interlocked.Read(ref free)), TimeSpan.FromMilliseconds(5));
        var extractor = new Archives();
        extractor.Interrupt = async (request, token) =>
        {
            using var held = File.OpenRead(extractor.Partial);
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally
            {
                draining.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            return new(true, request.Directory, "");
        };
        var executor = new FileProcessingPlanExecutor(extractor, writeGuard: guard);
        var run = executor.ExecuteAsync(Plan, input, files, checkpoint, ct: cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (reserveReached) Interlocked.Exchange(ref free, 99);
            else cancellation.Cancel();
            await draining.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsTrue(File.Exists(extractor.Partial), "The worker has observed cancellation and still owns the file.");
            Assert.IsFalse(run.IsCompleted);
        }
        finally { release.TrySetResult(); }
        if (reserveReached)
        {
            var wait = await run;
            Assert.AreEqual("StorageUnavailable", wait.FailureCode);
            Assert.AreEqual(StorageWriteGuard.ReserveLowCode, wait.Message);
        }
        else await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        Assert.IsFalse(File.Exists(extractor.Partial));
        Assert.IsTrue(File.Exists(files[1]));
        Assert.IsTrue(Directory.EnumerateFiles(checkpoint, "a.done", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    [DataRow("smb")]
    [DataRow("symlink")]
    [DataRow("replaced")]
    public async Task Ambiguous_staging_is_left_intact_and_preserves_the_original_error(string hazard)
    {
        if (hazard == "symlink" && OperatingSystem.IsWindows())
            Assert.Inconclusive("This fixture needs permission to create a symbolic link.");
        var external = Path.Combine(root, "keep.txt");
        await File.WriteAllTextAsync(external, "untouched");
        string special = "", original = "";
        var extractor = new Archives();
        extractor.Interrupt = (request, _) =>
        {
            original = request.Directory;
            if (hazard == "replaced")
            {
                original += ".moved";
                Directory.Move(request.Directory, original);
                Directory.CreateDirectory(request.Directory);
                special = Path.Combine(request.Directory, "belongs-to-another-writer");
                File.WriteAllText(special, "untouched");
            }
            else if (hazard == "symlink")
            {
                special = Path.Combine(request.Directory, "alias");
                File.CreateSymbolicLink(special, external);
            }
            else
            {
                special = Path.Combine(request.Directory, ".smbdeleteAAA12345.4");
                File.WriteAllText(special, "untouched");
            }
            return Task.FromResult(new ArchiveExtractionResult(false, request.Directory, "provider stopped"));
        };
        var executor = new FileProcessingPlanExecutor(extractor);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(Plan, input, files, checkpoint));
        Assert.AreEqual("provider stopped", error.Message);
        Assert.AreEqual("untouched", await File.ReadAllTextAsync(external));
        Assert.AreEqual("untouched", await File.ReadAllTextAsync(special));
        Assert.AreEqual("partial group", await File.ReadAllTextAsync(Path.Combine(original, "partial.data")));
    }

    [TestMethod, Timeout(10000)]
    public async Task Cancellation_before_capacity_admission_leaves_files_the_writer_never_owned()
    {
        using var owner = StorageWriteScope.Enter(new("workflow", "47"));
        using var cancellation = new CancellationTokenSource();
        var guard = new StorageWriteGuard(new Floor(),
            _ => ("fixture", 100 + StorageWriteGuard.ReservationHeadroomBytes + 32), TimeSpan.FromMilliseconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingWriter = guard.RunReservedAsync([new(Path.Combine(root, "other"), 32)],
            new("downloadTask", "19"), async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return true; });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var extractor = new Archives {ExpandedBytes = 32};
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = new FileProcessingPlanExecutor(extractor, writeGuard: guard);
            var run = executor.ExecuteAsync(Plan, input, [files[0]], checkpoint,
                reportStage: stage =>
                {
                    if (stage == "waitingForStorageCapacity") waiting.TrySetResult();
                    return Task.CompletedTask;
                }, ct: cancellation.Token);
            try
            {
                await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.AreEqual(0, extractor.Attempts.Count);
                var sentinel = Path.Combine(extractor.ListedDirectory!, "written-by-another-operation.txt");
                await File.WriteAllTextAsync(sentinel, "independent write");
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => run);
                Assert.AreEqual(0, extractor.Attempts.Count);
                Assert.AreEqual("independent write", await File.ReadAllTextAsync(sentinel));

                release.TrySetResult();
                await blockingWriter;
                var resumed = await executor.ExecuteAsync(Plan, input, [files[0]], checkpoint);
                Assert.IsTrue(resumed.Completed, resumed.Message);
                Assert.AreEqual(1, extractor.Attempts.Count);
                Assert.AreNotEqual(Path.GetDirectoryName(sentinel), extractor.LastDirectory);
                Assert.AreEqual("independent write", await File.ReadAllTextAsync(sentinel));
            }
            finally
            {
                cancellation.Cancel();
                try { await run; } catch (OperationCanceledException) { }
            }
        }
        finally { release.TrySetResult(); await blockingWriter; }
    }

    private sealed class Floor : IStorageWritePolicyProvider
    {
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? owner, CancellationToken ct = default) =>
            Task.FromResult(100L);
    }

    private sealed class Archives : IArchiveExtractionService
    {
        internal readonly List<string> Attempts = [];
        internal string LastDirectory = "";
        internal string Partial => Path.Combine(LastDirectory, "partial.data");
        internal long? ExpandedBytes { get; init; }
        internal string? ListedDirectory { get; private set; }
        public Task<long?> TryGetExpandedBytesAsync(ArchiveExtractionRequest request, CancellationToken ct)
        {
            ListedDirectory = request.Directory;
            return Task.FromResult(ExpandedBytes);
        }
        internal Func<ArchiveExtractionRequest,CancellationToken,Task<ArchiveExtractionResult>>? Interrupt;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? progress, CancellationToken ct)
        {
            var archive = Path.GetFileName(request.Files[0]);
            Attempts.Add(archive);
            LastDirectory = request.Directory;
            Directory.CreateDirectory(LastDirectory);
            if (archive == "b.zip" && Interrupt != null)
            {
                await File.WriteAllTextAsync(Partial, "partial group", ct);
                return await Interrupt(request, ct);
            }
            await File.WriteAllTextAsync(Path.Combine(LastDirectory, Path.GetFileNameWithoutExtension(archive) + ".done"), archive, ct);
            return new(true, LastDirectory, "");
        }
    }
}

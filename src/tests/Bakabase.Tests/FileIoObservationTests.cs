using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Tests;

[TestClass]
public sealed class FileIoObservationTests
{
    private static readonly StorageWriteContext Workflow = new("workflow", "12");
    private static readonly StorageWriteContext Download = new("downloadTask", "12");

    [TestMethod]
    public async Task Parallel_streams_roll_over_without_losing_bytes_or_mixing_owners()
    {
        var provider = new FileIoObservationProvider();
        using var active = provider.Begin(Workflow, observeRead: true, observeWrite: false);
        Assert.AreEqual(new FileIoObservationSample("workflow", "12", 0, 0, true, false), provider.Drain().Single());
        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            using var combined = provider.Begin(Workflow, observeRead: true, observeWrite: true);
            using var networkOutput = provider.Begin(Download, observeRead: false, observeWrite: true);
            for (var i = 0; i < 1000; i++)
            {
                combined.Add(19, 37);
                networkOutput.Add(999, 43);
            }
        })).ToArray();

        var observed = new List<FileIoObservationSample>();
        var completed = Task.WhenAll(workers);
        while (!completed.IsCompleted)
        {
            observed.AddRange(provider.Drain());
            await Task.Delay(1);
        }
        await completed;
        active.Add(7, 0);
        active.Dispose();
        observed.AddRange(provider.Drain());
        var workflow = observed.Where(x => x.OwnerKind == "workflow").ToArray();
        var download = observed.Where(x => x.OwnerKind == "downloadTask").ToArray();
        Assert.AreEqual(76_007L, workflow.Sum(x => x.ReadBytes));
        Assert.AreEqual(148_000L, workflow.Sum(x => x.WrittenBytes));
        Assert.AreEqual(172_000L, download.Sum(x => x.WrittenBytes));
        Assert.IsTrue(download.All(x => x.ReadBytes == 0 && !x.ReadObserved && x.WriteObserved));
        Assert.AreEqual(0, provider.Drain().Count, "Completed owners do not remain in the sampler.");
    }

    [TestMethod]
    public async Task File_copy_and_file_hash_record_real_bytes_under_the_same_active_owner()
    {
        var provider = new FileIoObservationProvider();
        var guard = new StorageWriteGuard(new NoReserve(), provider);
        var directory = Directory.CreateTempSubdirectory("file-io-test-");
        try
        {
            var source = Path.Combine(directory.FullName, "source");
            var target = Path.Combine(directory.FullName, "target");
            var bytes = new byte[1024 * 1024 + 17];
            await File.WriteAllBytesAsync(source, bytes);

            using var owner = FileIoObservationScope.Enter(provider, Workflow);
            await using (var input = File.OpenRead(source))
            await using (var output = File.OpenWrite(target))
                await guard.CopyFileAsync(input, output, target, new("cloudOperation", "independent-reserve-key"));
            Assert.AreEqual(new FileIoObservationSample("workflow", "12", bytes.Length, bytes.Length, true, true),
                provider.Drain().Single());

            await using var file = File.OpenRead(target);
            await ArtifactContentHash.ComputeAsync(file, default);
            Assert.AreEqual(new FileIoObservationSample("workflow", "12", bytes.Length, 0, true, false),
                provider.Drain().Single());
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Nonfile_input_and_failed_write_are_not_reported_as_successful_file_bytes()
    {
        var provider = new FileIoObservationProvider();
        var guard = new StorageWriteGuard(new NoReserve(), provider);
        var target = Path.Combine(Path.GetTempPath(), "io-telemetry-test-output");
        using var source = new MemoryStream([1, 2, 3]);
        using var destination = new MemoryStream();
        await guard.CopyAsync(source, destination, target, Download);
        Assert.AreEqual(new FileIoObservationSample("downloadTask", "12", 0, 3, false, true), provider.Drain().Single());

        source.Position = 0;
        using var failed = new FailingWriter();
        await Assert.ThrowsExactlyAsync<IOException>(() => guard.CopyFileAsync(source, failed, target, Workflow));
        Assert.AreEqual(new FileIoObservationSample("workflow", "12", 3, 0, true, true), provider.Drain().Single());
    }

    [TestMethod]
    public void Archive_process_observation_distinguishes_logical_counters_from_storage_device_counters()
    {
        using var both = new StringReader("rchar: 12345\nwchar: 67890\nsyscr: 5\nread_bytes: 2345\nwrite_bytes: 7890\n");
        Assert.AreEqual((12345L, 67890L), ArchiveProcessIo.ReadCounters(both)!.Value);
        using var deviceOnly = new StringReader("read_bytes: 2345\nwrite_bytes: 7890\n");
        Assert.IsNull(ArchiveProcessIo.ReadCounters(deviceOnly));
    }

    private sealed class NoReserve : IStorageWritePolicyProvider
    {
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class FailingWriter : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            ValueTask.FromException(new IOException("fixture writer failed"));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudPayloadHashTests
{
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(1024 * 1024 + 17)]
    public async Task Sequential_hash_matches_SHA256_including_empty_and_partial_final_chunks(int length)
    {
        var bytes = new byte[length];
        new Random(17).NextBytes(bytes);
        using var source = new ObservedStream(bytes);

        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)),
            await CloudPayloadHash.ComputeAsync(source, CancellationToken.None));
        Assert.IsTrue(source.CanRead, "The caller retains ownership of the supplied stream.");
        Assert.IsTrue(source.RequestSizes.TrueForAll(size => size == 1024 * 1024));
        Assert.AreEqual((int)Math.Ceiling(length / (double)(1024 * 1024)) + 1, source.RequestSizes.Count);
    }

    [TestMethod]
    public async Task Short_reads_are_hashed_until_EOF_without_hashing_stale_buffer_bytes()
    {
        var bytes = new byte[12345];
        new Random(23).NextBytes(bytes);
        using var source = new ObservedStream(bytes) {MaximumRead = 137};

        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)),
            await CloudPayloadHash.ComputeAsync(source, CancellationToken.None));
        Assert.IsTrue(source.RequestSizes.Count > 2);
        Assert.IsTrue(source.RequestSizes.TrueForAll(size => size == 1024 * 1024));
    }

    [TestMethod]
    public async Task Cancellation_before_and_during_reads_never_returns_a_partial_hash()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var unread = new ObservedStream([1, 2, 3]);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => CloudPayloadHash.ComputeAsync(unread, cancelled.Token));
        Assert.AreEqual(0, unread.RequestSizes.Count);

        using var cancellation = new CancellationTokenSource();
        using var source = new ObservedStream([1, 2, 3]) {AfterRead = cancellation.Cancel};
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => CloudPayloadHash.ComputeAsync(source, cancellation.Token));
        Assert.AreEqual(1, source.RequestSizes.Count);
        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    public async Task Cancellation_interrupts_an_outstanding_async_read()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new WaitingStream();
        var hashing = CloudPayloadHash.ComputeAsync(source, cancellation.Token);
        await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try
        {
            await hashing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("An interrupted read must not return a hash.");
        }
        catch (OperationCanceledException) { }
        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    public async Task File_hash_preserves_size_and_hex_format_and_releases_its_file_handle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cloud-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "payload");
        try
        {
            byte[] bytes = [0, 1, 2, 3, 254, 255];
            await File.WriteAllBytesAsync(path, bytes);
            var result = await CloudPayloadHash.ComputeFileAsync(path, CancellationToken.None);
            Assert.AreEqual((long)bytes.Length, result.Length);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.AreEqual((long)bytes.Length, exclusive.Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task Exact_payload_receipt_uses_newly_published_handle_when_cached_name_size_is_stale()
    {
        var directory = Directory.CreateTempSubdirectory("cloud-handoff-");
        var path = Path.Combine(directory.FullName, "payload");
        try
        {
            await File.WriteAllBytesAsync(path, []);
            var cached = new FileInfo(path);
            Assert.AreEqual(0L, cached.Length);
            var bytes = new byte[] { 11, 12, 13 };
            await File.WriteAllBytesAsync(path + ".download", bytes);
            File.Move(path + ".download", path, overwrite: true);
            Assert.AreEqual(0L, cached.Length, "The retained directory metadata describes the predecessor.");
            var receipt = await CloudPayloadHash.ComputeVerifiedFileAsync(path, bytes.Length, default);
            Assert.AreEqual((long)bytes.Length, receipt.Length);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(bytes)), receipt.Sha256);
            await Assert.ThrowsExactlyAsync<IOException>(() => CloudPayloadHash.ComputeVerifiedFileAsync(path, cached.Length, default));
        }
        finally { directory.Delete(true); }
    }

    [TestMethod]
    public async Task Replacing_the_named_payload_during_a_read_invalidates_its_receipt()
    {
        if (OperatingSystem.IsWindows()) return; // The sharing mode on Windows already denies the rename.
        var directory = Directory.CreateTempSubdirectory("cloud-handoff-");
        var path = Path.Combine(directory.FullName, "payload");
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            await File.WriteAllBytesAsync(path + ".other", [4, 5, 6]);
            await Assert.ThrowsExactlyAsync<IOException>(() => CloudPayloadHash.ComputeVerifiedFileAsync(path, 3,
                CancellationToken.None, () =>
                {
                    File.Move(path + ".other", path, overwrite: true);
                    return ValueTask.CompletedTask;
                }));
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(path));
        }
        finally { directory.Delete(true); }
    }

    private sealed class ObservedStream(byte[] data) : MemoryStream(data, writable: false)
    {
        internal List<int> RequestSizes { get; } = [];
        internal int MaximumRead { get; init; } = int.MaxValue;
        internal Action? AfterRead { get; init; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestSizes.Add(buffer.Length);
            var read = base.Read(buffer.Span[..Math.Min(buffer.Length, MaximumRead)]);
            AfterRead?.Invoke();
            return ValueTask.FromResult(read);
        }
    }

    private sealed class WaitingStream() : MemoryStream([1, 2, 3], writable: false)
    {
        internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}

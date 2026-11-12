using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class ArtifactContentHashTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(1024 * 1024 + 17)]
    [DataRow(4 * 1024 * 1024)]
    [DataRow(4 * 1024 * 1024 + 17)]
    public async Task Full_content_SHA256_and_requested_read_sizes_are_stable(int length)
    {
        var bytes = new byte[length]; new Random(71).NextBytes(bytes);
        using var source = new Observed(bytes);
        CollectionAssert.AreEqual(SHA256.HashData(bytes), await ArtifactContentHash.ComputeAsync(source, default));
        Assert.IsTrue(source.CanRead, "The caller retains its open handle for post-hash stability verification.");
        Assert.IsTrue(source.Requests.All(size => size == 4 * 1024 * 1024));
        Assert.AreEqual((int)Math.Ceiling(length / (double)(4 * 1024 * 1024)) + 1, source.Requests.Count);
    }

    [TestMethod]
    public async Task Short_reads_do_not_end_hashing_or_include_stale_buffer_bytes()
    {
        var bytes = new byte[54321]; new Random(17).NextBytes(bytes);
        using var source = new Observed(bytes) {MaximumRead = 113};
        CollectionAssert.AreEqual(SHA256.HashData(bytes), await ArtifactContentHash.ComputeAsync(source, default));
        Assert.IsTrue(source.Requests.Count > 400);
    }

    [TestMethod]
    public async Task Cancellation_before_and_after_reads_never_yields_partial_digest()
    {
        using var early = new CancellationTokenSource(); early.Cancel();
        using var untouched = new Observed([1, 2, 3]);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => ArtifactContentHash.ComputeAsync(untouched, early.Token).AsTask());
        Assert.AreEqual(0, untouched.Requests.Count);

        using var during = new CancellationTokenSource();
        using var source = new Observed([1, 2, 3]) {AfterRead = during.Cancel};
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => ArtifactContentHash.ComputeAsync(source, during.Token).AsTask());
        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    public async Task Read_errors_remain_errors_and_the_callers_handle_is_retained()
    {
        using var source = new Observed([1, 2, 3]) {Failure = new IOException("fixture read failure")};
        var error = await Assert.ThrowsExceptionAsync<IOException>(() => ArtifactContentHash.ComputeAsync(source, default).AsTask());
        Assert.AreEqual("fixture read failure", error.Message);
        Assert.IsTrue(source.CanRead);
    }

    private sealed class Observed(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        internal List<int> Requests {get;} = [];
        internal int MaximumRead {get;init;} = int.MaxValue;
        internal Action? AfterRead {get;init;}
        internal Exception? Failure {get;init;}
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(buffer.Length);
            if (Failure != null) return ValueTask.FromException<int>(Failure);
            var read = base.Read(buffer.Span[..Math.Min(buffer.Length, MaximumRead)]);
            AfterRead?.Invoke();
            return ValueTask.FromResult(read);
        }
    }
}

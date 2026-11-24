using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Components;
using MonoTorrent;
using MonoTorrent.PieceWriter;
using ReusableTasks;

namespace Bakabase.Tests;

[TestClass]
public sealed class ObservedTorrentPieceWriterTests
{
    [TestMethod]
    public async Task Successful_payload_io_counts_actual_bytes_and_flush_and_failure_do_not_add_bytes()
    {
        var provider = new FileIoObservationProvider();
        using var observation = provider.Begin(new("downloadTask", "torrent"), observeRead: true, observeWrite: true);
        var disk = new TestWriter();
        using var writer = new ObservedTorrentPieceWriter(disk, observation);
        var buffer = new byte[16];

        Assert.AreEqual(3, await writer.ReadAsync(null!, 12, buffer));
        await writer.WriteAsync(null!, 20, buffer.AsMemory(1, 5));
        await writer.FlushAsync(null!);
        disk.FailWrites = true;
        await Assert.ThrowsExactlyAsync<IOException>(async () => await writer.WriteAsync(null!, 25, buffer));

        Assert.AreEqual(12L, disk.ReadOffset);
        Assert.AreEqual(20L, disk.WriteOffset);
        Assert.IsTrue(disk.Flushed);
        var sample = provider.Drain().Single();
        Assert.AreEqual(3L, sample.ReadBytes);
        Assert.AreEqual(5L, sample.WrittenBytes);
    }

    private sealed class TestWriter : IPieceWriter
    {
        public int OpenFiles => 0;
        public int MaximumOpenFiles => 0;
        public long ReadOffset { get; private set; }
        public long WriteOffset { get; private set; }
        public bool Flushed { get; private set; }
        public bool FailWrites { get; set; }

        public ReusableTask<int> ReadAsync(ITorrentManagerFile file, long offset, Memory<byte> buffer)
        {
            ReadOffset = offset;
            return ReusableTask.FromResult(3);
        }

        public ReusableTask WriteAsync(ITorrentManagerFile file, long offset, ReadOnlyMemory<byte> buffer)
        {
            if (FailWrites) throw new IOException("Test write failed.");
            WriteOffset = offset;
            return ReusableTask.CompletedTask;
        }

        public ReusableTask FlushAsync(ITorrentManagerFile file)
        {
            Flushed = true;
            return ReusableTask.CompletedTask;
        }

        public ReusableTask CloseAsync(ITorrentManagerFile file) => throw new NotSupportedException();
        public ReusableTask<bool> CreateAsync(ITorrentManagerFile file, FileCreationOptions options) => throw new NotSupportedException();
        public ReusableTask<bool> ExistsAsync(ITorrentManagerFile file) => throw new NotSupportedException();
        public ReusableTask<long?> GetLengthAsync(ITorrentManagerFile file) => throw new NotSupportedException();
        public ReusableTask MoveAsync(ITorrentManagerFile file, string fullPath, bool overwrite) => throw new NotSupportedException();
        public ReusableTask<bool> SetLengthAsync(ITorrentManagerFile file, long length) => throw new NotSupportedException();
        public ReusableTask SetMaximumOpenFilesAsync(int maximumOpenFiles) => throw new NotSupportedException();
        public void Dispose() { }
    }
}

using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.HttpDownloads.Components;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Modules.Bittorrent.Components;
using Bakabase.Modules.Bittorrent.Models;
using HttpRequest = Bakabase.Modules.HttpDownloads.Models.HttpDownloadRequest;

namespace Bakabase.Tests;

[TestClass]
public sealed class StorageWriteDownloadTests
{
    private string _root = null!;
    [TestInitialize] public void Setup() => _root = Path.Combine(Path.GetTempPath(), "storage-download-" + Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod, Timeout(20000)]
    public async Task HttpReserveFailureDrainsWriterThenResumesSameJobWithoutNewIdentity()
    {
        var state = new State(); var transport = new Http(state);
        var guard = new StorageWriteGuard(state, _ => ("disk", Interlocked.Read(ref state.Available)), TimeSpan.FromMilliseconds(5));
        await using var manager = new HttpDownloadManager(transport, () => Path.Combine(_root, "records"), storageWrites: guard);
        var request = new HttpRequest { Url = "https://example.invalid/payload", OutputDirectory = Path.Combine(_root, "output"),
            FileName = "payload.bin", IdempotencyKey = "same-source", WriteContext = new("workflow", "17") };
        var queued = await manager.EnsureQueuedAsync(request);
        var failure = await Assert.ThrowsExceptionAsync<HttpDownloadJobException>(() => manager.WaitForCompletionAsync(queued.Id));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, failure.Job.Error);
        Assert.IsTrue(state.Drained);
        Assert.AreEqual(1, transport.Calls);
        state.Available = 10000; state.Pause = false;
        var same = await manager.EnsureQueuedAsync(request);
        Assert.AreEqual(queued.Id, same.Id);
        await manager.ResumeAsync(same.Id);
        var done = await manager.WaitForCompletionAsync(same.Id);
        Assert.AreEqual(HttpDownloadStatus.Completed, done.Status);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(done.Files.Single()));
        Assert.AreEqual(1, (await manager.ListAsync()).Count);
    }

    [TestMethod, Timeout(20000)]
    public async Task BtReserveFailureDrainsWriterAndKeepsExistingPartialFiles()
    {
        var state = new State(); var transport = new Torrent(state);
        var guard = new StorageWriteGuard(state, _ => ("disk", Interlocked.Read(ref state.Available)), TimeSpan.FromMilliseconds(5));
        await using var manager = new BtDownloadManager(transport, () => Path.Combine(_root, "records"), storageWrites: guard);
        var request = new BtDownloadRequest { MagnetUri = "magnet:?xt=urn:btih:0123456789012345678901234567890123456789",
            OutputDirectory = Path.Combine(_root, "output"), IdempotencyKey = "same-torrent", WriteContext = new("workflow", "17") };
        var queued = await manager.EnsureQueuedAsync(request);
        var failure = await Assert.ThrowsExceptionAsync<BtDownloadJobException>(() => manager.WaitForCompletionAsync(queued.Id));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, failure.Job.Error);
        Assert.IsTrue(state.Drained);
        Assert.AreEqual("partial", await File.ReadAllTextAsync(Path.Combine(request.OutputDirectory, "payload.bin")));
        state.Available = 10000; state.Pause = false;
        await manager.ResumeAsync(queued.Id);
        var done = await manager.WaitForCompletionAsync(queued.Id);
        Assert.AreEqual(BtDownloadStatus.Completed, done.Status);
        Assert.AreEqual("partial-completed", await File.ReadAllTextAsync(done.Files.Single()));
        Assert.AreEqual(1, (await manager.ListAsync()).Count);
    }

    private sealed class State : IStorageWritePolicyProvider
    {
        public long Available = 10000;
        public bool Pause = true, Drained;
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) => Task.FromResult(context?.OwnerKind == "workflow" ? 100L : 0L);
        public async Task Wait(CancellationToken ct)
        {
            Interlocked.Exchange(ref Available, 1);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { await Task.Delay(10); Drained = true; }
        }
    }

    private sealed class Http(State state) : IHttpDownloader
    {
        public int Calls;
        public async Task<string> DownloadAsync(Bakabase.Modules.Downloader.Models.HttpDownloadRequest request,
            Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Calls++;
            if (state.Pause) await state.Wait(ct);
            Directory.CreateDirectory(request.Directory);
            var path = Path.Combine(request.Directory, request.FileName!);
            await File.WriteAllTextAsync(path, "payload", ct);
            return path;
        }
    }

    private sealed class Torrent(State state) : ITorrentDownloader
    {
        public async Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(workingDirectory);
            var path = Path.Combine(workingDirectory, "payload.bin");
            if (state.Pause) { await File.WriteAllTextAsync(path, "partial", ct); await state.Wait(ct); }
            else { Assert.AreEqual("partial", await File.ReadAllTextAsync(path, ct)); await File.AppendAllTextAsync(path, "-completed", ct); }
            return new(workingDirectory, [path]);
        }
        public Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) => throw new NotSupportedException();
    }
}

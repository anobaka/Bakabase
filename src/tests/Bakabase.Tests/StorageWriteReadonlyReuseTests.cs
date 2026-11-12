using System.Net;
using System.Security.Cryptography;
using System.Text;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.HttpDownloads.Components;
using Bakabase.Modules.HttpDownloads.Models;
using Microsoft.Extensions.Logging.Abstractions;
using TransportRequest = Bakabase.Modules.Downloader.Models.HttpDownloadRequest;

namespace Bakabase.Tests;

[TestClass]
public sealed class StorageWriteReadonlyReuseTests
{
    private string root = null!;
    [TestInitialize] public void Setup() {root = Path.Combine(Path.GetTempPath(), "storage-readonly-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);}
    [TestCleanup] public void Cleanup() {if (Directory.Exists(root)) Directory.Delete(root, true);}

    [TestMethod, Timeout(20000)]
    public async Task HttpPublishedReceiptCanBeRecheckedBelowReserveButChangedFileCannotBeReused()
    {
        var policy = new Policy(); var transport = new ReadyHttp(); long free = 10000;
        var guard = new StorageWriteGuard(policy, _ => ("disk", Volatile.Read(ref free)), TimeSpan.FromMilliseconds(10));
        await using var manager = new HttpDownloadManager(transport, () => Path.Combine(root, "records"), storageWrites: guard);
        var queued = await manager.EnsureQueuedAsync(new()
        {
            Url = "https://example.invalid/file", OutputDirectory = Path.Combine(root, "output"), FileName = "payload.bin",
            IdempotencyKey = "receipt", WriteContext = new("workflow", "17")
        });
        var done = await manager.WaitForCompletionAsync(queued.Id);
        Assert.AreEqual(HttpDownloadStatus.Completed, done.Status); Assert.AreEqual(1, transport.Calls);
        Interlocked.Exchange(ref free, 1);
        await manager.RecheckAsync(done.Id);
        var reused = await manager.WaitForCompletionAsync(done.Id);
        Assert.AreEqual(done.Id, reused.Id); Assert.AreEqual(HttpDownloadStatus.Completed, reused.Status);
        Assert.AreEqual(1, transport.Calls, "Verified committed bytes need no new payload space.");
        await File.WriteAllTextAsync(reused.Files.Single(), "changed");
        await manager.RecheckAsync(done.Id);
        var failed = await Assert.ThrowsExceptionAsync<HttpDownloadJobException>(() => manager.WaitForCompletionAsync(done.Id));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, failed.Job.Error);
        Assert.AreEqual(1, transport.Calls, "A changed receipt must not bypass admission or initiate another download.");
        Assert.AreEqual("changed", await File.ReadAllTextAsync(done.Files.Single()));
    }

    [TestMethod]
    public async Task HttpTransportReusesServerChecksumBelowReserveAndDoesNotWriteOnMismatch()
    {
        var payload = Encoding.UTF8.GetBytes("verified"); var path = Path.Combine(root, "payload.bin");
        await File.WriteAllBytesAsync(path, payload);
        var factory = new ProbeFactory(payload);
        var guard = new StorageWriteGuard(new Policy(), _ => ("disk", 1), TimeSpan.FromMilliseconds(10));
        var transport = new HttpDownloader(() => Path.Combine(root, "cache"), factory, NullLoggerFactory.Instance, guard);
        var request = new TransportRequest("https://example.invalid/file", root)
        {FileName = "payload.bin", WriteContext = new("workflow", "17"), AllowOverwrite = true};
        Assert.AreEqual(path, await transport.DownloadAsync(request, null, default));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "cache")));
        await File.WriteAllTextAsync(path, "modified");
        var failure = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => transport.DownloadAsync(request, null, default));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, failure.Code);
        Assert.AreEqual(0, factory.BodyRequests); Assert.AreEqual("modified", await File.ReadAllTextAsync(path));
    }

    private sealed class Policy : IStorageWritePolicyProvider
    {public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) => Task.FromResult(context == null ? 0L : 100L);}
    private sealed class ReadyHttp : IHttpDownloader
    {
        public int Calls;
        public async Task<string> DownloadAsync(TransportRequest request, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Calls++; Directory.CreateDirectory(request.Directory);
            var path = Path.Combine(request.Directory, request.FileName!);
            await File.WriteAllTextAsync(path, "payload", ct); return path;
        }
    }
    private sealed class ProbeFactory(byte[] payload) : IHttpClientFactory
    {
        public int BodyRequests;
        public HttpClient CreateClient(string name) => new(new Handler(this, payload));
        private sealed class Handler(ProbeFactory owner, byte[] payload) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method != HttpMethod.Head) Interlocked.Increment(ref owner.BodyRequests);
                var response = new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(payload), RequestMessage = request};
                response.Content.Headers.ContentMD5 = MD5.HashData(payload);
                return Task.FromResult(response);
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public sealed class HttpDownloadResilienceTests
{
    private string _root = null!;
    private string Target => Path.Combine(_root, "finished", "resource.bin");
    private string Cache => Path.Combine(_root, "cache");

    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "BakabaseResume_" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // A fresh container and downloader on every call verifies that resumption has no in-memory dependency.
    private async Task<string> Download(Server server, int parallel = 1, int retries = 0,
        CancellationToken ct = default)
    {
        using var services = new ServiceCollection().AddDownloader(_ => Cache).BuildServiceProvider();
        return await services.GetRequiredService<IHttpDownloader>().DownloadAsync(
            new HttpDownloadRequest(server.Url, Path.GetDirectoryName(Target)!)
            {
                FileName = Path.GetFileName(Target), ParallelConnections = parallel,
                MaxRetries = retries, Timeout = TimeSpan.FromSeconds(30)
            }, null, ct);
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task ParallelTransfersRespectTheConfiguredConnectionLimit()
    {
        await using var server = new Server { Slow = true };
        await Download(server, parallel: 2);
        Assert.AreEqual(2, server.MaxActive, "Two payload connections should run concurrently, with no third connection.");
        Assert.IsTrue(server.Requests.Any(request => request.Start > 0));
        await AssertPayload(server);
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task ADisconnectedTransferRetriesFromReceivedBytesAutomatically()
    {
        await using var server = new Server { DisconnectOnce = true };
        await Download(server, retries: 1);
        Assert.IsTrue(server.Requests.Any(request => request.Start > 0), "The library should continue the interrupted chunk.");
        await AssertPayload(server);
    }

    private async Task Interrupt(Server server)
    {
        server.Slow = true;
        using var stop = new CancellationTokenSource();
        var download = Download(server, ct: stop.Token);
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Interlocked.Read(ref server.BytesWritten) < 128 * 1024)
            await Task.Delay(10, guard.Token);
        stop.Cancel();
        try { await download; Assert.Fail("Cancellation must stop the transfer."); }
        catch (OperationCanceledException) { }
        Assert.IsFalse(File.Exists(Target), "An incomplete file must not be published as the final resource.");
        await server.DrainPayloads();
        server.Requests.Clear();
        server.Slow = false;
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task ANewDownloaderResumesACanceledTransferWithTheSameETag()
    {
        await using var server = new Server();
        await Interrupt(server);
        Assert.IsTrue(Directory.GetFiles(Cache, "checkpoint.json", SearchOption.AllDirectories).Length > 0);
        await Download(server);
        Assert.IsTrue(server.Requests.Any(request => request.Start > 0), "Resume must request the missing bytes.");
        Assert.IsFalse(server.Requests.Any(request => request.Start == 0), "Already downloaded bytes should be reused.");
        await AssertPayload(server);
    }

    [TestMethod]
    [Timeout(30000)]
    public async Task AResumedTransferUsesTheNewRetrySetting()
    {
        await using var server = new Server();
        await Interrupt(server); // The saved chunks were created with zero retries.
        server.DisconnectOnce = true;
        await Download(server, retries: 1);
        Assert.IsTrue(server.Requests.Count(request => request.Start > 0) >= 2,
            "Both checkpoint restoration and the newly enabled retry should request missing bytes.");
        await AssertPayload(server);
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task LastModifiedCanIdentifyAResumableTransferWithoutAnETag()
    {
        await using var server = new Server { ETag = null, Modified = DateTimeOffset.Parse("2026-09-01T00:00:00Z") };
        await Interrupt(server);
        await Download(server);
        Assert.IsTrue(server.Requests.Any(request => request.Start > 0));
        await AssertPayload(server);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(20000)]
    public async Task ChangedRemoteContentOrCorruptLocalChunksRestartSafely(bool corruptLocal)
    {
        await using var server = new Server();
        await Interrupt(server);
        if (corruptLocal)
        {
            var partial = Directory.GetFiles(Cache, "*.download", SearchOption.AllDirectories).Single();
            await using var file = new FileStream(partial, FileMode.Open, FileAccess.Write);
            await file.WriteAsync(Enumerable.Repeat((byte)0xA5, 64).ToArray());
        }
        else
        {
            server.Body = RandomNumberGenerator.GetBytes(server.Body.Length);
            server.ETag = "\"another-version-with-the-same-length\"";
        }
        await Download(server);
        Assert.IsTrue(server.Requests.Any(request => request.Start == 0), "Invalid old pieces must be replaced.");
        await AssertPayload(server);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("W/\"weak-version\"")]
    [Timeout(20000)]
    public async Task AServerWithoutAReliableValidatorRestartsWithoutByteRanges(string? etag)
    {
        await using var server = new Server { ETag = etag };
        await Interrupt(server);
        await Download(server, parallel: 4);
        Assert.IsFalse(server.Requests.Any(request => request.Start > 0), "Unidentified old bytes cannot be resumed or combined.");
        // Header-only probes can overlap on the test server briefly after HttpClient has disposed
        // them. Assert the actual transfer offsets rather than counting those discarded probes.
        await AssertPayload(server);
    }

    private async Task AssertPayload(Server server)
    {
        CollectionAssert.AreEqual(SHA256.HashData(server.Body), SHA256.HashData(await File.ReadAllBytesAsync(Target)));
        Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(Target)!, "*", SearchOption.AllDirectories).Length);
    }

    private sealed record PayloadRequest(long Start, long End);

    private sealed class Server : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _handlers = [];
        private readonly Task _loop;
        private int _active;
        private int _disconnected;
        public byte[] Body = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        public string? ETag = "\"one-stable-version\"";
        public DateTimeOffset? Modified;
        public bool Slow;
        public bool DisconnectOnce;
        public int MaxActive;
        public long BytesWritten;
        public ConcurrentQueue<PayloadRequest> Requests { get; } = new();
        public string Url { get; }

        public Server()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/file.bin";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _loop = Serve();
        }

        private async Task Serve()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().WaitAsync(_stop.Token); }
                catch (Exception) when (_stop.IsCancellationRequested) { break; }
                _handlers.Add(Reply(context));
            }
        }

        private async Task Reply(HttpListenerContext context)
        {
            var active = false;
            try
            {
                var body = Body;
                var response = context.Response;
                response.Headers["Accept-Ranges"] = "bytes";
                if (ETag != null) response.Headers["ETag"] = ETag;
                if (Modified.HasValue) response.Headers["Last-Modified"] = Modified.Value.ToString("R");
                response.ContentLength64 = body.Length;
                if (context.Request.HttpMethod == "HEAD") { response.Close(); return; }
                var from = 0;
                var to = body.Length - 1;
                if (context.Request.Headers["Range"] is { } range)
                {
                    var offsets = range[6..].Split('-');
                    from = int.Parse(offsets[0]);
                    if (!string.IsNullOrEmpty(offsets[1])) to = int.Parse(offsets[1]);
                    response.StatusCode = 206;
                    response.Headers["Content-Range"] = $"bytes {from}-{to}/{body.Length}";
                }
                response.ContentLength64 = to - from + 1;
                if (to - from > 1)
                {
                    active = true;
                    Requests.Enqueue(new PayloadRequest(from, to));
                    var count = Interlocked.Increment(ref _active);
                    int previous;
                    do { previous = MaxActive; if (previous >= count) break; }
                    while (Interlocked.CompareExchange(ref MaxActive, count, previous) != previous);
                }
                var disconnect = active && DisconnectOnce && Interlocked.CompareExchange(ref _disconnected, 1, 0) == 0;
                for (var position = from; position <= to; position += 16 * 1024)
                {
                    var length = Math.Min(16 * 1024, to - position + 1);
                    await response.OutputStream.WriteAsync(body.AsMemory(position, length), _stop.Token);
                    await response.OutputStream.FlushAsync(_stop.Token);
                    if (active) Interlocked.Add(ref BytesWritten, length);
                    if (disconnect && position - from >= 64 * 1024) { response.Abort(); return; }
                    if (Slow && active) await Task.Delay(20, _stop.Token);
                }
                response.Close();
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException)
            {
                context.Response.Abort();
            }
            finally { if (active) Interlocked.Decrement(ref _active); }
        }

        public async Task DrainPayloads()
        {
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref _active) > 0) await Task.Delay(10, guard.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Close();
            await _loop;
            await Task.WhenAll(_handlers);
            _stop.Dispose();
        }
    }
}

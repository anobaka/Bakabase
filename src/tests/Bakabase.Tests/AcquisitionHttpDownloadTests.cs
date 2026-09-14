using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionHttpDownloadTests
{
    private string _directory = null!;
    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "BakabaseHttp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }
    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    private static SingleFileHttpDownloader Downloader(HttpClient client) =>
        new(client, NullLogger<SingleFileHttpDownloader>.Instance);

    [TestMethod]
    public async Task AnIgnoredRangeRestartsWithoutAppendingTheWholeFileToThePartialFile()
    {
        await using var server = new Server {IgnoreRange = true};
        var path = Path.Combine(_directory, "payload.bin");
        await File.WriteAllBytesAsync(path, server.Body[..1234]);
        using var client = new HttpClient();
        await Downloader(client).Download(server.Url, path, CancellationToken.None);
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    public async Task AnIncorrectContentRangeIsRejectedBeforeWritingAnyResponseBody()
    {
        await using var server = new Server {WrongRange = true};
        var path = Path.Combine(_directory, "payload.bin");
        await File.WriteAllBytesAsync(path, server.Body[..1234]);
        using var client = new HttpClient();
        await Assert.ThrowsExactlyAsync<IOException>(() => Downloader(client).Download(server.Url, path, CancellationToken.None));
        Assert.AreEqual(0, new FileInfo(path).Length);
    }

    [TestMethod]
    public async Task AnOversizedOldFileCanBeReplacedWithoutAccessingADisposedStream()
    {
        await using var server = new Server();
        var path = Path.Combine(_directory, "payload.bin");
        await File.WriteAllBytesAsync(path, new byte[server.Body.Length + 1]);
        using var client = new HttpClient();
        await Downloader(client).Download(server.Url, path, CancellationToken.None);
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task CancellationStopsTheBodyAndRetryRestartsWithoutAStoredVersionValidator()
    {
        await using var server = new Server {Slow = true};
        var path = Path.Combine(_directory, "payload.bin");
        using var client = new HttpClient();
        using var cancellation = new CancellationTokenSource();
        var download = Downloader(client).Download(server.Url, path, cancellation.Token);
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(path) || new FileInfo(path).Length == 0) await Task.Delay(10, guard.Token);
        cancellation.Cancel();
        try { await download; Assert.Fail("Cancellation must interrupt the streaming body."); }
        catch (OperationCanceledException) { }
        var prefixLength = new FileInfo(path).Length;
        Assert.IsTrue(prefixLength > 0 && prefixLength < server.Body.Length);
        server.Slow = false;
        await Downloader(client).Download(server.Url, path, CancellationToken.None);
        Assert.IsFalse(server.RangeStarts.Contains(prefixLength), "old bytes have no trusted version validator and must not be appended to");
        await AssertContent(server.Body, path);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedContentAtTheSameUrlCannotReuseAnOldPrefixOrSameSizeFile(bool partial)
    {
        await using var server = new Server {OmitChecksum = true};
        var path = Path.Combine(_directory, "payload.bin");
        using var client = new HttpClient();
        await Downloader(client).Download(server.Url, path, CancellationToken.None);
        if (partial)
        {
            await using var file = File.OpenWrite(path);
            file.SetLength(server.Body.Length / 2);
        }
        server.Body = RandomNumberGenerator.GetBytes(server.Body.Length);
        server.ETag = "\"updated-test-payload\"";
        var previousRequestCount = server.RangeStarts.Count;

        await Downloader(client).Download(server.Url, path, CancellationToken.None);

        Assert.IsTrue(server.RangeStarts.Count > previousRequestCount, "a new HEAD validator alone cannot validate old bytes");
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    public async Task ACompleteFileMatchingTheCurrentChecksumCanBeReused()
    {
        await using var server = new Server();
        var path = Path.Combine(_directory, "payload.bin");
        using var client = new HttpClient();
        await Downloader(client).Download(server.Url, path, CancellationToken.None);
        var previousRequestCount = server.RangeStarts.Count;

        await Downloader(client).Download(server.Url, path, CancellationToken.None);

        Assert.AreEqual(previousRequestCount, server.RangeStarts.Count);
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    public async Task APartialProbeChecksumIsNotMistakenForTheWholeFileChecksum()
    {
        await using var server = new Server {RefuseHead = true};
        using var client = new HttpClient();
        var path = await Downloader(client).DownloadToDirectory(server.Url, _directory, CancellationToken.None);
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    public async Task AServerChecksumMismatchFailsAndRemovesTheCorruptFile()
    {
        await using var server = new Server {WrongChecksum = true};
        var path = Path.Combine(_directory, "payload.bin");
        using var client = new HttpClient();
        try { await Downloader(client).Download(server.Url, path, CancellationToken.None); Assert.Fail("The checksum must be checked."); }
        catch (Exception ex) when (ex is not Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException)
        {
            StringAssert.Contains(ex.Message, "MD5");
        }
        Assert.IsFalse(File.Exists(path));
    }

    private static async Task AssertContent(byte[] expected, string path) =>
        CollectionAssert.AreEqual(SHA256.HashData(expected), SHA256.HashData(await File.ReadAllBytesAsync(path)));

    private sealed class Server : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _requests;
        public byte[] Body = RandomNumberGenerator.GetBytes(1024 * 1024);
        public readonly ConcurrentBag<long> RangeStarts = [];
        public string Url { get; }
        public bool IgnoreRange, WrongRange, WrongChecksum, Slow, RefuseHead, OmitChecksum;
        public string ETag = "\"fixed-test-payload\"";

        public Server()
        {
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            var number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            Url = $"http://127.0.0.1:{number}/payload.bin";
            _listener.Prefixes.Add($"http://127.0.0.1:{number}/");
            _listener.Start();
            _requests = ServeAsync();
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                try
                {
                    var response = context.Response;
                    response.Headers.Add("Accept-Ranges", "bytes");
                    response.Headers.Add("ETag", ETag);
                    if (!OmitChecksum)
                        response.Headers.Add("Content-MD5", Convert.ToBase64String(WrongChecksum ? new byte[16] : MD5.HashData(Body)));
                    if (context.Request.HttpMethod == "HEAD")
                    {
                        if (RefuseHead) response.StatusCode = 405;
                        response.ContentLength64 = RefuseHead ? 0 : Body.Length;
                        response.Close();
                        continue;
                    }
                    var start = 0;
                    var end = Body.Length - 1;
                    if (context.Request.Headers["Range"] is { } range)
                    {
                        var parts = range["bytes=".Length..].Split('-');
                        var requestedStart = int.Parse(parts[0]);
                        RangeStarts.Add(requestedStart);
                        if (!IgnoreRange)
                        {
                            start = requestedStart;
                            end = int.Parse(parts[1]);
                            response.StatusCode = 206;
                            response.Headers.Add("Content-Range", $"bytes {(WrongRange ? start + 1 : start)}-{end}/{Body.Length}");
                        }
                    }
                    if (RefuseHead)
                        response.Headers["Content-MD5"] = Convert.ToBase64String(MD5.HashData(Body.AsSpan(start, end - start + 1)));
                    response.ContentLength64 = end - start + 1;
                    for (var offset = start; offset <= end; offset += 16384)
                    {
                        await response.OutputStream.WriteAsync(Body.AsMemory(offset, Math.Min(16384, end - offset + 1)));
                        if (Slow) await Task.Delay(20);
                    }
                    response.Close();
                }
                catch (IOException) { context.Response.Abort(); }
                catch (HttpListenerException) { context.Response.Abort(); }
                catch (ObjectDisposedException) { context.Response.Abort(); }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();
            await _requests;
        }
    }
}

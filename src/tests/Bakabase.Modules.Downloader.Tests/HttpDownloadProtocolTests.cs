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
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public sealed class HttpDownloadProtocolTests
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

    private DownloadClient Downloader() => new(Path.Combine(_directory, "cache"));

    private sealed class DownloadClient(string cache)
    {
        public async Task Download(string url, string path, CancellationToken ct)
        {
            using var provider = new ServiceCollection().AddDownloader(_ => cache).BuildServiceProvider();
            await provider.GetRequiredService<IHttpDownloader>().DownloadAsync(
                new HttpDownloadRequest(url, Path.GetDirectoryName(path)!)
                { FileName = Path.GetFileName(path), MaxRetries = 0 }, null, ct);
        }
        public async Task<string> DownloadToDirectory(string url, string directory, CancellationToken ct)
        {
            using var provider = new ServiceCollection().AddDownloader(_ => cache).BuildServiceProvider();
            return await provider.GetRequiredService<IHttpDownloader>().DownloadAsync(
                new HttpDownloadRequest(url, directory) { MaxRetries = 0 }, null, ct);
        }
    }

    [TestMethod]
    public async Task AnIgnoredRangeRestartsWithoutAppendingTheWholeFileToThePartialFile()
    {
        await using var server = new Server {IgnoreRange = true};
        var path = Path.Combine(_directory, "payload.bin");
        await File.WriteAllBytesAsync(path, server.Body[..1234]);
        await Downloader().Download(server.Url, path, CancellationToken.None);
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    public async Task AnIncorrectContentRangeIsRejectedBeforeWritingAnyResponseBody()
    {
        await using var server = new Server {WrongRange = true};
        var path = Path.Combine(_directory, "payload.bin");
        await File.WriteAllBytesAsync(path, server.Body[..1234]);
        await Assert.ThrowsExactlyAsync<IOException>(() => Downloader().Download(server.Url, path, CancellationToken.None));
        Assert.AreEqual(1234, new FileInfo(path).Length, "An invalid response must not replace the previous file.");
    }

    [TestMethod]
    public async Task AnOversizedOldFileCanBeReplacedWithoutAccessingADisposedStream()
    {
        await using var server = new Server();
        var path = Path.Combine(_directory, "payload.bin");
        await File.WriteAllBytesAsync(path, new byte[server.Body.Length + 1]);
        await Downloader().Download(server.Url, path, CancellationToken.None);
        await AssertContent(server.Body, path);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedContentAtTheSameUrlCannotReuseAnOldPrefixOrSameSizeFile(bool partial)
    {
        await using var server = new Server {OmitChecksum = true};
        var path = Path.Combine(_directory, "payload.bin");
        await Downloader().Download(server.Url, path, CancellationToken.None);
        if (partial)
        {
            await using var file = File.OpenWrite(path);
            file.SetLength(server.Body.Length / 2);
        }
        server.Body = RandomNumberGenerator.GetBytes(server.Body.Length);
        server.ETag = "\"updated-test-payload\"";
        var previousRequestCount = server.RangeStarts.Count;

        await Downloader().Download(server.Url, path, CancellationToken.None);

        Assert.IsTrue(server.RangeStarts.Count > previousRequestCount, "a new HEAD validator alone cannot validate old bytes");
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    public async Task ACompleteFileMatchingTheCurrentChecksumCanBeReused()
    {
        await using var server = new Server();
        var path = Path.Combine(_directory, "payload.bin");
        await Downloader().Download(server.Url, path, CancellationToken.None);
        var previousRequestCount = server.RangeStarts.Count;

        await Downloader().Download(server.Url, path, CancellationToken.None);

        Assert.AreEqual(previousRequestCount, server.RangeStarts.Count);
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    public async Task APartialProbeChecksumIsNotMistakenForTheWholeFileChecksum()
    {
        await using var server = new Server {RefuseHead = true};
        var path = await Downloader().DownloadToDirectory(server.Url, _directory, CancellationToken.None);
        await AssertContent(server.Body, path);
    }

    [TestMethod]
    public async Task AServerChecksumMismatchFailsAndRemovesTheCorruptFile()
    {
        await using var server = new Server {WrongChecksum = true};
        var path = Path.Combine(_directory, "payload.bin");
        try { await Downloader().Download(server.Url, path, CancellationToken.None); Assert.Fail("The checksum must be checked."); }
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
                            end = string.IsNullOrEmpty(parts[1]) ? Body.Length - 1 : int.Parse(parts[1]);
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

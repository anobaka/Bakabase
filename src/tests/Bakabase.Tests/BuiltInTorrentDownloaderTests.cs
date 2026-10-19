using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.Logging.Abstractions;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BuiltInTorrentDownloaderTests
{
    private string _root = null!;
    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "BakabaseTorrent_" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private BuiltInTorrentDownloader Downloader() =>
        new(Path.Combine(_root, "engine-cache"), NullLogger<BuiltInTorrentDownloader>.Instance, false, new HttpFactory());

    private sealed class HttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() {Timeout = Timeout.InfiniteTimeSpan};
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(90000)]
    public async Task ManagedTransfersUseTheExactDirectoryAndRouteMetadataTrackersAndPeersTogether(bool useProxy)
    {
        await using var seed = await LocalSeed.CreateAsync(Path.Combine(_root, "seed"));
        await using var proxy = new ForwardingSocksProxy();
        var options = new FixedNetworkOptions(useProxy ? new TorrentNetworkOptions {ProxyUri = proxy.Address} : new TorrentNetworkOptions());
        var downloader = new BuiltInTorrentDownloader(Path.Combine(_root, "cache"),
            NullLogger<BuiltInTorrentDownloader>.Instance, false, networkOptions: options);
        var working = Path.Combine(_root, "content");
        var progress = new List<TorrentTransferProgress>();
        var previousSystemProxy = HttpClient.DefaultProxy;
        var forbiddenSystemProxy = new RejectSystemProxy();
        HttpClient.DefaultProxy = forbiddenSystemProxy;
        try
        {
            var result = await downloader.DownloadAsync(new TorrentDownloadRequest
            {
                TorrentUrl = seed.MetadataUrl, WorkingDirectory = working,
                UseWorkingDirectoryAsDestination = true, Timeout = TimeSpan.FromSeconds(60)
            }, update => { progress.Add(update); return Task.CompletedTask; }, CancellationToken.None);
            Assert.AreEqual(working, result.Directory);
            Assert.IsFalse(Directory.Exists(Path.Combine(working, "torrent-data")));
            await AssertFiles(seed, result.Directory, result.Files);
            Assert.AreEqual(0, forbiddenSystemProxy.Calls,
                "Metadata, HTTP trackers and peers must never consult the system proxy.");
            Assert.AreEqual("ReadingMetadata", progress[0].Stage);
            Assert.AreEqual("Completed", progress.Last().Stage);
            Assert.AreEqual(100, progress.Last().Percentage);
            Assert.AreEqual(seed.Files.Values.Sum(f => (long)f.Length), progress.Last().CompletedBytes);
            Assert.AreEqual(progress.Last().TotalBytes, progress.Last().CompletedBytes);
            Assert.AreEqual(1, options.ReadCount, "Each transfer snapshots the routing policy once.");
            if (useProxy)
            {
                CollectionAssert.AreEquivalent(new[] {new Uri(seed.MetadataUrl).Port, seed.PeerPort}, proxy.DestinationPorts.Distinct().ToArray());
                Assert.IsTrue(proxy.DestinationPorts.Count(p => p == new Uri(seed.MetadataUrl).Port) > 1,
                    "Both torrent metadata and tracker requests must pass through SOCKS.");
            }
            else Assert.AreEqual(0, proxy.DestinationPorts.Count);
        }
        finally { HttpClient.DefaultProxy = previousSystemProxy; }
    }

    private sealed class FixedNetworkOptions(TorrentNetworkOptions options) : ITorrentNetworkOptionsProvider
    {
        public int ReadCount;
        public TorrentNetworkOptions GetOptions() { ReadCount++; return options; }
    }

    private sealed class RejectSystemProxy : IWebProxy
    {
        public int Calls;
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) { Interlocked.Increment(ref Calls); throw new InvalidOperationException("System proxy unexpectedly consulted."); }
        public bool IsBypassed(Uri host) { Interlocked.Increment(ref Calls); return false; }
    }

    [DataTestMethod]
    [DataRow("magnet")]
    [DataRow("metadata")]
    [DataRow("url")]
    [Timeout(90000)]
    public async Task TheStandaloneDownloaderVerifiesANestedFileSetFromALoopbackSeed(string inputKind)
    {
        await using var seed = await LocalSeed.CreateAsync(Path.Combine(_root, "seed"));
        var working = Path.Combine(_root, "task");
        ITorrentDownloader downloader = Downloader();
        var progress = new List<int>();
        Task Report(int percent, string? message)
        {
            progress.Add(percent);
            return Task.CompletedTask;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var downloaded = inputKind switch
        {
            "magnet" => await downloader.DownloadMagnetAsync(seed.Magnet, working, TimeSpan.FromSeconds(60), Report, timeout.Token),
            "url" => await downloader.DownloadTorrentUrlAsync(seed.MetadataUrl, working, TimeSpan.FromSeconds(60), Report, timeout.Token),
            _ => await downloader.DownloadTorrentAsync(seed.Metadata, working, TimeSpan.FromSeconds(60), Report, timeout.Token)
        };
        await AssertFiles(seed, downloaded.Directory, downloaded.Files);
        Assert.AreEqual(100, progress.Last());
        Assert.IsTrue(seed.StoppedAnnounces > 0, "the downloader announces stop instead of continuing to seed");
        Assert.IsTrue(Directory.GetFiles(working, "*.torrent", SearchOption.AllDirectories).Length == 0,
            "engine metadata must not enter the resource's content directory");
        foreach (var file in downloaded.Files)
        {
            using var exclusive = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        Directory.Move(downloaded.Directory, Path.Combine(_root, "placed"));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(90000)]
    public async Task HybridTorrent_WithOrWithoutFinalPaddingDownloadsVerifiedFilesFromLoopback(bool finalPadding)
    {
        await using var seed = await LocalSeed.CreateAsync(Path.Combine(_root, "hybrid-seed"), hybridFinalPadding: finalPadding);
        var originalMetadata = seed.Metadata.ToArray();
        var sourceTorrent = Torrent.Load(originalMetadata);
        Assert.IsNotNull(sourceTorrent.InfoHashes.V1);
        Assert.IsNotNull(sourceTorrent.InfoHashes.V2);
        Assert.AreEqual(finalPadding, sourceTorrent.Files.Last().Padding > 0);
        var progress = new List<TorrentTransferProgress>();
        var result = await Downloader().DownloadAsync(new TorrentDownloadRequest
        {
            Metadata = seed.Metadata, WorkingDirectory = Path.Combine(_root, "hybrid-result"),
            UseWorkingDirectoryAsDestination = true, Timeout = TimeSpan.FromSeconds(60)
        }, update => { progress.Add(update); return Task.CompletedTask; }, CancellationToken.None);

        await AssertFiles(seed, result.Directory, result.Files);
        Assert.AreEqual(seed.Files.Count, Directory.GetFiles(result.Directory, "*", SearchOption.AllDirectories).Length,
            "The transferred resource must contain actual files, without synthetic padding files.");
        CollectionAssert.AreEqual(originalMetadata, seed.Metadata);
        Assert.AreEqual(sourceTorrent.InfoHashes, Torrent.Load(seed.Metadata).InfoHashes);
        Assert.AreEqual(100, progress.Last().Percentage);
        Assert.AreEqual("Completed", progress.Last().Stage);
        Assert.IsTrue(seed.StoppedAnnounces > 0);
    }

    [TestMethod]
    [Timeout(90000)]
    public async Task CancellationLeavesVerifiedPiecesForAHashCheckedRetry()
    {
        await using var seed = await LocalSeed.CreateAsync(Path.Combine(_root, "seed"), 2 * 1024 * 1024, 256 * 1024);
        var working = Path.Combine(_root, "task");
        var downloader = Downloader();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await downloader.DownloadTorrentAsync(seed.Metadata, working, TimeSpan.FromSeconds(60), (percent, _) =>
            {
                if (percent >= 15) cancellation.Cancel();
                return Task.CompletedTask;
            }, cancellation.Token));
        Assert.IsTrue(Directory.GetFiles(working, "*", SearchOption.AllDirectories).Any(f => new FileInfo(f).Length > 0));
        var bytesBeforeRetry = seed.Manager.Monitor.DataBytesUploaded;
        await seed.Manager.UpdateSettingsAsync(new TorrentSettingsBuilder(seed.Manager.Settings) {MaximumUploadRate = 0}.ToSettings());
        var downloaded = await downloader.DownloadTorrentAsync(seed.Metadata, working, TimeSpan.FromSeconds(45), null, CancellationToken.None);

        await AssertFiles(seed, downloaded.Directory, downloaded.Files);
        var bytesForRetry = seed.Manager.Monitor.DataBytesUploaded - bytesBeforeRetry;
        Assert.IsTrue(bytesForRetry < seed.Files.Values.Sum(f => (long)f.Length),
            "the retry must reuse verified pieces rather than downloading the complete payload again");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(15000)]
    public async Task ATorrentWithoutPeersTimesOutAndReleasesItsFiles(bool useMagnet)
    {
        await using var seed = await LocalSeed.CreateAsync(Path.Combine(_root, "seed"));
        seed.AdvertisePeer = false;
        var working = Path.Combine(_root, "task");
        await Assert.ThrowsExactlyAsync<TimeoutException>(async () =>
        {
            if (useMagnet)
                await Downloader().DownloadMagnetAsync(seed.Magnet, working, TimeSpan.FromSeconds(1), null, CancellationToken.None);
            else
                await Downloader().DownloadTorrentAsync(seed.Metadata, working, TimeSpan.FromSeconds(1), null, CancellationToken.None);
        });
        Directory.Delete(working, true);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(15000)]
    public async Task TorrentUrlDeadlineAndCancellationIncludeReadingTheMetadata(bool callerCancels)
    {
        await using var seed = await LocalSeed.CreateAsync(Path.Combine(_root, "seed"));
        seed.DelayMetadataBody = true;
        using var cancellation = new CancellationTokenSource();
        var transfer = Downloader().DownloadTorrentUrlAsync(seed.MetadataUrl, Path.Combine(_root, "task"),
            callerCancels ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(150), null, cancellation.Token);
        if (callerCancels)
        {
            await seed.MetadataResponseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try
            {
                await transfer;
                Assert.Fail("Caller cancellation must interrupt the metadata response body.");
            }
            catch (OperationCanceledException) { }
        }
        else
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => transfer);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "task", "torrent-data")), "metadata must be complete before content downloads begin");
    }

    [TestMethod]
    public void NetworkSettingsDoNotExposeAnIncomingPeerPortOrEnablePortMapping()
    {
        var settings = new BuiltInTorrentDownloader(Path.Combine(_root, "cache"),
            NullLogger<BuiltInTorrentDownloader>.Instance, true).SettingsFor(Path.Combine(_root, "task"));
        Assert.IsFalse(settings.AllowPortForwarding);
        Assert.IsFalse(settings.AllowLocalPeerDiscovery);
        Assert.AreEqual(0, settings.ListenEndPoints.Count);
        Assert.AreEqual(0, settings.DhtEndPoint!.Port, "DHT uses an ephemeral UDP endpoint, not a mapped port");
        Assert.IsFalse(settings.AutoSaveLoadFastResume, "retries hash existing pieces instead of trusting a stale snapshot");
        Assert.IsFalse(settings.CacheDirectory.StartsWith(Path.Combine(_root, "task") + Path.DirectorySeparatorChar));
    }

    private static async Task AssertFiles(LocalSeed seed, string directory, IReadOnlyList<string> downloaded)
    {
        CollectionAssert.AreEquivalent(seed.Files.Keys.ToArray(), downloaded.Select(f => Path.GetRelativePath(directory, f)).ToArray());
        foreach (var (relative, expected) in seed.Files)
            CollectionAssert.AreEqual(SHA256.HashData(expected), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, relative))));
    }

    /// <summary>A real MonoTorrent seeder plus a loopback-only HTTP tracker and torrent endpoint.</summary>
    private sealed class LocalSeed : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly ClientEngine _engine;
        private readonly Task _requests;
        private readonly int _peerPort;
        public int PeerPort => _peerPort;
        public readonly Dictionary<string, byte[]> Files;
        public readonly byte[] Metadata;
        public readonly string MetadataUrl;
        public readonly string Magnet;
        public TorrentManager Manager { get; private set; } = null!;
        public bool AdvertisePeer = true;
        public int StoppedAnnounces;
        public bool DelayMetadataBody;
        public readonly TaskCompletionSource MetadataResponseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private LocalSeed(HttpListener listener, ClientEngine engine, int peerPort, Dictionary<string, byte[]> files,
            byte[] metadata, string rootUrl)
        {
            _listener = listener;
            _engine = engine;
            _peerPort = peerPort;
            Files = files;
            Metadata = metadata;
            MetadataUrl = rootUrl + "test.torrent";
            var torrent = Torrent.Load(metadata);
            Magnet = new MagnetLink(torrent.InfoHashes, torrent.Name, [rootUrl + "announce"]).ToV1String();
            _requests = ServeAsync();
        }

        public static async Task<LocalSeed> CreateAsync(string root, int fileSize = 192 * 1024, int uploadRate = 0, bool? hybridFinalPadding = null)
        {
            var files = new Dictionary<string, byte[]>
            {
                [Path.Combine("disc1", "same.bin")] = RandomNumberGenerator.GetBytes(fileSize + (hybridFinalPadding.HasValue ? 7 : 0)),
                [Path.Combine("disc2", "nested", "same.bin")] = RandomNumberGenerator.GetBytes(48 * 1024),
                ["readme.txt"] = "A local test torrent, never a public download."u8.ToArray()
            };
            foreach (var (relative, content) in files)
            {
                var target = Path.Combine(root, "files", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, content);
            }
            var trackerPort = FreePort();
            var peerPort = FreePort();
            var rootUrl = $"http://127.0.0.1:{trackerPort}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(rootUrl);
            listener.Start();
            var metadata = TorrentFixture.Create("files", files, rootUrl + "announce", hybridFinalPadding);
            var engine = new ClientEngine(new EngineSettingsBuilder
            {
                CacheDirectory = Path.Combine(root, "cache"),
                AllowPortForwarding = false,
                AllowLocalPeerDiscovery = false,
                AutoSaveLoadDhtCache = false,
                DhtEndPoint = null,
                ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Loopback, peerPort) }
            }.ToSettings());
            var seed = new LocalSeed(listener, engine, peerPort, files, metadata, rootUrl);
            try
            {
                seed.Manager = await engine.AddAsync(Torrent.Load(metadata), Path.Combine(root, "files"),
                    new TorrentSettingsBuilder {CreateContainingDirectory = false, AllowDht = false,
                        AllowPeerExchange = false, MaximumUploadRate = uploadRate}.ToSettings());
                await seed.Manager.StartAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (seed.Manager.State != TorrentState.Seeding) await Task.Delay(20, timeout.Token);
                return seed;
            }
            catch { await seed.DisposeAsync(); throw; }
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
                    byte[] body;
                    if (context.Request.Url!.AbsolutePath == "/test.torrent") body = Metadata;
                    else
                    {
                        if (context.Request.QueryString["event"] == "stopped") Interlocked.Increment(ref StoppedAnnounces);
                        byte[] peers = AdvertisePeer ? [127, 0, 0, 1, (byte)(_peerPort >> 8), (byte)_peerPort] : [];
                        body = new BEncodedDictionary
                        {
                            ["interval"] = new BEncodedNumber(1),
                            ["peers"] = new BEncodedString(peers)
                        }.Encode();
                    }
                    context.Response.ContentLength64 = body.Length;
                    if (DelayMetadataBody && context.Request.Url!.AbsolutePath == "/test.torrent")
                    {
                        await context.Response.OutputStream.WriteAsync(body.AsMemory(0, 1));
                        MetadataResponseStarted.TrySetResult();
                        await Task.Delay(500);
                        await context.Response.OutputStream.WriteAsync(body.AsMemory(1));
                    }
                    else
                        await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                }
                catch (IOException) { context.Response.Abort(); }
                catch (HttpListenerException) { context.Response.Abort(); }
            }
        }

        private static int FreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }

        public async ValueTask DisposeAsync()
        {
            try { if (Manager != null) await Manager.StopAsync(TimeSpan.FromSeconds(2)); }
            finally
            {
                _engine.Dispose();
                _listener.Stop();
                _listener.Close();
                await _requests;
            }
        }
    }

    /// <summary>A real loopback SOCKS5 TCP tunnel, recording metadata, tracker and peer destinations.</summary>
    private sealed class ForwardingSocksProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accept;
        private readonly List<Task> _connections = [];
        public readonly System.Collections.Concurrent.ConcurrentBag<int> DestinationPorts = [];
        public Uri Address { get; }

        public ForwardingSocksProxy()
        {
            _listener.Start();
            Address = new Uri($"socks5://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _accept = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _connections.Add(ForwardAsync(client));
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ForwardAsync(TcpClient client)
        {
            using (client)
            using (var remote = new TcpClient())
            {
                try
                {
                    var input = client.GetStream();
                    var greeting = new byte[3];
                    await input.ReadExactlyAsync(greeting, _stop.Token);
                    CollectionAssert.AreEqual(new byte[] {5, 1, 0}, greeting);
                    await input.WriteAsync(new byte[] {5, 0}, _stop.Token);
                    var header = new byte[4];
                    await input.ReadExactlyAsync(header, _stop.Token);
                    Assert.AreEqual(5, header[0]);
                    Assert.AreEqual(1, header[1]);
                    var length = header[3] switch {1 => 4, 4 => 16, _ => 0};
                    if (header[3] == 3)
                    {
                        var count = new byte[1];
                        await input.ReadExactlyAsync(count, _stop.Token);
                        length = count[0];
                    }
                    var address = new byte[length];
                    await input.ReadExactlyAsync(address, _stop.Token);
                    var portBytes = new byte[2];
                    await input.ReadExactlyAsync(portBytes, _stop.Token);
                    var host = header[3] == 3 ? System.Text.Encoding.ASCII.GetString(address) : new IPAddress(address).ToString();
                    var port = (portBytes[0] << 8) | portBytes[1];
                    DestinationPorts.Add(port);
                    await remote.ConnectAsync(host, port, _stop.Token);
                    await input.WriteAsync(new byte[] {5, 0, 0, 1, 127, 0, 0, 1, 0, 0}, _stop.Token);
                    var outbound = input.CopyToAsync(remote.GetStream(), _stop.Token);
                    var inbound = remote.GetStream().CopyToAsync(input, _stop.Token);
                    await Task.WhenAny(outbound, inbound);
                    remote.Dispose();
                    client.Dispose();
                    await Task.WhenAll(outbound, inbound);
                }
                catch (IOException) { }
                catch (SocketException) { }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _accept;
            _listener.Stop();
            await Task.WhenAll(_connections);
            _stop.Dispose();
        }
    }
}

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
using Microsoft.Extensions.Logging.Abstractions;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;

namespace Bakabase.Tests;

[TestClass]
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

        public static async Task<LocalSeed> CreateAsync(string root, int fileSize = 192 * 1024, int uploadRate = 0)
        {
            var files = new Dictionary<string, byte[]>
            {
                [Path.Combine("disc1", "same.bin")] = RandomNumberGenerator.GetBytes(fileSize),
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
            var creator = new TorrentCreator(TorrentType.V1Only) {PieceLength = 16 * 1024};
            creator.Announces.Add([rootUrl + "announce"]);
            var metadata = (await creator.CreateAsync(new TorrentFileSource(Path.Combine(root, "files")))).Encode();
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
}

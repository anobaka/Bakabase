using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Service.Components.Acquisition.Downloads;
using Bakabase.Service.Components.Acquisition.Steps;
using Microsoft.Extensions.DependencyInjection;
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
        new(Path.Combine(_root, "engine-cache"), NullLogger<BuiltInTorrentDownloader>.Instance, false);

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [Timeout(90000)]
    public async Task TheRealStepDownloadsAndVerifiesANestedFileSetFromALoopbackSeed(bool useMagnet, bool selectedFromSharingPage)
    {
        await using var seed = await LocalSeed.CreateAsync(Path.Combine(_root, "seed"));
        var working = Path.Combine(_root, "task");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IAcquisitionTorrentDownloader>(Downloader());
        using var provider = services.BuildServiceProvider();
        var progress = new List<int>();
        var context = new AcquisitionStepContext(provider, NullLogger.Instance, (percent, _) =>
        {
            progress.Add(percent);
            return Task.CompletedTask;
        }, working);
        var item = (AcquisitionWorkItem)new AcquisitionRequestedTrigger().ExtractItems(new AcquisitionRequestedPayload
        {
            ResourceId = 1,
            LeadKind = useMagnet ? AcquisitionLeadKind.Magnet : AcquisitionLeadKind.Torrent,
            LeadValue = useMagnet ? seed.Magnet : seed.MetadataUrl,
            WorkingDirectory = working
        }).Single();
        if (selectedFromSharingPage)
        {
            var parsedLink = new AcquisitionLink(seed.MetadataUrl, DriveKind: AcquisitionDriveKind.DirectUrl);
            item = item with
            {
                LeadKind = AcquisitionLeadKind.SharedPage,
                LeadValue = "https://example.invalid/sharing-page",
                Links = [parsedLink],
                SelectedLinkIndex = 0
            };
        }
        IAcquisitionStep step = useMagnet ? new FetchMagnetStep() : new FetchTorrentStep();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await step.ExecuteAsync(context, item, timeout.Token);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome,
            outcome is AcquisitionStepOutcome.Fail failure ? failure.Message : "The download must finish.");
        var downloaded = ((AcquisitionStepOutcome.Continue)outcome).Item;
        Assert.IsTrue(downloaded.PreserveDirectoryStructure);
        await AssertFiles(seed, downloaded.ExtractedDirectory!, downloaded.Files);
        Assert.AreEqual(100, progress.Last());
        Assert.IsTrue(seed.StoppedAnnounces > 0, "the downloader announces stop instead of continuing to seed");
        Assert.IsTrue(Directory.GetFiles(working, "*.torrent", SearchOption.AllDirectories).Length == 0,
            "engine metadata must not enter the resource's content directory");
        foreach (var file in downloaded.Files)
        {
            using var exclusive = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        Directory.Move(downloaded.ExtractedDirectory!, Path.Combine(_root, "placed"));
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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Implementations;
using Bakabase.Tests.Notices;
using Bootstrap.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MonoTorrent;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // Owner tests replace the process-wide .NET system proxy.
public sealed class BtNetworkOptionsProviderTests
{
    [TestMethod]
    public void BtDefaultsToDirectRegardlessOfGlobalOrSiteProxy()
    {
        var options = new NetworkOptions
        {
            Proxy = new() {Mode = NetworkOptions.ProxyMode.UseSystem},
            ThirdPartyProxies = new() {[1] = new() {Mode = NetworkOptions.ProxyMode.UseSystem}}
        };
        var snapshot = new BtNetworkOptionsProvider(new TestBOptions<NetworkOptions>(options)).GetOptions();
        Assert.IsFalse(snapshot.UseSystemProxy);
        Assert.IsNull(snapshot.ProxyUri);
        options.BtProxy = new() {Mode = NetworkOptions.ProxyMode.UseSystem};
        snapshot = new BtNetworkOptionsProvider(new TestBOptions<NetworkOptions>(options)).GetOptions();
        Assert.IsTrue(snapshot.UseSystemProxy);
        Assert.IsNull(snapshot.ProxyUri);
    }

    [TestMethod]
    [DataRow("http://127.0.0.1:8080")]
    [DataRow("socks5://127.0.0.1:1080")]
    public async Task SharedSavedProxiesCanBeSelectedForBtWithoutChangingOtherSelections(string address)
    {
        var manager = new StubOptions<NetworkOptions>(new()
        {
            Proxy = new() {Mode = NetworkOptions.ProxyMode.UseSystem},
            CustomProxies = [new() {Id = "test", Address = address,
                Credentials = new() {Username = "user", Password = "secret"}}]
        });
        using var services = new ServiceCollection().BuildServiceProvider();
        var pool = new BakabaseOptionsManagerPool(services);
        pool.AllOptionsManagers[typeof(NetworkOptions)] = manager;
        var controller = new OptionsController(null!, null!, pool, null!, null!, null!, null!, null!);
        var response = await controller.PatchNetworkOptions(new()
            {BtProxy = new() {Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "test"}});
        Assert.AreEqual((int)ResponseCode.Success, response.Code);
        var snapshot = new BtNetworkOptionsProvider(manager).GetOptions();
        Assert.AreEqual(new Uri(address), snapshot.ProxyUri);
        Assert.AreEqual("user", snapshot.Credentials!.UserName);
        Assert.AreEqual(NetworkOptions.ProxyMode.UseSystem, manager.Value.Proxy.Mode);

        response = await controller.PatchNetworkOptions(new() {BtProxy = new() {Mode = NetworkOptions.ProxyMode.UseSystem}});
        Assert.AreEqual((int)ResponseCode.Success, response.Code);
        Assert.IsTrue(new BtNetworkOptionsProvider(manager).GetOptions().UseSystemProxy);
        Assert.AreEqual(2, manager.SaveCount);
    }

    [TestMethod]
    [DataRow("https://127.0.0.1:8080")]
    [DataRow("socks4://127.0.0.1:1080")]
    [DataRow("http://user:secret@127.0.0.1:8080")]
    public async Task UnsupportedCustomProxyIsRejectedBeforeSaving(string address)
    {
        var manager = new StubOptions<NetworkOptions>(new() {CustomProxies = [new() {Id = "test", Address = address}]});
        using var services = new ServiceCollection().BuildServiceProvider();
        var pool = new BakabaseOptionsManagerPool(services);
        pool.AllOptionsManagers[typeof(NetworkOptions)] = manager;
        var controller = new OptionsController(null!, null!, pool, null!, null!, null!, null!, null!);
        var response = await controller.PatchNetworkOptions(new()
            {BtProxy = new() {Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "test"}});
        Assert.AreEqual((int)ResponseCode.InvalidPayloadOrOperation, response.Code);
        Assert.AreEqual(0, manager.SaveCount);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [Timeout(30000)]
    public async Task FatalSystemProxyErrorsTerminateMetadataAndMagnetOwnersPromptly(bool magnet, bool authentication)
    {
        await using var proxy = new AuthenticationRejectingProxy();
        var previous = HttpClient.DefaultProxy;
        var root = Path.Combine(Path.GetTempPath(), "BakabaseBtProxy_" + Guid.NewGuid().ToString("N"));
        HttpClient.DefaultProxy = new WebProxy(authentication ? proxy.Address : new Uri("socks4://127.0.0.1:1"));
        try
        {
            var downloader = new BuiltInTorrentDownloader(Path.Combine(root, "cache"),
                NullLogger<BuiltInTorrentDownloader>.Instance, false, networkOptions: new SystemOptions());
            var metadata = TorrentFixture.Create("test", new Dictionary<string, byte[]> { ["file.bin"] = [1, 2, 3] },
                "http://tracker.invalid/announce");
            var torrent = Torrent.Load(metadata);
            var request = new TorrentDownloadRequest
            {
                Metadata = magnet ? null : metadata,
                MagnetUri = magnet ? $"magnet:?xt=urn:btih:{torrent.InfoHashes.V1!.ToHex()}&tr=http%3A%2F%2Ftracker.invalid%2Fannounce" : null,
                WorkingDirectory = Path.Combine(root, "files"), Timeout = TimeSpan.FromHours(4)
            };
            var error = await Assert.ThrowsAsync<IOException>(() => downloader.DownloadAsync(request, null, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(12)));
            StringAssert.Contains(error.Message, authentication ? "407" : "unsupported");
            Assert.IsFalse(error.Message.Contains("secret", StringComparison.Ordinal));
        }
        finally
        {
            HttpClient.DefaultProxy = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class SystemOptions : ITorrentNetworkOptionsProvider
    {
        public TorrentNetworkOptions GetOptions() => new() {UseSystemProxy = true};
    }

    private sealed class AuthenticationRejectingProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _connections = [];
        private readonly Task _accept;
        public Uri Address { get; }
        public AuthenticationRejectingProxy()
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _accept = Accept();
        }
        private async Task Accept()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _connections.Add(Handle(client));
                }
            }
            catch (OperationCanceledException) { }
        }
        private async Task Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    var header = new StringBuilder();
                    var next = new byte[1];
                    while (header.Length < 16384)
                    {
                        if (await stream.ReadAsync(next, _stop.Token) == 0) return;
                        header.Append((char)next[0]);
                        if (header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) break;
                    }
                    await stream.WriteAsync("HTTP/1.1 407 Proxy Authentication Required\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), _stop.Token);
                }
                catch (IOException) { }
                catch (OperationCanceledException) { }
            }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _accept;
            await Task.WhenAll(_connections);
            _stop.Dispose();
        }
    }
}

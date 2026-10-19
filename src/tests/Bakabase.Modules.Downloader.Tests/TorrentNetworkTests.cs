using System.Net;
using System.Net.Sockets;
using System.Text;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TorrentNetworkTests
{
    [TestMethod]
    [Timeout(10000)]
    public async Task DirectHttpExplicitlyIgnoresTheSystemProxy()
    {
        var original = HttpClient.DefaultProxy;
        var systemProxy = new RejectProxy();
        HttpClient.DefaultProxy = systemProxy;
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            var response = Task.Run(async () =>
            {
                using var connection = await server.AcceptTcpClientAsync();
                await WriteHttpResponse(connection.GetStream());
            });
            using var http = new TorrentNetwork(new TorrentNetworkOptions()).CreateHttpClient();
            Assert.AreEqual("ok", await http.GetStringAsync($"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/payload"));
            await response;
            Assert.AreEqual(0, systemProxy.Calls, "The system proxy is not even consulted for a direct transfer.");
        }
        finally { HttpClient.DefaultProxy = original; }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task HttpRequestsResolveDestinationsAtTheChosenSocksProxyAndAuthenticate()
    {
        using var proxy = new TcpListener(IPAddress.Loopback, 0);
        proxy.Start();
        string? destination = null;
        var serving = Task.Run(async () =>
        {
            using var connection = await proxy.AcceptTcpClientAsync();
            var stream = connection.GetStream();
            destination = await Handshake(stream, "test-user", "test-secret");
            await WriteHttpResponse(stream);
        });
        var network = new TorrentNetwork(new TorrentNetworkOptions
        {
            ProxyUri = new Uri($"socks5://127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}"),
            Credentials = new NetworkCredential("test-user", "test-secret")
        });
        using var http = network.CreateHttpClient();
        Assert.AreEqual("ok", await http.GetStringAsync("http://a-torrent-host.invalid:8123/content"));
        await serving;
        Assert.AreEqual("a-torrent-host.invalid:8123", destination, "A hostname which has no local DNS record must reach SOCKS intact.");
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task PeerConnectionsUseTheSameSocksPolicyAndUdpTrackersAreDisabled()
    {
        using var proxy = new TcpListener(IPAddress.Loopback, 0);
        proxy.Start();
        string? destination = null;
        var serving = Task.Run(async () =>
        {
            using var connection = await proxy.AcceptTcpClientAsync();
            destination = await Handshake(connection.GetStream());
            await connection.GetStream().WriteAsync("hello"u8.ToArray());
        });
        var network = new TorrentNetwork(new TorrentNetworkOptions
        {
            ProxyUri = new Uri($"socks5://127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}")
        });
        var factories = network.CreateFactories();
        Assert.IsNull(factories.CreateTracker(new Uri("udp://127.0.0.1:6969/announce")));
        using var peer = factories.CreatePeerConnection(new Uri("ipv4://192.0.2.10:6881"))!;
        await peer.ConnectAsync();
        var data = new byte[5];
        var received = 0;
        while (received < data.Length) received += await peer.ReceiveAsync(data.AsMemory(received));
        Assert.AreEqual("hello", Encoding.ASCII.GetString(data));
        await serving;
        Assert.AreEqual("192.0.2.10:6881", destination);

        var downloader = new BuiltInTorrentDownloader(Path.GetTempPath(), NullLogger<BuiltInTorrentDownloader>.Instance, true);
        var settings = downloader.SettingsFor(Path.Combine(Path.GetTempPath(), "bt-proxy-test"), true);
        Assert.IsNull(settings.DhtEndPoint);
        Assert.IsFalse(settings.AutoSaveLoadDhtCache);
        Assert.IsFalse(settings.AllowLocalPeerDiscovery);
        Assert.AreEqual(0, settings.ListenEndPoints.Count);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ARejectedSocksConnectionNeverFallsBackToTheDestination()
    {
        using var destination = new TcpListener(IPAddress.Loopback, 0);
        destination.Start();
        using var proxy = new TcpListener(IPAddress.Loopback, 0);
        proxy.Start();
        var serving = Task.Run(async () =>
        {
            using var connection = await proxy.AcceptTcpClientAsync();
            await Handshake(connection.GetStream(), rejectConnect: true);
        });
        var network = new TorrentNetwork(new TorrentNetworkOptions
        {
            ProxyUri = new Uri($"socks5://127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}")
        });
        using var http = network.CreateHttpClient();
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
            http.GetStringAsync($"http://127.0.0.1:{((IPEndPoint)destination.LocalEndpoint).Port}/payload"));
        await serving;
        Assert.IsFalse(destination.Pending(), "A proxy failure must never cause a direct destination connection.");
    }

    [TestMethod]
    public void AnUnsupportedOrCredentialBearingProxyFailsBeforeAnyConnection()
    {
        foreach (var proxy in new[] {"http://127.0.0.1:8080", "socks5://user:secret@127.0.0.1:1080"})
        {
            var error = Assert.ThrowsException<ArgumentException>(() =>
                new TorrentNetwork(new TorrentNetworkOptions {ProxyUri = new Uri(proxy)}));
            Assert.IsFalse(error.Message.Contains("secret"));
        }
    }

    private static async Task<string> Handshake(NetworkStream stream, string? user = null, string? password = null,
        bool rejectConnect = false)
    {
        var greeting = new byte[3];
        await stream.ReadExactlyAsync(greeting);
        CollectionAssert.AreEqual(new byte[] {5, 1, user == null ? (byte)0 : (byte)2}, greeting);
        await stream.WriteAsync(new byte[] {5, greeting[2]});
        if (user != null)
        {
            var authHeader = new byte[2];
            await stream.ReadExactlyAsync(authHeader);
            Assert.AreEqual(1, authHeader[0]);
            var actualUser = new byte[authHeader[1]];
            await stream.ReadExactlyAsync(actualUser);
            var passwordLength = new byte[1];
            await stream.ReadExactlyAsync(passwordLength);
            var actualPassword = new byte[passwordLength[0]];
            await stream.ReadExactlyAsync(actualPassword);
            Assert.AreEqual(user, Encoding.UTF8.GetString(actualUser));
            Assert.AreEqual(password, Encoding.UTF8.GetString(actualPassword));
            await stream.WriteAsync(new byte[] {1, 0});
        }
        var header = new byte[4];
        await stream.ReadExactlyAsync(header);
        Assert.AreEqual(5, header[0]);
        Assert.AreEqual(1, header[1]);
        var length = header[3] switch {1 => 4, 4 => 16, _ => 0};
        if (header[3] == 3)
        {
            var domainLength = new byte[1];
            await stream.ReadExactlyAsync(domainLength);
            length = domainLength[0];
        }
        var address = new byte[length];
        await stream.ReadExactlyAsync(address);
        var port = new byte[2];
        await stream.ReadExactlyAsync(port);
        await stream.WriteAsync(new byte[] {5, rejectConnect ? (byte)5 : (byte)0, 0, 1, 127, 0, 0, 1, 0, 0});
        return (header[3] == 3 ? Encoding.ASCII.GetString(address) : new IPAddress(address).ToString()) + ":" + ((port[0] << 8) | port[1]);
    }

    private static async Task WriteHttpResponse(NetworkStream stream)
    {
        var request = new byte[4096];
        var length = await stream.ReadAsync(request);
        Assert.IsTrue(length > 0);
        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
    }

    private sealed class RejectProxy : IWebProxy
    {
        public int Calls;
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) { Calls++; throw new InvalidOperationException("System proxy must not be queried."); }
        public bool IsBypassed(Uri host) { Calls++; return false; }
    }
}

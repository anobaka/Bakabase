using System.Net;
using System.Net.Sockets;
using System.Text;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public sealed class TorrentSystemProxyTests
{
    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(15));
    [TestCleanup] public void Cleanup() => _deadline.Dispose();
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HttpConnect_UsesRemoteDestinationAndCredentialsWithoutLosingFirstTunnelBytes(bool system)
    {
        string? request = null;
        await using var proxy = new OneConnectionServer(async stream =>
        {
            request = await ReadHeaders(stream);
            await stream.WriteAsync("HTTP/1.1 200 Connection established\r\n\r\npeer"u8.ToArray());
        });
        var credentials = new NetworkCredential("user", "secret", "domain");
        var resolver = new SystemProxy(_ => proxy.Address) {Credentials = credentials};
        using var network = new TorrentNetwork(system
            ? new() {UseSystemProxy = true}
            : new() {ProxyUri = proxy.Address, Credentials = credentials}, resolver);
        using var socket = await network.ConnectAsync(new Uri("ipv4://does-not-resolve.invalid:6881"),
            AddressFamily.Unspecified, Deadline);
        using var stream = new NetworkStream(socket, false);
        var payload = new byte[4];
        await stream.ReadExactlyAsync(payload, Deadline);
        CollectionAssert.AreEqual("peer"u8.ToArray(), payload);
        StringAssert.StartsWith(request!, "CONNECT does-not-resolve.invalid:6881 HTTP/1.1\r\n");
        StringAssert.Contains(request!, "Proxy-Authorization: Basic " + Convert.ToBase64String("domain\\user:secret"u8));
        Assert.AreEqual(system ? "http" : null, resolver.Destinations.SingleOrDefault()?.Scheme);
        Assert.IsTrue(network.UsesProxy);
    }

    [TestMethod]
    public async Task SystemSocks5_ResolvesHostnameRemotelyAndAuthenticates()
    {
        string? destination = null;
        await using var proxy = new OneConnectionServer(async stream =>
        {
            var greeting = new byte[3];
            await stream.ReadExactlyAsync(greeting, Deadline);
            CollectionAssert.AreEqual(new byte[] {5, 1, 2}, greeting);
            await stream.WriteAsync(new byte[] {5, 2});
            var auth = new byte[2];
            await stream.ReadExactlyAsync(auth, Deadline);
            var username = new byte[auth[1]];
            await stream.ReadExactlyAsync(username, Deadline);
            var length = new byte[1];
            await stream.ReadExactlyAsync(length, Deadline);
            var password = new byte[length[0]];
            await stream.ReadExactlyAsync(password, Deadline);
            Assert.AreEqual("user", Encoding.UTF8.GetString(username));
            Assert.AreEqual("secret", Encoding.UTF8.GetString(password));
            await stream.WriteAsync(new byte[] {1, 0});
            var header = new byte[5];
            await stream.ReadExactlyAsync(header, Deadline);
            CollectionAssert.AreEqual(new byte[] {5, 1, 0, 3}, header[..4]);
            var address = new byte[header[4]];
            await stream.ReadExactlyAsync(address, Deadline);
            var port = new byte[2];
            await stream.ReadExactlyAsync(port, Deadline);
            destination = Encoding.ASCII.GetString(address);
            Assert.AreEqual(6881, (port[0] << 8) | port[1]);
            await stream.WriteAsync(new byte[] {5, 0, 0, 1, 0, 0, 0, 0, 0, 0, 42});
        });
        var endpoint = new UriBuilder(proxy.Address) {Scheme = "socks5"}.Uri;
        var resolver = new SystemProxy(_ => endpoint) {Credentials = new NetworkCredential("user", "secret")};
        using var network = new TorrentNetwork(new() {UseSystemProxy = true}, resolver);
        using var socket = await network.ConnectAsync("does-not-resolve.invalid", 6881, AddressFamily.Unspecified, Deadline);
        using var stream = new NetworkStream(socket, false);
        var payload = new byte[1];
        await stream.ReadExactlyAsync(payload, Deadline);
        Assert.AreEqual(42, payload[0]);
        Assert.AreEqual("does-not-resolve.invalid", destination);
    }

    [TestMethod]
    [DataRow("http")]
    [DataRow("https")]
    public async Task HttpRequests_PreserveActualSchemeAndPathForSystemResolution(string scheme)
    {
        var resolver = new SystemProxy(_ => new Uri("socks4://127.0.0.1:1"));
        using var network = new TorrentNetwork(new() {UseSystemProxy = true}, resolver);
        using var client = network.CreateHttpClient();
        var destination = new Uri($"{scheme}://does-not-resolve.invalid/source/file.torrent?x=1");
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetAsync(destination, Deadline));
        Assert.AreEqual(destination, resolver.Destinations.Single());
        Assert.IsInstanceOfType<NotSupportedException>(network.ConfigurationError);
        Assert.IsTrue(network.RoutingFailureToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task SystemBypass_ConnectsDirectlyAndDoesNotAskForProxyEndpoint()
    {
        await using var server = new OneConnectionServer(stream => stream.WriteAsync(new byte[] {42}).AsTask());
        var resolver = new SystemProxy(_ => throw new AssertFailedException("GetProxy must not run for a bypassed destination."), _ => true);
        using var network = new TorrentNetwork(new() {UseSystemProxy = true}, resolver);
        using var socket = await network.ConnectAsync(server.Address, AddressFamily.Unspecified, Deadline);
        using var stream = new NetworkStream(socket, false);
        var value = new byte[1];
        await stream.ReadExactlyAsync(value, Deadline);
        Assert.AreEqual(42, value[0]);
        Assert.IsTrue(network.UsesProxy, "System mode must still disable global UDP/DHT routes when one destination bypasses.");
        Assert.IsNull(network.CreateFactories().CreateTracker(new Uri("udp://127.0.0.1:1")));
    }

    [TestMethod]
    public async Task Direct_UsesNeitherSystemProxyNorItsBypassRules()
    {
        await using var server = new OneConnectionServer(async stream =>
        {
            await ReadHeaders(stream);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
        });
        var resolver = new SystemProxy(_ => throw new AssertFailedException(), _ => throw new AssertFailedException());
        using var network = new TorrentNetwork(new(), resolver);
        using var client = network.CreateHttpClient();
        Assert.AreEqual("ok", await client.GetStringAsync(server.Address, Deadline));
        Assert.IsFalse(network.UsesProxy);
    }

    [TestMethod]
    [DataRow(407)]
    [DataRow(502)]
    public async Task RejectedProxy_DoesNotFallBackToReachableDestination(int status)
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        await using var proxy = new OneConnectionServer(async stream =>
        {
            await ReadHeaders(stream);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Rejected\r\n\r\n"));
        });
        using var network = new TorrentNetwork(new() {UseSystemProxy = true}, new SystemProxy(_ => proxy.Address));
        var error = await Assert.ThrowsAsync<IOException>(() => network.ConnectAsync("127.0.0.1",
            ((IPEndPoint)target.LocalEndpoint).Port, AddressFamily.Unspecified, Deadline));
        StringAssert.Contains(error.Message, status.ToString());
        Assert.IsFalse(target.Pending(), "A rejected tunnel must never attempt the direct destination.");
        Assert.AreEqual(status == 407, network.RoutingFailureToken.IsCancellationRequested,
            "Authentication failures terminate the owner; an unreachable individual peer does not.");
    }

    [TestMethod]
    [DataRow("https")]
    [DataRow("socks4")]
    public async Task UnsupportedSystemProxy_FailsExplicitlyWithoutConnectingDirect(string scheme)
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        using var network = new TorrentNetwork(new() {UseSystemProxy = true}, new SystemProxy(_ => new Uri($"{scheme}://127.0.0.1:1")));
        var error = await Assert.ThrowsExceptionAsync<NotSupportedException>(() => network.ConnectAsync("127.0.0.1",
            ((IPEndPoint)target.LocalEndpoint).Port, AddressFamily.Unspecified, Deadline));
        StringAssert.Contains(error.Message, "unsupported");
        Assert.IsFalse(target.Pending());
        Assert.IsTrue(network.RoutingFailureToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task MetadataAndTrackerHttp_TravelInsideConnectTunnel()
    {
        string? connect = null;
        string? tunneled = null;
        await using var proxy = new OneConnectionServer(async stream =>
        {
            connect = await ReadHeaders(stream);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\n\r\n"u8.ToArray());
            tunneled = await ReadHeaders(stream);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
        });
        using var network = new TorrentNetwork(new() {UseSystemProxy = true}, new SystemProxy(_ => proxy.Address));
        using var client = network.CreateHttpClient();
        Assert.AreEqual("ok", await client.GetStringAsync("http://does-not-resolve.invalid:8080/announce?info_hash=test", Deadline));
        StringAssert.StartsWith(connect!, "CONNECT does-not-resolve.invalid:8080 HTTP/1.1");
        StringAssert.StartsWith(tunneled!, "GET /announce?info_hash=test HTTP/1.1");
    }

    [TestMethod]
    public async Task SystemRouting_ReevaluatesDifferentPathsOnTheSameOrigin()
    {
        await using var direct = new OneConnectionServer(async stream =>
        {
            await ReadHeaders(stream);
            // Deliberately keep the connection alive: system/PAC routing must not reuse it.
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\ndirect"u8.ToArray());
            var next = new byte[1];
            Assert.AreEqual(0, await stream.ReadAsync(next, Deadline));
        });
        await using var proxy = new OneConnectionServer(async stream =>
        {
            await ReadHeaders(stream);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\n\r\n"u8.ToArray());
            var request = await ReadHeaders(stream);
            StringAssert.StartsWith(request, "GET /proxy ");
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nproxy"u8.ToArray());
        });
        var resolver = new SystemProxy(_ => proxy.Address, uri => uri.AbsolutePath == "/direct");
        using var network = new TorrentNetwork(new() {UseSystemProxy = true}, resolver);
        using var client = network.CreateHttpClient();
        Assert.AreEqual("direct", await client.GetStringAsync(new Uri(direct.Address, "/direct"), Deadline));
        Assert.AreEqual("proxy", await client.GetStringAsync(new Uri(direct.Address, "/proxy"), Deadline));
        Assert.AreEqual("/proxy", resolver.Destinations.Single().AbsolutePath);
    }

    private CancellationToken Deadline => _deadline.Token;

    private async Task<string> ReadHeaders(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var next = new byte[1];
        while (bytes.Count < 16384)
        {
            await stream.ReadExactlyAsync(next, Deadline);
            bytes.Add(next[0]);
            if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10)
                return Encoding.ASCII.GetString(bytes.ToArray());
        }
        throw new IOException("Test request too large.");
    }

    private sealed class SystemProxy(Func<Uri, Uri?> resolve, Func<Uri, bool>? bypass = null) : IWebProxy
    {
        public List<Uri> Destinations { get; } = [];
        public ICredentials? Credentials { get; set; }
        public Uri? GetProxy(Uri destination) { Destinations.Add(destination); return resolve(destination); }
        public bool IsBypassed(Uri host) => bypass?.Invoke(host) ?? false;
    }

    private sealed class OneConnectionServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15));
        private readonly Task _connection;
        public Uri Address { get; }

        public OneConnectionServer(Func<NetworkStream, Task> handle)
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _connection = Run(handle);
        }

        private async Task Run(Func<NetworkStream, Task> handle)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            using var stream = client.GetStream();
            await handle(stream);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _connection; }
            catch (OperationCanceledException) { }
            finally { _stop.Dispose(); }
        }
    }
}

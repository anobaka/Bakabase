using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// A proxy on this computer in fake-IP or TUN mode answers names with addresses of its own
/// (198.18.0.0/15). For a name only the LAN knows, connecting there reaches the proxy, never the
/// device — so it is not attempted, and the failure says so rather than "cannot connect". A
/// domain the proxy resolves itself is connected to through it, and only a failure there is
/// said to be the proxy's.
/// </summary>
[TestClass]
public class ProxyFakeAddressTests
{
    [TestMethod]
    [DataRow("198.18.0.0", true)]
    [DataRow("198.18.0.29", true, DisplayName = "What Clash answered jaxs-Mac-mini.local with")]
    [DataRow("198.19.255.255", true)]
    [DataRow("::ffff:198.18.0.1", true, DisplayName = "IPv4-mapped")]
    [DataRow("198.17.255.255", false)]
    [DataRow("198.20.0.0", false)]
    [DataRow("192.168.1.5", false)]
    [DataRow("10.0.0.2", false)]
    [DataRow("fe80::1", false)]
    [DataRow("2001:db8::1", false)]
    public void Only_198_18_0_0_15_is_a_proxys_address(string address, bool fake)
    {
        Assert.AreEqual(fake, ProxyFakeAddresses.Contains(IPAddress.Parse(address)));
    }

    [TestMethod]
    public void A_name_the_proxy_answered_first_is_refused_as_the_proxys()
    {
        var refused = Assert.ThrowsExactly<ProxyFakeAddressException>(() =>
            ProxyFakeAddresses.Screen("mac.local", [IPAddress.Parse("198.18.0.29"), IPAddress.Parse("192.168.1.5")]));

        Assert.AreEqual("mac.local", refused.Host);
        Assert.AreEqual(IPAddress.Parse("198.18.0.29"), refused.Address);
        // Still a socket failure to whatever only knows those.
        Assert.AreEqual(SocketError.HostUnreachable, refused.SocketErrorCode);
        StringAssert.Contains(refused.Message, "mac.local");
        StringAssert.Contains(refused.Message, "198.18.0.29");
    }

    [TestMethod]
    public void A_proxys_address_behind_a_real_answer_is_only_left_out()
    {
        CollectionAssert.AreEqual(new[] {IPAddress.Parse("192.168.1.5"), IPAddress.Parse("fe80::1")},
            ProxyFakeAddresses.Screen("nas", [
                IPAddress.Parse("192.168.1.5"), IPAddress.Parse("198.18.0.3"), IPAddress.Parse("fe80::1")
            ]).ToArray());
        Assert.AreEqual(0, ProxyFakeAddresses.Screen("nas", []).Count);
    }

    [TestMethod]
    public void It_is_found_however_deep_an_http_client_wrapped_it()
    {
        var proxy = new ProxyFakeAddressException("mac.local", IPAddress.Parse("198.18.0.29"));

        Assert.AreSame(proxy, ProxyFakeAddresses.Find(new HttpRequestException("connect failed", proxy)));
        Assert.AreSame(proxy,
            ProxyFakeAddresses.Find(new AggregateException(new IOException("other"),
                new TaskCanceledException("wrapped", new HttpRequestException("x", proxy)))));
        Assert.IsNull(ProxyFakeAddresses.Find(new HttpRequestException("refused",
            new SocketException((int) SocketError.ConnectionRefused))));
        Assert.IsNull(ProxyFakeAddresses.Find(null));
    }

    [TestMethod]
    public async Task A_name_resolving_to_a_proxys_address_is_never_dialled()
    {
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = Connector(["198.18.0.29"], dialled);

        var refused = await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            connector.ConnectAsync(new DnsEndPoint("jaxs-Mac-mini.local", 34567), CancellationToken.None).AsTask());

        Assert.AreEqual("jaxs-Mac-mini.local", refused.Host);
        Assert.AreEqual(0, dialled.Count);
    }

    [TestMethod]
    public async Task A_proxys_address_typed_as_it_is_is_refused_the_same_way()
    {
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = Connector([], dialled);

        var refused = await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            connector.ConnectAsync(new DnsEndPoint("198.18.0.29", 34567), CancellationToken.None).AsTask());

        Assert.AreEqual(IPAddress.Parse("198.18.0.29"), refused.Address);
        Assert.AreEqual(0, dialled.Count);
    }

    [TestMethod]
    public async Task A_real_address_beside_a_proxys_is_connected_to_alone()
    {
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = Connector(["192.168.1.5", "198.18.0.7"], dialled);

        await using var stream = await connector.ConnectAsync(new DnsEndPoint("nas", 34567), CancellationToken.None);

        CollectionAssert.AreEqual(new[] {new IPEndPoint(IPAddress.Parse("192.168.1.5"), 34567)}, dialled.ToArray());
    }

    [TestMethod]
    public async Task An_http_client_on_the_connector_reports_it_through_its_own_exception()
    {
        // What every caller actually sees: SocketsHttpHandler wraps what its connect step threw.
        var connector = Connector(["198.18.0.29"], new ConcurrentQueue<IPEndPoint>());
        using var http = new HttpClient(new SocketsHttpHandler {UseProxy = false, ConnectCallback = connector.ConnectCallback});

        var failed = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            http.GetAsync("http://jaxs-Mac-mini.local:34567/remote-access/server-info"));

        Assert.IsNotNull(ProxyFakeAddresses.Find(failed));
    }

    [TestMethod]
    [DataRow("nas.example.com", true)]
    [DataRow("nas.example-ddns.net.", true)]
    [DataRow("home.lan", true, DisplayName = "A domain a proxy may resolve with its own DNS")]
    [DataRow("jaxs-Mac-mini.local", false)]
    [DataRow("fileserver.corp.local", false)]
    [DataRow("NAS.LOCAL.", false)]
    [DataRow("PC1", false, DisplayName = "A computer name only the LAN knows")]
    [DataRow("PC1.", false)]
    [DataRow("198.18.0.29", false)]
    [DataRow("192.168.1.5", false)]
    [DataRow("[fe80::1]", false)]
    [DataRow("fe80::1%4", false)]
    public void Only_a_domain_is_one_a_proxy_can_reach_on_its_own(string host, bool itself)
    {
        Assert.AreEqual(itself, ProxyFakeAddresses.ProxyResolvesItself(host));
    }

    [TestMethod]
    public void A_domain_the_proxy_answered_is_left_to_the_proxy_and_said_to_go_through_it()
    {
        IReadOnlyList<IPAddress> resolved = [IPAddress.Parse("198.18.0.5")];

        var screened = ProxyFakeAddresses.Screen("nas.example.com", resolved, out var throughProxy);

        CollectionAssert.AreEqual(resolved.ToArray(), screened.ToArray());
        Assert.AreEqual(IPAddress.Parse("198.18.0.5"), throughProxy);

        // A real first answer keeps nothing of the proxy's, whatever the name.
        CollectionAssert.AreEqual(new[] {IPAddress.Parse("192.168.1.5")},
            ProxyFakeAddresses.Screen("nas.example.com", [IPAddress.Parse("192.168.1.5"), IPAddress.Parse("198.18.0.5")],
                out throughProxy).ToArray());
        Assert.IsNull(throughProxy);
    }

    [TestMethod]
    public async Task A_domain_resolving_to_a_proxys_address_is_dialled_there_and_an_http_client_gets_its_answer()
    {
        // Under Clash in TUN and fake-IP mode a DDNS or public name connects through the proxy,
        // as for every other program: the connection must not be refused for it.
        using var server = new OneAnswerHttpServer();
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = new DualStackConnector((_, _) => Task.FromResult(new[] {IPAddress.Parse("198.18.0.5")}),
            async (to, ct) =>
            {
                dialled.Enqueue(to);
                var socket = new TcpClient();
                await socket.ConnectAsync(IPAddress.Loopback, server.Port, ct);
                return socket.GetStream();
            });
        using var http = new HttpClient(new SocketsHttpHandler {UseProxy = false, ConnectCallback = connector.ConnectCallback});

        var answer = await http.GetStringAsync("http://nas.example.com:34567/remote-access/server-info");

        Assert.AreEqual("ok", answer);
        CollectionAssert.AreEqual(new[] {new IPEndPoint(IPAddress.Parse("198.18.0.5"), 34567)}, dialled.ToArray());
    }

    [TestMethod]
    public async Task A_domain_the_proxy_does_not_get_through_to_is_said_to_be_behind_the_proxy()
    {
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = new DualStackConnector((_, _) => Task.FromResult(new[] {IPAddress.Parse("198.18.0.5")}),
            (to, _) =>
            {
                dialled.Enqueue(to);
                return ValueTask.FromException<Stream>(new SocketException((int) SocketError.HostUnreachable));
            });

        var failed = await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            connector.ConnectAsync(new DnsEndPoint("nas.example.com", 34567), CancellationToken.None).AsTask());

        Assert.AreEqual("nas.example.com", failed.Host);
        Assert.AreEqual(IPAddress.Parse("198.18.0.5"), failed.Address);
        Assert.IsTrue(failed.ProxyResolvesItself);
        Assert.AreEqual(SocketError.HostUnreachable, ((SocketException) failed.ConnectError!).SocketErrorCode);
        StringAssert.Contains(failed.Message, "could not be reached through the proxy");
        Assert.AreEqual(1, dialled.Count);

        // Through an HTTP client as well, which is how every caller sees it.
        using var http = new HttpClient(new SocketsHttpHandler {UseProxy = false, ConnectCallback = connector.ConnectCallback});
        var wrapped = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            http.GetAsync("http://nas.example.com:34567/remote-access/server-info"));

        Assert.AreEqual(true, ProxyFakeAddresses.Find(wrapped)?.ProxyResolvesItself);
    }

    [TestMethod]
    [DataRow("mac.local")]
    [DataRow("PC1")]
    public async Task A_name_only_the_LAN_knows_resolving_to_a_proxys_address_is_still_never_dialled(string host)
    {
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = Connector(["198.18.0.29"], dialled);

        var refused = await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            connector.ConnectAsync(new DnsEndPoint(host, 34567), CancellationToken.None).AsTask());

        Assert.IsFalse(refused.ProxyResolvesItself);
        Assert.IsNull(refused.ConnectError);
        StringAssert.Contains(refused.Message, "nothing was sent to it");
        Assert.AreEqual(0, dialled.Count);
    }

    [TestMethod]
    public async Task Connecting_again_where_a_domain_led_goes_through_the_proxy_and_nowhere_else()
    {
        // A relay's later connections go to the address its question reached: for a domain the
        // proxy took over, the proxy's.
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = Connector(["192.168.1.9"], dialled);
        var reached = IPAddress.Parse("198.18.0.5");

        await using (await connector.ConnectAgainAsync(new DnsEndPoint("nas.example.com", 34567), reached,
                         CancellationToken.None))
        {
        }

        CollectionAssert.AreEqual(new[] {new IPEndPoint(reached, 34567)}, dialled.ToArray());

        // Never for a name only the LAN knows, which would not have led there.
        await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            connector.ConnectAgainAsync(new DnsEndPoint("nas.local", 34567), reached, CancellationToken.None).AsTask());
        Assert.AreEqual(1, dialled.Count);

        // A failure there is the proxy's.
        var failing = new DualStackConnector((_, _) => Task.FromResult(Array.Empty<IPAddress>()),
            (_, _) => ValueTask.FromException<Stream>(new SocketException((int) SocketError.ConnectionRefused)));

        Assert.IsNotNull((await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            failing.ConnectAgainAsync(new DnsEndPoint("nas.example.com", 34567), reached, CancellationToken.None)
                .AsTask())).ConnectError);
    }

    private static DualStackConnector Connector(string[] resolvesTo, ConcurrentQueue<IPEndPoint> dialled) =>
        new((_, _) => Task.FromResult(resolvesTo.Select(IPAddress.Parse).ToArray()),
            (to, _) =>
            {
                dialled.Enqueue(to);
                return new ValueTask<Stream>(new MemoryStream());
            });

    /// <summary>A loopback HTTP server answering every request with <c>ok</c>.</summary>
    private sealed class OneAnswerHttpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public OneAnswerHttpServer()
        {
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint) _listener.LocalEndpoint).Port;

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var stream = client.GetStream();
                    var buffer = new byte[4096];
                    var request = "";

                    while (!request.Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await stream.ReadAsync(buffer, _stop.Token);

                        if (read == 0)
                        {
                            break;
                        }

                        request += Encoding.ASCII.GetString(buffer, 0, read);
                    }

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"), _stop.Token);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException
                                          or IOException)
            {
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }
}

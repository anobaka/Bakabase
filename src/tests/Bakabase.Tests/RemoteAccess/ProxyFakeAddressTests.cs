using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// A proxy on this computer in fake-IP or TUN mode answers names with addresses of its own
/// (198.18.0.0/15). Connecting there reaches the proxy, never the device — so it is not
/// attempted, and the failure says so rather than "cannot connect".
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

    private static DualStackConnector Connector(string[] resolvesTo, ConcurrentQueue<IPEndPoint> dialled) =>
        new((_, _) => Task.FromResult(resolvesTo.Select(IPAddress.Parse).ToArray()),
            (to, _) =>
            {
                dialled.Enqueue(to);
                return new ValueTask<Stream>(new MemoryStream());
            });
}

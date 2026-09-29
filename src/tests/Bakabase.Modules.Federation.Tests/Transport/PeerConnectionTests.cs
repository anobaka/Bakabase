using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Transport;

/// <summary>
/// How library sharing connects to a peer, through the client <c>AddFederationPeers</c>
/// composes: a name's IPv4 address is not held up behind an IPv6 one that drops everything (a
/// Windows computer name resolves IPv6-first, and a Bakabase server listens on IPv4 only), and
/// a peer that is switched off is reported after the 2 s a connection gets once resolved.
/// </summary>
/// <remarks>
/// The names and the dropped address are the test's own (a <see cref="DualStackConnector"/>
/// service); everything else — the handler, its timeouts, <see cref="FederationHttpClient"/> —
/// is what the app composes, and the peer is a real listener.
/// </remarks>
[TestClass]
public sealed class PeerConnectionTests
{
    private static readonly IPAddress Dropped = IPAddress.Parse("fe80::1");
    private static readonly IPAddress SwitchedOff = IPAddress.Parse("192.0.2.1");

    [TestMethod]
    public async Task A_peer_known_by_a_name_that_resolves_IPv6_first_answers_at_once()
    {
        using var peer = new InfoListener();
        using var provider = Compose(host => host == "peer-pc" ? [Dropped, IPAddress.Loopback] : []);
        var http = provider.GetRequiredService<FederationHttpClient>();

        var watch = Stopwatch.StartNew();
        var info = await http.PublicAsync<JsonElement>($"http://peer-pc:{peer.Port}", HttpMethod.Get,
            "/federation/v1/info", null, CancellationToken.None);

        Assert.AreEqual("peer", info.GetProperty("nodeId").GetString());
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(1.5), $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task A_peer_switched_off_at_its_address_is_unreachable_after_two_seconds_not_five()
    {
        using var provider = Compose(_ => []);
        var http = provider.GetRequiredService<FederationHttpClient>();

        var watch = Stopwatch.StartNew();
        var unreachable = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => http.PublicAsync<JsonElement>(
            $"http://{SwitchedOff}:34567", HttpMethod.Get, "/federation/v1/info", null, CancellationToken.None));

        Assert.AreEqual("NodeUnreachable", unreachable.ErrorCode);
        Assert.IsTrue(watch.Elapsed >= TimeSpan.FromSeconds(1.5), $"took {watch.Elapsed}");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(4), $"took {watch.Elapsed}");
    }

    /// <summary>
    /// The app's peer client over a network where <paramref name="resolve"/> says what a name is,
    /// <see cref="Dropped"/> and <see cref="SwitchedOff"/> drop every connection, and anything else
    /// is a real connection.
    /// </summary>
    private static ServiceProvider Compose(Func<string, IPAddress[]> resolve)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DualStackConnector(
            (host, _) => Task.FromResult(resolve(host)),
            async (to, ct) =>
            {
                if (to.Address.Equals(Dropped) || to.Address.Equals(SwitchedOff))
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }

                var socket = new Socket(to.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(to, ct);
                    return new NetworkStream(socket, true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }));
        services.AddFederationPeers();
        return services.BuildServiceProvider();
    }

    /// <summary>A peer answering <c>/federation/v1/info</c> on loopback, as plain HTTP/1.1.</summary>
    private sealed class InfoListener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public InfoListener()
        {
            _listener.Start();
            _ = ServeAsync();
        }

        public int Port => ((IPEndPoint) _listener.LocalEndpoint).Port;

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    var request = new StringBuilder();
                    var buffer = new byte[4096];

                    while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await stream.ReadAsync(buffer, _stop.Token);
                        if (read == 0) break;
                        request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    var body = Encoding.UTF8.GetBytes("{\"nodeId\":\"peer\"}");
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                                                       $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, _stop.Token);
                    await stream.WriteAsync(body, _stop.Token);
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
        }
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Service.Components.Federation;
using Bakabase.Tests.RemoteAccess.Console;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// Library sharing's "Find nearby devices": what answered the beacons, asked who it is over the
/// connector the host composes — with only the network underneath being the test's.
/// </summary>
[TestClass]
public sealed class FederationNodeDiscoveryTests
{
    [TestMethod]
    public async Task A_copy_of_this_node_on_another_machine_is_listed_and_this_node_itself_is_not()
    {
        using var nodes = new NodeInfoListener(new Dictionary<string, (string NodeId, string Name)>
        {
            ["this-pc"] = ("local", "This PC"),
            ["copy-pc"] = ("local", "Copy PC"),
            ["other-pc"] = ("other", "Other PC"),
            ["second-install"] = ("second", "Second install")
        });
        // Each name as Windows resolves a computer name: its IPv6 address first, where every
        // connection is dropped.
        var network = new TestNetwork();
        foreach (var name in new[] {"this-pc", "copy-pc", "other-pc", "second-install"})
            network.Name(name, TestNetwork.Dropped, IPAddress.Loopback);
        DiscoveredServer Answer(string host, bool isThisMachine) =>
            new($"beacon-{host}", host, $"http://{host}:{nodes.Port}", "9.9.9", 1, isThisMachine);
        var beacons = new FakeDiscovery
        {
            Found =
            [
                Answer("this-pc", true), Answer("copy-pc", false), Answer("other-pc", false),
                // Another install on this same machine is another node.
                Answer("second-install", true)
            ]
        };

        var found = await new FederationNodeDiscovery(beacons, new FixedIdentity("local"), network.Connector)
            .DiscoverAsync(CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] {"Copy PC", "Other PC", "Second install"},
            found.Select(c => c.Name).ToArray());
        // Under this node's own id, from another machine: a copy, which connecting to then says.
        Assert.AreEqual($"http://copy-pc:{nodes.Port}", found.Single(c => c.NodeId == "local").Address);
    }

    private sealed class FixedIdentity(string nodeId) : INodeIdentityProvider
    {
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeIdentity(nodeId, "epoch", "This PC"));
    }

    /// <summary>Answers <c>/federation/v1/info</c> on loopback as the node its <c>Host</c> names.</summary>
    private sealed class NodeInfoListener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly IReadOnlyDictionary<string, (string NodeId, string Name)> _nodes;

        public NodeInfoListener(IReadOnlyDictionary<string, (string NodeId, string Name)> nodes)
        {
            _nodes = nodes;
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint) _listener.LocalEndpoint).Port;

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = AnswerAsync(client);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        }

        private async Task AnswerAsync(TcpClient client)
        {
            using var _ = client;

            try
            {
                await using var stream = client.GetStream();
                var request = new StringBuilder();
                var buffer = new byte[4096];

                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0) return;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var host = request.ToString().Split("\r\n")
                    .First(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))[5..].Trim()
                    .Split(':')[0];
                var (nodeId, name) = _nodes[host];
                var body = Encoding.UTF8.GetBytes(
                    $"{{\"nodeId\":\"{nodeId}\",\"libraryEpoch\":\"epoch\",\"name\":\"{name}\",\"protocolVersion\":1," +
                    "\"serverTimeUtc\":\"2026-01-01T00:00:00Z\"}");
                var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                                                   $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, _stop.Token);
                await stream.WriteAsync(body, _stop.Token);
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

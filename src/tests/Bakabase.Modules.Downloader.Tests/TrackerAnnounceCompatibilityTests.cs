using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Bakabase.Modules.Downloader.Components;
using MonoTorrent;
using MonoTorrent.Trackers;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public sealed class TrackerAnnounceCompatibilityTests
{
    [DataTestMethod]
    [DataRow(-1, 6881)]
    [DataRow(0, 6881)]
    [DataRow(44444, 44444)]
    [Timeout(20000)]
    public async Task DirectUdpAnnounce_UsesValidPortWithoutOverridingIpOrChangingSwarm(int reportedPort, int expectedPort)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var network = new TorrentNetwork(new());
        var tracker = network.CreateFactories().CreateTracker(
            new Uri($"udp://127.0.0.1:{((IPEndPoint)server.Client.LocalEndPoint!).Port}/announce"))!;
        var hash = Enumerable.Range(1, 20).Select(i => (byte)i).ToArray();
        var request = new AnnounceRequest(new InfoHashes(InfoHash.FromMemory(hash), null))
            .WithPeerId(new byte[20]).WithReportedEndPointFunc(_ => (null, reportedPort));

        async Task<byte[]> Serve()
        {
            var connect = await server.ReceiveAsync(deadline.Token);
            var connectionResponse = new byte[16];
            connect.Buffer.AsSpan(12, 4).CopyTo(connectionResponse.AsSpan(4));
            BinaryPrimitives.WriteInt64BigEndian(connectionResponse.AsSpan(8), 123);
            await server.SendAsync(connectionResponse, connect.RemoteEndPoint, deadline.Token);
            var announce = await server.ReceiveAsync(deadline.Token);
            var response = new byte[20];
            BinaryPrimitives.WriteInt32BigEndian(response, 1);
            announce.Buffer.AsSpan(12, 4).CopyTo(response.AsSpan(4));
            BinaryPrimitives.WriteInt32BigEndian(response.AsSpan(8), 60);
            await server.SendAsync(response, announce.RemoteEndPoint, deadline.Token);
            return announce.Buffer;
        }
        async Task<AnnounceResponse> Announce() => await tracker.AnnounceAsync(request, deadline.Token);
        var serving = Serve();
        var announcing = Announce();
        await Task.WhenAll(serving, announcing).WaitAsync(deadline.Token);

        var datagram = await serving;
        Assert.AreEqual(1, BinaryPrimitives.ReadInt32BigEndian(datagram.AsSpan(8)));
        Assert.AreEqual(expectedPort, BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(96)));
        Assert.AreEqual(0u, BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(84)), "The tracker infers the source IP.");
        CollectionAssert.AreEqual(hash, datagram.AsSpan(16, 20).ToArray());
        Assert.AreEqual(TrackerState.Ok, (await announcing).State);
        Assert.AreEqual(reportedPort, request.GetReportedAddress("ipv4").port, "The engine's original request stays unchanged.");
    }
}

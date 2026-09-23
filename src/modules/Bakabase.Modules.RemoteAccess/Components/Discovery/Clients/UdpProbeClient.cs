using System.Net;
using System.Net.Sockets;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

/// <summary>
/// A server found on this network, with the address that actually reached it.
/// </summary>
/// <param name="BaseAddress">
/// Built from the sender's address rather than anything the server said. A server
/// with several interfaces cannot know which of its addresses this machine can
/// route to — but the datagram that arrived came back over one that works.
/// </param>
/// <param name="IsThisMachine">
/// True when the server answered from a loopback address, i.e. the all-in-one is
/// running right here. Worth showing, because "connect to my own computer" is a
/// normal thing to want and an odd thing to have to type an address for.
/// </param>
public sealed record DiscoveredServer(
    string ServerId,
    string ServerName,
    string BaseAddress,
    string AppVersion,
    int ProtocolVersion,
    bool IsThisMachine);

public interface IServerDiscovery
{
    Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// Finds servers by broadcasting on the probe port and listening for replies.
/// </summary>
/// <remarks>
/// <para>
/// The plain half of discovery, and the half that works where the other does not:
/// multicast is dropped on plenty of home networks and blocked outright on some
/// corporate ones, while a subnet broadcast usually survives. It is also the only
/// half that needs no platform API, which is what lets it be tested and run
/// anywhere.
/// </para>
/// <para>
/// Deliberately best effort throughout. A machine with several interfaces has
/// several broadcast addresses and some of them will refuse the send; a reply may
/// arrive from something that is not us at all. Neither is an error worth showing
/// the user, who asked "what is on my network" and wants the answer, not a report
/// about a virtual adapter.
/// </para>
/// </remarks>
public sealed class UdpProbeClient(ILogger<UdpProbeClient> logger) : IServerDiscovery
{
    public async Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) {EnableBroadcast = true};
        var request = System.Text.Encoding.UTF8.GetBytes(RemoteAccessProtocol.ProbeRequest);

        foreach (var address in BroadcastAddresses())
        {
            try
            {
                await socket.SendAsync(request, new IPEndPoint(address, RemoteAccessProtocol.ProbePort), ct);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                // A down interface, or one that does not do broadcast. The other
                // addresses are still worth trying.
                logger.LogDebug(e, "Could not probe {Address}", address);
            }
        }

        // Keyed by server id, so a machine that answers on several interfaces — or
        // answers the loopback and the subnet probe both — appears once.
        var found = new Dictionary<string, DiscoveredServer>(StringComparer.Ordinal);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            while (!deadline.IsCancellationRequested)
            {
                var reply = await socket.ReceiveAsync(deadline.Token);
                var server = Interpret(reply);

                if (server != null)
                {
                    // First answer wins, except that a loopback answer replaces a
                    // routed one for the same server: both reach it, and only one of
                    // them can say "this is your own computer".
                    if (!found.TryGetValue(server.ServerId, out var existing) ||
                        (server.IsThisMachine && !existing.IsThisMachine))
                    {
                        found[server.ServerId] = server;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The window closing is how this ends, not a failure.
        }
        catch (SocketException e)
        {
            logger.LogDebug(e, "Discovery stopped early");
        }

        return found.Values
            .OrderByDescending(s => s.IsThisMachine)
            .ThenBy(s => s.ServerName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Turns one datagram into a server, or null if it was not one of ours.
    /// </summary>
    public static DiscoveredServer? Interpret(UdpReceiveResult reply)
    {
        var descriptor = DiscoveryProtocol.TryParseProbeResponse(reply.Buffer);

        if (descriptor?.Port is not {} port)
        {
            return null;
        }

        var host = reply.RemoteEndPoint.Address;

        return new DiscoveredServer(
            descriptor.Id,
            descriptor.Name,
            $"http://{FormatHost(host)}:{port}",
            descriptor.AppVersion,
            descriptor.ProtocolVersion,
            IPAddress.IsLoopback(host));
    }

    /// <summary>IPv6 literals need brackets before they can go in a URL.</summary>
    private static string FormatHost(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

    /// <summary>
    /// Every address worth sending to: the global broadcast, each interface's own
    /// directed broadcast, and loopback.
    /// </summary>
    /// <remarks>
    /// The global 255.255.255.255 is not enough on its own — a host with several
    /// interfaces sends it out of exactly one, chosen by the routing table, which on
    /// a machine with a VPN or a virtual switch is regularly the wrong one. Loopback
    /// is listed because the most common thing a new client connects to is the
    /// all-in-one on the same computer, and a broadcast does not reach it.
    /// </remarks>
    public static IReadOnlyList<IPAddress> BroadcastAddresses()
    {
        var addresses = new List<IPAddress> {IPAddress.Broadcast, IPAddress.Loopback};

        try
        {
            foreach (var candidate in System.Net.NetworkInformation.NetworkInterface
                         .GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus ==
                                     System.Net.NetworkInformation.OperationalStatus.Up)
                         .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                         .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                var directed = DirectedBroadcast(candidate.Address, candidate.IPv4Mask);

                if (directed != null && !addresses.Contains(directed))
                {
                    addresses.Add(directed);
                }
            }
        }
        catch (Exception e) when (e is System.Net.NetworkInformation.NetworkInformationException
                                      or PlatformNotSupportedException)
        {
            // Enumerating interfaces is not available everywhere. The global
            // broadcast still covers the common single-interface case.
        }

        return addresses;
    }

    /// <summary>The all-ones host address of the subnet, e.g. 192.168.1.255.</summary>
    public static IPAddress? DirectedBroadcast(IPAddress address, IPAddress? mask)
    {
        if (mask == null || IPAddress.IsLoopback(address))
        {
            return null;
        }

        var host = address.GetAddressBytes();
        var bits = mask.GetAddressBytes();

        if (host.Length != 4 || bits.Length != 4)
        {
            return null;
        }

        for (var i = 0; i < 4; i++)
        {
            host[i] |= (byte) ~bits[i];
        }

        return new IPAddress(host);
    }
}

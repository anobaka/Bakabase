using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

/// <summary>
/// <see cref="MdnsHostResolver"/>'s questions on the real network: sent on each LAN interface
/// in turn, answers heard on three sockets.
/// </summary>
/// <remarks>
/// <para>
/// Each question is sent out of every LAN interface by name — never where the routing table
/// would send it: with a proxy's TUN adapter holding the default route, a multicast packet
/// left to routing goes into the proxy and dies there. Interfaces that are down, loopback,
/// tunnels, or hold nothing but a proxy's address (<see cref="ProxyFakeAddresses"/>) are
/// skipped.
/// </para>
/// <para>
/// The questions go from ports of their own, over IPv4 (224.0.0.251) and IPv6 (ff02::fb on the
/// same interfaces), so the operating systems' responders answer them straight back there
/// (RFC 6762 §6.7). A socket on 5353 in the IPv4 group hears the responders that only
/// multicast their answers, as Bakabase's own does; it is shared with the system's responder
/// by address reuse, as <see cref="MdnsBrowser"/>'s is, and simply left out where it cannot be
/// opened. Each answer carries the interface it came in on, which is the scope an IPv6
/// link-local address in it needs.
/// </para>
/// </remarks>
public sealed class MdnsSocketTransport : IMdnsQueryTransport
{
    private static readonly IPAddress GroupV4 = IPAddress.Parse("224.0.0.251");
    private static readonly IPAddress GroupV6 = IPAddress.Parse("ff02::fb");
    private const int MdnsPort = 5353;

    public async IAsyncEnumerable<MdnsDatagram> ExchangeAsync(IReadOnlyList<byte[]> queries,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var lan = LanInterfaces();

        if (lan.Count == 0)
        {
            yield break;
        }

        var heard = Channel.CreateUnbounded<MdnsDatagram>(new UnboundedChannelOptions {SingleReader = true});
        List<Socket> sockets = [];

        try
        {
            if (OpenListener(lan) is { } listener)
            {
                sockets.Add(listener);
            }

            if (Open(AddressFamily.InterNetwork) is { } v4)
            {
                sockets.Add(v4);

                foreach (var nic in lan)
                {
                    foreach (var address in nic.V4)
                    {
                        Send(v4, queries, new IPEndPoint(GroupV4, MdnsPort),
                            s => s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                                BitConverter.ToInt32(address.GetAddressBytes())));
                    }
                }
            }

            if (lan.Any(n => n.V6Index > 0) && Open(AddressFamily.InterNetworkV6) is { } v6)
            {
                sockets.Add(v6);

                foreach (var nic in lan.Where(n => n.V6Index > 0))
                {
                    Send(v6, queries, new IPEndPoint(new IPAddress(GroupV6.GetAddressBytes(), nic.V6Index), MdnsPort),
                        s => s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface,
                            nic.V6Index));
                }
            }

            foreach (var socket in sockets)
            {
                _ = Task.Run(() => ListenAsync(socket, heard.Writer, ct), CancellationToken.None);
            }

            await foreach (var datagram in heard.Reader.ReadAllAsync(ct))
            {
                yield return datagram;
            }
        }
        finally
        {
            // Ends every listening loop too.
            foreach (var socket in sockets)
            {
                socket.Dispose();
            }
        }
    }

    private static void Send(Socket socket, IReadOnlyList<byte[]> queries, IPEndPoint group, Action<Socket> through)
    {
        try
        {
            through(socket);

            foreach (var query in queries)
            {
                socket.SendTo(query, group);
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            // An interface that does not take multicast (a VPN adapter commonly does not): the
            // others are still worth asking on.
        }
    }

    /// <summary>A socket on a port of its own to ask from, or null where there is none to be had.</summary>
    private static Socket? Open(AddressFamily family)
    {
        Socket? socket = null;

        try
        {
            socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(family == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0));
        }
        catch (Exception e) when (e is SocketException or PlatformNotSupportedException)
        {
            socket?.Dispose();
            return null;
        }

        try
        {
            // mDNS's hop limit: a responder may ignore a packet that could have come from
            // further away than the link. Asking without it still beats not asking.
            socket.SetSocketOption(family == AddressFamily.InterNetwork ? SocketOptionLevel.IP : SocketOptionLevel.IPv6,
                SocketOptionName.MulticastTimeToLive, 255);
        }
        catch (SocketException)
        {
        }

        return socket;
    }

    /// <summary>A socket on 5353 in the IPv4 group of every LAN interface, or null where that cannot be had.</summary>
    private static Socket? OpenListener(IReadOnlyList<LanInterface> lan)
    {
        Socket? socket = null;

        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));

            var joined = false;

            foreach (var address in lan.SelectMany(n => n.V4))
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                        new MulticastOption(GroupV4, address));
                    joined = true;
                }
                catch (SocketException)
                {
                }
            }

            if (joined)
            {
                return socket;
            }
        }
        catch (Exception e) when (e is SocketException or PlatformNotSupportedException)
        {
        }

        socket?.Dispose();
        return null;
    }

    private static async Task ListenAsync(Socket socket, ChannelWriter<MdnsDatagram> heard, CancellationToken ct)
    {
        var buffer = new byte[9000];
        EndPoint anywhere = new IPEndPoint(
            socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
        var withInterface = true;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (withInterface)
                {
                    var result = await socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, anywhere, ct);

                    heard.TryWrite(new MdnsDatagram(buffer.AsSpan(0, result.ReceivedBytes).ToArray(),
                        result.PacketInformation.Interface));
                }
                else
                {
                    var received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, anywhere, ct);

                    heard.TryWrite(new MdnsDatagram(buffer.AsSpan(0, received.ReceivedBytes).ToArray(), 0));
                }
            }
            catch (Exception e) when (withInterface && e is PlatformNotSupportedException or SocketException
                                      {
                                          SocketErrorCode: SocketError.OperationNotSupported
                                          or SocketError.ProtocolOption or SocketError.InvalidArgument
                                      })
            {
                // No interface is reported here. An answer then lacks the scope an IPv6
                // link-local address needs, and nothing else.
                withInterface = false;
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
        }
    }

    /// <summary>The interfaces a question goes out of.</summary>
    /// <param name="V4">Its IPv4 addresses, each the way to send out of it.</param>
    /// <param name="V6Index">Its IPv6 interface index; 0 without IPv6.</param>
    private sealed record LanInterface(IReadOnlyList<IPAddress> V4, int V6Index);

    private static IReadOnlyList<LanInterface> LanInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast &&
                            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n =>
                {
                    var properties = n.GetIPProperties();
                    var v4 = properties.UnicastAddresses
                        .Select(a => a.Address)
                        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) &&
                                    !ProxyFakeAddresses.Contains(a))
                        .ToList();
                    var v6 = n.Supports(NetworkInterfaceComponent.IPv6) &&
                             properties.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        ? properties.GetIPv6Properties()?.Index ?? 0
                        : 0;

                    return new LanInterface(v4, v6);
                })
                // A LAN interface has an IPv4 address to send from; one with nothing but a
                // proxy's is the proxy's.
                .Where(n => n.V4.Count > 0)
                .ToList();
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException)
        {
            return [];
        }
    }
}

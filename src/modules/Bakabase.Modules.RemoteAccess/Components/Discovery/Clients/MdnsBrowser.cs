using System.Net;
using System.Net.Sockets;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;

/// <summary>
/// Finds servers by asking the network's mDNS responders.
/// </summary>
/// <remarks>
/// <para>
/// The other half of discovery. The UDP probe covers most home networks and needs no
/// platform support, but a subnet broadcast is dropped by some access points and by
/// most enterprise wireless, while mDNS is the one thing those networks are usually
/// configured to carry — it is how printers and speakers are found.
/// </para>
/// <para>
/// Best effort all the way through, like its sibling: an interface that refuses to
/// join the group, a packet from something that is not us, a port already held by
/// something else. None of those is an error the user asked about.
/// </para>
/// </remarks>
public sealed class MdnsBrowser(ILogger<MdnsBrowser> logger) : IServerDiscovery
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    public async Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var socket = Open();

        if (socket == null)
        {
            return [];
        }

        try
        {
            await socket.SendToAsync(
                MdnsMessage.BuildQuery(RemoteAccessProtocol.MdnsServiceType, MdnsMessage.TypePtr),
                new IPEndPoint(MulticastAddress, MdnsPort), ct);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            logger.LogDebug(e, "mDNS query could not be sent");

            return [];
        }

        var collector = new InstanceCollector();
        var buffer = new byte[9000];

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            while (!deadline.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer, SocketFlags.None, deadline.Token);

                if (MdnsMessage.TryParseResponse(buffer.AsSpan(0, received), out var records))
                {
                    collector.Add(records);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // How this ends.
        }
        catch (SocketException e)
        {
            logger.LogDebug(e, "mDNS browse stopped early");
        }

        return collector.Build();
    }

    /// <summary>
    /// A socket on the mDNS port with the group joined, or null when that is not
    /// possible here.
    /// </summary>
    /// <remarks>
    /// Bound to the well-known port with address reuse rather than to an ephemeral
    /// one, because the responders on this network multicast their answers instead
    /// of replying to whoever asked — an ephemeral port would never see them. Reuse
    /// is also what lets this run beside the operating system's own responder, and
    /// beside an all-in-one Bakabase on the same machine.
    /// </remarks>
    private Socket? Open()
    {
        Socket? socket = null;

        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));

            var joined = false;

            foreach (var address in LocalAddresses())
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                        new MulticastOption(MulticastAddress, address));
                    joined = true;
                }
                catch (SocketException)
                {
                    // A VPN or virtual adapter that does not do multicast. The others
                    // are still worth joining.
                }
            }

            if (!joined)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                    new MulticastOption(MulticastAddress));
            }

            return socket;
        }
        catch (Exception e) when (e is SocketException or PlatformNotSupportedException)
        {
            logger.LogDebug(e, "mDNS browsing is not available here; the UDP probe still is");
            socket?.Dispose();

            return null;
        }
    }

    private static IReadOnlyList<IPAddress> LocalAddresses()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToList();
        }
        catch (Exception e) when (e is System.Net.NetworkInformation.NetworkInformationException
                                     or PlatformNotSupportedException)
        {
            return [];
        }
    }

    /// <summary>
    /// Assembles the records of a DNS-SD browse into servers.
    /// </summary>
    /// <remarks>
    /// The four record types arrive separately and often in separate packets: PTR
    /// names an instance, SRV gives its port and host, TXT its facts, A the host's
    /// address. Only an instance with all of them is reachable, so the rest are
    /// dropped rather than shown as something that cannot be connected to.
    /// </remarks>
    public sealed class InstanceCollector
    {
        private readonly Dictionary<string, RemoteAccessServerDescriptor> _facts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string Host, ushort Port)> _hosts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IPAddress> _addresses = new(StringComparer.OrdinalIgnoreCase);

        public void Add(IEnumerable<MdnsMessage.ParsedRecord> records)
        {
            foreach (var record in records)
            {
                // TTL zero is a goodbye: the server is shutting down and saying so.
                // Listing it would offer an address that is already gone.
                if (record.Ttl == 0)
                {
                    Forget(record);
                    continue;
                }

                switch (record.Type)
                {
                    case MdnsMessage.TypeSrv when record.Target != null && record.Port > 0:
                        _hosts[Key(record.Name)] = (record.Target, record.Port);
                        break;
                    case MdnsMessage.TypeTxt when record.Txt.Count > 0:
                    {
                        var descriptor = DiscoveryProtocol.TryParseTxtEntries(record.Txt);

                        if (descriptor != null)
                        {
                            _facts[Key(record.Name)] = descriptor;
                        }

                        break;
                    }
                    case MdnsMessage.TypeA when record.Address != null:
                        Remember(Key(record.Name), record.Address);
                        break;
                }
            }
        }

        public IReadOnlyList<DiscoveredServer> Build()
        {
            var found = new Dictionary<string, DiscoveredServer>(StringComparer.Ordinal);

            foreach (var (instance, descriptor) in _facts)
            {
                if (!_hosts.TryGetValue(instance, out var host) ||
                    !_addresses.TryGetValue(Key(host.Host), out var address))
                {
                    continue;
                }

                // The port comes from SRV rather than from the TXT facts. They agree
                // today, but SRV is where DNS-SD says a port lives, and a browser that
                // ignored it would be reading the advertisement rather than the record.
                var server = new DiscoveredServer(
                    descriptor.Id,
                    descriptor.Name,
                    $"http://{address}:{host.Port}",
                    descriptor.AppVersion,
                    descriptor.ProtocolVersion,
                    IPAddress.IsLoopback(address),
                    descriptor.Kind,
                    descriptor.Platform);

                if (!found.TryGetValue(server.ServerId, out var existing) ||
                    (server.IsThisMachine && !existing.IsThisMachine))
                {
                    found[server.ServerId] = server;
                }
            }

            return found.Values.ToList();
        }

        /// <summary>
        /// Keeps one address per host, preferring one that means something here.
        /// </summary>
        /// <remarks>
        /// A multi-homed server publishes an A record per interface, and nothing in the
        /// packet says which of them this machine can route to — that is what the probe
        /// channel knows, and where both find the same server its answer is kept. What
        /// this can rule out is a loopback address, which read from another machine
        /// points at the reader. It is still taken when it is all that was offered:
        /// then the server really is on this computer.
        /// </remarks>
        private void Remember(string host, IPAddress address)
        {
            if (!_addresses.TryGetValue(host, out var existing))
            {
                _addresses[host] = address;
                return;
            }

            if (IPAddress.IsLoopback(existing) && !IPAddress.IsLoopback(address))
            {
                _addresses[host] = address;
            }
        }

        private void Forget(MdnsMessage.ParsedRecord record)
        {
            var key = Key(record.Name);

            switch (record.Type)
            {
                case MdnsMessage.TypeSrv:
                    _hosts.Remove(key);
                    break;
                case MdnsMessage.TypeTxt:
                    _facts.Remove(key);
                    break;
                case MdnsMessage.TypeA:
                    _addresses.Remove(key);
                    break;
            }
        }

        /// <summary>DNS names differ only in case and a trailing dot; keys must not.</summary>
        private static string Key(string name) => name.TrimEnd('.');
    }
}

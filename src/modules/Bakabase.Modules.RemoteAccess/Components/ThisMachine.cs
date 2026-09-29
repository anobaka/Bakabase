using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// Whether an address reaches this machine: a loopback address, or one that one of its own
/// network interfaces holds.
/// </summary>
/// <remarks>
/// What tells this device apart from a copy of it. An install is its identity, and copying its
/// data directory to another computer carries that identity along: an address that answers with
/// this install's own id is this device only when the address is this machine's. Anywhere else it
/// is another installation that has the same identity — or this device reached through another
/// door (a reverse proxy, a port forward), which the address alone cannot tell.
/// </remarks>
public static class ThisMachine
{
    /// <summary>Whether <paramref name="address"/> is loopback, or held by one of this machine's interfaces.</summary>
    /// <remarks>See <see cref="ThisMachineAddresses.Holds"/>. Judging several, take a <see cref="Snapshot"/> once.</remarks>
    public static bool Holds(IPAddress address) => Snapshot().Holds(address);

    /// <summary>
    /// The addresses this machine's interfaces hold now, to judge several addresses against.
    /// </summary>
    /// <remarks>
    /// Reading them enumerates every network interface — milliseconds each time on a machine
    /// with many virtual or VPN adapters — which a discovery window reading one reply after
    /// another must not spend on each of them.
    /// </remarks>
    public static ThisMachineAddresses Snapshot() => new(OwnAddresses());

    /// <summary>
    /// Whether the host of <paramref name="address"/> reaches this machine: an address it holds,
    /// or a name that resolves only to such addresses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name that no longer resolves is not known to be this machine. Nor is one that also
    /// resolves elsewhere: another computer can advertise an address this machine holds too —
    /// a VPN or proxy adapter's (198.18.0.1), a virtual machine host's (192.168.56.1) — beside its
    /// own, and which of them answered is not known here.
    /// </para>
    /// <para>
    /// A name is resolved as a connection to it resolves it (<see cref="LanHostResolver"/>), so
    /// the answer is about the machine that connection reached.
    /// </para>
    /// </remarks>
    public static Task<bool> ReachedByAsync(Uri address, CancellationToken ct = default) =>
        ReachedByAsync(address, LanHostResolver.Default.ResolveAsync, ct);

    /// <inheritdoc cref="ReachedByAsync(Uri, CancellationToken)"/>
    /// <param name="resolve">What a name resolves to; injected for tests.</param>
    public static async Task<bool> ReachedByAsync(Uri address,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve, CancellationToken ct = default)
    {
        var host = address.IdnHost.Trim('[', ']');

        if (IPAddress.TryParse(host, out var literal))
        {
            return Holds(literal);
        }

        IPAddress[] resolved;
        try
        {
            resolved = await resolve(host, ct);
        }
        catch (Exception e) when (e is SocketException or ArgumentException)
        {
            return false;
        }

        var own = Snapshot();
        return resolved.Length > 0 && resolved.All(own.Holds);
    }

    private static IPAddress[] OwnAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .ToArray();
        }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException)
        {
            return [];
        }
    }
}

/// <summary>This machine's addresses at one moment: see <see cref="ThisMachine.Snapshot"/>.</summary>
public sealed class ThisMachineAddresses
{
    private readonly HashSet<IPAddress> _held;

    /// <param name="held">What this machine's interfaces hold; given by tests, read by <see cref="ThisMachine.Snapshot"/>.</param>
    public ThisMachineAddresses(IEnumerable<IPAddress> held)
    {
        _held = held.Select(Plain).ToHashSet();
    }

    /// <summary>Whether <paramref name="address"/> is loopback, or held by one of this machine's interfaces.</summary>
    /// <remarks>
    /// Compared without an IPv6 scope: a link-local address names the same interface whichever
    /// zone it was written with. The unspecified addresses count too, since connecting to one
    /// reaches this machine.
    /// </remarks>
    public bool Holds(IPAddress address)
    {
        address = Plain(address);

        return IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
               address.Equals(IPAddress.IPv6Any) || _held.Contains(address);
    }

    /// <summary>The address without its IPv6 scope, and an IPv4 address mapped into IPv6 as itself.</summary>
    private static IPAddress Plain(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : new IPAddress(address.GetAddressBytes());
}

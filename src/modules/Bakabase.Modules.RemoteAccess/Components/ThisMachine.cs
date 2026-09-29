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
/// is another installation that has the same identity.
/// </remarks>
public static class ThisMachine
{
    /// <summary>Whether <paramref name="address"/> is loopback, or held by one of this machine's interfaces.</summary>
    /// <remarks>
    /// Compared without an IPv6 scope: a link-local address names the same interface whichever
    /// zone it was written with. The unspecified addresses count too, since connecting to one
    /// reaches this machine.
    /// </remarks>
    public static bool Holds(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return OwnAddresses().Any(own => own.GetAddressBytes().AsSpan().SequenceEqual(bytes));
    }

    /// <summary>
    /// Whether the host of <paramref name="address"/> reaches this machine: an address it holds,
    /// or a name that resolves only to such addresses.
    /// </summary>
    /// <remarks>
    /// A name that no longer resolves is not known to be this machine. Nor is one that also
    /// resolves elsewhere: another computer can advertise an address this machine holds too —
    /// a VPN or proxy adapter's (198.18.0.1), a virtual machine host's (192.168.56.1) — beside its
    /// own, and which of them answered is not known here.
    /// </remarks>
    public static Task<bool> ReachedByAsync(Uri address, CancellationToken ct = default) =>
        ReachedByAsync(address, Dns.GetHostAddressesAsync, ct);

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

        return resolved.Length > 0 && resolved.All(Holds);
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

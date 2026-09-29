using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// Which of this machine's addresses another device should type.
/// </summary>
/// <remarks>
/// <para>
/// A computer with a VPN, a proxy's TUN adapter and a hypervisor lists a dozen addresses, and
/// several of them are private (RFC 1918) addresses no other device can reach: a VM host-only
/// network (Parallels' <c>vnic0</c>, VirtualBox's host-only adapter), an overlay (ZeroTier).
/// Only this machine can tell them from the network a router serves: that one has a default
/// gateway. The name and description catch what the gateway cannot, and the address ranges
/// that say it outright come first.
/// </para>
/// <para>
/// The web keeps a heuristic of its own (<c>devices/addresses.ts</c>) for a server that
/// sends no kind; the patterns here are a superset of its interface names.
/// </para>
/// </remarks>
public static class RemoteAccessAddressClassifier
{
    /// <summary>Overlay networks and VPN clients: reachable from the devices on them.</summary>
    private static readonly Regex VpnInterface = new(
        @"zerotier|^zt[0-9a-z]|^feth\d|wireguard|^wg\d|tailscale|openvpn|tap-windows|nordlynx|^ppp\d|^ipsec",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Bridges, container networks, hypervisors and proxy TUN adapters.</summary>
    private static readonly Regex VirtualInterface = new(
        @"bridge|docker|^br-|veth|vethernet|vmnet|virbr|^vnic|^utun|^tun|^tap|hyper-v|virtualbox|vmware|" +
        @"parallels|host-only|wintun|clash",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>What kind of network <paramref name="address"/> is on.</summary>
    /// <param name="hasGateway">
    /// Whether its interface has a default gateway; null where the platform cannot say, which
    /// then decides nothing.
    /// </param>
    public static RemoteAccessAddressKind Classify(IPAddress address, string interfaceName,
        string? description, bool? hasGateway)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return RemoteAccessAddressKind.Unknown;
        }

        var bytes = address.GetAddressBytes();
        var (a, b) = (bytes[0], bytes[1]);

        if (a == 169 && b == 254) return RemoteAccessAddressKind.LinkLocal;
        // A proxy's benchmark range (Clash's TUN) is never another device.
        if (a == 198 && b is 18 or 19) return RemoteAccessAddressKind.Virtual;
        // Carrier-grade NAT space, which Tailscale uses whatever its adapter is called.
        if (a == 100 && b is >= 64 and <= 127) return RemoteAccessAddressKind.Vpn;

        if (Matches(VpnInterface, interfaceName, description)) return RemoteAccessAddressKind.Vpn;
        if (Matches(VirtualInterface, interfaceName, description)) return RemoteAccessAddressKind.Virtual;

        var isPrivate = a == 10 || (a == 172 && b is >= 16 and <= 31) || (a == 192 && b == 168);
        if (!isPrivate) return RemoteAccessAddressKind.Unknown;

        // A private network with no way out: a host-only or overlay adapter, most likely.
        return hasGateway == false ? RemoteAccessAddressKind.Unknown : RemoteAccessAddressKind.Lan;
    }

    /// <summary>
    /// The address to recommend, as an index into <paramref name="candidates"/> (in the order
    /// they are listed): the first LAN address whose interface has a default gateway, else the
    /// first LAN address; none when there is no LAN address.
    /// </summary>
    public static int? Recommend(IReadOnlyList<(RemoteAccessAddressKind Kind, bool? HasGateway)> candidates)
    {
        int? fallback = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Kind != RemoteAccessAddressKind.Lan) continue;
            if (candidates[i].HasGateway == true) return i;
            fallback ??= i;
        }

        return fallback;
    }

    private static bool Matches(Regex pattern, string interfaceName, string? description)
    {
        try
        {
            return pattern.IsMatch(interfaceName.Trim()) ||
                   (!string.IsNullOrWhiteSpace(description) && pattern.IsMatch(description.Trim()));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

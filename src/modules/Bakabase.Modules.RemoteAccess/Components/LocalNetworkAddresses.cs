using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>One IPv4 address of an interface, with what the interface says about itself.</summary>
/// <param name="Description">The adapter's description (on Windows its product name), or empty.</param>
/// <param name="HasGateway">
/// Whether the interface has an IPv4 default gateway; null where the platform cannot say.
/// </param>
internal sealed record LocalNetworkAddress(IPAddress Address, string InterfaceName, string Description,
    bool? HasGateway);

/// <summary>
/// IPv4 addresses of interfaces that are up and are not loopback or tunnels —
/// the ones another device on the same network could actually route to. Shared
/// by the reachable-address list and the discovery beacon so both tell the same
/// story.
/// </summary>
internal static class LocalNetworkAddresses
{
    public static IEnumerable<(IPAddress Address, string InterfaceName)> EnumerateIPv4(ILogger? logger = null) =>
        Enumerate(logger, false).Select(a => (a.Address, a.InterfaceName));

    /// <summary>
    /// <see cref="EnumerateIPv4"/> with what the interfaces say about themselves — read only for
    /// the addresses a person picks from, not for every beacon answer.
    /// </summary>
    public static IEnumerable<LocalNetworkAddress> EnumerateIPv4Details(ILogger? logger = null) =>
        Enumerate(logger, true);

    private static IEnumerable<LocalNetworkAddress> Enumerate(ILogger? logger, bool details)
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Could not enumerate network interfaces; no reachable addresses will be shown");
            yield break;
        }

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var properties = ni.GetIPProperties();
            var hasGateway = details ? HasIPv4Gateway(properties) : null;
            var description = details ? DescriptionOf(ni) : "";

            foreach (var info in properties.UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(info.Address))
                {
                    continue;
                }

                yield return new LocalNetworkAddress(info.Address, ni.Name, description, hasGateway);
            }
        }
    }

    private static string DescriptionOf(NetworkInterface ni)
    {
        try
        {
            return ni.Description ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Whether the interface routes anywhere beyond its own subnet — the mark of the network a
    /// router serves, which host-only and overlay adapters lack.
    /// </summary>
    private static bool? HasIPv4Gateway(IPInterfaceProperties properties)
    {
        try
        {
            return properties.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
        }
        catch (Exception)
        {
            // Not every platform can read the routing table.
            return null;
        }
    }
}

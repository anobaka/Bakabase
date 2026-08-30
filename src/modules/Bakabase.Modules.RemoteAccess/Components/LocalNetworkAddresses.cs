using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// IPv4 addresses of interfaces that are up and are not loopback or tunnels —
/// the ones another device on the same network could actually route to. Shared
/// by the reachable-address list and the discovery beacon so both tell the same
/// story.
/// </summary>
internal static class LocalNetworkAddresses
{
    public static IEnumerable<(IPAddress Address, string InterfaceName)> EnumerateIPv4(ILogger? logger = null)
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

            foreach (var info in ni.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(info.Address))
                {
                    continue;
                }

                yield return (info.Address, ni.Name);
            }
        }
    }
}

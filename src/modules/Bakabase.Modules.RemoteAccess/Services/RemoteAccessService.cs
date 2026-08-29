using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Services;

public class RemoteAccessService(
    IBOptionsManager<RemoteAccessOptions> optionsManager,
    RemoteAccessDefaults defaults,
    IListeningAddressProvider listeningAddressProvider,
    ILogger<RemoteAccessService> logger) : IRemoteAccessService
{
    public RemoteAccessMode GetEffectiveMode() => optionsManager.Value.Mode ?? defaults.Mode;

    public async Task SetModeAsync(RemoteAccessMode? mode)
    {
        await optionsManager.SaveAsync(o => o.Mode = mode);
        logger.LogInformation("Remote access mode set to {Mode} (effective: {Effective})", mode, GetEffectiveMode());
    }

    public IReadOnlyList<RemoteAccessAddress> GetReachableAddresses()
    {
        var ports = GetListeningPorts();
        if (ports.Count == 0)
        {
            return [];
        }

        var addresses = new List<RemoteAccessAddress>();

        foreach (var (ip, interfaceName) in GetLocalNetworkAddresses())
        {
            foreach (var port in ports)
            {
                addresses.Add(new RemoteAccessAddress($"http://{ip}:{port}", interfaceName));
            }
        }

        return addresses;
    }

    private IReadOnlyList<int> GetListeningPorts()
    {
        var ports = new List<int>();

        foreach (var address in listeningAddressProvider.GetListeningAddresses())
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Port > 0)
            {
                if (!ports.Contains(uri.Port))
                {
                    ports.Add(uri.Port);
                }
            }
        }

        return ports;
    }

    /// <summary>
    /// IPv4 addresses of interfaces that are up and are not loopback or tunnels —
    /// the ones another device on the same network could actually route to.
    /// </summary>
    private IEnumerable<(string Ip, string InterfaceName)> GetLocalNetworkAddresses()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not enumerate network interfaces; no reachable addresses will be shown");
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

                yield return (info.Address.ToString(), ni.Name);
            }
        }
    }
}

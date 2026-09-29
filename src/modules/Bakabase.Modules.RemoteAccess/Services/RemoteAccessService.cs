using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Services;

public class RemoteAccessService(
    IBOptionsManager<RemoteAccessOptions> optionsManager,
    RemoteAccessDefaults defaults,
    RemoteAccessHostInfo hostInfo,
    IListeningAddressProvider listeningAddressProvider,
    ILogger<RemoteAccessService> logger,
    IServerSelfDescription? self = null) : IRemoteAccessService
{
    private readonly SemaphoreSlim _serverIdLock = new(1, 1);

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

        var hosts = LocalNetworkAddresses.EnumerateIPv4Details(logger)
            .Select(a => (a.Address, a.InterfaceName,
                Kind: RemoteAccessAddressClassifier.Classify(a.Address, a.InterfaceName, a.Description,
                    a.HasGateway), a.HasGateway))
            .ToList();
        var recommended = RemoteAccessAddressClassifier.Recommend(hosts.Select(h => (h.Kind, h.HasGateway)).ToList());
        var addresses = new List<RemoteAccessAddress>();

        // Recommended first, then by what can reach it: every host and every port, in the
        // order the page shows them and a device reading this one back tries them.
        foreach (var i in RemoteAccessAddressClassifier.Order(hosts.Select(h => h.Kind).ToList(), recommended))
        {
            var (ip, interfaceName, kind, _) = hosts[i];
            foreach (var port in ports)
            {
                addresses.Add(new RemoteAccessAddress($"http://{ip}:{port}", interfaceName, kind, i == recommended));
            }
        }

        return addresses;
    }

    public async Task<string> GetOrCreateServerIdAsync()
    {
        var existing = optionsManager.Value.ServerId;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        await _serverIdLock.WaitAsync();
        try
        {
            existing = optionsManager.Value.ServerId;
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            var id = Guid.NewGuid().ToString("N");
            await optionsManager.SaveAsync(o => o.ServerId = id);
            logger.LogInformation("Generated server id {ServerId}", id);
            return id;
        }
        finally
        {
            _serverIdLock.Release();
        }
    }

    public async Task<string> RegenerateServerIdAsync()
    {
        await _serverIdLock.WaitAsync();
        try
        {
            var previous = optionsManager.Value.ServerId;
            var id = Guid.NewGuid().ToString("N");
            await optionsManager.SaveAsync(o => o.ServerId = id);
            logger.LogInformation("Replaced server id {Previous} with {ServerId}", previous, id);
            return id;
        }
        finally
        {
            _serverIdLock.Release();
        }
    }

    public bool GetAllowLiveTranscode() => optionsManager.Value.AllowLiveTranscode;

    public async Task SetAllowLiveTranscodeAsync(bool allow)
    {
        await optionsManager.SaveAsync(o => o.AllowLiveTranscode = allow);
        logger.LogInformation("Remote live transcode set to {Allow}", allow);
    }

    public bool GetRequirePairing() => optionsManager.Value.RequirePairing;

    public async Task SetRequirePairingAsync(bool require)
    {
        await optionsManager.SaveAsync(o => o.RequirePairing = require);
        logger.LogInformation("Remote access pairing requirement set to {Require}", require);
    }

    public async Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync()
    {
        var id = await GetOrCreateServerIdAsync();
        var ports = GetListeningPorts();

        return new RemoteAccessServerDescriptor(
            id,
            GetServerName(),
            ports.Count > 0 ? ports[0] : null,
            hostInfo.AppVersion,
            RemoteAccessProtocol.CurrentVersion,
            ServerSelfDescriptionWords.Known(self?.Kind),
            ServerSelfDescriptionWords.Known(self?.Platform));
    }

    private static string GetServerName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return "Bakabase";
        }
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
}

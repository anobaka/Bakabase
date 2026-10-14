using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Remoting.Components.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.RemoteAccess.Components.Management;

public static class ManagedServerServiceCollectionExtensions
{
    public static IServiceCollection AddManagedServerConnections(this IServiceCollection services,
        ManagedServerOptions? options = null)
    {
        services.TryAddSingleton(sp => options ?? new ManagedServerOptions
        {
            LocalPorts = () => sp.GetService<IListeningAddressProvider>()?.GetListeningAddresses()
                .Select(address => Uri.TryCreate(address.Replace("0.0.0.0", "localhost"), UriKind.Absolute, out var uri)
                    ? uri.Port : 0).Where(port => port > 0) ?? []
        });
        services.TryAddSingleton(sp => new ManagedServerStore(new ManagedServerDirectory(() =>
            sp.GetRequiredService<ManagedServerOptions>().ManagedDirectory ?? Path.Combine(
                sp.GetRequiredService<IRemoteAccessDataDirectory>().Path, ManagedServerDirectory.DirectoryName))));
        services.TryAddSingleton<UdpProbeClient>();
        services.TryAddSingleton<MdnsBrowser>();
        services.TryAddSingleton<IServerDiscovery, ServerDiscovery>();
        services.TryAddSingleton<ManagedServerManager>();
        services.TryAddSingleton<IManagedServerService>(sp => sp.GetRequiredService<ManagedServerManager>());
        services.AddHostedService(sp => (ManagedServerManager) sp.GetRequiredService<IManagedServerService>());
        return services;
    }
}

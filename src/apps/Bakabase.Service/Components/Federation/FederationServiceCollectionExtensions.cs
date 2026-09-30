using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Service.Components.Federation;

public static class FederationServiceCollectionExtensions
{
    public static IServiceCollection AddFederatedLibrary(this IServiceCollection services)
    {
        services.AddSingleton<IFederationDataDirectory, FederationDataDirectory>();
        services.AddSingleton<INodeIdSource, FederationNodeIdSource>();
        services.AddFederationPeers();
        services.AddSingleton<IFederationQueryAccess, FederationQueryAccess>();
        services.AddSingleton<ILocalLibraryReader, ServiceLocalLibraryReader>();
        services.AddSingleton<FederationQueryLimits>();
        services.AddSingleton<LocalSearchSnapshotService>();
        services.AddSingleton<IPeerSearchTargetResolver, PeerSearchTargetResolver>();
        services.AddSingleton<FederatedQueryCoordinator>();
        services.AddSingleton<AssetLeaseStore>();
        services.AddSingleton<FederationMediaSessions>();
        services.AddSingleton<FederationBrowsingControl>();
        services.AddScoped<FederationResourceService>();
        services.AddScoped<FederationMediaService>();
        services.AddScoped<FederationDirectoryService>();
        services.TryAddSingleton<IFederationDirectoryOpener, FederationDirectoryOpener>();
        services.TryAddSingleton<LocalPlayerResolver>();
        services.TryAddSingleton<IFederationPlayerProxyEnvironment, FederationPlayerProxyEnvironment>();
        services.TryAddSingleton<FederationPlayerPolicy>();
        services.TryAddSingleton<UdpProbeClient>();
        services.TryAddSingleton<MdnsBrowser>();
        services.TryAddSingleton<IServerDiscovery, ServerDiscovery>();
        services.AddSingleton<INodePeerDiscovery, FederationNodeDiscovery>();
        services.AddSingleton<FederationPairingFlow>();
        services.AddHostedService(sp => sp.GetRequiredService<FederationPairingFlow>());
        // Data sync over federation (§7): what /info and the handshake say about it, definitions access and the
        // feed reader for the data sync runtime. Registered after anything data sync itself registers, so these are
        // the ones resolved. The reader is a singleton: its per-peer lock covers every caller (§7.6).
        services.AddSingleton<INodeInfoContributor, DataSyncNodeInfoContributor>();
        services.AddSingleton<FederationDataSyncGrants>();
        services.AddSingleton<IDataSyncGrantService>(sp => sp.GetRequiredService<FederationDataSyncGrants>());
        services.AddSingleton<FederationDataSyncPeerClient>();
        services.AddSingleton<IDataSyncPeerClient>(sp => sp.GetRequiredService<FederationDataSyncPeerClient>());
        services.AddHostedService<FederationInviteAnnouncer>();
        services.AddHostedService<FederationRemoteModeMonitor>();
        services.Configure<MvcOptions>(options => options.Filters.Add<FederationLocalAccessFilter>());
        services.Configure<MvcOptions>(options => options.Filters.Add<FederationExceptionFilter>());
        return services;
    }
}

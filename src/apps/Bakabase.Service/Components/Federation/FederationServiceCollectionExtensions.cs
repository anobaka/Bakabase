using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.Service.Components.Federation.Processing;
using System.IO;

namespace Bakabase.Service.Components.Federation;

public static class FederationServiceCollectionExtensions
{
    public static IServiceCollection AddMultiDeviceServices(this IServiceCollection services)
    {
        services.AddSingleton<IFederationDataDirectory, FederationDataDirectory>();
        services.AddSingleton<INodeIdSource, FederationNodeIdSource>();
        services.AddFederationPeers();
        services.AddScoped<ManagedLibraryMappingTreeService>();
        services.AddSingleton<ManagedLibraryMappingTreeTokens>();
        services.AddScoped<ResourceProfileSourceReader>();
        services.AddScoped<ManagedLibraryService>();
        services.AddSingleton<MetadataSourceJournalGate>();
        services.AddScoped<MetadataSourceJournal>();
        services.AddScoped<MetadataReplicaProjection>();
        services.AddScoped<MetadataSourceFeed>();
        services.AddSingleton<MetadataReplicaRegistry>();
        services.AddSingleton(sp => new MetadataReplicaStore(Path.Combine(
            sp.GetRequiredService<IFederationDataDirectory>().Path, "metadata-replicas")));
        services.AddSingleton<MetadataReplicaQuery>();
        services.AddSingleton<MetadataReplicaCoordinator>();
        services.AddSingleton<MetadataSyncActivityStore>();
        services.AddScoped<Bakabase.Service.Components.DataSync.SyncActivityService>();
        services.AddScoped<ManagedMetadataReader>();
        services.AddScoped<ResourceProcessingJobStore>();
        services.AddSingleton<ResourceProcessingArtifacts>();
        services.AddScoped<ResourceProcessingOwner>();
        services.AddScoped<ResourceProcessingWorker>();
        services.AddSingleton<ResourceProcessingCoordinator>();
        services.AddScoped<SourceResourceProfileService>();
        services.AddScoped<ISourceResourceProfilePlaybackService>(sp => sp.GetRequiredService<SourceResourceProfileService>());
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
        services.AddHostedService<FederationRemoteModeMonitor>();
        services.Configure<MvcOptions>(options => options.Filters.Add<FederationLocalAccessFilter>());
        services.Configure<MvcOptions>(options => options.Filters.Add<FederationExceptionFilter>());
        services.Configure<MvcOptions>(options => options.Filters.Add<ManagedLibraryEpochFilter>());
        return services;
    }
}

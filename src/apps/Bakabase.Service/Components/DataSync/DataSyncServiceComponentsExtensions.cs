using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Service.Components.DataSync;

public static class DataSyncServiceComponentsExtensions
{
    /// <summary>
    /// What data sync takes from the Service host (spec §2.11): this install's device identity over the federation
    /// node, its host kind, the addresses other devices reach it at, whether a federation session to a peer is online
    /// (§8.2), and the headless sharing announcer (§7.8). <c>AddDataSync()</c> registers none of them, so the test
    /// kit's fakes stay in place. Called by <see cref="BakabaseStartup"/> after <c>AddFederatedLibrary()</c>.
    /// </summary>
    /// <remarks>
    /// The facade (<see cref="IDataSyncService"/>) and the grant events are the runtime's, registered earlier through
    /// <c>AddInsideWorldBusinesses</c> (<c>AddDataSync()</c> → <c>AddDataSyncRuntime()</c>); the feed source, the gate
    /// and the stores are the persistence layer's, and the peer client and grant service federation's
    /// (<c>AddFederatedLibrary()</c>).
    /// </remarks>
    public static IServiceCollection AddDataSyncServiceComponents(this IServiceCollection services)
    {
        services.TryAddSingleton<IDataSyncDeviceIdentity, FederationDataSyncDeviceIdentity>();
        services.TryAddSingleton<IDataSyncHostKind, ServiceDataSyncHostKind>();
        services.TryAddSingleton<IDataSyncHostAddresses, ServiceDataSyncHostAddresses>();
        services.TryAddSingleton<IDataSyncPeerSessions, FederationDataSyncPeerSessions>();
        services.AddHostedService<DataSyncSharingAnnouncer>();
        return services;
    }
}

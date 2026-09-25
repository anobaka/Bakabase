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
    /// Until the persistence package registers the runtime (<c>AddDataSync()</c> → <c>AddDataSyncRuntime()</c>, made
    /// earlier through <c>AddInsideWorldBusinesses</c>), two placeholders fill the gaps, each registered with
    /// <c>TryAdd</c> so the runtime's own registrations win: grant events nobody listens to, and a facade that answers
    /// that data sync is not available yet.
    /// </remarks>
    public static IServiceCollection AddDataSyncServiceComponents(this IServiceCollection services)
    {
        services.TryAddSingleton<IDataSyncDeviceIdentity, FederationDataSyncDeviceIdentity>();
        services.TryAddSingleton<IDataSyncHostKind, ServiceDataSyncHostKind>();
        services.TryAddSingleton<IDataSyncHostAddresses, ServiceDataSyncHostAddresses>();
        services.TryAddSingleton<IDataSyncPeerSessions, FederationDataSyncPeerSessions>();
        services.AddHostedService<DataSyncSharingAnnouncer>();

        services.TryAddSingleton<IDataSyncGrantEvents, NoOpDataSyncGrantEvents>();
        services.TryAddScoped<IDataSyncService, UnavailableDataSyncService>();
        return services;
    }
}

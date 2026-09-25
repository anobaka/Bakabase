using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Service.Components.DataSync;

public static class DataSyncServiceComponentsExtensions
{
    /// <summary>
    /// What data sync takes from the Service host (spec §2.11): this install's device identity over the federation
    /// node, its host kind and the headless sharing announcer. <c>AddDataSync()</c> registers none of them, so the test
    /// kit's fakes stay in place. Called by <see cref="BakabaseStartup"/> after <c>AddFederatedLibrary()</c>.
    /// </summary>
    /// <remarks>
    /// Until the sync runtime lands, it only fills the gaps with placeholders, each registered with <c>TryAdd</c> so
    /// the runtime's own registration (made earlier, through <c>AddInsideWorldBusinesses</c>) wins: grant events
    /// nobody listens to, and a facade that answers that data sync is not available yet.
    /// </remarks>
    public static IServiceCollection AddDataSyncServiceComponents(this IServiceCollection services)
    {
        services.TryAddSingleton<IDataSyncGrantEvents, NoOpDataSyncGrantEvents>();
        services.TryAddScoped<IDataSyncService, UnavailableDataSyncService>();
        return services;
    }
}

using System;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync;

public static class DataSyncServiceCollectionExtensions
{
    /// <summary>
    /// Data sync's persistence (spec §4, §5) and the runtime (<see cref="DataSyncRuntimeServiceCollectionExtensions.AddDataSyncRuntime"/>).
    /// Called from <c>AddInsideWorldBusinesses()</c>, so every host and the TestKit get it.
    /// </summary>
    /// <remarks>
    /// Registration never resolves anything, never reads the device identity and never touches the file system
    /// (v3.1 §1): the data directory is resolved on first use, and the device identity and host kind are the host's
    /// (the Service in production, fakes in the TestKit), resolved by the members that need them. The data directory
    /// is <c>TryAdd</c>, so the TestKit's per-provider folders registered later win.
    /// </remarks>
    public static IServiceCollection AddDataSync(this IServiceCollection services)
    {
        services.TryAddSingleton<DataSyncGate>();
        services.TryAddSingleton<DataSyncReaderLog>();
        services.TryAddSingleton<IDataSyncDataDirectory, AppDataSyncDataDirectory>();
        services.TryAddSingleton<DataSyncActorWatermarkFile>();
        services.TryAddSingleton<IDataSyncReviewStore>(sp =>
            new DataSyncReviewStore(sp.GetService<TimeProvider>() ?? TimeProvider.System));

        services.TryAddScoped<DataSyncStore>();
        services.TryAddScoped<IDataSyncStore>(sp => sp.GetRequiredService<DataSyncStore>());
        services.TryAddScoped<DataSyncIdentityStore>();

        // Change detection and the actor (§5.6, §6). The guard is process-wide: verification and pending evidence are
        // this process's; it opens its own scopes for its own transactions.
        services.TryAddSingleton<DataSyncActorGuard>(sp => new DataSyncActorGuard(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<DataSyncActorWatermarkFile>(),
            sp.GetService<TimeProvider>(), sp.GetService<ILogger<DataSyncActorGuard>>()));
        services.TryAddSingleton<IDataSyncActorGuard>(sp => sp.GetRequiredService<DataSyncActorGuard>());
        services.TryAddScoped<DataSyncRefresher>();
        services.TryAddScoped<IDataSyncRefresher>(sp => sp.GetRequiredService<DataSyncRefresher>());
        services.TryAddScoped<DataSyncLocalStateReader>();
        services.TryAddSingleton<DataSyncRefreshCoordinator>(sp => new DataSyncRefreshCoordinator(
            sp.GetRequiredService<DataSyncGate>(), sp.GetRequiredService<IDataSyncActorGuard>(),
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<DataSyncActorWatermarkFile>(),
            sp.GetService<TimeProvider>()));
        services.TryAddSingleton<DataSyncRetention>(sp => new DataSyncRetention(sp.GetRequiredService<DataSyncGate>(),
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IDataSyncDataDirectory>(),
            sp.GetService<TimeProvider>(), sp.GetService<ILogger<DataSyncRetention>>()));

        // Kind adapters register next to the services that own their tables: the custom property kind in the Property
        // module, the extension group kind through AddExtensionGroupDataSyncKind(codec) with the pure engine's codec.
        // Refresh of a kind with an order needs an IDataSyncOrderMoveDetector over the engine's order planner.

        services.AddDataSyncRuntime();
        return services;
    }
}

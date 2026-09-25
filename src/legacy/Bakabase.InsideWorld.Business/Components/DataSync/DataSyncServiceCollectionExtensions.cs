using System;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Feed;
using Bakabase.InsideWorld.Business.Components.DataSync.Kinds;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync;

public static class DataSyncServiceCollectionExtensions
{
    /// <summary>
    /// Data sync's persistence (spec §4, §5), change detection (§6), the feed source (§7.5) and the runtime
    /// (<see cref="DataSyncRuntimeServiceCollectionExtensions.AddDataSyncRuntime"/>).
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
        // The runtime's facade enters this very gate (§10.1): one gate serializes the feed, the runner and the API.
        services.TryAddSingleton<IDataSyncGateEntry>(sp => sp.GetRequiredService<DataSyncGate>());
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
        services.TryAddScoped<IDataSyncLocalStateReader>(sp => sp.GetRequiredService<DataSyncLocalStateReader>());
        services.TryAddSingleton<DataSyncRefreshCoordinator>(sp => new DataSyncRefreshCoordinator(
            sp.GetRequiredService<DataSyncGate>(), sp.GetRequiredService<IDataSyncActorGuard>(),
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<DataSyncActorWatermarkFile>(),
            sp.GetService<TimeProvider>()));
        services.TryAddSingleton<DataSyncRetention>(sp => new DataSyncRetention(sp.GetRequiredService<DataSyncGate>(),
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IDataSyncDataDirectory>(),
            sp.GetService<TimeProvider>(), sp.GetService<ILogger<DataSyncRetention>>()));
        services.TryAddSingleton<IDataSyncRetention>(sp => sp.GetRequiredService<DataSyncRetention>());

        // The feed source (§7.5): D's node controller serves it to readers holding a datasync grant. Snapshots and the
        // SeenCounter high-water map are process-wide; every store feeds the map the vectors its context saves. Pages
        // are written by the pure engine's wire writer (package A) through a seam the feed's tests can fill.
        services.TryAddSingleton(DataSyncLimits.Default);
        services.TryAddSingleton<DataSyncFeedSnapshots>(sp => new DataSyncFeedSnapshots(sp.GetService<TimeProvider>()));
        services.TryAddSingleton<DataSyncSeenCounters>(sp => new DataSyncSeenCounters(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetService<ILogger<DataSyncSeenCounters>>()));
        services.TryAddSingleton<IDataSyncFeedPageWriter, DataSyncWireFeedPageWriter>();
        services.TryAddSingleton<DataSyncFeedSource>(sp => new DataSyncFeedSource(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<DataSyncGate>(),
            sp.GetRequiredService<DataSyncActorGuard>(), sp.GetRequiredService<DataSyncRefreshCoordinator>(),
            sp.GetRequiredService<DataSyncFeedSnapshots>(), sp.GetRequiredService<DataSyncSeenCounters>(),
            sp.GetRequiredService<IDataSyncFeedPageWriter>(), sp.GetService<DataSyncLimits>(),
            sp.GetService<TimeProvider>(), sp.GetService<ILogger<DataSyncFeedSource>>()));
        services.TryAddSingleton<IDataSyncFeedSource>(sp => sp.GetRequiredService<DataSyncFeedSource>());

        // Kind adapters register next to the services that own their tables: the custom property kind in the Property
        // module (AddProperty), the extension group kind here with the pure engine's codec (every kind of
        // DataSyncKindIds.All, DbSetClassificationTests). Refresh of a kind with an order detects local moves with the
        // engine's order planner.
        services.AddExtensionGroupDataSyncKind(ExtensionGroupCodec.Instance);
        services.TryAddSingleton<IDataSyncOrderMoveDetector, DataSyncPlannerOrderMoveDetector>();

        // Apply, undo and restore (§8.10, §8.11, §9.2, §9.5): the runner is process-wide and opens a scope per attempt.
        services.TryAddSingleton<DataSyncTaskRegistry>();
        services.TryAddSingleton<IDataSyncTaskRegistry>(sp => sp.GetRequiredService<DataSyncTaskRegistry>());
        services.TryAddSingleton<DataSyncBackup>(sp => new DataSyncBackup(sp.GetRequiredService<IDataSyncDataDirectory>(),
            sp.GetService<TimeProvider>()));
        services.TryAddSingleton<DataSyncUndoPlanner>(sp =>
            new DataSyncUndoPlanner(sp.GetRequiredService<IServiceScopeFactory>()));
        services.TryAddSingleton<IDataSyncUndoPreviewer>(sp => sp.GetRequiredService<DataSyncUndoPlanner>());
        services.TryAddSingleton<DataSyncApplyRunner>(sp => new DataSyncApplyRunner(sp,
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<DataSyncGate>(),
            sp.GetRequiredService<DataSyncActorGuard>(), sp.GetRequiredService<DataSyncActorWatermarkFile>(),
            sp.GetRequiredService<IDataSyncTaskRegistry>(), sp.GetRequiredService<IDataSyncReviewStore>(),
            sp.GetRequiredService<DataSyncBackup>(), sp.GetService<DataSyncRefreshCoordinator>(),
            sp.GetService<ILogger<DataSyncApplyRunner>>()));
        services.TryAddSingleton<IDataSyncApplyRunner>(sp => sp.GetRequiredService<DataSyncApplyRunner>());

        services.AddDataSyncRuntime();
        return services;
    }
}

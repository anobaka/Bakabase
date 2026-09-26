using System.Linq;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

public static class DataSyncRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// The sync runtime (spec §2.9, §8.2, §8.10): the scheduler, the <c>DataSync</c> fetch task's cycle, the
    /// <c>DataSyncApply</c> task, the task launcher and attempt registry, the staged-pull store, the link service,
    /// and grant events; the notifier and the hub publisher (§9.4, §8.10.6); and the
    /// <see cref="IDataSyncService"/> facade the <c>/data-sync</c> API calls (§10.1). <c>AddDataSync()</c> calls it,
    /// so the test kit gets the runtime too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like <c>AddDataSync()</c>, it never resolves identity or touches the file system at registration time. The
    /// scheduler is a hosted service that does nothing until the host has registered the fetch task, which happens
    /// after the database migrations.
    /// </para>
    /// <para>
    /// The <c>DataSync</c> task itself is discovered by <c>AddBTask</c> like every predefined task, so it is not
    /// registered here. Seams other packages fill are registered with <c>TryAdd</c> so theirs win when they register
    /// first: the clock, the limits, the observer (the notifier and hub publisher) and the page reader. The runtime
    /// needs C's store, runner, actor guard, review store, local state reader and undo previewer, D's peer client and
    /// grant service, and the host's gate entry over C's DataSyncGate, device identity and host kind; it resolves them
    /// per scope and registers none of them.
    /// </para>
    /// <para>
    /// The facade is registered with <c>TryAdd</c> as well: the Service's placeholder, registered later, then stays
    /// out, and a test that registered its own fake first keeps it.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDataSyncRuntime(this IServiceCollection services)
    {
        // Idempotent: grant events and the hosted scheduler must be registered once.
        if (services.Any(d => d.ServiceType == typeof(DataSyncScheduler))) return services;

        services.TryAddSingleton<IDataSyncClock, SystemDataSyncClock>();
        services.TryAddSingleton<IDataSyncRowTransactions, DataSyncDbRowTransactions>();
        services.TryAddSingleton(DataSyncLimits.Default);
        services.TryAddSingleton<DataSyncNotifier>();
        services.TryAddSingleton<DataSyncHubPublisher>();
        services.TryAddSingleton<DataSyncRuntimeEvents>();
        services.TryAddSingleton<IDataSyncRuntimeObserver>(sp => sp.GetRequiredService<DataSyncRuntimeEvents>());
        // The apply runner tells the hub and the notifier what an apply changed, after its commit (§8.10.2).
        services.AddSingleton<IDataSyncApplyListener>(sp => sp.GetRequiredService<DataSyncRuntimeEvents>());
        services.TryAddScoped<IDataSyncKindPageReader, DataSyncKindPageReader>();
        services.TryAddScoped<IDataSyncService, DataSyncService>();

        // The attempt registry is the apply runner's (package C, the same instance whatever registers first).
        services.TryAddSingleton<DataSyncTaskRegistry>();
        services.TryAddSingleton<IDataSyncTaskRegistry>(sp => sp.GetRequiredService<DataSyncTaskRegistry>());
        services.TryAddSingleton<DataSyncStagedPullStore>();
        services.TryAddSingleton<IDataSyncStagedPullStore>(sp => sp.GetRequiredService<DataSyncStagedPullStore>());

        services.TryAddSingleton<DataSyncRuntimeState>();
        services.TryAddSingleton<DataSyncTaskLauncher>();
        services.TryAddSingleton<DataSyncLinkService>();
        services.TryAddSingleton<DataSyncFetcher>();
        services.TryAddSingleton<DataSyncApplyTask>();

        // Grant events are the runtime's own: registered before the Service's no-op placeholder, which then stays out.
        services.TryAddSingleton<DataSyncGrantEventsHandler>();
        services.AddSingleton<IDataSyncGrantEvents>(sp => sp.GetRequiredService<DataSyncGrantEventsHandler>());

        services.TryAddSingleton<DataSyncScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<DataSyncScheduler>());
        return services;
    }
}

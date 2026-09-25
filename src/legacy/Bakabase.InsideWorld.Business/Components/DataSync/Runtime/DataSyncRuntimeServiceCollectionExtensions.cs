using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

public static class DataSyncRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// The sync runtime (spec §2.9): the scheduler, the <c>DataSync</c> and <c>DataSyncApply</c> tasks, the staged-pull
    /// store, grant events, the link, inbox and restore services, the notifier, the hub publisher and the
    /// <c>IDataSyncService</c> facade. <c>AddDataSync()</c> calls it, so the test kit gets the runtime too.
    /// </summary>
    /// <remarks>
    /// Empty until the runtime lands. Like <c>AddDataSync()</c>, it must never resolve identity or touch the file
    /// system at registration time.
    /// </remarks>
    public static IServiceCollection AddDataSyncRuntime(this IServiceCollection services) => services;
}

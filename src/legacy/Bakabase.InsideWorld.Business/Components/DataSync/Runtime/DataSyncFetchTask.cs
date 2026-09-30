using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The recurring <c>DataSync</c> task (§8.2): it <b>fetches only</b> — head polls, manifests, pages, staging of pulls
/// and reviews, and retention once a day. It writes no definitions, so its conflict key is its own id and it never
/// waits behind an enhancement run; applying is <c>DataSyncApply</c>'s. The scheduler starts it whenever a link is
/// due; its 10-minute interval is the fallback pull.
/// </summary>
/// <remarks>Discovered by <c>AddBTask</c> like every predefined task.</remarks>
public sealed class DataSyncFetchTask : AbstractPredefinedBTaskBuilder
{
    public DataSyncFetchTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => DataSyncTaskIds.Fetch;

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => DataSyncRuntimeState.FetchInterval;

    public override HashSet<string>? ConflictKeys => [DataSyncTaskIds.Fetch];

    /// <remarks>
    /// However the cycle ends, the observer hears it (<see cref="IDataSyncRuntimeObserver.TaskEndedAsync"/>): what the
    /// cycle pushed counted the fetch as syncing, and nothing else says it is over.
    /// </remarks>
    public override async Task RunAsync(BTaskArgs args)
    {
        try
        {
            await args.YieldAsync();
            await ServiceProvider.GetRequiredService<DataSyncFetcher>().RunCycleAsync(args);
        }
        finally
        {
            await DataSyncTaskLauncher.NoteEndedAsync(ServiceProvider.GetRequiredService<IDataSyncRuntimeObserver>(), Id,
                ServiceProvider.GetRequiredService<ILogger<DataSyncFetchTask>>());
        }
    }
}

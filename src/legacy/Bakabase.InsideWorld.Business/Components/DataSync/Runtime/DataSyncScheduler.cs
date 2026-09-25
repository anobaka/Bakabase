using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// Once per second, decides what data sync runs (§8.2). It never does network work, and its only database work is
/// reading link rows and the local state row (and, once at the start, making every link due):
/// <list type="bullet">
/// <item>it hands grant events to the link service, so a granted request reaches its review within seconds;</item>
/// <item>it starts the <c>DataSync</c> fetch task when a link is due and the task is not active;</item>
/// <item>it enqueues <c>DataSyncApply</c> when staged pulls wait or a link's once flags act without a pull, and the
/// actor is verified.</item>
/// </list>
/// Two rules keep it from fighting the person and the host: nothing is started or enqueued once
/// <c>ApplicationStopping</c> fired (<c>BTaskManager.Start</c> has no shutdown check, F71), and a <c>DataSync</c> task
/// the person stopped is not restarted before its next 10-minute interval unless they press "Sync now"
/// (<c>Start</c> would otherwise restart a cancelled task one second later).
/// </summary>
/// <remarks>
/// Nothing runs until the fetch task is registered with the task manager: the host registers predefined tasks after
/// its database migrations, and hosted services start before them.
/// </remarks>
public sealed class DataSyncScheduler : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly BTaskManager _btm;
    private readonly DataSyncTaskLauncher _launcher;
    private readonly DataSyncLinkService _links;
    private readonly DataSyncGrantEventsHandler _grantEvents;
    private readonly IDataSyncStagedPullStore _stagedPulls;
    private readonly DataSyncRuntimeState _state;
    private readonly IDataSyncClock _clock;
    private readonly ILogger<DataSyncScheduler> _logger;
    private readonly SemaphoreSlim _tickLock = new(1, 1);

    private DateTime? _observedStopStartedAt;
    private DateTime? _personStoppedAtUtc;

    public DataSyncScheduler(IServiceScopeFactory scopes, BTaskManager btm, DataSyncTaskLauncher launcher,
        DataSyncLinkService links, DataSyncGrantEventsHandler grantEvents, IDataSyncStagedPullStore stagedPulls,
        DataSyncRuntimeState state, IDataSyncClock clock, ILogger<DataSyncScheduler> logger)
    {
        _scopes = scopes;
        _btm = btm;
        _launcher = launcher;
        _links = links;
        _grantEvents = grantEvents;
        _stagedPulls = stagedPulls;
        _state = state;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "The data sync scheduler failed a tick");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One evaluation (§8.2). Public so tests drive it with their own clock instead of waiting.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (_launcher.IsStopping) return;
        await _tickLock.WaitAsync(ct);
        try
        {
            if (_launcher.IsStopping) return;
            var fetchTask = FetchTask();
            if (fetchTask is null) return;

            var now = _clock.UtcNow;
            if (_state.TryStart(now))
            {
                // Startup: every link is due in 5 s; that first head round also verifies the actor (§5.6).
                await _links.ScheduleAllAsync(now + DataSyncSchedule.StartupDelay, ct);
            }

            await _grantEvents.DrainAsync(ct);
            if (_launcher.IsStopping) return;

            var links = await _links.GetLinksAsync(ct);
            DataSyncLocalStateDbModel? local;
            IDataSyncActorGuard? guard;
            await using (var scope = _scopes.CreateAsyncScope())
            {
                local = await scope.ServiceProvider.GetRequiredService<IDataSyncStore>().GetLocalStateAsync(ct);
                guard = scope.ServiceProvider.GetService<IDataSyncActorGuard>();
                if (guard is { IsVerified: false } && _state.CanVerify(links, now)) guard.MarkVerified();
            }

            var allPaused = local?.AllPaused == true;
            if (!allPaused && links.Any(l => IsDue(l, now)) && CanStartFetch(fetchTask, now))
            {
                await _btm.Start(DataSyncTaskIds.Fetch);
            }

            if (_launcher.IsStopping) return;
            var verified = guard is null || guard.IsVerified;
            var applyWaits = _stagedPulls.LinksWaiting().Count > 0 || links.Any(l => HasApplyWork(l, now));
            if (!allPaused && verified && applyWaits) await _launcher.EnqueueApplyAsync();
        }
        finally
        {
            _tickLock.Release();
        }
    }

    /// <summary>
    /// "Sync now" (§8.2): the link (or every link) is due now, a person's earlier stop no longer holds, and the fetch
    /// task starts at once. Returns the task id, or null while the host is stopping or the task is not registered.
    /// </summary>
    public async Task<string?> SyncNowAsync(int? linkId, CancellationToken ct)
    {
        await _links.MarkDueAsync(linkId, ct);
        _personStoppedAtUtc = null;
        if (_launcher.IsStopping) return null;
        var fetchTask = FetchTask();
        if (fetchTask is null) return null;
        if (!fetchTask.Task.Status.IsActive()) await _btm.Start(DataSyncTaskIds.Fetch);
        return DataSyncTaskIds.Fetch;
    }

    /// <summary>A link the fetch cycle looks at, whose next attempt is due.</summary>
    public static bool IsDue(DataSyncLinkDbModel link, DateTime nowUtc) =>
        link.IsFetchable() && (link.NextAttemptAtUtc is not { } next || next <= nowUtc);

    /// <summary>
    /// A link whose once flags act without a pull (N13): the scheduler enqueues <c>DataSyncApply</c> for it, unless
    /// the link is backing off after a failed apply.
    /// </summary>
    private static bool HasApplyWork(DataSyncLinkDbModel link, DateTime nowUtc) =>
        link.State == DataSyncLinkState.Active &&
        link.GetOnceFlags().PullIndependent() != DataSyncMergeFlags.None &&
        !(link.ConsecutiveFailures > 0 && link.NextAttemptAtUtc is { } next && next > nowUtc);

    private BTaskHandler? FetchTask() =>
        _btm.Tasks.FirstOrDefault(t => string.Equals(t.Id, DataSyncTaskIds.Fetch, StringComparison.Ordinal));

    /// <summary>
    /// Whether the scheduler may start the fetch task now: it is not active, and it was not stopped by a person
    /// within its interval. A stop is recognised by the task ending Cancelled — the scheduler itself never stops it —
    /// and is timed by this runtime's clock from when the scheduler first saw it.
    /// </summary>
    private bool CanStartFetch(BTaskHandler fetchTask, DateTime nowUtc)
    {
        var task = fetchTask.Task;
        if (task.Status.IsActive()) return false;
        if (task.Status == BTaskStatus.Cancelled)
        {
            if (_observedStopStartedAt != task.StartedAt)
            {
                _observedStopStartedAt = task.StartedAt;
                _personStoppedAtUtc = nowUtc;
                _logger.LogInformation("The data sync task was stopped; it runs again at its next interval");
            }

            if (_personStoppedAtUtc is { } stoppedAt && nowUtc - stoppedAt < DataSyncRuntimeState.FetchInterval)
                return false;
        }

        return true;
    }
}

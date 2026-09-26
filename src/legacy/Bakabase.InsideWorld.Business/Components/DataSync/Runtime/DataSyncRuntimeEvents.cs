using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The runtime's observer (<see cref="IDataSyncRuntimeObserver"/>): each event goes to the notifier (§9.4) and then to
/// the hub (§8.10.6). One failing never keeps the other from running, and neither ever fails the change it reports.
/// </summary>
/// <remarks>
/// It also hears the persistence layer directly:
/// <list type="bullet">
/// <item>the apply runner's <see cref="IDataSyncApplyListener"/> after every apply that changed definitions committed —
/// the hub's <see cref="DataSyncHubPublisher.AppliedKey"/> (what changed, for open pages to refetch) and the notifier's
/// sweep (items the apply closed, here or on other links, may settle their notifications);</item>
/// <item>the actor guard's <see cref="DataSyncActorGuard.RestoreDetected"/>, whoever ran the check that detected it (a
/// reader's head, an apply, the start): the restore is announced at once, one notification per detection however
/// many links it paused, and an escalation from suspected to detected sends no second one (§8.7 B6).</item>
/// </list>
/// Both are raised by the persistence layer after its commit, so they are handled in the background, one at a time.
/// </remarks>
public sealed class DataSyncRuntimeEvents : IDataSyncRuntimeObserver, IDataSyncApplyListener
{
    private readonly DataSyncNotifier _notifier;
    private readonly DataSyncHubPublisher _hub;
    private readonly ILogger<DataSyncRuntimeEvents> _logger;
    private readonly SemaphoreSlim _background = new(1, 1);
    private string? _lastRestoreSeen;

    public DataSyncRuntimeEvents(DataSyncNotifier notifier, DataSyncHubPublisher hub, IServiceProvider services,
        ILogger<DataSyncRuntimeEvents> logger)
    {
        _notifier = notifier;
        _hub = hub;
        _logger = logger;
        // The guard is the persistence layer's; a host that composes the runtime without it has no detections to hear.
        if (services.GetService<DataSyncActorGuard>() is { } guard) guard.RestoreDetected += OnRestoreDetected;
    }

    public async Task LinkChangedAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        await GuardAsync(() => _notifier.LinkChangedAsync(link, ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

    public async Task LinkRemovedAsync(DataSyncLinkDbModel removed, CancellationToken ct)
    {
        // A reset closes the link's items (§8.1), which may settle their notifications.
        await GuardAsync(() => _notifier.LinkRemovedAsync(removed.Id, ct));
        await GuardAsync(() => _notifier.SweepAsync(ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

    public Task LinkCycleStartedAsync(int linkId, CancellationToken ct) =>
        GuardAsync(() => _notifier.LinkCycleStartedAsync(linkId, ct));

    public Task LinkFetchEndedAsync(int linkId, bool applyFollows, CancellationToken ct) =>
        GuardAsync(() => _notifier.LinkFetchEndedAsync(linkId, applyFollows, ct));

    public async Task LinkPausedAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        await GuardAsync(() => _notifier.LinkPausedAsync(link, ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

    public async Task ReviewReadyAsync(DataSyncLinkDbModel link, DataSyncReviewEntry review, CancellationToken ct)
    {
        await GuardAsync(() => _notifier.ReviewReadyAsync(link, review, ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

    /// <remarks>What the pull changed reached the hub from the runner already (<see cref="OnApplied"/>).</remarks>
    public async Task AutoSyncAppliedAsync(DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome, bool firstSync,
        CancellationToken ct)
    {
        await GuardAsync(() => _notifier.AutoSyncAppliedAsync(link, outcome, firstSync, ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

    /// <remarks>
    /// Resolutions, undo, a restore and entity settings close items here and elsewhere (§9.3). The runner's writes
    /// reached the hub from the runner already (<see cref="OnApplied"/>); an entity setting, which changes no
    /// definition but how one syncs, is pushed from its history entry.
    /// </remarks>
    public async Task WriteAppliedAsync(DataSyncHistoryKind kind, int? applyLogId, int? linkId, CancellationToken ct)
    {
        await GuardAsync(() => _notifier.SweepAsync(ct));
        if (kind == DataSyncHistoryKind.EntitySetting) await GuardAsync(() => _hub.PublishAppliedAsync(applyLogId, ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

    public async Task LocalStateSeenAsync(DataSyncLocalStateDbModel? local, CancellationToken ct)
    {
        await GuardAsync(() => _notifier.LocalStateSeenAsync(local, ct));

        // The indicator learns of a restore detection or choice on the tick that sees it, not a second later.
        var restore = local?.RestoreReason is null ? null : $"{local.RestoreReason}@{local.RestoreDetectedAtUtc:O}";
        if (restore != _lastRestoreSeen)
        {
            _lastRestoreSeen = restore;
            await GuardAsync(() => _hub.PublishStatusAsync(ct));
        }
    }

    public Task StateChangedAsync(CancellationToken ct) => GuardAsync(() => _hub.PublishStatusAsync(ct));

    /// <summary>
    /// The apply runner committed an apply that changed definitions (§8.10.2 "after commit"): open pages refetch what
    /// changed, and notifications whose items the apply closed are marked read.
    /// </summary>
    public void OnApplied(DataSyncAppliedEvent applied) => InBackground(async () =>
    {
        await GuardAsync(() => _hub.PublishAppliedAsync(applied, CancellationToken.None));
        await GuardAsync(() => _notifier.SweepAsync(CancellationToken.None));
    });

    private void OnRestoreDetected(object? sender, DataSyncRestoreDetection detection) => InBackground(async () =>
    {
        if (!detection.Escalated) await GuardAsync(() => _notifier.RestoreDetectedAsync(CancellationToken.None));
        await GuardAsync(() => _hub.PublishStatusAsync(CancellationToken.None));
    });

    /// <summary>
    /// Runs <paramref name="work"/> after the caller returns, one piece at a time: the persistence layer raises these
    /// after its commit, from inside a task body or a feed request that must not wait for notifications.
    /// </summary>
    private void InBackground(Func<Task> work) => _ = Task.Run(async () =>
    {
        await _background.WaitAsync();
        try
        {
            await work();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "A data sync notification or hub push failed");
        }
        finally
        {
            _background.Release();
        }
    });

    private async Task GuardAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "A data sync notification or hub push failed");
        }
    }
}

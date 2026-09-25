using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The runtime's observer (<see cref="IDataSyncRuntimeObserver"/>): each event goes to the notifier (§9.4) and then to
/// the hub (§8.10.6). One failing never keeps the other from running, and neither ever fails the change it reports.
/// </summary>
public sealed class DataSyncRuntimeEvents : IDataSyncRuntimeObserver
{
    private readonly DataSyncNotifier _notifier;
    private readonly DataSyncHubPublisher _hub;
    private readonly ILogger<DataSyncRuntimeEvents> _logger;
    private string? _lastRestoreSeen;

    public DataSyncRuntimeEvents(DataSyncNotifier notifier, DataSyncHubPublisher hub,
        ILogger<DataSyncRuntimeEvents> logger)
    {
        _notifier = notifier;
        _hub = hub;
        _logger = logger;
    }

    public async Task LinkChangedAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        await GuardAsync(() => _notifier.LinkChangedAsync(link, ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

    public async Task LinkRemovedAsync(DataSyncLinkDbModel removed, CancellationToken ct)
    {
        // A reset closes the link's items (§8.1), which may settle their notifications.
        await GuardAsync(() => _notifier.SweepAsync(ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

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

    public async Task AutoSyncAppliedAsync(DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome, bool firstSync,
        CancellationToken ct)
    {
        await GuardAsync(() => _notifier.AutoSyncAppliedAsync(link, outcome, firstSync, ct));
        await GuardAsync(() => _hub.PublishAppliedAsync(outcome.ApplyLogId, ct));
        await GuardAsync(() => _hub.PublishStatusAsync(ct));
    }

    public async Task WriteAppliedAsync(DataSyncHistoryKind kind, int? applyLogId, int? linkId, CancellationToken ct)
    {
        // Resolutions, undo, a restore and entity settings close items here and elsewhere (§9.3).
        await GuardAsync(() => _notifier.SweepAsync(ct));
        await GuardAsync(() => _hub.PublishAppliedAsync(applyLogId, ct));
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

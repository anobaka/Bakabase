using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>The runtime's clock, so tests can move time without waiting for it. Every value is UTC.</summary>
public interface IDataSyncClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemDataSyncClock : IDataSyncClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// What the runtime tells the notifier (§9.4) and the hub publisher (§8.10.6). Called after the change is stored,
/// never inside a transaction; an observer failure is logged and never undoes the change. The runtime registers a
/// no-op with <c>TryAdd</c>, so the notifier and hub publisher replace it by registering first.
/// </summary>
public interface IDataSyncRuntimeObserver
{
    /// <summary>A link changed state, mode or kinds (§8.1: every transition publishes the status).</summary>
    Task LinkChangedAsync(DataSyncLinkDbModel link, CancellationToken ct);

    /// <summary>A link was reset or dismissed: its row is gone (§8.1).</summary>
    Task LinkRemovedAsync(DataSyncLinkDbModel removed, CancellationToken ct);

    /// <summary>A link paused (§8.7); called once per pause.</summary>
    Task LinkPausedAsync(DataSyncLinkDbModel link, CancellationToken ct);

    /// <summary>A first-link review was staged: once per staged review (§8.3 step 3).</summary>
    Task ReviewReadyAsync(DataSyncLinkDbModel link, DataSyncReviewEntry review, CancellationToken ct);

    /// <summary>
    /// A pull or a re-merge of pending records was applied for a link. <paramref name="firstSync"/>: the approver's
    /// first pull finished (§8.3, §9.4 "First sync with {0}").
    /// </summary>
    Task AutoSyncAppliedAsync(DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome, bool firstSync,
        CancellationToken ct);
}

public sealed class NoOpDataSyncRuntimeObserver : IDataSyncRuntimeObserver
{
    public Task LinkChangedAsync(DataSyncLinkDbModel link, CancellationToken ct) => Task.CompletedTask;
    public Task LinkRemovedAsync(DataSyncLinkDbModel removed, CancellationToken ct) => Task.CompletedTask;
    public Task LinkPausedAsync(DataSyncLinkDbModel link, CancellationToken ct) => Task.CompletedTask;

    public Task ReviewReadyAsync(DataSyncLinkDbModel link, DataSyncReviewEntry review, CancellationToken ct) =>
        Task.CompletedTask;

    public Task AutoSyncAppliedAsync(DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome, bool firstSync,
        CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Retention (§4.6), run by the <c>DataSync</c> task once a day. The implementation [C] enters the DataSyncGate and
/// runs <see cref="IDataSyncStore.PruneAsync"/> in its own short transaction; the fetch task skips retention while
/// none is registered.
/// </summary>
public interface IDataSyncRetention
{
    Task RunAsync(DateTime nowUtc, CancellationToken ct);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;

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

    /// <summary>
    /// A write that is not an automatic pull finished (§8.10.1, §6.6): a review
    /// (<see cref="DataSyncHistoryKind.FirstLink"/>), a resolution batch, an undo, a restore choice or an entity
    /// setting. <paramref name="applyLogId"/> is its history entry, null when nothing was applied;
    /// <paramref name="linkId"/> the link it was about, when one.
    /// </summary>
    Task WriteAppliedAsync(DataSyncHistoryKind kind, int? applyLogId, int? linkId, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>
    /// The scheduler read the local state row on its tick (§8.2): about once a second, never inside a transaction.
    /// What a restore detection wrote there is announced from here (§9.4), whoever paused the links.
    /// </summary>
    Task LocalStateSeenAsync(DataSyncLocalStateDbModel? local, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Something the status shows changed outside a link row: sharing switched, a request approved or rejected, a
    /// reader revoked, the global pause.
    /// </summary>
    Task StateChangedAsync(CancellationToken ct) => Task.CompletedTask;
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

/// <summary>
/// The process-wide DataSyncGate as the facade enters it (§10.1: "Gate" waits at most 30 s, else <c>Busy</c>).
/// Package C's <c>DataSyncGate</c> issues the leases, and the host registers this over that very gate, so the facade,
/// the apply runner and the feed share one. Nothing registers a default: a second gate would serialize nothing.
/// </summary>
public interface IDataSyncGateEntry
{
    /// <summary>A lease, or null when the gate was not free within <paramref name="timeout"/> (null: no limit).</summary>
    Task<DataSyncGateLease?> TryEnterAsync(TimeSpan? timeout, CancellationToken ct);
}

/// <summary>
/// This device's definitions as the first-contact planner compares a staged review with (§8.3 step 4): the last
/// committed state of the given kinds, read in a read transaction, without Refresh and without any write (F78) [C].
/// </summary>
public interface IDataSyncLocalStateReader
{
    Task<IReadOnlyDictionary<string, DataSyncLocalKindState>> ReadAsync(IReadOnlyCollection<string> kinds,
        CancellationToken ct);
}

/// <summary>
/// What undoing one history entry would do (§8.11), computed read-only with the continuous rules and the per-entity
/// refusals [C]. The facade answers for the entries nothing can undo (an undo, an undone or unknown entry) itself.
/// </summary>
public interface IDataSyncUndoPreviewer
{
    Task<DataSyncUndoPreview> PreviewAsync(DataSyncApplyLogDbModel entry, CancellationToken ct);
}

/// <summary>
/// The addresses other devices can reach this one at (the overview, §10.1). The Service registers it over remote
/// access; without it the overview lists none.
/// </summary>
public interface IDataSyncHostAddresses
{
    IReadOnlyList<string> GetReachableAddresses();
}

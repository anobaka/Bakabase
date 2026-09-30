using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
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
/// never inside a transaction; an observer failure is logged and never undoes the change. The runtime registers
/// <see cref="DataSyncRuntimeEvents"/>, which hands every event to both.
/// </summary>
public interface IDataSyncRuntimeObserver
{
    /// <summary>A link changed state, mode or kinds (§8.1: every transition publishes the status).</summary>
    Task LinkChangedAsync(DataSyncLinkDbModel link, CancellationToken ct);

    /// <summary>A link was reset or dismissed: its row is gone (§8.1).</summary>
    Task LinkRemovedAsync(DataSyncLinkDbModel removed, CancellationToken ct);

    /// <summary>A link paused (§8.7); called once per pause.</summary>
    Task LinkPausedAsync(DataSyncLinkDbModel link, CancellationToken ct);

    /// <summary>A first sync's snapshot was staged for its person's Start (§8.3 step 3).</summary>
    Task ReviewReadyAsync(DataSyncLinkDbModel link, CancellationToken ct);

    /// <summary>
    /// A pull or a re-merge of pending records was applied for a link. <paramref name="firstSync"/>: its first sync
    /// finished (§8.3, §9.4 "First sync with {0}").
    /// </summary>
    Task AutoSyncAppliedAsync(DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome, bool firstSync,
        CancellationToken ct);

    /// <summary>
    /// A write that is not an automatic pull finished (§8.10.1, §6.6): a resolution batch, an undo, a restore choice
    /// or an entity setting. <paramref name="applyLogId"/> is its history entry, null when nothing was applied;
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

    /// <summary>
    /// A data sync task's body returned or threw — the fetch cycle or a write task (§8.10.1) — and the task leaves the
    /// active set right after. Whatever it pushed while it ran counted it as syncing: the status is said again, as it
    /// is once <paramref name="taskId"/> is over.
    /// </summary>
    Task TaskEndedAsync(string taskId, CancellationToken ct) => Task.CompletedTask;
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
/// The write transaction a read-modify-write of a link row (or the local state row) runs in
/// (<see cref="DataSyncLinkService"/>). The default, <see cref="DataSyncDbRowTransactions"/>, opens one on the scope's
/// <c>BakabaseDbContext</c> — <c>BEGIN IMMEDIATE</c> on SQLite (F27) — so the row is read only once no other
/// transaction can change it before the write: the apply runner's commits of link state (§8.10.2) are ordered with the
/// runtime's own bookkeeping by the database, not by a lock the runner cannot take.
/// </summary>
public interface IDataSyncRowTransactions
{
    /// <summary>
    /// Begins the transaction on <paramref name="scope"/>'s context, which the store of that scope joins. Disposing it
    /// without <see cref="IDataSyncRowTransaction.CommitAsync"/> rolls it back.
    /// </summary>
    Task<IDataSyncRowTransaction> BeginAsync(IServiceProvider scope, CancellationToken ct);
}

public interface IDataSyncRowTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

/// <summary>
/// The addresses other devices can reach this one at (the overview, §10.1). The Service registers it over remote
/// access; without it the overview lists none.
/// </summary>
public interface IDataSyncHostAddresses
{
    IReadOnlyList<string> GetReachableAddresses();
}

/// <summary>
/// Whether a federation session to a peer is verified now (§8.2 "a federation session to it came online → now"). The
/// scheduler reads it on every tick, so it must answer from memory. The Service registers it over the federation
/// sessions; without it that trigger is not wired and links wait for their own next attempt.
/// </summary>
public interface IDataSyncPeerSessions
{
    bool IsOnline(string peerNodeId);
}

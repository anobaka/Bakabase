using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Modules.DataSync.Runtime;

// §2.9: Refresh, the actor guard, the apply runner and the in-memory stores [C, E].

/// <summary>
/// Proof that the caller holds the process-wide DataSyncGate. The gate (Business) issues leases; interfaces here
/// only name the type. Disposing releases the gate.
/// </summary>
public abstract class DataSyncGateLease : IDisposable
{
    public abstract bool IsHeld { get; }
    public abstract void Dispose();
}

/// <summary>[C] v3.1 §4.4 extended by §6.</summary>
public interface IDataSyncRefresher
{
    /// <summary>
    /// Skipped (no writes at all) while the actor is unverified or a restore is pending evidence handling (§5.6).
    /// Never rotates: it asserts the actor is current (the caller ran IDataSyncActorGuard.CheckAsync before any
    /// transaction) and otherwise throws DataSyncActorChangedException (§5.6).
    /// collectPublished: also return what each synced entity publishes, so a snapshot reuses exactly what Refresh
    /// read (§6.6).
    /// </summary>
    Task<DataSyncRefreshResult> RefreshAsync(DataSyncGateLease lease, IReadOnlyCollection<string> kinds,
        bool collectPublished, CancellationToken ct);
}

public sealed record DataSyncRefreshResult(int Changed, int Tombstoned, DataSyncActorId Actor,
    bool Skipped, IReadOnlyDictionary<(string Kind, string LocalKey), DataSyncPublishedEntity>? Published);

/// <summary>
/// What one synced entity publishes, as Refresh computed it (§3.5, §6.6). A held entity (the reader would hold it,
/// or this device cannot read it, §3.3) is served as a <c>HeldAtSource</c> record: Held is set, and Content and
/// Hash are null.
/// </summary>
/// <param name="HeldDetail">This device's own diagnostics (e.g. <c>tooManyChildren</c>); never on the wire.</param>
public sealed record DataSyncPublishedEntity(JsonObject? Content, string? Hash, int ChildrenWithheld,
    DataSyncHeldReason? Held = null, string? HeldDetail = null);

/// <summary>[C] The actor lifecycle of §5.6.</summary>
public interface IDataSyncActorGuard
{
    bool IsVerified { get; }

    /// <summary>Evidence was reported and waits for the next <see cref="CheckAsync"/>.</summary>
    bool HasPendingEvidence { get; }

    /// <summary>
    /// The actor check of §5.6 (identity, actor.json, pending evidence). Called after entering the gate and BEFORE any
    /// transaction by the apply runner, the feed, SetEntitySync and the scheduler while evidence waits. May rotate in
    /// its own short transaction and pause; returns the pause it caused.
    /// </summary>
    Task<DataSyncPauseReason?> CheckAsync(DataSyncGateLease lease, CancellationToken ct);

    /// <summary>
    /// A peer has seen a counter of one of this device's actors above what this device recorded for it (head
    /// SeenCounter or row A1). Evidence at or below the recorded counter does nothing; other evidence waits for the
    /// next CheckAsync. Evidence about an already-retired actor only raises its recorded counter and appends to
    /// RestoreEvidenceJson; it never rotates or pauses (§5.6).
    /// </summary>
    Task ReportPeerEvidenceAsync(string peerNodeId, string actorId, long seenCounter, CancellationToken ct);

    /// <summary>
    /// A reader's cursor is above LastSeq (§7.5.1): local restore, handled by the next CheckAsync. Not reported again
    /// for a reader already recorded in RestoreEvidenceJson until it has once read with every cursor ≤ LastSeq.
    /// </summary>
    Task ReportReaderAheadAsync(string readerNodeId, CancellationToken ct);

    /// <summary>A fetch cycle asked every Active link's peer (§5.6); the guard's own fallback is 2 minutes.</summary>
    void MarkVerified();
}

/// <summary>
/// Thrown by Refresh inside a transaction when the actor is no longer current (§5.6): the device identity was reset
/// after the caller's check. The caller rolls back; the apply runner checks again and retries once, a request answers
/// Busy.
/// </summary>
public sealed class DataSyncActorChangedException() : Exception("actorChanged");

/// <summary>[C] Runs inside a BTask body; owns gate, transaction(s), caches, history (§8.10).</summary>
public interface IDataSyncApplyRunner
{
    /// <summary>
    /// pull == null: re-merge this link's pending records only. <paramref name="choices"/>: a first sync's Start, what
    /// the person chose in its preview (§8.3).
    /// </summary>
    Task<DataSyncAutoSyncOutcome> RunAutoSyncAsync(int linkId, DataSyncStagedPull? pull, BTaskArgs args,
        IReadOnlyList<DataSyncFirstSyncChoice>? choices = null);

    Task<int?> RunResolutionsAsync(IReadOnlyList<DataSyncResolveInput> resolutions, DataSyncApplyOptions options,
        BTaskArgs args);

    Task<int?> RunUndoAsync(int applyLogId, BTaskArgs args);

    /// <summary>linkId: a restore suspected through one link only (§5.6); null: every link.</summary>
    Task<int?> RunRestoreAsync(DataSyncRestoreChoice choice, int? linkId, BTaskArgs args);
}

public sealed record DataSyncApplyOptions(bool BackupBeforeDestructive);

/// <summary>
/// Attempt records for one-shot tasks (§8.10.1) [C]. E registers an attempt when it enqueues a task and cancels
/// through it.
/// </summary>
public interface IDataSyncTaskRegistry
{
    /// <summary>A new attempt id; replaces a finished one.</summary>
    DataSyncTaskAttempt Register(string taskId);

    /// <summary>Interlocked; false when unknown.</summary>
    bool RequestCancel(string taskId);

    /// <summary>Flag clear and this attempt current.</summary>
    bool ShouldRun(string taskId, Guid attemptId);
}

public sealed record DataSyncTaskAttempt(string TaskId, Guid AttemptId);

/// <summary>
/// Where data sync keeps files (§4.7). Default [C] (TryAddSingleton): {AppData}/data-sync and {AppData}/backups.
/// TestKit [C] (AddSingleton, so it wins): {testDir}/data-sync and {testDir}/backups — AppService's data directory
/// is static per process, so tests must never share these paths.
/// </summary>
public interface IDataSyncDataDirectory
{
    string Path { get; }
    string BackupsPath { get; }
}

/// <param name="End">How the apply ended: only a committed apply recorded anything on the link.</param>
/// <param name="FirstSync">The apply completed the link's first contact: it is the first sync with the peer (§8.3).</param>
public sealed record DataSyncAutoSyncOutcome(int? ApplyLogId, DataSyncPauseReason? Paused, int NewInboxItems,
    int ClosedInboxItems, int Applied, IReadOnlyList<DataSyncMergeNote> Notes, IReadOnlyList<long> ClosedItemIds,
    DataSyncAutoSyncEnd End, bool FirstSync = false);

/// <summary>How one auto-sync apply of a link ended (§8.10.2).</summary>
public enum DataSyncAutoSyncEnd
{
    /// <summary>Its final transaction committed: the pull, or the re-merge, was applied.</summary>
    Committed = 1,

    /// <summary>
    /// Nothing was applied and nothing recorded: the attempt ended, the link was paused, stopped or gone, or the actor
    /// was unverified or changed under every try (§5.6). The pull and a requested re-merge wait for the next run.
    /// </summary>
    NotApplied = 2,
}

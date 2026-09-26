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

    /// <summary>
    /// The actor check of §5.6 (identity, actor.json). Called after entering the gate and BEFORE any transaction by
    /// the apply runner, the feed and SetEntitySync. May rotate in its own short transaction and pause; returns the
    /// pause it caused.
    /// </summary>
    Task<DataSyncPauseReason?> CheckAsync(DataSyncGateLease lease, CancellationToken ct);

    /// <summary>
    /// A peer has seen a counter of one of this device's actors above what this device recorded for it (head
    /// SeenCounter or row A1). Evidence at or below the recorded counter does nothing. Evidence about an
    /// already-retired actor only raises its recorded counter and appends to RestoreEvidenceJson; it never rotates
    /// or pauses (§5.6).
    /// </summary>
    Task ReportPeerEvidenceAsync(string peerNodeId, string actorId, long seenCounter, CancellationToken ct);

    /// <summary>
    /// A reader's cursor is above LastSeq (§7.5.1): local restore. Not reported again for a reader already recorded
    /// in RestoreEvidenceJson until it has once read with every cursor ≤ LastSeq.
    /// </summary>
    Task ReportReaderAheadAsync(string readerNodeId, CancellationToken ct);

    /// <summary>Every Active link answered one head, or 2 minutes passed since start.</summary>
    void MarkVerified();
}

/// <summary>
/// Thrown by Refresh inside a transaction when the actor is no longer current (§5.6). The runner rolls back, calls
/// CheckAsync and retries once.
/// </summary>
public sealed class DataSyncActorChangedException() : Exception("actorChanged");

/// <summary>[C] Runs inside a BTask body; owns gate, transaction(s), re-plan, caches, history (§8.10).</summary>
public interface IDataSyncApplyRunner
{
    /// <summary>pull == null: re-merge this link's pending records only (once flags, retries).</summary>
    Task<DataSyncAutoSyncOutcome> RunAutoSyncAsync(DataSyncLinkContext link, DataSyncStagedPull? pull, BTaskArgs args);

    Task<int?> RunReviewAsync(string reviewId, IReadOnlyList<DataSyncPlanDecision> decisions,
        DataSyncApplyOptions options, BTaskArgs args);

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

/// <summary>
/// Staged first-link reviews and copy-once pulls [C], in memory: one per link waiting for its review, never evicted for
/// another (§8.3: a review the person is reading is never replaced by a cycle); 60 min idle TTL; lost on restart.
/// </summary>
public interface IDataSyncReviewStore
{
    /// <summary>The link's current review, unless it expired. Reading it counts as access: its idle time restarts.</summary>
    DataSyncReviewEntry? GetForLink(int linkId);

    /// <summary>
    /// <see cref="GetForLink"/> without counting as access: what the fetch cycle and the status views look at, so a
    /// review nobody reads still idles out (§8.3). A cycle stages a new one only when this returns null.
    /// </summary>
    DataSyncReviewEntry? PeekForLink(int linkId);

    DataSyncReviewEntry Stage(int? linkId, bool copyOnce, DataSyncStagedPull pull);

    /// <summary>Sliding TTL; Applying never expires.</summary>
    DataSyncReviewEntry? Get(string reviewId);

    void SetLastPlan(string reviewId, DataSyncPlan plan);
    void MarkApplying(string reviewId, string taskId);
    void MarkApplied(string reviewId, int applyLogId);

    /// <summary>
    /// The apply attempt ended without applying (failed, stopped, or exited early): the review is no longer applying,
    /// so it expires and can be replaced again. Nothing happens to an applied or unknown review.
    /// </summary>
    void MarkApplyEnded(string reviewId);

    void Discard(string reviewId);
}

public sealed record DataSyncReviewEntry(string ReviewId, int? LinkId, bool CopyOnce, DataSyncStagedPull Pull,
    DataSyncPlan? LastPlan, string? TaskId, int? ApplyLogId, DateTime LastAccessUtc, bool Notified);

/// <summary>
/// Pulls fetched by the DataSync task and waiting for the DataSyncApply task [E], in memory: one per link (a newer
/// one replaces it), MaxStagedPullBytes each, 256 MiB in total (the oldest is dropped and refetched), lost on
/// restart.
/// </summary>
public interface IDataSyncStagedPullStore
{
    void Put(int linkId, DataSyncStagedPull pull);
    DataSyncStagedPull? Take(int linkId);

    /// <summary>The fetch half skips a refetch while this is still current (§8.10.2).</summary>
    DataSyncStagedPull? Peek(int linkId);

    IReadOnlyList<int> LinksWaiting();
}

/// <param name="End">
/// How the apply ended, which decides what the apply task records on the link: only a committed apply is recorded as
/// synced and consumes the link's once flags and first contact.
/// </param>
public sealed record DataSyncAutoSyncOutcome(int? ApplyLogId, DataSyncPauseReason? Paused, int NewInboxItems,
    int ClosedInboxItems, int Applied, IReadOnlyList<DataSyncMergeNote> Notes, IReadOnlyList<long> ClosedItemIds,
    DataSyncAutoSyncEnd End);

/// <summary>How one auto-sync apply of a link ended (§8.10.2).</summary>
public enum DataSyncAutoSyncEnd
{
    /// <summary>Its final transaction committed: the pull, or the re-merge, was applied (a breaker's pause included).</summary>
    Committed = 1,

    /// <summary>
    /// Nothing was applied and nothing recorded: the attempt ended, the link was paused, stopped or gone, or the actor
    /// was unverified or changed under every try (§5.6). The pull and a requested re-merge wait for the next run.
    /// </summary>
    NotApplied = 2,

    /// <summary>
    /// It failed and rolled back: the runner recorded <c>ApplyFailed</c> and a backoff on the link, and the pull is
    /// dropped (committed chunks stand, the cursor did not move).
    /// </summary>
    Failed = 3,
}

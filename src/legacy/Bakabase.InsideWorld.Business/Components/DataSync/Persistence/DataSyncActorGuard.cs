using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>One entry of <c>RestoreEvidenceJson</c> (§4.1): what made this device think its data was restored (§5.6).</summary>
/// <param name="Source"><see cref="Watermark"/>, <see cref="Reader"/> or <see cref="Peer"/>.</param>
/// <param name="Settled">
/// Reader entries only: the reader has since read with every cursor ≤ <c>LastSeq</c> (§7.5.1). Until then it is not
/// reported again.
/// </param>
public sealed record DataSyncRestoreEvidence(string Source, string? NodeId, string? Name, string? ActorId,
    long? Counter, DateTime At, bool? Settled = null)
{
    /// <summary>actor.json was ahead of the database: "this device's own records".</summary>
    public const string Watermark = "watermark";

    /// <summary>A reader had seen sequence numbers this database never issued.</summary>
    public const string Reader = "reader";

    /// <summary>A peer had seen counters of this device's actor above what this device issued.</summary>
    public const string Peer = "peer";

    public static IReadOnlyList<DataSyncRestoreEvidence> Read(string? json) =>
        string.IsNullOrEmpty(json) ? [] : DataSyncStoredJson.Read<List<DataSyncRestoreEvidence>>(json, "RestoreEvidenceJson");

    public static string? Write(IReadOnlyCollection<DataSyncRestoreEvidence> evidence) =>
        evidence.Count == 0 ? null : DataSyncStoredJson.Write(evidence);
}

/// <summary>A restore the guard detected or escalated (§5.6, §8.7 B6), announced after its commit.</summary>
/// <param name="LinkId">The one link paused while a restore is only suspected; null when every link paused.</param>
/// <param name="Escalated">A suspected restore became a detected one: the same restore, not a new one.</param>
public sealed record DataSyncRestoreDetection(DataSyncPauseReason Reason, int? LinkId, string Detail, bool Escalated);

/// <summary>
/// The actor lifecycle of §5.6: which actor this device issues counters under, when it must rotate, and what a
/// restore or a copy pauses.
/// </summary>
/// <remarks>
/// <para>
/// A rotation always runs in its own short transaction on its own scope, and writes <c>actor.json</c> after its
/// commit, before anything continues. The guard never waits for the data sync gate: <see cref="CheckAsync"/> runs
/// under the caller's lease, and evidence arrives both from under the gate (the apply runner after row A1) and from
/// outside it (the fetch half's head, the feed's reader check). Evidence is therefore <b>pending</b> from the moment
/// it is reported until it is handled: meanwhile <see cref="IsVerified"/> is false, so no Refresh issues a counter
/// under an actor that is about to retire. Evidence that fails to be handled stays pending, and the next
/// <see cref="CheckAsync"/> handles it.
/// </para>
/// <para>
/// Nothing holding an open transaction ever waits for the guard's own lock (Refresh only reads
/// <see cref="IsVerified"/>), so the guard's transaction may wait for SQLite's writer, never the other way round.
/// </para>
/// <para>
/// Evidence about an actor that is already retired only raises its recorded counter and is appended: it never
/// rotates, so one restore never rotates or pauses twice (gate fix B1(a)). While a restore is only suspected through
/// one link, a second peer, a reader or the watermark corroborates it and every link pauses (§5.6, product must-fix
/// 19); that escalates the pending restore and is not a new one.
/// </para>
/// </remarks>
public sealed class DataSyncActorGuard : IDataSyncActorGuard
{
    /// <summary>After a start, the actor is verified once every Active link's peer answered a head, or after this.</summary>
    public static readonly TimeSpan VerificationWindow = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly DataSyncActorWatermarkFile _watermark;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly DateTimeOffset _startedAt;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _pendingLock = new();
    private readonly List<PendingEvidence> _pending = [];
    private volatile bool _verified;

    public DataSyncActorGuard(IServiceScopeFactory scopes, DataSyncActorWatermarkFile watermark,
        TimeProvider? time = null, ILogger<DataSyncActorGuard>? logger = null)
    {
        _scopes = scopes;
        _watermark = watermark;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? (ILogger) NullLogger.Instance;
        _startedAt = _time.GetUtcNow();
    }

    /// <summary>Raised after a restore was detected or escalated (the notifier sends one notification in total, §8.7).</summary>
    public event EventHandler<DataSyncRestoreDetection>? RestoreDetected;

    /// <summary>Startup verification is done and no evidence waits to be handled (§5.6). Refresh is skipped otherwise.</summary>
    public bool IsVerified => IsStartupVerified && !HasPendingEvidence;

    /// <summary>Every Active link answered a head, there was none, or <see cref="VerificationWindow"/> passed.</summary>
    public bool IsStartupVerified => _verified || _time.GetUtcNow() - _startedAt >= VerificationWindow;

    /// <summary>Evidence was reported and is not handled yet; <see cref="CheckAsync"/> handles it.</summary>
    public bool HasPendingEvidence
    {
        get
        {
            lock (_pendingLock) return _pending.Count > 0;
        }
    }

    public void MarkVerified() => _verified = true;

    /// <summary>
    /// The check of §5.6, under the caller's lease and before any transaction: creates the local state row on first
    /// use; rotates when the device identity changed (a deliberate reset: nothing pauses) or <c>actor.json</c> is
    /// ahead of the row (a restore: every Active link pauses <c>LocalRestoreDetected</c>); handles pending evidence;
    /// keeps <c>actor.json</c> in step; verifies at once when no link is Active. Returns the strongest pause it caused.
    /// </summary>
    public async Task<DataSyncPauseReason?> CheckAsync(DataSyncGateLease lease, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.IsHeld) throw new InvalidOperationException("The actor check runs under the data sync gate (§5.6).");
        var detections = new List<DataSyncRestoreDetection>();
        DataSyncPauseReason? pause;
        await _lock.WaitAsync(ct);
        try
        {
            pause = await CheckIdentityAndWatermarkLockedAsync(detections, ct);
            pause = Strongest(pause, await HandlePendingLockedAsync(detections, ct));
        }
        finally
        {
            _lock.Release();
        }

        Announce(detections);
        return pause;
    }

    /// <summary>
    /// Head <c>SeenCounter</c> or row A1 (§5.6): a peer has seen <paramref name="seenCounter"/> of this device's
    /// <paramref name="actorId"/>. At or below what is recorded for that actor it does nothing; about the current
    /// actor it rotates and pauses that link (<c>LocalRestoreSuspected</c>), or every link when corroborated; about a
    /// retired actor it only raises the recorded counter.
    /// </summary>
    public async Task ReportPeerEvidenceAsync(string peerNodeId, string actorId, long seenCounter, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(peerNodeId);
        if (!DataSyncActorId.IsValid(actorId) || seenCounter < 1) return;
        // Heads carry SeenCounter every few seconds; only news is worth making the actor unverified for.
        var state = await ReadStateAsync(ct);
        if (state is null || !IsAboveRecorded(state, actorId, seenCounter)) return;
        await ReportAsync(new PendingEvidence(DataSyncRestoreEvidence.Peer, peerNodeId, actorId, seenCounter), ct);
    }

    /// <summary>
    /// A reader's cursor is above <c>LastSeq</c> (§7.5.1): this database lost sequence numbers the reader saw. Rotates
    /// and pauses every link, unless a restore is already pending (then it corroborates it). A reader already recorded
    /// is not reported again until <see cref="NoteReaderInStepAsync"/> settles it (gate fix B1(b)).
    /// </summary>
    public async Task ReportReaderAheadAsync(string readerNodeId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(readerNodeId);
        var state = await ReadStateAsync(ct);
        if (state is not null && IsRecordedReader(state, readerNodeId)) return;
        await ReportAsync(new PendingEvidence(DataSyncRestoreEvidence.Reader, readerNodeId, null, null), ct);
    }

    /// <summary>
    /// Whether <paramref name="readerNodeId"/> is a recorded reader-ahead that has not read in step since: the feed
    /// keeps answering it <c>CursorSuperseded</c> for every kind (§7.5.1 step 1).
    /// </summary>
    public async Task<bool> IsReaderAheadRecordedAsync(string readerNodeId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(readerNodeId);
        var state = await ReadStateAsync(ct);
        return state is not null && IsRecordedReader(state, readerNodeId);
    }

    /// <summary>
    /// A recorded reader-ahead read with every cursor ≤ <c>LastSeq</c> (§7.5.1): its entries are settled, and the next
    /// time it reads ahead is a new report. Returns whether anything was settled. Never call it with a transaction open.
    /// </summary>
    public async Task<bool> NoteReaderInStepAsync(string readerNodeId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(readerNodeId);
        var current = await ReadStateAsync(ct);
        if (current is null || !IsRecordedReader(current, readerNodeId)) return false;
        await _lock.WaitAsync(ct);
        try
        {
            return await InOwnTransactionAsync(async (store, db) =>
            {
                var state = await store.LoadStateAsync(ct);
                if (state is null) return false;
                var evidence = DataSyncRestoreEvidence.Read(state.RestoreEvidenceJson).ToList();
                var changed = false;
                for (var i = 0; i < evidence.Count; i++)
                {
                    if (!IsUnsettledReader(evidence[i], readerNodeId)) continue;
                    evidence[i] = evidence[i] with {Settled = true};
                    changed = true;
                }

                if (!changed) return false;
                state.RestoreEvidenceJson = DataSyncRestoreEvidence.Write(evidence);
                state.UpdatedAtUtc = UtcNow;
                await db.SaveChangesAsync(ct);
                return true;
            }, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    #region Identity and watermark

    private async Task<DataSyncPauseReason?> CheckIdentityAndWatermarkLockedAsync(
        List<DataSyncRestoreDetection> detections, CancellationToken ct)
    {
        var device = await GetDeviceAsync(ct);
        var file = _watermark.Read();
        if (file.Problem is { } problem)
            _logger.LogWarning("The data sync watermark {Path} is unreadable ({Problem}); it is written again.",
                _watermark.FilePath, problem);

        // The usual answer is "nothing to do": it is found without a transaction, so a head every few seconds never
        // takes SQLite's writer for it.
        var current = await ReadStateAsync(ct);
        if (current is not null && !NeedsWrite(current, device, file.Watermark))
        {
            if (file.Watermark != DataSyncActorWatermark.Of(current)) await _watermark.WriteAsync(current, ct);
            if (!_verified && !await AnyActiveLinkAsync(ct)) MarkVerified();
            return null;
        }

        var (state, rotated, pause, active) = await InOwnTransactionAsync(async (store, db) =>
        {
            var now = UtcNow;
            var row = await store.LoadStateAsync(ct);
            var created = row is null;
            if (row is null)
            {
                row = DataSyncLocalStateRows.New(device, now);
                db.DataSyncLocalStates.Add(row);
            }

            // A row created here with a watermark on disk is a database that lost its data sync state behind this
            // device's back: its instance id differs, so it reads as ahead like any restore.
            var ahead = file.Watermark is { } w && w.IsAheadOf(row) ? w : null;
            var identityChanged = !string.Equals(row.NodeId, device.NodeId, StringComparison.Ordinal) ||
                                  !string.Equals(row.LibraryEpoch, device.LibraryEpoch, StringComparison.Ordinal);
            DataSyncPauseReason? caused = null;
            var rotate = identityChanged || ahead is not null;
            if (rotate) Rotate(row, device, ahead, 0);
            if (ahead is not null)
            {
                var evidence = DataSyncRestoreEvidence.Read(row.RestoreEvidenceJson).ToList();
                evidence.Add(new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Watermark, null, null, ahead.ActorId,
                    ahead.Counter, now));
                row.RestoreEvidenceJson = DataSyncRestoreEvidence.Write(evidence);
                caused = await DetectAsync(db, row, DataSyncPauseReason.LocalRestoreDetected, null, now, detections, ct);
            }

            if (created || rotate) row.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            var anyActive = await db.DataSyncLinks.AnyAsync(l => l.State == DataSyncLinkState.Active, ct);
            return (row, created || rotate, caused, anyActive);
        }, ct);

        if (rotated || file.Watermark != DataSyncActorWatermark.Of(state))
            await _watermark.WriteAsync(state, ct);
        // With no Active link there is no peer whose head could tell (§5.6); a device with readers only is the
        // accepted residual case.
        if (!active) MarkVerified();
        if (rotated)
            _logger.LogInformation("Data sync now issues revisions as actor {Actor} (generation {Generation}).",
                state.ActorId, state.ActorGeneration);
        return pause;
    }

    /// <summary>The row must be written: the identity changed, or the watermark is ahead of it (§5.6).</summary>
    private static bool NeedsWrite(DataSyncLocalStateDbModel state, DataSyncDevice device, DataSyncActorWatermark? file) =>
        !string.Equals(state.NodeId, device.NodeId, StringComparison.Ordinal) ||
        !string.Equals(state.LibraryEpoch, device.LibraryEpoch, StringComparison.Ordinal) ||
        file?.IsAheadOf(state) == true;

    private async Task<bool> AnyActiveLinkAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().DataSyncLinks
            .AnyAsync(l => l.State == DataSyncLinkState.Active, ct);
    }

    #endregion

    #region Evidence

    private async Task ReportAsync(PendingEvidence evidence, CancellationToken ct)
    {
        lock (_pendingLock) _pending.Add(evidence);
        var detections = new List<DataSyncRestoreDetection>();
        await _lock.WaitAsync(ct);
        try
        {
            await HandlePendingLockedAsync(detections, ct);
        }
        finally
        {
            _lock.Release();
        }

        Announce(detections);
    }

    /// <summary>Handles every pending report in order, each in its own transaction; a failure leaves the rest pending.</summary>
    private async Task<DataSyncPauseReason?> HandlePendingLockedAsync(List<DataSyncRestoreDetection> detections,
        CancellationToken ct)
    {
        DataSyncPauseReason? strongest = null;
        while (true)
        {
            PendingEvidence? next;
            lock (_pendingLock) next = _pending.Count > 0 ? _pending[0] : null;
            if (next is null) return strongest;

            var device = await GetDeviceAsync(ct);
            var (state, rotated, pause) = await InOwnTransactionAsync(
                (store, db) => next.Source == DataSyncRestoreEvidence.Peer
                    ? HandlePeerAsync(store, db, device, next, detections, ct)
                    : HandleReaderAsync(store, db, device, next, detections, ct), ct);
            if (rotated && state is not null) await _watermark.WriteAsync(state, ct);
            lock (_pendingLock) _pending.Remove(next);
            strongest = Strongest(strongest, pause);
        }
    }

    private async Task<(DataSyncLocalStateDbModel? State, bool Rotated, DataSyncPauseReason? Pause)> HandlePeerAsync(
        DataSyncStore store, BakabaseDbContext db, DataSyncDevice device, PendingEvidence e,
        List<DataSyncRestoreDetection> detections, CancellationToken ct)
    {
        var state = await store.LoadStateAsync(ct);
        if (state is null || !IsAboveRecorded(state, e.ActorId!, e.Counter!.Value)) return (state, false, null);

        var now = UtcNow;
        var link = await db.DataSyncLinks.SingleOrDefaultAsync(l => l.PeerNodeId == e.NodeId, ct);
        var evidence = DataSyncRestoreEvidence.Read(state.RestoreEvidenceJson).ToList();
        evidence.Add(new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Peer, e.NodeId,
            await NameOfAsync(db, e.NodeId, link), e.ActorId, e.Counter, now));
        state.RestoreEvidenceJson = DataSyncRestoreEvidence.Write(evidence);
        state.UpdatedAtUtc = now;

        DataSyncPauseReason? pause;
        var rotated = false;
        if (e.ActorId != state.ActorId)
        {
            // A retired actor: the counters it issued and lost are history after the first detection (B1(a)).
            var retired = new Dictionary<string, long>(
                DataSyncStoredJson.ReadCounters(state.RetiredActorsJson, "RetiredActorsJson"), StringComparer.Ordinal)
            {
                [e.ActorId!] = e.Counter!.Value,
            };
            state.RetiredActorsJson = DataSyncStoredJson.WriteCounters(retired);
            pause = state.RestoreReason == DataSyncPauseReason.LocalRestoreSuspected && state.RestoreLinkId != link?.Id
                ? await DetectAsync(db, state, DataSyncPauseReason.LocalRestoreDetected, null, now, detections, ct)
                : null;
        }
        else
        {
            Rotate(state, device, null, e.Counter!.Value);
            rotated = true;
            // One peer alone pauses only its own link; with a restore already pending (or no link to pause) it is
            // corroboration, and every link pauses.
            pause = state.RestoreReason is null && link is not null
                ? await DetectAsync(db, state, DataSyncPauseReason.LocalRestoreSuspected, link, now, detections, ct)
                : await DetectAsync(db, state, DataSyncPauseReason.LocalRestoreDetected, null, now, detections, ct);
        }

        await db.SaveChangesAsync(ct);
        return (state, rotated, pause);
    }

    private async Task<(DataSyncLocalStateDbModel? State, bool Rotated, DataSyncPauseReason? Pause)> HandleReaderAsync(
        DataSyncStore store, BakabaseDbContext db, DataSyncDevice device, PendingEvidence e,
        List<DataSyncRestoreDetection> detections, CancellationToken ct)
    {
        var now = UtcNow;
        var state = await store.LoadStateAsync(ct);
        var rotated = false;
        if (state is null)
        {
            // Nothing issued anything here yet: a fresh actor needs no rotation.
            state = DataSyncLocalStateRows.New(device, now);
            db.DataSyncLocalStates.Add(state);
            rotated = true;
        }
        else if (IsRecordedReader(state, e.NodeId))
        {
            return (state, false, null);
        }

        var link = await db.DataSyncLinks.SingleOrDefaultAsync(l => l.PeerNodeId == e.NodeId, ct);
        var evidence = DataSyncRestoreEvidence.Read(state.RestoreEvidenceJson).ToList();
        evidence.Add(new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Reader, e.NodeId,
            await NameOfAsync(db, e.NodeId, link), null, null, now));
        state.RestoreEvidenceJson = DataSyncRestoreEvidence.Write(evidence);
        state.UpdatedAtUtc = now;

        DataSyncPauseReason? pause;
        if (state.RestoreReason is not null)
        {
            // The restore this reader tells of is already known; while it is only suspected, this corroborates it.
            pause = state.RestoreReason == DataSyncPauseReason.LocalRestoreSuspected
                ? await DetectAsync(db, state, DataSyncPauseReason.LocalRestoreDetected, null, now, detections, ct)
                : null;
        }
        else
        {
            if (!rotated)
            {
                Rotate(state, device, null, 0);
                rotated = true;
            }

            pause = await DetectAsync(db, state, DataSyncPauseReason.LocalRestoreDetected, null, now, detections, ct);
        }

        await db.SaveChangesAsync(ct);
        return (state, rotated, pause);
    }

    #endregion

    #region Rotation and pauses

    /// <summary>
    /// §5.6 rotation: the current actor retires with recorded counter = max(ActorCounter, what actor.json says of it,
    /// the evidence's counter); an actor the watermark names that this row never knew (lost with a restore) is recorded
    /// too; a new random salt derives the new actor, whose counter starts at 0; the generation moves past both the row's
    /// and the watermark's, so it never repeats one the file has seen.
    /// </summary>
    private static void Rotate(DataSyncLocalStateDbModel state, DataSyncDevice device, DataSyncActorWatermark? ahead,
        long evidenceCounter)
    {
        var retired = new Dictionary<string, long>(
            DataSyncStoredJson.ReadCounters(state.RetiredActorsJson, "RetiredActorsJson"), StringComparer.Ordinal);
        var old = state.ActorId;
        var recorded = Math.Max(Math.Max(state.ActorCounter, evidenceCounter), retired.GetValueOrDefault(old));
        if (ahead is not null && ahead.ActorId == old) recorded = Math.Max(recorded, ahead.Counter);
        // An actor that never issued a counter has nothing to remember.
        if (recorded > 0) retired[old] = recorded;
        if (ahead is not null && ahead.ActorId != old && ahead.Counter > 0)
            retired[ahead.ActorId] = Math.Max(retired.GetValueOrDefault(ahead.ActorId), ahead.Counter);

        string salt;
        DataSyncActorId actor;
        do
        {
            salt = DataSyncActorId.NewSalt();
            actor = DataSyncActorId.Derive(device.NodeId, device.LibraryEpoch, salt);
        } while (actor.Value == old || retired.ContainsKey(actor.Value));

        state.NodeId = device.NodeId;
        state.LibraryEpoch = device.LibraryEpoch;
        state.ActorSalt = salt;
        state.ActorId = actor.Value;
        state.ActorCounter = 0;
        state.ActorGeneration = Math.Max(state.ActorGeneration, ahead?.Generation ?? 0) + 1;
        state.RetiredActorsJson = DataSyncStoredJson.WriteCounters(retired);
    }

    /// <summary>
    /// Records a restore and pauses (§5.6, §9.5). Detected: every Active link (and the one a suspected restore paused)
    /// pauses <c>LocalRestoreDetected</c>. Suspected: only <paramref name="link"/> pauses, and only when no restore is
    /// pending yet. A link paused for another reason (by the person, a breaker) keeps its reason, so resuming after the
    /// restore choice never resumes it.
    /// </summary>
    private static async Task<DataSyncPauseReason?> DetectAsync(BakabaseDbContext db, DataSyncLocalStateDbModel state,
        DataSyncPauseReason reason, DataSyncLinkDbModel? link, DateTime now, List<DataSyncRestoreDetection> detections,
        CancellationToken ct)
    {
        var escalated = state.RestoreReason == DataSyncPauseReason.LocalRestoreSuspected &&
                        reason == DataSyncPauseReason.LocalRestoreDetected;
        if (state.RestoreReason == DataSyncPauseReason.LocalRestoreDetected) reason = state.RestoreReason.Value;
        if (state.RestoreReason is null) state.RestoreDetectedAtUtc = now;
        state.RestoreReason = reason;
        state.RestoreLinkId = reason == DataSyncPauseReason.LocalRestoreSuspected ? link?.Id : null;
        state.RestoreDetail = DetailOf(state);
        state.UpdatedAtUtc = now;

        var paused = new List<DataSyncLinkDbModel>();
        if (reason == DataSyncPauseReason.LocalRestoreSuspected)
        {
            if (link is {State: DataSyncLinkState.Active}) paused.Add(link);
        }
        else
        {
            paused.AddRange(await db.DataSyncLinks
                .Where(l => l.State == DataSyncLinkState.Active ||
                            (l.State == DataSyncLinkState.Paused &&
                             l.PausedReason == DataSyncPauseReason.LocalRestoreSuspected))
                .ToListAsync(ct));
        }

        foreach (var l in paused)
        {
            l.State = DataSyncLinkState.Paused;
            l.PausedReason = reason;
            l.PausedDetail = state.RestoreDetail;
            l.UpdatedAtUtc = now;
        }

        detections.Add(new DataSyncRestoreDetection(reason, state.RestoreLinkId, state.RestoreDetail!, escalated));
        return reason;
    }

    /// <summary>A short machine string naming the evidence of the pending restore, e.g. <c>evidence=peer+reader</c>.</summary>
    private static string DetailOf(DataSyncLocalStateDbModel state)
    {
        var since = state.RestoreDetectedAtUtc ?? DateTime.MinValue;
        var sources = DataSyncRestoreEvidence.Read(state.RestoreEvidenceJson)
            .Where(e => e.At >= since)
            .Select(e => e.Source)
            .ToHashSet(StringComparer.Ordinal);
        var ordered = new[] {DataSyncRestoreEvidence.Watermark, DataSyncRestoreEvidence.Peer, DataSyncRestoreEvidence.Reader}
            .Where(sources.Contains);
        return "evidence=" + string.Join('+', ordered);
    }

    private static DataSyncPauseReason? Strongest(DataSyncPauseReason? a, DataSyncPauseReason? b) =>
        a == DataSyncPauseReason.LocalRestoreDetected || b == DataSyncPauseReason.LocalRestoreDetected
            ? DataSyncPauseReason.LocalRestoreDetected
            : a ?? b;

    #endregion

    #region Helpers

    /// <summary>
    /// Whether a peer's counter of <paramref name="actorId"/> is news: above <c>ActorCounter</c> for the current
    /// actor, above the recorded counter for a retired one. Another device's actor is never news.
    /// </summary>
    private static bool IsAboveRecorded(DataSyncLocalStateDbModel state, string actorId, long counter)
    {
        if (actorId == state.ActorId) return counter > state.ActorCounter;
        var retired = DataSyncStoredJson.ReadCounters(state.RetiredActorsJson, "RetiredActorsJson");
        return retired.TryGetValue(actorId, out var recorded) && counter > recorded;
    }

    private static bool IsRecordedReader(DataSyncLocalStateDbModel state, string readerNodeId) =>
        DataSyncRestoreEvidence.Read(state.RestoreEvidenceJson).Any(e => IsUnsettledReader(e, readerNodeId));

    private static bool IsUnsettledReader(DataSyncRestoreEvidence e, string readerNodeId) =>
        e.Source == DataSyncRestoreEvidence.Reader && e.NodeId == readerNodeId && e.Settled != true;

    private static async Task<string?> NameOfAsync(BakabaseDbContext db, string? nodeId, DataSyncLinkDbModel? link)
    {
        if (link is not null) return link.PeerName;
        if (nodeId is null) return null;
        return await db.DataSyncReaders.AsNoTracking().Where(r => r.NodeId == nodeId).Select(r => r.Name)
            .SingleOrDefaultAsync();
    }

    private void Announce(List<DataSyncRestoreDetection> detections)
    {
        foreach (var detection in detections)
        {
            _logger.LogWarning("Data sync paused: {Reason} ({Detail}).", detection.Reason, detection.Detail);
            try
            {
                RestoreDetected?.Invoke(this, detection);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "A restore detection handler failed.");
            }
        }
    }

    private async Task<DataSyncLocalStateDbModel?> ReadStateAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        return await db.DataSyncLocalStates.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == DataSyncLocalStateRows.SingletonId, ct);
    }

    private async Task<DataSyncDevice> GetDeviceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(ct);
    }

    /// <summary>Runs <paramref name="work"/> in its own scope and its own short transaction, and commits.</summary>
    private async Task<T> InOwnTransactionAsync<T>(Func<DataSyncStore, BakabaseDbContext, Task<T>> work,
        CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<DataSyncStore>();
        var db = store.Db;
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The actor guard runs in its own transaction (§5.6).");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await work(store, db);
        await transaction.CommitAsync(ct);
        return result;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private sealed record PendingEvidence(string Source, string NodeId, string? ActorId, long? Counter);

    #endregion
}

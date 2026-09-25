using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

// §8.10.2, the apply half of one cycle for one link.
public sealed partial class DataSyncApplyRunner
{
    /// <summary>The ApplyFailed error code a link carries after an apply that rolled back (§8.10.2).</summary>
    public const string ApplyFailedCode = "ApplyFailed";

    private static readonly DataSyncAutoSyncOutcome Nothing = new(null, null, 0, 0, 0, [], []);

    /// <summary>
    /// Applies a staged pull (or, with <paramref name="pull"/> null, re-merges the link's pending records) (§8.10.2):
    /// Refresh, merge, anomalies and breakers, the writes in chunks, bases, order, the inbox, the cursor last. A
    /// regression rolls everything back and reports the evidence outside any transaction; the link then pauses, or —
    /// when only a retired actor's recorded counter rose — is merged once more. A failure is recorded on the link
    /// (<see cref="ApplyFailedCode"/>, a backoff) and the pull is dropped: committed chunks stand, the cursor did not
    /// move, and re-sent records meet row K4.
    /// </summary>
    public async Task<DataSyncAutoSyncOutcome> RunAutoSyncAsync(DataSyncLinkContext link, DataSyncStagedPull? pull,
        BTaskArgs args)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(args);
        var ct = args.CancellationToken;
        if (!await StartAsync(args)) return Nothing;
        await WaitStartupVerifiedAsync(ct);
        using var lease = await _gate.EnterAsync(null, ct);
        if (!MayRun(args)) return Nothing;
        // A link the check paused stops here (the link row says so below).
        await _guard.CheckAsync(lease, ct);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var (outcome, retry) = await AutoSyncOnceAsync(lease, link, pull, args, ct);
                if (!retry || attempt > 0) return outcome;
            }
            catch (DataSyncActorChangedException)
            {
                await _guard.CheckAsync(lease, ct);
            }
        }

        return Nothing;
    }

    private async Task<(DataSyncAutoSyncOutcome Outcome, bool Retry)> AutoSyncOnceAsync(DataSyncGateLease lease,
        DataSyncLinkContext link, DataSyncStagedPull? pull, BTaskArgs args, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
        var openBefore = await OpenItemIdsAsync(s, ct);
        await s.BeginAsync(ct);
        try
        {
            var linkRow = await s.LinkAsync(link.LinkId, ct);
            if (linkRow is null || linkRow.State == DataSyncLinkState.Stopped)
            {
                await s.RollbackAsync();
                return (Nothing, false);
            }

            if (linkRow.State == DataSyncLinkState.Paused)
            {
                await s.RollbackAsync();
                return (Nothing with { Paused = linkRow.PausedReason }, false);
            }

            var kinds = link.Kinds.Where(s.Kinds.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
            try
            {
                await RefreshAsync(s, lease, kinds, null, ct);
            }
            catch (DataSyncActorUnverifiedException)
            {
                await s.RollbackAsync();
                return (Nothing, false);
            }

            var context = DataSyncMergeInputs.LinkContext(s, linkRow, link);
            var takesTheirs = pull is not null && pull.Kinds.Any(k => k.FullReconciliation) &&
                              _othersWinNext.ContainsKey(link.LinkId);
            if (takesTheirs) context = context with { EffectiveMode = DataSyncLinkMode.Follow };

            var pending = await PendingToMergeAsync(s, linkRow.Id, pull, ct);
            var input = await DataSyncMergeInputs.BuildAsync(s, context, pull, pending, ct);
            var result = DataSyncMerger.Merge(input);

            if (result.Anomaly is { } anomaly)
            {
                // The whole transaction rolls back, Refresh included (§5.6): nothing it issued stands under this actor.
                await s.RollbackAsync();
                if (anomaly.Code == DataSyncAnomalies.DuplicateActor)
                {
                    var reason = result.Pause ?? DataSyncPauseReason.PeerIdentityDuplicated;
                    await PauseLinkAsync(link.LinkId, reason, result.PauseDetail, ct);
                    return (Nothing with { Paused = reason }, false);
                }

                // Outside any transaction: it rotates and pauses (§5.6), or only raises a retired actor's recorded
                // counter, and then this link is merged once more.
                await _guard.ReportPeerEvidenceAsync(link.PeerNodeId, anomaly.ActorId, anomaly.SeenCounter, ct);
                await _guard.CheckAsync(lease, ct);
                var paused = await PausedReasonAsync(link.LinkId, ct);
                return paused is not null ? (Nothing with { Paused = paused }, false) : (Nothing, true);
            }

            if (result.Pause is { } pause)
            {
                linkRow.State = DataSyncLinkState.Paused;
                linkRow.PausedReason = pause;
                linkRow.PausedDetail = result.PauseDetail;
                linkRow.UpdatedAtUtc = s.Now;
                await CommitAsync(s, ct);
                return (Nothing with { Paused = pause }, false);
            }

            var recorder = new DataSyncApplyRecorder();
            var writes = new DataSyncEntityWrites(s, recorder);
            var writer = new DataSyncMergeWriter(s, writes, input, result, linkRow.Id, async c =>
            {
                await CommitAsync(s, c);
                await s.BeginAsync(c);
            });
            await writer.WriteAsync(ct);
            var written = writer.Result;

            var now = s.Now;
            var inbox = await s.Store.ReconcileInboxAsync(linkRow.Id, linkRow.PeerNodeId, written.Inbox,
                written.Evaluated, written.ClosureHints, now, ct);
            await s.Store.CloseStaleStateItemsAsync(written.Evaluated.Concat(recorder.Touched).Distinct().ToList(),
                linkRow.Id, now, ct);

            AfterPull(linkRow, context, pull, written, now);
            int? logId = null;
            if (recorder.Applied)
            {
                logId = await s.Store.AddHistoryAsync(recorder.ToLog(DataSyncHistoryKind.AutoSync, linkRow, args.Task.Id,
                    now, ElapsedMs(started)), ct);
            }

            await CommitAsync(s, ct);
            if (takesTheirs) _othersWinNext.TryRemove(link.LinkId, out _);
            await AfterCommitAsync(s, recorder, kinds, DataSyncHistoryKind.AutoSync, logId, linkRow.Id, ct);

            var closed = openBefore.Except(await OpenItemIdsAsync(s, ct)).OrderBy(i => i).ToList();
            return (new DataSyncAutoSyncOutcome(logId, null, inbox.Created, closed.Count, writer.Applied, written.Notes,
                closed), false);
        }
        catch (DataSyncActorChangedException)
        {
            await s.RollbackAsync();
            throw;
        }
        catch (OperationCanceledException)
        {
            await s.RollbackAsync();
            throw;
        }
        catch (DataSyncActorUnverifiedException)
        {
            await s.RollbackAsync();
            return (Nothing, false);
        }
        catch (Exception e)
        {
            await s.RollbackAsync();
            _logger.LogError(e, "Data sync could not apply a pull from {Peer}.", link.PeerName);
            await RecordFailureAsync(link.LinkId, e, ct);
            return (Nothing, false);
        }
    }

    /// <summary>
    /// The pending records to re-merge (§8.4): the store's conditions, and every pending record of a kind the pull
    /// reconciles in full (condition 4).
    /// </summary>
    private static async Task<IReadOnlyList<(string Kind, SyncKey Key)>> PendingToMergeAsync(DataSyncApplySession s,
        int linkId, DataSyncStagedPull? pull, CancellationToken ct)
    {
        var keys = (await s.Store.GetPendingToMergeAsync(linkId, false, ct)).ToList();
        var full = pull?.Kinds.Where(k => k.FullReconciliation).Select(k => k.Kind).ToHashSet(StringComparer.Ordinal);
        if (full is not { Count: > 0 }) return keys;
        var known = keys.ToHashSet();
        foreach (var key in await s.Store.GetPendingToMergeAsync(linkId, true, ct))
        {
            if (full.Contains(key.Kind) && known.Add(key)) keys.Add(key);
        }

        return keys;
    }

    /// <summary>
    /// The link after a pull (§8.10.2, last): cursors advanced for every kind fully evaluated, errors cleared, once flags
    /// consumed, the kinds' first contact completed and the full reconciliation noted.
    /// </summary>
    private static void AfterPull(DataSyncLinkDbModel link, DataSyncLinkContext context, DataSyncStagedPull? pull,
        DataSyncMergeResult result, DateTime now)
    {
        var cursors = new Dictionary<string, long>(DataSyncStoredJson.ReadCounters(link.CursorsJson, "CursorsJson"),
            StringComparer.Ordinal);
        foreach (var (kind, seq) in result.CursorAdvance) cursors[kind] = seq;
        link.CursorsJson = DataSyncStoredJson.WriteCounters(cursors);
        link.OnceFlagsJson = null;
        link.ConsecutiveFailures = 0;
        link.LastErrorCode = null;
        link.LastErrorDetail = null;
        link.LastAttemptAtUtc = now;
        if (pull is not null)
        {
            link.LastSyncedAtUtc = now;
            var linkKinds = context.Kinds.ToHashSet(StringComparer.Ordinal);
            var completed = DataSyncStoredJson.ReadStrings(link.FirstContactKindsJson, "FirstContactKindsJson")
                .ToHashSet(StringComparer.Ordinal);
            completed.UnionWith(pull.Kinds.Select(k => k.Kind).Where(linkKinds.Contains));
            link.FirstContactKindsJson = DataSyncStoredJson.Write(completed.OrderBy(k => k, StringComparer.Ordinal).ToList());
            if (linkKinds.All(completed.Contains)) link.FirstContactCompletedAtUtc ??= now;
            if (pull.Kinds.Count > 0 && pull.Kinds.All(k => k.FullReconciliation)) link.LastFullReconciliationAtUtc = now;
        }

        link.UpdatedAtUtc = now;
    }

    /// <summary>A breaker's pause, written in its own short transaction (§8.7).</summary>
    private async Task PauseLinkAsync(int linkId, DataSyncPauseReason reason, string? detail, CancellationToken ct)
    {
        await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
        await s.BeginAsync(ct);
        var link = await s.LinkAsync(linkId, ct);
        if (link is not null)
        {
            link.State = DataSyncLinkState.Paused;
            link.PausedReason = reason;
            link.PausedDetail = detail;
            link.UpdatedAtUtc = s.Now;
        }

        await s.CommitAsync(ct);
    }

    /// <summary>§8.10.2 catch: <c>ApplyFailed</c> with the detail, and the next attempt after a backoff.</summary>
    private async Task RecordFailureAsync(int linkId, Exception e, CancellationToken ct)
    {
        try
        {
            await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
            await s.BeginAsync(ct);
            var link = await s.LinkAsync(linkId, ct);
            if (link is not null)
            {
                var now = s.Now;
                link.ConsecutiveFailures++;
                link.LastErrorCode = ApplyFailedCode;
                link.LastErrorDetail = Truncate(e.Message, 500);
                link.LastAttemptAtUtc = now;
                link.NextAttemptAtUtc = now + BackoffAfter(link.ConsecutiveFailures);
                link.UpdatedAtUtc = now;
            }

            await s.CommitAsync(ct);
        }
        catch (Exception inner) when (inner is not OperationCanceledException)
        {
            _logger.LogError(inner, "Data sync could not record the failed apply of link {Link}.", linkId);
        }
    }

    private async Task<DataSyncPauseReason?> PausedReasonAsync(int linkId, CancellationToken ct)
    {
        await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
        var link = await s.Db.DataSyncLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId, ct);
        return link is { State: DataSyncLinkState.Paused } ? link.PausedReason ?? DataSyncPauseReason.ByUser : null;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

// §8.10.2, the apply half of one cycle for one link.
public sealed partial class DataSyncApplyRunner
{
    /// <summary>Nothing applied and nothing recorded (<see cref="DataSyncAutoSyncEnd.NotApplied"/>).</summary>
    private static readonly DataSyncAutoSyncOutcome Nothing =
        new(null, null, 0, 0, 0, [], [], DataSyncAutoSyncEnd.NotApplied);

    /// <summary>
    /// Applies a staged pull (or, with <paramref name="pull"/> null, re-merges the link's pending records) (§8.10.2), in
    /// one transaction: Refresh, merge, anomalies and breakers, the writes, bases, order, the inbox, the history entry,
    /// the link's bookkeeping (<see cref="DataSyncLinkColumns.RecordApplied"/>) and the cursor last. A regression rolls
    /// everything back and reports the evidence outside any transaction; the link then pauses, or — when only a retired
    /// actor's recorded counter rose — is merged once more. A failure rolls everything back and propagates: the task
    /// records it and backs the link off, and the cursor did not move. Once it holds the gate it looks again at what the
    /// task looked at before:
    /// "Pause all" applies nothing and answers <c>Paused = AllPaused</c>, and a link paused or stopped meanwhile applies
    /// nothing either (the link row, read in the transaction). The outcome's <see cref="DataSyncAutoSyncOutcome.End"/>
    /// says which of these happened: only <see cref="DataSyncAutoSyncEnd.Committed"/> applied anything.
    /// </summary>
    /// <remarks>
    /// With <paramref name="choices"/> it is a first sync's Start (§8.3): the ordinary merge of the snapshot the person
    /// previewed, applied only while the link still awaits it. What the preview skipped is excluded first, and a copy
    /// once — the link's mode as read in the transaction, never what it was when the person pressed Start — merges
    /// with the preview's answers to its name matches and stops the link in the same transaction.
    /// </remarks>
    public async Task<DataSyncAutoSyncOutcome> RunAutoSyncAsync(int linkId, DataSyncStagedPull? pull, BTaskArgs args,
        IReadOnlyList<DataSyncFirstSyncChoice>? choices = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var ct = args.CancellationToken;
        if (!await StartAsync(args)) return Nothing;
        await WaitStartupVerifiedAsync(ct);
        using var lease = await _gate.EnterAsync(null, ct);
        if (!MayRun(args)) return Nothing;
        // A link the check paused stops here (the link row says so below).
        await _guard.CheckAsync(lease, ct);

        // "Pause all" pressed while this apply waited for the gate (§8.7 "Other pauses"): nothing applies, and the
        // outcome says why, so the task keeps the pull for after the unpause.
        if (await IsAllPausedAsync(ct)) return Nothing with { Paused = DataSyncPauseReason.AllPaused };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var (outcome, retry) = await AutoSyncOnceAsync(lease, linkId, pull, choices, args, ct);
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
        int linkId, DataSyncStagedPull? pull, IReadOnlyList<DataSyncFirstSyncChoice>? choices, BTaskArgs args,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
        var openBefore = await OpenItemIdsAsync(s, ct);
        await s.BeginAsync(ct);
        try
        {
            var kinds = DataSyncStoredJson.ReadKinds((await s.LinkAsync(linkId, ct))?.KindsJson)
                .Where(s.Kinds.ContainsKey).ToList();
            await RefreshAsync(s, lease, kinds, null, ct);
            // Refresh tracks every entity row it read, and every later save would scan them all.
            await s.ForgetTrackedAsync(ct);
            var linkRow = await s.LinkAsync(linkId, ct);
            if (linkRow is null || linkRow.State is DataSyncLinkState.Stopped or DataSyncLinkState.Paused ||
                (choices is not null) != (linkRow.State == DataSyncLinkState.AwaitingReview))
            {
                await s.RollbackAsync();
                return (Nothing with { Paused = linkRow?.State == DataSyncLinkState.Paused ? linkRow.PausedReason : null },
                    false);
            }

            var copyOnce = choices is not null && linkRow.Mode == DataSyncLinkMode.Off;
            if (choices is not null && pull is not null) await SkipAsync(s, linkRow.Id, pull, choices, ct);
            var context = DataSyncMergeInputs.LinkContext(s, linkRow, copyOnce ? CopyOnceOf(choices!) : null);
            var takesTheirs = pull is not null && pull.Kinds.Any(k => k.FullReconciliation) &&
                              await TakesTheirsAsync(s, linkRow, ct);
            if (takesTheirs) context = context with { EffectiveMode = DataSyncLinkMode.Follow };

            var pending = (await s.Store.GetPendingAsync(linkRow.Id, ct)).Select(p => (p.Kind, p.Key)).ToList();
            var input = await DataSyncMergeInputs.BuildAsync(s, context, pull, pending, ct);
            var result = DataSyncMerger.Merge(input);

            if (result.Anomaly is { } anomaly)
            {
                // The whole transaction rolls back, Refresh included (§5.6): nothing it issued stands under this actor.
                // Outside any transaction it pauses, or only raises a retired actor's recorded counter, and then this
                // link is merged once more.
                await s.RollbackAsync();
                var paused = await HandleAnomalyAsync(lease, linkId, linkRow.PeerNodeId, anomaly, result.Pause,
                    result.PauseDetail, ct);
                return paused is not null ? (Nothing with { Paused = paused }, false) : (Nothing, true);
            }

            // A copy once asks nothing (§8.1): a name match its preview did not show — a definition made here since —
            // applies nothing, and the next preview shows it.
            if (copyOnce && result.Inbox.Any(d => d.Type == DataSyncInboxItemType.LinkSuggestion))
            {
                await s.RollbackAsync();
                return (Nothing, false);
            }

            var recorder = new DataSyncApplyRecorder();
            var writer = new DataSyncMergeWriter(s, new DataSyncEntityWrites(s, recorder), input, result, linkRow.Id);
            await writer.WriteAsync(ct);
            var written = writer.Result;

            var now = s.Now;
            var newItems = 0;
            if (copyOnce)
            {
                // A copy once ends here (§8.1): the link stops as Off stops it, and nothing waits on it.
                await s.Store.StopLinkAsync(linkRow.Id, ct);
            }
            else
            {
                newItems = (await s.Store.ReconcileInboxAsync(linkRow.Id, linkRow.PeerNodeId, written.Inbox,
                    written.Evaluated, written.ClosureHints, now, ct)).Created;
                await s.Store.CloseStaleStateItemsAsync(written.Evaluated.Concat(recorder.Touched).Distinct().ToList(),
                    linkRow.Id, now, ct);
            }

            // The pull that completes the link's first contact — the initiator's Start, the approver's first pull or
            // its "Start anyway" — is its "First sync with X" entry (§8.3), or "Copied from X", written even when it
            // only raised link suggestions or conflicts. Any other pull is an entry only when it applied something.
            var firstSync = linkRow.RecordApplied(pull, written.CursorAdvance, now);
            var historyKind = copyOnce ? DataSyncHistoryKind.CopyOnce
                : firstSync ? DataSyncHistoryKind.FirstLink
                : DataSyncHistoryKind.AutoSync;
            int? logId = null;
            if (recorder.Applied || firstSync)
            {
                logId = await s.Store.AddHistoryAsync(recorder.ToLog(historyKind, linkRow, args.Task.Id, now,
                    ElapsedMs(started)), ct);
            }

            await CommitAsync(s, ct);
            // Committed: a stop requested from here on no longer turns the apply into Cancelled.
            await AfterCommitAsync(s, recorder, kinds, historyKind, logId, linkRow.Id);

            var closed = openBefore.Except(await OpenItemIdsAsync(s, CancellationToken.None)).OrderBy(i => i).ToList();
            return (new DataSyncAutoSyncOutcome(logId, null, newItems, closed.Count,
                writer.Applied, written.Notes, closed, DataSyncAutoSyncEnd.Committed, firstSync), false);
        }
        catch (DataSyncActorUnverifiedException)
        {
            await s.RollbackAsync();
            return (Nothing, false);
        }
        catch
        {
            await s.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// What a first sync's preview skipped (§8.3) is excluded on the link under the record's keys, so this merge and
    /// every later one leave it alone until the person includes it.
    /// </summary>
    private static async Task SkipAsync(DataSyncApplySession s, int linkId, DataSyncStagedPull pull,
        IReadOnlyList<DataSyncFirstSyncChoice> choices, CancellationToken ct)
    {
        var skipped = choices.Where(c => c.Action == DataSyncFirstSyncAction.Skip).Select(c => (c.Kind, c.Key))
            .ToHashSet();
        var updates = pull.Kinds.SelectMany(k => k.Entities
                .Where(e => skipped.Contains((k.Kind, e.Record.Keys[0])))
                .Select(e => new DataSyncBaseUpdate(k.Kind, new SyncKey(e.Record.Keys[0]), DataSyncBaseState.Excluded,
                    DataSyncExclusionReason.Skipped, e.Record, null, null, true)))
            .ToList();
        if (updates.Count > 0) await s.Store.UpsertBasesAsync(linkId, updates, ct);
    }

    private static DataSyncCopyOnce CopyOnceOf(IReadOnlyList<DataSyncFirstSyncChoice> choices) => new(
        choices.Where(c => c is { Action: DataSyncFirstSyncAction.Link, LocalKey: not null })
            .GroupBy(c => (c.Kind, c.Key)).ToDictionary(g => g.Key, g => g.First().LocalKey!),
        choices.Where(c => c.Action == DataSyncFirstSyncAction.KeepBoth).Select(c => (c.Kind, c.Key)).ToHashSet());

    /// <summary>An anomaly's pause (§8.4 row A2), written in its own short transaction.</summary>
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

    private async Task<bool> IsAllPausedAsync(CancellationToken ct)
    {
        await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
        return await s.Db.DataSyncLocalStates.AsNoTracking().AnyAsync(l => l.AllPaused, ct);
    }

    private async Task<DataSyncPauseReason?> PausedReasonAsync(int linkId, CancellationToken ct)
    {
        await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
        var link = await s.Db.DataSyncLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId, ct);
        return link is { State: DataSyncLinkState.Paused } ? link.PausedReason ?? DataSyncPauseReason.ByUser : null;
    }
}

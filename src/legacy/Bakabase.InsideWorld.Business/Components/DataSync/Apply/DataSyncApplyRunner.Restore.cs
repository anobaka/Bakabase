using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

// §9.5: the restore choice ("my configuration wins").
public sealed partial class DataSyncApplyRunner
{
    /// <summary>
    /// The body of <c>DataSyncRestore</c> (§9.5). <b>This device's definitions win</b>: in one transaction, after
    /// Refresh, every live synced entity and every served tombstone gets <c>RestoreWins</c> = <c>Max(local, every base,
    /// pending and item vector of it, {a: recorded(a)} for every retired own actor a) + self</c>, so peers fast-forward
    /// to this device's definitions for everything they had seen, including the counters this device issued and lost
    /// (gate fix B1(a)). <b>Take the other devices' definitions</b>: no counters are raised; the next full
    /// reconciliation of the link pulled most recently before the restore takes the peer's version of concurrent
    /// entities, for that cycle only. Either way cursors go to 0 (a full reconciliation), bases are kept, the paused
    /// links resume, and the restore is cleared. Scoped to a suspected link, only that link's vectors count, only the
    /// entities it has a base for are revised, and only its cursors reset.
    /// </summary>
    /// <param name="linkId">A restore suspected through one link only (§5.6); null: the pending restore's own scope.</param>
    /// <returns>The <c>Restore</c> history entry, or null when no restore was pending.</returns>
    public async Task<int?> RunRestoreAsync(DataSyncRestoreChoice choice, int? linkId, BTaskArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var ct = args.CancellationToken;
        if (!await StartAsync(args)) return null;
        await WaitStartupVerifiedAsync(ct);
        using var lease = await _gate.EnterAsync(null, ct);
        if (!MayRun(args)) return null;
        await _guard.CheckAsync(lease, ct);

        int? others = null;
        var logId = await InTransactionAsync(lease, async s =>
        {
            var started = Stopwatch.GetTimestamp();
            // Refresh first: local edits made while the choice waited are revisions of the new actor, never collide.
            var state = await s.Store.GetLocalStateAsync(ct);
            if (state?.RestoreReason is null) return (int?) null;
            await RefreshAsyncOrKeep(s, lease, ct);
            state = await s.LoadStateAsync(ct);
            if (state.RestoreReason is not { } reason) return null;

            var scopedId = reason == DataSyncPauseReason.LocalRestoreSuspected ? linkId ?? state.RestoreLinkId : null;
            var links = await s.Db.DataSyncLinks.ToListAsync(ct);
            var scoped = scopedId is { } sid ? links.SingleOrDefault(l => l.Id == sid) : null;
            var affected = scoped is not null
                ? new List<DataSyncLinkDbModel> { scoped }
                : links.Where(l => l.State != DataSyncLinkState.Stopped).ToList();
            var recorder = new DataSyncApplyRecorder();

            if (choice == DataSyncRestoreChoice.ThisDeviceWins)
                await RestoreWinsAsync(s, recorder, affected.Select(l => l.Id).ToHashSet(), scoped is not null, ct);
            else
                others = affected.OrderByDescending(l => l.LastSyncedAtUtc ?? DateTime.MinValue).ThenBy(l => l.Id)
                    .FirstOrDefault()?.Id;

            var now = s.Now;
            foreach (var link in affected)
            {
                // Cursors to 0: the next cycle is one full reconciliation (§8.8); bases are kept.
                link.CursorsJson = "{}";
                link.LastFullReconciliationAtUtc = null;
                if (link.State == DataSyncLinkState.Paused &&
                    link.PausedReason is DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected)
                {
                    link.State = DataSyncLinkState.Active;
                    link.PausedReason = null;
                    link.PausedDetail = null;
                    link.NextAttemptAtUtc = now;
                }

                link.UpdatedAtUtc = now;
            }

            state.RestoreReason = null;
            state.RestoreLinkId = null;
            state.RestoreDetail = null;
            state.RestoreDetectedAtUtc = null;
            state.UpdatedAtUtc = now;

            var log = recorder.ToLog(DataSyncHistoryKind.Restore, scoped, args.Task.Id, now, ElapsedMs(started));
            var id = await s.Store.AddHistoryAsync(log, ct);
            await CommitAsync(s, ct);
            await AfterCommitAsync(s, recorder, s.Kinds.Keys.ToList(), DataSyncHistoryKind.Restore, id, scoped?.Id, ct);
            return id;
        }, ct);

        if (logId is not null && others is { } first) _othersWinNext[first] = true;
        return logId;
    }

    /// <summary>
    /// Refresh inside the restore's transaction. Evidence is handled by now (the actor check ran), so a skipped Refresh
    /// only means the start-up window is still open: the choice then revises what is stored.
    /// </summary>
    private async Task RefreshAsyncOrKeep(DataSyncApplySession s, DataSyncGateLease lease,
        System.Threading.CancellationToken ct)
    {
        try
        {
            await RefreshAsync(s, lease, s.Kinds.Keys.ToList(), null, ct);
        }
        catch (DataSyncActorUnverifiedException)
        {
            await s.LoadStateAsync(ct);
        }
    }

    /// <summary>
    /// <c>RestoreWins</c> (§9.5) over the affected links: every live synced entity and every served tombstone — scoped
    /// to one link, only those it has a base for — takes a revision that dominates every vector of it this device knows
    /// of and every counter its retired actors issued.
    /// </summary>
    private static async Task RestoreWinsAsync(DataSyncApplySession s, DataSyncApplyRecorder recorder,
        IReadOnlySet<int> linkIds, bool scoped, System.Threading.CancellationToken ct)
    {
        var retired = s.RetiredActorCounters();
        var bases = await s.Db.DataSyncPeerBases.AsNoTracking().Where(b => linkIds.Contains(b.LinkId)).ToListAsync(ct);
        var items = await s.Db.DataSyncInboxItems.AsNoTracking()
            .Where(i => i.RecordVvJson != null && (i.LinkId == null || linkIds.Contains(i.LinkId.Value)))
            .Select(i => new { i.Kind, i.SyncKey, i.RecordVvJson }).ToListAsync(ct);
        foreach (var kind in s.Kinds.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var index = await s.Identity.GetKeyIndexAsync(kind, null, ct);
            var rows = (await s.Store.GetEntitiesAsync(kind, includeTombstones: true, ct))
                .Where(r => r.DeletedAtUtc is null ? r.State == DataSyncEntitySyncState.Synced : r.TombstoneServed)
                .ToList();
            foreach (var row in rows)
            {
                var keys = index.Entities.Single(e => e.Id == row.Id).Keys.All.Select(k => k.Value)
                    .ToHashSet(StringComparer.Ordinal);
                var mine = bases.Where(b => b.Kind == kind && keys.Contains(b.SyncKey)).ToList();
                if (scoped && mine.Count == 0) continue;
                var vectors = mine.SelectMany(b => new[]
                    {
                        DataSyncStoredJson.ReadVv(b.VvJson),
                        DataSyncStoredJson.ReadRecordVv(b.PendingRecordJson),
                    })
                    .Concat(items.Where(i => i.Kind == kind && keys.Contains(i.SyncKey))
                        .Select(i => DataSyncStoredJson.ReadVv(i.RecordVvJson)))
                    .Where(v => v is not null).Select(v => v!).ToList();
                var remote = DataSyncRevisionRules.RestoreWinsRemote(vectors, retired);
                var local = DataSyncVersionVector.ParseStored(row.VvJson);
                row.VvJson = DataSyncRevisionRules.Next(DataSyncRevisionKind.RestoreWins, local, remote, false, false,
                    s.SelfActor, s.NextCounter).ToCanonicalString();
                DataSyncEntityWrites.SetEditor(row, s.Self);
                row.Seq = await s.Store.NextSeqAsync(ct);
                row.UpdatedAtUtc = s.Now;
                recorder.Touched.Add((kind, new SyncKey(row.SyncKey)));
                recorder.Resolved++;
            }
        }

        await s.Db.SaveChangesAsync(ct);
    }
}

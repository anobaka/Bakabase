using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

// §8.11: continuous undo.
public sealed partial class DataSyncApplyRunner
{
    /// <summary>
    /// Undoes one history entry (§8.11) with the continuous rules: an undone create is deleted through the service and
    /// leaves an unserved <c>UndoneCreate</c> tombstone and <c>Excluded(Undone)</c> bases on every link (keys and
    /// aliases kept, nothing proposed to anyone); an undone bind excludes the supplying link's record; an undone update
    /// writes back exactly the changed paths as a new local revision (<c>Undo</c>); an undone deletion re-creates the
    /// definition with the same child ids, reviving its key; a type change converts back (backed up first when lossy);
    /// key moves return to their owners. Entities that changed since are refused one by one; there is no redo.
    /// </summary>
    /// <returns>The <c>Undo</c> history entry, or null when nothing could be undone.</returns>
    public async Task<int?> RunUndoAsync(int applyLogId, BTaskArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var ct = args.CancellationToken;
        if (!await StartAsync(args)) return null;
        await WaitStartupVerifiedAsync(ct);
        using var lease = await _gate.EnterAsync(null, ct);
        if (!MayRun(args)) return null;
        await _guard.CheckAsync(lease, ct);

        // §8.10.4: an undo that converts a type back with a lossy preview is backed up first, before the transaction.
        await using (var planning = await DataSyncApplySession.OpenAsync(_scopes, ct))
        {
            var (_, steps, problem) = await DataSyncUndoPlanner.PlanAsync(planning, applyLogId, ct);
            if (problem is not null)
                throw new BTaskException(nameof(DataSyncProblemCode.UndoNotAvailable), "This entry cannot be undone.");
            if (steps.Any(x => x.Blocked is null && x.Destructive)) await BackupAsync(ct);
        }

        return await InTransactionAsync(lease, async s =>
        {
            var started = Stopwatch.GetTimestamp();
            await RefreshOrFailAsync(s, lease, s.Kinds.Keys.ToList(), ct);
            var (log, steps, problem) = await DataSyncUndoPlanner.PlanAsync(s, applyLogId, ct);
            if (problem is not null || log is null)
                throw new BTaskException(nameof(DataSyncProblemCode.UndoNotAvailable), "This entry cannot be undone.");

            var recorder = new DataSyncApplyRecorder();
            var writes = new DataSyncEntityWrites(s, recorder);
            var results = new List<DataSyncUndoPreviewItem>();
            var applied = 0;
            foreach (var step in steps)
            {
                // Inside the transaction only a stop is honoured: a pause here would keep SQLite's writer lock.
                ct.ThrowIfCancellationRequested();
                var blocked = step.Blocked ?? await UndoStepAsync(s, writes, step, ct);
                results.Add(step.ToView() with { Blocked = blocked });
                recorder.Item(step.PreImage.Kind + "/" + step.PreImage.EntityId, step.PreImage.Kind, step.PreImage.Name,
                    blocked is null ? DataSyncItemOutcome.Applied : DataSyncItemOutcome.ChangedSinceReview,
                    UndoActionOf(step.Action), step.PreImage.LocalKey, DataSyncPlanItemType.Update);
                if (blocked is null) applied++;
            }

            // An entity setting set back changes what the entity publishes, or whether it does: Refresh makes that a
            // revision now, as the setting's own Refresh did (§6.6).
            var settingKinds = steps.Zip(results).Where(x => x.First.Setting is not null && x.Second.Blocked is null)
                .Select(x => x.First.PreImage.Kind).Distinct(StringComparer.Ordinal).ToList();
            if (settingKinds.Count > 0) await s.Refresher.RefreshAsync(lease, settingKinds, false, ct);

            // Key moves (KeepWithEntity, rekeys, KeepRecordLinked) return to their pre-image owners — all of them or
            // none: the moves are saved one by one, and one taken back halfway could leave a key that no entity owns,
            // so a peer's record carrying it would bind as new (§5.3; undo never frees a key, §8.11).
            var document = DataSyncPreImageDocument.Read(log.PreImageJson);
            if (document.Identity is { KeyMoves.Count: > 0 } identity)
            {
                const string savepoint = "undoKeyMoves";
                await s.SavepointAsync(savepoint, ct);
                try
                {
                    await s.Identity.RestoreKeyOwnersAsync(identity, ct);
                    await s.ReleaseSavepointAsync(savepoint, ct);
                    applied++;
                }
                catch (DataSyncIdentityRefusedException e)
                {
                    await s.RollbackToSavepointAsync(savepoint);
                    // The rollback forgot every tracked row, the log among them.
                    log = await s.Db.DataSyncApplyLogs.SingleAsync(l => l.Id == applyLogId, ct);
                    recorder.ChangedSinceReview++;
                    recorder.Item("keys/" + e.Kind + "/" + e.Key, e.Kind, e.Key, DataSyncItemOutcome.ChangedSinceReview,
                        DataSyncItemAction.None, null, DataSyncPlanItemType.Update);
                    _logger.LogInformation(e, "Data sync undo kept every key where it is: {Reason}", e.Message);
                }
            }

            if (applied == 0)
            {
                await s.RollbackAsync();
                return (int?) null;
            }

            var now = s.Now;
            var link = log.LinkId is { } linkId ? await s.LinkAsync(linkId, ct) : null;
            var undoLog = recorder.ToLog(DataSyncHistoryKind.Undo, link, args.Task.Id, now, ElapsedMs(started), log.Id);
            var undoId = await s.Store.AddHistoryAsync(undoLog, ct);
            log.UndoneAtUtc = now;
            log.UndoResultJson = DataSyncStoredJson.Write(results);
            await s.Store.CloseStaleStateItemsAsync(recorder.Touched.ToList(), null, now, ct);
            await CommitAsync(s, ct);
            await AfterCommitAsync(s, recorder, s.Kinds.Keys.ToList(), DataSyncHistoryKind.Undo, undoId, log.LinkId);
            return undoId;
        }, ct);
    }

    private static DataSyncItemAction UndoActionOf(DataSyncUndoAction action) => action switch
    {
        DataSyncUndoAction.Remove => DataSyncItemAction.Deleted,
        DataSyncUndoAction.Recreate => DataSyncItemAction.Created,
        DataSyncUndoAction.Exclude => DataSyncItemAction.KeysRecorded,
        _ => DataSyncItemAction.Updated,
    };

    /// <summary>One entity's undo; returns the refusal it met while writing (the plan's checks passed).</summary>
    private async Task<DataSyncUndoBlock?> UndoStepAsync(DataSyncApplySession s, DataSyncEntityWrites writes,
        DataSyncUndoStep step, CancellationToken ct)
    {
        var p = step.PreImage;
        var row = await s.Db.DataSyncEntities.SingleOrDefaultAsync(e => e.Id == p.EntityId, ct);
        if (row is null) return DataSyncUndoBlock.Missing;
        var kind = row.Kind;
        var adapter = s.Adapter(kind);
        var codec = adapter.Codec;
        var keys = await s.KeysOfAsync(row, ct);
        var recorder = writes.Recorder;
        switch (p.Action)
        {
            case DataSyncPreImageActions.Created:
            {
                // Deleted through the service; the side row becomes an unserved UndoneCreate tombstone with its keys and
                // aliases (never deleted), so the peer's record never binds as new and nobody is asked to delete it.
                await s.Writer(kind).DeleteAsync(row.LocalKey, ct);
                writes.Written(kind);
                var vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Undo, DataSyncVersionVector.ParseStored(row.VvJson),
                    null, false, false, s.SelfActor, s.NextCounter);
                await s.Identity.TombstoneAsync(row,
                    new DataSyncTombstoneWrite(vv, DataSyncTombstoneKind.UndoneCreate, false, s.Self), ct);
                await ExcludeUndoneAsync(s, kind, row.SyncKey, keys.All.Select(k => k.Value), null, ct);
                recorder.Deleted++;
                recorder.ChangedDefinitions.Add((kind, row.LocalKey));
                writes.Touch(row);
                return null;
            }
            case DataSyncPreImageActions.Bound:
            {
                // The link that supplied the bind stops taking the peer's changes to it; the aliases are kept.
                if (p.LinkId is { } linkId && await s.LinkAsync(linkId, ct) is not null)
                {
                    await ExcludeUndoneAsync(s, kind, row.SyncKey,
                        keys.All.Select(k => k.Value).Concat(p.AliasesAdded ?? []), linkId, ct);
                }

                recorder.Linked++;
                return null;
            }
            case DataSyncPreImageActions.Deleted:
            {
                // Re-created with the same child ids; the tombstone's key revives with Undo ≥ the tombstone.
                var itemId = DataSyncMergeItemIds.Of(kind, new SyncKey(row.SyncKey));
                var create = new CreateEntityOperation(itemId, keys, row.OriginNodeId, 0, p.Content!);
                var outcome = await s.Writer(kind).ApplyAsync(new ApplyBatch(kind, [create]), ct);
                writes.Written(kind);
                if (!outcome.CreatedLocalKeysByItemId.TryGetValue(itemId, out var localKey))
                    return DataSyncUndoBlock.ChangedSinceImport;
                var decision = new DataSyncRevisionDecision(kind, keys, null, DataSyncRevisionKind.Undo, null, null, false,
                    false, row.OrderKey, DataSyncEntityForms.ReadUnknown(row.UnknownJson), row.ChildrenLocal, null);
                var tombstoneVv = DataSyncVersionVector.ParseStored(row.VvJson);
                var state = row.State;
                var created = await writes.RecordCreateAsync(kind, localKey, keys, row.OriginNodeId, decision, null, null,
                    createdBySync: false, ct, tombstoneVv);
                created.State = state;
                recorder.Created++;
                recorder.ChangedDefinitions.Add((kind, localKey));
                if (codec.Descriptor.HasOrder && created.OrderKey is not null)
                {
                    await PlaceOrderOfKindAsync(s, kind, ct);
                    writes.Written(kind);
                }

                return null;
            }
            case DataSyncPreImageActions.EntitySetting:
            {
                // How it syncs goes back (§6.6); the Refresh after the steps makes a shared change a revision, as the
                // setting's own did.
                if (row.DeletedAtUtc is not null || step.Setting is not { } setting) return DataSyncUndoBlock.Missing;
                await s.Store.SetEntityStateAsync(kind, row.LocalKey, setting.State, ct);
                await s.Store.SetChildrenLocalAsync(kind, row.LocalKey, setting.ChildrenLocal, ct);
                if (!SameOverlay(Runtime.DataSyncViews.ReadOverlay(row.OverlayJson), setting.Overlay))
                    await s.Store.SetOverlayAsync(kind, row.LocalKey, setting.Overlay, ct);
                writes.Touch(row);
                recorder.Updated++;
                return null;
            }
            case DataSyncPreImageActions.TypeChanged:
            {
                var before = codec.ReadLocal((await writes.ReReadAsync(kind, row.LocalKey, ct)).Content);
                var outcome = await s.Writer(kind).ApplyAsync(new ApplyBatch(kind,
                    [new ChangeSubtypeOperation(DataSyncMergeItemIds.Of(kind, new SyncKey(row.SyncKey)), row.LocalKey,
                        row.LocalHash, p.FromSubtype!)]), ct);
                writes.Written(kind);
                if (outcome.ChangedDuringApplyItemIds.Count > 0) return DataSyncUndoBlock.ChangedSinceImport;
                row.PublishHeld = false;
                await writes.RecordLiveAsync(kind, row.LocalKey, before, UndoDecision(row, keys), null, null,
                    EntityKeys.None, null, ct);
                recorder.TypeChanged++;
                return null;
            }
            default:
            {
                // Exactly the changed paths, back to their values before the apply, as a new local revision.
                var current = codec.ReadLocal((await writes.ReReadAsync(kind, row.LocalKey, ct)).Content);
                if (p.Changes is null) return null;
                var edit = DataSyncChangeLists.Apply(codec, current, row.ChildrenLocal, p.Changes.Scalars,
                    p.Changes.Children, backward: true);
                if (edit.Conflicts.Count > 0) return DataSyncUndoBlock.ChangedSinceImport;
                var merged = codec.Write(codec.ReadLocal(edit.Content));
                if (!System.Text.Json.Nodes.JsonNode.DeepEquals(merged, codec.Write(current)))
                {
                    var update = new UpdateEntityOperation(DataSyncMergeItemIds.Of(kind, new SyncKey(row.SyncKey)),
                        row.LocalKey, row.LocalHash, merged, EntityKeys.None, edit.AddedChildIds, edit.RemovedChildIds);
                    var outcome = await s.Writer(kind).ApplyAsync(new ApplyBatch(kind, [update]), ct);
                    writes.Written(kind);
                    if (outcome.ChangedDuringApplyItemIds.Count > 0) return DataSyncUndoBlock.ChangedSinceImport;
                }

                // An undo clears PublishHeld (§6.5); its own writes are exempt from the lost-update guard.
                row.PublishHeld = false;
                await writes.RecordLiveAsync(kind, row.LocalKey, current,
                    UndoDecision(row, keys) with { ChildrenLocal = edit.ChildrenLocal ?? row.ChildrenLocal }, null, null,
                    EntityKeys.None, null, ct);
                recorder.Updated++;
                return null;
            }
        }
    }

    private static bool SameOverlay(DataSyncOverlay a, DataSyncOverlay b) =>
        a.LocalOnlyChildren.OrderBy(c => c, StringComparer.Ordinal)
            .SequenceEqual(b.LocalOnlyChildren.OrderBy(c => c, StringComparer.Ordinal)) &&
        a.HeldChildren.OrderBy(h => h.ChildId, StringComparer.Ordinal).ThenBy(h => h.LinkId)
            .SequenceEqual(b.HeldChildren.OrderBy(h => h.ChildId, StringComparer.Ordinal).ThenBy(h => h.LinkId));

    private static DataSyncRevisionDecision UndoDecision(DataSyncEntityDbModel row, EntityKeys keys) =>
        new(row.Kind, keys, row.LocalKey, DataSyncRevisionKind.Undo, null, null, false, false, row.OrderKey,
            DataSyncEntityForms.ReadUnknown(row.UnknownJson), row.ChildrenLocal, null);

    /// <summary>
    /// <c>Excluded(Undone)</c> with the entity's keys (§8.11): on one link, or on every link (an undone create). Its
    /// pending records are cleared; [Include] clears the exclusion again.
    /// </summary>
    private static async Task ExcludeUndoneAsync(DataSyncApplySession s, string kind, string baseKey,
        IEnumerable<string> keys, int? linkId, CancellationToken ct)
    {
        var all = keys.Distinct(StringComparer.Ordinal).ToList();
        var links = linkId is { } id
            ? new List<int> { id }
            : await s.Db.DataSyncLinks.AsNoTracking().Select(l => l.Id).ToListAsync(ct);
        foreach (var link in links) await s.Store.ExcludeAsync(link, kind, baseKey, DataSyncExclusionReason.Undone, all, ct);
    }

    /// <summary>Every live synced entity of a kind with order fills the synced slots in shared order (§3.7).</summary>
    private static async Task PlaceOrderOfKindAsync(DataSyncApplySession s, string kind, CancellationToken ct)
    {
        var index = await s.Identity.GetKeyIndexAsync(kind, null, ct);
        var tie = index.Entities.Where(e => e.Live)
            .ToDictionary(e => e.Id, e => Bakabase.Modules.DataSync.Ordering.DataSyncOrderPlanner.TieKeyOf(e.Keys));
        var entries = (await s.Store.ReadEntitiesAsync(kind, false, ct))
            .Where(r => r.State == DataSyncEntitySyncState.Synced && !r.PublishHeld && !r.Unreadable && r.OrderKey is not null)
            .Select(r => new Bakabase.Modules.DataSync.Ordering.DataSyncOrderEntry(r.LocalKey, r.OrderKey, tie[r.Id]))
            .ToList();
        await s.Writer(kind).ApplyOrderAsync(
            Bakabase.Modules.DataSync.Ordering.DataSyncOrderPlanner.Sort(entries).Select(e => e.LocalKey).ToList(), ct);
    }
}

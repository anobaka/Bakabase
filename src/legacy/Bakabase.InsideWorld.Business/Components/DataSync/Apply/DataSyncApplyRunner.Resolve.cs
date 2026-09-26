using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

// §9.2: resolving inbox items.
public sealed partial class DataSyncApplyRunner
{
    /// <summary>
    /// Resolves a batch of inbox items (§9.2): under the gate, after a backup when the batch is destructive
    /// (§8.10.4), in one transaction that starts with Refresh, cut into chunks of whole entities once a chunk has run
    /// for <see cref="TransactionBudget"/>. Every item is validated again first — a merger-derived item is derived
    /// once more by the merger over its stored pending record (once per entity and link, from the state the decisions
    /// before it left, with the definitions' contents read once per transaction), and stands only with the same
    /// token; a state-derived item stands only while its state does —
    /// then its action is applied from the table of §9.2, with the re-merges some actions need run in the same task.
    /// Items that changed are updated, never applied (<c>InboxItemChanged</c>); items whose subject is gone close
    /// <c>Superseded</c>. A regression the merger finds rolls back the current transaction; the evidence is reported
    /// outside it (§5.6) and the batch runs once more.
    /// </summary>
    /// <remarks>
    /// Pausing the task waits only between chunks, where no transaction is open, and with the gate given back: inside
    /// a transaction only cancellation is checked, so a paused task never keeps SQLite's writer lock, nor the gate that
    /// heads and every other data sync caller wait for. After the pause the attempt and the actor are checked again,
    /// and the next chunk validates its items from what is stored then.
    /// </remarks>
    /// <returns>The <c>Resolution</c> history entry, or null when nothing was resolved.</returns>
    public async Task<int?> RunResolutionsAsync(IReadOnlyList<DataSyncResolveInput> resolutions,
        DataSyncApplyOptions options, BTaskArgs args)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(args);
        var ct = args.CancellationToken;
        if (!await StartAsync(args)) return null;
        await WaitStartupVerifiedAsync(ct);
        using var lease = await DataSyncTaskLease.EnterAsync(_gate, ct);
        if (!MayRun(args)) return null;
        await _guard.CheckAsync(lease, ct);
        if (options.BackupBeforeDestructive && await IsDestructiveAsync(resolutions, ct)) await BackupAsync(ct);

        var anomalyReported = false;
        while (true)
        {
            try
            {
                return await InTransactionAsync(lease,
                    s => ResolveInTransactionAsync(s, lease, resolutions, anomalyReported, args), ct);
            }
            catch (DataSyncAttemptEndedException)
            {
                // Stopped or replaced while paused between chunks: the chunks committed before stand.
                return null;
            }
            catch (DataSyncMergeAnomalyException e) when (!anomalyReported)
            {
                // Rolled back, Refresh included: nothing issued under a regressed actor stands (§5.6). Reported outside
                // any transaction, then once more — the link paused, or only a retired actor's recorded counter rose.
                anomalyReported = true;
                await HandleAnomalyAsync(lease, e.LinkId, e.PeerNodeId, e.Anomaly, e.Pause, e.PauseDetail, ct);
            }
        }
    }

    /// <summary>
    /// One run of the batch in the session's open transaction: Refresh, each entity's items validated and applied in
    /// time-cut chunks, the state-derived closure, the history entry, the commit.
    /// </summary>
    private async Task<int?> ResolveInTransactionAsync(DataSyncApplySession s, DataSyncTaskLease lease,
        IReadOnlyList<DataSyncResolveInput> resolutions, bool anomalyReported, BTaskArgs args)
    {
        var ct = args.CancellationToken;
        var started = Stopwatch.GetTimestamp();
        var recorder = new DataSyncApplyRecorder();
        var writer = new ResolutionWriter(this, s, new DataSyncEntityWrites(s, recorder), anomalyReported);
        var release = await writer.PublishReleasesAsync(resolutions, ct);
        await RefreshOrFailAsync(s, lease, s.Kinds.Keys.ToList(), ct, new DataSyncRefreshOptions(release));
        // Refresh tracks every entity row it read; every later save would scan them all.
        await s.ForgetTrackedAsync(ct);

        var groups = await writer.GroupAsync(resolutions, ct);
        var chunkStarted = Stopwatch.GetTimestamp();
        for (var i = 0; i < groups.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await writer.ResolveGroupAsync(groups[i], ct);
            if (i < groups.Count - 1 && Stopwatch.GetElapsedTime(chunkStarted) >= TransactionBudget)
            {
                // ≈ 2 s per transaction (non-blocking note 6), whole entities only. Between chunks nothing is
                // open: other writers get the lock, and this is where the task may be paused (the gate given back).
                // The next transaction must still see the actor this one's Refresh saw (§5.6).
                await CommitAsync(s, ct);
                await s.ForgetTrackedAsync(ct);
                writer.ForgetChunk();
                await BetweenChunksAsync(lease, args, ct);
                await ContinueAsync(s, ct);
                chunkStarted = Stopwatch.GetTimestamp();
            }
        }

        var now = s.Now;
        await s.Store.CloseStaleStateItemsAsync(recorder.Touched.Concat(writer.Subjects).Distinct().ToList(), null, now,
            ct);
        int? logId = null;
        if (writer.Closed.Count > 0 || recorder.Applied)
        {
            var linkIds = writer.LinkIds.Distinct().ToList();
            var link = linkIds.Count == 1 ? await s.LinkAsync(linkIds[0], ct) : null;
            recorder.Resolved = writer.Closed.Count;
            logId = await s.Store.AddHistoryAsync(recorder.ToLog(DataSyncHistoryKind.Resolution, link, args.Task.Id,
                now, ElapsedMs(started)), ct);
            var closed = writer.Closed.ToList();
            foreach (var item in await s.Db.DataSyncInboxItems.Where(i => closed.Contains(i.Id)).ToListAsync(ct))
                item.ApplyLogId = logId;
        }

        await CommitAsync(s, ct);
        await AfterCommitAsync(s, recorder, s.Kinds.Keys.ToList(), DataSyncHistoryKind.Resolution, logId,
            writer.LinkIds.Distinct().Count() == 1 ? writer.LinkIds[0] : null);
        return logId;
    }

    /// <summary>
    /// §8.10.4: a batch is destructive when it deletes an entity with values, converts a type with a lossy preview,
    /// or deletes a held child that resources use. Read before the transaction, without writing.
    /// </summary>
    private async Task<bool> IsDestructiveAsync(IReadOnlyList<DataSyncResolveInput> resolutions, CancellationToken ct)
    {
        await using var s = await DataSyncApplySession.OpenAsync(_scopes, ct);
        foreach (var input in resolutions)
        {
            var item = await s.Db.DataSyncInboxItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == input.ItemId, ct);
            if (item is null || item.ClosedAtUtc is not null || !s.Kinds.TryGetValue(item.Kind, out var adapter)) continue;
            var owner = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (owner is not { DeletedAtUtc: null }) continue;
            switch (item.Type, input.Action)
            {
                case (DataSyncInboxItemType.DeletedThere, DataSyncInboxAction.DeleteHere):
                {
                    var usage = await adapter.GetUsageAsync(
                        new Dictionary<string, IReadOnlyCollection<string>> { [owner.LocalKey] = [] }, ct);
                    if (usage.GetValueOrDefault(owner.LocalKey)?.ValueCount > 0) return true;
                    break;
                }
                case (DataSyncInboxItemType.ChildDeletedInUse, DataSyncInboxAction.DeleteHere):
                {
                    var localChild = await HeldChildOfAsync(s, item, owner, ct);
                    if (localChild is null) break;
                    var usage = await adapter.GetUsageAsync(
                        new Dictionary<string, IReadOnlyCollection<string>> { [owner.LocalKey] = [localChild] }, ct);
                    if (usage.GetValueOrDefault(owner.LocalKey)?.ResourceCountByChildId.GetValueOrDefault(localChild) > 0)
                        return true;
                    break;
                }
                case (DataSyncInboxItemType.TypeChange, DataSyncInboxAction.Convert):
                {
                    var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "PayloadJson");
                    if (payload.RemoteSubtype is not { } subtype) break;
                    var preview = await adapter.PreviewSubtypeChangeAsync(owner.LocalKey, subtype, ct);
                    if (preview.LossyCount > 0) return true;
                    break;
                }
            }
        }

        return false;
    }

    /// <summary>The local child a <c>ChildDeletedInUse</c> item is about (its subject's peer id through the base's child map).</summary>
    private static async Task<string?> HeldChildOfAsync(DataSyncApplySession s, DataSyncInboxItemDbModel item,
        DataSyncEntityDbModel owner, CancellationToken ct)
    {
        if (item.LinkId is not { } linkId) return null;
        var separator = item.SubjectPath.IndexOf(':');
        if (separator < 0) return null;
        var peerId = item.SubjectPath[(separator + 1)..];
        var held = DataSyncStoredJson.ReadOverlay(owner.OverlayJson).HeldChildren
            .Where(h => h.LinkId == linkId).Select(h => h.ChildId).ToHashSet(StringComparer.Ordinal);
        if (held.Contains(peerId)) return peerId;
        var row = await s.Db.DataSyncPeerBases.AsNoTracking().FirstOrDefaultAsync(b =>
            b.LinkId == linkId && b.Kind == item.Kind && (b.SyncKey == owner.SyncKey || b.SyncKey == item.SyncKey), ct);
        return DataSyncStoredJson.ReadChildMap(row?.ChildMapJson).TryGetValue(peerId, out var local) && held.Contains(local)
            ? local
            : null;
    }

    /// <summary>The §9.2 action table over one session.</summary>
    /// <param name="anomalyReported">
    /// A regression this batch's merges met was reported already (§5.6): a merge that meets one again cannot run now
    /// instead of rolling the batch back once more.
    /// </param>
    private sealed class ResolutionWriter(DataSyncApplyRunner runner, DataSyncApplySession s, DataSyncEntityWrites writes,
        bool anomalyReported)
    {
        private readonly DataSyncApplyRecorder _recorder = writes.Recorder;

        /// <summary>
        /// The current group's derivations, per link and set of pending records: every item of one entity and link is
        /// validated against one merge (§9.2 step 2, "re-derive once per entity").
        /// </summary>
        private readonly Dictionary<(int LinkId, string Keys), DataSyncMergeResult?> _derivations = new();

        /// <summary>
        /// Local contents read in the current transaction: a derivation or re-merge reads again only the entities
        /// written since (their <c>LocalHash</c> moved), not every definition of the kind.
        /// </summary>
        private readonly DataSyncLocalContentCache _contents = new();

        /// <summary>Items closed as resolved here.</summary>
        public HashSet<long> Closed { get; } = [];

        /// <summary>The links of the items resolved.</summary>
        public List<int> LinkIds { get; } = [];

        /// <summary>Every subject touched, for the state-derived closure (§9.3).</summary>
        public List<(string Kind, SyncKey Key)> Subjects { get; } = [];

        /// <summary>
        /// §6.5 Publish: the held entities the Refresh that starts the transaction releases as ordinary local
        /// revisions. Only a <c>SuspectedLostUpdate</c> item still open on an entity still held.
        /// </summary>
        public async Task<IReadOnlyCollection<(string Kind, string LocalKey)>> PublishReleasesAsync(
            IReadOnlyList<DataSyncResolveInput> inputs, CancellationToken ct)
        {
            var result = new List<(string, string)>();
            foreach (var input in inputs.Where(i => i.Action == DataSyncInboxAction.Publish))
            {
                var item = await s.Db.DataSyncInboxItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == input.ItemId, ct);
                if (item is not { ClosedAtUtc: null, Type: DataSyncInboxItemType.SuspectedLostUpdate } ||
                    item.Token != input.Token) continue;
                var owner = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
                if (owner is { DeletedAtUtc: null, PublishHeld: true }) result.Add((owner.Kind, owner.LocalKey));
            }

            _released.UnionWith(result);
            return result;
        }

        /// <summary>The batch per entity (§9.2 step 2: re-derived once per entity), in the order the items came.</summary>
        public async Task<IReadOnlyList<IReadOnlyList<DataSyncResolveInput>>> GroupAsync(
            IReadOnlyList<DataSyncResolveInput> inputs, CancellationToken ct)
        {
            var groups = new List<(string Key, List<DataSyncResolveInput> Inputs)>();
            foreach (var input in inputs.DistinctBy(i => i.ItemId))
            {
                var item = await s.Db.DataSyncInboxItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == input.ItemId, ct);
                string key;
                if (item is null) key = "?" + input.ItemId;
                else if (item.Type == DataSyncInboxItemType.LargeChange) key = "link:" + item.LinkId;
                else
                {
                    var owner = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
                    key = item.Kind + "/" + (owner?.SyncKey ?? item.SyncKey);
                }

                var group = groups.FirstOrDefault(g => g.Key == key);
                if (group.Inputs is null) groups.Add((key, [input]));
                else group.Inputs.Add(input);
            }

            return groups.Select(g => (IReadOnlyList<DataSyncResolveInput>) g.Inputs).ToList();
        }

        /// <summary>
        /// One entity's items (§9.2): validated (step 2, re-derived once per entity and link), then those that stand
        /// applied. Before its action each item is read again: an action before it in the group may have closed it,
        /// or rolled back to a savepoint (Convert).
        /// </summary>
        public async Task ResolveGroupAsync(IReadOnlyList<DataSyncResolveInput> inputs, CancellationToken ct)
        {
            // Derived from the state earlier groups left, never from an older read: a decision before this one may
            // have moved a record onto this entity's base row.
            _derivations.Clear();
            var valid = new List<(DataSyncResolveInput Input, DataSyncInboxItemDbModel Item)>();
            foreach (var input in inputs)
            {
                var item = await s.Store.GetItemAsync(input.ItemId, ct);
                if (item is null || item.ClosedAtUtc is not null) continue;
                Subjects.Add((item.Kind, SyncKey.IsValid(item.SyncKey) ? new SyncKey(item.SyncKey) : SyncKey.LinkLevel));
                if (item.Token != input.Token || !await IsAllowedAsync(item, input.Action, ct) ||
                    !await StandsAsync(item, input, ct))
                {
                    continue;
                }

                valid.Add((input, item));
            }

            var conflicts = valid.Where(v => v.Item.Type is DataSyncInboxItemType.FieldConflict
                                                 or DataSyncInboxItemType.ChildRenameConflict &&
                                             v.Input.Action != DataSyncInboxAction.Detach).ToList();
            if (conflicts.Count > 0) await ResolveConflictsAsync(conflicts, ct);

            foreach (var (input, _) in valid.Except(conflicts))
            {
                var item = await s.Store.GetItemAsync(input.ItemId, ct);
                if (item is not { ClosedAtUtc: null } || item.Token != input.Token) continue;
                await ResolveOneAsync(input, item, ct);
            }
        }

        /// <summary>A chunk committed: other writers may have changed definitions since, so none is reused.</summary>
        public void ForgetChunk() => _contents.Clear();

        private async Task<bool> IsAllowedAsync(DataSyncInboxItemDbModel item, DataSyncInboxAction action,
            CancellationToken ct)
        {
            var link = item.LinkId is { } id ? await s.LinkAsync(id, ct) : null;
            var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "PayloadJson");
            return Runtime.DataSyncInboxRules.Allowed(item.Type, item.SubjectPath, payload,
                    Runtime.DataSyncInboxRules.IsEffectivelyTwoWay(link))
                .Contains(action);
        }

        #region Validation (§9.2 step 2)

        /// <summary>
        /// Whether the item still stands. A merger-derived item is derived again from its stored pending record(s);
        /// a changed one is updated and not applied, one no longer produced closes <c>Superseded</c>. A state-derived
        /// item stands while its state does; otherwise it closes <c>Superseded</c>.
        /// </summary>
        private async Task<bool> StandsAsync(DataSyncInboxItemDbModel item, DataSyncResolveInput input, CancellationToken ct)
        {
            if (item.Origin == DataSyncInboxItemOrigin.State)
            {
                if (await StateStandsAsync(item, ct)) return true;
                await CloseAsync(item, DataSyncInboxClosure.Superseded, null, ct);
                return false;
            }

            if (item.LinkId is not { } linkId || await s.LinkAsync(linkId, ct) is not { } link ||
                await PendingKeysOfAsync(item, linkId, ct) is not { Count: > 0 } keys)
            {
                await CloseAsync(item, DataSyncInboxClosure.Superseded, null, ct);
                return false;
            }

            // Null: the merge met a regression already reported; the item waits, open.
            var result = await DeriveAsync(link, keys, ct);
            if (result is null || result.Pause is not null) return false;
            var draft = result.Inbox.FirstOrDefault(d => d.Type == item.Type && d.Kind == item.Kind &&
                                                         d.Key.Value == item.SyncKey && d.SubjectPath == item.SubjectPath);
            if (draft is null)
            {
                await CloseAsync(item, DataSyncInboxClosure.Superseded, null, ct);
                return false;
            }

            if (draft.Token != input.Token)
            {
                // The card changed while the person looked at it: it is updated, never applied (InboxItemChanged).
                await s.Store.UpsertItemsAsync(linkId, link.PeerNodeId, [draft], s.Now, ct);
                _recorder.ChangedSinceReview++;
                return false;
            }

            // A target must be one the merger offers now, not only one the card showed.
            return input.Action switch
            {
                DataSyncInboxAction.Link or DataSyncInboxAction.KeepWithEntity =>
                    draft.Payload.Candidates?.Any(c => c.Updatable && c.LocalKey == input.TargetLocalKey) == true,
                DataSyncInboxAction.KeepRecordLinked =>
                    draft.Payload.Records?.Any(r => r.PrimaryKey == input.TargetRecordKey) == true,
                _ => true,
            };
        }

        /// <summary>
        /// The merger over the given pending records of one link, once per group and set of records, whichever items
        /// ask; the input covers only the records' kinds, and reuses the contents the transaction read. Null when it
        /// met a regression already reported (§5.6); a new one rolls the batch back.
        /// </summary>
        private async Task<DataSyncMergeResult?> DeriveAsync(DataSyncLinkDbModel link,
            IReadOnlyList<(string Kind, SyncKey Key)> keys, CancellationToken ct)
        {
            var id = (link.Id, string.Join('\n', keys.Select(k => k.Kind + "/" + k.Key.Value)));
            if (_derivations.TryGetValue(id, out var derived)) return derived;
            var result = DataSyncMerger.Merge(await DataSyncMergeInputs.BuildAsync(s, KindsOf(link, keys), null, keys, ct,
                _contents));
            if (result.Anomaly is { } anomaly)
            {
                if (!anomalyReported)
                    throw new DataSyncMergeAnomalyException(link.Id, link.PeerNodeId, anomaly, result.Pause, result.PauseDetail);
                result = null;
            }

            return _derivations[id] = result;
        }

        private async Task<bool> StateStandsAsync(DataSyncInboxItemDbModel item, CancellationToken ct)
        {
            switch (item.Type)
            {
                case DataSyncInboxItemType.SuspectedLostUpdate:
                {
                    var owner = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
                    // Publish released it in this transaction's Refresh: the decision stands.
                    return owner is { DeletedAtUtc: null } &&
                           (owner.PublishHeld || _released.Contains((owner.Kind, owner.LocalKey)));
                }
                case DataSyncInboxItemType.LargeChange:
                    return item.LinkId is { } largeLink && await s.Db.DataSyncPeerBases.AnyAsync(
                        b => b.LinkId == largeLink && b.PendingReason == DataSyncPendingReason.LargeChange, ct);
                case DataSyncInboxItemType.ChildDeletedInUse:
                {
                    var owner = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
                    return owner is { DeletedAtUtc: null } && await HeldChildOfAsync(s, item, owner, ct) is not null;
                }
                case DataSyncInboxItemType.MassChildDeletion:
                {
                    var owner = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
                    if (owner is not { DeletedAtUtc: null } || item.LinkId is not { } massLink) return false;
                    var row = await s.BaseRowAsync(massLink, item.Kind, owner.SyncKey, ct);
                    return row is { PendingReason: DataSyncPendingReason.MassChildDeletion } &&
                           (item.RecordHash is null || row.PendingRecordHash == item.RecordHash);
                }
                default:
                    return false;
            }
        }

        private readonly HashSet<(string Kind, string LocalKey)> _released = new();

        /// <summary>
        /// The base rows an item's pending record(s) wait on: its own key's row, else its entity's; for row M every
        /// record the item lists, each under its own primary key.
        /// </summary>
        private async Task<List<(string Kind, SyncKey Key)>> PendingKeysOfAsync(DataSyncInboxItemDbModel item, int linkId,
            CancellationToken ct)
        {
            var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "PayloadJson");
            if (payload.Records is { Count: > 0 } records)
            {
                var primaries = records.Select(r => r.PrimaryKey).ToHashSet(StringComparer.Ordinal);
                var rows = await s.Db.DataSyncPeerBases.AsNoTracking()
                    .Where(b => b.LinkId == linkId && b.Kind == item.Kind && b.PendingReason != null).ToListAsync(ct);
                return rows.Where(b => DataSyncStoredJson.ReadRecordKeys(b.PendingRecordJson).FirstOrDefault() is { } p &&
                                       primaries.Contains(p))
                    .Select(b => (b.Kind, new SyncKey(b.SyncKey))).ToList();
            }

            if (await s.BaseRowAsync(linkId, item.Kind, item.SyncKey, ct) is { PendingReason: not null })
                return [(item.Kind, new SyncKey(item.SyncKey))];
            var owner = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (owner is not null && await s.BaseRowAsync(linkId, item.Kind, owner.SyncKey, ct) is { PendingReason: not null })
                return [(item.Kind, new SyncKey(owner.SyncKey))];
            return [];
        }

        #endregion

        #region Conflicts (KeepLocal, UseRemote, UseCustom)

        /// <summary>
        /// Every open conflict item of one entity, with every device, in one batch (§9.2): the chosen value per path and
        /// device is written, the revision is <c>Max(L, R of every item) + self</c>, each link's base takes its record
        /// and every item closes. When the entity has an open conflict item the batch does not hold, nothing of it is
        /// applied: a partial resolution would settle the rest silently in this device's favour.
        /// </summary>
        private async Task ResolveConflictsAsync(
            IReadOnlyList<(DataSyncResolveInput Input, DataSyncInboxItemDbModel Item)> batch, CancellationToken ct)
        {
            var first = batch[0].Item;
            var row = await s.OwnerAsync(first.Kind, first.SyncKey, ct);
            if (row is not { DeletedAtUtc: null }) return;
            var keys = await s.KeysOfAsync(row, ct);
            var keyValues = keys.All.Select(k => k.Value).ToList();
            var open = await s.Db.DataSyncInboxItems.AsNoTracking()
                .Where(i => i.ClosedAtUtc == null && i.Kind == first.Kind && keyValues.Contains(i.SyncKey) &&
                            (i.Type == DataSyncInboxItemType.FieldConflict || i.Type == DataSyncInboxItemType.ChildRenameConflict))
                .Select(i => i.Id).ToListAsync(ct);
            if (open.Except(batch.Select(b => b.Item.Id)).Any()) return;

            var kind = row.Kind;
            var codec = s.Adapter(kind).Codec;
            var current = (await writes.ReReadAsync(kind, row.LocalKey, ct));
            var before = codec.ReadLocal(current.Content);
            var json = codec.Write(before);
            var childrenLocal = row.ChildrenLocal;
            var remote = DataSyncVersionVector.Empty;
            var agreed = new Dictionary<int, DataSyncWireRecord>();
            foreach (var (input, item) in batch)
            {
                var baseRow = await s.BaseRowAsync(item.LinkId!.Value, kind, row.SyncKey, ct);
                if (baseRow is null) return;
                var peerBase = DataSyncStore.ToPeerBase(baseRow);
                if (peerBase.Pending is not { } pending) return;
                remote = DataSyncVersionVector.Max(remote, pending.Record.Vv);
                agreed[item.LinkId.Value] = pending.Record;
                if (input.Action == DataSyncInboxAction.KeepLocal) continue;
                if (item.SubjectPath == DataSyncChangeLists.ChildrenLocalPath)
                {
                    // §3.6: the flag is the side row's, not the content's.
                    childrenLocal = input.Action == DataSyncInboxAction.UseRemote
                        ? DataSyncRecordValidation.ChildrenLocalOf(pending.Record.Content)
                        : childrenLocal;
                    continue;
                }

                var written = input.Action == DataSyncInboxAction.UseCustom
                    ? DataSyncFieldEdits.TryUseCustom(json, item.SubjectPath, input.CustomValue ?? "", peerBase.ChildMap)
                    : RemoteJson(codec, pending.Record) is { } theirs &&
                      DataSyncFieldEdits.TryUseRemote(json, theirs, item.SubjectPath, peerBase.ChildMap);
                if (!written) return;
            }

            if (!await WriteContentAsync(kind, row, before, json, "resolve", ct)) return;
            var decision = new DataSyncRevisionDecision(kind, keys, row.LocalKey, DataSyncRevisionKind.Resolution, remote,
                null, false, false, row.OrderKey, DataSyncEntityForms.ReadUnknown(row.UnknownJson), childrenLocal, null);
            await writes.RecordLiveAsync(kind, row.LocalKey, before, decision, null, null, EntityKeys.None, null, ct);
            foreach (var (linkId, record) in agreed)
            {
                await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(row.SyncKey),
                    DataSyncBaseState.Normal, null, record, null, null, true)], ct);
                LinkIds.Add(linkId);
            }

            foreach (var (input, item) in batch) await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
        }

        #endregion

        private async Task ResolveOneAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item, CancellationToken ct)
        {
            if (item.LinkId is { } linkId) LinkIds.Add(linkId);
            switch (item.Type)
            {
                case DataSyncInboxItemType.FieldConflict or DataSyncInboxItemType.ChildRenameConflict:
                    await DetachAsync(item, input.Action, ct);
                    break;
                case DataSyncInboxItemType.TypeChange:
                    await ResolveTypeChangeAsync(input, item, ct);
                    break;
                case DataSyncInboxItemType.DeletedThere:
                    await ResolveDeletedThereAsync(input, item, ct);
                    break;
                case DataSyncInboxItemType.ChildDeletedInUse:
                    await ResolveChildInUseAsync(input, item, ct);
                    break;
                case DataSyncInboxItemType.DeletedHereEditedThere:
                    await ResolveDeletedHereAsync(input, item, ct);
                    break;
                case DataSyncInboxItemType.LinkSuggestion:
                    await ResolveSuggestionAsync(input, item, ct);
                    break;
                case DataSyncInboxItemType.IdentityConflict:
                    await ResolveIdentityAsync(input, item, ct);
                    break;
                case DataSyncInboxItemType.MassChildDeletion:
                    await ResolveMassDeletionAsync(input, item, ct);
                    break;
                case DataSyncInboxItemType.LargeChange:
                    await ResolveLargeChangeAsync(input, item, ct);
                    break;
                case DataSyncInboxItemType.SuspectedLostUpdate:
                    await ResolveLostUpdateAsync(input, item, ct);
                    break;
            }
        }

        #region Detach, type changes, deletions

        /// <summary>
        /// Detach (§9.2): the entity stops publishing (a Seq bump); every open item of it closes; its pending records
        /// are cleared and <c>PublishHeld</c> is cleared. Its holds become local-only.
        /// </summary>
        private async Task DetachAsync(DataSyncInboxItemDbModel item, DataSyncInboxAction action, CancellationToken ct)
        {
            var row = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (row is not { DeletedAtUtc: null }) return;
            await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, action, ct);
            await s.Store.SetEntityStateAsync(row.Kind, row.LocalKey, DataSyncEntitySyncState.Detached, ct);
            writes.Touch(row);
        }

        private async Task ResolveTypeChangeAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            if (input.Action == DataSyncInboxAction.Detach)
            {
                await DetachAsync(item, input.Action, ct);
                return;
            }

            var linkId = item.LinkId!.Value;
            var row = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (row is not { DeletedAtUtc: null }) return;
            var baseRow = await s.BaseRowAsync(linkId, row.Kind, row.SyncKey, ct);
            if (baseRow is null || DataSyncStore.ToPeerBase(baseRow).Pending is not { } pending) return;
            var kind = row.Kind;
            var codec = s.Adapter(kind).Codec;

            if (input.Action == DataSyncInboxAction.KeepLocal)
            {
                // A revision that dominates R with the local type; the peer then gets the TypeChange item.
                var current = codec.ReadLocal((await writes.ReReadAsync(kind, row.LocalKey, ct)).Content);
                await writes.RecordLiveAsync(kind, row.LocalKey, current, Revision(row, await s.KeysOfAsync(row, ct),
                    DataSyncRevisionKind.Resolution, pending.Record.Vv), null, null, EntityKeys.None, linkId, ct);
                await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(row.SyncKey),
                    DataSyncBaseState.Normal, null, pending.Record, null, null, true)], ct);
                await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
                return;
            }

            // Convert happens only when phase two can follow it in this transaction: a converted entity that no merge
            // completes would be published half-converted by the next Refresh. A link that is paused or off merges
            // nothing now, so the item waits, open.
            if (await s.LinkAsync(linkId, ct) is not { State: not (DataSyncLinkState.Paused or DataSyncLinkState.Stopped) })
                return;

            // Convert, phase one (§8.5.6): the subtype changes through the service, which converts values.
            var theirs = RemoteContent(codec, pending.Record);
            var subtype = theirs is null ? null : codec.SubtypeOf(theirs);
            if (subtype is null) return;
            var before = codec.ReadLocal((await writes.ReReadAsync(kind, row.LocalKey, ct)).Content);
            var fromSubtype = codec.SubtypeOf(before);
            var raw = await s.Adapter(kind).CapturePreImageAsync([row.LocalKey], ct);
            var savepoint = "convert" + item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await s.SavepointAsync(savepoint, ct);
            var outcome = await s.Writer(kind).ApplyAsync(new ApplyBatch(kind,
                [new ChangeSubtypeOperation(DataSyncMergeItemIds.Of(kind, new SyncKey(row.SyncKey)), row.LocalKey,
                    row.LocalHash, subtype)]), ct);
            writes.Written(kind);
            if (outcome.ChangedDuringApplyItemIds.Count > 0)
            {
                await s.RollbackToSavepointAsync(savepoint);
                writes.Written(kind);
                return;
            }

            // The fresh local hash (phase two merges against it, §8.5.6); no revision until phase two.
            var converted = await writes.ReReadAsync(kind, row.LocalKey, ct);
            row.LocalHash = Bakabase.Modules.DataSync.Canonical.ContentHash.Of(converted.Content);
            row.RawHash = null;
            row.UpdatedAtUtc = s.Now;

            // Phase two: the waiting record meets an entity of its own type (Merge3 Convert), with no Refresh between.
            // A merge that would pause (or a regression already reported) takes phase one back: the item waits, open.
            var key = (kind, new SyncKey(row.SyncKey));
            if (await PrepareRemergeAsync(linkId, [key], keepWhenStopped: false, ct) is not { } remerge)
            {
                await s.RollbackToSavepointAsync(savepoint);
                writes.Written(kind);
                return;
            }

            _recorder.TypeChanged++;
            _recorder.ChangedDefinitions.Add((kind, row.LocalKey));
            _recorder.PreImages.Add(new DataSyncEntityPreImage(kind, row.Id, row.LocalKey, codec.NameOf(before),
                DataSyncPreImageActions.TypeChanged, linkId, (await s.KeysOfAsync(row, ct)).All.Select(k => k.Value).ToList(),
                row.LocalHash, Content: codec.Write(before), Row: raw.GetValueOrDefault(row.LocalKey),
                FromSubtype: fromSubtype, ToSubtype: subtype));
            await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
            await WriteRemergeAsync(remerge, ct);
            await s.ReleaseSavepointAsync(savepoint, ct);
        }

        private async Task ResolveDeletedThereAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            if (input.Action == DataSyncInboxAction.KeepHereOnly)
            {
                await DetachAsync(item, input.Action, ct);
                return;
            }

            var linkId = item.LinkId!.Value;
            var row = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (row is not { DeletedAtUtc: null }) return;
            var baseRow = await s.BaseRowAsync(linkId, row.Kind, row.SyncKey, ct);
            if (baseRow is null || DataSyncStore.ToPeerBase(baseRow).Pending is not { } pending) return;
            var kind = row.Kind;
            var codec = s.Adapter(kind).Codec;
            var keys = await s.KeysOfAsync(row, ct);
            var current = codec.ReadLocal((await writes.ReReadAsync(kind, row.LocalKey, ct)).Content);

            if (input.Action == DataSyncInboxAction.RestoreEverywhere)
            {
                // A revision that dominates the tombstone; the peer gets DeletedHereEditedThere.
                await writes.RecordLiveAsync(kind, row.LocalKey, current,
                    Revision(row, keys, DataSyncRevisionKind.Resolution, pending.Record.Vv), null, null, EntityKeys.None,
                    linkId, ct);
                await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(row.SyncKey),
                    baseRow.State, baseRow.ExclusionReason, null, null, null, true)], ct);
                await CloseEntityItemsAsync(kind, keys, input.Action, ct);
                return;
            }

            // DeleteHere: through the service (values go with it, §8.10.6), after the backup (§8.10.4).
            var raw = await s.Adapter(kind).CapturePreImageAsync([row.LocalKey], ct);
            var itemId = DataSyncMergeItemIds.Of(kind, new SyncKey(row.SyncKey));
            var outcome = await s.Writer(kind).ApplyAsync(new ApplyBatch(kind,
                [new DeleteEntityOperation(itemId, row.LocalKey, row.LocalHash)]), ct);
            writes.Written(kind);
            if (outcome.ChangedDuringApplyItemIds.Count > 0) return;
            await CloseEntityItemsAsync(kind, keys, input.Action, ct);
            await writes.RecordDeleteAsync(kind, row.LocalKey, current, raw.GetValueOrDefault(row.LocalKey),
                Revision(row, keys, DataSyncRevisionKind.AcceptRemoteDelete, pending.Record.Vv) with { LocalKey = row.LocalKey },
                pending.Record, linkId, ct);
            _recorder.Deleted++;
            await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(row.SyncKey),
                DataSyncBaseState.Normal, null, pending.Record, null, null, true)], ct);
        }

        private async Task ResolveChildInUseAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            var linkId = item.LinkId!.Value;
            var row = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (row is not { DeletedAtUtc: null } || await HeldChildOfAsync(s, item, row, ct) is not { } child) return;
            var kind = row.Kind;
            var codec = s.Adapter(kind).Codec;
            var overlay = DataSyncStoredJson.ReadOverlay(row.OverlayJson);
            var hold = new DataSyncHeldChild(child, linkId);
            var current = codec.ReadLocal((await writes.ReReadAsync(kind, row.LocalKey, ct)).Content);
            await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
            switch (input.Action)
            {
                case DataSyncInboxAction.DeleteHere:
                {
                    // The held class and its subtree are removed; they were never published, so no revision. The
                    // values keep the id and show nothing (unified miss behaviour).
                    var json = codec.Write(current);
                    var nodes = DataSyncContentNodes.Index(json);
                    if (!nodes.TryGetValue(child, out var node)) return;
                    DataSyncContentNodes.Remove(node);
                    if (!await WriteContentAsync(kind, row, current, json, "deleteHeldChild", ct)) return;
                    var remaining = codec.ChildrenOf(codec.ReadLocal(json)).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
                    var release = overlay.HeldChildren.Where(h => !remaining.Contains(h.ChildId)).ToList();
                    await writes.RecordLiveAsync(kind, row.LocalKey, current, null, null,
                        new DataSyncOverlayChange(kind, row.LocalKey, [], release), EntityKeys.None, linkId, ct);
                    if (overlay.LocalOnlyChildren.Any(c => !remaining.Contains(c)))
                    {
                        var after = DataSyncStoredJson.ReadOverlay(row.OverlayJson);
                        row.OverlayJson = DataSyncStoredJson.WriteOverlay(after with
                        {
                            LocalOnlyChildren = after.LocalOnlyChildren.Where(remaining.Contains).ToList(),
                        });
                    }

                    _recorder.Deleted++;
                    break;
                }
                case DataSyncInboxAction.KeepHereOnly:
                    // Held and local-only children are both withheld: what the entity publishes stays the same, so
                    // there is nothing for readers to fetch again (§6.2).
                    row.OverlayJson = DataSyncStoredJson.WriteOverlay(new DataSyncOverlay(
                        overlay.LocalOnlyChildren.Where(c => c != child).Append(child).ToList(),
                        overlay.HeldChildren.Where(h => h != hold).ToList()));
                    row.UpdatedAtUtc = s.Now;
                    break;
                default:
                {
                    // RestoreEverywhere: released and published again; the peer receives it as an addition.
                    var peerBase = await s.BaseRowAsync(linkId, kind, row.SyncKey, ct);
                    await writes.RecordLiveAsync(kind, row.LocalKey, current,
                        Revision(row, await s.KeysOfAsync(row, ct), DataSyncRevisionKind.Resolution,
                            DataSyncStoredJson.ReadVv(peerBase?.VvJson)),
                        null, new DataSyncOverlayChange(kind, row.LocalKey, [], [hold]), EntityKeys.None, linkId, ct);
                    break;
                }
            }
        }

        private async Task ResolveDeletedHereAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            var linkId = item.LinkId!.Value;
            var tombstone = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (tombstone is not { DeletedAtUtc: not null }) return;
            var kind = tombstone.Kind;
            var baseRow = await s.BaseRowAsync(linkId, kind, tombstone.SyncKey, ct);
            if (baseRow is null || DataSyncStore.ToPeerBase(baseRow).Pending is not { } pending) return;

            // Row T3 for several of the peer's lineages: one decision covers every record the item lists; each
            // record's history is absorbed and each record's pending row is cleared.
            var others = (await PendingKeysOfAsync(item, linkId, ct))
                .Where(k => k.Key.Value != tombstone.SyncKey).ToList();
            var remote = pending.Record.Vv;
            foreach (var (otherKind, otherKey) in others)
            {
                var other = await s.BaseRowAsync(linkId, otherKind, otherKey.Value, ct);
                if (other is null || DataSyncStore.ToPeerBase(other).Pending is not { } p) continue;
                remote = DataSyncVersionVector.Max(remote, p.Record.Vv);
                await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(otherKind, otherKey, other.State,
                    other.ExclusionReason, null, null, null, true)], ct);
            }

            await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
            if (input.Action == DataSyncInboxAction.KeepDeleted)
            {
                await writes.RecordTombstoneRevisionAsync(kind, new SyncKey(tombstone.SyncKey),
                    DataSyncRevisionKind.KeepDeleted, remote, null, ct);
                await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(tombstone.SyncKey),
                    baseRow.State, baseRow.ExclusionReason, null, null, null, true)], ct);
                return;
            }

            // RestoreHere: created from R, reviving the tombstone's key, with Max(T, R) + self.
            var codec = s.Adapter(kind).Codec;
            var theirs = RemoteContent(codec, pending.Record);
            if (theirs is null) return;
            var prepared = codec.PrepareCreate(theirs, null);
            var keys = await s.KeysOfAsync(tombstone, ct);
            var allKeys = new EntityKeys(keys.All.Concat(pending.Record.Keys.Select(k => new SyncKey(k))).Distinct().ToList());
            var created = await CreateAsync(kind, allKeys, pending.Record, prepared.Content,
                DataSyncRevisionKind.Resolution, DataSyncVersionVector.Max(remote,
                    DataSyncVersionVector.ParseStored(tombstone.VvJson)), createdBySync: false, linkId, ct);
            if (created is null) return;
            await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(created.SyncKey),
                DataSyncBaseState.Normal, null, pending.Record, prepared.ChildIdMap, null, true)], ct);
        }

        #endregion

        #region Identity: link suggestions, identity conflicts

        private async Task ResolveSuggestionAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            var linkId = item.LinkId!.Value;
            var baseRow = await s.BaseRowAsync(linkId, item.Kind, item.SyncKey, ct);
            if (baseRow is null || DataSyncStore.ToPeerBase(baseRow).Pending is not { } pending) return;
            var record = pending.Record;
            var kind = item.Kind;
            switch (input.Action)
            {
                case DataSyncInboxAction.Link:
                {
                    // BindOnly (R's keys become the candidate's aliases), then R merges with it as K4/K5/K6 without a
                    // base; the Unbound base is replaced by the candidate's.
                    var target = input.TargetLocalKey is null ? null : await TargetRowAsync(kind, input.TargetLocalKey, ct);
                    if (target is null) return;
                    var targetKeys = await s.KeysOfAsync(target, ct);
                    var aliases = record.Keys.Select(k => new SyncKey(k)).Where(k => !targetKeys.Contains(k)).ToList();
                    var itemId = DataSyncMergeItemIds.Of(kind, new SyncKey(record.Keys[0]));
                    var check = await s.Identity.CheckApplyAsync(
                        [new ApplyBatch(kind, [new BindOnlyOperation(itemId, target.LocalKey, new EntityKeys(aliases))])], ct);
                    if (check.RefusedItemIds.Count > 0) return;
                    await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
                    var codec = s.Adapter(kind).Codec;
                    var current = codec.ReadLocal((await writes.ReReadAsync(kind, target.LocalKey, ct)).Content);
                    var row = await writes.RecordLiveAsync(kind, target.LocalKey, current, null, null, null,
                        aliases.Count == 0 ? EntityKeys.None : new EntityKeys(aliases), linkId, ct);
                    _recorder.Linked++;
                    _recorder.PreImages.Add(new DataSyncEntityPreImage(kind, row.Id, row.LocalKey, codec.NameOf(current),
                        DataSyncPreImageActions.Bound, linkId, (await s.KeysOfAsync(row, ct)).All.Select(k => k.Value).ToList(),
                        row.LocalHash, AliasesAdded: aliases.Select(k => k.Value).ToList()));
                    await MoveToEntityRowAsync(linkId, kind, item.SyncKey, row.SyncKey, pending, ct);
                    await RemergeAsync(linkId, [(kind, new SyncKey(row.SyncKey))], ct);
                    return;
                }
                case DataSyncInboxAction.Skip:
                    await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(item.SyncKey),
                        DataSyncBaseState.Excluded, DataSyncExclusionReason.Skipped, record, null, null, true)], ct);
                    _recorder.Skipped++;
                    await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
                    return;
                default:
                {
                    // KeepBoth: created from R under another name; in two-way the peer's copy is renamed too.
                    var codec = s.Adapter(kind).Codec;
                    var theirs = RemoteContent(codec, record);
                    if (theirs is null) return;
                    var link = await s.LinkAsync(linkId, ct);
                    var name = string.IsNullOrWhiteSpace(input.NewName)
                        ? $"{codec.NameOf(theirs)} ({link?.PeerName})"
                        : input.NewName.Trim();
                    var prepared = codec.PrepareCreate(theirs, name);
                    await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
                    var created = await CreateAsync(kind, new EntityKeys(record.Keys.Select(k => new SyncKey(k)).ToList()),
                        record, prepared.Content, DataSyncRevisionKind.Create, record.Vv, createdBySync: true, linkId, ct);
                    if (created is null) return;
                    await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(created.SyncKey),
                        DataSyncBaseState.Normal, null, record, prepared.ChildIdMap, null, true)], ct);
                    return;
                }
            }
        }

        private async Task ResolveIdentityAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            var linkId = item.LinkId!.Value;
            var kind = item.Kind;
            var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "PayloadJson");
            var rowM = payload.Records is { Count: > 0 };
            if (input.Action == DataSyncInboxAction.Detach)
            {
                if (rowM)
                {
                    await DetachAsync(item, input.Action, ct);
                    return;
                }

                var baseRow = await s.BaseRowAsync(linkId, kind, item.SyncKey, ct);
                var record = baseRow is null ? null : DataSyncStore.ToPeerBase(baseRow).Pending?.Record;
                await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
                await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(item.SyncKey),
                    DataSyncBaseState.Excluded, DataSyncExclusionReason.DroppedIdentity, record, null, null, true)], ct);
                return;
            }

            if (!rowM)
            {
                // Row I, KeepWithEntity: the record's keys that belong to the other candidates move to the chosen one
                // (a candidate whose primary moves is re-keyed); then R merges with it.
                var baseRow = await s.BaseRowAsync(linkId, kind, item.SyncKey, ct);
                if (baseRow is null || DataSyncStore.ToPeerBase(baseRow).Pending is not { } pending) return;
                var chosen = input.TargetLocalKey is null ? null : await TargetRowAsync(kind, input.TargetLocalKey, ct);
                if (chosen is null) return;
                await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
                foreach (var key in pending.Record.Keys)
                {
                    var owner = await s.OwnerAsync(kind, key, ct);
                    if (owner is not { DeletedAtUtc: null } || owner.Id == chosen.Id) continue;
                    await s.Identity.MoveKeysAsync(owner, chosen, [new SyncKey(key)], _recorder.Identity, ct);
                    writes.Touch(owner);
                }

                writes.Touch(chosen);
                _recorder.Linked++;
                await MoveToEntityRowAsync(linkId, kind, item.SyncKey, chosen.SyncKey, pending, ct);
                await RemergeAsync(linkId, [(kind, new SyncKey(chosen.SyncKey))], ct);
                return;
            }

            // Row M, KeepRecordLinked: the other records' keys are dropped from the entity into
            // Excluded(DroppedIdentity) bases; then the kept record merges with the entity.
            var entity = await s.OwnerAsync(kind, item.SyncKey, ct);
            if (entity is not { DeletedAtUtc: null } || input.TargetRecordKey is not { } kept) return;
            var waiting = new List<(string Primary, DataSyncWireRecord Record)>();
            foreach (var (_, key) in await PendingKeysOfAsync(item, linkId, ct))
            {
                var row = await s.BaseRowAsync(linkId, kind, key.Value, ct);
                if (row is not null && DataSyncStore.ToPeerBase(row).Pending is { } p) waiting.Add((key.Value, p.Record));
            }

            await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
            // Keys the kept record claims too stay the entity's.
            var keptKeys = waiting.Where(w => w.Record.Keys[0] == kept).SelectMany(w => w.Record.Keys)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var (rowKey, record) in waiting.Where(w => w.Record.Keys[0] != kept))
            {
                var dropped = record.Keys.Where(k => !keptKeys.Contains(k)).ToList();
                await s.Identity.DropKeysAsync(entity, dropped.Select(k => new SyncKey(k)), _recorder.Identity, ct);
                // The dropped record is not synced here from now on (listed with [Include]); its row no longer waits.
                if (await s.BaseRowAsync(linkId, kind, rowKey, ct) is { } waitedOn)
                {
                    DataSyncStore.ClearPending(waitedOn);
                    waitedOn.UpdatedAtUtc = s.Now;
                    await s.Db.SaveChangesAsync(ct);
                }

                await s.Store.ExcludeAsync(linkId, kind, record.Keys[0], DataSyncExclusionReason.DroppedIdentity,
                    dropped, ct);
            }

            writes.Touch(entity);
            var remerge = new List<(string, SyncKey)>();
            foreach (var (rowKey, record) in waiting.Where(w => w.Record.Keys[0] == kept))
            {
                // The rows a rekey deleted lose their records: the kept one waits again, under its own primary.
                if (await s.BaseRowAsync(linkId, kind, rowKey, ct) is not { PendingReason: not null })
                {
                    await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(record.Keys[0]),
                        DataSyncBaseState.Unbound, null, null, null,
                        DataSyncPendingRecords.Create(record, DataSyncPendingReason.Retry, 0, DataSyncMergeFlags.None),
                        false)], ct);
                    remerge.Add((kind, new SyncKey(record.Keys[0])));
                }
                else
                {
                    remerge.Add((kind, new SyncKey(rowKey)));
                }
            }

            await RemergeAsync(linkId, remerge, ct);
        }

        /// <summary>
        /// "Then R merges with the entity" (§9.2): the record moves from the row it waited on to the entity's own row,
        /// as a <c>Retry</c> pending record (the entity's base, if any, is kept); the row it left is removed.
        /// </summary>
        private async Task MoveToEntityRowAsync(int linkId, string kind, string fromKey, string toKey,
            DataSyncPendingRecord pending, CancellationToken ct)
        {
            var existing = await s.BaseRowAsync(linkId, kind, toKey, ct);
            if (fromKey != toKey && await s.BaseRowAsync(linkId, kind, fromKey, ct) is { } from)
            {
                s.Db.DataSyncPeerBases.Remove(from);
                await s.Db.SaveChangesAsync(ct);
            }

            var state = existing?.State is null or DataSyncBaseState.Excluded or DataSyncBaseState.Unbound
                ? DataSyncBaseState.Normal
                : existing.State;
            await s.Store.UpsertBasesAsync(linkId, [new DataSyncBaseUpdate(kind, new SyncKey(toKey), state, null, null,
                null, pending with { Reason = DataSyncPendingReason.Retry }, false)], ct);
        }

        #endregion

        #region State-derived: mass deletions, large changes, lost updates

        private async Task ResolveMassDeletionAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            var linkId = item.LinkId!.Value;
            var row = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (row is not { DeletedAtUtc: null }) return;
            var baseRow = await s.BaseRowAsync(linkId, row.Kind, row.SyncKey, ct);
            if (baseRow is null || DataSyncStore.ToPeerBase(baseRow).Pending is not { } pending) return;
            var mode = input.Action switch
            {
                DataSyncInboxAction.ApplyAll => DataSyncChildDeletionMode.Apply,
                DataSyncInboxAction.ReviewEach => DataSyncChildDeletionMode.ReviewEach,
                _ => DataSyncChildDeletionMode.Restore,
            };
            DataSyncStore.SetPending(baseRow, pending with { Flags = pending.Flags with { ChildDeletions = mode } });
            // §8.4 condition 5, "a resolution changed its flags": never evaluated with them, so the link's next merge
            // takes the record even when the re-merge below cannot run now (a paused link).
            baseRow.PendingEvaluatedLocalSeq = null;
            baseRow.UpdatedAtUtc = s.Now;
            await s.Db.SaveChangesAsync(ct);
            await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
            await RemergeAsync(linkId, [(row.Kind, new SyncKey(row.SyncKey))], ct);
        }

        /// <summary>
        /// Apply all (B5, §9.2): the once flag <c>SkipLargeChange</c>; <c>DataSyncApply</c> re-merges the waiting
        /// records at once (the resolving task's caller enqueues it, §8.2).
        /// </summary>
        private async Task ResolveLargeChangeAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            var link = await s.LinkAsync(item.LinkId!.Value, ct);
            if (link is null) return;
            var flags = DataSyncStoredJson.ReadFlags(link.OnceFlagsJson, "OnceFlagsJson");
            link.OnceFlagsJson = DataSyncStoredJson.WriteFlags(flags with { SkipLargeChange = true });
            link.UpdatedAtUtc = s.Now;
            await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
        }

        /// <summary>
        /// §6.5: Publish keeps this device's version (released by the Refresh that started the transaction, a local
        /// revision); Reapply writes back only the undone changes, by path and by id, as a <c>Resolution</c>. Either way
        /// the entity's <c>PublishHeld</c> pending records are re-merged on every link.
        /// </summary>
        private async Task ResolveLostUpdateAsync(DataSyncResolveInput input, DataSyncInboxItemDbModel item,
            CancellationToken ct)
        {
            var row = await s.OwnerAsync(item.Kind, item.SyncKey, ct);
            if (row is not { DeletedAtUtc: null }) return;
            var kind = row.Kind;
            if (input.Action == DataSyncInboxAction.Reapply)
            {
                var codec = s.Adapter(kind).Codec;
                var current = codec.ReadLocal((await writes.ReReadAsync(kind, row.LocalKey, ct)).Content);
                var applied = await DataSyncAppliedChangesIndex.FindLatestAsync(s.Db, kind, row.LocalKey, ct);
                if (applied is null)
                {
                    // Nothing says any more what the apply wrote: retention pruned its entry (§4.6 keeps pre-images
                    // within a budget, even inside 30 days), or it was undone. Clearing the hold alone would let the
                    // next Refresh publish the stale overwrite — the opposite of what the person asked for. The hold
                    // and the item stay; the item no longer offers Reapply, and Publish or a later decision settles it.
                    await WithdrawReapplyAsync(item, ct);
                    return;
                }

                var undone = DataSyncChangeLists.Undone(codec, current, row.ChildrenLocal, applied.Changes);
                var edit = DataSyncChangeLists.Apply(codec, current, row.ChildrenLocal, undone.Scalars, undone.Children,
                    backward: false);
                // Putting a removal back never takes away a child resources here use (§8.5.4 step 3 holds those, and
                // undo refuses AddedOptionsInUse): nothing is written, the hold and the item stay, and the card
                // names them.
                if (await Persistence.DataSyncLostUpdateGuard.InUseAsync(s.Adapter(kind), row.LocalKey, current,
                        edit.RemovedChildIds, ct) is { Count: > 0 } inUse)
                {
                    await RefuseReapplyInUseAsync(item, inUse, ct);
                    return;
                }

                if (edit.Changed && !await WriteContentAsync(kind, row, current, edit.Content, "reapply", ct)) return;
                row.PublishHeld = false;
                await writes.RecordLiveAsync(kind, row.LocalKey, current,
                    Revision(row, await s.KeysOfAsync(row, ct), DataSyncRevisionKind.Resolution, null) with
                    {
                        ChildrenLocal = edit.ChildrenLocal ?? row.ChildrenLocal,
                    }, null, null, EntityKeys.None, null, ct);
            }
            else
            {
                _released.Add((kind, row.LocalKey));
                // The Resolution becomes the entity's most recent guarded apply (§6.5), with nothing to undo: the
                // person kept this content, so a later edit inside the window is an ordinary revision.
                _recorder.Changes(new DataSyncEntityChanges(kind, row.LocalKey, [], []));
            }

            await CloseAsync(item, DataSyncInboxClosure.ResolvedHere, input.Action, ct);
            writes.Touch(row);
            var keys = (await s.KeysOfAsync(row, ct)).All.Select(k => k.Value).ToList();
            foreach (var link in await s.Db.DataSyncLinks.AsNoTracking().ToListAsync(ct))
            {
                var held = await s.Db.DataSyncPeerBases.AsNoTracking()
                    .Where(b => b.LinkId == link.Id && b.Kind == kind && keys.Contains(b.SyncKey) &&
                                b.PendingReason == DataSyncPendingReason.PublishHeld)
                    .Select(b => b.SyncKey).ToListAsync(ct);
                if (held.Count > 0) await RemergeAsync(link.Id, held.Select(k => (kind, new SyncKey(k))).ToList(), ct);
            }
        }

        /// <summary>
        /// Reapply cannot run (§6.5): the item stays open, its card says why (<c>Detail</c>) and no longer lists Reapply
        /// (<see cref="Runtime.DataSyncInboxRules.Allowed"/>). The decision counts as one that changed since the person saw it.
        /// </summary>
        private async Task WithdrawReapplyAsync(DataSyncInboxItemDbModel item, CancellationToken ct)
        {
            var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "PayloadJson");
            item.PayloadJson = DataSyncStoredJson.Write(payload with
            {
                Detail = Persistence.DataSyncLostUpdateGuard.ReapplyUnavailable,
            });
            item.UpdatedAtUtc = s.Now;
            await s.Db.SaveChangesAsync(ct);
            _recorder.ChangedSinceReview++;
        }

        /// <summary>
        /// Reapply would remove children resources here use (§6.5): nothing is written, the hold and the item stay,
        /// and the card names them (<see cref="Persistence.DataSyncLostUpdateGuard.ReapplyInUse"/>). Reapply stays
        /// offered for when nothing uses them any more. The decision counts as one that changed since the person saw it.
        /// </summary>
        private async Task RefuseReapplyInUseAsync(DataSyncInboxItemDbModel item,
            IReadOnlyList<DataSyncDisplayValue> inUse, CancellationToken ct)
        {
            var payload = DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "PayloadJson");
            item.PayloadJson = DataSyncStoredJson.Write(
                Persistence.DataSyncLostUpdateGuard.WithReapplyInUse(payload, inUse));
            item.UpdatedAtUtc = s.Now;
            await s.Db.SaveChangesAsync(ct);
            _recorder.ChangedSinceReview++;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// A re-merge in the resolving task (§9.2, §8.4): the given pending records of one link merged and written
        /// like a pull's, in this transaction. When it cannot run now — the link is paused or off, the merge would
        /// pause it, or it meets a regression already reported — the records stay re-mergeable instead: their
        /// <c>PendingEvaluatedLocalSeq</c> is cleared, so the link's next merge takes them (§8.4 condition 2's
        /// never-evaluated case), and the decision is not lost. A new regression rolls the batch back (§5.6).
        /// </summary>
        private async Task RemergeAsync(int linkId, IReadOnlyList<(string Kind, SyncKey Key)> keys, CancellationToken ct)
        {
            if (keys.Count == 0) return;
            if (await PrepareRemergeAsync(linkId, keys, keepWhenStopped: true, ct) is { } remerge)
                await WriteRemergeAsync(remerge, ct);
        }

        /// <summary>
        /// The merge half of <see cref="RemergeAsync"/>: null when it cannot run now (with
        /// <paramref name="keepWhenStopped"/>, its records are then kept re-mergeable). The input covers only the
        /// kinds of <paramref name="keys"/>.
        /// </summary>
        private async Task<(DataSyncLinkDbModel Link, DataSyncMergeInput Input, DataSyncMergeResult Result)?>
            PrepareRemergeAsync(int linkId, IReadOnlyList<(string Kind, SyncKey Key)> keys, bool keepWhenStopped,
                CancellationToken ct)
        {
            var link = await s.LinkAsync(linkId, ct);
            if (link is null) return null;
            if (link.State is not (DataSyncLinkState.Paused or DataSyncLinkState.Stopped))
            {
                var input = await DataSyncMergeInputs.BuildAsync(s, KindsOf(link, keys), null, keys, ct, _contents);
                var result = DataSyncMerger.Merge(input);
                if (result.Anomaly is { } anomaly && !anomalyReported)
                    throw new DataSyncMergeAnomalyException(linkId, link.PeerNodeId, anomaly, result.Pause, result.PauseDetail);
                if (result.Anomaly is null && result.Pause is null) return (link, input, result);
            }

            if (keepWhenStopped) await KeepToRemergeAsync(linkId, keys, ct);
            return null;
        }

        /// <summary>
        /// The link's context for merging <paramref name="keys"/> alone: only their kinds are read. With no pull, the
        /// merger evaluates nothing of the others (§8.4).
        /// </summary>
        private DataSyncLinkContext KindsOf(DataSyncLinkDbModel link, IReadOnlyList<(string Kind, SyncKey Key)> keys)
        {
            var context = DataSyncMergeInputs.LinkContext(s, link);
            var kinds = keys.Select(k => k.Kind).ToHashSet(StringComparer.Ordinal);
            return context with { Kinds = context.Kinds.Where(kinds.Contains).ToList() };
        }

        private async Task WriteRemergeAsync(
            (DataSyncLinkDbModel Link, DataSyncMergeInput Input, DataSyncMergeResult Result) remerge, CancellationToken ct)
        {
            var (link, input, result) = remerge;
            var writer = new DataSyncMergeWriter(s, writes, input, result, link.Id, null);
            await writer.WriteAsync(ct);
            await s.Store.ReconcileInboxAsync(link.Id, link.PeerNodeId, writer.Result.Inbox, writer.Result.Evaluated,
                writer.Result.ClosureHints, s.Now, ct);
            Subjects.AddRange(writer.Result.Evaluated);
        }

        /// <summary>The records wait for the link's next merge, which takes them as never evaluated (§8.4).</summary>
        private async Task KeepToRemergeAsync(int linkId, IReadOnlyList<(string Kind, SyncKey Key)> keys,
            CancellationToken ct)
        {
            foreach (var (kind, key) in keys)
            {
                if (await s.BaseRowAsync(linkId, kind, key.Value, ct) is not { PendingReason: not null } row) continue;
                row.PendingEvaluatedLocalSeq = null;
                row.UpdatedAtUtc = s.Now;
            }

            await s.Db.SaveChangesAsync(ct);
        }

        /// <summary>A create from a peer's record with a decision's revision (KeepBoth, RestoreHere).</summary>
        private async Task<DataSyncEntityDbModel?> CreateAsync(string kind, EntityKeys keys, DataSyncWireRecord record,
            object content, DataSyncRevisionKind revision, DataSyncVersionVector remote, bool createdBySync, int linkId,
            CancellationToken ct)
        {
            var codec = s.Adapter(kind).Codec;
            var itemId = DataSyncMergeItemIds.Of(kind, keys.Primary!.Value);
            var create = new CreateEntityOperation(itemId, keys, record.Origin, 0, codec.Write(content));
            var check = await s.Identity.CheckApplyAsync([new ApplyBatch(kind, [create])], ct);
            if (check.RefusedItemIds.Count > 0) return null;
            var outcome = await s.Writer(kind).ApplyAsync(new ApplyBatch(kind, [create]), ct);
            writes.Written(kind);
            if (!outcome.CreatedLocalKeysByItemId.TryGetValue(itemId, out var localKey)) return null;
            var childrenLocal = codec.Descriptor.SupportsChildrenLocal && DataSyncRecordValidation.ChildrenLocalOf(record.Content);
            var unknown = DataSyncRecordValidation.ReadContent(codec, record, s.Limits)?.Unknown;
            var decision = new DataSyncRevisionDecision(kind, keys, null, revision, remote, null, false, false,
                codec.Descriptor.HasOrder ? record.OrderKey : null, unknown, childrenLocal,
                revision == DataSyncRevisionKind.Create ? record.EditedBy : null);
            var row = await writes.RecordCreateAsync(kind, localKey, keys, record.Origin, decision, record, linkId,
                createdBySync, ct);
            _recorder.Created++;
            _recorder.ChangedDefinitions.Add((kind, localKey));
            if (codec.Descriptor.HasOrder) await PlaceAsync(kind, ct);
            return row;
        }

        /// <summary>A definition created by a decision takes its place in the shared order (§3.7).</summary>
        private async Task PlaceAsync(string kind, CancellationToken ct)
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
            writes.Written(kind);
        }

        /// <summary>
        /// Writes edited content through the adapter (an update with the added and removed children), after reading
        /// it back through the codec. False when the entity changed since it was read (ChangedDuringApply) or the edit
        /// does not read back.
        /// </summary>
        private async Task<bool> WriteContentAsync(string kind, DataSyncEntityDbModel row, object before, JsonObject edited,
            string what, CancellationToken ct)
        {
            var codec = s.Adapter(kind).Codec;
            JsonObject merged;
            try
            {
                merged = codec.Write(codec.ReadLocal(edited));
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException
                                          or System.Text.Json.JsonException)
            {
                runner._logger.LogWarning(e, "Data sync could not {What} {Kind}/{LocalKey}: the edit does not read back.",
                    what, kind, row.LocalKey);
                return false;
            }

            var beforeJson = codec.Write(before);
            if (JsonNode.DeepEquals(merged, beforeJson)) return true;
            var beforeIds = codec.ChildrenOf(before).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var afterIds = codec.ChildrenOf(codec.ReadLocal(merged)).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var update = new UpdateEntityOperation(DataSyncMergeItemIds.Of(kind, new SyncKey(row.SyncKey)), row.LocalKey,
                row.LocalHash, merged, EntityKeys.None, afterIds.Except(beforeIds).ToList(),
                beforeIds.Except(afterIds).ToList());
            var outcome = await s.Writer(kind).ApplyAsync(new ApplyBatch(kind, [update]), ct);
            writes.Written(kind);
            return outcome.ChangedDuringApplyItemIds.Count == 0;
        }

        /// <summary>A live, synced entity a decision names as its target.</summary>
        private async Task<DataSyncEntityDbModel?> TargetRowAsync(string kind, string localKey, CancellationToken ct)
        {
            await s.Store.FlushAsync(ct);
            return await s.Db.DataSyncEntities.SingleOrDefaultAsync(e =>
                e.Kind == kind && e.LocalKey == localKey && e.DeletedAtUtc == null &&
                e.State == DataSyncEntitySyncState.Synced, ct);
        }

        private JsonObject? RemoteJson(IDataSyncKindCodec codec, DataSyncWireRecord record) =>
            RemoteContent(codec, record) is { } content ? codec.Write(content) : null;

        private object? RemoteContent(IDataSyncKindCodec codec, DataSyncWireRecord record) =>
            DataSyncRecordValidation.ReadContent(codec, record, s.Limits)?.Content;

        private static DataSyncRevisionDecision Revision(DataSyncEntityDbModel row, EntityKeys keys,
            DataSyncRevisionKind kind, DataSyncVersionVector? remote) =>
            new(row.Kind, keys, row.LocalKey, kind, remote, null, false, false, row.OrderKey,
                DataSyncEntityForms.ReadUnknown(row.UnknownJson), row.ChildrenLocal, null);

        private async Task CloseAsync(DataSyncInboxItemDbModel item, DataSyncInboxClosure closure,
            DataSyncInboxAction? action, CancellationToken ct)
        {
            // The item is the context's tracked row: closing it updates this instance too.
            await s.Store.CloseItemsAsync([item.Id], closure,
                action, closure == DataSyncInboxClosure.ResolvedHere ? s.Self : null, null, ct);
            if (closure == DataSyncInboxClosure.ResolvedHere) Closed.Add(item.Id);
        }

        /// <summary>Every open item of the entity closes resolved here (a deletion decided, a restore everywhere).</summary>
        private async Task CloseEntityItemsAsync(string kind, EntityKeys keys, DataSyncInboxAction action,
            CancellationToken ct)
        {
            var keyValues = keys.All.Select(k => k.Value).ToList();
            var ids = await s.Db.DataSyncInboxItems.AsNoTracking()
                .Where(i => i.ClosedAtUtc == null && i.Kind == kind && keyValues.Contains(i.SyncKey))
                .Select(i => i.Id).ToListAsync(ct);
            await s.Store.CloseItemsAsync(ids, DataSyncInboxClosure.ResolvedHere, action, s.Self, null, ct);
            Closed.UnionWith(ids);
        }

        #endregion
    }
}

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
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Ordering;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

// §8.10.3: the first-link review and copy once (v3.1 §8.2–§8.4).
public sealed partial class DataSyncApplyRunner
{
    /// <summary>
    /// Applies a staged review with the person's decisions (§8.10.3, §8.3 step 5). Inside the transaction, Refresh, the
    /// re-plan and a non-strict Resolve run again: an item whose plan changed since the person saw it is
    /// <c>ChangedSinceReview</c> and its record a <c>Retry</c> pending record, merged by the link's first ordinary pull.
    /// The writes go in chunks; then bases (with the merges' child maps), pending records, exclusions of skipped items,
    /// revisions, order and — last — the link: cursors, first contact, <c>Active</c> (or <c>Stopped</c> for copy once).
    /// </summary>
    /// <returns>The history entry (<c>FirstLink</c> or <c>CopyOnce</c>), or null when the attempt exited early.</returns>
    public async Task<int?> RunReviewAsync(string reviewId, IReadOnlyList<DataSyncPlanDecision> decisions,
        DataSyncApplyOptions options, BTaskArgs args)
    {
        ArgumentException.ThrowIfNullOrEmpty(reviewId);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(args);
        var ct = args.CancellationToken;
        if (!await StartAsync(args)) return null;
        await WaitStartupVerifiedAsync(ct);
        using var lease = await _gate.EnterAsync(null, ct);
        if (!MayRun(args)) return null;
        var review = _reviews.Get(reviewId) ??
                     throw new BTaskException("ReviewExpired", "The review is no longer staged; fetch it again.");
        await _guard.CheckAsync(lease, ct);

        var kinds = review.Pull.Kinds.Select(k => k.Kind).ToList();
        var logId = await InTransactionAsync(lease, async s =>
        {
            var started = Stopwatch.GetTimestamp();
            var recorder = new DataSyncApplyRecorder();
            await RefreshOrFailAsync(s, lease, kinds, ct);
            var link = review.LinkId is { } id ? await s.LinkAsync(id, ct) : null;
            var mode = review.CopyOnce ? DataSyncLinkMode.Off : link?.Mode ?? DataSyncLinkMode.Off;
            var local = await DataSyncMergeInputs.ReadLocalAsync(s, kinds.Where(s.Kinds.ContainsKey), ct);
            var input = DataSyncPlanInput.FromLocalState(review.Pull, local, s.Codecs, s.State.NodeId, mode);
            var plan = DataSyncPlanner.Plan(input);
            var resolved = DataSyncPlanner.Resolve(plan, input, decisions, strict: false);

            await new ReviewWriter(this, s, review, plan, resolved, local, link, recorder).WriteAsync(ct);

            if (link is not null)
            {
                var now = s.Now;
                var cursors = new Dictionary<string, long>(
                    DataSyncStoredJson.ReadCounters(link.CursorsJson, "CursorsJson"), StringComparer.Ordinal);
                foreach (var kind in review.Pull.Kinds) cursors[kind.Kind] = kind.MaxSeq;
                link.CursorsJson = DataSyncStoredJson.WriteCounters(cursors);
                var completed = DataSyncStoredJson.ReadStrings(link.FirstContactKindsJson, "FirstContactKindsJson")
                    .Concat(kinds).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
                link.FirstContactKindsJson = DataSyncStoredJson.Write(completed);
                link.FirstContactCompletedAtUtc ??= now;
                link.LastSyncedAtUtc = now;
                link.LastAttemptAtUtc = now;
                link.LastFullReconciliationAtUtc = now;
                link.ConsecutiveFailures = 0;
                link.LastErrorCode = null;
                link.LastErrorDetail = null;
                link.ReviewId = null;
                if (review.CopyOnce)
                {
                    if (link.Mode != DataSyncLinkMode.Off) link.LastMode = link.Mode;
                    link.Mode = DataSyncLinkMode.Off;
                    link.State = DataSyncLinkState.Stopped;
                }
                else if (link.State is DataSyncLinkState.AwaitingReview or DataSyncLinkState.AwaitingAccess)
                {
                    link.State = DataSyncLinkState.Active;
                }

                link.UpdatedAtUtc = now;
            }

            var log = await s.Store.AddHistoryAsync(recorder.ToLog(
                review.CopyOnce ? DataSyncHistoryKind.CopyOnce : DataSyncHistoryKind.FirstLink, link, args.Task.Id, s.Now,
                ElapsedMs(started)), ct);
            await CommitAsync(s, ct);
            await AfterCommitAsync(s, recorder, kinds,
                review.CopyOnce ? DataSyncHistoryKind.CopyOnce : DataSyncHistoryKind.FirstLink, log, link?.Id, ct);
            return log;
        }, ct);

        _reviews.MarkApplied(reviewId, logId);
        return logId;
    }

    /// <summary>Refresh at the start of a task body; a skipped one ends the task with nothing applied.</summary>
    private async Task RefreshOrFailAsync(DataSyncApplySession s, DataSyncGateLease lease,
        IReadOnlyCollection<string> kinds, CancellationToken ct, DataSyncRefreshOptions? options = null)
    {
        try
        {
            await RefreshAsync(s, lease, kinds, options, ct);
        }
        catch (DataSyncActorUnverifiedException)
        {
            throw new BTaskException("ActorUnverified", "Nothing was changed; it will run again.");
        }
    }

    /// <summary>The review's writes (§8.3 step 5), in chunks as §8.10.2.</summary>
    private sealed class ReviewWriter(
        DataSyncApplyRunner runner,
        DataSyncApplySession s,
        DataSyncReviewEntry review,
        DataSyncPlan plan,
        ResolveResult resolved,
        IReadOnlyDictionary<string, DataSyncLocalKindState> local,
        DataSyncLinkDbModel? link,
        DataSyncApplyRecorder recorder)
    {
        private readonly DataSyncEntityWrites _writes = new(s, recorder);
        private readonly Dictionary<string, string> _created = new(StringComparer.Ordinal);
        private readonly HashSet<string> _changed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _orderedKinds = new(StringComparer.Ordinal);

        public async Task WriteAsync(CancellationToken ct)
        {
            var batches = DataSyncPlanner.BuildBatches(resolved, DataSyncKindIds.All);
            var check = await s.Identity.CheckApplyAsync(batches, ct);
            _changed.UnionWith(check.RefusedItemIds);
            var ops = check.Batches.SelectMany(b => b.Operations.Select(o => (b.Kind, Op: o))).ToList();
            var byItem = resolved.Items.ToDictionary(i => i.ItemId, StringComparer.Ordinal);
            var chunks = ops.Chunk(DataSyncMergeWriter.MaxEntitiesPerTransaction).ToList();
            for (var c = 0; c < chunks.Count; c++)
            {
                for (var i = 0; i < chunks[c].Length;)
                {
                    var kind = chunks[c][i].Kind;
                    var run = new List<ApplyOperation>();
                    while (i < chunks[c].Length && chunks[c][i].Kind == kind) run.Add(chunks[c][i++].Op);
                    ct.ThrowIfCancellationRequested();
                    var outcome = await s.Adapter(kind).ApplyAsync(new ApplyBatch(kind, run), ct);
                    _writes.Written(kind);
                    foreach (var (itemId, localKey) in outcome.CreatedLocalKeysByItemId) _created[itemId] = localKey;
                    _changed.UnionWith(outcome.ChangedDuringApplyItemIds);
                }

                foreach (var (_, op) in chunks[c]) await RecordAsync(byItem[op.ItemId], ct);
                if (c < chunks.Count - 1)
                {
                    await runner.CommitAsync(s, ct);
                    await s.BeginAsync(ct);
                }
            }

            var written = ops.Select(o => o.Op.ItemId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in resolved.Items.Where(i => !written.Contains(i.ItemId))) await RecordAsync(item, ct);
            await PlaceOrderAsync(ct);
        }

        private async Task RecordAsync(ResolvedItem item, CancellationToken ct)
        {
            var planItem = plan.Kinds.SelectMany(k => k.Items).Single(i => i.ItemId == item.ItemId);
            var incoming = Incoming(item);
            var record = incoming?.Record;
            var codec = s.Kinds.GetValueOrDefault(item.Kind)?.Codec;
            var outcome = _changed.Contains(item.ItemId) ? DataSyncItemOutcome.ChangedDuringApply : item.Outcome;
            string? localKey = item.TargetLocalKey;

            switch (outcome)
            {
                case DataSyncItemOutcome.Applied or DataSyncItemOutcome.NoChange when codec is not null && record is not null:
                    localKey = await RecordAppliedAsync(item, codec, incoming!, ct);
                    break;
                case DataSyncItemOutcome.SkippedByUser when record is not null:
                    // §8.3: a skipped entity is remembered, so it is not proposed again.
                    if (link is not null)
                    {
                        await s.Store.UpsertBasesAsync(link.Id, [new DataSyncBaseUpdate(item.Kind,
                            new SyncKey(record.Keys[0]), DataSyncBaseState.Excluded, DataSyncExclusionReason.Skipped,
                            record, null, null, true)], ct);
                    }

                    recorder.Skipped++;
                    break;
                case DataSyncItemOutcome.Held when record is not null:
                    await PendAsync(item.Kind, record, DataSyncPendingReason.Held, ct);
                    recorder.Held++;
                    break;
                case DataSyncItemOutcome.ChangedSinceReview or DataSyncItemOutcome.ChangedDuringApply when record is not null:
                    await PendAsync(item.Kind, record, DataSyncPendingReason.Retry, ct);
                    if (outcome == DataSyncItemOutcome.ChangedSinceReview) recorder.ChangedSinceReview++;
                    else recorder.ChangedDuringApply++;
                    break;
            }

            recorder.Item(item.ItemId, item.Kind, planItem.Incoming.Name, outcome,
                outcome is DataSyncItemOutcome.Applied or DataSyncItemOutcome.NoChange ? item.Action : DataSyncItemAction.None,
                localKey, planItem.Type);
        }

        /// <summary>A written item (§8.3 step 6): its revision, then its base with the merge's child map.</summary>
        private async Task<string?> RecordAppliedAsync(ResolvedItem item, IDataSyncKindCodec codec,
            DataSyncIncomingEntity incoming, CancellationToken ct)
        {
            var record = incoming.Record;
            var childrenLocal = codec.Descriptor.SupportsChildrenLocal && DataSyncRecordValidation.ChildrenLocalOf(record.Content);
            if (item.Operation is CreateEntityOperation create)
            {
                var localKey = _created[create.ItemId];
                var orderKey = codec.Descriptor.HasOrder ? record.OrderKey : null;
                var createDecision = new DataSyncRevisionDecision(item.Kind, create.Keys, null,
                    DataSyncRevisionKind.Create, record.Vv, null, false, false, orderKey, incoming.Unknown, childrenLocal,
                    record.EditedBy);
                var createdRow = await _writes.RecordCreateAsync(item.Kind, localKey,
                    create.Keys.All.Count == 0 ? EntityKeys.None : create.Keys, create.OriginNodeId, createDecision, record,
                    link?.Id, createdBySync: true, ct);
                recorder.Created++;
                recorder.ChangedDefinitions.Add((item.Kind, localKey));
                if (codec.Descriptor.HasOrder) _orderedKinds.Add(item.Kind);
                // A separate create under a fresh key is not the peer's entity: no base (§8.3 step 5, CreateSeparate).
                if (create.Keys.All.Count > 0) await AgreeAsync(item.Kind, createdRow.SyncKey, record, item.ChildMap, ct);
                return localKey;
            }

            if (item.TargetLocalKey is not { } target) return null;
            var entity = local.GetValueOrDefault(item.Kind)?.Entities.FirstOrDefault(e => e.LocalKey == target);
            if (entity is null) return target;

            var aliases = item.Operation switch
            {
                UpdateEntityOperation u => u.AliasKeysToAdd,
                BindOnlyOperation b => b.AliasKeysToAdd,
                _ => EntityKeys.None,
            };
            // Unchanged with R ≤ L: nothing; otherwise the merge's revision, compared with the peer's record (§8.3).
            var relation = entity.Vv.CompareTo(record.Vv);
            var contentWritten = item.Operation is UpdateEntityOperation;
            DataSyncRevisionDecision? decision = null;
            if (contentWritten || relation is not (DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates))
            {
                var orderKey = codec.Descriptor.HasOrder ? NoBaseOrderKey(entity, record) : null;
                var unknown = DataSyncUnknownMembers.Merge(null, entity.Unknown, incoming.Unknown, DataSyncMerge3Mode.NoBase)
                    .Merged;
                decision = new DataSyncRevisionDecision(item.Kind, entity.Keys, target,
                    DataSyncRevisionKind.MergedNoConflict, record.Vv, null, false, false, orderKey, unknown,
                    entity.ChildrenLocal, null);
                if (orderKey != entity.OrderKey) _orderedKinds.Add(item.Kind);
            }

            var row = await _writes.RecordLiveAsync(item.Kind, target, entity.Content, decision, record, null, aliases,
                link?.Id, ct);
            if (aliases.All.Count > 0)
            {
                recorder.PreImages.Add(new DataSyncEntityPreImage(item.Kind, row.Id, target, codec.NameOf(entity.Content),
                    DataSyncPreImageActions.Bound, link?.Id, (await s.KeysOfAsync(row, ct)).All.Select(k => k.Value).ToList(),
                    row.LocalHash, AliasesAdded: aliases.All.Select(k => k.Value).ToList()));
            }

            switch (item.Action)
            {
                case DataSyncItemAction.Updated:
                    recorder.Updated++;
                    break;
                case DataSyncItemAction.Linked or DataSyncItemAction.KeysRecorded:
                    recorder.Linked++;
                    break;
                default:
                    recorder.Unchanged++;
                    break;
            }

            await AgreeAsync(item.Kind, row.SyncKey, record, item.ChildMap, ct);
            return target;
        }

        /// <summary>§8.5.5 without a base: the side that has an order key gives it; both: the appearance winner.</summary>
        private static string? NoBaseOrderKey(DataSyncLocalEntityState entity, DataSyncWireRecord record)
        {
            if (entity.OrderKey is null) return record.OrderKey;
            if (record.OrderKey is null || entity.OrderKey == record.OrderKey) return entity.OrderKey;
            var local = entity.LastActor?.Value ?? "";
            var remote = record.EditedBy?.ActorId ?? "";
            return string.CompareOrdinal(remote, local) > 0 ? record.OrderKey : entity.OrderKey;
        }

        private async Task AgreeAsync(string kind, string primary, DataSyncWireRecord record,
            IReadOnlyDictionary<string, string>? childMap, CancellationToken ct)
        {
            if (link is null) return;
            await s.Store.UpsertBasesAsync(link.Id, [new DataSyncBaseUpdate(kind, new SyncKey(primary),
                DataSyncBaseState.Normal, null, record, childMap, null, true)], ct);
        }

        /// <summary>A record waiting on the base row of the entity it binds to by key, else under its own primary.</summary>
        private async Task PendAsync(string kind, DataSyncWireRecord record, DataSyncPendingReason reason,
            CancellationToken ct)
        {
            if (link is null) return;
            DataSyncEntityDbModel? owner = null;
            foreach (var key in record.Keys)
            {
                owner = await s.OwnerAsync(kind, key, ct);
                if (owner is not null) break;
            }

            var bound = owner is { DeletedAtUtc: null };
            var baseKey = bound ? owner!.SyncKey : record.Keys[0];
            var existing = await s.BaseRowAsync(link.Id, kind, baseKey, ct);
            var state = existing?.State ?? (bound ? DataSyncBaseState.Normal : DataSyncBaseState.Unbound);
            await s.Store.UpsertBasesAsync(link.Id, [new DataSyncBaseUpdate(kind, new SyncKey(baseKey),
                state == DataSyncBaseState.Excluded ? DataSyncBaseState.Unbound : state, null, null, null,
                DataSyncPendingRecords.Create(record, reason, bound ? owner!.Seq : 0, DataSyncMergeFlags.None), false)], ct);
        }

        private DataSyncIncomingEntity? Incoming(ResolvedItem item) =>
            DataSyncMergeItemIds.TryParse(item.ItemId, out var kind, out var key)
                ? review.Pull.Kinds.FirstOrDefault(k => k.Kind == kind)?.Entities
                    .FirstOrDefault(e => e.Record.Keys.Count > 0 && e.Record.Keys[0] == key.Value)
                : null;

        /// <summary>
        /// Shared order (§3.7) for the kinds this review created in or re-keyed: every live synced entity in
        /// <c>(orderKey, tieKey)</c> order fills the synced slots; the rest keep theirs.
        /// </summary>
        private async Task PlaceOrderAsync(CancellationToken ct)
        {
            foreach (var kind in _orderedKinds)
            {
                var adapter = s.Adapter(kind);
                var index = await s.Identity.GetKeyIndexAsync(kind, null, ct);
                var rows = (await s.Store.GetEntitiesAsync(kind, false, ct))
                    .Where(r => r.State == DataSyncEntitySyncState.Synced && !r.PublishHeld && !r.Unreadable &&
                                r.OrderKey is not null)
                    .ToList();
                var tie = index.Entities.Where(e => e.Live).ToDictionary(e => e.Id, e => DataSyncOrderPlanner.TieKeyOf(e.Keys));
                var entries = rows.Select(r => new DataSyncOrderEntry(r.LocalKey, r.OrderKey, tie[r.Id])).ToList();
                await adapter.ApplyOrderAsync(DataSyncOrderPlanner.Sort(entries).Select(e => e.LocalKey).ToList(), ct);
                _writes.Written(kind);
            }
        }
    }
}

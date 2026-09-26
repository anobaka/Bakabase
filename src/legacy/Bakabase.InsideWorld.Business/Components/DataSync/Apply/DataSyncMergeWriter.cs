using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// The writes of one merge (§8.10.2 apply half), in the session's open transaction: the identity pre-flight, then the
/// batches in chunks of at most <see cref="MaxEntitiesPerTransaction"/> entities (an entity is never split) — each
/// operation through its adapter, the entity re-read and recorded, its base written — with a commit and a new
/// <c>BEGIN IMMEDIATE</c> between chunks; then the revisions that wrote no content, holds, tombstones served again,
/// the remaining bases and the shared order. An operation whose content changed since the merge read it (or whose
/// key the pre-flight refused, or — in a chunk after a gap — whose decision rested on usage that changed since, see
/// <see cref="RecheckUsageAsync"/>) is <c>ChangedDuringApply</c>: its record becomes a <c>Retry</c> pending record
/// and nothing else of its entity is written (<see cref="DataSyncRecordApply.WithoutChangedDuringApply"/>).
/// </summary>
/// <remarks>
/// When chunks commit on their own, each chunk also writes the state-derived items (§9.1) of the entities it wrote —
/// a hold's <c>ChildDeletedInUse</c> — in its own transaction: they are drafted only when their state is made, and a
/// later pull meets that state already agreed (row K4) and drafts nothing, so a chunk that committed a hold without
/// its item, the apply stopping at a later gap, would withhold the child with nobody asked. The caller reconciles
/// the rest (<see cref="InboxLeft"/>).
/// </remarks>
internal sealed class DataSyncMergeWriter
{
    /// <summary>≈ 2 s per transaction at most (non-blocking note 6).</summary>
    public const int MaxEntitiesPerTransaction = 200;

    private readonly DataSyncApplySession _s;
    private readonly DataSyncEntityWrites _writes;
    private readonly DataSyncMergeInput _input;
    private readonly int? _historyLinkId;
    private readonly Func<CancellationToken, Task>? _commitChunk;
    private readonly Dictionary<string, string> _created = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Kind, SyncKey Key), long> _newSeqs = new();
    private readonly HashSet<DataSyncRevisionDecision> _revised = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DataSyncOverlayChange> _overlaid = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(string Kind, string Key)> _basesWritten = new();
    private readonly Dictionary<(string Kind, DataSyncVersionVector Vv), List<DataSyncWireRecord>> _recordsByVv = new();
    private readonly HashSet<(string Kind, string Key, DataSyncInboxItemType Type, string SubjectPath)> _itemsWritten = [];
    private readonly List<long> _itemsCreated = [];

    /// <param name="commitChunk">Commits and begins the next transaction between chunks; null writes everything in one.</param>
    public DataSyncMergeWriter(DataSyncApplySession s, DataSyncEntityWrites writes, DataSyncMergeInput input,
        DataSyncMergeResult result, int? historyLinkId, Func<CancellationToken, Task>? commitChunk)
    {
        _s = s;
        _writes = writes;
        _input = input;
        Result = result;
        _historyLinkId = historyLinkId;
        _commitChunk = commitChunk;
        foreach (var (kind, record) in (input.Incoming?.Kinds.SelectMany(k => k.Entities.Select(e => (k.Kind, e.Record))) ?? [])
                 .Concat(input.Bases.Values.Where(b => b.Pending is not null).Select(b => (b.Kind, b.Pending!.Record))))
        {
            if (!_recordsByVv.TryGetValue((kind, record.Vv), out var list)) _recordsByVv[(kind, record.Vv)] = list = [];
            list.Add(record);
        }
    }

    /// <summary>The merge result without what changed during the apply: what was actually written.</summary>
    public DataSyncMergeResult Result { get; private set; }

    /// <summary>Entities this merge created: item id → new local key.</summary>
    public IReadOnlyDictionary<string, string> Created => _created;

    /// <summary>Operations skipped as <c>ChangedDuringApply</c>.</summary>
    public IReadOnlyCollection<string> ChangedDuringApply => _changed;

    public int Applied { get; private set; }

    /// <summary>The ids of the items the chunks created in their own transactions (see the remarks).</summary>
    public IReadOnlyList<long> ItemsCreated => _itemsCreated;

    /// <summary>The drafts of <see cref="Result"/> the caller still reconciles: those no chunk wrote.</summary>
    public IReadOnlyList<DataSyncInboxDraft> InboxLeft =>
        Result.Inbox.Where(d => !_itemsWritten.Contains(SubjectOf(d))).ToList();

    private static (string, string, DataSyncInboxItemType, string) SubjectOf(DataSyncInboxDraft d) =>
        (d.Kind, d.Key.Value, d.Type, d.SubjectPath);

    public async Task WriteAsync(CancellationToken ct)
    {
        // The identity pre-flight (§5.3, v3.1 B4): refused items are ChangedDuringApply and write nothing.
        var check = await _s.Identity.CheckApplyAsync(Result.Batches, ct);
        if (check.RefusedItemIds.Count > 0) MarkChanged(check.RefusedItemIds);

        var ops = Result.Batches.SelectMany(b => b.Operations.Select(o => (b.Kind, Op: o))).ToList();
        var raw = await CaptureDeletesAsync(ops, ct);
        var chunks = ops.Chunk(MaxEntitiesPerTransaction).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            // Other writers had SQLite's lock in the gap before this chunk: the usage the merge decided on is read
            // again in this chunk's transaction.
            if (i > 0 && _commitChunk is not null) await RecheckUsageAsync(chunks[i], ct);
            await WriteChunkAsync(chunks[i], raw, ct);
            if (i < chunks.Count - 1 && _commitChunk is not null) await _commitChunk(ct);
        }

        await WriteRestAsync(ct);
    }

    private void MarkChanged(IEnumerable<string> itemIds)
    {
        var before = _changed.Count;
        _changed.UnionWith(itemIds);
        if (_changed.Count != before)
            Result = DataSyncRecordApply.WithoutChangedDuringApply(Result, _changed, _input.Bases);
    }

    /// <summary>
    /// The usage the merge read before the first chunk (§2.7 phase 1), read again for a chunk that follows a gap: an
    /// automatic deletion (§8.6) of an entity that has values now, and an update that removes a child (§8.5.4 step 3)
    /// that resources use now — or a child below it — are <c>ChangedDuringApply</c>. Their records become
    /// <c>Retry</c> pending records, merged again with the usage of that time, which asks instead.
    /// </summary>
    private async Task RecheckUsageAsync(IReadOnlyList<(string Kind, ApplyOperation Op)> chunk, CancellationToken ct)
    {
        var changed = new List<string>();
        foreach (var byKind in chunk.Where(o => !_changed.Contains(o.Op.ItemId))
                     .GroupBy(o => o.Kind, StringComparer.Ordinal))
        {
            var kind = byKind.Key;
            if (!_s.Kinds.TryGetValue(kind, out var adapter)) continue;
            var deletions = new List<DeleteEntityOperation>();
            var removals = new List<(UpdateEntityOperation Op, IReadOnlyCollection<string> ChildIds)>();
            var request = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (var (_, op) in byKind)
            {
                switch (op)
                {
                    case DeleteEntityOperation { RequireNoValues: true } delete:
                        deletions.Add(delete);
                        request.TryAdd(delete.LocalKey, []);
                        break;
                    case UpdateEntityOperation { RemovedChildIds.Count: > 0 } update:
                    {
                        var ids = WithDescendants(adapter.Codec, kind, update.LocalKey, update.RemovedChildIds);
                        removals.Add((update, ids));
                        request[update.LocalKey] = request.TryGetValue(update.LocalKey, out var asked)
                            ? asked.Union(ids, StringComparer.Ordinal).ToList()
                            : ids;
                        break;
                    }
                }
            }

            if (request.Count == 0) continue;
            var usage = await adapter.GetUsageAsync(request, ct);
            changed.AddRange(deletions.Where(d => usage.GetValueOrDefault(d.LocalKey)?.ValueCount > 0)
                .Select(d => d.ItemId));
            changed.AddRange(removals.Where(r => usage.GetValueOrDefault(r.Op.LocalKey) is { } entity &&
                                                 r.ChildIds.Any(id =>
                                                     entity.ResourceCountByChildId.GetValueOrDefault(id) > 0))
                .Select(r => r.Op.ItemId));
        }

        MarkChanged(changed);
    }

    /// <summary>
    /// Children and every child below them in the entity's content as the merge read it (a multilevel subtree's
    /// usage counts, §8.5.4 step 3).
    /// </summary>
    private IReadOnlyCollection<string> WithDescendants(IDataSyncKindCodec codec, string kind, string localKey,
        IReadOnlyList<string> roots)
    {
        var result = new HashSet<string>(roots, StringComparer.Ordinal);
        if (LocalOf(kind, localKey) is not { } local) return result;
        var children = codec.ChildrenOf(local.Content);
        bool grew;
        do
        {
            grew = false;
            foreach (var child in children)
            {
                if (child.ParentId is { } parent && result.Contains(parent) && result.Add(child.Id)) grew = true;
            }
        } while (grew);

        return result;
    }

    /// <summary>The whole raw rows of what the merge deletes, before anything is written (§8.11).</summary>
    private async Task<Dictionary<(string, string), JsonObject>> CaptureDeletesAsync(
        IReadOnlyList<(string Kind, ApplyOperation Op)> ops, CancellationToken ct)
    {
        var result = new Dictionary<(string, string), JsonObject>();
        foreach (var byKind in ops.Where(o => o.Op is DeleteEntityOperation).GroupBy(o => o.Kind))
        {
            var keys = byKind.Select(o => ((DeleteEntityOperation) o.Op).LocalKey).ToList();
            foreach (var (localKey, row) in await _s.Adapter(byKind.Key).CapturePreImageAsync(keys, ct))
                result[(byKind.Key, localKey)] = row;
        }

        return result;
    }

    private async Task WriteChunkAsync(IReadOnlyList<(string Kind, ApplyOperation Op)> chunk,
        IReadOnlyDictionary<(string, string), JsonObject> raw, CancellationToken ct)
    {
        // Through the adapters, one batch per run of one kind, in merge order.
        var live = chunk.Where(o => !_changed.Contains(o.Op.ItemId)).ToList();
        for (var i = 0; i < live.Count;)
        {
            var kind = live[i].Kind;
            var run = new List<ApplyOperation>();
            while (i < live.Count && live[i].Kind == kind) run.Add(live[i++].Op);
            ct.ThrowIfCancellationRequested();
            var outcome = await _s.Writer(kind).ApplyAsync(new ApplyBatch(kind, run), ct);
            _writes.Written(kind);
            foreach (var (itemId, localKey) in outcome.CreatedLocalKeysByItemId) _created[itemId] = localKey;
            MarkChanged(outcome.ChangedDuringApplyItemIds);
        }

        foreach (var (kind, op) in chunk)
        {
            if (_changed.Contains(op.ItemId))
            {
                _writes.Recorder.ChangedDuringApply++;
                _writes.Recorder.Item(op.ItemId, kind, NameOf(kind, op), DataSyncItemOutcome.ChangedDuringApply,
                    DataSyncItemAction.None, LocalKeyOf(op), TypeOf(op));
                continue;
            }

            await RecordAsync(kind, op, raw, ct);
        }

        // The chunk's bases: those of the entities it wrote.
        var keys = chunk.Where(o => !_changed.Contains(o.Op.ItemId))
            .Select(o => DataSyncMergeItemIds.TryParse(o.Op.ItemId, out var k, out var key) ? (k, key.Value) : default)
            .Where(k => k.Item1 is not null).ToHashSet();
        await WriteBasesAsync(u => keys.Contains((u.Kind, u.Key.Value)), ct);
        if (_commitChunk is not null) await WriteStateItemsAsync(keys, ct);
    }

    /// <summary>The state-derived items of the entities a chunk wrote, in the chunk's transaction (see the remarks).</summary>
    private async Task WriteStateItemsAsync(IReadOnlySet<(string Kind, string Key)> keys, CancellationToken ct)
    {
        var drafts = Result.Inbox.Where(d => d.Origin == DataSyncInboxItemOrigin.State &&
                                             keys.Contains((d.Kind, d.Key.Value)) &&
                                             !_itemsWritten.Contains(SubjectOf(d))).ToList();
        if (drafts.Count == 0) return;
        foreach (var draft in drafts) _itemsWritten.Add(SubjectOf(draft));
        var upsert = await _s.Store.UpsertItemsAsync(_input.Link.LinkId, _input.Link.PeerNodeId, drafts, _s.Now, ct);
        _itemsCreated.AddRange(upsert.CreatedIds);
    }

    private async Task RecordAsync(string kind, ApplyOperation op,
        IReadOnlyDictionary<(string, string), JsonObject> raw, CancellationToken ct)
    {
        var recorder = _writes.Recorder;
        switch (op)
        {
            case CreateEntityOperation create:
            {
                if (!_created.TryGetValue(create.ItemId, out var localKey))
                    throw new InvalidOperationException($"The adapter did not create {create.ItemId}.");
                var decision = Result.Revisions.FirstOrDefault(r =>
                    r.Kind == kind && r.LocalKey is null && r.Keys.Primary == create.Keys.Primary &&
                    r.Revision is DataSyncRevisionKind.Create or DataSyncRevisionKind.Revive) ??
                               throw new InvalidOperationException($"No revision for the create {create.ItemId}.");
                _revised.Add(decision);
                var row = await _writes.RecordCreateAsync(kind, localKey, create.Keys, create.OriginNodeId, decision,
                    RecordOf(kind, decision), _historyLinkId, createdBySync: true, ct);
                NoteSeq(row.Kind, row.SyncKey, row.Seq);
                recorder.Created++;
                recorder.ChangedDefinitions.Add((kind, localKey));
                recorder.Item(create.ItemId, kind, NameOf(kind, op), DataSyncItemOutcome.Applied, DataSyncItemAction.Created,
                    localKey, DataSyncPlanItemType.Create);
                break;
            }
            case UpdateEntityOperation update:
            {
                var row = await RecordLiveAsync(kind, update.LocalKey, update.AliasKeysToAdd, ct);
                recorder.Updated++;
                recorder.Item(update.ItemId, kind, NameOf(kind, op), DataSyncItemOutcome.Applied, DataSyncItemAction.Updated,
                    update.LocalKey, DataSyncPlanItemType.Update);
                NoteSeq(row.Kind, row.SyncKey, row.Seq);
                break;
            }
            case BindOnlyOperation bind:
            {
                var row = await RecordLiveAsync(kind, bind.LocalKey, bind.AliasKeysToAdd, ct);
                recorder.Linked++;
                recorder.Item(bind.ItemId, kind, NameOf(kind, op), DataSyncItemOutcome.Applied,
                    DataSyncItemAction.KeysRecorded, bind.LocalKey, DataSyncPlanItemType.Link);
                NoteSeq(row.Kind, row.SyncKey, row.Seq);
                break;
            }
            case DeleteEntityOperation delete:
            {
                var decision = Result.Revisions.FirstOrDefault(r => r.Kind == kind && r.LocalKey == delete.LocalKey) ??
                               throw new InvalidOperationException($"No revision for the deletion {delete.ItemId}.");
                _revised.Add(decision);
                var before = LocalOf(kind, delete.LocalKey) ??
                             throw new InvalidOperationException($"{kind}/{delete.LocalKey} was not in the merge's input.");
                var row = await _writes.RecordDeleteAsync(kind, delete.LocalKey, before.Content,
                    raw.GetValueOrDefault((kind, delete.LocalKey)), decision, RecordOf(kind, decision), _historyLinkId, ct);
                NoteSeq(row.Kind, row.SyncKey, row.Seq);
                recorder.Deleted++;
                recorder.Item(delete.ItemId, kind, NameOf(kind, op), DataSyncItemOutcome.Applied, DataSyncItemAction.Deleted,
                    delete.LocalKey, DataSyncPlanItemType.Update);
                break;
            }
            default:
                throw new InvalidOperationException($"A merge does not propose {op.GetType().Name}.");
        }

        Applied++;
    }

    /// <summary>A live entity the merge wrote (content, keys, or both) with its revision and hold changes.</summary>
    private async Task<Bakabase.Modules.DataSync.Models.Db.DataSyncEntityDbModel> RecordLiveAsync(string kind,
        string localKey, EntityKeys aliases, CancellationToken ct)
    {
        var decision = Result.Revisions.FirstOrDefault(r => r.Kind == kind && r.LocalKey == localKey);
        if (decision is not null) _revised.Add(decision);
        var overlay = Result.OverlayChanges.FirstOrDefault(o => o.Kind == kind && o.LocalKey == localKey);
        if (overlay is not null) _overlaid.Add(overlay);
        var before = LocalOf(kind, localKey) ??
                     throw new InvalidOperationException($"{kind}/{localKey} was not in the merge's input.");
        return await _writes.RecordLiveAsync(kind, localKey, before.Content, decision,
            decision is null ? null : RecordOf(kind, decision), overlay, aliases, _historyLinkId, ct);
    }

    private async Task WriteRestAsync(CancellationToken ct)
    {
        // Revisions that wrote no content: a new order key or childrenLocal, a hold, a collision's own counter, a
        // tombstone taking the peer's deletion history (row T1).
        foreach (var decision in Result.Revisions.Where(r => !_revised.Contains(r)).ToList())
        {
            _revised.Add(decision);
            if (decision.LocalKey is { } localKey)
            {
                var overlay = Result.OverlayChanges.FirstOrDefault(o => o.Kind == decision.Kind && o.LocalKey == localKey);
                if (overlay is not null) _overlaid.Add(overlay);
                var before = LocalOf(decision.Kind, localKey);
                if (before is null) continue;
                var row = await _writes.RecordLiveAsync(decision.Kind, localKey, before.Content, decision,
                    RecordOf(decision.Kind, decision), overlay, EntityKeys.None, _historyLinkId, ct);
                NoteSeq(row.Kind, row.SyncKey, row.Seq);
                if (row.OrderKey != before.OrderKey) _writes.Recorder.Reordered++;
            }
            else if (decision.Revision == DataSyncRevisionKind.AcceptRemoteDelete && decision.Keys.Primary is { } key)
            {
                var record = RecordOf(decision.Kind, decision);
                var row = await _writes.RecordTombstoneRevisionAsync(decision.Kind, key, decision.Revision,
                    decision.RemoteVv, record?.EditedBy, ct);
                if (row is not null) NoteSeq(row.Kind, row.SyncKey, row.Seq);
            }
        }

        // Holds without a revision of their own.
        foreach (var overlay in Result.OverlayChanges.Where(o => !_overlaid.Contains(o)).ToList())
        {
            var before = LocalOf(overlay.Kind, overlay.LocalKey);
            if (before is null) continue;
            var row = await _writes.RecordLiveAsync(overlay.Kind, overlay.LocalKey, before.Content, null, null, overlay,
                EntityKeys.None, _historyLinkId, ct);
            NoteSeq(row.Kind, row.SyncKey, row.Seq);
        }

        // Row T2: tombstones served again so the peer receives this device's deletion (§4.6).
        foreach (var (kind, key) in Result.TombstonesToServe ?? [])
        {
            var row = await _s.OwnerAsync(kind, key.Value, ct);
            if (row is not { DeletedAtUtc: not null }) continue;
            row.TombstoneServed = true;
            row.Seq = await _s.Store.NextSeqAsync(ct);
            row.UpdatedAtUtc = _s.Now;
            NoteSeq(row.Kind, row.SyncKey, row.Seq);
        }

        await WriteBasesAsync(_ => true, ct);

        // Shared order (§3.7), after every batch; an entity whose create did not happen keeps no slot.
        foreach (var assignment in Result.Order)
        {
            if (!_s.Kinds.TryGetValue(assignment.Kind, out var adapter) || !adapter.Codec.Descriptor.HasOrder) continue;
            var keys = DataSyncRecordApply.ResolveOrder(assignment, _created);
            await _s.Writer(assignment.Kind).ApplyOrderAsync(keys, ct);
            _writes.Written(assignment.Kind);
        }
    }

    private async Task WriteBasesAsync(Func<DataSyncBaseUpdate, bool> which, CancellationToken ct)
    {
        await _s.Store.FlushAsync(ct);
        var updates = Result.BaseUpdates.Where(u => !_basesWritten.Contains((u.Kind, u.Key.Value)) && which(u)).ToList();
        if (updates.Count == 0) return;
        foreach (var u in updates) _basesWritten.Add((u.Kind, u.Key.Value));
        await _s.Store.UpsertBasesAsync(_input.Link.LinkId, DataSyncPendingRecords.WithEvaluatedSeqs(updates, _newSeqs), ct);
    }

    /// <summary>
    /// The Seq an entity got in this apply, by its base key (its primary): pending records the merge wrote on its row
    /// are not re-merged for that revision alone (§8.4 condition 2, <see cref="DataSyncPendingRecords.WithEvaluatedSeqs"/>).
    /// </summary>
    private void NoteSeq(string kind, string primary, long seq) => _newSeqs[(kind, new SyncKey(primary))] = seq;

    private DataSyncLocalEntityState? LocalOf(string kind, string localKey) =>
        _input.Local.TryGetValue(kind, out var state) ? state.Entities.FirstOrDefault(e => e.LocalKey == localKey) : null;

    /// <summary>The peer record a revision took (by its vector and one of its keys).</summary>
    private DataSyncWireRecord? RecordOf(string kind, DataSyncRevisionDecision decision)
    {
        if (decision.RemoteVv is not { } vv || !_recordsByVv.TryGetValue((kind, vv), out var candidates)) return null;
        var local = decision.LocalKey is { } lk ? LocalOf(kind, lk) : null;
        return candidates.LastOrDefault(r => r.Keys.Any(k => decision.Keys.Contains(new SyncKey(k)) ||
                                                             (local?.Keys.Contains(new SyncKey(k)) ?? false))) ??
               candidates[^1];
    }

    private string NameOf(string kind, ApplyOperation op)
    {
        var codec = _s.Kinds.GetValueOrDefault(kind)?.Codec;
        return op switch
        {
            CreateEntityOperation c when codec is not null => codec.NameOf(codec.ReadLocal(c.Content)),
            UpdateEntityOperation u when codec is not null => codec.NameOf(codec.ReadLocal(u.MergedContent)),
            _ when LocalKeyOf(op) is { } localKey && LocalOf(kind, localKey) is { } l && codec is not null =>
                codec.NameOf(l.Content),
            _ => op.ItemId,
        };
    }

    private static string? LocalKeyOf(ApplyOperation op) => op switch
    {
        UpdateEntityOperation u => u.LocalKey,
        BindOnlyOperation b => b.LocalKey,
        DeleteEntityOperation d => d.LocalKey,
        ChangeSubtypeOperation c => c.LocalKey,
        _ => null,
    };

    private static DataSyncPlanItemType TypeOf(ApplyOperation op) => op switch
    {
        CreateEntityOperation => DataSyncPlanItemType.Create,
        BindOnlyOperation => DataSyncPlanItemType.Link,
        _ => DataSyncPlanItemType.Update,
    };
}

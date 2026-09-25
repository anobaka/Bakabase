using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Ordering;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

internal enum SimPullOutcome { Applied, Skipped, Paused, Unreachable, Waiting }

// One cycle for one link (§8.10.2): the fetch half (head, breakers B1/B1b, evidence, manifest and pages) and the
// apply half (Refresh, merge, anomaly rollback, pre-flight, writes, RecordApply, bases, order, inbox, cursor), plus
// the actor guard (§5.6) the runner calls around it.
internal sealed partial class SimNode
{
    /// <summary>Pulls <paramref name="peer"/> through this node's link to it.</summary>
    public SimPullOutcome Pull(SimNode peer) => Pull(LinkTo(peer));

    public SimPullOutcome Pull(SimLink link)
    {
        if (link.Stopped) return SimPullOutcome.Skipped;
        if (link.Paused is not null) return SimPullOutcome.Paused;
        var peer = link.Peer;
        if (!_world.Reachable(this, peer)) return SimPullOutcome.Unreachable;

        // 1. head
        var answer = peer.Head(this, link);
        var head = answer.Head;
        _headsAnswered.Add(link.Id);
        UpdateVerified();
        link.PeerAttention = head.Attention;
        link.PeerActorId = head.ActorId;
        if (link.PeerEpoch is { } epoch && epoch != head.LibraryEpoch)
        {
            Pause(link, DataSyncPauseReason.PeerReset, "epoch");
            return SimPullOutcome.Paused;
        }

        if (DataSyncBreakers.PeerRestored(link.Cursors, head.Kinds, SimKinds.Ids) is { } restored)
        {
            Pause(link, restored.Reason, restored.Detail);
            return SimPullOutcome.Paused;
        }

        if (DataSyncAnomalies.IsNewEvidence(head.SeenCounter, ActorCounter))
        {
            _world.Log($"{Name}: {peer.Name} has seen counter {head.SeenCounter} of {Db.Local.ActorId} (issued {ActorCounter})");
            ReportEvidence(new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Peer, peer.NodeId), Db.Local.ActorId,
                head.SeenCounter);
            if (link.Paused is not null) return SimPullOutcome.Paused;
        }

        if (answer.RestorePending || answer.Busy || !Verified) return SimPullOutcome.Waiting;

        // The apply half's actor check (§5.6), before any transaction.
        CheckActor();
        if (link.Paused is not null) return SimPullOutcome.Paused;

        // 2–3. manifest and pages
        var full = Now - link.LastFullReconciliation >= FullReconciliationInterval;
        var pull = peer.Snapshot(this, link, full);
        if (pull is null) return SimPullOutcome.Waiting;
        link.PeerEpoch ??= head.LibraryEpoch;
        LastPull = pull;
        return Apply(link, pull, null, skipRefresh: false, retried: false);
    }

    /// <summary>The staged pull the last <see cref="Pull(SimLink)"/> fetched.</summary>
    public DataSyncStagedPull? LastPull { get; private set; }

    /// <summary>Delivers a staged pull again (invariant I3: the second delivery changes nothing).</summary>
    public SimPullOutcome Redeliver(SimLink link, DataSyncStagedPull pull) =>
        link.Paused is not null || link.Stopped ? SimPullOutcome.Paused : Apply(link, pull, null, skipRefresh: false, retried: false);

    /// <summary>Re-merges pending records of one link without a pull (a resolution's re-merge, Apply all).</summary>
    public SimPullOutcome Remerge(SimLink link, IEnumerable<(string Kind, SyncKey Key)> keys, bool skipRefresh = false) =>
        Apply(link, null, keys.ToList(), skipRefresh, retried: false);

    public SimPullOutcome Remerge(SimLink link, IEnumerable<SyncKey> keys) => Remerge(link, keys.Select(k => (Kind, k)));

    // ---- the apply half ----------------------------------------------------------------------------

    private SimPullOutcome Apply(SimLink link, DataSyncStagedPull? pull, IReadOnlyList<(string Kind, SyncKey Key)>? pending,
        bool skipRefresh, bool retried)
    {
        if (!Verified) return SimPullOutcome.Waiting;
        var rollback = Db.Clone();
        if (!skipRefresh) Refresh();

        var fullKinds = pull?.Kinds.Where(k => k.FullReconciliation).Select(k => k.Kind).ToHashSet(StringComparer.Ordinal) ??
                        new HashSet<string>(StringComparer.Ordinal);
        pending ??= link.Bases
            .Where(b => b.Value.Pending is { } p && (link.RemergeAllPending ||
                                                     DataSyncPendingRecords.ShouldRemerge(p, SeqOf(b.Key.Kind, b.Key.Key),
                                                         fullKinds.Contains(b.Key.Kind), link.OnceFlags, false)))
            .Select(b => b.Key).OrderBy(k => k.Kind, StringComparer.Ordinal).ThenBy(k => k.Key.Value, StringComparer.Ordinal)
            .ToList();

        var bases = new Dictionary<(string Kind, SyncKey Key), DataSyncPeerBase>(link.Bases);
        var result = RunMerge(link, pull, pending, bases);
        LastResult = result;
        if (result.Anomaly is { } anomaly)
        {
            // The whole apply transaction rolls back, Refresh included (§5.6, engineering B4e).
            Db = rollback;
            link = Links[link.Peer.NodeId];
            _world.Count("anomaly:" + anomaly.Code + (anomaly.ActorId == Db.Local.ActorId ? "" : ":retired"));
            _world.Log($"{Name} ← {link.Peer.Name}: anomaly {anomaly.Code} actor {anomaly.ActorId}:{anomaly.SeenCounter}");
            if (anomaly.Code == DataSyncAnomalies.DuplicateActor)
            {
                Pause(link, result.Pause ?? DataSyncPauseReason.PeerIdentityDuplicated, result.PauseDetail);
                return SimPullOutcome.Paused;
            }

            var retired = anomaly.ActorId != Db.Local.ActorId;
            ReportEvidence(new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Peer, link.Peer.NodeId, retired),
                anomaly.ActorId, anomaly.SeenCounter);
            if (link.Paused is not null) return SimPullOutcome.Paused;
            return retried ? SimPullOutcome.Waiting : Apply(link, pull, null, skipRefresh, true);
        }

        if (result.Pause is { } pause)
        {
            Pause(link, pause, result.PauseDetail);
            return SimPullOutcome.Paused;
        }

        var ops = result.Batches.SelectMany(b => b.Operations).ToList();
        if (ops.Count > 0 || result.Revisions.Count > 0 || result.Inbox.Count > 0)
        {
            _world.Log($"{Name} ← {link.Peer.Name}{(pull is null ? " (re-merge)" : "")}: " +
                       $"{string.Join(",", ops.Select(o => o.GetType().Name.Replace("Operation", "") + ":" + o.ItemId[^6..]))} " +
                       $"rev [{string.Join(",", result.Revisions.Select(r => r.Revision + ":" + r.Keys.Primary?.Value[..6]))}] " +
                       $"items [{string.Join(",", result.Inbox.Select(d => d.Type + ":" + d.Key.Value[..6] + ":" + d.SubjectPath))}]");
        }

        BeforeApply?.Invoke(this, result);
        ApplyResult(link, pull, bases, pending, result);
        return SimPullOutcome.Applied;
    }

    /// <summary>Both phases of the merger over this node's state (§2.7): the usage it asks for is read in between.</summary>
    private DataSyncMergeResult RunMerge(SimLink link, DataSyncStagedPull? pull, IReadOnlyList<(string Kind, SyncKey Key)> pending,
        IReadOnlyDictionary<(string Kind, SyncKey Key), DataSyncPeerBase> bases)
    {
        var effective = link.OthersWinNext ? DataSyncLinkMode.Follow : EffectiveMode(link);
        var own = new Dictionary<string, long>(Db.Local.RetiredActors, StringComparer.Ordinal) { [Db.Local.ActorId] = ActorCounter };
        var context = new DataSyncLinkContext(link.Id, link.Peer.NodeId, link.Peer.Name, link.Mode, effective, SimKinds.Ids,
            SimKinds.Ids.Where(k => !link.CompletedKinds.Contains(k)).ToList(), Headless, Actor, own, link.PeerActorId,
            SimKinds.All.ToDictionary(k => k.Kind, k => k.Codec.ComparisonFormVersion, StringComparer.Ordinal), link.OnceFlags);
        var open = Items.Where(i => i.Open && i.LinkId == link.Id && i.Origin == DataSyncInboxItemOrigin.Merger)
            .Select(ToOpen).ToList();
        var input = new DataSyncMergeInput(context, pull, LocalStates(), bases, pending, SimKinds.Codecs,
            new Dictionary<(string, string), IReadOnlyDictionary<string, int>>(), new Dictionary<(string, string), int>(),
            open, DataSyncAutoApplyPolicy.Default, Limits);

        var usage = new Dictionary<(string, string), IReadOnlyDictionary<string, int>>();
        var values = new Dictionary<(string, string), int>();
        foreach (var query in DataSyncMerger.CollectUsageQueries(input))
        {
            var known = Db.Usage.GetValueOrDefault((query.Kind, query.LocalKey)) ?? new Dictionary<string, int>();
            usage[(query.Kind, query.LocalKey)] = query.ChildIds.ToDictionary(id => id, id => known.GetValueOrDefault(id));
            if (query.NeedValueCount) values[(query.Kind, query.LocalKey)] = Db.Values.GetValueOrDefault((query.Kind, query.LocalKey));
        }

        return DataSyncMerger.Merge(input with { ChildUsage = usage, ValueCounts = values });
    }

    /// <summary>
    /// §9.2 step 2 for a merger-derived item: the merger runs once more over the item's pending record, and the
    /// item stands only when it is produced again with the same token.
    /// </summary>
    private bool Rederives(SimItem item, string? target = null)
    {
        if (LinkById(item.LinkId) is not { } link) return false;
        var pending = PendingKeysOf(item, link);
        if (pending.Count == 0) return false;
        var result = RunMerge(link, null, pending, new Dictionary<(string, SyncKey), DataSyncPeerBase>(link.Bases));
        if (result.Anomaly is not null || result.Pause is not null) return false;
        var draft = result.Inbox.FirstOrDefault(d =>
            d.Type == item.Type && d.Kind == item.Kind && d.Key == item.Key && d.SubjectPath == item.Subject && d.Token == item.Token);
        // A target must be among the candidates (or records) the merger derives now, not the ones the card showed.
        return draft is not null &&
               (target is null || (draft.Payload.Candidates?.Any(c => c.Updatable && c.LocalKey == target) ?? false) ||
                (draft.Payload.Records?.Any(r => r.PrimaryKey == target) ?? false));
    }

    /// <summary>
    /// The base rows whose pending records an item was derived from: its own (by its key, or its entity's primary),
    /// and for row M every record binding to the entity, each waiting under its own primary.
    /// </summary>
    private List<(string Kind, SyncKey Key)> PendingKeysOf(SimItem item, SimLink link)
    {
        var baseKey = link.Bases.GetValueOrDefault((item.Kind, item.Key))?.Pending is not null
            ? item.Key
            : Rows.FirstOrDefault(r => r.Kind == item.Kind && r.Keys.Contains(item.Key))?.Primary;
        if (baseKey is not { } key || link.Bases.GetValueOrDefault((item.Kind, key))?.Pending is null) return [];
        return item.Payload.Records is { Count: > 0 } records
            ? link.Bases.Where(b => b.Key.Kind == item.Kind && b.Value.Pending is { } p &&
                                    records.Any(r => r.PrimaryKey == p.Record.Keys[0])).Select(b => b.Key).ToList()
            : [(item.Kind, key)];
    }

    private IReadOnlyDictionary<string, DataSyncLocalKindState> LocalStates() =>
        SimKinds.All.ToDictionary(k => k.Kind, k => new DataSyncLocalKindState(k.Kind,
            Live(k.Kind).Where(r => r.HasSideRow).Select(r => new DataSyncLocalEntityState(r.LocalKey, new EntityKeys(r.Keys),
                r.Content!, r.LocalHash, r.SharedHash ?? "", r.Vv,
                r.LastEditor is { } e ? new DataSyncActorId(e.ActorId) : null, r.LastEditor, r.OrderKey, r.State, r.Overlay,
                false, r.CreatedBySync, r.PublishHeld, r.Unknown, Db.Values.GetValueOrDefault((r.Kind, r.LocalKey)), r.Seq)).ToList(),
            Rows.Where(r => r.Kind == k.Kind && r.Deleted).Select(r => new DataSyncTombstoneState(new EntityKeys(r.Keys), r.Vv,
                r.LastEditor, r.StateAtDeletion, r.TombstoneKind, r.Served, r.Seq)).ToList()), StringComparer.Ordinal);

    /// <summary>
    /// The writes of one merge (§8.10.2 apply half): the identity pre-flight and hash checks, then content, overlays,
    /// revisions (through <see cref="DataSyncRecordApply"/>), retirements, bases, served tombstones, order, the inbox
    /// and the cursor. Checks invariant I4 on every child and entity it removes.
    /// </summary>
    private void ApplyResult(SimLink link, DataSyncStagedPull? pull,
        IReadOnlyDictionary<(string Kind, SyncKey Key), DataSyncPeerBase> bases, IReadOnlyList<(string Kind, SyncKey Key)> pending,
        DataSyncMergeResult result)
    {
        var records = (pull?.Kinds.SelectMany(k => k.Entities.Select(e => (Kind: k.Kind, Record: e.Record))) ?? [])
            .Concat(pending.Select(p => (p.Kind, Record: bases.GetValueOrDefault(p)?.Pending?.Record))
                .Where(p => p.Record is not null).Select(p => (Kind: p.Kind, Record: p.Record!)))
            .ToList();

        var changed = PreFlight(result);
        if (changed.Count > 0)
        {
            _world.Count("changedDuringApply");
            _world.Log($"{Name} ← {link.Peer.Name}: changed during apply {string.Join(",", changed)}");
            result = DataSyncRecordApply.WithoutChangedDuringApply(result, changed, bases);
        }

        var before = new Dictionary<SimRow, object?>(ReferenceEqualityComparer.Instance);
        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        var retires = new List<(SimRow Live, SimRow Tombstone)>();
        var seqBefore = Rows.ToDictionary(r => r, r => r.Seq, (IEqualityComparer<SimRow>)ReferenceEqualityComparer.Instance);
        var history = new SimHistoryEntry { Id = ++Db.NextHistoryId, Kind = DataSyncHistoryKind.AutoSync, At = Now };

        foreach (var batch in result.Batches)
        {
            var kind = SimKinds.Of(batch.Kind);
            foreach (var op in batch.Operations)
            {
                switch (op)
                {
                    case CreateEntityOperation create:
                    {
                        var revived = TombstoneOwner(batch.Kind, create.Keys.All[0]);
                        var row = revived ?? new SimRow { Kind = batch.Kind, LocalKey = "" };
                        row.LocalKey = NewLocalKey();
                        foreach (var key in create.Keys.All.Where(k => !row.Keys.Contains(k)))
                        {
                            if (TombstoneOwner(batch.Kind, key) is { } other && other != row) retires.Add((row, other));
                            else row.Keys.Add(key);
                        }

                        row.Content = kind.Store(kind.Codec.ReadLocal(create.Content), null);
                        row.Origin = create.OriginNodeId;
                        row.Deleted = false;
                        row.DeletedAt = null;
                        row.State = DataSyncEntitySyncState.Synced;
                        row.Overlay = DataSyncOverlay.None;
                        before[row] = null;
                        created[create.ItemId] = row.LocalKey;
                        if (revived is null) Rows.Add(row);
                        if (kind.HasOrder) OrderOf(batch.Kind).Add(row.LocalKey);
                        break;
                    }
                    case UpdateEntityOperation update:
                    {
                        var target = LiveRow(batch.Kind, update.LocalKey)!;
                        before[target] = target.Content;
                        var was = kind.Codec.ChildrenOf(target.Content!).Select(c => c.Id).ToList();
                        target.Content = kind.Store(kind.Codec.ReadLocal(update.MergedContent), target.Content);
                        var now = kind.Codec.ChildrenOf(target.Content).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
                        var usage = Db.Usage.GetValueOrDefault((batch.Kind, target.LocalKey));
                        foreach (var gone in was.Where(id => !now.Contains(id)))
                        {
                            if (!update.RemovedChildIds.Contains(gone))
                                Violations.Add($"I4: {Name} dropped child {gone} of {target.Name} outside RemovedChildIds");
                            else if (usage?.GetValueOrDefault(gone) > 0)
                                Violations.Add($"I4: {Name} removed child {gone} of {target.Name} used by {usage[gone]} resources");
                        }

                        AddAliases(target, update.AliasKeysToAdd, retires);
                        break;
                    }
                    case BindOnlyOperation bind:
                    {
                        var target = LiveRow(batch.Kind, bind.LocalKey)!;
                        AddAliases(target, bind.AliasKeysToAdd, retires);
                        target.Seq = NextSeq();
                        break;
                    }
                    case DeleteEntityOperation delete:
                    {
                        var target = LiveRow(batch.Kind, delete.LocalKey)!;
                        before[target] = target.Content;
                        if (!target.CreatedBySync || Db.Values.GetValueOrDefault((batch.Kind, target.LocalKey)) > 0)
                            Violations.Add($"I4: {Name} deleted {target.Name} by itself (createdBySync {target.CreatedBySync})");
                        break;
                    }
                }
            }
        }

        foreach (var overlay in result.OverlayChanges)
        {
            if (overlay.Hold.Count > 0) _world.Count("hold");
            if (overlay.Release.Count > 0) _world.Count("release");
            var row = LiveRow(overlay.Kind, overlay.LocalKey)!;
            row.Overlay = row.Overlay with
            {
                HeldChildren = row.Overlay.HeldChildren.Where(h => !overlay.Release.Contains(h)).Concat(overlay.Hold).ToList(),
            };
        }

        foreach (var decision in result.Revisions)
        {
            var kind = SimKinds.Of(decision.Kind);
            // Row T1's revision names a tombstone (no local key): it takes the peer's deletion history.
            var ofTombstone = decision.LocalKey is null && decision.Revision == DataSyncRevisionKind.AcceptRemoteDelete;
            var row = decision.LocalKey is { } localKey
                ? LiveRow(decision.Kind, localKey)!
                : Rows.First(r => r.Kind == decision.Kind && r.Deleted == ofTombstone && decision.Keys.All.Any(r.Keys.Contains));
            var record = records.LastOrDefault(r => r.Kind == decision.Kind && r.Record.Vv == decision.RemoteVv &&
                                                    r.Record.Keys.Any(k => decision.Keys.Contains(new SyncKey(k)) || row.Keys.Contains(new SyncKey(k))))
                .Record;
            var remoteShared = record is null || record.Deleted ? null : DataSyncPublication.SharedHashOfRecord(kind.Codec, record, Limits);
            var isCreate = decision.Revision is DataSyncRevisionKind.Create or DataSyncRevisionKind.Revive;
            var deletion = decision.Revision == DataSyncRevisionKind.AcceptRemoteDelete;
            if (decision.Revision == DataSyncRevisionKind.Revive && decision.TombstoneVv is { } tombstone &&
                DataSyncVersionVector.Max(tombstone, row.Vv) != tombstone)
                Violations.Add($"I7: {Name} revived {row.Name} with a tombstone vector below its own");
            _world.Count("revision:" + decision.Revision);
            var applied = DataSyncRecordApply.Revise(kind.Codec, decision, isCreate ? DataSyncVersionVector.Empty : row.Vv,
                isCreate ? null : row.SharedHash, deletion ? null : row.Content, row.Overlay, remoteShared, record?.EditedBy,
                Editor, Actor, NextCounter);
            if (decision.Revision == DataSyncRevisionKind.Revive && decision.TombstoneVv is { } t &&
                applied.Vv.CompareTo(t) is not (DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates))
                Violations.Add($"I7: {Name} revived {row.Name} below its tombstone");
            row.Vv = applied.Vv;
            row.LastEditor = applied.LastEditor;
            if (ofTombstone)
            {
                row.Seq = NextSeq();
                continue;
            }

            row.LocalHash = applied.LocalHash ?? row.LocalHash;
            row.SharedHash = applied.SharedHash ?? row.SharedHash;
            row.OrderKey = decision.OrderKey;
            row.Unknown = decision.Unknown;
            if (isCreate) row.CreatedBySync = true;
            var was = before.GetValueOrDefault(row);
            if (deletion)
            {
                history.Changes.Add(new SimHistoryChange(row.Kind, row.Primary, "deleted", was, null, link.Id, []));
                Tombstone(row, DataSyncTombstoneKind.Deleted);
                continue;
            }

            row.Seq = NextSeq();
            if (was is not null && row.Content is not null)
            {
                row.LastApply = (Now, DataSyncEntityChangeList.Between(kind.Codec, was, row.Content));
                row.PreApplyContent = was;
                history.Changes.Add(new SimHistoryChange(row.Kind, row.Primary, "updated", was, row.Content, link.Id, []));
            }
            else if (isCreate)
            {
                history.Changes.Add(new SimHistoryChange(row.Kind, row.Primary, "created", null, row.Content, link.Id, []));
            }
        }

        foreach (var (live, tombstone) in retires) Retire(live, tombstone);

        // Serving a tombstone again is a Seq bump of this apply like any other: pending records the merge evaluated
        // against that row take its new Seq with the rest, or the next pull re-merges them for nothing.
        foreach (var (kind, key) in result.TombstonesToServe ?? [])
        {
            var row = TombstoneOwner(kind, key)!;
            row.Served = true;
            row.Seq = NextSeq();
            _world.Count("tombstoneServedAgain");
        }

        var newSeqs = new Dictionary<(string Kind, SyncKey Key), long>();
        foreach (var row in Rows.Where(r => r.HasSideRow && (!seqBefore.TryGetValue(r, out var s) || s != r.Seq)))
            newSeqs[(row.Kind, row.Primary)] = row.Seq;
        foreach (var update in DataSyncPendingRecords.WithEvaluatedSeqs(result.BaseUpdates, newSeqs)) UpsertBase(link, update);

        foreach (var assignment in result.Order)
        {
            var order = OrderOf(assignment.Kind);
            var placed = DataSyncOrderPlanner.Place(order, DataSyncRecordApply.ResolveOrder(assignment, created));
            order.Clear();
            order.AddRange(placed);
        }

        Notes.AddRange(result.Notes);
        foreach (var note in result.Notes) _world.Count("note:" + note.Code);
        var reconciliation = DataSyncInboxRules.Reconcile(
            Items.Where(i => i.Open && i.LinkId == link.Id).Select(ToOpen).ToList(), result.Inbox, result.Evaluated,
            result.ClosureHints);
        var inserted = new List<long>();
        foreach (var upsert in reconciliation.Upserts)
        {
            var (item, isNew) = UpsertItem(link.Id, upsert.Draft, upsert.ExistingId);
            if (isNew) inserted.Add(item.Id);
        }

        foreach (var close in reconciliation.Closes) Close(Items.Single(i => i.Id == close.ItemId), close.Closure, close.By);
        CloseStaleStateItems();
        CloseDominated();

        foreach (var (kind, cursor) in result.CursorAdvance) link.Cursors[kind] = cursor;
        if (pull is not null)
        {
            foreach (var kind in pull.Kinds) link.CompletedKinds.Add(kind.Kind);
            if (pull.Kinds.All(k => k.FullReconciliation)) link.LastFullReconciliation = Now;
            link.LastSuccess = Now;
        }

        link.OnceFlags = DataSyncMergeFlags.None;
        link.RemergeAllPending = false;
        link.OthersWinNext = false;
        if (history.Changes.Count > 0) Db.History.Add(history);

        var source = "DataSync:" + link.Peer.NodeId;
        if (inserted.Count > 0) Notify(source, "newItems", inserted);
        if (result.Notes.Any(n => n.Code == DataSyncMergeNoteCodes.FollowOverride) &&
            !Notifications.Any(n => n.Source == source && n.Case == "followOverride" && Now - n.At < TimeSpan.FromDays(1)))
            Notify(source, "followOverride", []);
        WriteWatermark();
    }

    /// <summary>
    /// The adapter's hash check and the identity pre-flight (v3.1 §5.3), in batch order against the current rows
    /// and the effects of the earlier operations: an operation whose content changed since the merge read it, or
    /// that would give a key live elsewhere to another entity, is <c>ChangedDuringApply</c>.
    /// </summary>
    private List<string> PreFlight(DataSyncMergeResult result)
    {
        var changed = new List<string>();
        var claimed = new Dictionary<(string, SyncKey), string>();   // keys given to a row by an earlier operation
        foreach (var batch in result.Batches)
        {
            var codec = SimKinds.Of(batch.Kind).Codec;
            string? OwnerOf(SyncKey key) =>
                claimed.TryGetValue((batch.Kind, key), out var owner) ? owner : LiveOwner(batch.Kind, key)?.LocalKey;

            foreach (var op in batch.Operations)
            {
                switch (op)
                {
                    case CreateEntityOperation create:
                        if (create.Keys.All.Any(k => OwnerOf(k) is not null))
                        {
                            changed.Add(op.ItemId);
                            continue;
                        }

                        foreach (var key in create.Keys.All) claimed[(batch.Kind, key)] = "new:" + op.ItemId;
                        break;
                    case UpdateEntityOperation update:
                    {
                        var target = LiveRow(batch.Kind, update.LocalKey);
                        if (target?.Content is null || ContentHash.Of(codec.Write(target.Content)) != update.ExpectedLocalHash ||
                            update.AliasKeysToAdd.All.Any(k => OwnerOf(k) is { } owner && owner != update.LocalKey))
                        {
                            changed.Add(op.ItemId);
                            continue;
                        }

                        foreach (var key in update.AliasKeysToAdd.All) claimed[(batch.Kind, key)] = update.LocalKey;
                        break;
                    }
                    case BindOnlyOperation bind:
                        if (LiveRow(batch.Kind, bind.LocalKey) is null ||
                            bind.AliasKeysToAdd.All.Any(k => OwnerOf(k) is { } owner && owner != bind.LocalKey))
                        {
                            changed.Add(op.ItemId);
                            continue;
                        }

                        foreach (var key in bind.AliasKeysToAdd.All) claimed[(batch.Kind, key)] = bind.LocalKey;
                        break;
                    case DeleteEntityOperation delete:
                    {
                        var target = LiveRow(batch.Kind, delete.LocalKey);
                        if (target?.Content is null || ContentHash.Of(codec.Write(target.Content)) != delete.ExpectedLocalHash)
                            changed.Add(op.ItemId);
                        break;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>Adds alias keys to a live row; a key a tombstone owns retires that tombstone into it (§5.3).</summary>
    private void AddAliases(SimRow row, EntityKeys aliases, List<(SimRow Live, SimRow Tombstone)> retires)
    {
        foreach (var key in aliases.All.Where(k => !row.Keys.Contains(k)))
        {
            if (TombstoneOwner(row.Kind, key) is { } tombstone)
            {
                if (!retires.Any(r => r.Tombstone == tombstone)) retires.Add((row, tombstone));
                continue;
            }

            row.Keys.Add(key);
        }
    }

    /// <summary>
    /// Retires tombstone T into live L (§5.3): L takes T's keys and <c>Max(L, T)</c> (revision <c>Retire</c>, no
    /// counter, a Seq bump); T's bases move to L on links that have none for L, and are dropped elsewhere.
    /// </summary>
    private void Retire(SimRow live, SimRow tombstone)
    {
        _world.Count("retire");
        foreach (var key in tombstone.Keys.Where(k => !live.Keys.Contains(k))) live.Keys.Add(key);
        live.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Retire, live.Vv, null, false, false, Actor, NextCounter,
            tombstone.Vv);
        live.Seq = NextSeq();
        Rows.Remove(tombstone);
        foreach (var link in Links.Values)
        {
            if (!link.Bases.Remove((tombstone.Kind, tombstone.Primary), out var moved)) continue;
            if (!link.Bases.ContainsKey((live.Kind, live.Primary)))
                link.Bases[(live.Kind, live.Primary)] = moved with { Key = live.Primary };
        }

        _world.Log($"{Name}: retired tombstone {tombstone.Primary.Value[..6]} into {live.Name}");
    }

    /// <summary>The store's base write (C's <c>UpsertBasesAsync</c> semantics).</summary>
    public static void UpsertBase(SimLink link, DataSyncBaseUpdate update)
    {
        var existing = link.Bases.GetValueOrDefault((update.Kind, update.Key));
        var excluded = update.State == DataSyncBaseState.Excluded;
        var exclusionKeys = (existing?.ExclusionKeys ?? []).ToList();
        if (excluded)
        {
            exclusionKeys.Add(update.Key.Value);
            exclusionKeys.AddRange(update.Record?.Keys ?? []);
        }

        var record = !excluded && update.Record is not null ? update.Record : existing?.Record;
        var pending = update.Pending ?? (update.ClearPending ? null : existing?.Pending);
        link.Bases[(update.Kind, update.Key)] = new DataSyncPeerBase(update.Kind, update.Key, update.State,
            excluded ? update.Exclusion : null, record?.Vv,
            update.ChildMap ?? existing?.ChildMap ?? new Dictionary<string, string>(), pending, record,
            excluded ? exclusionKeys.Distinct(StringComparer.Ordinal).ToList() : []);
    }

    private void Pause(SimLink link, DataSyncPauseReason reason, string? detail)
    {
        _world.Count("pause:" + reason + (detail?.StartsWith("restored", StringComparison.Ordinal) == true ? ":restored" : ""));
        link.Paused = reason;
        link.PausedDetail = detail;
        _world.Log($"{Name}: {link} paused ({detail})");
        if (reason is not (DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected))
            Notify("DataSync:" + link.Peer.NodeId, "paused", []);
    }

    // ---- the actor guard (§5.6) ---------------------------------------------------------------------

    /// <summary>
    /// <c>CheckAsync</c>: an identity change rotates (a deliberate reset, no pause); <c>actor.json</c> ahead of the
    /// database (a higher generation, the same actor with a larger counter, another database instance) rotates and
    /// pauses every link <c>LocalRestoreDetected</c>.
    /// </summary>
    public void CheckActor()
    {
        var local = Db.Local;
        if (local.ActorId != DataSyncActorId.Derive(NodeId, Epoch, local.Salt).Value)
        {
            Rotate(null);
            return;
        }

        var w = Watermark;
        var ahead = w.Generation > local.Generation || (w.ActorId == local.ActorId && w.Counter > local.ActorCounter) ||
                    w.DbInstanceId != local.DbInstanceId;
        if (!ahead) return;
        _world.Log($"{Name}: actor.json is ahead (gen {w.Generation} vs {local.Generation}, counter {w.Counter} vs {local.ActorCounter})");
        if (w.ActorId != local.ActorId && !local.RetiredActors.ContainsKey(w.ActorId))
            local.RetiredActors[w.ActorId] = w.Counter;   // the lost actor is this device's too
        ReportEvidence(new DataSyncRestoreEvidence(DataSyncRestoreEvidence.Watermark, null),
            w.ActorId == local.ActorId ? local.ActorId : null, w.ActorId == local.ActorId ? w.Counter : null);
    }

    /// <summary>
    /// Restore evidence (§5.6): about a retired actor it only raises the recorded counter; otherwise the actor
    /// rotates and the links pause as <see cref="DataSyncAnomalies.RestorePause"/> says.
    /// </summary>
    private void ReportEvidence(DataSyncRestoreEvidence evidence, string? actorId, long? counter)
    {
        var local = Db.Local;
        if (actorId is not null && actorId != local.ActorId)
        {
            if (local.RetiredActors.TryGetValue(actorId, out var recorded) && counter > recorded)
                local.RetiredActors[actorId] = counter.Value;
            return;
        }

        Rotate(counter);
        local.Evidence.Add(evidence);
        var pause = DataSyncAnomalies.RestorePause(local.Evidence);
        _world.Count($"evidence:{evidence.Source}:{pause}");
        if (pause == DataSyncPauseReason.LocalRestoreDetected)
        {
            local.RestoreReason = pause;
            local.RestoreLinkId = null;
            foreach (var link in Links.Values.Where(l => !l.Stopped))
            {
                link.Paused = pause;
                link.PausedDetail = "restore";
            }
        }
        else if (pause == DataSyncPauseReason.LocalRestoreSuspected &&
                 Links.Values.FirstOrDefault(l => l.Peer.NodeId == evidence.NodeId) is { } link)
        {
            local.RestoreReason ??= pause;
            local.RestoreLinkId ??= link.Id;
            link.Paused = pause;
            link.PausedDetail = "restore";
        }

        _world.Log($"{Name}: restore evidence {evidence.Source} ({evidence.NodeId}) → {pause}");
        Notify("DataSync", "restore", []);
    }

    /// <summary>A rotation (§5.6): the current actor retires with its recorded counter; a new salt, a new actor.</summary>
    private void Rotate(long? evidenceCounter)
    {
        var local = Db.Local;
        var watermarkCounter = Watermark.ActorId == local.ActorId ? Watermark.Counter : (long?)null;
        local.RetiredActors[local.ActorId] = DataSyncAnomalies.RetiredCounter(local.ActorCounter, watermarkCounter, evidenceCounter);
        local.Salt = _world.NewSalt();
        local.Generation = Math.Max(local.Generation, Watermark.Generation) + 1;
        local.ActorId = DataSyncActorId.Derive(NodeId, Epoch, local.Salt).Value;
        local.ActorCounter = 0;
        WriteWatermark();
        _world.Log($"{Name}: rotated to {local.ActorId} (gen {local.Generation})");
    }

    /// <summary>Verified once every Active link's peer answered a head, or after two minutes (§5.6).</summary>
    public void UpdateVerified()
    {
        if (Verified) return;
        var needed = Links.Values.Where(l => !l.Stopped && l.Paused is null).Select(l => l.Id).ToList();
        var answered = needed.All(_headsAnswered.Contains);
        if (answered || Now - _startedAt >= UnverifiedTimeout)
        {
            Verified = true;
            _world.Log($"{Name}: verified");
            if (_directoryRestored && (!answered || needed.Count == 0))
            {
                // §5.6's residual windows: a whole directory restored and every peer silent for two minutes, or a
                // device with readers but no Active link of its own (verified at once); the counters Refresh issues
                // before any evidence may be ones the peers already hold.
                ResidualWindow = true;
                ResidualActors.Add(Db.Local.ActorId);
                _world.Count(answered ? "residualWindow:noActiveLink" : "residualWindow:verifiedByTimeout");
            }
        }
    }

    /// <summary>A whole directory was restored and the actor was verified by the timeout (§5.6 residual risk).</summary>
    public bool ResidualWindow { get; private set; }

    /// <summary>The actors that may have reissued counters in a residual window.</summary>
    public HashSet<string> ResidualActors { get; } = new(StringComparer.Ordinal);

    private bool _directoryRestored;

    /// <summary>A process start: the actor is unverified until peers answer or the timeout passes.</summary>
    public void Restart(bool directoryRestored = false)
    {
        Verified = false;
        _startedAt = Now;
        _headsAnswered.Clear();
        _directoryRestored = directoryRestored;
        UpdateVerified();
    }
}

using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

// §8.4, record by record: each record is handled by the first matching row.
internal sealed partial class DataSyncMergeEngine
{
    /// <summary>What one record (or one M group) proposes.</summary>
    private sealed class Proposal(KindState kind, Candidate? candidate)
    {
        public KindState Kind { get; } = kind;
        public Candidate? Candidate { get; } = candidate;
        public ApplyOperation? Operation { get; set; }
        public DataSyncRevisionDecision? Revision { get; set; }
        public List<DataSyncBaseUpdate> BaseUpdates { get; } = [];

        /// <summary>Clears the base row a re-merged pending record waited on, when its outcome lands elsewhere.</summary>
        public List<DataSyncBaseUpdate> SourceClears { get; } = [];

        public List<DataSyncInboxDraft> Items { get; } = [];
        public DataSyncOverlayChange? Overlay { get; set; }
        public List<DataSyncMergeNote> Notes { get; } = [];
        public DataSyncClosureHint? Hint { get; set; }
        public List<SyncKey> Evaluated { get; } = [];
        public SyncKey? ServeTombstone { get; set; }

        /// <summary>The live entity this proposal touches (order placement).</summary>
        public string? EntityLocalKey { get; set; }

        public bool OrderKeySet { get; set; }
        public string? NewOrderKey { get; set; }
        public IReadOnlyList<SyncKey> AliasKeys { get; set; } = [];

        public bool IsNewDeletion { get; set; }
        public bool Deletes { get; set; }
        public string Name { get; set; } = "";

        /// <summary>Rows M and T3: every record the one decision covers (their source rows are cleared like the candidate's).</summary>
        public IReadOnlyList<Candidate>? Group { get; set; }
    }

    private void EvaluateKind(KindState k)
    {
        foreach (var c in k.Candidates)
        {
            if (c.SameEntity is { } group)
            {
                // Row M is decided once, for the whole group, at its first record.
                if (group[0] == c) Add(RowM(k, group));
                continue;
            }

            if (Evaluate(k, c) is { } proposal) Add(proposal);
        }
    }

    private void Add(Proposal p)
    {
        AddClears(p);
        _proposals.Add(p);
    }

    /// <summary>
    /// The rows a proposal's records leave (<see cref="Proposal.SourceClears"/>), so a record is never left behind on
    /// the row it waited on while its outcome lands on another: stored twice, it was merged twice into one entity at
    /// the next apply.
    /// </summary>
    private static void AddClears(Proposal p)
    {
        void Clear(DataSyncPeerBase source)
        {
            if (p.BaseUpdates.Any(u => u.Kind == source.Kind && u.Key.Value == source.Key.Value) ||
                p.SourceClears.Any(u => u.Kind == source.Kind && u.Key.Value == source.Key.Value)) return;
            // The record's outcome now lives on another row: the row it waited on keeps its state, not the record.
            p.SourceClears.Add(new DataSyncBaseUpdate(source.Kind, source.Key, source.State, source.Exclusion, null,
                null, null, true));
        }

        foreach (var c in p.Group ?? (p.Candidate is null ? [] : [p.Candidate]))
        {
            if (c.SourceBase is { } source) Clear(source);
            foreach (var duplicate in c.DuplicateSources) Clear(duplicate);
        }

        if (p.Candidate is { SourceBase: null } incoming)
        {
            // §8.4 condition 1 wherever the older record waits: a pull record replaces the pending record of its own
            // entity (its primary key is one of the record's keys) also when that one waits on another base row — a
            // name match waiting as Unbound before a Link bound the entity by an alias, a lineage the peer has since
            // linked or retired into this record. Left there, the stale record was re-merged later and overrode
            // newer outcomes: it closed a DeletedThere question and cleared its pending record, or met the new record
            // as a row M on one delivery and not on the next (found by the convergence simulator).
            foreach (var stale in p.Kind.Bases.Values.Where(b => b.Pending is { } pending &&
                                                                 incoming.Record.Keys.Contains(pending.Record.Keys[0]) &&
                                                                 p.BaseUpdates.All(u => u.Key != b.Key)))
                p.SourceClears.Add(new DataSyncBaseUpdate(stale.Kind, stale.Key, stale.State, stale.Exclusion, null, null, null, true));
        }
    }

    private Proposal? Evaluate(KindState k, Candidate c)
    {
        var bind = c.Bind;
        if (bind.Kind == BindingKind.Excluded) return null;                           // row E
        // A copy once takes no deletion (§8.1): not the peer's, and not this device's by reviving what it deleted.
        if (_link.CopyOnce is not null && (c.Record.Deleted || bind.Kind == BindingKind.Tombstone)) return null;
        if (UndoneHere(k, c) is { } undone) return undone;                           // row E, an undone create
        if (c.Entity.Held is not null || (bind.Live?.Unreadable ?? false) || k.Codec is null)
            return Waiting(k, c, DataSyncPendingReason.Held);                         // row H
        if (c.Drift) return Drift(k, c);                                              // row A2, drift
        switch (bind.Kind)
        {
            case BindingKind.NotSynced:
                return NotSyncedHere(k, c);                                           // row X
            case BindingKind.Live:
                var l = bind.Live!;
                if (l.PublishHeld) return Waiting(k, c, DataSyncPendingReason.PublishHeld);   // row F
                return c.Record.Deleted ? LiveDeleted(k, c, l) : LiveChanged(k, c, l, BaseOf(k, l.Keys.Primary!.Value), []);
            case BindingKind.Many:
                return IdentityConflict(k, c);                                        // row I
            case BindingKind.Tombstone:
                return Tombstoned(k, c, bind.Tombstone!);                             // rows T0–T3
            default:
                return c.Record.Deleted ? Unbound(k, c) : Unmatched(k, c);            // rows N1–N3
        }
    }

    /// <summary>
    /// §8.11: an undone create stays unsynced on a link until the person includes it there, and [Include] leaves a
    /// base that is not excluded — the only evidence row T0 acts on. Exclusions are per link: a link made after the
    /// undo, or one that had no base for it then, has none, and reviving there would bring back what the person undid
    /// without asking. A record of it on such a link, whatever it is (held, live or a tombstone), is excluded like
    /// row E, under its keys: never revived (T0), served (T2) or asked about (T3), and never waiting on a base row a
    /// later record could take for that evidence. Null when the record binds to no undone create, or it was included.
    /// </summary>
    private Proposal? UndoneHere(KindState k, Candidate c)
    {
        if (c.Bind is not { Kind: BindingKind.Tombstone, Tombstone: { } t } ||
            t.TombstoneKind != DataSyncTombstoneKind.UndoneCreate) return null;
        var key = t.Keys.Primary!.Value;
        var b = BaseOf(k, key);
        if (b is { State: not DataSyncBaseState.Excluded }) return null;
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        Evaluated(p, key, c);
        p.BaseUpdates.Add(new DataSyncBaseUpdate(k.Kind, key, DataSyncBaseState.Excluded,
            b?.Exclusion ?? DataSyncExclusionReason.Undone, c.Record, null, null, true));
        return p;
    }

    // ---- rows H and F -------------------------------------------------------------------------------

    /// <summary>Nothing changes: the base keeps its content, and the record waits as a pending record.</summary>
    private Proposal Waiting(KindState k, Candidate c, DataSyncPendingReason reason)
    {
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        var key = TargetBaseKey(c)!.Value;
        p.BaseUpdates.Add(Pend(k, key, bound: c.Bind.Kind is not BindingKind.Nothing and not BindingKind.Many,
            Pending(c, reason), keepState: false));
        return p;
    }

    // ---- row A2, drift ----------------------------------------------------------------------------

    /// <summary>
    /// Equal vectors, different forms, and not a duplicate actor: normalization drift of a third device relayed by
    /// a hub on another build. No pause and no revision; the base takes the record.
    /// </summary>
    private Proposal Drift(KindState k, Candidate c)
    {
        var l = c.Bind.Live!;
        var p = new Proposal(k, c) { Name = k.Codec!.NameOf(l.Content), EntityLocalKey = l.LocalKey };
        p.BaseUpdates.Add(Agree(k, l.Keys.Primary!.Value, c.Record, null));
        p.Notes.Add(new DataSyncMergeNote(k.Kind, l.LocalKey, p.Name, DataSyncMergeNoteCodes.NormalizationChanged, null));
        Evaluated(p, l.Keys.Primary!.Value, c);
        return p;
    }

    // ---- row X ------------------------------------------------------------------------------------

    /// <summary>A local-only or detached entity, or a tombstone of one: ignored, and the base excludes the record.</summary>
    private Proposal NotSyncedHere(KindState k, Candidate c)
    {
        var key = TargetBaseKey(c)!.Value;
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        p.BaseUpdates.Add(new DataSyncBaseUpdate(k.Kind, key, DataSyncBaseState.Excluded,
            DataSyncExclusionReason.NotSyncedHere, c.Record, null, null, true));
        Evaluated(p, key, c);
        return p;
    }

    // ---- rows K1–K3 -------------------------------------------------------------------------------

    private Proposal LiveDeleted(KindState k, Candidate c, DataSyncLocalEntityState l)
    {
        // Row A2's collision: this device's live version and the peer's deletion share a reissued vector.
        var rel = c.Collision ? DataSyncVvRelation.Concurrent : l.Vv.CompareTo(c.Record.Vv);
        var key = l.Keys.Primary!.Value;
        var b = BaseOf(k, key);
        var name = k.Codec!.NameOf(l.Content);
        var p = new Proposal(k, c) { Name = name, EntityLocalKey = l.LocalKey };
        Evaluated(p, key, c);

        var proposal = rel is DataSyncVvRelation.DominatedBy or DataSyncVvRelation.Equal ||
                       (rel == DataSyncVvRelation.Concurrent && MergeMode == DataSyncLinkMode.Follow);
        if (!proposal)
        {
            // K2: the edit wins here and the deleting device gets DeletedHereEditedThere (row T3); K3: superseded.
            if (rel == DataSyncVvRelation.Concurrent)
            {
                p.Notes.Add(new DataSyncMergeNote(k.Kind, l.LocalKey, name, DataSyncMergeNoteCodes.EditWinsKept,
                    new Dictionary<string, string> { ["peer"] = _link.PeerName }));
            }

            // A collision's live version is reissued under a fresh counter of this device, so the deleting device
            // meets it as newer (row T3) instead of equal to its tombstone (row T2, which ignores it).
            if (c.Collision)
            {
                p.Revision = new DataSyncRevisionDecision(k.Kind, l.Keys, l.LocalKey, DataSyncRevisionKind.LocalEdit, null,
                    null, false, true, l.OrderKey, l.ChildrenLocal, null);
            }

            if (b?.Pending is not null) p.BaseUpdates.Add(Clear(k, key, b));
            return p;
        }

        // K1: a deletion proposal (K2 under Follow too). §8.6 decides, and B2 makes it a question.
        // §8.6 counts every link: an item or a pending record elsewhere waits for a person as much as one here.
        var valueCount = ValueCountOf(k, l);
        var hasOpenItem = l.OpenItemAnyLink || _in.OpenItems.Any(i => i.Kind == k.Kind && l.Keys.Contains(i.Key));
        var hasPending = l.PendingRecordAnyLink || b?.Pending is not null;
        var verdict = _in.Policy.DecideEntityDeletion(new DataSyncEntityDeletionFacts(rel, l.CreatedBySync, valueCount,
            hasOpenItem, hasPending, l.Overlay.HeldChildren.Count > 0, k.AskDeletions));
        var alreadyAsked = _in.OpenItems.Any(i => i.Type == DataSyncInboxItemType.DeletedThere && i.Kind == k.Kind &&
                                                  l.Keys.Contains(i.Key)) ||
                           b?.Pending is { Reason: DataSyncPendingReason.AwaitingDecision, Record.Deleted: true };
        p.IsNewDeletion = !alreadyAsked;

        if (verdict.IsAutomatic)
        {
            p.Operation = new DeleteEntityOperation(DataSyncMergeItemIds.Of(k.Kind, key), l.LocalKey, l.LocalHash,
                RequireNoValues: true);
            p.Revision = new DataSyncRevisionDecision(k.Kind, l.Keys, l.LocalKey, DataSyncRevisionKind.AcceptRemoteDelete,
                c.Record.Vv, null, false, false, l.OrderKey, l.ChildrenLocal, null);
            p.BaseUpdates.Add(Agree(k, key, c.Record, null));
            p.Notes.Add(new DataSyncMergeNote(k.Kind, l.LocalKey, name, DataSyncMergeNoteCodes.AutoDeleted, null));
            p.Deletes = true;
            return p;
        }

        var pending = Pending(c, DataSyncPendingReason.AwaitingDecision);
        p.BaseUpdates.Add(Pend(k, key, bound: true, pending, keepState: false));
        p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxItemType.DeletedThere, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, l, [], valueCount: valueCount), pending, ItemVv(c), l.Vv, c.Flags));
        return p;
    }

    // ---- rows K4–K6 -------------------------------------------------------------------------------

    /// <param name="aliases">Keys to record beyond the record's own (an automatic link, row N2).</param>
    private Proposal? LiveChanged(KindState k, Candidate c, DataSyncLocalEntityState l, DataSyncPeerBase? b,
        IReadOnlyList<SyncKey> aliases)
    {
        var codec = k.Codec!;
        var key = l.Keys.Primary!.Value;
        var name = codec.NameOf(l.Content);
        // A key another local row still owns (a LocalOnly or Detached one: two Synced owners are row I) is live
        // elsewhere, and the identity pre-flight refuses it as an alias (§5.3): proposing it made every apply
        // ChangedDuringApply and every re-merge propose it again (found by the convergence simulator).
        var aliasKeys = c.Record.Keys.Select(x => new SyncKey(x)).Concat(aliases)
            .Where(x => !l.Keys.Contains(x) &&
                        (k.LiveByKey.GetValueOrDefault(x.Value) is not { } owners || owners.All(o => o == l)))
            .Distinct().ToList();
        var p = new Proposal(k, c) { Name = name, EntityLocalKey = l.LocalKey, AliasKeys = aliasKeys };
        Evaluated(p, key, c);

        // Recording the record's keys retires the tombstones that own them into this entity (§5.3: Max(L, T), no
        // counter), so the record is compared with the vector the entity has once it holds their history — the one a
        // second delivery meets. Compared with L alone, one record merged as concurrent (a conflict) on its first
        // delivery and as an ancestor on its second (invariant I3, found by the convergence simulator).
        var retiring = aliasKeys.Select(x => k.TombstoneByKey.GetValueOrDefault(x.Value)).OfType<DataSyncTombstoneState>()
            .Distinct().ToList();
        var localVv = retiring.Aggregate(l.Vv, (vv, t) => DataSyncVersionVector.Max(vv, t.Vv));
        // Likewise its bases: where the entity has none on this link, a retired tombstone's base is re-pointed to it
        // (§5.3), so the merge runs three-way against that base now, as the next delivery will.
        b ??= retiring.Select(t => BaseOf(k, t.Keys.Primary!.Value))
            .FirstOrDefault(tb => tb is { State: DataSyncBaseState.Normal, Record: not null });

        // Row A2's collision: one vector, two contents this device's retired actor both produced — concurrent versions.
        // So is one linked onto an entity agreed with another of the peer's records (no ancestor there): it has no base.
        var lineage = b?.Record is not { } agreed || agreed.Keys.Any(c.Record.Keys.Contains);
        var rel = c.Collision || !lineage ? DataSyncVvRelation.Concurrent : localVv.CompareTo(c.Record.Vv);
        if (rel is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates)
        {
            // K4: R is an ancestor (or equal). The base takes it; keys are recorded.
            p.BaseUpdates.Add(Agree(k, key, c.Record, null));
            if (aliasKeys.Count > 0)
                p.Operation = new BindOnlyOperation(DataSyncMergeItemIds.Of(k.Kind, key), l.LocalKey, Keys(aliasKeys));
            return p;
        }

        // A copy once merges without a base, whatever the vectors say, so it removes nothing (§8.1), and takes the
        // peer's values whoever edited them here last: the person asked for them.
        var copyOnce = _link.CopyOnce is not null;
        var remote = c.Entity.Content!;
        var baseRecord = copyOnce || !lineage ? null : b?.Record;
        var baseRead = !copyOnce && lineage && b is { Record: { Deleted: false } } ? BaseContent(k, b) : null;
        var mode3 = copyOnce ? DataSyncMerge3Mode.NoBase
            : rel == DataSyncVvRelation.DominatedBy ? DataSyncMerge3Mode.FastForward
            : baseRead is not null ? DataSyncMerge3Mode.ThreeWay
            : DataSyncMerge3Mode.NoBase;

        // Type changes are never applied without a decision (§8.5.6).
        var localType = codec.SubtypeOf(l.Content);
        var remoteType = codec.SubtypeOf(remote);
        var baseType = baseRead is not null ? codec.SubtypeOf(baseRead.Content!) : null;
        var typeChange = localType != remoteType && (mode3 != DataSyncMerge3Mode.ThreeWay ||
                                                     baseType == localType || baseType != remoteType);
        if (typeChange) return Frozen(p, k, c, l, b, DataSyncPendingReason.TypeChange, baseType, localType, remoteType);

        // Convert, phase two (§8.5.6): the person chose Convert and phase one gave the entity the peer's type, so the
        // waiting TypeChange record now meets a local entity of its own type. The options were rebuilt from this
        // device's values with fresh ids, so only a base-free union matches them to the record's by class: name
        // merges three-way against the base from before the type change, every other scalar takes the record's. A
        // conflict that froze phase two waits as TypeChange too (ConflictItemsOnly), so its decision re-merges as phase two.
        if (c.SourceBase?.Pending?.Reason == DataSyncPendingReason.TypeChange && localType == remoteType &&
            (baseRead is null || baseType != localType))
            mode3 = DataSyncMerge3Mode.Convert;

        var remoteCl = DataSyncRecordValidation.ChildrenLocalOf(c.Record.Content);
        var baseCl = baseRead is not null && DataSyncRecordValidation.ChildrenLocalOf(baseRecord!.Content);
        var usage = _in.ChildUsage.TryGetValue((k.Kind, l.LocalKey), out var u) ? u : new Dictionary<string, int>();
        var childMap = b?.ChildMap ?? new Dictionary<string, string>();
        var winner = string.CompareOrdinal(c.Record.EditedBy?.ActorId ?? "", l.LastActor?.Value ?? "") > 0
            ? DataSyncMergeSide.Remote
            : DataSyncMergeSide.Local;

        var m3 = codec.Merge3(new DataSyncMerge3Input(baseRead?.Content, l.Content, l.Overlay, remote, mode3, childMap,
            l.ChildrenLocal, baseCl, MergeMode, copyOnce || LocalLastEditorIsSelf(l), winner, usage, c.Flags.ChildDeletions));
        if (m3.TypeChanged)
            return Frozen(p, k, c, l, b, DataSyncPendingReason.TypeChange, baseType, localType, remoteType);
        if (m3.MassDeletionCandidates.Count > 0)
            return Frozen(p, k, c, l, b, DataSyncPendingReason.MassChildDeletion, baseType, localType, remoteType,
                m3.MassDeletionCandidates);

        return Merged(p, k, c, l, b, baseRecord, mode3, m3, remoteCl, winner, aliasKeys);
    }

    /// <summary>
    /// K5/K6 once the codec merged: with no conflict the result applies; a conflict freezes the entity for this link
    /// (<see cref="ConflictItemsOnly"/>). A path a person decided (<see cref="DataSyncMergeFlags.DecidedPaths"/>) is no
    /// longer a conflict: the local value, which the decision wrote, stands.
    /// </summary>
    private Proposal Merged(Proposal p, KindState k, Candidate c, DataSyncLocalEntityState l, DataSyncPeerBase? b,
        DataSyncWireRecord? baseRecord, DataSyncMerge3Mode mode3, DataSyncMerge3Result m3,
        bool remoteCl, DataSyncMergeSide winner, IReadOnlyList<SyncKey> aliasKeys)
    {
        var codec = k.Codec!;
        var key = l.Keys.Primary!.Value;
        var conflicts = m3.Fields.Where(f => f.Resolution == DataSyncFieldResolution.Conflict &&
                                             c.Flags.DecidedPaths?.Contains(f.Path) != true)
            .ToList();
        // By the closure property (§8.5) no codec reports a conflict in FastForward; one that did freezes the entity
        // like any conflict, never a FastForward revision that absorbs the peer's counters while this device keeps its
        // own values.
        if (conflicts.Count > 0) return ConflictItemsOnly(p, k, c, l, b, mode3, m3, conflicts, aliasKeys);
        var fastForward = mode3 == DataSyncMerge3Mode.FastForward;

        // Record-level fields: childrenLocal (§3.6) and the order key (§8.5.5).
        var clField = m3.Fields.FirstOrDefault(f => f.Path == "childrenLocal");
        var mergedCl = codec.Descriptor.SupportsChildrenLocal &&
                       (fastForward
                           ? remoteCl
                           : clField is { Resolution: DataSyncFieldResolution.TookRemote or DataSyncFieldResolution.FollowTookRemote }
                               ? remoteCl
                               : l.ChildrenLocal);
        var orderKey = codec.Descriptor.HasOrder
            ? MergeOrderKey(mode3, baseRecord?.OrderKey, l.OrderKey, c.Record.OrderKey, winner)
            : null;

        // Holds and releases of this link (§8.5.4 step 0 and step 3).
        var hold = m3.HeldChildIds.Distinct(StringComparer.Ordinal)
            .Where(id => !l.Overlay.HeldChildren.Any(h => h.ChildId == id && h.LinkId == _link.LinkId))
            .Select(id => new DataSyncHeldChild(id, _link.LinkId)).ToList();
        var release = m3.ReleasedChildIds.Distinct(StringComparer.Ordinal)
            .Where(id => l.Overlay.HeldChildren.Any(h => h.ChildId == id && h.LinkId == _link.LinkId))
            .Select(id => new DataSyncHeldChild(id, _link.LinkId)).ToList();
        var overlay = l.Overlay with
        {
            HeldChildren = l.Overlay.HeldChildren.Where(h => !release.Contains(h)).Concat(hold).ToList(),
        };
        if (hold.Count > 0 || release.Count > 0) p.Overlay = new DataSyncOverlayChange(k.Kind, l.LocalKey, hold, release);

        var mergedForm = DataSyncPublication.Of(codec, m3.Merged, overlay, mergedCl, orderKey).SharedHash;
        var remoteForm = RemoteForm(k, c);
        var localForm = LocalForm(k, l);
        var equalsRemote = mergedForm is not null && mergedForm == remoteForm;
        var equalsLocal = mergedForm is not null && mergedForm == localForm;

        var mergedJson = codec.Write(m3.Merged);
        var contentChanged = !JsonNode.DeepEquals(mergedJson, codec.Write(l.Content));
        var itemId = DataSyncMergeItemIds.Of(k.Kind, key);
        if (contentChanged)
        {
            p.Operation = new UpdateEntityOperation(itemId, l.LocalKey, l.LocalHash, mergedJson, Keys(aliasKeys),
                m3.AddedChildIds, m3.RemovedChildIds);
        }
        else if (aliasKeys.Count > 0)
        {
            p.Operation = new BindOnlyOperation(itemId, l.LocalKey, Keys(aliasKeys));
        }

        var revisionKind = fastForward ? DataSyncRevisionKind.FastForward
            : m3.Fields.Any(f => f.Resolution == DataSyncFieldResolution.FollowTookRemote) ? DataSyncRevisionKind.FollowMerged
            : DataSyncRevisionKind.MergedNoConflict;
        // A concurrent merge whose result equals the peer's content but not this device's took the peer's over a local
        // difference that only this link's base weighed: it has seen both sides (SeenBoth).
        var seenBoth = revisionKind == DataSyncRevisionKind.MergedNoConflict && equalsRemote && !equalsLocal;
        p.Revision = new DataSyncRevisionDecision(k.Kind, l.Keys, l.LocalKey, revisionKind, c.Record.Vv, null,
            equalsRemote, equalsLocal, orderKey, mergedCl,
            revisionKind == DataSyncRevisionKind.FastForward ? c.Record.EditedBy : null, seenBoth);
        p.OrderKeySet = true;
        p.NewOrderKey = orderKey;
        p.BaseUpdates.Add(Agree(k, key, c.Record, m3.ChildMap));

        // One ChildDeletedInUse item per class held (state-derived: it closes when the hold goes, §9.3).
        foreach (var field in m3.Fields.Where(f => f.Resolution == DataSyncFieldResolution.DeletionHeldInUse)
                     .OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxItemType.ChildDeletedInUse, field.Path,
                Payload(k, c, l, [field], usageCount: UsageOfClass(codec, l, field, m3, _in.ChildUsage),
                    children: field.Local is { } shown ? [shown] : [], childrenTotal: 1),
                null, ItemVv(c), l.Vv, c.Flags));
        }

        var follow = m3.Fields.Count(f => f.Resolution == DataSyncFieldResolution.FollowTookRemote);
        if (follow > 0)
            p.Notes.Add(Note(k, l.LocalKey, p.Name, DataSyncMergeNoteCodes.FollowOverride, ("count", Invariant(follow))));
        if (m3.RemovedChildIds.Count > 0)
        {
            p.Notes.Add(Note(k, l.LocalKey, p.Name, DataSyncMergeNoteCodes.ChildrenRemoved,
                ("count", Invariant(m3.RemovedChildIds.Count))));
        }

        if (l.ChildrenLocal && !mergedCl ||
            m3.Warnings.Any(w => w.Code == DataSyncWarningCode.ChildrenLocalTurnedOff))
            p.Notes.Add(Note(k, l.LocalKey, p.Name, DataSyncMergeNoteCodes.ChildrenLocalTurnedOff));

        // Row K5 took a peer revision: the entity's items were resolved where that revision was made (§9.3).
        if (fastForward && c.Record.EditedBy is { } editor && !IsOwn(editor.ActorId))
            p.Hint = new DataSyncClosureHint(k.Kind, key, DataSyncInboxClosure.ResolvedElsewhere, editor);
        return p;
    }

    /// <summary>
    /// A conflict freezes the entity for this link (§8.4 row K6): nothing of the record applies — every other change of
    /// it waits with the conflicts, as a <c>Conflict</c> pending record, until a person decides them. Only the record's
    /// new keys are recorded and the child map learns the classes it matched here (never a child the merge would add,
    /// which does not exist yet).
    /// </summary>
    private Proposal ConflictItemsOnly(Proposal p, KindState k, Candidate c, DataSyncLocalEntityState l, DataSyncPeerBase? b,
        DataSyncMerge3Mode mode3, DataSyncMerge3Result m3, List<DataSyncFieldOutcome> conflicts, IReadOnlyList<SyncKey> aliasKeys)
    {
        var key = l.Keys.Primary!.Value;
        if (aliasKeys.Count > 0)
            p.Operation = new BindOnlyOperation(DataSyncMergeItemIds.Of(k.Kind, key), l.LocalKey, Keys(aliasKeys));
        var map = new Dictionary<string, string>(b?.ChildMap ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var (peerId, localId) in m3.ChildMap.Where(m => !m3.AddedChildIds.Contains(m.Value))) map[peerId] = localId;
        var pending = Pending(c, mode3 == DataSyncMerge3Mode.Convert ? DataSyncPendingReason.TypeChange : DataSyncPendingReason.Conflict);
        p.BaseUpdates.Add(new DataSyncBaseUpdate(k.Kind, key, BaseStateFor(b, bound: true), b?.Exclusion, null, map, pending,
            false));
        foreach (var field in conflicts.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxDrafts.ConflictTypeOf(field.Path), field.Path,
                Payload(k, c, l, [field]), pending, ItemVv(c), l.Vv, c.Flags));
        }

        return p;
    }

    /// <summary>
    /// K5/K6 frozen for this link: a type change waits for a <c>TypeChange</c> item (§8.5.6), a mass child deletion
    /// for a <c>MassChildDeletion</c> item (B4). Nothing of the entity applies.
    /// </summary>
    private Proposal Frozen(Proposal p, KindState k, Candidate c, DataSyncLocalEntityState l, DataSyncPeerBase? b,
        DataSyncPendingReason reason, string? baseType, string? localType, string? remoteType,
        IReadOnlyList<string>? massCandidates = null)
    {
        p.AliasKeys = [];
        var key = l.Keys.Primary!.Value;
        var pending = Pending(c, reason);
        p.BaseUpdates.Add(Pend(k, key, bound: true, pending, keepState: false));
        if (reason == DataSyncPendingReason.TypeChange)
        {
            var field = new DataSyncFieldOutcome(DataSyncInboxDrafts.TypeSubject, DataSyncFieldResolution.TypeChangeHeld,
                Display(baseType), Display(localType), Display(remoteType), Display(localType));
            p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxItemType.TypeChange, DataSyncInboxDrafts.TypeSubject,
                Payload(k, c, l, [field], valueCount: ValueCountOf(k, l), remoteSubtype: remoteType, localSubtype: localType),
                pending, ItemVv(c), l.Vv, c.Flags));
            return p;
        }

        var ids = massCandidates!.ToHashSet(StringComparer.Ordinal);
        var shown = k.Codec!.ChildrenOf(l.Content).Where(child => ids.Contains(child.Id)).Select(child => child.Display)
            .Take(DataSyncInboxDrafts.MaxListed).ToList();
        p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxItemType.MassChildDeletion, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, l, [], children: shown, childrenTotal: ids.Count), pending, ItemVv(c), l.Vv, c.Flags));
        return p;
    }

    // ---- rows T0–T3 -------------------------------------------------------------------------------

    private Proposal Tombstoned(KindState k, Candidate c, DataSyncTombstoneState t)
    {
        var key = t.Keys.Primary!.Value;
        var b = BaseOf(k, key);
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        Evaluated(p, key, c);

        // Other records of this merge — other lineages of the peer's that this device had merged into the entity it
        // deleted — bound to the same tombstone share its base row. A question about it (T3) owns the row, then a
        // live record's agreement (T2, which decides whether the tombstone is served again), then a deletion's (T1),
        // whatever the records' order: the row decides the next delivery, and one record's agreement overwriting
        // another's made a second delivery serve again or drop a question's pending record (invariant I3, found by
        // the convergence simulator).
        var siblings = k.Candidates.Where(x => x != c && x.Bind.Kind == BindingKind.Tombstone && x.Bind.Tombstone == t &&
                                               x.Primary != c.Primary).ToList();
        var siblingAsks = siblings.Any(x => !x.Record.Deleted && t.TombstoneKind != DataSyncTombstoneKind.UndoneCreate &&
                                            (x.Collision || t.Vv.CompareTo(x.Record.Vv) is not
                                                (DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates)));

        if (c.Record.Deleted)
        {
            // T1: both deleted. The same content on both sides, so the tombstone takes the peer's deletion history too
            // (Max, no counter; nothing when it already has it): a later revive then covers every base it had seen,
            // instead of reviving concurrent with a deletion it already knew (invariant I7, found by the
            // convergence simulator).
            if (siblings.All(x => x.Record.Deleted)) p.BaseUpdates.Add(Agree(k, key, c.Record, null));
            if (t.Vv.CompareTo(c.Record.Vv) is DataSyncVvRelation.DominatedBy or DataSyncVvRelation.Concurrent)
            {
                p.Revision = new DataSyncRevisionDecision(k.Kind, t.Keys, null, DataSyncRevisionKind.AcceptRemoteDelete,
                    c.Record.Vv, null, false, false, null, null, null);
            }

            return p;
        }

        if (t.TombstoneKind == DataSyncTombstoneKind.UndoneCreate)
            return Create(p, k, c, key, [key], t);                                    // T0, included

        // Row A2's collision: the peer's live version and this device's deletion share a reissued vector (T3).
        var rel = c.Collision ? DataSyncVvRelation.Concurrent : t.Vv.CompareTo(c.Record.Vv);
        if (rel is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates)
        {
            // T2: the peer still publishes what this device deleted, so it has not taken the deletion in. The tombstone
            // is served again (a Seq bump; served too when retention had stopped serving it), so the peer receives it
            // at its next pull: its cursor may be past the tombstone already, read while it did not know the entity
            // yet (row N1) and learned it later from a third device (found by the convergence simulator). The base
            // records the peer's record, so the same record — delivered twice, re-sent by a full reconciliation —
            // serves nothing again; with live siblings the row holds one of theirs, which counts as seen too. While a
            // sibling's question waits, its answer decides what the peer receives: nothing is served meanwhile.
            if (siblingAsks) return p;
            var agreedHash = b?.Record is { } agreed ? DataSyncPendingRecords.RecordHashOf(agreed) : null;
            var seen = agreedHash is not null &&
                       siblings.Where(x => !x.Record.Deleted).Append(c)
                           .Any(x => DataSyncPendingRecords.RecordHashOf(x.Record) == agreedHash);
            if (!t.Served || !seen) p.ServeTombstone = key;
            p.BaseUpdates.Add(Agree(k, key, c.Record, null));
            return p;
        }

        // T3: never an automatic revive.
        var askers = k.Candidates.Where(x => x.Bind.Kind == BindingKind.Tombstone && x.Bind.Tombstone == t && AsksT3(t, x))
            .ToList();
        if (askers.Count > 1) return askers[0] == c ? DeletedHereGroup(p, k, t, askers) : p;

        var pending = Pending(c, DataSyncPendingReason.AwaitingDecision);
        p.BaseUpdates.Add(Pend(k, key, bound: true, pending, keepState: false));
        p.Items.Add(Draft(k, key, null, DataSyncInboxItemType.DeletedHereEditedThere, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, null, [], detail: rel == DataSyncVvRelation.DominatedBy
                ? DataSyncInboxDrafts.DetailRestored
                : DataSyncInboxDrafts.DetailChangedAfterDelete),
            pending, ItemVv(c), t.Vv, c.Flags));
        return p;
    }

    /// <summary>A record bound to tombstone <paramref name="t"/> that row T3 asks about: live, readable, and newer or concurrent.</summary>
    private static bool AsksT3(DataSyncTombstoneState t, Candidate x) =>
        !x.Record.Deleted && x.Entity.Held is null && t.TombstoneKind != DataSyncTombstoneKind.UndoneCreate &&
        (x.Collision || t.Vv.CompareTo(x.Record.Vv) is not (DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates));

    /// <summary>
    /// Row T3 for two or more of the peer's records — other lineages this device had merged into the entity it
    /// deleted — that bind to one tombstone: one decision, like row M for a live entity. One item lists the records
    /// (<c>Payload.Records</c>) and refers to them by <see cref="DataSyncInboxDrafts.CombinedRecordHash"/>; each record
    /// waits as its own pending record, the one with the tombstone's primary key (else the first) on the tombstone's
    /// row and every other under its own primary, never two on one row. Decided per record, each record drafted
    /// its own item with the same subject and wrote its pending record over the other's: one of them was lost while
    /// the cursor moved past it, and a store keeping one open item per subject failed the apply at every cycle.
    /// </summary>
    private Proposal DeletedHereGroup(Proposal p, KindState k, DataSyncTombstoneState t, IReadOnlyList<Candidate> askers)
    {
        var key = t.Keys.Primary!.Value;
        p.Group = askers;
        var ordered = askers.OrderBy(x => x.Primary, StringComparer.Ordinal).ToList();
        var lead = ordered.FirstOrDefault(x => x.Primary == key.Value) ?? ordered[0];
        var records = new List<DataSyncInboxRecordRef>();
        var hashes = new List<string>();
        var vv = DataSyncVersionVector.Empty;
        var restored = true;
        foreach (var x in ordered)
        {
            Evaluated(p, key, x);
            var pending = Pending(x, DataSyncPendingReason.AwaitingDecision);
            p.BaseUpdates.Add(x == lead
                ? Pend(k, key, bound: true, pending, keepState: false)
                : Pend(k, new SyncKey(x.Primary), bound: false, pending, keepState: true));
            records.Add(new DataSyncInboxRecordRef(x.Primary, x.Entity.DisplayName,
                x.Entity.Content is { } content ? k.Codec!.SubtypeOf(content) : null));
            hashes.Add(pending.RecordHash);
            vv = DataSyncVersionVector.Max(vv, x.Record.Vv);
            restored &= !x.Collision && t.Vv.CompareTo(x.Record.Vv) == DataSyncVvRelation.DominatedBy;
        }

        var payload = Payload(k, lead, null, [], records: records,
            detail: restored ? DataSyncInboxDrafts.DetailRestored : DataSyncInboxDrafts.DetailChangedAfterDelete) with
        {
            RemoteEditor = null,
        };
        p.Items.Add(DataSyncInboxDrafts.Create(k.Kind, key, null, DataSyncInboxItemType.DeletedHereEditedThere,
            DataSyncInboxDrafts.EntitySubject, payload, DataSyncInboxDrafts.CombinedRecordHash(hashes),
            askers.Any(x => x.Collision) ? null : vv, t.Vv, DataSyncMergeFlags.None));
        return p;
    }

    // ---- rows I and M -----------------------------------------------------------------------------

    /// <summary>Row I: one record, two or more synced entities here.</summary>
    private Proposal IdentityConflict(KindState k, Candidate c)
    {
        var codec = k.Codec!;
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        var key = new SyncKey(c.Primary);
        Evaluated(p, key, c);

        // A tombstone binding to several entities names no type: no candidate can be chosen to follow it.
        var remote = c.Entity.Content;
        var remoteType = remote is null ? null : codec.SubtypeOf(remote);
        var candidates = c.Bind.Many!
            .Select(l => new DataSyncInboxCandidate(l.LocalKey, codec.NameOf(l.Content), codec.SubtypeOf(l.Content),
                remote is null ? DataSyncNaturalMatch.None : codec.MatchNatural(remote, l.Content),
                remote is not null && codec.SubtypeOf(l.Content) == remoteType))
            .OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.LocalKey, StringComparer.Ordinal).ToList();
        // Stored on the record's primary key: when that key is a candidate's, its Seq is the one §8.4 condition 2
        // compares, so the record is not re-merged at every pull while nothing changed here.
        var owner = c.Bind.Many!.FirstOrDefault(l => l.Keys.Contains(key));
        var pending = Pending(c, DataSyncPendingReason.IdentityConflict);
        p.BaseUpdates.Add(Pend(k, key, bound: false, pending, keepState: true));
        p.Items.Add(Draft(k, key, null, DataSyncInboxItemType.IdentityConflict, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, null, [], candidates: candidates), pending, c.Record.Vv, null, c.Flags));
        return p;
    }

    /// <summary>
    /// Row M: two or more records of this merge bind to one entity. Nothing applies to it and no base is written;
    /// every record waits under its own primary key, one item lists them.
    /// </summary>
    private Proposal RowM(KindState k, IReadOnlyList<Candidate> group)
    {
        var l = group[0].Bind.Live!;
        var codec = k.Codec;
        var key = l.Keys.Primary!.Value;
        var p = new Proposal(k, null) { Name = codec?.NameOf(l.Content) ?? l.LocalKey };
        p.Evaluated.Add(key);
        foreach (var c in group) p.Evaluated.Add(new SyncKey(c.Primary));

        var records = new List<DataSyncInboxRecordRef>();
        var hashes = new List<string>();
        var vv = DataSyncVersionVector.Empty;
        p.Group = group;
        foreach (var c in group.OrderBy(c => c.Primary, StringComparer.Ordinal))
        {
            var pending = Pending(c, DataSyncPendingReason.IdentityConflict);
            p.BaseUpdates.Add(Pend(k, new SyncKey(c.Primary), bound: c.Primary == key.Value, pending, keepState: true));
            records.Add(new DataSyncInboxRecordRef(c.Primary, c.Entity.DisplayName,
                c.Entity.Content is { } content && codec is not null ? codec.SubtypeOf(content) : null));
            hashes.Add(pending.RecordHash);
            vv = DataSyncVersionVector.Max(vv, c.Record.Vv);
        }

        var first = group[0];
        var payload = Payload(k, first, l, [], records: records) with { RemoteEditor = null };
        p.Items.Add(DataSyncInboxDrafts.Create(k.Kind, key, l.LocalKey, DataSyncInboxItemType.IdentityConflict,
            DataSyncInboxDrafts.EntitySubject, payload, DataSyncInboxDrafts.CombinedRecordHash(hashes), vv, l.Vv,
            DataSyncMergeFlags.None));
        return p;
    }

    // ---- rows N1–N3 -------------------------------------------------------------------------------

    /// <summary>N1: a tombstone of an entity never known here. Ignored.</summary>
    private Proposal Unbound(KindState k, Candidate c)
    {
        var key = new SyncKey(c.Primary);
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        Evaluated(p, key, c);
        if (BaseOf(k, key) is { Pending: not null } b) p.BaseUpdates.Add(Clear(k, key, b));
        return p;
    }

    /// <summary>N2 and N3: a live record that binds to nothing.</summary>
    private Proposal? Unmatched(KindState k, Candidate c)
    {
        var codec = k.Codec!;
        var remote = c.Entity.Content!;
        var remoteType = codec.SubtypeOf(remote);
        var candidates = k.Entities
            .Where(l => l.State == DataSyncEntitySyncState.Synced && !k.BoundByKey.Contains(l.LocalKey) &&
                        !k.AutoLinked.Contains(l.LocalKey))
            .Select(l => (Local: l, Match: codec.MatchNatural(remote, l.Content)))
            .Where(m => m.Match != DataSyncNaturalMatch.None)
            .ToList();

        // §3.3: a definition here that cannot be read takes part in no decision, so the record it may be waits
        // (Held(LocalUnreadable), as in a review) instead of being created beside it or offered for a link. Its name
        // and type still read, and every natural match needs them.
        if (candidates.Any(m => m.Local.Unreadable)) return Waiting(k, c, DataSyncPendingReason.Held);

        var key = new SyncKey(c.Primary);
        if (candidates.Count == 0)
        {
            var created = new Proposal(k, c) { Name = c.Entity.DisplayName };
            Evaluated(created, key, c);
            return Create(created, k, c, key, [], null);                              // N3
        }

        if (codec.Descriptor.AutoLinkIdentical && candidates is [{ Match: DataSyncNaturalMatch.Identical }] &&
            !AgreedOnThisLink(k, candidates[0].Local))
        {
            // The D09 exception: an identical extension group with a unique candidate links by itself, then
            // merges as K4/K6 without a base. Never onto an entity already agreed with another of this peer's
            // entities: that would bind two of the peer's records to one entity here (row M next pull) and, with a
            // third device, re-raise the identity questions it answered (found by the convergence simulator).
            var l = candidates[0].Local;
            // Row F first: a definition the lost-update guard holds takes no peer version, not even by a link (§6.5).
            if (l.PublishHeld) return Waiting(k, c, DataSyncPendingReason.PublishHeld);
            k.AutoLinked.Add(l.LocalKey);
            return LiveChanged(k, c, l, null, []);
        }

        // A copy once's name matches were answered in its preview (§8.3): linked to the definition chosen there, or
        // kept beside it under another name.
        if (_link.CopyOnce is { } once)
        {
            if (once.Links.TryGetValue((k.Kind, c.Primary), out var chosen) &&
                candidates.FirstOrDefault(m => m.Local.LocalKey == chosen).Local is { } target &&
                !AgreedOnThisLink(k, target))
            {
                if (target.PublishHeld) return Waiting(k, c, DataSyncPendingReason.PublishHeld);
                k.AutoLinked.Add(target.LocalKey);
                return LiveChanged(k, c, target, null, []);
            }

            if (once.KeepBoth.Contains((k.Kind, c.Primary)))
            {
                var kept = new Proposal(k, c) { Name = c.Entity.DisplayName };
                Evaluated(kept, key, c);
                return Create(kept, k, c, key, [], null, $"{c.Entity.DisplayName} ({_link.PeerName})");
            }
        }

        // N2: never linked by name without a person (D09). A definition already agreed with another of this peer's
        // records is not offered for a link, for the reason the D09 exception gives: two of the peer's records would
        // bind to it, and every later change of either would merge without a base. The question still stands, with
        // Keep both and Skip.
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        Evaluated(p, key, c);
        var listed = candidates
            .Where(m => !AgreedOnThisLink(k, m.Local))
            .Select(m => new DataSyncInboxCandidate(m.Local.LocalKey, codec.NameOf(m.Local.Content),
                codec.SubtypeOf(m.Local.Content), m.Match, codec.SubtypeOf(m.Local.Content) == remoteType))
            .OrderByDescending(x => x.Match).ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.LocalKey, StringComparer.Ordinal).ToList();
        var pending = Pending(c, DataSyncPendingReason.AwaitingDecision);
        p.BaseUpdates.Add(Pend(k, key, bound: false, pending, keepState: true));
        p.Items.Add(Draft(k, key, null, DataSyncInboxItemType.LinkSuggestion, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, null, [], candidates: listed), pending, c.Record.Vv, null, c.Flags));
        return p;
    }

    /// <summary>
    /// N3 (a create) and T0 (a revive of an undone create, reusing the tombstone's key first): the peer's entity is
    /// created here with its keys, origin and order key, under <paramref name="name"/> when given; <c>CreatedBySync</c>
    /// follows from the revision.
    /// </summary>
    private Proposal Create(Proposal p, KindState k, Candidate c, SyncKey baseKey, IReadOnlyList<SyncKey> leadingKeys,
        DataSyncTombstoneState? revived, string? name = null)
    {
        var codec = k.Codec!;
        var remote = c.Entity.Content!;
        var prepared = codec.PrepareCreate(remote, name);
        var keys = Keys(leadingKeys.Concat(c.Record.Keys.Select(x => new SyncKey(x))).Distinct().ToList());
        var remoteCl = codec.Descriptor.SupportsChildrenLocal && DataSyncRecordValidation.ChildrenLocalOf(c.Record.Content);
        var orderKey = codec.Descriptor.HasOrder ? c.Record.OrderKey : null;
        var form = DataSyncPublication.Of(codec, prepared.Content, DataSyncOverlay.None, remoteCl, orderKey).SharedHash;

        p.Operation = new CreateEntityOperation(DataSyncMergeItemIds.Of(k.Kind, baseKey), keys, c.Record.Origin,
            c.Position, codec.Write(prepared.Content));
        p.Revision = new DataSyncRevisionDecision(k.Kind, keys, null,
            revived is null ? DataSyncRevisionKind.Create : DataSyncRevisionKind.Revive, c.Record.Vv, revived?.Vv,
            form is not null && form == RemoteForm(k, c), false, orderKey, remoteCl, c.Record.EditedBy);
        p.BaseUpdates.Add(Agree(k, baseKey, c.Record, prepared.ChildIdMap));
        p.NewOrderKey = orderKey;
        return p;
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private DataSyncPeerBase? BaseOf(KindState k, SyncKey key) => k.Bases.GetValueOrDefault(key.Value);

    /// <summary>The entity already agrees with one of this link's peer records (a base holding a record).</summary>
    private static bool AgreedOnThisLink(KindState k, DataSyncLocalEntityState l) =>
        k.Bases.GetValueOrDefault(l.Keys.Primary!.Value.Value) is { State: DataSyncBaseState.Normal, Record: not null };

    private static void Evaluated(Proposal p, SyncKey key, Candidate c)
    {
        p.Evaluated.Add(key);
        p.Evaluated.Add(new SyncKey(c.Primary));
    }

    /// <summary>
    /// A pending record, with the flags that change how the entity is merged — the child-deletion mode a decision
    /// chose — so re-deriving later gives the same result.
    /// </summary>
    private static DataSyncPendingRecord Pending(Candidate c, DataSyncPendingReason reason) =>
        DataSyncPendingRecords.Create(c.Record, reason, c.Flags);

    /// <summary>
    /// The record vector an item keeps for closure by dominance (§9.3). A collision's record carries the same
    /// vector as the entity it collides with, so that vector "dominates" the record at once and the question would
    /// close before anyone saw it, leaving the two devices apart (found by the convergence simulator): its items
    /// keep no vector and close by reconciliation or by being answered.
    /// </summary>
    private static DataSyncVersionVector? ItemVv(Candidate c) => c.Collision ? null : c.Record.Vv;

    private static DataSyncBaseUpdate Agree(KindState k, SyncKey key, DataSyncWireRecord record,
        IReadOnlyDictionary<string, string>? childMap) =>
        new(k.Kind, key, DataSyncBaseState.Normal, null, record, childMap, null, true);

    private static DataSyncBaseUpdate Clear(KindState k, SyncKey key, DataSyncPeerBase b) =>
        new(k.Kind, key, b.State, b.Exclusion, null, null, null, true);

    /// <param name="keepState">
    /// Rows I and M: a record whose primary key already keys a base row is stored as that row's pending record and
    /// the row's state is unchanged.
    /// </param>
    private static DataSyncBaseUpdate Pend(KindState k, SyncKey key, bool bound, DataSyncPendingRecord pending,
        bool keepState)
    {
        var b = k.Bases.GetValueOrDefault(key.Value);
        var state = keepState && b is not null ? b.State : BaseStateFor(b, bound);
        return new DataSyncBaseUpdate(k.Kind, key, state, b?.Exclusion, null, null, pending, false);
    }

    /// <summary>A base row's state while a record waits on it: bound rows are agreements (a missing one is back).</summary>
    private static DataSyncBaseState BaseStateFor(DataSyncPeerBase? b, bool bound) =>
        b is null ? bound ? DataSyncBaseState.Normal : DataSyncBaseState.Unbound
        : b.State == DataSyncBaseState.MissingAtPeer ? DataSyncBaseState.Normal
        : b.State;

    private static EntityKeys Keys(IEnumerable<SyncKey> keys)
    {
        var list = keys.ToList();
        return list.Count == 0 ? EntityKeys.None : new EntityKeys(list);
    }

    private bool IsOwn(string? actorId) => actorId is not null && _link.OwnActorCounters.ContainsKey(actorId);

    /// <summary>
    /// Follow on a hub (§8.1, §8.5.2): overriding a value this device did not write asks. A row with no recorded
    /// editor counts as this device's.
    /// </summary>
    private bool LocalLastEditorIsSelf(DataSyncLocalEntityState l) => l.LastActor is null || IsOwn(l.LastActor.Value.Value);

    private int? ValueCountOf(KindState k, DataSyncLocalEntityState l) =>
        _in.ValueCounts.TryGetValue((k.Kind, l.LocalKey), out var count) ? count : l.ValueCount;

    /// <summary>§8.5.5: one side changed → taken; both → the appearance winner; FastForward/Convert → the peer's.</summary>
    internal static string? MergeOrderKey(DataSyncMerge3Mode mode3, string? baseKey, string? local, string? remote,
        DataSyncMergeSide winner)
    {
        if (local == remote) return local;
        return mode3 switch
        {
            DataSyncMerge3Mode.FastForward or DataSyncMerge3Mode.Convert => remote,
            DataSyncMerge3Mode.ThreeWay => local == baseKey ? remote
                : remote == baseKey ? local
                : winner == DataSyncMergeSide.Remote ? remote : local,
            _ => local is null ? remote : remote is null ? local : winner == DataSyncMergeSide.Remote ? remote : local,
        };
    }

    private string? RemoteForm(KindState k, Candidate c) =>
        c.Entity.Content is null || k.Codec is null
            ? null
            : k.Codec.SharedHash(c.Entity.Content, c.Record.OrderKey,
                DataSyncRecordValidation.ChildrenLocalOf(c.Record.Content));

    /// <summary>The local entity's comparison form, recomputed from its stored content (never the stored hash).</summary>
    private static string? LocalForm(KindState k, DataSyncLocalEntityState l) =>
        DataSyncPublication.Of(k.Codec!, l.Content, l.Overlay, l.ChildrenLocal, l.OrderKey).SharedHash;

    private string? BaseForm(KindState k, DataSyncPeerBase b)
    {
        if (b.Record is not { } record || BaseContent(k, b) is not { } read) return null;
        return k.Codec!.SharedHash(read.Content!, record.OrderKey, DataSyncRecordValidation.ChildrenLocalOf(record.Content));
    }

    /// <summary>The base's peer content as this build reads it; null when it cannot (then there is no base to merge against).</summary>
    private CodecReadResult? BaseContent(KindState k, DataSyncPeerBase b)
    {
        if (!k.BaseContents.TryGetValue(b.Key.Value, out var read))
        {
            read = b.Record is { } record && k.Codec is not null
                ? DataSyncRecordValidation.ReadContent(k.Codec, record, _in.Limits)
                : null;
            k.BaseContents[b.Key.Value] = read;
        }

        return read;
    }

    /// <summary>Candidates and every child below them (a multilevel subtree's usage counts, §8.5.4 step 3).</summary>
    private static IEnumerable<string> WithDescendants(IDataSyncKindCodec codec, object content, IReadOnlyList<string> roots)
    {
        var children = codec.ChildrenOf(content);
        var result = new HashSet<string>(roots, StringComparer.Ordinal);
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

    /// <summary>
    /// The usage a <c>ChildDeletedInUse</c> card shows: the class's local representative (mapped from the path's
    /// peer id) and every node below it. Other members of a folded class are not counted.
    /// </summary>
    private static int? UsageOfClass(IDataSyncKindCodec codec, DataSyncLocalEntityState l, DataSyncFieldOutcome field,
        DataSyncMerge3Result m3, IReadOnlyDictionary<(string Kind, string LocalKey), IReadOnlyDictionary<string, int>> usage)
    {
        var separator = field.Path.IndexOf(':');
        if (separator < 0) return null;
        var peerId = field.Path[(separator + 1)..];
        var local = m3.ChildMap.TryGetValue(peerId, out var mapped) ? mapped : peerId;
        if (!usage.TryGetValue((codec.Descriptor.Kind, l.LocalKey), out var counts)) return null;
        var total = 0;
        foreach (var id in WithDescendants(codec, l.Content, [local]))
        {
            if (counts.TryGetValue(id, out var count)) total += count;
        }

        return total;
    }

    private DataSyncInboxDraft Draft(KindState k, SyncKey key, string? localKey, DataSyncInboxItemType type,
        string subject, DataSyncInboxPayload payload, DataSyncPendingRecord? pending, DataSyncVersionVector? recordVv,
        DataSyncVersionVector? localVv, DataSyncMergeFlags flags) =>
        DataSyncInboxDrafts.Create(k.Kind, key, localKey, type, subject, payload, pending?.RecordHash, recordVv, localVv,
            flags);

    private DataSyncInboxPayload Payload(KindState k, Candidate c, DataSyncLocalEntityState? l,
        IReadOnlyList<DataSyncFieldOutcome> fields, int? valueCount = null, int? usageCount = null,
        IReadOnlyList<DataSyncDisplayValue>? children = null, int childrenTotal = 0, string? remoteSubtype = null,
        string? localSubtype = null, IReadOnlyList<DataSyncInboxCandidate>? candidates = null,
        IReadOnlyList<DataSyncInboxRecordRef>? records = null, string? detail = null)
    {
        var codec = k.Codec!;
        var name = l is not null ? codec.NameOf(l.Content) : c.Entity.DisplayName;
        var subtype = l is not null ? codec.SubtypeOf(l.Content)
            : c.Entity.Content is { } content ? codec.SubtypeOf(content)
            : null;
        var origin = c.Record.Origin == _link.PeerNodeId ? _link.PeerName
            : c.Record.EditedBy is { } editor && editor.NodeId == c.Record.Origin ? editor.Name
            : null;
        return new DataSyncInboxPayload(name, subtype, _link.PeerName, c.Record.EditedBy, origin, fields, valueCount,
            usageCount, children, childrenTotal, remoteSubtype, localSubtype, candidates, records, detail);
    }

    private static DataSyncMergeNote Note(KindState k, string? localKey, string name, string code,
        params (string Key, string Value)[] args) =>
        new(k.Kind, localKey, name, code, args.Length == 0 ? null : args.ToDictionary(a => a.Key, a => a.Value));

    private static DataSyncDisplayValue? Display(string? value) => value is null ? null : new DataSyncDisplayValue(value);
}

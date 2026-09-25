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
    /// <summary>What one record (or one M group) proposes. Breakers may replace it before the result is assembled.</summary>
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

        public bool IsCreate { get; set; }
        public bool IsUpdateChange { get; set; }
        public bool IsNewDeletion { get; set; }
        public bool Deletes { get; set; }
        public int ChangeCount { get; set; }
        public string Name { get; set; } = "";
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
        if (_collectOnly) return;
        if (p.Candidate?.SourceBase is { } source &&
            p.BaseUpdates.All(u => u.Key.Value != source.Key.Value || u.Kind != source.Kind))
        {
            // The record's outcome now lives on another row: the row it waited on keeps its state, not the record.
            p.SourceClears.Add(new DataSyncBaseUpdate(source.Kind, source.Key, source.State, source.Exclusion, null,
                null, null, true));
        }

        _proposals.Add(p);
    }

    private Proposal? Evaluate(KindState k, Candidate c)
    {
        var bind = c.Bind;
        if (bind.Kind == BindingKind.Excluded) return null;                           // row E
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

    // ---- rows H, F and the waits of B5 and the children budget -------------------------------------

    /// <summary>Nothing changes: the base keeps its content, and the record waits as a pending record.</summary>
    private Proposal Waiting(KindState k, Candidate c, DataSyncPendingReason reason)
    {
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        var key = TargetBaseKey(c)!.Value;
        p.BaseUpdates.Add(Pend(k, key, bound: c.Bind.Kind is not BindingKind.Nothing and not BindingKind.Many,
            Pending(c, reason, EvaluatedSeq(c)), keepState: false));
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
        var rel = l.Vv.CompareTo(c.Record.Vv);
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

            if (b?.Pending is not null) p.BaseUpdates.Add(Clear(k, key, b));
            return p;
        }

        // K1: a deletion proposal (K2 under Follow too). §8.6 decides.
        if (_collectOnly)
        {
            Query(k, l, [], needValueCount: true);
            return p;
        }

        var valueCount = ValueCountOf(k, l);
        var hasOpenItem = _in.OpenItems.Any(i => i.Kind == k.Kind && l.Keys.Contains(i.Key));
        var verdict = _in.Policy.DecideEntityDeletion(new DataSyncEntityDeletionFacts(rel, l.CreatedBySync, valueCount,
            hasOpenItem, b?.Pending is not null, l.Overlay.HeldChildren.Count > 0, c.Flags.DeletionsAsItems));
        var alreadyAsked = _in.OpenItems.Any(i => i.Type == DataSyncInboxItemType.DeletedThere && i.Kind == k.Kind &&
                                                  l.Keys.Contains(i.Key)) ||
                           b?.Pending is { Reason: DataSyncPendingReason.AwaitingDecision, Record.Deleted: true };
        p.IsNewDeletion = !alreadyAsked;

        if (verdict.IsAutomatic)
        {
            p.Operation = new DeleteEntityOperation(DataSyncMergeItemIds.Of(k.Kind, key), l.LocalKey, l.LocalHash);
            p.Revision = new DataSyncRevisionDecision(k.Kind, l.Keys, l.LocalKey, DataSyncRevisionKind.AcceptRemoteDelete,
                c.Record.Vv, null, false, false, l.OrderKey, l.Unknown, l.ChildrenLocal, null);
            p.BaseUpdates.Add(Agree(k, key, c.Record, null));
            p.Notes.Add(new DataSyncMergeNote(k.Kind, l.LocalKey, name, DataSyncMergeNoteCodes.AutoDeleted, null));
            p.Deletes = true;
            return p;
        }

        var pending = Pending(c, DataSyncPendingReason.AwaitingDecision, l.Seq);
        p.BaseUpdates.Add(Pend(k, key, bound: true, pending, keepState: false));
        p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxItemType.DeletedThere, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, l, [], valueCount: valueCount), pending, c.Record.Vv, l.Vv, StoredFlags(c)));
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
        var aliasKeys = c.Record.Keys.Select(x => new SyncKey(x)).Concat(aliases)
            .Where(x => !l.Keys.Contains(x)).Distinct().ToList();
        var p = new Proposal(k, c) { Name = name, EntityLocalKey = l.LocalKey, AliasKeys = aliasKeys };
        Evaluated(p, key, c);

        var rel = l.Vv.CompareTo(c.Record.Vv);
        if (rel is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates)
        {
            // K4: R is an ancestor (or equal). The base takes it; keys are recorded.
            p.BaseUpdates.Add(Agree(k, key, c.Record, null));
            if (aliasKeys.Count > 0)
                p.Operation = new BindOnlyOperation(DataSyncMergeItemIds.Of(k.Kind, key), l.LocalKey, Keys(aliasKeys));
            return p;
        }

        var remote = c.Entity.Content!;
        var baseRead = b is { Record: { Deleted: false } } ? BaseContent(k, b) : null;
        var mode3 = rel == DataSyncVvRelation.DominatedBy ? DataSyncMerge3Mode.FastForward
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
        // merges three-way against the base from before the type change, every other scalar takes the record's.
        if (c.SourceBase?.Pending?.Reason == DataSyncPendingReason.TypeChange && localType == remoteType &&
            (baseRead is null || baseType != localType))
            mode3 = DataSyncMerge3Mode.Convert;

        if (!WithinBudget(codec, remote)) return Waiting(k, c, DataSyncPendingReason.OverBudget);

        var remoteCl = DataSyncRecordValidation.ChildrenLocalOf(c.Record.Content);
        var baseCl = baseRead is not null && DataSyncRecordValidation.ChildrenLocalOf(b!.Record!.Content);
        var usage = _in.ChildUsage.TryGetValue((k.Kind, l.LocalKey), out var u) ? u : new Dictionary<string, int>();
        var childMap = b?.ChildMap ?? new Dictionary<string, string>();
        var winner = string.CompareOrdinal(c.Record.EditedBy?.ActorId ?? "", l.LastActor?.Value ?? "") > 0
            ? DataSyncMergeSide.Remote
            : DataSyncMergeSide.Local;

        if (_collectOnly)
        {
            var candidates = codec.ChildDeletionCandidates(new DataSyncChildCandidatesInput(baseRead?.Content,
                l.Content, l.Overlay, remote, mode3, childMap, l.ChildrenLocal || baseCl || remoteCl));
            if (candidates.Count > 0) Query(k, l, WithDescendants(codec, l.Content, candidates), needValueCount: false);
            return p;
        }

        var m3 = codec.Merge3(new DataSyncMerge3Input(baseRead?.Content, l.Content, l.Overlay, remote, mode3, childMap,
            l.ChildrenLocal, baseCl, MergeMode, LocalLastEditorIsSelf(l), winner, usage, c.Flags.ChildDeletions));
        if (m3.TypeChanged)
            return Frozen(p, k, c, l, b, DataSyncPendingReason.TypeChange, baseType, localType, remoteType);
        if (m3.MassDeletionCandidates.Count > 0)
            return Frozen(p, k, c, l, b, DataSyncPendingReason.MassChildDeletion, baseType, localType, remoteType,
                m3.MassDeletionCandidates);

        return Merged(p, k, c, l, b, baseRead, mode3, m3, remoteCl, winner, aliasKeys);
    }

    /// <summary>K5/K6 once the codec merged: the safe part applies; conflicts become items and a pending record.</summary>
    private Proposal Merged(Proposal p, KindState k, Candidate c, DataSyncLocalEntityState l, DataSyncPeerBase? b,
        CodecReadResult? baseRead, DataSyncMerge3Mode mode3, DataSyncMerge3Result m3, bool remoteCl,
        DataSyncMergeSide winner, IReadOnlyList<SyncKey> aliasKeys)
    {
        var codec = k.Codec!;
        var key = l.Keys.Primary!.Value;
        var conflicts = m3.Fields.Where(f => f.Resolution == DataSyncFieldResolution.Conflict &&
                                             !f.Path.StartsWith(DataSyncUnknownMembers.PathPrefix, StringComparison.Ordinal))
            .ToList();

        // Record-level fields: childrenLocal (§3.6), the order key (§8.5.5) and unknown members (§8.9).
        var clField = m3.Fields.FirstOrDefault(f => f.Path == "childrenLocal");
        var mergedCl = codec.Descriptor.SupportsChildrenLocal &&
                       (mode3 == DataSyncMerge3Mode.FastForward
                           ? remoteCl
                           : clField is { Resolution: DataSyncFieldResolution.TookRemote or DataSyncFieldResolution.FollowTookRemote }
                               ? remoteCl
                               : l.ChildrenLocal);
        var orderKey = codec.Descriptor.HasOrder
            ? MergeOrderKey(mode3, b?.Record?.OrderKey, l.OrderKey, c.Record.OrderKey, winner)
            : null;
        var unknown = DataSyncUnknownMembers.Merge(baseRead?.Unknown, l.Unknown, c.Entity.Unknown, mode3);

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

        var mergedForm = DataSyncPublication.Of(codec, m3.Merged, overlay, mergedCl, orderKey, unknown.Merged).SharedHash;
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

        var revisionKind = mode3 == DataSyncMerge3Mode.FastForward ? DataSyncRevisionKind.FastForward
            : conflicts.Count > 0 ? DataSyncRevisionKind.MergedWithConflicts
            : m3.Fields.Any(f => f.Resolution == DataSyncFieldResolution.FollowTookRemote) ? DataSyncRevisionKind.FollowMerged
            : DataSyncRevisionKind.MergedNoConflict;
        var changes = contentChanged || !equalsLocal || p.Overlay is not null;
        if (revisionKind != DataSyncRevisionKind.MergedWithConflicts || changes)
        {
            p.Revision = new DataSyncRevisionDecision(k.Kind, l.Keys, l.LocalKey, revisionKind, c.Record.Vv, null,
                equalsRemote, equalsLocal, orderKey, unknown.Merged, mergedCl,
                revisionKind == DataSyncRevisionKind.FastForward ? c.Record.EditedBy : null);
        }

        p.OrderKeySet = true;
        p.NewOrderKey = orderKey;
        p.IsUpdateChange = contentChanged || !equalsLocal;
        p.ChangeCount = m3.Fields.Count(f => f.Resolution != DataSyncFieldResolution.Unchanged) +
                        unknown.Fields.Count(f => f.Resolution != DataSyncFieldResolution.Unchanged) +
                        (orderKey != l.OrderKey ? 1 : 0);

        if (conflicts.Count == 0)
        {
            p.BaseUpdates.Add(Agree(k, key, c.Record, m3.ChildMap));
        }
        else
        {
            // Base unchanged; the child map keeps the base's classes and learns the ones this merge added.
            var map = new Dictionary<string, string>(b?.ChildMap ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            foreach (var (peerId, localId) in m3.ChildMap) map[peerId] = localId;
            var pending = Pending(c, DataSyncPendingReason.Conflict, l.Seq);
            p.BaseUpdates.Add(new DataSyncBaseUpdate(k.Kind, key, BaseStateFor(b, bound: true), b?.Exclusion, null,
                map, pending, false));
            foreach (var field in conflicts.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxDrafts.ConflictTypeOf(field.Path), field.Path,
                    Payload(k, c, l, [field]), pending, c.Record.Vv, l.Vv, StoredFlags(c)));
            }
        }

        // One ChildDeletedInUse item per class held (state-derived: it closes when the hold goes, §9.3).
        foreach (var field in m3.Fields.Where(f => f.Resolution == DataSyncFieldResolution.DeletionHeldInUse)
                     .OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxItemType.ChildDeletedInUse, field.Path,
                Payload(k, c, l, [field], usageCount: UsageOfClass(codec, l, field, m3, _in.ChildUsage),
                    children: field.Local is { } shown ? [shown] : [], childrenTotal: 1),
                null, c.Record.Vv, l.Vv, StoredFlags(c)));
        }

        var follow = m3.Fields.Count(f => f.Resolution == DataSyncFieldResolution.FollowTookRemote);
        if (follow > 0)
            p.Notes.Add(Note(k, l.LocalKey, p.Name, DataSyncMergeNoteCodes.FollowOverride, ("count", Invariant(follow))));
        if (m3.RemovedChildIds.Count > 0)
        {
            p.Notes.Add(Note(k, l.LocalKey, p.Name, DataSyncMergeNoteCodes.ChildrenRemoved,
                ("count", Invariant(m3.RemovedChildIds.Count))));
        }

        if (unknown.ConflictsKeptLocal.Count > 0)
        {
            p.Notes.Add(Note(k, l.LocalKey, p.Name, DataSyncMergeNoteCodes.UnknownMembersKeptLocal,
                ("members", string.Join(",", unknown.ConflictsKeptLocal))));
        }

        if (l.ChildrenLocal && !mergedCl ||
            m3.Warnings.Any(w => w.Code == DataSyncWarningCode.ChildrenLocalTurnedOff))
            p.Notes.Add(Note(k, l.LocalKey, p.Name, DataSyncMergeNoteCodes.ChildrenLocalTurnedOff));

        // Row K5 took a peer revision: the entity's items were resolved where that revision was made (§9.3).
        if (mode3 == DataSyncMerge3Mode.FastForward && c.Record.EditedBy is { } editor && !IsOwn(editor.ActorId))
            p.Hint = new DataSyncClosureHint(k.Kind, key, DataSyncInboxClosure.ResolvedElsewhere, editor);
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
        if (_collectOnly)
        {
            if (reason == DataSyncPendingReason.TypeChange) Query(k, l, [], needValueCount: true);
            return p;
        }

        var key = l.Keys.Primary!.Value;
        var pending = Pending(c, reason, l.Seq);
        p.BaseUpdates.Add(Pend(k, key, bound: true, pending, keepState: false));
        if (reason == DataSyncPendingReason.TypeChange)
        {
            var field = new DataSyncFieldOutcome(DataSyncInboxDrafts.TypeSubject, DataSyncFieldResolution.TypeChangeHeld,
                Display(baseType), Display(localType), Display(remoteType), Display(localType));
            p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxItemType.TypeChange, DataSyncInboxDrafts.TypeSubject,
                Payload(k, c, l, [field], valueCount: ValueCountOf(k, l), remoteSubtype: remoteType, localSubtype: localType),
                pending, c.Record.Vv, l.Vv, StoredFlags(c)));
            return p;
        }

        var ids = massCandidates!.ToHashSet(StringComparer.Ordinal);
        var shown = k.Codec!.ChildrenOf(l.Content).Where(child => ids.Contains(child.Id)).Select(child => child.Display)
            .Take(DataSyncInboxDrafts.MaxListed).ToList();
        p.Items.Add(Draft(k, key, l.LocalKey, DataSyncInboxItemType.MassChildDeletion, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, l, [], children: shown, childrenTotal: ids.Count), pending, c.Record.Vv, l.Vv, StoredFlags(c)));
        return p;
    }

    // ---- rows T0–T3 -------------------------------------------------------------------------------

    private Proposal Tombstoned(KindState k, Candidate c, DataSyncTombstoneState t)
    {
        var key = t.Keys.Primary!.Value;
        var b = BaseOf(k, key);
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        Evaluated(p, key, c);

        if (c.Record.Deleted)
        {
            // T1: both deleted.
            p.BaseUpdates.Add(Agree(k, key, c.Record, null));
            return p;
        }

        if (t.TombstoneKind == DataSyncTombstoneKind.UndoneCreate)
            return Create(p, k, c, key, [key], t);                                    // T0

        var rel = t.Vv.CompareTo(c.Record.Vv);
        if (rel is DataSyncVvRelation.Equal or DataSyncVvRelation.Dominates)
        {
            // T2: the peer will receive this device's deletion; an unserved tombstone is served again.
            if (!t.Served) p.ServeTombstone = key;
            if (b?.Pending is not null) p.BaseUpdates.Add(Clear(k, key, b));
            return p;
        }

        // T3: never an automatic revive.
        if (_collectOnly) return p;
        var pending = Pending(c, DataSyncPendingReason.AwaitingDecision, t.Seq);
        p.BaseUpdates.Add(Pend(k, key, bound: true, pending, keepState: false));
        p.Items.Add(Draft(k, key, null, DataSyncInboxItemType.DeletedHereEditedThere, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, null, [], detail: rel == DataSyncVvRelation.DominatedBy
                ? DataSyncInboxDrafts.DetailRestored
                : DataSyncInboxDrafts.DetailChangedAfterDelete),
            pending, c.Record.Vv, t.Vv, StoredFlags(c)));
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
        if (_collectOnly) return p;

        // A tombstone binding to several entities names no type: no candidate can be chosen to follow it.
        var remote = c.Entity.Content;
        var remoteType = remote is null ? null : codec.SubtypeOf(remote);
        var candidates = c.Bind.Many!
            .Select(l => new DataSyncInboxCandidate(l.LocalKey, codec.NameOf(l.Content), codec.SubtypeOf(l.Content),
                remote is null ? DataSyncNaturalMatch.None : codec.MatchNatural(remote, l.Content),
                remote is not null && codec.SubtypeOf(l.Content) == remoteType))
            .OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.LocalKey, StringComparer.Ordinal).ToList();
        var pending = Pending(c, DataSyncPendingReason.IdentityConflict, 0);
        p.BaseUpdates.Add(Pend(k, key, bound: false, pending, keepState: true));
        p.Items.Add(Draft(k, key, null, DataSyncInboxItemType.IdentityConflict, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, null, [], candidates: candidates), pending, c.Record.Vv, null, StoredFlags(c)));
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
        if (_collectOnly) return p;

        var records = new List<DataSyncInboxRecordRef>();
        var hashes = new List<string>();
        var vv = DataSyncVersionVector.Empty;
        foreach (var c in group.OrderBy(c => c.Primary, StringComparer.Ordinal))
        {
            var pending = Pending(c, DataSyncPendingReason.IdentityConflict, c.Primary == key.Value ? l.Seq : 0);
            p.BaseUpdates.Add(Pend(k, new SyncKey(c.Primary), bound: c.Primary == key.Value, pending, keepState: true));
            if (c.SourceBase is { } source && source.Key.Value != c.Primary)
                p.SourceClears.Add(new DataSyncBaseUpdate(source.Kind, source.Key, source.State, source.Exclusion, null,
                    null, null, true));
            records.Add(new DataSyncInboxRecordRef(c.Primary, c.Entity.DisplayName,
                c.Entity.Content is { } content && codec is not null ? codec.SubtypeOf(content) : null));
            hashes.Add(pending.RecordHash);
            vv = DataSyncVersionVector.Max(vv, c.Record.Vv);
        }

        var first = group[0];
        var payload = Payload(k, first, l, [], records: records) with { RemoteEditor = null };
        p.Items.Add(DataSyncInboxDrafts.Create(k.Kind, key, l.LocalKey, DataSyncInboxItemType.IdentityConflict,
            DataSyncInboxDrafts.EntitySubject, payload, DataSyncInboxDrafts.CombinedRecordHash(hashes), vv, l.Vv,
            group.Select(StoredFlags).Aggregate(DataSyncPendingRecords.Combine)));
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
            .Where(l => l.State == DataSyncEntitySyncState.Synced && !l.Unreadable && !k.BoundByKey.Contains(l.LocalKey) &&
                        !k.AutoLinked.Contains(l.LocalKey))
            .Select(l => (Local: l, Match: codec.MatchNatural(remote, l.Content)))
            .Where(m => m.Match != DataSyncNaturalMatch.None)
            .ToList();

        var key = new SyncKey(c.Primary);
        if (candidates.Count == 0)
        {
            var created = new Proposal(k, c) { Name = c.Entity.DisplayName };
            Evaluated(created, key, c);
            return Create(created, k, c, key, [], null);                              // N3
        }

        if (codec.Descriptor.AutoLinkIdentical && candidates is [{ Match: DataSyncNaturalMatch.Identical }])
        {
            // The D09 exception: an identical extension group with a unique candidate links by itself, then
            // merges as K4/K6 without a base.
            var l = candidates[0].Local;
            k.AutoLinked.Add(l.LocalKey);
            return LiveChanged(k, c, l, null, []);
        }

        // N2: never linked by name without a person (D09).
        var p = new Proposal(k, c) { Name = c.Entity.DisplayName };
        Evaluated(p, key, c);
        if (_collectOnly) return p;
        var listed = candidates
            .Select(m => new DataSyncInboxCandidate(m.Local.LocalKey, codec.NameOf(m.Local.Content),
                codec.SubtypeOf(m.Local.Content), m.Match, codec.SubtypeOf(m.Local.Content) == remoteType))
            .OrderByDescending(x => x.Match).ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.LocalKey, StringComparer.Ordinal).ToList();
        var pending = Pending(c, DataSyncPendingReason.AwaitingDecision, 0);
        p.BaseUpdates.Add(Pend(k, key, bound: false, pending, keepState: true));
        p.Items.Add(Draft(k, key, null, DataSyncInboxItemType.LinkSuggestion, DataSyncInboxDrafts.EntitySubject,
            Payload(k, c, null, [], candidates: listed), pending, c.Record.Vv, null, StoredFlags(c)));
        return p;
    }

    /// <summary>
    /// N3 (a create) and T0 (a revive of an undone create, reusing the tombstone's key first): the peer's entity is
    /// created here with its keys, origin and order key; <c>CreatedBySync</c> follows from the revision.
    /// </summary>
    private Proposal Create(Proposal p, KindState k, Candidate c, SyncKey baseKey, IReadOnlyList<SyncKey> leadingKeys,
        DataSyncTombstoneState? revived)
    {
        var codec = k.Codec!;
        var remote = c.Entity.Content!;
        if (!WithinBudget(codec, remote)) return Waiting(k, c, DataSyncPendingReason.OverBudget);
        if (_collectOnly) return p;

        var prepared = codec.PrepareCreate(remote, null);
        var keys = Keys(leadingKeys.Concat(c.Record.Keys.Select(x => new SyncKey(x))).Distinct().ToList());
        var remoteCl = codec.Descriptor.SupportsChildrenLocal && DataSyncRecordValidation.ChildrenLocalOf(c.Record.Content);
        var orderKey = codec.Descriptor.HasOrder ? c.Record.OrderKey : null;
        var form = DataSyncPublication.Of(codec, prepared.Content, DataSyncOverlay.None, remoteCl, orderKey, c.Entity.Unknown)
            .SharedHash;

        p.Operation = new CreateEntityOperation(DataSyncMergeItemIds.Of(k.Kind, baseKey), keys, c.Record.Origin,
            c.Position, codec.Write(prepared.Content));
        p.Revision = new DataSyncRevisionDecision(k.Kind, keys, null,
            revived is null ? DataSyncRevisionKind.Create : DataSyncRevisionKind.Revive, c.Record.Vv, revived?.Vv,
            form is not null && form == RemoteForm(k, c), false, orderKey, c.Entity.Unknown, remoteCl, c.Record.EditedBy);
        p.BaseUpdates.Add(Agree(k, baseKey, c.Record, prepared.ChildIdMap));
        p.IsCreate = true;
        p.NewOrderKey = orderKey;
        p.ChangeCount = 0;
        return p;
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private DataSyncPeerBase? BaseOf(KindState k, SyncKey key) => k.Bases.GetValueOrDefault(key.Value);

    private static void Evaluated(Proposal p, SyncKey key, Candidate c)
    {
        p.Evaluated.Add(key);
        p.Evaluated.Add(new SyncKey(c.Primary));
    }

    /// <summary>The children budget of one pull (§7.5.4): an entity that would exceed it waits as <c>OverBudget</c>.</summary>
    private bool WithinBudget(IDataSyncKindCodec codec, object content)
    {
        var children = codec.ChildCountOf(content);
        if (_childrenMerged > 0 && _childrenMerged + children > _in.Limits.MaxChildrenPerStagedPull) return false;
        _childrenMerged += children;
        return true;
    }

    private long EvaluatedSeq(Candidate c) => c.Bind.Kind switch
    {
        BindingKind.Live or BindingKind.NotSynced when c.Bind.Live is { } l => l.Seq,
        BindingKind.Tombstone or BindingKind.NotSynced when c.Bind.Tombstone is { } t => t.Seq,
        _ => 0,
    };

    private static DataSyncPendingRecord Pending(Candidate c, DataSyncPendingReason reason, long evaluatedSeq) =>
        DataSyncPendingRecords.Create(c.Record, reason, evaluatedSeq, StoredFlags(c));

    /// <summary>
    /// The flags a pending record and its items keep (§8.7): the ones that change how the entity is merged — B2's
    /// and the child-deletion mode — so re-deriving later gives the same result. <c>SkipLargeChange</c> belongs to
    /// the one pull that consumes it.
    /// </summary>
    private static DataSyncMergeFlags StoredFlags(Candidate c) => c.Flags with { SkipLargeChange = false };

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
            : DataSyncContentForms.SharedHash(k.Codec, c.Entity.Content, c.Record.OrderKey,
                DataSyncRecordValidation.ChildrenLocalOf(c.Record.Content), c.Entity.Unknown);

    /// <summary>The local entity's comparison form, recomputed from its stored content (never the stored hash).</summary>
    private static string? LocalForm(KindState k, DataSyncLocalEntityState l) =>
        DataSyncPublication.Of(k.Codec!, l.Content, l.Overlay, l.ChildrenLocal, l.OrderKey, l.Unknown).SharedHash;

    private string? BaseForm(KindState k, DataSyncPeerBase b)
    {
        if (b.Record is not { } record || BaseContent(k, b) is not { } read) return null;
        return DataSyncContentForms.SharedHash(k.Codec!, read.Content!, record.OrderKey,
            DataSyncRecordValidation.ChildrenLocalOf(record.Content), read.Unknown);
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

    private void Query(KindState k, DataSyncLocalEntityState l, IEnumerable<string> childIds, bool needValueCount)
    {
        if (!_collectOnly) return;
        var key = (k.Kind, l.LocalKey);
        if (!_queries.TryGetValue(key, out var query)) query = (new SortedSet<string>(StringComparer.Ordinal), false);
        query.Ids.UnionWith(childIds);
        _queries[key] = (query.Ids, query.NeedValueCount || needValueCount);
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
            usageCount, children, childrenTotal, remoteSubtype, localSubtype, candidates, records, null, detail);
    }

    private static DataSyncMergeNote Note(KindState k, string? localKey, string name, string code,
        params (string Key, string Value)[] args) =>
        new(k.Kind, localKey, name, code, args.Length == 0 ? null : args.ToDictionary(a => a.Key, a => a.Value));

    private static DataSyncDisplayValue? Display(string? value) => value is null ? null : new DataSyncDisplayValue(value);
}

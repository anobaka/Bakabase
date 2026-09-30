using System.Globalization;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Ordering;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// One run of <see cref="DataSyncMerger"/> (§8.4). The steps, in order:
/// <list type="number">
/// <item>per kind (apply order), the candidates: the pull's records and the pending records to re-merge, a
/// pending record dropped when the pull carries a record with its primary key (§8.4 condition 1), and a pending
/// record added when a pull record takes over the base row it waits on (so row M can see both);</item>
/// <item>binding (§5.2): the exclusion index first, then live keys, tombstone keys, then nothing;</item>
/// <item>rows A1 and A2 over the whole pull: an anomaly returns before anything else is decided;</item>
/// <item>the rows, record by record in order (<see cref="Evaluate"/>);</item>
/// <item>per kind, B2 (§8.7) — a mass deletion makes every deletion of the kind a question — then full
/// reconciliation (§8.8);</item>
/// <item>the result, every list sorted.</item>
/// </list>
/// </summary>
internal sealed partial class DataSyncMergeEngine
{
    private readonly DataSyncMergeInput _in;
    private readonly DataSyncLinkContext _link;
    private readonly List<KindState> _kinds = [];
    private readonly List<Proposal> _proposals = [];

    public DataSyncMergeEngine(DataSyncMergeInput input)
    {
        _in = input;
        _link = input.Link ?? throw new ArgumentException("A merge needs its link.", nameof(input));
    }

    /// <summary>This device's retired actors: every own actor but the current one (§5.6).</summary>
    private IReadOnlyCollection<string> RetiredOwnActors =>
        _link.OwnActorCounters.Keys.Where(a => a != _link.SelfActor.Value).ToList();

    /// <summary>The mode merges run in: the effective mode, Follow for a copy once.</summary>
    private DataSyncLinkMode MergeMode =>
        _link.CopyOnce is not null || _link.EffectiveMode == DataSyncLinkMode.Follow
            ? DataSyncLinkMode.Follow
            : DataSyncLinkMode.TwoWay;

    // ---- entry points ----------------------------------------------------------------------------

    public IReadOnlyDictionary<string, Dictionary<string, IReadOnlyCollection<string>>> UsageTargets()
    {
        Prepare();
        var retired = RetiredOwnActors;
        var targets = new Dictionary<string, Dictionary<string, IReadOnlyCollection<string>>>(StringComparer.Ordinal);
        foreach (var k in _kinds)
        {
            if (k.Codec is not { } codec) continue;
            foreach (var c in k.Candidates)
            {
                if (c.Bind is not { Kind: BindingKind.Live, Live: { Unreadable: false } l }) continue;
                var rel = l.Vv.CompareTo(c.Record.Vv);
                if (rel is DataSyncVvRelation.Dominates ||
                    (rel == DataSyncVvRelation.Equal && !retired.Contains(c.Record.EditedBy?.ActorId ?? ""))) continue;
                var children = codec.ChildrenOf(l.Content).Select(child => child.Id).ToList();
                var retyped = c.Entity.Content is { } remote && codec.SubtypeOf(remote) != codec.SubtypeOf(l.Content);
                if (!c.Record.Deleted && !retyped && children.Count == 0) continue;
                if (!targets.TryGetValue(k.Kind, out var byEntity)) targets[k.Kind] = byEntity = new(StringComparer.Ordinal);
                byEntity[l.LocalKey] = children;
            }
        }

        return targets;
    }

    public DataSyncMergeResult Merge()
    {
        Prepare();

        // Rows A1 and A2 look at the whole pull before anything is decided: the runner rolls everything back.
        if (FindRegression() is { } regression) return Stopped(null, null, regression);
        if (JudgeEqualVectors(out var duplicate) is { } trip) return Stopped(trip.Reason, trip.Detail, duplicate);

        foreach (var kind in _kinds)
        {
            EvaluateKind(kind);
            if (IsMassDeletion(kind)) AskEveryDeletion(kind);
            if (kind.Staged is { FullReconciliation: true }) ReconcileMissing(kind);
        }

        return Assemble();
    }

    private static DataSyncMergeResult Stopped(DataSyncPauseReason? pause, string? detail, DataSyncAnomaly? anomaly) =>
        new(pause, detail, anomaly, [], [], [], [], [], [], new Dictionary<string, long>(), [], [], [], []);

    // ---- step 1: kinds and candidates -------------------------------------------------------------

    private sealed class KindState
    {
        public required string Kind { get; init; }
        public required IDataSyncKindCodec? Codec { get; init; }
        public DataSyncStagedKind? Staged { get; init; }
        public required IReadOnlyList<DataSyncLocalEntityState> Entities { get; init; }
        public required IReadOnlyList<DataSyncTombstoneState> Tombstones { get; init; }
        public Dictionary<string, List<DataSyncLocalEntityState>> LiveByKey { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DataSyncTombstoneState> TombstoneByKey { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DataSyncPeerBase> Bases { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ExcludedKeys { get; } = new(StringComparer.Ordinal);
        public List<Candidate> Candidates { get; } = [];

        /// <summary>Local keys some record of this merge binds to by key; natural matching skips them (row N2).</summary>
        public HashSet<string> BoundByKey { get; } = new(StringComparer.Ordinal);

        /// <summary>B2 tripped for the kind in this merge: every deletion is a question.</summary>
        public bool AskDeletions { get; set; }

        /// <summary>Local keys an identical extension group was linked to automatically in this merge.</summary>
        public HashSet<string> AutoLinked { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, CodecReadResult?> BaseContents { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Candidate
    {
        public required DataSyncIncomingEntity Entity { get; init; }
        public DataSyncWireRecord Record => Entity.Record;
        public string Primary => Record.Keys[0];

        /// <summary>The base row a re-merged pending record waited on.</summary>
        public DataSyncPeerBase? SourceBase { get; init; }

        /// <summary>
        /// Other rows that hold the same peer record (its primary key) as a pending record. A record is stored once
        /// per link and entity (§8.4); one found on several rows is merged once, and the rows its outcome does not
        /// write are cleared.
        /// </summary>
        public List<DataSyncPeerBase> DuplicateSources { get; } = [];

        public required DataSyncMergeFlags Flags { get; init; }
        public int Position { get; set; }
        public Binding Bind { get; set; } = Binding.Nothing;

        /// <summary>Row M: another record of this merge binds to the same entity.</summary>
        public List<Candidate>? SameEntity { get; set; }

        /// <summary>Row A2 found drift: equal vectors, different forms, not a duplicate actor.</summary>
        public bool Drift { get; set; }

        /// <summary>
        /// Row A2 found a collision: this device's own content and the record share a vector a retired actor of this
        /// device issued twice. It merges as concurrent (<see cref="DataSyncAnomalies.Collision"/>).
        /// </summary>
        public bool Collision { get; set; }
    }

    private enum BindingKind { Nothing, Excluded, Live, Many, NotSynced, Tombstone }

    private sealed record Binding(BindingKind Kind, DataSyncLocalEntityState? Live = null,
        IReadOnlyList<DataSyncLocalEntityState>? Many = null, DataSyncTombstoneState? Tombstone = null)
    {
        public static Binding Nothing { get; } = new(BindingKind.Nothing);
    }

    private int KindOrder(string kind)
    {
        var index = _kinds.FindIndex(k => k.Kind == kind);
        return index < 0 ? int.MaxValue : index;
    }

    private void Prepare()
    {
        var staged = (_in.Incoming?.Kinds ?? []).GroupBy(k => k.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var pendingKinds = _in.PendingToMerge.Select(p => p.Kind);
        var linkKinds = _link.Kinds.ToHashSet(StringComparer.Ordinal);
        var kinds = staged.Keys.Concat(pendingKinds).Where(linkKinds.Contains).Distinct(StringComparer.Ordinal);

        foreach (var kind in ApplyOrder(kinds))
        {
            var local = _in.Local.TryGetValue(kind, out var state) ? state : null;
            var k = new KindState
            {
                Kind = kind,
                Codec = _in.Codecs.TryGetValue(kind, out var codec) ? codec : null,
                Staged = staged.GetValueOrDefault(kind),
                Entities = local?.Entities ?? [],
                Tombstones = local?.Tombstones ?? [],
            };
            Index(k);
            CollectCandidates(k);
            _kinds.Add(k);
        }
    }

    /// <summary>Apply order (§8.4: topological by <c>DependsOn</c>, ties as <see cref="DataSyncKindIds.All"/>, then ordinal).</summary>
    private IEnumerable<string> ApplyOrder(IEnumerable<string> kinds) => DataSyncKindOrder.Of(kinds, _in.Codecs);

    private void Index(KindState k)
    {
        foreach (var entity in k.Entities)
        {
            foreach (var key in entity.Keys.All)
            {
                if (!k.LiveByKey.TryGetValue(key.Value, out var list)) k.LiveByKey[key.Value] = list = [];
                if (!list.Contains(entity)) list.Add(entity);
            }
        }

        foreach (var tombstone in k.Tombstones)
        {
            foreach (var key in tombstone.Keys.All) k.TombstoneByKey.TryAdd(key.Value, tombstone);
        }

        foreach (var ((kind, key), b) in _in.Bases)
        {
            if (kind != k.Kind) continue;
            k.Bases[key.Value] = b;
            if (b.State != DataSyncBaseState.Excluded) continue;
            k.ExcludedKeys.Add(key.Value);
            foreach (var excluded in b.ExclusionKeys) k.ExcludedKeys.Add(excluded);
        }
    }

    private void CollectCandidates(KindState k)
    {
        // The pull's records. A kind this build cannot read holds all of them.
        var incoming = new List<Candidate>();
        if (k.Staged is { } staged)
        {
            var kindHeld = !staged.Supported || k.Codec is null
                ? staged.KindHeld ?? DataSyncHeldReason.UnknownKind
                : staged.KindHeld;
            foreach (var entity in staged.Entities)
            {
                var staging = kindHeld is { } held && entity.Held is null && !entity.Record.Deleted
                    ? entity with { Content = null, Held = held }
                    : entity;
                incoming.Add(new Candidate { Entity = staging, Flags = DataSyncMergeFlags.None });
            }
        }

        // §8.4 condition 1: a newer record for the same key in the pull replaces the pending one — also when the peer
        // now publishes that key as an alias of another entity (it linked or retired the pending record's lineage).
        var incomingPrimaries = incoming.SelectMany(c => c.Record.Keys).ToHashSet(StringComparer.Ordinal);
        var pending = new List<Candidate>();
        var pendingBases = new HashSet<string>(StringComparer.Ordinal);
        var pendingByPrimary = new Dictionary<string, Candidate>(StringComparer.Ordinal);

        // One peer record found on two rows (a store that lost a clear) is merged once: the newest copy, preferably
        // on the row its own primary keys; the other rows are cleared with its outcome. Merged from both rows it made
        // two operations on one entity, and the second failed its hash check at apply.
        bool MergedAlready(DataSyncPeerBase b)
        {
            if (!pendingByPrimary.TryGetValue(b.Pending!.Record.Keys[0], out var kept)) return false;
            if (kept.SourceBase != b && !kept.DuplicateSources.Contains(b)) kept.DuplicateSources.Add(b);
            pendingBases.Add(b.Key.Value);
            return true;
        }

        void AddPending(Candidate candidate, DataSyncPeerBase b)
        {
            pending.Add(candidate);
            pendingBases.Add(b.Key.Value);
            pendingByPrimary[b.Pending!.Record.Keys[0]] = candidate;
        }

        var toMerge = _in.PendingToMerge.Distinct()
            .Where(x => x.Kind == k.Kind)
            .Select(x => k.Bases.GetValueOrDefault(x.Key.Value))
            .Where(b => b?.Pending is not null && !incomingPrimaries.Contains(b.Pending.Record.Keys[0]))
            .Select(b => b!)
            .OrderByDescending(b => b.Pending!.Record.Seq)
            .ThenBy(b => b.Key.Value == b.Pending!.Record.Keys[0] ? 0 : 1)
            .ThenBy(b => b.Key.Value, StringComparer.Ordinal);
        foreach (var b in toMerge)
        {
            if (!MergedAlready(b)) AddPending(PendingCandidate(k, b), b);
        }

        foreach (var candidate in incoming.Concat(pending)) candidate.Bind = BindRecord(k, candidate.Record);

        // A pull record that takes over a base row whose pending record belongs to another source entity: merge
        // that record too, so two records binding to one entity meet row M instead of one silently replacing the
        // other.
        foreach (var candidate in incoming.ToList())
        {
            if (TargetBaseKey(candidate) is not { } target || pendingBases.Contains(target.Value)) continue;
            if (!k.Bases.TryGetValue(target.Value, out var b) || b.Pending is null) continue;
            if (b.Pending.Record.Keys[0] == candidate.Primary || incomingPrimaries.Contains(b.Pending.Record.Keys[0])) continue;
            if (MergedAlready(b)) continue;
            var extra = PendingCandidate(k, b);
            extra.Bind = BindRecord(k, extra.Record);
            AddPending(extra, b);
        }

        // Row M again: a pull record that binds to an entity whose IdentityConflict question waits on this link with
        // another record meets that record too, so the question is derived again whichever of the two the pull
        // carries. Merged alone, the pull record merged into the entity as if the other did not exist, and one
        // delivery asked who is who while the next merged a name conflict instead (invariant I3, found by the
        // convergence simulator).
        var boundByIncoming = incoming.Where(c => c.Bind.Kind == BindingKind.Live).Select(c => c.Bind.Live!.LocalKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (key, b) in k.Bases.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            if (boundByIncoming.Count == 0) break;
            if (b.Pending is not { Reason: DataSyncPendingReason.IdentityConflict } waiting || pendingBases.Contains(key) ||
                incomingPrimaries.Contains(waiting.Record.Keys[0])) continue;
            var extra = PendingCandidate(k, b);
            extra.Bind = BindRecord(k, extra.Record);
            if (extra.Bind.Kind != BindingKind.Live || !boundByIncoming.Contains(extra.Bind.Live!.LocalKey)) continue;
            if (MergedAlready(b)) continue;
            AddPending(extra, b);
        }

        // §8.4: by Seq with ties by primary key.
        var ordered = incoming.Concat(pending).OrderBy(c => c.Record.Seq).ThenBy(c => c.Primary, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Position = i;
        k.Candidates.AddRange(ordered);

        foreach (var candidate in k.Candidates.Where(c => c.Bind.Kind == BindingKind.Live))
            k.BoundByKey.Add(candidate.Bind.Live!.LocalKey);

        // Row M: two or more records of this merge bind to one live synced entity.
        foreach (var group in k.Candidates.Where(c => c.Bind.Kind == BindingKind.Live)
                     .GroupBy(c => c.Bind.Live!.LocalKey, StringComparer.Ordinal))
        {
            var records = group.ToList();
            if (records.Select(c => c.Primary).Distinct(StringComparer.Ordinal).Count() < 2) continue;
            foreach (var candidate in records) candidate.SameEntity = records;
        }
    }

    private Candidate PendingCandidate(KindState k, DataSyncPeerBase b) => new()
    {
        Entity = DataSyncPendingRecords.Stage(k.Codec, b.Pending!, _in.Limits),
        SourceBase = b,
        Flags = b.Pending!.Flags,
    };

    // ---- step 2: binding (§5.2) ----------------------------------------------------------------

    private static Binding BindRecord(KindState k, DataSyncWireRecord record)
    {
        // The exclusion index is checked first, for bound and unbound records alike (engineering must-fix 22).
        if (record.Keys.Any(k.ExcludedKeys.Contains)) return new Binding(BindingKind.Excluded);

        var live = record.Keys.SelectMany(key => k.LiveByKey.GetValueOrDefault(key) ?? []).Distinct().ToList();
        if (live.Count > 0)
        {
            var synced = live.Where(e => e.State == DataSyncEntitySyncState.Synced).ToList();
            return synced.Count switch
            {
                1 => new Binding(BindingKind.Live, synced[0]),
                > 1 => new Binding(BindingKind.Many, Many: synced),
                // Only LocalOnly or Detached rows: the user chose not to sync this entity (row X).
                _ => new Binding(BindingKind.NotSynced, live[0]),
            };
        }

        foreach (var key in record.Keys)
        {
            if (!k.TombstoneByKey.TryGetValue(key, out var tombstone)) continue;
            return tombstone.StateAtDeletion == DataSyncEntitySyncState.Synced
                ? new Binding(BindingKind.Tombstone, Tombstone: tombstone)
                : new Binding(BindingKind.NotSynced, Tombstone: tombstone);
        }

        return Binding.Nothing;
    }

    /// <summary>
    /// The base row a record is agreed or pending on: the bound entity's (or tombstone's) primary key, else the
    /// record's own primary. Null for an excluded record, which writes nothing.
    /// </summary>
    private static SyncKey? TargetBaseKey(Candidate c) => c.Bind.Kind switch
    {
        BindingKind.Excluded => null,
        BindingKind.Live => c.Bind.Live!.Keys.Primary,
        BindingKind.NotSynced => (c.Bind.Live?.Keys ?? c.Bind.Tombstone!.Keys).Primary,
        BindingKind.Tombstone => c.Bind.Tombstone!.Keys.Primary,
        _ => new SyncKey(c.Primary),
    };

    // ---- step 3: rows A1 and A2 ------------------------------------------------------------------

    private DataSyncAnomaly? FindRegression()
    {
        var vectors = _kinds.SelectMany(k => k.Candidates
            .Where(c => c.Bind.Kind != BindingKind.Excluded && c.Entity.Held is null)
            .Select(c => (k.Kind, new SyncKey(c.Primary), c.Record.Vv)));
        return DataSyncAnomalies.FindRegression(vectors, _link.SelfActor, _link.OwnActorCounters);
    }

    /// <summary>
    /// Row A2 for every record whose vector equals its entity's (or its base's): marks drift or a collision on the
    /// candidate and returns the pause of the first duplicate actor, in merge order. A deletion and a live version
    /// under one vector differ whatever their contents: a tombstone meeting a live entity with its vector, or a live
    /// record meeting a tombstone with its vector, is judged too (the residual restore window of §5.6 reissues a
    /// counter the peer holds as a deletion; left to rows K1 and T2, the deletion question closed by dominance at
    /// once and the two sides stayed apart in silence — found by the convergence simulator).
    /// </summary>
    private DataSyncBreakerTrip? JudgeEqualVectors(out DataSyncAnomaly? anomaly)
    {
        anomaly = null;
        foreach (var k in _kinds)
        {
            if (k.Codec is null) continue;
            foreach (var c in k.Candidates)
            {
                if (c.Entity.Held is not null || c.SameEntity is not null) continue;
                bool? formsEqual = null;
                var againstLocal = false;
                if (c.Bind.Kind == BindingKind.Tombstone)
                {
                    // A live record with the vector of this device's tombstone.
                    if (c.Record.Deleted || c.Record.Vv != c.Bind.Tombstone!.Vv) continue;
                    formsEqual = false;
                    againstLocal = true;
                }
                else if (c.Bind.Kind == BindingKind.Live && c.Record.Deleted)
                {
                    // A tombstone with the vector of this device's live entity.
                    var live = c.Bind.Live!;
                    if (live.Unreadable || live.PublishHeld || c.Record.Vv != live.Vv) continue;
                    formsEqual = false;
                    againstLocal = true;
                }
                else if (c.Bind.Kind == BindingKind.Live)
                {
                    var l = c.Bind.Live!;
                    if (l.Unreadable || l.PublishHeld) continue;
                    var b = k.Bases.GetValueOrDefault(l.Keys.Primary!.Value.Value);

                    // Both forms are recomputed from stored, validated content with this build's codec; a side that
                    // cannot be read (content this build or the peer would hold) cannot be judged here.
                    var remoteForm = RemoteForm(k, c);
                    if (remoteForm is null) continue;
                    againstLocal = c.Record.Vv == l.Vv;
                    if (againstLocal)
                    {
                        if (LocalForm(k, l) is { } localForm) formsEqual = remoteForm == localForm;
                    }
                    else if (b?.Vv is { } baseVv && c.Record.Vv == baseVv && BaseForm(k, b) is { } baseForm)
                    {
                        formsEqual = remoteForm == baseForm;
                    }
                }

                if (formsEqual is not { } equal) continue;

                // A collision is only this device's own content against the record; a base re-sent with another
                // form is drift whoever produced it.
                var verdict = DataSyncAnomalies.JudgeEqualVectors(equal, c.Record.EditedBy?.ActorId, _link.SelfActor,
                    _link.PeerActorId, againstLocal ? RetiredOwnActors : null);
                // Drift re-records a live entity's base; against a tombstone there is no such base, so rows T decide.
                if (verdict == DataSyncAnomalies.Drift && c.Bind.Kind == BindingKind.Live) c.Drift = true;
                if (verdict == DataSyncAnomalies.Collision) c.Collision = true;
                if (verdict != DataSyncAnomalies.DuplicateActor || anomaly is not null) continue;

                var actor = c.Record.EditedBy!.ActorId;
                anomaly = new DataSyncAnomaly(DataSyncAnomalies.DuplicateActor, actor,
                    c.Record.Vv.Counters.TryGetValue(actor, out var counter) ? counter : 0, k.Kind, new SyncKey(c.Primary));
                var own = DataSyncAnomalies.IsOwnActor(actor, _link.OwnActorCounters) ? "true" : "false";
                return new DataSyncBreakerTrip(DataSyncPauseReason.PeerIdentityDuplicated, $"actor={actor};own={own}");
            }
        }

        return null;
    }

    // ---- step 5: B2 and full reconciliation ------------------------------------------------------------

    /// <summary>
    /// §8.8: after a full reconciliation of a kind, every agreed base whose entity is absent from the complete set
    /// (no record, live or tombstone, carries its key or its agreed record's keys) becomes <c>MissingAtPeer</c>.
    /// Nothing changes locally: missing means unknown, never a deletion.
    /// </summary>
    private void ReconcileMissing(KindState k)
    {
        var offered = k.Staged!.Entities.SelectMany(e => e.Record.Keys).ToHashSet(StringComparer.Ordinal);

        // A pending record re-merged in this merge (not in the pull) writes the base of an entity the peer may no
        // longer offer — a new agreement, or the agreement it keeps while it waits again: that base is recorded as
        // missing there too, so a second delivery of the same pull finds nothing left to change (invariant I3, found
        // by the convergence simulator).
        foreach (var p in _proposals.Where(p => p.Kind == k && p.Candidate?.SourceBase is not null))
        {
            for (var i = 0; i < p.BaseUpdates.Count; i++)
            {
                var u = p.BaseUpdates[i];
                if (u.State != DataSyncBaseState.Normal) continue;
                var agreed = u.Record ?? k.Bases.GetValueOrDefault(u.Key.Value)?.Record;
                if (agreed is null) continue;
                if (agreed.Keys.Any(offered.Contains) || offered.Contains(u.Key.Value)) continue;
                p.BaseUpdates[i] = u with { State = DataSyncBaseState.MissingAtPeer };
            }
        }

        var touched = _proposals.Where(p => p.Kind == k)
            .SelectMany(p => p.BaseUpdates.Select(u => u.Key.Value)).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, b) in k.Bases.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            if (b.State != DataSyncBaseState.Normal || b.Record is null || touched.Contains(key)) continue;
            if (offered.Contains(key) || b.Record.Keys.Any(offered.Contains)) continue;
            var p = new Proposal(k, null);
            p.BaseUpdates.Add(new DataSyncBaseUpdate(k.Kind, new SyncKey(key), DataSyncBaseState.MissingAtPeer,
                null, null, null, null, false));
            var live = k.LiveByKey.GetValueOrDefault(key)?.FirstOrDefault();
            p.Notes.Add(new DataSyncMergeNote(k.Kind, live?.LocalKey,
                live is not null && k.Codec is not null ? k.Codec.NameOf(live.Content) : NameOfRecord(b.Record),
                DataSyncMergeNoteCodes.MissingAtPeer, null));
            _proposals.Add(p);
        }
    }

    /// <summary>
    /// B2 (§8.7): more new deletions of the kind than the policy allows in one pull. New is not already asked — an
    /// open <c>DeletedThere</c> item or a deletion waiting as <c>AwaitingDecision</c> is counted once, when it came.
    /// </summary>
    private bool IsMassDeletion(KindState k) =>
        DataSyncBreakers.IsMassDeletion(_proposals.Count(p => p.Kind == k && p.IsNewDeletion),
            k.Bases.Values.Count(b => b.State == DataSyncBaseState.Normal && b.Record is not null), _in.Policy);

    /// <summary>
    /// B2 tripped: every deletion this merge would apply by itself for the kind becomes a <c>DeletedThere</c> question
    /// instead (<see cref="LiveDeleted"/>'s ask); the rest of the pull applies.
    /// </summary>
    private void AskEveryDeletion(KindState k)
    {
        k.AskDeletions = true;
        for (var i = 0; i < _proposals.Count; i++)
        {
            if (_proposals[i] is not { Deletes: true, Candidate: { } c } p || p.Kind != k) continue;
            var asked = LiveDeleted(k, c, c.Bind.Live!);
            AddClears(asked);
            _proposals[i] = asked;
        }
    }

    // ---- step 6: the result -----------------------------------------------------------------------

    private DataSyncMergeResult Assemble()
    {
        var batches = new List<ApplyBatch>();
        var order = new List<DataSyncOrderAssignment>();
        foreach (var k in _kinds)
        {
            var mine = _proposals.Where(p => p.Kind == k && p.Operation is not null).ToList();
            var ops = mine.Where(p => p.Operation is CreateEntityOperation)
                .Concat(mine.Where(p => p.Operation is UpdateEntityOperation or BindOnlyOperation))
                .Concat(mine.Where(p => p.Operation is DeleteEntityOperation))
                .Select(p => p.Operation!).ToList();
            if (ops.Count > 0) batches.Add(new ApplyBatch(k.Kind, ops));
            if (OrderOf(k) is { } assignment) order.Add(assignment);
        }

        var revisions = _proposals.Where(p => p.Revision is not null).Select(p => p.Revision!)
            .OrderBy(r => KindOrder(r.Kind)).ThenBy(r => r.Keys.Primary?.Value, StringComparer.Ordinal).ToList();

        // One update per base row: the record's own outcome wins over clearing the row a pending record moved from.
        var bases = new Dictionary<(string, string), DataSyncBaseUpdate>();
        foreach (var update in _proposals.SelectMany(p => p.BaseUpdates))
            bases[(update.Kind, update.Key.Value)] = update;
        foreach (var clear in _proposals.SelectMany(p => p.SourceClears)) bases.TryAdd((clear.Kind, clear.Key.Value), clear);
        var baseUpdates = bases.Values.OrderBy(u => KindOrder(u.Kind)).ThenBy(u => u.Key.Value, StringComparer.Ordinal)
            .ToList();

        var inbox = _proposals.SelectMany(p => p.Items).OrderBy(d => KindOrder(d.Kind))
            .ThenBy(d => d.Key.Value, StringComparer.Ordinal).ThenBy(d => d.Type)
            .ThenBy(d => d.SubjectPath, StringComparer.Ordinal).ToList();

        var overlays = _proposals.Where(p => p.Overlay is not null).Select(p => p.Overlay!)
            .OrderBy(o => KindOrder(o.Kind)).ThenBy(o => o.LocalKey, StringComparer.Ordinal).ToList();

        var cursors = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var k in _kinds.Where(k => k.Staged is not null)) cursors[k.Kind] = k.Staged!.MaxSeq;

        var notes = _proposals.SelectMany(p => p.Notes).ToList();
        notes = notes.Select((n, i) => (n, i)).OrderBy(x => KindOrder(x.n.Kind)).ThenBy(x => x.i).Select(x => x.n).ToList();

        var hints = _proposals.Where(p => p.Hint is not null).Select(p => p.Hint!)
            .OrderBy(h => KindOrder(h.Kind)).ThenBy(h => h.Key.Value, StringComparer.Ordinal).ToList();

        var evaluated = _proposals.SelectMany(p => p.Evaluated.Select(key => (p.Kind.Kind, key))).Distinct()
            .OrderBy(e => KindOrder(e.Item1)).ThenBy(e => e.key.Value, StringComparer.Ordinal).ToList();

        var serve = _proposals.Where(p => p.ServeTombstone is not null).Select(p => (p.Kind.Kind, p.ServeTombstone!.Value))
            .Distinct().OrderBy(s => KindOrder(s.Item1)).ThenBy(s => s.Value.Value, StringComparer.Ordinal).ToList();

        return new DataSyncMergeResult(null, null, null, batches, revisions, baseUpdates, inbox, overlays, order, cursors,
            notes, hints, new EvaluatedSet(evaluated), serve);
    }

    /// <summary>
    /// The shared order to place after the batches (§3.7), for a kind with order: every live synced entity with
    /// its order key after this merge, and every entity this merge creates (under its item id). Emitted only when
    /// a key changed or an entity with a key is created.
    /// </summary>
    private DataSyncOrderAssignment? OrderOf(KindState k)
    {
        if (k.Codec is not { Descriptor.HasOrder: true }) return null;
        var byEntity = _proposals.Where(p => p.Kind == k && p.EntityLocalKey is not null)
            .GroupBy(p => p.EntityLocalKey!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var entries = new List<DataSyncOrderEntry>();
        var changed = false;
        foreach (var entity in k.Entities)
        {
            if (entity.State != DataSyncEntitySyncState.Synced || entity.Unreadable || entity.PublishHeld) continue;
            var orderKey = entity.OrderKey;
            var keys = entity.Keys.All.Select(key => key.Value);
            if (byEntity.TryGetValue(entity.LocalKey, out var p))
            {
                if (p.Deletes) continue;
                if (p.OrderKeySet)
                {
                    changed |= p.NewOrderKey != entity.OrderKey;
                    orderKey = p.NewOrderKey;
                }

                // New aliases can change the tie key (the smallest key), and with it the place among equal order
                // keys: Place must run then too, or the next Refresh reads the unchanged local order as a move and
                // issues a revision (invariant I3, found by the convergence simulator).
                var before = DataSyncOrderPlanner.TieKeyOf(keys);
                keys = keys.Concat(p.AliasKeys.Select(a => a.Value));
                changed |= DataSyncOrderPlanner.TieKeyOf(keys) != before;
            }

            entries.Add(new DataSyncOrderEntry(entity.LocalKey, orderKey, DataSyncOrderPlanner.TieKeyOf(keys)));
        }

        foreach (var p in _proposals.Where(p => p.Kind == k && p.Operation is CreateEntityOperation))
        {
            var create = (CreateEntityOperation)p.Operation!;
            if (p.NewOrderKey is null) continue;
            changed = true;
            entries.Add(new DataSyncOrderEntry(create.ItemId, p.NewOrderKey, DataSyncOrderPlanner.TieKeyOf(create.Keys)));
        }

        if (!changed) return null;
        var sorted = DataSyncOrderPlanner.Sort(entries.Where(e => e.OrderKey is not null));
        return new DataSyncOrderAssignment(k.Kind, sorted.Select(e => (e.LocalKey, e.OrderKey!)).ToList());
    }

    private static string NameOfRecord(DataSyncWireRecord record) =>
        record.Content is { } content && DataSyncWireFormat.TryGetString(content, "name", out var name) &&
        name.Length > 0 && DataSyncWireFormat.IsDisplayText(name)
            ? name
            : record.Keys[0];

    /// <summary>A read-only set of evaluated subjects with value lookups (the result's <c>Evaluated</c>).</summary>
    private sealed class EvaluatedSet(IReadOnlyList<(string Kind, SyncKey Key)> items) : IReadOnlyCollection<(string Kind, SyncKey Key)>
    {
        private readonly HashSet<(string, SyncKey)> _set = items.ToHashSet();
        public int Count => items.Count;
        public bool Contains((string Kind, SyncKey Key) item) => _set.Contains(item);
        public IEnumerator<(string Kind, SyncKey Key)> GetEnumerator() => items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}

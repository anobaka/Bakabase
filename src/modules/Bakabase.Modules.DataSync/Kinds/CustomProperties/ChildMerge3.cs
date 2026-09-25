using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>The rules children merge by (§8.5.4); <c>Convert</c> and "Sync the definition only" turned off use NoBase.</summary>
internal enum ChildMergeRules { ThreeWay, FastForward, NoBase }

/// <param name="IgnoreCase">The IgnoreCase every side's classes are built with: the merged value, or the local one while
/// a conflict on it waits (§8.5.2).</param>
internal sealed record ChildMergeSettings(
    ChildListKind Kind,
    bool IgnoreCase,
    ChildMergeRules Rules,
    DataSyncLinkMode LinkMode,
    bool LocalLastEditorIsSelf,
    DataSyncMergeSide AppearanceWinner,
    IReadOnlyDictionary<string, string> ChildMap,
    DataSyncOverlay Overlay,
    DataSyncChildDeletionMode Deletions,
    DataSyncAutoApplyPolicy Policy);

internal enum PeerClassStatus
{
    Unset,

    /// <summary>Has local counterparts: the members it claimed by id or by key.</summary>
    Present,

    /// <summary>Maps onto an overlay child (or lies below one): never touched.</summary>
    Invisible,

    /// <summary>Added here with the peer's representative (step 2, FastForward, NoBase).</summary>
    Add,

    /// <summary>Deleted here, changed there since the base: edit wins, it comes back (step 1).</summary>
    Restore,

    /// <summary>Deleted here, unchanged there since the base: this device's deletion stands (step 1).</summary>
    StaysDeleted,
}

/// <summary>One label class of the peer's content (R) and what the merge does with it.</summary>
internal sealed class PeerClass(ChildClass cls, PeerClass? parent)
{
    public ChildClass Class { get; } = cls;
    public PeerClass? Parent { get; } = parent;

    /// <summary>The id of the peer's representative: the <c>{peerId}</c> of every path about this class (§8.5.1).</summary>
    public string PeerId => Class.Rep.Uuid!;

    public string Key => Class.Key;
    public ChildNode Rep => Class.Rep;

    /// <summary>ThreeWay only: its first member the base has, and that member's base class.</summary>
    public ChildNode? BaseNode { get; set; }

    public ChildClass? Base { get; set; }

    /// <summary>Its local counterparts, one group per local class; the primary group first.</summary>
    public List<ChildGroup> Groups { get; } = [];

    public ChildNode? InvisibleTarget { get; set; }

    /// <summary>Every local counterpart it has by id is already another peer class's.</summary>
    public bool Split { get; set; }

    public PeerClassStatus Status { get; set; }
    public ChildNode? Created { get; set; }

    /// <summary>Where its class is here: the first node of its first group (the one adds and moves go under).</summary>
    public ChildNode PrimaryNode => Groups[0].Owned[0];
}

/// <summary>
/// Multilevel: the members of a peer class whose parents end in one class here where the class has no node, added there
/// (see <c>ChildMerge3.PartsOf</c>). <see cref="Member"/> is the first of them in the peer's order.
/// </summary>
internal sealed class SplitPart(PeerClass peer, ChildNode member, string parentPath)
{
    public PeerClass Peer { get; } = peer;
    public ChildNode Member { get; } = member;

    /// <summary>The class key path the part's parent ends at.</summary>
    public string ParentPath { get; } = parentPath;

    /// <summary>A present class's leading group, whose key and colour the part takes; null for an added or restored
    /// class, whose parts are as the peer has them.</summary>
    public ChildGroup? Leader { get; init; }

    public ChildNode? Created { get; set; }
}

/// <summary>
/// The members of one local class that one peer class claimed. Keys, colours and parents are decided per group: a peer
/// class whose members are split over several local classes (or parents) gives each its own three-way.
/// </summary>
internal sealed class ChildGroup(PeerClass peer, ChildClass local, ChildNode remoteMember, ChildNode? baseNode,
    ChildClass? baseClass)
{
    public PeerClass Peer { get; } = peer;
    public ChildClass Local { get; } = local;

    /// <summary>The peer's member that linked this group (its representative for a group claimed by key).</summary>
    public ChildNode RemoteMember { get; } = remoteMember;

    /// <summary>Claimed by key: the local members have base counterparts of their own (or none).</summary>
    public bool ByKey { get; set; }

    /// <summary>ThreeWay only: the base node of the peer member that linked this group, and its base class.</summary>
    public ChildNode? BaseNode { get; } = baseNode;

    public ChildClass? Base { get; } = baseClass;

    public List<ChildNode> Owned { get; } = [];

    /// <summary>Multilevel, claimed by id: the parent its members have here (a node), at the peer and in the base (ids,
    /// <c>""</c> for the root; null without a base).</summary>
    public (ChildNode? Local, string? Remote, string? Base) Parents { get; init; }

    /// <summary>Creation order within one merge.</summary>
    public int Index { get; init; }

    public bool TakeRemoteKey { get; set; }

    /// <summary>The colour this local class ends with (§8.5.5).</summary>
    public string? Color { get; set; }

    /// <summary>Multilevel: the node whose parent the group's parent is judged by (see AnchorOf).</summary>
    public ChildNode? Anchor { get; set; }

    /// <summary>Multilevel: the logical parent its nodes had here, the one they end under, whether they move.</summary>
    public object? LocalParent { get; set; }

    public object? FinalParent { get; set; }
    public bool Moves { get; set; }
    public DataSyncFieldResolution? ParentResolution { get; set; }

    public int FirstSeq => Owned[0].Seq;
}

/// <param name="MassDeletionCandidates">Non-empty when B4 tripped: nothing of the children applies (§8.5.4 step 7).</param>
internal sealed record ChildMergeOutcome(
    IReadOnlyList<ChildNode> Roots,
    IReadOnlyList<DataSyncFieldOutcome> Fields,
    IReadOnlyDictionary<string, string> ChildMap,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Held,
    IReadOnlyList<string> Released,
    IReadOnlyList<string> MassDeletionCandidates,
    IReadOnlyList<DataSyncPlanWarning> Warnings);

/// <summary>
/// ChildMerge3: the children of one custom property (choices, tags or multilevel nodes) merged as label classes
/// (§8.5.4, §3.4). A flat list is a tree of roots.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mapping.</b> Every peer class (R) is matched to local members: first by id — each member's <c>ChildMap</c>
/// target, or a local option with the same id when the map has no entry — then, for a class with none, by an equal class
/// key among the local classes under its parent's counterpart (only a class no other peer class claimed; see
/// <see cref="SiblingsUnder"/>). Each local member is claimed at most once, by the first peer class in R's pre-order;
/// the members a peer class claimed in one local class that come from one base class — and, multilevel by id, hang under
/// one parent on every side — form a group. A local class's free members follow the group that claimed its first member
/// (its leader): they take its renames, its colour and its moves; outside a FastForward a multilevel free member follows
/// the group whose anchor ends under the same parent class, if any, and stays with its parent
/// (<see cref="FollowedGroup"/>). A peer class whose counterpart is an overlay child, or which lies below one, is
/// invisible: mapped, never touched. Any other local node this device does not publish (no id, below a parent without
/// an id, dropped by the reader) is no counterpart: the peer's class is matched among what is published, or added.
/// </para>
/// <para>
/// <b>Deciding.</b> Per group, against the base node of the member that linked it: the key three-way, the colour
/// (§8.5.5, written to the representative of the class it ends in) and, for multilevel, the parent over logical classes
/// (the peer class that owns or leads a parent node, or a local class nobody claimed). Moves that would close a cycle
/// keep their local parent and become conflicts, in the order of peer ids. A peer class with no counterpart is added
/// (FastForward, NoBase, not in the base), restored (in the base and changed there since) or stays deleted.
/// </para>
/// <para>
/// <b>Placing.</b> Where this merge splits a multilevel class — its parents end in several classes here — the peer's
/// members are placed by where their own parents end, as merging the other way places them (<see cref="PartsOf"/>): an
/// added or restored class is added under each of those parent classes, and a class this device has gains the members
/// the peer added or changed there.
/// </para>
/// <para>
/// <b>Deleting.</b> See <see cref="FindCandidates"/>: FastForward, every local class nobody claimed; ThreeWay, what the
/// peer deleted and this device did not change, class by class, and never an extra member of a class the peer still
/// has. A multilevel candidate goes with its subtree, and only when everything published in that subtree goes too; its
/// usage is the sum over every node of it (a node without an id, without a usage entry or kept by an overlay counts as
/// in use). B4 counts candidate classes.
/// </para>
/// <para>
/// <b>Comparing.</b> "Changed since the base" is judged as the comparison form sees it: a class is unchanged while one
/// of its members still sits in the base class it came from (the same key and colour, the same parent node, by id on
/// both sides), so moving a duplicate into another existing class changes nothing.
/// </para>
/// <para>
/// <b>Symmetry.</b> Merging (L, R, B) and (R, L, B) without conflicts ends at one comparison form (§8.5): every choice
/// that could depend on which side is local — a class's colour among members with different decisions, where a split
/// class's members go — is made by something both sides share (option ids, the peer's order of a class's members), never
/// by local order.
/// </para>
/// <para>
/// Nothing is reordered: renames and colours change nodes in place, a move or an add is appended at the end of its
/// new parent's children, and every local node the steps do not remove stays, valid or not. An add that lands beside
/// a local option of its class without an id is stored in that option instead (<see cref="AdoptTwins"/>).
/// </para>
/// </remarks>
internal sealed class ChildMerge3
{
    private static readonly object Root = new();

    private readonly ChildMergeSettings _s;
    private readonly string _prefix;
    private readonly List<ChildNode> _localRoots;
    private readonly List<ChildNode> _local = [];
    private readonly Dictionary<string, ChildNode> _localByUuid = new(StringComparer.Ordinal);
    private readonly List<ChildClass> _localRootClasses;
    private readonly List<ChildClass> _localClasses = [];
    private readonly List<PeerClass> _peers = [];
    private readonly Dictionary<string, PeerClass> _peerByMember = new(StringComparer.Ordinal);
    private readonly bool _hasBase;
    private readonly List<ChildClass> _baseClasses = [];
    private readonly Dictionary<string, ChildClass> _baseByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChildNode> _baseNodeByUuid = new(StringComparer.Ordinal);
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private readonly Dictionary<ChildClass, ChildGroup> _leaders = [];
    private readonly Dictionary<ChildClass, List<ChildGroup>> _classGroups = [];
    private readonly HashSet<ChildNode> _extras = [];
    /// <summary>By the roots, a peer parent class, or a string naming groups of one (value equality).</summary>
    private readonly Dictionary<object, Siblings> _siblings = [];
    private int _groupCount;
    private Dictionary<ChildNode, ChildNode>? _baseOf;
    private Dictionary<ChildNode, string>? _peerIds;
    private readonly Dictionary<ChildNode, (bool Moves, object? Target)> _moves = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ChildNode> _decidingMoves = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, string> _finalPaths = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _decidingPaths = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PeerClass, Dictionary<string, object>> _parts = [];
    private readonly HashSet<PeerClass> _placingParts = [];
    private readonly Dictionary<string, ChildNode> _remoteByUuid = new(StringComparer.Ordinal);
    private Dictionary<string, ChildNode>? _mergedByPath;
    private readonly List<DataSyncFieldOutcome> _fields = [];
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly List<DataSyncPlanWarning> _warnings = [];
    private readonly List<string> _released = [];
    private readonly HashSet<string> _releasedIds = new(StringComparer.Ordinal);
    private readonly List<ChildNode> _created = [];
    private List<Candidate> _candidates = [];
    private List<Candidate> _candidateRoots = [];

    /// <summary>The local classes and overlay children under one parent, by key, for claims by key.</summary>
    private sealed record Siblings(Dictionary<string, List<ChildClass>> Classes, Dictionary<string, ChildNode> Overlay);

    /// <summary>What a deletion takes: one class's members (with their subtrees), under the peer id of its item.</summary>
    private sealed record Candidate(IReadOnlyList<ChildNode> Members, string PeerId, ChildNode? BaseRep)
    {
        public ChildNode Rep => Members[0];
    }

    /// <param name="localRoots">ReadLocal content's list: every local child, in local order. Merged in place.</param>
    /// <param name="publishedRoots">The same list as this device publishes it (§3.5): what is visible to merging.</param>
    /// <param name="remoteRoots">The peer's validated list.</param>
    /// <param name="baseRoots">The base's list (ThreeWay; FastForward only to tell a re-added class), else null.</param>
    public ChildMerge3(ChildMergeSettings settings, List<ChildNode> localRoots, IReadOnlyList<ChildNode> publishedRoots,
        List<ChildNode> remoteRoots, List<ChildNode>? baseRoots)
    {
        _s = settings;
        _prefix = settings.Kind switch
        {
            ChildListKind.Choices => "choice",
            ChildListKind.Tags => "tag",
            _ => "node",
        };
        _localRoots = localRoots;
        ChildNode.PreOrder(localRoots, n =>
        {
            n.Seq = _local.Count;
            n.OriginalParent = n.Parent;
            _local.Add(n);
            if (n.Uuid is not { } uuid) return;
            _localByUuid.TryAdd(uuid, n);
            _taken.Add(uuid);
        });

        var hidden = settings.Overlay.HiddenChildIds.ToHashSet(StringComparer.Ordinal);
        var held = settings.Overlay.HeldChildren.Select(h => h.ChildId).ToHashSet(StringComparer.Ordinal);
        foreach (var node in _local)
        {
            if (node.Uuid is not { } uuid || !hidden.Contains(uuid)) continue;
            node.Overlay = true;
            node.Held = held.Contains(uuid);
        }

        Align(localRoots, publishedRoots);
        _localRootClasses = Classes(localRoots, n => n.Visible);
        foreach (var cls in _localRootClasses.SelectMany(c => c.SelfAndDescendants()))
        {
            _localClasses.Add(cls);
            foreach (var member in cls.Members) member.Class = cls;
        }

        var remoteSeq = 0;
        ChildNode.PreOrder(remoteRoots, n => n.Seq = remoteSeq++);
        foreach (var cls in Classes(remoteRoots, n => n.Uuid is not null)) AddPeer(cls, null);

        if (baseRoots is not null)
        {
            _hasBase = true;
            foreach (var cls in Classes(baseRoots, n => n.Uuid is not null).SelectMany(c => c.SelfAndDescendants()))
            {
                _baseClasses.Add(cls);
                foreach (var member in cls.Members)
                {
                    _baseByMember.TryAdd(member.Uuid!, cls);
                    _baseNodeByUuid.TryAdd(member.Uuid!, member);
                }
            }
        }

        if (settings.Rules == ChildMergeRules.ThreeWay)
        {
            foreach (var peer in _peers)
            {
                peer.BaseNode = BaseNodeOf(peer.Class.Members);
                peer.Base = peer.BaseNode is null ? null : _baseByMember[peer.BaseNode.Uuid!];
            }
        }
    }

    // ---- the plan ---------------------------------------------------------------------------

    /// <summary>
    /// Maps, decides and applies everything but deletions; returns every local id the deletion candidates would take
    /// (members and their subtrees): the ids whose usage <see cref="Finish"/> needs.
    /// </summary>
    public IReadOnlyList<string> Plan()
    {
        ClaimById();
        ClaimByKey();
        ChooseLeaders();
        SettleUnclaimed();
        DecideKeysAndColors();
        if (_s.Kind == ChildListKind.Nodes) DecideParents();
        Realize();
        FindCandidates();

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in _candidateRoots)
        {
            foreach (var member in candidate.Members)
            {
                foreach (var node in member.SelfAndDescendants())
                {
                    if (node.Uuid is { } uuid && seen.Add(uuid)) ids.Add(uuid);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Deletions by usage (§8.5.4 steps 3, 5 and 7), then colours onto the classes as they end, and the outputs.
    /// </summary>
    public ChildMergeOutcome Finish(IReadOnlyDictionary<string, int> usage)
    {
        var removed = new List<string>();
        var held = new List<string>();
        if (_candidateRoots.Count > 0)
        {
            var count = _candidates.Count;
            var total = _localClasses.Count;
            var policy = _s.Policy;
            var tripped = count > policy.MaxChildDeletionsPerEntity ||
                          (total >= policy.MinChildrenForRatio && count > policy.MaxChildDeletionRatio * total);
            if (tripped && _s.Deletions == DataSyncChildDeletionMode.Normal)
            {
                return new ChildMergeOutcome([], [], new Dictionary<string, string>(_s.ChildMap), [], [], [], [],
                    _candidates.Select(c => c.Rep.Uuid!).ToArray(), []);
            }

            foreach (var candidate in _candidateRoots)
            {
                var path = KeyPath(candidate.PeerId);
                var baseDisplay = candidate.BaseRep is null ? null : Display(candidate.BaseRep);
                var display = Display(candidate.Rep);
                if (_s.Deletions == DataSyncChildDeletionMode.Restore)
                {
                    AddField(path, DataSyncFieldResolution.KeptLocal, baseDisplay, display, null, display);
                    continue;
                }

                var subtree = candidate.Members.SelectMany(m => m.SelfAndDescendants()).ToList();
                var inUse = subtree.Any(n => n.Overlay || n.Uuid is null ||
                                             !usage.TryGetValue(n.Uuid, out var resources) || resources > 0);
                if (inUse || _s.Deletions == DataSyncChildDeletionMode.ReviewEach)
                {
                    foreach (var member in candidate.Members)
                    {
                        member.HeldHere = true;
                        held.Add(member.Uuid!);
                    }

                    AddField(path, DataSyncFieldResolution.DeletionHeldInUse, baseDisplay, display, null, display);
                }
                else
                {
                    foreach (var member in candidate.Members) Detach(member);
                    foreach (var node in subtree)
                    {
                        node.Removed = true;
                        if (node.Uuid is { } uuid) removed.Add(uuid);
                    }

                    AddField(path, DataSyncFieldResolution.TookRemote, baseDisplay, display, null, null);
                }
            }
        }

        ApplyColors();
        AdoptTwins();
        return new ChildMergeOutcome(_localRoots, _fields, BuildChildMap(), _created.Select(n => n.Uuid!).ToArray(),
            removed, held, _released, [], _warnings);
    }

    // ---- defaultValue (§8.5.4 step 9) --------------------------------------------------------

    /// <summary>
    /// The local option a peer option ends as after the merge (asked once the merge is done): its own counterpart when
    /// that is still its class's, else its class's node where its parent ends (the class's first counterpart, or the
    /// option added or restored for it), or the overlay child it maps to. Null when its class stays deleted here.
    /// </summary>
    public ChildNode? RemoteTarget(string uuid) =>
        _remoteByUuid.TryGetValue(uuid, out var remote) ? MergedPosition(remote) : null;

    /// <summary>
    /// The node a peer node ends at in the merged tree, or null when it is gone here. A member this device deleted,
    /// under a parent class where its class has no node, is where that class ends under its parent, if anything ends
    /// there; else it is gone, and so is everything below it that has no counterpart of its own. Merging the other way it
    /// stays under its parent, in whatever class ends there, or in a class of its own that goes (see PartsOf).
    /// </summary>
    private ChildNode? MergedPosition(ChildNode remote)
    {
        var peer = _peerByMember[remote.Uuid!];
        if (peer.Status == PeerClassStatus.Invisible) return peer.InvisibleTarget;
        if (peer.Status is not (PeerClassStatus.Present or PeerClassStatus.Add or PeerClassStatus.Restore)) return null;
        if (peer.Status == PeerClassStatus.Present && TargetOf(remote.Uuid!) is { Removed: false } own &&
            own.Owner == peer) return own;
        ChildNode? parent = null;
        if (remote.Parent is not null && (parent = MergedPosition(remote.Parent)) is null) return null;
        if (peer.Status == PeerClassStatus.Present && PartsOf(peer) is { } parts &&
            !parts.ContainsKey(ParentPathOf(remote)))
        {
            var path = (parent is null ? "" : PathInMergedTree(parent)) + "\u0001" + peer.Key;
            return MergedByPath().GetValueOrDefault(path);
        }

        return NodeAt(PositionOfRemoteNode(remote));
    }

    /// <summary>The first published node of each class of the merged tree, by class key path.</summary>
    private Dictionary<string, ChildNode> MergedByPath()
    {
        if (_mergedByPath is not null) return _mergedByPath;
        _mergedByPath = new Dictionary<string, ChildNode>(StringComparer.Ordinal);
        ChildNode.PreOrder(_localRoots, n =>
        {
            if (Token(n) is not null) _mergedByPath.TryAdd(PathInMergedTree(n), n);
        });
        return _mergedByPath;
    }

    private string PathInMergedTree(ChildNode node)
    {
        var keys = new List<string>();
        for (var n = node; n is not null; n = n.Parent) keys.Add(KeyOf(n));
        keys.Reverse();
        return "\u0001" + string.Join("\u0001", keys);
    }

    /// <summary>A local option this device publishes (removed by this merge or not); null for one it does not.</summary>
    public ChildNode? LocalTarget(string uuid) =>
        _localByUuid.TryGetValue(uuid, out var node) && node.Visible ? node : null;

    /// <summary>A base option as it ends here: as the peer still has it, else its local counterpart.</summary>
    public ChildNode? BaseTarget(string uuid) =>
        _peerByMember.ContainsKey(uuid) ? RemoteTarget(uuid)
        : TargetOf(uuid) is { Visible: true } target ? target
        : null;

    /// <summary>
    /// The class an option ends in, as a token: its class key path in the merged tree, which is what the comparison form
    /// names. Two sides that name options ending in one class name the same default. Null when the option is gone or
    /// no longer published after the merge (removed, held, an overlay child): it names nothing a reader sees.
    /// </summary>
    public string? Token(ChildNode? node)
    {
        if (node is null || node.Removed || !(node.Visible || node.CreatedFor is not null)) return null;
        for (var n = node; n is not null; n = n.Parent)
        {
            if (n.HeldHere) return null;
        }

        var keys = new List<string>();
        for (var n = node; n is not null; n = n.Parent) keys.Add(KeyOf(n));
        keys.Reverse();
        return "k:" + string.Join("\u0001", keys);
    }

    /// <summary>True when this merge removed the local option with <paramref name="uuid"/>.</summary>
    public bool IsRemoved(string uuid) => _localByUuid.TryGetValue(uuid, out var node) && node.Removed;

    // ---- mapping ----------------------------------------------------------------------------

    private void ClaimById()
    {
        foreach (var peer in _peers)
        {
            var targetOwned = false;
            foreach (var member in peer.Class.Members)
            {
                var target = TargetOf(member.Uuid!);
                if (target is null) continue;
                if (!target.Visible)
                {
                    // An overlay child, or a node below one, is mapped and left alone. Any other node this device does
                    // not publish (below a parent without an id, dropped by the reader) is no counterpart: the class is
                    // matched by key or added, so what this device publishes has it.
                    if (IsOverlaid(target)) peer.InvisibleTarget ??= target;
                    continue;
                }

                if (target.Owner is null)
                {
                    target.Owner = peer;
                    var group = GroupOf(peer, target.Class!, member, BaseNodeOf([member]), target);
                    group.Owned.Add(target);
                    target.OwnerGroup = group;
                }
                else if (target.Owner != peer)
                {
                    targetOwned = true;
                }
            }

            if (peer.Groups.Count > 0) peer.Status = PeerClassStatus.Present;
            else if (peer.InvisibleTarget is not null) peer.Status = PeerClassStatus.Invisible;
            else peer.Split = targetOwned;
        }
    }

    private void ClaimByKey()
    {
        foreach (var peer in _peers)
        {
            if (peer.Status != PeerClassStatus.Unset || peer.Split) continue;
            Siblings siblings;
            if (peer.Parent is null)
            {
                siblings = SiblingsOf(Root, _localRootClasses, _localRoots);
            }
            else if (peer.Parent.Status == PeerClassStatus.Present)
            {
                siblings = SiblingsUnder(peer);
            }
            else
            {
                continue;
            }

            var match = siblings.Classes.TryGetValue(peer.Key, out var sameKey)
                ? sameKey.FirstOrDefault(c => c.Members.All(m => m.Owner is null))
                : null;
            if (match is not null)
            {
                var group = GroupOf(peer, match, peer.Rep, peer.BaseNode);
                group.ByKey = true;
                foreach (var member in match.Members)
                {
                    member.Owner = peer;
                    member.OwnerGroup = group;
                    group.Owned.Add(member);
                }

                peer.Status = PeerClassStatus.Present;
                continue;
            }

            // The key is an overlay child's: it is mapped and left alone, never added beside it.
            if (!siblings.Overlay.TryGetValue(peer.Key, out var overlay)) continue;
            peer.InvisibleTarget = overlay;
            peer.Status = PeerClassStatus.Invisible;
        }
    }

    /// <summary>
    /// The local classes and overlay children a peer class is looked for among by key: those under its parent's
    /// counterparts. Outside a FastForward a multilevel parent class this merge splits keeps its parts apart (see
    /// <see cref="PartsOf"/>), and a class under one part is not the class under another: only the children of the
    /// nodes that end in one class with the parents its own members have here — the groups of those parents (by id;
    /// all of the parent's groups when none is linked), their nodes whose key ends the same, and the free members of
    /// their local classes, never another peer class's nodes.
    /// </summary>
    private Siblings SiblingsUnder(PeerClass peer)
    {
        var parent = peer.Parent!;
        var parents = parent.Groups.Select(g => g.Local).Distinct().ToArray();
        if (_s.Kind != ChildListKind.Nodes || _s.Rules == ChildMergeRules.FastForward)
        {
            return SiblingsOf(parent, parents.SelectMany(c => c.Children),
                parents.SelectMany(c => c.Members).SelectMany(m => m.Children));
        }

        var linked = peer.Class.Members
            .Select(m => TargetOf(m.Parent!.Uuid!))
            .Where(t => t is { OwnerGroup: not null } && t.Owner == parent)
            .Select(t => t!.OwnerGroup!)
            .Distinct()
            .OrderBy(g => g.Index)
            .ToArray();
        var groups = linked.Length > 0 ? linked : parent.Groups.OrderBy(g => g.Index).ToArray();
        var cacheKey = parent.PeerId + "\u0001" + string.Join(",", groups.Select(g => g.Index));
        if (_siblings.TryGetValue(cacheKey, out var cached)) return cached;
        var keys = groups.Select(FinalKeyOfGroup).ToHashSet(StringComparer.Ordinal);
        var nodes = groups.Select(g => g.Local).Distinct().SelectMany(c => c.Members)
            .Where(m => m.Owner is null || (m.Owner == parent && keys.Contains(FinalKeyOfGroup(m.OwnerGroup!))))
            .ToArray();
        var children = nodes.SelectMany(n => n.Children).ToArray();
        return SiblingsOf(cacheKey, children.Select(n => n.Class).OfType<ChildClass>().Distinct(), children);
    }

    /// <summary>The key a group's local class ends with: the peer's, when the group takes it (§8.5.2).</summary>
    private string FinalKeyOfGroup(ChildGroup group) =>
        group.Local.Key == group.Peer.Key || !TakesRemote(KeyResolution(group)) ? group.Local.Key : group.Peer.Key;

    private DataSyncFieldResolution KeyResolution(ChildGroup group)
    {
        var localBase = LocalBaseClassOf(group);
        return Resolve(localBase?.Key, group.Base?.Key, group.Local.Key, group.Peer.Key, localBase is not null,
            group.Base is not null);
    }

    /// <summary>The local classes and overlay children under the counterpart of one peer parent (or the roots).</summary>
    private Siblings SiblingsOf(object parent, IEnumerable<ChildClass> classes, IEnumerable<ChildNode> nodes)
    {
        if (_siblings.TryGetValue(parent, out var siblings)) return siblings;
        var byKey = new Dictionary<string, List<ChildClass>>(StringComparer.Ordinal);
        foreach (var cls in classes)
        {
            if (!byKey.TryGetValue(cls.Key, out var list)) byKey[cls.Key] = list = [];
            list.Add(cls);
        }

        var overlay = new Dictionary<string, ChildNode>(StringComparer.Ordinal);
        foreach (var node in nodes.Where(n => n.Overlay)) overlay.TryAdd(KeyOf(node), node);
        return _siblings[parent] = new Siblings(byKey, overlay);
    }

    private void SettleUnclaimed()
    {
        foreach (var peer in _peers)
        {
            if (peer.Status is PeerClassStatus.Present or PeerClassStatus.Invisible) continue;
            if (peer.Parent is { Status: PeerClassStatus.Invisible })
            {
                peer.Status = PeerClassStatus.Invisible;
                continue;
            }

            peer.Status = _s.Rules == ChildMergeRules.ThreeWay && !peer.Split && peer.Base is not null
                ? ChangedSinceBase(peer) ? PeerClassStatus.Restore : PeerClassStatus.StaysDeleted
                : PeerClassStatus.Add;
        }

        // What comes back needs its parent back.
        for (var i = _peers.Count - 1; i >= 0; i--)
        {
            if (_peers[i].Status is PeerClassStatus.Add or PeerClassStatus.Restore) RestoreAncestors(_peers[i].Parent);
        }

        // The one release (§8.5.4 step 0): a held child whose class the peer re-added since the base.
        foreach (var peer in _peers)
        {
            if (peer is not { Status: PeerClassStatus.Invisible, InvisibleTarget: { Held: true } target }) continue;
            var reAdded = _hasBase
                ? peer.Class.Members.All(m => !_baseByMember.ContainsKey(m.Uuid!))
                : _s.Rules == ChildMergeRules.FastForward;
            if (reAdded && _releasedIds.Add(target.Uuid!)) _released.Add(target.Uuid!);
        }
    }

    private static void RestoreAncestors(PeerClass? peer)
    {
        for (; peer is { Status: PeerClassStatus.StaysDeleted }; peer = peer.Parent) peer.Status = PeerClassStatus.Restore;
    }

    /// <summary>
    /// A peer class changed since the base unless one of its members still sits in the base class it came from as the
    /// comparison form sees it — the same key, the class's colour — under the same parent node.
    /// </summary>
    private bool ChangedSinceBase(PeerClass peer) => !peer.Class.Members.Any(member =>
    {
        if (!_baseNodeByUuid.TryGetValue(member.Uuid!, out var baseNode)) return false;
        var baseClass = _baseByMember[member.Uuid!];
        return baseClass.Key == peer.Key && NormColor(baseClass.Rep.Color) == NormColor(peer.Rep.Color) &&
               (_s.Kind != ChildListKind.Nodes || (member.Parent?.Uuid ?? "") == (baseNode.Parent?.Uuid ?? ""));
    });

    private void ChooseLeaders()
    {
        foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
        {
            foreach (var group in peer.Groups) group.Owned.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            peer.Groups.Sort((a, b) => a.FirstSeq.CompareTo(b.FirstSeq));
            foreach (var group in peer.Groups)
            {
                if (!_leaders.TryGetValue(group.Local, out var leader) || group.FirstSeq < leader.FirstSeq)
                    _leaders[group.Local] = group;
                if (!_classGroups.TryGetValue(group.Local, out var groups)) _classGroups[group.Local] = groups = [];
                groups.Add(group);
            }
        }

        foreach (var groups in _classGroups.Values) groups.Sort((a, b) => a.FirstSeq.CompareTo(b.FirstSeq));
    }

    // ---- decisions --------------------------------------------------------------------------

    private void DecideKeysAndColors()
    {
        foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
        {
            foreach (var group in peer.Groups)
            {
                if (group.Local.Key == peer.Key) continue;
                var resolution = KeyResolution(group);
                group.TakeRemoteKey = TakesRemote(resolution);
                var local = group.Local.Rep;
                AddField(KeyPath(peer.PeerId), resolution, group.Base is null ? null : Display(group.Base.Rep),
                    Display(local), Display(peer.Rep),
                    group.TakeRemoteKey ? Display(local, peer.Rep.Label, peer.Rep.Group) : Display(local));
            }

            // §8.5.5: an appearance field, never an item. Decided per local class, like the key: a peer class whose
            // members are split over two classes here gives each its own three-way.
            foreach (var group in peer.Groups)
            {
                var l = NormColor(group.Local.Rep.Color);
                var r = NormColor(peer.Rep.Color);
                group.Color = l;
                if (l == r) continue;
                var b = NormColor(group.Base?.Rep.Color);
                var localBase = LocalBaseClassOf(group);
                var takeRemote = _s.Rules switch
                {
                    ChildMergeRules.FastForward => true,
                    ChildMergeRules.ThreeWay when localBase is not null || group.Base is not null => TakesRemote(Decide(
                        localBase is null || l != NormColor(localBase.Rep.Color), group.Base is null || r != b,
                        _s.AppearanceWinner == DataSyncMergeSide.Remote
                            ? DataSyncFieldResolution.TookRemote
                            : DataSyncFieldResolution.KeptLocal)),
                    // Without a base a cleared colour cannot be told from one never set: the side that has one gives it.
                    _ => l is null || (r is not null && _s.AppearanceWinner == DataSyncMergeSide.Remote),
                };
                group.Color = takeRemote ? r : l;
                var label = group.Local.Rep.Label;
                AddField($"{KeyPath(peer.PeerId)}:color",
                    takeRemote ? DataSyncFieldResolution.AppearanceTookRemote : DataSyncFieldResolution.AppearanceKeptLocal,
                    group.Base is null ? null : new DataSyncDisplayValue(group.Base.Rep.Label, b),
                    new DataSyncDisplayValue(label, l), new DataSyncDisplayValue(peer.Rep.Label, r),
                    new DataSyncDisplayValue(label, group.Color));
            }
        }
    }

    /// <summary>
    /// A node's parent merges as a scalar (§8.5.4 step 1), per group, judged at the group's anchor. Where the parent is
    /// linked by id on all three sides, node by node: a move between two members of one class, which the form does not
    /// show, is still carried, since another change may split that class; kept within its class, it is no outcome. Else
    /// over logical classes: the peer class that owns (or leads) the parent node here; in the base, the peer class that
    /// still has the base parent, else that parent's counterpart here.
    /// </summary>
    private void DecideParents()
    {
        var peerIds = PeerIds;
        foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
        {
            var r = (object?)peer.Parent ?? Root;
            foreach (var group in peer.Groups)
            {
                var anchor = group.Anchor = AnchorOf(group);
                var l = OriginalParentOf(anchor);
                group.LocalParent = l;
                group.FinalParent = l;
                var sameClass = ReferenceEquals(l, r);

                // Node by node where all three sides are linked by id: a move between two members of one class, which
                // the form does not show, is still carried (another change may split that class).
                var localId = anchor.OriginalParent is null ? "" : peerIds.GetValueOrDefault(anchor.OriginalParent);
                var remoteId = group.RemoteMember.Parent?.Uuid ?? "";
                var baseId = group.BaseNode is null ? null : group.BaseNode.Parent?.Uuid ?? "";
                DataSyncFieldResolution resolution;
                if (!group.ByKey && localId is not null && localId == remoteId)
                {
                    continue;
                }

                if (!group.ByKey && localId is not null &&
                    (_s.Rules == ChildMergeRules.FastForward || (_s.Rules == ChildMergeRules.ThreeWay && baseId is not null)))
                {
                    resolution = _s.Rules == ChildMergeRules.FastForward || localId == baseId
                        ? DataSyncFieldResolution.TookRemote
                        : remoteId == baseId || sameClass
                            ? DataSyncFieldResolution.KeptLocal
                            : Concurrent();
                }
                else if (sameClass)
                {
                    continue;
                }
                else if (_s.Rules == ChildMergeRules.FastForward)
                {
                    resolution = DataSyncFieldResolution.TookRemote;
                }
                else if (_s.Rules == ChildMergeRules.ThreeWay && (group.BaseNode is not null || LocalBaseNodeOf(group) is not null))
                {
                    var localBase = LocalBaseNodeOf(group);
                    resolution = Decide(localBase is null || !ReferenceEquals(l, BaseParentOf(localBase)),
                        group.BaseNode is null || !ReferenceEquals(r, BaseParentOf(group.BaseNode)),
                        Concurrent());
                }
                else
                {
                    resolution = Concurrent();
                }

                // A node is never moved under an overlay child.
                if (TakesRemote(resolution) && peer.Parent is { Status: PeerClassStatus.Invisible })
                    resolution = DataSyncFieldResolution.KeptLocal;
                // Kept within the class it had: nothing the form shows, nothing to say.
                if (sameClass && !TakesRemote(resolution)) continue;
                group.ParentResolution = resolution;
                group.Moves = TakesRemote(resolution);
                if (group.Moves) group.FinalParent = r;
            }
        }

        BreakCycles();

        foreach (var group in _peers.SelectMany(p => p.Groups).Where(g => g.Moves)) RestoreAncestors(group.Peer.Parent);

        // One outcome per class path: the representative's group's (else the first decided group's). A group whose parent
        // went another way (its members hang under other parents) reports under the id of the peer member that linked
        // it, so neither a move nor a conflict is hidden behind the other; that path is as stable across pulls.
        foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
        {
            var decided = peer.Groups.Where(g => g.ParentResolution is not null).ToArray();
            if (decided.Length == 0) continue;
            var classResolution = (decided.FirstOrDefault(g => g.RemoteMember.Uuid == peer.PeerId) ?? decided[0])
                .ParentResolution!.Value;
            var remoteDisplay = peer.Parent is null ? RootDisplay : Display(peer.Parent.Rep, withColor: false);
            foreach (var group in decided)
            {
                var local = group.Anchor!.OriginalParent;
                var localDisplay = local is null ? RootDisplay : Display(local, withColor: false);
                var baseParent = group.BaseNode?.Parent;
                var baseDisplay = group.BaseNode is null ? null
                    : baseParent is null ? RootDisplay
                    : Display(baseParent, withColor: false);
                var resolution = group.ParentResolution!.Value;
                var id = resolution == classResolution ? peer.PeerId : group.RemoteMember.Uuid!;
                AddField($"{KeyPath(id)}:parent", resolution, baseDisplay, localDisplay, remoteDisplay,
                    group.Moves ? remoteDisplay : localDisplay);
            }
        }
    }

    /// <summary>
    /// Moves whose merged parents would form a cycle keep their local parents: every move on the cycle becomes a
    /// conflict. The cycle is looked for in the final structure — moved nodes under their targets, added and restored
    /// classes under their peer parents, everything else where it was — trying moves in the order of peer ids.
    /// </summary>
    private void BreakCycles()
    {
        var moving = _peers.SelectMany(p => p.Groups).Where(g => g.Moves)
            .OrderBy(g => g.Peer.PeerId, StringComparer.Ordinal).ThenBy(g => g.FirstSeq).ToArray();
        bool broke;
        do
        {
            ForgetFinalPositions();
            broke = false;
            foreach (var group in moving)
            {
                if (!group.Moves) continue;
                foreach (var start in Movers(group))
                {
                    var path = new List<object> { start };
                    var seen = new HashSet<object>(ReferenceEqualityComparer.Instance) { start };
                    var x = FinalParentPosition(start);
                    while (x is not null && seen.Add(x))
                    {
                        path.Add(x);
                        x = FinalParentPosition(x);
                    }

                    if (!ReferenceEquals(x, start)) continue;
                    foreach (var node in path.OfType<ChildNode>())
                    {
                        if (MoverGroupOf(node) is not { Moves: true } moved) continue;
                        moved.Moves = false;
                        moved.ParentResolution = DataSyncFieldResolution.Conflict;
                        moved.FinalParent = moved.LocalParent;
                        broke = true;
                    }

                    if (broke) break;
                }
            }
        } while (broke);
    }

    /// <summary>
    /// Where a node (or a class still to be created, standing for its node) ends up: the position of its parent, a node
    /// or a class still to be created, or null for the roots.
    /// </summary>
    private object? FinalParentPosition(object position) => position switch
    {
        PeerClass created => created.Rep.Parent is null ? null : PositionOfRemoteNode(created.Rep.Parent),
        SplitPart part => part.Member.Parent is null ? null : PositionOfRemoteNode(part.Member.Parent),
        ChildNode node => TryMoveTarget(node, out var target) ? target : node.OriginalParent,
        _ => null,
    };

    /// <summary>
    /// Whether a node moves, and where to (§8.5.4 step 8: only a node whose parent took the peer's value moves). A
    /// moving group's anchor goes under the counterpart of the peer's own parent node; its other members, and the free
    /// members that follow it along (<see cref="FollowedGroup"/>), go there too unless the parent they have here ends
    /// in that same class. A group that does not move moves nothing, except in a FastForward, where a class is taken
    /// along as a whole: a member whose parent ends in another class than the anchor's (a peer rename or move split the
    /// class it was in) joins the anchor's parent, unless the group's parent is a conflict.
    /// </summary>
    private bool TryMoveTarget(ChildNode node, out object? position)
    {
        position = null;
        if (_s.Kind != ChildListKind.Nodes || FollowedGroup(node) is not ({ FinalParent: not null } group, true))
            return false;
        if (_moves.TryGetValue(node, out var known))
        {
            position = known.Target;
            return known.Moves;
        }

        // Asked again while deciding itself: only a structure with a cycle still to break does that; it stays.
        if (!_decidingMoves.Add(node)) return false;
        try
        {
            var moves = DecideMove(node, group, out position);
            if (!moves) position = null;
            _moves[node] = (moves, position);
            return moves;
        }
        finally
        {
            _decidingMoves.Remove(node);
        }
    }

    private bool DecideMove(ChildNode node, ChildGroup group, out object? target)
    {
        if (group.Moves)
        {
            target = group.RemoteMember.Parent is null ? null : PositionOfRemoteNode(group.RemoteMember.Parent);
            if (ReferenceEquals(target, node.OriginalParent)) return false;
            // The anchor is carried node by node: a move between two members of one class is still a move.
            if (ReferenceEquals(node, group.Anchor)) return true;
        }
        else
        {
            target = group.Anchor!.OriginalParent;
            if (_s.Rules != ChildMergeRules.FastForward || ReferenceEquals(target, node.OriginalParent) ||
                group.ParentResolution == DataSyncFieldResolution.Conflict) return false;
        }

        return FinalPathOf(node.OriginalParent) != FinalPathOf(target);
    }

    /// <summary>
    /// The class key path a position ends at after this merge's keys and moves: a local node, a class still to be
    /// created, or null for the roots. Two positions with one path are one class in the merged tree.
    /// </summary>
    private string FinalPathOf(object? position)
    {
        if (position is null) return "";
        if (_finalPaths.TryGetValue(position, out var path)) return path;
        // Asked again while computing itself: a cycle still to break; any answer will be recomputed once it is.
        if (!_decidingPaths.Add(position)) return "\u0002";
        try
        {
            path = position switch
            {
                PeerClass created => ParentPathOf(created.Rep) + "\u0001" + created.Key,
                // A present class's part takes its leading group's key (see CreatePart).
                SplitPart part => part.ParentPath + "\u0001" +
                                  (part.Leader is { } leader ? FinalKeyOf(leader.Anchor!) : part.Peer.Key),
                ChildNode { Part: { } part } => FinalPathOf(part),
                ChildNode { CreatedFor: { } created } => FinalPathOf(created),
                ChildNode node => FinalPathOf(TryMoveTarget(node, out var target) ? target : node.OriginalParent) +
                                  "\u0001" + FinalKeyOf(node),
                _ => "",
            };
            _finalPaths[position] = path;
            return path;
        }
        finally
        {
            _decidingPaths.Remove(position);
        }
    }

    /// <summary>The class key a local node ends with: the peer's, when the group it follows takes the peer's
    /// key.</summary>
    private string FinalKeyOf(ChildNode node) =>
        MoverGroupOf(node) is { TakeRemoteKey: true } group ? group.Peer.Key : KeyOf(node);

    /// <summary>Forgets every decided move, path and part: the groups' moves changed.</summary>
    private void ForgetFinalPositions()
    {
        _moves.Clear();
        _finalPaths.Clear();
        _parts.Clear();
    }

    /// <summary>
    /// The group's anchor: the local node its linking member maps to, whose parent the group's parent is judged by
    /// (its first member for a group claimed by key).
    /// </summary>
    private ChildNode AnchorOf(ChildGroup group) =>
        !group.ByKey && TargetOf(group.RemoteMember.Uuid!) is { } target && target.OwnerGroup == group
            ? target
            : group.Owned[0];

    /// <summary>Local nodes by the peer id they carry here: a member of R, or of the base, that maps to them.</summary>
    private Dictionary<ChildNode, string> PeerIds => _peerIds ??= PeerIdentities();

    private Dictionary<ChildNode, string> PeerIdentities()
    {
        var ids = new Dictionary<ChildNode, string>();
        foreach (var uuid in _peerByMember.Keys.Concat(_baseNodeByUuid.Keys))
        {
            if (TargetOf(uuid) is { } target) ids.TryAdd(target, uuid);
        }

        return ids;
    }

    /// <summary>
    /// The position a peer node has here: its own counterpart while that is still its class's; else, for a multilevel
    /// class this merge splits, the part its parent ends in (<see cref="PartsOf"/>); else its class's first counterpart,
    /// the overlay child it maps to, or its class still to be created (standing for its node).
    /// </summary>
    private object? PositionOfRemoteNode(ChildNode remoteNode)
    {
        var peer = _peerByMember[remoteNode.Uuid!];
        return peer.Status switch
        {
            PeerClassStatus.Present => TargetOf(remoteNode.Uuid!) is { Removed: false } target && target.Owner == peer
                ? target
                : PartOf(peer, remoteNode) ?? peer.PrimaryNode,
            PeerClassStatus.Invisible => peer.InvisibleTarget,
            PeerClassStatus.Add or PeerClassStatus.Restore => PartOf(peer, remoteNode) ?? (object?)peer.Created ?? peer,
            _ => (object?)peer.Created ?? peer,
        };
    }

    private static ChildNode? NodeAt(object? position) => position switch
    {
        ChildNode node => node,
        PeerClass peer => peer.Created,
        SplitPart part => part.Created,
        _ => null,
    };

    /// <summary>The part of its class a peer node without a counterpart of its own belongs to, or null for the class's
    /// usual place.</summary>
    private object? PartOf(PeerClass peer, ChildNode remoteNode) =>
        PartsOf(peer) is { } parts && parts.TryGetValue(ParentPathOf(remoteNode), out var part) ? part : null;

    /// <summary>
    /// A multilevel peer class whose members' parents end in several classes here — this merge split the class they are
    /// in: a rename or a move of one member, or two classes here that stay apart — is placed part by part, by the class
    /// each member's parent ends in, as merging the other way places those members (a node stays with its parent,
    /// §8.5.4 step 8). A part goes to the class's node already under that parent class, whose decisions it shares; a
    /// part without one is added there: every part of a class added or restored here, and of a class this device has,
    /// the members the peer added since the base or changed since (edit wins). Other members, which this device deleted,
    /// stay deleted. Keyed by the class key path each part's parent ends at; null in a FastForward, where every member
    /// takes the peer's place and nothing is split.
    /// </summary>
    private Dictionary<string, object>? PartsOf(PeerClass peer)
    {
        if (_s.Kind != ChildListKind.Nodes || _s.Rules == ChildMergeRules.FastForward ||
            peer.Status is not (PeerClassStatus.Present or PeerClassStatus.Add or PeerClassStatus.Restore)) return null;
        if (_parts.TryGetValue(peer, out var parts)) return parts;
        // Asked again while placing itself: a structure still being settled; the class keeps its usual place.
        if (!_placingParts.Add(peer)) return null;
        try
        {
            parts = new Dictionary<string, object>(StringComparer.Ordinal);
            if (peer.Status != PeerClassStatus.Present)
            {
                parts[ParentPathOf(peer.Rep)] = peer;
                foreach (var member in peer.Class.Members)
                {
                    var path = ParentPathOf(member);
                    if (!parts.ContainsKey(path)) parts[path] = new SplitPart(peer, member, path);
                }

                return _parts[peer] = parts;
            }

            // The class's groups in the peer's order — its members' order there, which is how merging the other way
            // orders them: the first one under a parent class gives that part its node, the first of all leads.
            var groups = GroupsInPeerOrder(peer);
            foreach (var node in groups.SelectMany(g => g.Owned)) parts.TryAdd(FinalParentPathOf(node), node);
            var leader = groups[0];
            foreach (var member in peer.Class.Members)
            {
                if (TargetOf(member.Uuid!) is { Removed: false } target && target.Owner == peer) continue;
                var path = ParentPathOf(member);
                if (parts.ContainsKey(path)) continue;
                // No node of the class under that parent class. Merging the other way, the member follows the leading
                // group: along with a move this device made, else under its own parent with the group's key and colour.
                if (leader.ParentResolution == DataSyncFieldResolution.KeptLocal) parts[path] = leader.Anchor!;
                else if (AddedOrChangedThere(member) || HoldsAnAdd(member))
                    parts[path] = new SplitPart(peer, member, path) { Leader = leader };
            }

            return _parts[peer] = parts;
        }
        finally
        {
            _placingParts.Remove(peer);
        }
    }

    /// <summary>A present class's groups by the first of its members (in the peer's order) that claimed each by id, then
    /// in local order.</summary>
    private List<ChildGroup> GroupsInPeerOrder(PeerClass peer)
    {
        var first = new Dictionary<ChildGroup, int>();
        for (var i = 0; i < peer.Class.Members.Count; i++)
        {
            if (TargetOf(peer.Class.Members[i].Uuid!) is { Owner: var owner, OwnerGroup: { } group } && owner == peer)
                first.TryAdd(group, i);
        }

        return peer.Groups.OrderBy(g => first.GetValueOrDefault(g, -1)).ThenBy(g => g.FirstSeq).ToList();
    }

    /// <summary>
    /// A peer node without a counterpart here that something added or restored here goes under: an option of a class
    /// added or restored, or one the peer added or changed, below it without a counterpart of its own between. Merging
    /// the other way that option keeps this node, its parent, alive.
    /// </summary>
    private bool HoldsAnAdd(ChildNode remoteNode)
    {
        foreach (var child in remoteNode.Children)
        {
            var peer = _peerByMember[child.Uuid!];
            if (peer.Status is PeerClassStatus.Add or PeerClassStatus.Restore) return true;
            if (peer.Status != PeerClassStatus.Present) continue;
            if (TargetOf(child.Uuid!) is { } own && own.Owner == peer)
            {
                // A node of this device that moves under it (it took the peer's parent) needs it too.
                if (own.OwnerGroup is { Moves: true }) return true;
                continue;
            }

            if (AddedOrChangedThere(child) || HoldsAnAdd(child)) return true;
        }

        return false;
    }

    /// <summary>A peer node not in the base, or changed there since: key, colour or parent.</summary>
    private bool AddedOrChangedThere(ChildNode remoteNode) =>
        !_baseNodeByUuid.TryGetValue(remoteNode.Uuid!, out var baseNode) || KeyOf(remoteNode) != KeyOf(baseNode) ||
        NormColor(remoteNode.Color) != NormColor(baseNode.Color) ||
        (remoteNode.Parent?.Uuid ?? "") != (baseNode.Parent?.Uuid ?? "");

    /// <summary>The class a peer node's parent ends in here.</summary>
    private string ParentPathOf(ChildNode remoteNode) =>
        FinalPathOf(remoteNode.Parent is null ? null : PositionOfRemoteNode(remoteNode.Parent));

    /// <summary>The class a local node's parent ends in, after its move.</summary>
    private string FinalParentPathOf(ChildNode node) =>
        FinalPathOf(TryMoveTarget(node, out var target) ? target : node.OriginalParent);

    /// <summary>The group whose decisions — key, colour, parent — a node follows (see
    /// <see cref="FollowedGroup"/>).</summary>
    private ChildGroup? MoverGroupOf(ChildNode node) => FollowedGroup(node).Group;

    /// <summary>
    /// The group whose decisions a node follows, and whether it follows that group's moves too: its own; for a free
    /// member — a node the peer does not have — the group leading its class, which it follows wherever it goes. A
    /// multilevel class this merge splits keeps its parts apart outside a FastForward: a free member then follows the
    /// first group of its class whose anchor ends under the same parent class, and stays with its parent (§8.5.4
    /// step 8), as merging the other way places it (<see cref="PartsOf"/>). With no group there it follows the leading
    /// one.
    /// </summary>
    private (ChildGroup? Group, bool Along) FollowedGroup(ChildNode node)
    {
        if (node.OwnerGroup is { } own) return (own, true);
        if (node.Class is not { } cls || !_leaders.TryGetValue(cls, out var leader)) return (null, false);
        if (_s.Rules == ChildMergeRules.FastForward || leader.Anchor is null) return (leader, true);
        var parent = FinalPathOf(node.OriginalParent);
        var beside = _classGroups[cls].FirstOrDefault(g => g.Anchor is not null && FinalParentPathOf(g.Anchor) == parent);
        return beside is null ? (leader, true) : (beside, false);
    }

    /// <summary>The free members of a local class that follow <paramref name="group"/> (see
    /// <see cref="MoverGroupOf"/>).</summary>
    private IEnumerable<ChildNode> FollowersOf(ChildGroup group) =>
        group.Local.Members.Where(m => m.Owner is null && MoverGroupOf(m) == group);

    // ---- applying ---------------------------------------------------------------------------

    private void Realize()
    {
        // Moves are decided first, on the structure and labels as they were read. Everything that moves is then taken
        // out and put back in R's pre-order, so a parent is in place before its children, and a subtree is never
        // attached below itself.
        var moves = new List<(PeerClass Peer, ChildNode Node, object? Target)>();
        var followed = new List<(ChildGroup Group, ChildNode Node, DataSyncDisplayValue From)>();
        if (_s.Kind == ChildListKind.Nodes)
        {
            ForgetFinalPositions();
            foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
            {
                foreach (var group in peer.Groups)
                {
                    foreach (var node in Movers(group))
                    {
                        if (!TryMoveTarget(node, out var target)) continue;
                        moves.Add((peer, node, target));
                        if (!group.Moves) followed.Add((group, node, ParentDisplay(node.OriginalParent)));
                    }
                }
            }
        }

        foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
        {
            foreach (var group in peer.Groups.Where(g => g.TakeRemoteKey))
            {
                foreach (var node in Movers(group))
                {
                    node.Label = peer.Rep.Label;
                    if (_s.Kind == ChildListKind.Tags) node.Group = peer.Rep.Group;
                }
            }
        }

        var pending = new Dictionary<PeerClass, List<(ChildNode Node, ChildNode? From, object? Target)>>();
        foreach (var (peer, node, target) in moves)
        {
            if (!pending.TryGetValue(peer, out var list)) pending[peer] = list = [];
            list.Add((node, node.Parent, target));
            Detach(node);
        }

        foreach (var peer in _peers)
        {
            if (peer.Status is PeerClassStatus.Add or PeerClassStatus.Restore)
            {
                Create(peer);
            }
            else if (pending.TryGetValue(peer, out var list))
            {
                foreach (var (node, from, target) in list) Attach(node, NodeAt(target), from);
            }

            if (PartsOf(peer) is not { } parts) continue;
            foreach (var part in parts.Values.OfType<SplitPart>()) CreatePart(part);
        }

        // A node that follows its class out of a parent class a peer change split moved too, and says so.
        foreach (var (group, node, from) in followed)
        {
            var peer = group.Peer;
            AddField($"{KeyPath(peer.PeerId)}:parent", DataSyncFieldResolution.TookRemote,
                group.BaseNode is null ? null : ParentDisplay(group.BaseNode.Parent), from,
                peer.Parent is null ? RootDisplay : Display(peer.Parent.Rep, withColor: false), ParentDisplay(node.Parent));
        }
    }

    private DataSyncDisplayValue ParentDisplay(ChildNode? parent) =>
        parent is null ? RootDisplay : Display(parent, withColor: false);

    /// <summary>The group's own members, and the free members of its local class that follow it.</summary>
    private IEnumerable<ChildNode> Movers(ChildGroup group)
    {
        var nodes = new List<ChildNode>(group.Owned);
        nodes.AddRange(FollowersOf(group));
        return nodes.OrderBy(n => n.Seq);
    }

    private void Create(PeerClass peer)
    {
        var rep = peer.Rep;
        var node = CreateNode(peer, rep, null);
        peer.Created = node;

        var restored = peer.Status == PeerClassStatus.Restore;
        AddField(KeyPath(peer.PeerId),
            restored ? DataSyncFieldResolution.EditWinsRestored : DataSyncFieldResolution.TookRemote,
            peer.Base is null ? null : Display(peer.Base.Rep), null, Display(rep), Display(node));
        if (restored) _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.ChildRestored, ("uuid", rep.Uuid!)));
    }

    /// <summary>A part of a class split by this merge, added where its members' parents end (see <see cref="PartsOf"/>);
    /// its path is its first member's, since it is a class of its own here.</summary>
    private void CreatePart(SplitPart part)
    {
        var member = part.Member;
        var node = part.Created = CreateNode(part.Peer, member, part);
        if (part.Leader is { } leader)
        {
            node.Label = leader.Anchor!.Label;
            node.Color = leader.Color;
        }

        var baseNode = _s.Rules == ChildMergeRules.ThreeWay ? _baseNodeByUuid.GetValueOrDefault(member.Uuid!) : null;
        var restored = baseNode is not null;
        AddField(KeyPath(member.Uuid!),
            restored ? DataSyncFieldResolution.EditWinsRestored : DataSyncFieldResolution.TookRemote,
            baseNode is null ? null : Display(baseNode), null, Display(member), Display(node));
        if (restored) _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.ChildRestored, ("uuid", member.Uuid!)));
    }

    /// <summary>A new option for <paramref name="peer"/> from one of its members, under that member's parent as it ends
    /// here, appended after the local children (a fresh id when its own is taken here).</summary>
    private ChildNode CreateNode(PeerClass peer, ChildNode source, SplitPart? part)
    {
        var uuid = source.Uuid!;
        if (_taken.Contains(uuid))
        {
            var fresh = CustomPropertyUuids.Remap(uuid, _taken.Contains);
            _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.OptionUuidRemapped, ("uuid", uuid),
                ("newUuid", fresh)));
            uuid = fresh;
        }

        _taken.Add(uuid);
        var node = new ChildNode(uuid, source.Label, source.Group, source.Color)
        {
            Visible = true, CreatedFor = peer, Part = part, Seq = _local.Count + _created.Count,
        };
        var parent = source.Parent is null ? null : NodeAt(PositionOfRemoteNode(source.Parent));
        node.Parent = parent;
        (parent?.Children ?? _localRoots).Add(node);
        _created.Add(node);
        return node;
    }

    private void Detach(ChildNode node)
    {
        (node.Parent?.Children ?? _localRoots).Remove(node);
        node.Parent = null;
    }

    private void Attach(ChildNode node, ChildNode? target, ChildNode? from)
    {
        // A safety net under the cycle check: a subtree is never attached below itself.
        if (target is not null && target.IsWithin(node)) target = from is not null && !from.IsWithin(node) ? from : null;
        node.Parent = target;
        (target?.Children ?? _localRoots).Add(node);
    }

    // ---- deletions --------------------------------------------------------------------------

    /// <summary>
    /// The classes this merge may delete (§8.5.4 steps 3 and 5), and their roots.
    /// <list type="bullet">
    /// <item>FastForward: every local class no peer class claimed (the peer has seen it, so it deleted it).</item>
    /// <item>ThreeWay: a local class no peer class claimed that holds the counterpart of an option the peer deleted (in
    /// the base, not in R), when it is unchanged here — changed here, it is kept (edit wins).</item>
    /// <item>ThreeWay: an extra member of a class a peer class claimed, whose own counterpart the peer deleted and which is
    /// unchanged here, goes when the merge leaves it in a class of its own (another change split the class it was in);
    /// while it shares its class with an option the peer still has, it is never removed to match.</item>
    /// </list>
    /// A subtree goes only when everything published in it goes too.
    /// </summary>
    private void FindCandidates()
    {
        var picked = new List<Candidate>();
        if (_s.Rules == ChildMergeRules.FastForward)
        {
            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (peerId, localId) in _s.ChildMap.OrderBy(p => p.Key, StringComparer.Ordinal))
                reverse.TryAdd(localId, peerId);
            foreach (var cls in _localClasses.Where(c => !_leaders.ContainsKey(c)))
            {
                var peerId = cls.Members.Select(m => reverse.GetValueOrDefault(m.Uuid!)).FirstOrDefault(p => p is not null)
                             ?? cls.Rep.Uuid!;
                picked.Add(new Candidate(cls.Members, peerId, null));
            }
        }
        else if (_s.Rules == ChildMergeRules.ThreeWay && _hasBase)
        {
            var baseOf = BaseOf;
            var extra = new Dictionary<ChildNode, ChildNode>();
            foreach (var cls in _localClasses)
            {
                if (_leaders.ContainsKey(cls))
                {
                    foreach (var member in cls.Members.Where(m => m.Owner is null))
                    {
                        if (!baseOf.TryGetValue(member, out var baseMember) ||
                            _peerByMember.ContainsKey(baseMember.Uuid!) || !MemberUnchangedHere(member, baseMember))
                            continue;
                        extra[member] = baseMember;
                        _extras.Add(member);
                    }

                    continue;
                }

                var deleted = cls.Members.Select(n => baseOf.GetValueOrDefault(n))
                    .FirstOrDefault(m => m is not null && !_peerByMember.ContainsKey(m.Uuid!));
                if (deleted is null) continue;
                var baseClass = _baseByMember[deleted.Uuid!];
                if (!cls.Members.Any(n => baseOf.TryGetValue(n, out var m) && UnchangedHere(cls, n, m)))
                {
                    // Edit wins: kept, and published again; the peer receives it back as an add.
                    AddField(KeyPath(deleted.Uuid!), DataSyncFieldResolution.KeptLocal, Display(baseClass.Rep),
                        Display(cls.Rep), null, Display(cls.Rep));
                    continue;
                }

                picked.Add(new Candidate(cls.Members, deleted.Uuid!, baseClass.Rep));
            }

            if (extra.Count > 0)
            {
                var covered = picked.SelectMany(c => c.Members).ToHashSet();
                foreach (var cls in Classes(_localRoots, n => n.Visible || n.CreatedFor is not null)
                             .SelectMany(c => c.SelfAndDescendants()))
                {
                    if (!cls.Members.Any(extra.ContainsKey) ||
                        !cls.Members.All(n => covered.Contains(n) || extra.ContainsKey(n))) continue;
                    var members = cls.Members.Where(extra.ContainsKey).ToArray();
                    var baseMember = extra[members[0]];
                    picked.Add(new Candidate(members, baseMember.Uuid!, _baseByMember[baseMember.Uuid!].Rep));
                }
            }
        }
        else
        {
            return;
        }

        var nodes = picked.SelectMany(c => c.Members).ToHashSet();
        var valid = picked
            .Where(c => c.Members.All(m => m.SelfAndDescendants().Skip(1)
                .All(d => !(d.Visible || d.CreatedFor is not null) || nodes.Contains(d))))
            .OrderBy(c => c.Rep.Seq)
            .ToList();
        var validNodes = valid.SelectMany(c => c.Members).ToHashSet();
        // What the peer deleted and stays only for free members of the peer's classes below it gives its class nothing
        // either (ApplyColors): merging the other way those members join their classes elsewhere, and nothing brings it
        // back. What stays for a node of its own below it (added or changed here) is brought back there, and gives.
        foreach (var candidate in picked.Where(c => !c.Members.All(validNodes.Contains)))
        {
            if (candidate.Members.SelectMany(KeepersBelow)
                .All(d => d.Owner is null && d.Class is { } cls && _leaders.ContainsKey(cls)))
                _extras.UnionWith(candidate.Members);
        }

        // The nearest published nodes below a candidate that are no candidates themselves: what keeps it.
        IEnumerable<ChildNode> KeepersBelow(ChildNode node)
        {
            foreach (var child in node.Children)
            {
                if (!(child.Visible || child.CreatedFor is not null)) continue;
                if (!nodes.Contains(child))
                {
                    yield return child;
                    continue;
                }

                foreach (var keeper in KeepersBelow(child)) yield return keeper;
            }
        }

        _candidates = valid;
        _candidateRoots = valid.Where(c => !c.Members.Any(m =>
        {
            for (var a = m.Parent; a is not null; a = a.Parent)
            {
                if (validNodes.Contains(a)) return true;
            }

            return false;
        })).ToList();
    }

    /// <summary>An option itself unchanged here since the base: its key (as read, before this merge renamed anything), its
    /// colour and its parent.</summary>
    private bool MemberUnchangedHere(ChildNode member, ChildNode baseMember) =>
        member.Class!.Key == KeyOf(baseMember) && NormColor(member.Color) == NormColor(baseMember.Color) &&
        ParentUnchangedHere(member, baseMember);

    /// <summary>ThreeWay: every published local node that is the counterpart of a base option, with that option.</summary>
    private Dictionary<ChildNode, ChildNode> BaseOf =>
        _baseOf ??= _s.Rules == ChildMergeRules.ThreeWay && _hasBase ? BaseCounterparts() : [];

    private Dictionary<ChildNode, ChildNode> BaseCounterparts()
    {
        var baseOf = new Dictionary<ChildNode, ChildNode>();
        foreach (var member in _baseClasses.SelectMany(c => c.Members))
        {
            if (TargetOf(member.Uuid!) is { Visible: true } target) baseOf.TryAdd(target, member);
        }

        return baseOf;
    }

    /// <summary>
    /// A local class is unchanged here since the base when one of its members still sits in the base class it came
    /// from as the comparison form sees it: the same key, the class's colour, the same parent. A member renamed into
    /// another existing class changes nothing the form shows.
    /// </summary>
    private bool UnchangedHere(ChildClass local, ChildNode member, ChildNode baseMember)
    {
        var baseClass = _baseByMember[baseMember.Uuid!];
        return baseClass.Key == local.Key && NormColor(local.Rep.Color) == NormColor(baseClass.Rep.Color) &&
               ParentUnchangedHere(member, baseMember);
    }

    /// <summary>
    /// A node still has the parent it had in the base, judged node by node, as the other side judges it (a move between
    /// two members of one class counts): its parent here by the peer id it is linked by, else by its own id.
    /// </summary>
    private bool ParentUnchangedHere(ChildNode member, ChildNode baseMember)
    {
        if (_s.Kind != ChildListKind.Nodes) return true;
        var parent = member.OriginalParent;
        var localId = parent is null ? "" : PeerIds.GetValueOrDefault(parent) ?? parent.Uuid;
        return localId == (baseMember.Parent?.Uuid ?? "");
    }

    // ---- finishing --------------------------------------------------------------------------

    /// <summary>
    /// §8.5.5: every class as it ends takes a colour on its representative, the one member whose colour the class shows.
    /// A member's colour is the one its group decided (an owned member's group, the group a free member follows), else
    /// its own; an option this merge added gives the colour of the peer's option with the smallest id placed there. When
    /// a class gathers members with different decisions (a rename or a move onto another class), the member that most
    /// is this class gives it: first one whose class had this key on both sides, then one whose class had it on one side,
    /// then any; among those the one with the ordinally smallest identity. An identity is what both directions of one
    /// merge share: for a group, the smallest id among the options it claimed (the options both sides have); for an
    /// added option, that peer option's id; else the option's own id. Never the first in order: moves and adds are
    /// appended, and the two directions of one merge order them differently. A member the peer deleted, kept only
    /// because its class stays or for free members below it (see <see cref="FindCandidates"/>), gives nothing. A class
    /// no group decided anything about, whose members were one class before (here, or at the peer for what was added),
    /// is left alone.
    /// </summary>
    private void ApplyColors()
    {
        var classes = Classes(_localRoots,
            n => (n.Visible || n.CreatedFor is not null) && !n.Removed && !n.HeldHere);
        foreach (var cls in classes.SelectMany(c => c.SelfAndDescendants()))
        {
            // A member the peer deleted, which stays only because its class stays, gives nothing: merging the other way
            // it is not there.
            var withId = cls.Members.Where(m => m.Uuid is not null).ToArray();
            var givers = withId.Any(m => !_extras.Contains(m)) ? withId.Where(m => !_extras.Contains(m)) : withId;
            var members = givers.Select(m => (Node: m, Giver: GiverOf(m, cls.Key))).ToArray();
            // Left alone: a class this merge decided nothing about, which was one class before — here, or at the peer
            // for what this merge added.
            if (!members.Any(m => m.Giver.Decided) && withId.Select(OriginOf).Distinct().Count() <= 1) continue;
            var giver = members.OrderBy(m => m.Giver.Tier)
                .ThenBy(m => m.Giver.Identity, StringComparer.Ordinal)
                .ThenBy(m => m.Node.Uuid, StringComparer.Ordinal)
                .First();
            if (NormColor(cls.Rep.Color) != giver.Giver.Color) cls.Rep.Color = giver.Giver.Color;
        }
    }

    /// <summary>
    /// What a member of a class ending with <paramref name="key"/> would give it: the colour, whether it was decided by
    /// this merge, its tier — 1 when its class had the key here and at the peer, 2 on one side, 3 on neither — and the
    /// identity of the decision (see <see cref="ApplyColors"/>).
    /// </summary>
    private (bool Decided, string? Color, int Tier, string Identity) GiverOf(ChildNode node, string key)
    {
        var group = MoverGroupOf(node) ?? node.Part?.Leader;
        if (group is not null)
            return (true, group.Color, 3 - (group.Local.Key == key ? 1 : 0) - (group.Peer.Key == key ? 1 : 0),
                IdentityOf(group));
        // What this merge added stands for the peer's options placed there, which merging the other way are that side's
        // own options, each giving its own colour: the one with the smallest id speaks for them.
        if (node.CreatedFor is { } peer)
        {
            var first = peer.Class.Members.Where(m => ReferenceEquals(NodeAt(PositionOfRemoteNode(m)), node))
                .MinBy(m => m.Uuid, StringComparer.Ordinal) ?? node.Part?.Member ?? peer.Rep;
            return (false, NormColor(first.Color), peer.Key == key ? 2 : 3, first.Uuid!);
        }

        return (false, NormColor(node.Color), node.Class?.Key == key ? 2 : 3, node.Uuid!);
    }

    /// <summary>Where a node's class came from: its class here, or what this merge added it for.</summary>
    private static object? OriginOf(ChildNode node) => node.Part ?? (object?) node.CreatedFor ?? node.Class;

    private static string IdentityOf(ChildGroup group) =>
        group.Owned.Select(n => n.Uuid!).Min(StringComparer.Ordinal)!;

    /// <summary>
    /// An option this merge created (an add, a restore, a part of a split class) that lands beside an option of its class
    /// this device keeps without an id (§3.3) is stored in that option instead: it takes the created option's id, colour
    /// and children, keeps its own label and place, and the created option goes. The list gains no second member of the
    /// class, and the option without an id becomes the class's published counterpart; nothing referenced it, so nothing
    /// is lost. Only a childless one — its own subtree would be published with it — and only where no option with an id
    /// has that key: the class then has the created option alone, so what this device publishes is exactly as without
    /// the adoption.
    /// </summary>
    private void AdoptTwins()
    {
        for (var i = 0; i < _created.Count; i++)
        {
            var created = _created[i];
            var siblings = created.Parent?.Children ?? _localRoots;
            var key = KeyOf(created);
            ChildNode? twin = null;
            var taken = false;
            foreach (var node in siblings)
            {
                if (ReferenceEquals(node, created) || KeyOf(node) != key) continue;
                if (node.Uuid is not null)
                {
                    taken = true;
                    break;
                }

                if (twin is null && node.Children.Count == 0 && !IsOverlaid(node)) twin = node;
            }

            if (taken || twin is null) continue;
            twin.Uuid = created.Uuid;
            twin.Color = created.Color;
            twin.CreatedFor = created.CreatedFor;
            twin.Part = created.Part;
            foreach (var child in created.Children)
            {
                child.Parent = twin;
                twin.Children.Add(child);
            }

            created.Children.Clear();
            siblings.Remove(created);
            if (created.Part is { } part && ReferenceEquals(part.Created, created)) part.Created = twin;
            else if (created.CreatedFor is { } peer && ReferenceEquals(peer.Created, created)) peer.Created = twin;
            _created[i] = twin;
        }
    }

    /// <summary>Every member of R mapped to where its class ends here; older entries kept while their target stays.</summary>
    private Dictionary<string, string> BuildChildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var peer in _peers)
        {
            switch (peer.Status)
            {
                case PeerClassStatus.Present or PeerClassStatus.Add or PeerClassStatus.Restore:
                    foreach (var member in peer.Class.Members)
                        map[member.Uuid!] = NodeAt(PositionOfRemoteNode(member))!.Uuid!;
                    break;
                case PeerClassStatus.Invisible when peer.InvisibleTarget?.Uuid is { } target:
                    foreach (var member in peer.Class.Members) map[member.Uuid!] = target;
                    break;
            }
        }

        foreach (var (peerId, localId) in _s.ChildMap.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!map.ContainsKey(peerId) && !_peerByMember.ContainsKey(peerId) &&
                _localByUuid.TryGetValue(localId, out var node) && !node.Removed) map[peerId] = localId;
        }

        return map;
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>The local node a peer option maps to: its <c>ChildMap</c> target, else a local option with its id.</summary>
    private ChildNode? TargetOf(string peerUuid) =>
        _s.ChildMap.TryGetValue(peerUuid, out var localUuid)
            ? _localByUuid.GetValueOrDefault(localUuid)
            : _localByUuid.GetValueOrDefault(peerUuid);

    /// <summary>An overlay child (§3.6), or a node below one: never touched by a merge.</summary>
    private static bool IsOverlaid(ChildNode node)
    {
        for (var n = node; n is not null; n = n.OriginalParent)
        {
            if (n.Overlay) return true;
        }

        return false;
    }

    /// <summary>ThreeWay only: the base node of the first of <paramref name="peerMembers"/> the base has.</summary>
    private ChildNode? BaseNodeOf(IEnumerable<ChildNode> peerMembers) =>
        _s.Rules != ChildMergeRules.ThreeWay
            ? null
            : peerMembers.Select(m => _baseNodeByUuid.GetValueOrDefault(m.Uuid!)).FirstOrDefault(n => n is not null);

    /// <summary>
    /// The group of <paramref name="peer"/>'s members in <paramref name="local"/> that come from one base class: members
    /// of one class here with different histories (one renamed into it here, another there) each merge against their
    /// own base. Multilevel members claimed by id are grouped by their parent node on every side too, so a node's
    /// parent merges node by node: a move between two members of one class, which the form does not show, is decided
    /// for the node that moved alone, and still carried when another change splits that class.
    /// </summary>
    private ChildGroup GroupOf(PeerClass peer, ChildClass local, ChildNode remoteMember, ChildNode? baseNode,
        ChildNode? localNode = null)
    {
        var baseClass = baseNode is null ? null : _baseByMember[baseNode.Uuid!];
        var parents = _s.Kind == ChildListKind.Nodes && localNode is not null
            ? (localNode.OriginalParent, remoteMember.Parent?.Uuid ?? "",
                baseNode is null ? null : baseNode.Parent?.Uuid ?? "")
            : ((ChildNode?)null, (string?)null, (string?)null);
        var group = peer.Groups.FirstOrDefault(g => g.Local == local && g.Base == baseClass &&
                                                    ReferenceEquals(g.Parents.Local, parents.Item1) &&
                                                    g.Parents.Remote == parents.Item2 && g.Parents.Base == parents.Item3);
        if (group is not null) return group;
        group = new ChildGroup(peer, local, remoteMember, baseNode, baseClass)
        {
            Parents = parents, Index = _groupCount++,
        };
        peer.Groups.Add(group);
        return group;
    }

    /// <summary>
    /// A local node as a logical class: the peer class that owns it or leads its class, else its class (nobody claimed
    /// it), else the node itself (it is not published).
    /// </summary>
    private object NodeLogical(ChildNode node)
    {
        if (node.Owner is { } owner) return owner;
        if (node.Class is not { } cls) return node;
        return _leaders.TryGetValue(cls, out var leader) ? leader.Peer : cls;
    }

    /// <summary>The logical class a local node was under before the merge.</summary>
    private object OriginalParentOf(ChildNode node) => node.OriginalParent is null ? Root : NodeLogical(node.OriginalParent);

    /// <summary>A base node's parent as a logical class: the peer class that still has it, else its counterpart here,
    /// else itself (a parent gone everywhere).</summary>
    private object BaseParentOf(ChildNode baseNode)
    {
        var parent = baseNode.Parent;
        if (parent is null) return Root;
        if (_peerByMember.TryGetValue(parent.Uuid!, out var peer)) return peer;
        return TargetOf(parent.Uuid!) is { Visible: true } target ? NodeLogical(target) : parent;
    }

    private void AddPeer(ChildClass cls, PeerClass? parent)
    {
        var peer = new PeerClass(cls, parent);
        _peers.Add(peer);
        foreach (var member in cls.Members)
        {
            _peerByMember.TryAdd(member.Uuid!, peer);
            _remoteByUuid.TryAdd(member.Uuid!, member);
        }

        foreach (var child in cls.Children) AddPeer(child, peer);
    }

    /// <summary>
    /// Marks the local nodes that are published: the published list is the local list with some options left out, in
    /// the same order, so a greedy walk finds them (a duplicate uuid keeps its first occurrence, as the reader does).
    /// </summary>
    private static void Align(IReadOnlyList<ChildNode> local, IReadOnlyList<ChildNode> published)
    {
        var p = 0;
        foreach (var node in local)
        {
            if (p >= published.Count) break;
            var candidate = published[p];
            if (node.Uuid != candidate.Uuid || node.Label != candidate.Label || node.Group != candidate.Group ||
                NormColor(node.Color) != NormColor(candidate.Color)) continue;
            node.Visible = true;
            Align(node.Children, candidate.Children);
            p++;
        }
    }

    private List<ChildClass> Classes(IEnumerable<ChildNode> level, Func<ChildNode, bool> include)
    {
        var classes = new List<ChildClass>();
        var byKey = new Dictionary<string, ChildClass>(StringComparer.Ordinal);
        foreach (var node in level)
        {
            if (!include(node)) continue;
            var key = KeyOf(node);
            if (!byKey.TryGetValue(key, out var cls))
            {
                byKey[key] = cls = new ChildClass(key);
                classes.Add(cls);
            }

            cls.Members.Add(node);
        }

        foreach (var cls in classes) cls.Children.AddRange(Classes(cls.Members.SelectMany(m => m.Children), include));
        return classes;
    }

    /// <summary>§3.4's class key; a tag's is its folded group (<c>""</c> for none) and name, joined by U+0000, which no
    /// published label contains.</summary>
    private string KeyOf(ChildNode node) => _s.Kind == ChildListKind.Tags
        ? DataSyncLabelKey.Fold(node.Group ?? "", _s.IgnoreCase) + "\0" + DataSyncLabelKey.Fold(node.Label, _s.IgnoreCase)
        : DataSyncLabelKey.Fold(node.Label, _s.IgnoreCase);

    /// <summary>
    /// A three-way over a field of one group. Each side is judged against its own base: the base counterpart of the
    /// local members and that of the peer member, which are one and the same except for a class claimed by key (two
    /// base classes that met under one key). A side without a base counterpart counts as changed.
    /// </summary>
    private DataSyncFieldResolution Resolve(string? localBase, string? remoteBase, string l, string r, bool hasLocalBase,
        bool hasRemoteBase) => _s.Rules switch
    {
        ChildMergeRules.FastForward => DataSyncFieldResolution.TookRemote,
        ChildMergeRules.ThreeWay when hasLocalBase || hasRemoteBase =>
            Decide(!hasLocalBase || l != localBase, !hasRemoteBase || r != remoteBase, Concurrent()),
        _ => Concurrent(),
    };

    /// <summary>Only the peer changed it: take it; only this device: keep it; both (or neither, yet they differ): <paramref name="both"/>.</summary>
    private DataSyncFieldResolution Decide(bool localChanged, bool remoteChanged, DataSyncFieldResolution both) =>
        remoteChanged && !localChanged ? DataSyncFieldResolution.TookRemote
        : localChanged && !remoteChanged ? DataSyncFieldResolution.KeptLocal
        : both;

    /// <summary>The base node the local side of a group comes from: the linking member's, or for a group claimed by
    /// key, the first of its local members that has a base counterpart.</summary>
    private ChildNode? LocalBaseNodeOf(ChildGroup group)
    {
        if (!group.ByKey) return group.BaseNode;
        return group.Owned.Select(n => BaseOf.GetValueOrDefault(n)).FirstOrDefault(n => n is not null);
    }

    private ChildClass? LocalBaseClassOf(ChildGroup group) =>
        LocalBaseNodeOf(group) is { } node ? _baseByMember[node.Uuid!] : null;

    /// <summary>Both sides changed it (or no base can tell): Follow takes the peer's value unless the local one came from
    /// another device (§8.5.2).</summary>
    private DataSyncFieldResolution Concurrent() =>
        _s.LinkMode == DataSyncLinkMode.Follow && _s.LocalLastEditorIsSelf
            ? DataSyncFieldResolution.FollowTookRemote
            : DataSyncFieldResolution.Conflict;

    internal static bool TakesRemote(DataSyncFieldResolution resolution) =>
        resolution is DataSyncFieldResolution.TookRemote or DataSyncFieldResolution.FollowTookRemote;

    private string KeyPath(string peerId) => $"{_prefix}:{peerId}";

    /// <summary>
    /// One outcome per path. A peer class split over several local groups reports its first group's, unless a later
    /// group's is a conflict: the item must not be lost. Likewise a parent that took the peer's value somewhere is
    /// reported over one kept: every node that moves has an outcome saying so.
    /// </summary>
    private void AddField(string path, DataSyncFieldResolution resolution, DataSyncDisplayValue? b,
        DataSyncDisplayValue? l, DataSyncDisplayValue? r, DataSyncDisplayValue? result)
    {
        var outcome = new DataSyncFieldOutcome(path, resolution, b, l, r, result);
        if (_paths.Add(path))
        {
            _fields.Add(outcome);
            return;
        }

        var index = _fields.FindIndex(f => f.Path == path);
        var existing = _fields[index].Resolution;
        if (existing == DataSyncFieldResolution.Conflict) return;
        if (resolution == DataSyncFieldResolution.Conflict ||
            (path.EndsWith(":parent", StringComparison.Ordinal) && TakesRemote(resolution) && !TakesRemote(existing)))
            _fields[index] = outcome;
    }

    private static readonly DataSyncDisplayValue RootDisplay = new(null, Path: []);

    private DataSyncDisplayValue Display(ChildNode node, string? label = null, string? group = null,
        bool withColor = true)
    {
        var color = withColor ? NormColor(node.Color) : null;
        return _s.Kind switch
        {
            ChildListKind.Choices => new DataSyncDisplayValue(label ?? node.Label, color),
            ChildListKind.Tags => new DataSyncDisplayValue(label ?? node.Label, color, label is null ? node.Group : group),
            _ => new DataSyncDisplayValue(label ?? node.Label, color,
                Path: label is null ? node.Path() : node.Path().SkipLast(1).Append(label).ToArray()),
        };
    }

    internal static string? NormColor(string? color) => string.IsNullOrEmpty(color) ? null : color;
}

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

    /// <summary>An added or restored class's colour: the peer representative's.</summary>
    public string? Color { get; set; }

    /// <summary>Where its class is here: the first node of its first group (the one adds and moves go under).</summary>
    public ChildNode PrimaryNode => Groups[0].Owned[0];
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
/// key among the local classes under its parent's counterpart (only a class no other peer class claimed). Each local
/// member is claimed at most once, by the first peer class in R's pre-order; the members a peer class claimed in one
/// local class that come from one base class form a group. A local class's free members follow the group that claimed
/// its first member (its leader): they take its renames and its moves. A peer class whose counterpart is an overlay
/// child, or which lies below one, is invisible: mapped, never touched.
/// </para>
/// <para>
/// <b>Deciding.</b> Per group, against the base node of the member that linked it: the key three-way, the colour
/// (§8.5.5, written to the representative of the class it ends in) and, for multilevel, the parent over logical classes
/// (the peer class that owns or leads a parent node, or a local class nobody claimed). Moves that would close a cycle
/// keep their local parent and become conflicts, in the order of peer ids. A peer class with no counterpart is added
/// (FastForward, NoBase, not in the base), restored (in the base and changed there since) or stays deleted.
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
/// of its members still sits in the base class it came from (the same key and colour, the same parent node), so moving
/// a duplicate into another existing class changes nothing.
/// </para>
/// <para>
/// Nothing is reordered: renames and colours change nodes in place, a move or an add is appended at the end of its
/// new parent's children, and every local node the steps do not remove stays, valid or not.
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
    private readonly Dictionary<object, Siblings> _siblings = new(ReferenceEqualityComparer.Instance);
    private Dictionary<ChildNode, ChildNode>? _baseOf;
    private Dictionary<ChildNode, string>? _peerIds;
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
        return new ChildMergeOutcome(_localRoots, _fields, BuildChildMap(), _created.Select(n => n.Uuid!).ToArray(),
            removed, held, _released, [], _warnings);
    }

    // ---- defaultValue (§8.5.4 step 9) --------------------------------------------------------

    /// <summary>
    /// The local option a peer option ends as after the merge: its own counterpart when that is still its class's, else
    /// the class's first counterpart, the option added or restored for it, or the overlay child it maps to. Null when
    /// its class stays deleted here.
    /// </summary>
    public ChildNode? RemoteTarget(string uuid)
    {
        if (!_peerByMember.TryGetValue(uuid, out var peer)) return null;
        switch (peer.Status)
        {
            case PeerClassStatus.Present:
                var target = TargetOf(uuid);
                return target is { Removed: false } && target.Owner == peer ? target : peer.PrimaryNode;
            case PeerClassStatus.Add or PeerClassStatus.Restore:
                return peer.Created;
            case PeerClassStatus.Invisible:
                return peer.InvisibleTarget;
            default:
                return null;
        }
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
                    peer.InvisibleTarget ??= target;
                    continue;
                }

                if (target.Owner is null)
                {
                    target.Owner = peer;
                    var group = GroupOf(peer, target.Class!, member, BaseNodeOf([member]));
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
                var parents = peer.Parent.Groups.Select(g => g.Local).ToArray();
                siblings = SiblingsOf(peer.Parent, parents.SelectMany(c => c.Children),
                    parents.SelectMany(c => c.Members).SelectMany(m => m.Children));
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
            }
        }
    }

    // ---- decisions --------------------------------------------------------------------------

    private void DecideKeysAndColors()
    {
        foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
        {
            foreach (var group in peer.Groups)
            {
                if (group.Local.Key == peer.Key) continue;
                var localBase = LocalBaseClassOf(group);
                var resolution = Resolve(localBase?.Key, group.Base?.Key, group.Local.Key, peer.Key, localBase is not null,
                    group.Base is not null);
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

        foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
        {
            var remoteDisplay = peer.Parent is null ? RootDisplay : Display(peer.Parent.Rep, withColor: false);
            foreach (var group in peer.Groups.Where(g => g.ParentResolution is not null))
            {
                var local = group.Anchor!.OriginalParent;
                var localDisplay = local is null ? RootDisplay : Display(local, withColor: false);
                var baseParent = group.BaseNode?.Parent;
                var baseDisplay = group.BaseNode is null ? null
                    : baseParent is null ? RootDisplay
                    : Display(baseParent, withColor: false);
                AddField($"{KeyPath(peer.PeerId)}:parent", group.ParentResolution!.Value, baseDisplay, localDisplay,
                    remoteDisplay, group.Moves ? remoteDisplay : localDisplay);
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
        ChildNode node => TryMoveTarget(node, out var target) ? target : node.OriginalParent,
        _ => null,
    };

    /// <summary>
    /// Whether a node moves, and where to: a group's nodes (and the free members it leads) all end under the group's
    /// final parent, so a member that sits elsewhere here follows even when the group itself did not move. A moving group
    /// goes under the counterpart of the peer's own parent node; a member that follows goes under the node the group's
    /// first member already has.
    /// </summary>
    private bool TryMoveTarget(ChildNode node, out object? position)
    {
        position = null;
        if (_s.Kind != ChildListKind.Nodes || MoverGroupOf(node) is not { FinalParent: { } finalParent } group) return false;
        if (group.Moves)
        {
            position = group.RemoteMember.Parent is null ? null : PositionOfRemoteNode(group.RemoteMember.Parent);
            return !ReferenceEquals(position, node.OriginalParent);
        }

        if (ReferenceEquals(OriginalParentOf(node), finalParent)) return false;
        position = group.Anchor!.OriginalParent;
        return true;
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
    /// The position a peer node has here: its own counterpart while that is still its class's, else its class's first
    /// counterpart, the overlay child it maps to, or its class still to be created (standing for its node).
    /// </summary>
    private object? PositionOfRemoteNode(ChildNode remoteNode)
    {
        var peer = _peerByMember[remoteNode.Uuid!];
        return peer.Status switch
        {
            PeerClassStatus.Present => TargetOf(remoteNode.Uuid!) is { Removed: false } target && target.Owner == peer
                ? target
                : peer.PrimaryNode,
            PeerClassStatus.Invisible => peer.InvisibleTarget,
            _ => (object?)peer.Created ?? peer,
        };
    }

    private static ChildNode? NodeAt(object? position) => position switch
    {
        ChildNode node => node,
        PeerClass peer => peer.Created,
        _ => null,
    };

    /// <summary>The group whose decisions a node follows: its own, or the one leading its class.</summary>
    private ChildGroup? MoverGroupOf(ChildNode node) =>
        node.OwnerGroup ?? (node.Class is { } cls && _leaders.TryGetValue(cls, out var leader) ? leader : null);

    // ---- applying ---------------------------------------------------------------------------

    private void Realize()
    {
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

        // Moves: everything that moves is taken out first and put back in R's pre-order, so a parent is in place
        // before its children, and a subtree is never attached below itself.
        var pending = new Dictionary<PeerClass, List<(ChildNode Node, ChildNode? From, object? Target)>>();
        if (_s.Kind == ChildListKind.Nodes)
        {
            foreach (var peer in _peers.Where(p => p.Status == PeerClassStatus.Present))
            {
                foreach (var group in peer.Groups)
                {
                    foreach (var node in Movers(group))
                    {
                        if (!TryMoveTarget(node, out var target)) continue;
                        if (!pending.TryGetValue(peer, out var list)) pending[peer] = list = [];
                        list.Add((node, node.Parent, target));
                        Detach(node);
                    }
                }
            }
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
        }
    }

    /// <summary>The group's own members, and the free members of its local class when it leads that class.</summary>
    private IEnumerable<ChildNode> Movers(ChildGroup group)
    {
        var nodes = new List<ChildNode>(group.Owned);
        if (_leaders.TryGetValue(group.Local, out var leader) && leader == group)
            nodes.AddRange(group.Local.Members.Where(m => m.Owner is null));
        return nodes.OrderBy(n => n.Seq);
    }

    private void Create(PeerClass peer)
    {
        var rep = peer.Rep;
        var uuid = rep.Uuid!;
        if (_taken.Contains(uuid))
        {
            var fresh = CustomPropertyUuids.Remap(uuid, _taken.Contains);
            _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.OptionUuidRemapped, ("uuid", uuid),
                ("newUuid", fresh)));
            uuid = fresh;
        }

        _taken.Add(uuid);
        var node = new ChildNode(uuid, rep.Label, rep.Group, rep.Color)
        {
            Visible = true, CreatedFor = peer, Seq = _local.Count + _created.Count,
        };
        var parent = rep.Parent is null ? null : NodeAt(PositionOfRemoteNode(rep.Parent));
        node.Parent = parent;
        (parent?.Children ?? _localRoots).Add(node);
        _created.Add(node);
        peer.Created = node;
        peer.Color = NormColor(rep.Color);

        var restored = peer.Status == PeerClassStatus.Restore;
        AddField(KeyPath(peer.PeerId),
            restored ? DataSyncFieldResolution.EditWinsRestored : DataSyncFieldResolution.TookRemote,
            peer.Base is null ? null : Display(peer.Base.Rep), null, Display(rep), Display(node));
        if (restored) _warnings.Add(CustomPropertyCodec.Warning(DataSyncWarningCode.ChildRestored, ("uuid", rep.Uuid!)));
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
                        if (baseOf.TryGetValue(member, out var baseMember) && !_peerByMember.ContainsKey(baseMember.Uuid!) &&
                            MemberUnchangedHere(member, baseMember)) extra[member] = baseMember;
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

    /// <summary>An option itself unchanged here since the base: its key, its colour and its parent.</summary>
    private bool MemberUnchangedHere(ChildNode member, ChildNode baseMember) =>
        KeyOf(member) == KeyOf(baseMember) && NormColor(member.Color) == NormColor(baseMember.Color) &&
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
    /// A node still has the parent it had in the base: judged node by node where its parent here is linked by id (a move
    /// between two members of one class counts, as it does on the other side), else as logical classes.
    /// </summary>
    private bool ParentUnchangedHere(ChildNode member, ChildNode baseMember)
    {
        if (_s.Kind != ChildListKind.Nodes) return true;
        var parent = member.OriginalParent;
        var localId = parent is null ? "" : PeerIds.GetValueOrDefault(parent);
        return localId is not null
            ? localId == (baseMember.Parent?.Uuid ?? "")
            : ReferenceEquals(OriginalParentOf(member), BaseParentOf(baseMember));
    }

    // ---- finishing --------------------------------------------------------------------------

    /// <summary>
    /// §8.5.5: every class as it ends takes a colour on its representative, the one member whose colour the class shows.
    /// A member's colour is the one decided for it (an owned member's group, a free member's leading group, an added
    /// option's peer class), else its own. When a class gathers members with different decisions (a rename or a move onto
    /// another class), the member that most is this class gives it: first one whose class had this key on both sides,
    /// then one whose class had it on one side, then any; among those the ordinally smallest id. Never the first in
    /// order: moves and adds are appended, and the two directions of one merge order them differently. A class with no
    /// decided member is left alone.
    /// </summary>
    private void ApplyColors()
    {
        var classes = Classes(_localRoots,
            n => (n.Visible || n.CreatedFor is not null) && !n.Removed && !n.HeldHere);
        foreach (var cls in classes.SelectMany(c => c.SelfAndDescendants()))
        {
            var members = cls.Members.Where(m => m.Uuid is not null)
                .Select(m => (Node: m, Giver: GiverOf(m, cls.Key)))
                .ToArray();
            if (!members.Any(m => m.Giver.Decided)) continue;
            var giver = members.OrderBy(m => m.Giver.Tier).ThenBy(m => m.Node.Uuid, StringComparer.Ordinal).First();
            if (NormColor(cls.Rep.Color) != giver.Giver.Color) cls.Rep.Color = giver.Giver.Color;
        }
    }

    /// <summary>
    /// What a member of a class ending with <paramref name="key"/> would give it: the colour, whether it was decided by
    /// this merge, and its tier — 1 when its class had the key here and at the peer, 2 on one side, 3 on neither.
    /// </summary>
    private (bool Decided, string? Color, int Tier) GiverOf(ChildNode node, string key)
    {
        var group = MoverGroupOf(node);
        if (group is not null)
            return (true, group.Color, 3 - (group.Local.Key == key ? 1 : 0) - (group.Peer.Key == key ? 1 : 0));
        if (node.CreatedFor is { } peer) return (true, peer.Color, peer.Key == key ? 2 : 3);
        return (false, NormColor(node.Color), node.Class?.Key == key ? 2 : 3);
    }

    /// <summary>Every member of R mapped to where its class ends here; older entries kept while their target stays.</summary>
    private Dictionary<string, string> BuildChildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var peer in _peers)
        {
            switch (peer.Status)
            {
                case PeerClassStatus.Present:
                    var primary = peer.PrimaryNode.Uuid!;
                    foreach (var member in peer.Class.Members)
                    {
                        var target = TargetOf(member.Uuid!);
                        map[member.Uuid!] = target is { Removed: false } && target.Owner == peer ? target.Uuid! : primary;
                    }

                    break;
                case PeerClassStatus.Add or PeerClassStatus.Restore:
                    foreach (var member in peer.Class.Members) map[member.Uuid!] = peer.Created!.Uuid!;
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

    /// <summary>ThreeWay only: the base node of the first of <paramref name="peerMembers"/> the base has.</summary>
    private ChildNode? BaseNodeOf(IEnumerable<ChildNode> peerMembers) =>
        _s.Rules != ChildMergeRules.ThreeWay
            ? null
            : peerMembers.Select(m => _baseNodeByUuid.GetValueOrDefault(m.Uuid!)).FirstOrDefault(n => n is not null);

    /// <summary>
    /// The group of <paramref name="peer"/>'s members in <paramref name="local"/> that come from one base class: members
    /// of one class here with different histories (one renamed into it here, another there) each merge against their
    /// own base.
    /// </summary>
    private ChildGroup GroupOf(PeerClass peer, ChildClass local, ChildNode remoteMember, ChildNode? baseNode)
    {
        var baseClass = baseNode is null ? null : _baseByMember[baseNode.Uuid!];
        var group = peer.Groups.FirstOrDefault(g => g.Local == local && g.Base == baseClass);
        if (group is not null) return group;
        group = new ChildGroup(peer, local, remoteMember, baseNode, baseClass);
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
        foreach (var member in cls.Members) _peerByMember.TryAdd(member.Uuid!, peer);
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
    /// group's is a conflict: the item must not be lost.
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

        if (resolution != DataSyncFieldResolution.Conflict) return;
        var index = _fields.FindIndex(f => f.Path == path);
        if (_fields[index].Resolution != DataSyncFieldResolution.Conflict) _fields[index] = outcome;
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

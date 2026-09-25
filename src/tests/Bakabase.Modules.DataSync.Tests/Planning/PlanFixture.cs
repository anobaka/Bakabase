using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Tests.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Planning;

/// <summary>
/// One first-contact review around the test kind (and extension groups): this device's rows and tombstones, the
/// peer's staged snapshot (records staged through the real validation), and the planner's input.
/// </summary>
internal sealed class PlanFixture
{
    public const string ItemKind = TestItemCodec.Kind;
    public const string GroupKind = DataSyncKindIds.ExtensionGroup;
    public const string SelfNode = MergeFixture.SelfNode;
    public const string PeerNode = MergeFixture.PeerNode;
    public static readonly DataSyncActorId Self = MergeFixture.Self;
    public static readonly DataSyncActorId Peer = MergeFixture.Peer;

    public readonly Dictionary<string, IDataSyncKindCodec> Codecs = new(StringComparer.Ordinal)
    {
        [ItemKind] = TestItemCodec.Instance, [GroupKind] = ExtensionGroupCodec.Instance,
    };

    public readonly Dictionary<string, List<DataSyncLocalEntityState>> Entities = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<DataSyncTombstoneState>> Tombstones = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<DataSyncWireRecord>> Records = new(StringComparer.Ordinal);
    public readonly Dictionary<string, DataSyncHeldReason> KindHeld = new(StringComparer.Ordinal);

    /// <summary>Kinds the peer serves that this build does not know (no codec; staged as unsupported).</summary>
    public readonly HashSet<string> Unsupported = new(StringComparer.Ordinal);

    public DataSyncLinkMode Mode = DataSyncLinkMode.Follow;
    public string? OwnNodeId = SelfNode;
    public string SourceNodeId = PeerNode;
    public DataSyncLimits Limits = DataSyncLimits.Default;
    private long _seq = 100;

    public static SyncKey K(int i) => MergeFixture.K(i);
    public static string Hex(int i) => K(i).Value;

    public static DataSyncVersionVector Vv(params (DataSyncActorId Actor, long Counter)[] counters) =>
        MergeFixture.Vv(counters);

    public static TestItemContent T(string name, params (string Id, string Label)[] children) =>
        MergeFixture.T(name, children);

    public static TestItemContent Typed(string name, string type, params (string Id, string Label)[] children) =>
        MergeFixture.T(name, null, type, children);

    public static ExtensionGroupContentV1 G(string name, params string[] extensions) => new(name, extensions);

    public IDataSyncKindCodec CodecOf(string kind) => Codecs[kind];

    // ---- this device -------------------------------------------------------------------------------

    /// <summary>A live row; its vector is <c>{self: selfCounter}</c> unless given.</summary>
    public DataSyncLocalEntityState Local(string localKey, int key, object content, string kind = ItemKind,
        DataSyncVersionVector? vv = null, DataSyncEntitySyncState state = DataSyncEntitySyncState.Synced,
        bool unreadable = false, params int[] aliases)
    {
        var codec = CodecOf(kind);
        var entity = new DataSyncLocalEntityState(localKey, new EntityKeys([K(key), .. aliases.Select(K)]), content,
            ContentHash.Of(codec.Write(content)), "", vv ?? Vv((Self, 1)), Self, MergeFixture.SelfEditor, null, state,
            DataSyncOverlay.None, false, false, false, null, null, 10, unreadable);
        if (!Entities.TryGetValue(kind, out var list)) Entities[kind] = list = [];
        list.Add(entity);
        return entity;
    }

    public void Tombstone(int key, string kind = ItemKind, params int[] aliases)
    {
        if (!Tombstones.TryGetValue(kind, out var list)) Tombstones[kind] = list = [];
        list.Add(new DataSyncTombstoneState(new EntityKeys([K(key), .. aliases.Select(K)]), Vv((Self, 1)),
            MergeFixture.SelfEditor, DataSyncEntitySyncState.Synced, DataSyncTombstoneKind.Deleted, true, 5));
    }

    // ---- the peer ----------------------------------------------------------------------------------

    /// <summary>A live record of the peer; its vector is <c>{peer: 1}</c> (concurrent with local rows) unless given.</summary>
    public DataSyncWireRecord Pull(int key, object content, string kind = ItemKind, DataSyncVersionVector? vv = null,
        string? orderKey = null, params int[] aliases)
    {
        var json = CodecOf(kind).Write(content);
        return Add(kind, new DataSyncWireRecord([Hex(key), .. aliases.Select(Hex)], PeerNode, ++_seq, vv ?? Vv((Peer, 1)),
            MergeFixture.PeerEditor, false, 1, orderKey, json, ContentHash.Of(json), null, 0));
    }

    /// <summary>A record with raw content, for kinds without a codec and for content a codec holds.</summary>
    public DataSyncWireRecord PullRaw(int key, JsonObject json, string kind, int schemaVersion = 1)
    {
        return Add(kind, new DataSyncWireRecord([Hex(key)], PeerNode, ++_seq, Vv((Peer, 1)), MergeFixture.PeerEditor,
            false, schemaVersion, null, json, ContentHash.Of(json), null, 0));
    }

    public DataSyncWireRecord PullHeldAtSource(int key, string kind = ItemKind) =>
        Add(kind, new DataSyncWireRecord([Hex(key)], PeerNode, ++_seq, Vv((Peer, 1)), null, false, 1, null, null, null,
            DataSyncHeldReason.TooLarge, 0));

    public DataSyncWireRecord PullTombstone(int key, string kind = ItemKind) =>
        Add(kind, new DataSyncWireRecord([Hex(key)], PeerNode, ++_seq, Vv((Peer, 2)), null, true, 1, null, null, null,
            null, 0));

    private DataSyncWireRecord Add(string kind, DataSyncWireRecord record)
    {
        if (!Records.TryGetValue(kind, out var list)) Records[kind] = list = [];
        list.Add(record);
        return record;
    }

    // ---- the input ---------------------------------------------------------------------------------

    public DataSyncStagedPull Staged()
    {
        var kinds = new List<DataSyncStagedKind>();
        var manifestKinds = new List<DataSyncFeedKind>();
        foreach (var (kind, records) in Records.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var codec = Codecs.GetValueOrDefault(kind);
            var supported = codec is not null && !Unsupported.Contains(kind);
            var entities = records.Select((r, i) => DataSyncRecordValidation.Stage(supported ? codec : null, r, i, Limits))
                .ToList();
            var maxSeq = records.Max(r => r.Seq);
            kinds.Add(new DataSyncStagedKind(kind, 1, supported, KindHeld.TryGetValue(kind, out var held) ? held : null,
                entities, maxSeq, true));
            manifestKinds.Add(new DataSyncFeedKind(kind, 1, maxSeq, 0, records.Count(r => !r.Deleted),
                records.Count(r => r.Deleted), DataSyncWireFormat.KindContentHash(records), 0, records.Count, false));
        }

        var manifest = new DataSyncFeedManifest("snap", 120_000, SourceNodeId, "epoch", Peer.Value,
            DataSyncContract.Version, DataSyncContract.MinimumPeerVersion, "1.0.0", manifestKinds, null,
            new DataSyncSourceAttention(false, 0, 0, false, 0));
        return new DataSyncStagedPull(PeerNode, "PC-1", manifest, kinds,
            new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc));
    }

    public IReadOnlyDictionary<string, DataSyncLocalKindState> LocalState() =>
        Entities.Keys.Union(Tombstones.Keys).Distinct(StringComparer.Ordinal).ToDictionary(k => k,
            k => new DataSyncLocalKindState(k, Entities.GetValueOrDefault(k) ?? [], Tombstones.GetValueOrDefault(k) ?? []),
            StringComparer.Ordinal);

    public DataSyncPlanInput Input() =>
        DataSyncPlanInput.FromLocalState(Staged(), LocalState(), new Dictionary<string, IDataSyncKindCodec>(Codecs),
            OwnNodeId, Mode);

    public DataSyncPlan Plan() => DataSyncPlanner.Plan(Input());

    public ResolveResult Resolve(DataSyncPlan plan, IReadOnlyList<DataSyncPlanDecision> decisions, bool strict) =>
        DataSyncPlanner.Resolve(plan, Input(), decisions, strict);

    // ---- reading a plan ----------------------------------------------------------------------------

    public static DataSyncPlanItem Item(DataSyncPlan plan, int key, string kind = ItemKind) =>
        plan.Kinds.Single(s => s.Kind == kind).Items.Single(i => i.ItemId == $"{kind}/k/{Hex(key)}");

    public static DataSyncPlanKindSection Section(DataSyncPlan plan, string kind = ItemKind) =>
        plan.Kinds.Single(s => s.Kind == kind);

    /// <summary>The item's default made explicit, or a decision with the given resolution and target.</summary>
    public static DataSyncPlanDecision Decide(DataSyncPlanItem item, DataSyncPlanResolution? resolution = null,
        string? target = null, IReadOnlyList<string>? excluded = null, string? newName = null, string? token = null)
    {
        resolution ??= item.DefaultResolution ?? throw new InvalidOperationException($"{item.ItemId} has no default.");
        if (resolution is DataSyncPlanResolution.Link or DataSyncPlanResolution.Update)
            target ??= item.DefaultTargetLocalKey ?? item.Candidates.FirstOrDefault()?.LocalKey;
        token ??= item.Candidates.FirstOrDefault(c => c.LocalKey == target)?.ReviewToken ?? item.ReviewToken;
        return new DataSyncPlanDecision(item.ItemId, resolution.Value, target, newName, excluded ?? [], token);
    }
}

/// <summary>
/// A codec that delegates to another and lets a test rename the kind, declare dependencies, rewrite diffs and see
/// every accepted set a merge received.
/// </summary>
internal sealed class DecoratedCodec(IDataSyncKindCodec inner) : IDataSyncKindCodec
{
    public DataSyncKindDescriptor? DescriptorOverride { get; init; }
    public Func<EntityDiff, EntityDiff>? OnDiff { get; init; }
    public List<IReadOnlySet<string>> Accepted { get; } = [];

    public DataSyncKindDescriptor Descriptor => DescriptorOverride ?? inner.Descriptor;
    public int ComparisonFormVersion => inner.ComparisonFormVersion;
    public JsonObject Upgrade(JsonObject content, int fromSchemaVersion) => inner.Upgrade(content, fromSchemaVersion);
    public CodecReadResult Read(JsonObject content, DataSyncLimits limits) => inner.Read(content, limits);
    public object ReadLocal(JsonObject content) => inner.ReadLocal(content);
    public JsonObject Write(object content) => inner.Write(content);
    public string NameOf(object content) => inner.NameOf(content);
    public string? SubtypeOf(object content) => inner.SubtypeOf(content);
    public int ChildCountOf(object content) => inner.ChildCountOf(content);
    public DataSyncNaturalMatch MatchNatural(object incoming, object local) => inner.MatchNatural(incoming, local);

    public EntityDiff Diff(object local, object incoming)
    {
        var diff = inner.Diff(local, incoming);
        return OnDiff?.Invoke(diff) ?? diff;
    }

    public MergeResult Merge(object local, object incoming, IReadOnlySet<string> acceptedChangeIds)
    {
        Accepted.Add(acceptedChangeIds);
        return inner.Merge(local, incoming, acceptedChangeIds);
    }

    public MergeResult PrepareCreate(object incoming, string? nameOverride) => inner.PrepareCreate(incoming, nameOverride);

    public DataSyncPublishable Publish(object localContent, DataSyncOverlay overlay, bool childrenLocal) =>
        inner.Publish(localContent, overlay, childrenLocal);

    public JsonObject ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal) =>
        inner.ComparisonForm(publishedContent, orderKey, childrenLocal);

    public IReadOnlyList<string> ChildDeletionCandidates(DataSyncChildCandidatesInput input) =>
        inner.ChildDeletionCandidates(input);

    public DataSyncMerge3Result Merge3(DataSyncMerge3Input input) => inner.Merge3(input);
    public IReadOnlyList<DataSyncChildInfo> ChildrenOf(object content) => inner.ChildrenOf(content);
}

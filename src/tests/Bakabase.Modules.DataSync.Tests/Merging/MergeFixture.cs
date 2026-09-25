using System.Globalization;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Merging;

/// <summary>
/// Builds one link's merge input around the test kind (and extension groups): local entities and tombstones, bases
/// with pending records, the pull's records (staged through the real validation) and the link's context.
/// </summary>
internal sealed class MergeFixture
{
    public static readonly DataSyncActorId Self = Actor(0x5e1f);
    public static readonly DataSyncActorId Peer = Actor(0xbee);
    public static readonly DataSyncActorId Third = Actor(0x3d);
    public static readonly DataSyncActorId Retired = Actor(0x01d);
    public const string SelfNode = "self-node";
    public const string PeerNode = "peer-node";
    public const string ThirdNode = "third-node";
    public const int LinkId = 7;

    public static readonly DataSyncEditorRef SelfEditor = new(SelfNode, "This PC", Self.Value);
    public static readonly DataSyncEditorRef PeerEditor = new(PeerNode, "PC-1", Peer.Value);
    public static readonly DataSyncEditorRef ThirdEditor = new(ThirdNode, "PC-2", Third.Value);

    public static readonly IDataSyncKindCodec Items = TestItemCodec.Instance;
    public static readonly IDataSyncKindCodec Groups = ExtensionGroupCodec.Instance;
    public const string ItemKind = TestItemCodec.Kind;
    public const string GroupKind = DataSyncKindIds.ExtensionGroup;
    public static readonly IDataSyncKindCodec CustomProperties = CustomPropertyCodec.Instance;
    public const string CustomPropertyKind = DataSyncKindIds.CustomProperty;

    public readonly Dictionary<string, List<DataSyncLocalEntityState>> Entities = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<DataSyncTombstoneState>> Tombstones = new(StringComparer.Ordinal);
    public readonly Dictionary<(string Kind, SyncKey Key), DataSyncPeerBase> Bases = new();
    public readonly Dictionary<string, List<DataSyncWireRecord>> Incoming = new(StringComparer.Ordinal);
    public readonly List<(string Kind, SyncKey Key)> PendingToMerge = [];
    public readonly List<DataSyncOpenInboxItem> OpenItems = [];
    public readonly Dictionary<(string Kind, string LocalKey), IReadOnlyDictionary<string, int>> Usage = new();
    public readonly Dictionary<(string Kind, string LocalKey), int> ValueCounts = new();
    public readonly Dictionary<string, long> RetiredCounters = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> LiveCounts = new(StringComparer.Ordinal);
    public readonly HashSet<string> FullReconciliation = new(StringComparer.Ordinal);
    public readonly List<string> FirstContactKinds = [];

    public DataSyncLinkMode Mode = DataSyncLinkMode.TwoWay;
    public DataSyncLinkMode? EffectiveMode;
    public DataSyncMergeFlags LinkFlags = DataSyncMergeFlags.None;
    public long ActorCounter = 1_000;
    public string? PeerActorId = Peer.Value;
    public int? PeerComparisonFormVersion = 1;
    public DataSyncAutoApplyPolicy Policy = DataSyncAutoApplyPolicy.Default;
    public DataSyncLimits Limits = DataSyncLimits.Default;
    public bool NoPull;
    public long Seq = 500;

    internal static DataSyncActorId Actor(int i) => new(i.ToString("x16", CultureInfo.InvariantCulture));

    public static SyncKey K(int i) => i == 0
        ? throw new ArgumentOutOfRangeException(nameof(i), "0 is the link-level sentinel.")
        : new SyncKey(i.ToString("x32", CultureInfo.InvariantCulture));

    public static DataSyncVersionVector Vv(params (DataSyncActorId Actor, long Counter)[] counters) =>
        counters.Aggregate(DataSyncVersionVector.Empty, (v, c) => v.With(c.Actor, c.Counter));

    public static TestItemContent T(string name, params (string Id, string Label)[] children) =>
        new(name, null, children.Select(c => new TestChild(c.Id, c.Label)));

    public static TestItemContent T(string name, string? color, string? type, params (string Id, string Label)[] children) =>
        new(name, color, children.Select(c => new TestChild(c.Id, c.Label)), type);

    public static IDataSyncKindCodec CodecOf(string kind) =>
        kind == GroupKind ? Groups : kind == CustomPropertyKind ? CustomProperties : Items;

    // ---- local state -----------------------------------------------------------------------------

    public DataSyncLocalEntityState Local(string localKey, SyncKey key, object content, DataSyncVersionVector vv,
        DataSyncEditorRef? lastEditor = null, string? orderKey = null,
        DataSyncEntitySyncState state = DataSyncEntitySyncState.Synced, DataSyncOverlay? overlay = null,
        bool createdBySync = false, bool publishHeld = false, JsonObject? unknown = null, int? valueCount = null,
        long seq = 10, bool unreadable = false, string kind = ItemKind, IEnumerable<SyncKey>? aliases = null,
        bool childrenLocal = false, bool openItemAnyLink = false, bool pendingRecordAnyLink = false)
    {
        var codec = CodecOf(kind);
        overlay ??= DataSyncOverlay.None;
        lastEditor ??= SelfEditor;
        var publication = DataSyncPublication.Of(codec, content, overlay, childrenLocal, orderKey, unknown);
        var entity = new DataSyncLocalEntityState(localKey, new EntityKeys([key, .. aliases ?? []]), content,
            ContentHash.Of(codec.Write(content)), publication.SharedHash ?? "", vv, new DataSyncActorId(lastEditor.ActorId),
            lastEditor, orderKey, state, overlay, childrenLocal, createdBySync, publishHeld, unknown, valueCount, seq,
            unreadable, openItemAnyLink, pendingRecordAnyLink);
        EntitiesOf(kind).Add(entity);
        return entity;
    }

    public DataSyncTombstoneState Tombstone(SyncKey key, DataSyncVersionVector vv,
        DataSyncTombstoneKind tombstoneKind = DataSyncTombstoneKind.Deleted, bool served = true,
        DataSyncEntitySyncState stateAtDeletion = DataSyncEntitySyncState.Synced, long seq = 20, string kind = ItemKind,
        IEnumerable<SyncKey>? aliases = null)
    {
        var tombstone = new DataSyncTombstoneState(new EntityKeys([key, .. aliases ?? []]), vv, SelfEditor, stateAtDeletion,
            tombstoneKind, served, seq);
        if (!Tombstones.TryGetValue(kind, out var list)) Tombstones[kind] = list = [];
        list.Add(tombstone);
        return tombstone;
    }

    public List<DataSyncLocalEntityState> EntitiesOf(string kind)
    {
        if (!Entities.TryGetValue(kind, out var list)) Entities[kind] = list = [];
        return list;
    }

    // ---- records and bases ------------------------------------------------------------------------

    /// <param name="childrenLocal">Publish the content as "sync the definition only" (§3.6): no children, the flag set.</param>
    public DataSyncWireRecord Record(SyncKey key, object? content, DataSyncVersionVector vv,
        DataSyncEditorRef? editedBy = null, bool deleted = false, string? orderKey = null, long? seq = null,
        IEnumerable<SyncKey>? aliases = null, string kind = ItemKind, int schemaVersion = 1, string origin = PeerNode,
        JsonObject? unknown = null, bool childrenLocal = false)
    {
        JsonObject? json = null;
        if (content is not null && !deleted)
        {
            json = childrenLocal
                ? (JsonObject)DataSyncPublication.Of(CodecOf(kind), content, DataSyncOverlay.None, true, orderKey, null).Content!
                    .DeepClone()
                : CodecOf(kind).Write(content);
            foreach (var (name, value) in unknown ?? []) json[name] = value?.DeepClone();
        }

        return new DataSyncWireRecord([key.Value, .. (aliases ?? []).Select(a => a.Value)], origin, seq ?? ++Seq, vv,
            editedBy ?? PeerEditor, deleted, schemaVersion, orderKey, json, json is null ? null : ContentHash.Of(json),
            null, 0);
    }

    /// <summary>Adds a record to the pull.</summary>
    public DataSyncWireRecord Pull(DataSyncWireRecord record, string kind = ItemKind)
    {
        if (!Incoming.TryGetValue(kind, out var list)) Incoming[kind] = list = [];
        list.Add(record);
        return record;
    }

    public DataSyncPeerBase Base(SyncKey key, DataSyncWireRecord? record,
        DataSyncBaseState state = DataSyncBaseState.Normal, IReadOnlyDictionary<string, string>? childMap = null,
        DataSyncPendingRecord? pending = null, DataSyncExclusionReason? exclusion = null,
        IReadOnlyList<string>? exclusionKeys = null, string kind = ItemKind)
    {
        var b = new DataSyncPeerBase(kind, key, state, exclusion, record?.Vv, childMap ?? new Dictionary<string, string>(),
            pending, record, exclusionKeys ?? []);
        Bases[(kind, key)] = b;
        return b;
    }

    public static DataSyncPendingRecord PendingOf(DataSyncWireRecord record, DataSyncPendingReason reason,
        long evaluatedAt = 10, DataSyncMergeFlags? flags = null) =>
        DataSyncPendingRecords.Create(record, reason, evaluatedAt, flags ?? DataSyncMergeFlags.None);

    public void OpenItem(long id, SyncKey key, DataSyncInboxItemType type, string subject = "",
        DataSyncVersionVector? recordVv = null, string kind = ItemKind) =>
        OpenItems.Add(new DataSyncOpenInboxItem(id, LinkId, kind, key, type, DataSyncInboxDrafts.OriginOf(type), subject,
            "token", recordVv));

    // ---- the input ---------------------------------------------------------------------------------

    public DataSyncLinkContext Link() => new(LinkId, PeerNode, "PC-1", Mode, EffectiveMode ?? Mode,
        [ItemKind, GroupKind, CustomPropertyKind], FirstContactKinds, false, Self,
        new Dictionary<string, long>(RetiredCounters) { [Self.Value] = ActorCounter }, PeerActorId,
        PeerComparisonFormVersion is { } v
            ? new Dictionary<string, int> { [ItemKind] = v, [GroupKind] = v, [CustomPropertyKind] = v }
            : new Dictionary<string, int>(),
        LinkFlags);

    public DataSyncStagedPull? StagedPull()
    {
        if (NoPull) return null;
        var kinds = new List<DataSyncStagedKind>();
        var manifestKinds = new List<DataSyncFeedKind>();
        foreach (var kind in Incoming.Keys.Union(LiveCounts.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal))
        {
            var records = Incoming.GetValueOrDefault(kind) ?? [];
            var codec = CodecOf(kind);
            var entities = records.Select((r, i) => DataSyncRecordValidation.Stage(codec, r, i, Limits)).ToList();
            var maxSeq = records.Count == 0 ? Seq : records.Max(r => r.Seq);
            kinds.Add(new DataSyncStagedKind(kind, codec.Descriptor.SchemaVersion, true, null, entities, maxSeq,
                FullReconciliation.Contains(kind)));
            // The manifest's LiveCount is the source's total, not this pull's: a large default unless a test sets it.
            var live = LiveCounts.TryGetValue(kind, out var count) ? count : 1_000;
            manifestKinds.Add(new DataSyncFeedKind(kind, 1, maxSeq, 0, live, records.Count(r => r.Deleted),
                DataSyncWireFormat.KindContentHash(records), 0, records.Count, false));
        }

        var manifest = new DataSyncFeedManifest("snap", 120_000, PeerNode, "epoch", Peer.Value, DataSyncContract.Version,
            DataSyncContract.MinimumPeerVersion, "1.0.0", manifestKinds, null,
            new DataSyncSourceAttention(false, 0, 0, false, 0));
        return new DataSyncStagedPull(PeerNode, "PC-1", manifest, kinds, new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>The codec the merger is given for the test kind (records are staged with the test kind's own).</summary>
    public IDataSyncKindCodec ItemCodec = Items;

    public DataSyncMergeInput Input() => new(Link(), StagedPull(),
        Entities.Keys.Union(Tombstones.Keys).Distinct().ToDictionary(k => k,
            k => new DataSyncLocalKindState(k, Entities.GetValueOrDefault(k) ?? [], Tombstones.GetValueOrDefault(k) ?? [])),
        new Dictionary<(string, SyncKey), DataSyncPeerBase>(Bases), PendingToMerge.ToList(),
        new Dictionary<string, IDataSyncKindCodec>
            { [ItemKind] = ItemCodec, [GroupKind] = Groups, [CustomPropertyKind] = CustomProperties },
        new Dictionary<(string, string), IReadOnlyDictionary<string, int>>(Usage),
        new Dictionary<(string, string), int>(ValueCounts), OpenItems.ToList(), Policy, Limits,
        OpenStateItems.Count == 0 ? null : OpenStateItems.ToList());

    /// <summary>This link's open state-derived items (B8 counts them, §8.7).</summary>
    public readonly List<DataSyncOpenInboxItem> OpenStateItems = [];

    /// <summary>
    /// Merges, and checks what every merge must hold whatever the case: one draft per subject
    /// <c>(kind, key, type, subjectPath)</c> — a store keeps one open item per subject (§4.2) — and one update per
    /// base row.
    /// </summary>
    public DataSyncMergeResult Merge()
    {
        var result = DataSyncMerger.Merge(Input());
        AssertWellFormed(result);
        return result;
    }

    public static void AssertWellFormed(DataSyncMergeResult result)
    {
        foreach (var subject in result.Inbox.GroupBy(d => (d.Kind, d.Key, d.Type, d.SubjectPath)).Where(g => g.Count() > 1))
        {
            throw new Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException(
                $"{subject.Count()} drafts of one subject {subject.Key.Type} {subject.Key.Kind}/{subject.Key.Key.Value} " +
                $"'{subject.Key.SubjectPath}'");
        }

        foreach (var row in result.BaseUpdates.GroupBy(u => (u.Kind, u.Key)).Where(g => g.Count() > 1))
        {
            throw new Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException(
                $"{row.Count()} updates of one base row {row.Key.Kind}/{row.Key.Key.Value}");
        }
    }

    /// <summary>Every child of every local entity used by <paramref name="count"/> resources (0 = unused).</summary>
    public void UseAllChildren(int count)
    {
        foreach (var (kind, list) in Entities)
        {
            foreach (var entity in list)
            {
                Usage[(kind, entity.LocalKey)] = CodecOf(kind).ChildrenOf(entity.Content)
                    .ToDictionary(c => c.Id, _ => count);
            }
        }
    }
}

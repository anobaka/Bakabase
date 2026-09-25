using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>A clock the scenarios move by hand (the lost-update window, retention, the unverified window).</summary>
internal sealed class SimClock
{
    public DateTime Now { get; private set; } = new(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// The world the nodes live in: the clock, the network (which pairs are partitioned), and the counters that stand
/// in for randomness a real install draws itself (sync keys, option uuids, actor salts), so a seed replays exactly.
/// </summary>
internal sealed class SimWorld
{
    private int _nextLinkId;
    private long _nextKey;
    private long _nextChild;
    private readonly Random _salts;

    public SimWorld(int seed = 0)
    {
        Seed = seed;
        _salts = new Random(seed ^ 0x5a17);
    }

    public int Seed { get; }
    public SimClock Clock { get; } = new();
    public List<SimNode> Nodes { get; } = [];

    /// <summary>Directed pairs (reader node id, source node id) that cannot reach each other.</summary>
    public HashSet<(string Reader, string Source)> Down { get; } = [];

    public List<string> Trace { get; } = [];

    /// <summary>How often each engine path ran (coverage of the simulator's runs).</summary>
    public Dictionary<string, int> Counters { get; } = new(StringComparer.Ordinal);

    public void Count(string what) => Counters[what] = Counters.GetValueOrDefault(what) + 1;

    public SimNode AddNode(string name, bool headless = false)
    {
        var node = new SimNode(this, name, headless);
        Nodes.Add(node);
        return node;
    }

    public SimNode Node(string nodeId) => Nodes.Single(n => n.NodeId == nodeId);

    public bool Reachable(SimNode reader, SimNode source) => !Down.Contains((reader.NodeId, source.NodeId));

    public int NextLinkId() => ++_nextLinkId;

    /// <summary>A fresh 32-hex sync key (never the link-level sentinel), unique across the world and across restores.</summary>
    public SyncKey NewKey()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("key/" + (++_nextKey).ToString(CultureInfo.InvariantCulture)));
        var key = Convert.ToHexStringLower(hash)[..32];
        return new SyncKey(key == new string('0', 32) ? "1" + key[1..] : key);
    }

    /// <summary>A fresh child id (an option uuid stand-in), unique across the world.</summary>
    public string NewChildId() => "c" + (++_nextChild).ToString(CultureInfo.InvariantCulture);

    /// <summary>A fresh actor salt (16 hex), drawn from the seed.</summary>
    public string NewSalt()
    {
        var bytes = new byte[8];
        _salts.NextBytes(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    public void Log(string line) => Trace.Add($"[{Clock.Now:MM-dd HH:mm}] {line}");
}

/// <summary>
/// One definition and its side row (live or tombstone), as the service [B/C] and the store [C] keep them.
/// <see cref="Content"/> is the service's row (null once deleted); the rest is the side row.
/// </summary>
internal sealed class SimRow
{
    public required string Kind { get; init; }
    public required string LocalKey { get; set; }
    public List<SyncKey> Keys { get; set; } = [];
    public object? Content { get; set; }
    public string LocalHash { get; set; } = "";
    public string? SharedHash { get; set; }
    public DataSyncVersionVector Vv { get; set; } = DataSyncVersionVector.Empty;
    public DataSyncEditorRef? LastEditor { get; set; }
    public string? OrderKey { get; set; }
    public DataSyncEntitySyncState State { get; set; } = DataSyncEntitySyncState.Synced;
    public DataSyncOverlay Overlay { get; set; } = DataSyncOverlay.None;
    public bool CreatedBySync { get; set; }
    public bool PublishHeld { get; set; }
    public JsonObject? Unknown { get; set; }
    public long Seq { get; set; }
    public string Origin { get; set; } = "";
    public bool Deleted { get; set; }
    public DataSyncTombstoneKind TombstoneKind { get; set; } = DataSyncTombstoneKind.Deleted;
    public DataSyncEntitySyncState StateAtDeletion { get; set; } = DataSyncEntitySyncState.Synced;
    public bool Served { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>The service deleted the definition; Refresh has not tombstoned the side row yet.</summary>
    public bool DeletedLocally { get; set; }

    /// <summary>The most recent covered apply of this entity (§6.5): when, and what it changed.</summary>
    public (DateTime At, DataSyncEntityChangeList Changes)? LastApply { get; set; }

    /// <summary>The content before that apply: what a whole-row writer that read it earlier writes back (§6.5).</summary>
    public object? PreApplyContent { get; set; }

    public SyncKey Primary => Keys[0];
    public bool HasSideRow => Keys.Count > 0;
    public bool IsLive => !Deleted && !DeletedLocally;
    public TestItemContent? Item => Content as TestItemContent;
    public string Name => Content is null ? "" : SimKinds.Of(Kind).NameOf(Content);

    public SimRow Clone()
    {
        var clone = (SimRow)MemberwiseClone();
        clone.Keys = [.. Keys];
        return clone;
    }

    public override string ToString() => $"{Kind}:{LocalKey} {(Deleted ? "†" : "")}{Content} {Vv}";
}

/// <summary>This device's link to one peer (DataSyncLinks + its bases and pending records).</summary>
internal sealed class SimLink
{
    public required int Id { get; init; }
    public required SimNode Peer { get; init; }
    public DataSyncLinkMode Mode { get; set; }
    public DataSyncLinkMode LastMode { get; set; }
    public DataSyncPauseReason? Paused { get; set; }
    public string? PausedDetail { get; set; }
    public Dictionary<string, long> Cursors { get; private set; } = new(StringComparer.Ordinal);
    public Dictionary<(string Kind, SyncKey Key), DataSyncPeerBase> Bases { get; private set; } = new();
    public DataSyncMergeFlags OnceFlags { get; set; } = DataSyncMergeFlags.None;
    public HashSet<string> CompletedKinds { get; private set; } = new(StringComparer.Ordinal);
    public DataSyncSourceAttention? PeerAttention { get; set; }
    public string? PeerEpoch { get; set; }

    /// <summary>The peer's current actor as its last head named it (§8.4 row A2).</summary>
    public string? PeerActorId { get; set; }
    public DateTime LastFullReconciliation { get; set; } = DateTime.MinValue;
    public DateTime LastSuccess { get; set; } = DateTime.MinValue;

    /// <summary>A restore choice <c>OthersWin</c> picked this link for its next cycle (§9.5).</summary>
    public bool OthersWinNext { get; set; }

    /// <summary>Turned on again after a stop: every pending record is re-merged with the next pull (§8.1).</summary>
    public bool RemergeAllPending { get; set; }

    public bool Stopped => Mode == DataSyncLinkMode.Off;

    public SimLink Clone()
    {
        var clone = (SimLink)MemberwiseClone();
        clone.Cursors = new Dictionary<string, long>(Cursors, StringComparer.Ordinal);
        clone.Bases = new Dictionary<(string, SyncKey), DataSyncPeerBase>(Bases);
        clone.CompletedKinds = new HashSet<string>(CompletedKinds, StringComparer.Ordinal);
        return clone;
    }

    public override string ToString() => $"link {Id} → {Peer.Name} ({Mode}{(Paused is { } p ? ", paused " + p : "")})";
}

internal sealed class SimItem
{
    public required long Id { get; init; }
    public int? LinkId { get; set; }
    public required string Kind { get; init; }
    public required SyncKey Key { get; set; }
    public string? LocalKey { get; set; }
    public required DataSyncInboxItemType Type { get; init; }
    public required DataSyncInboxItemOrigin Origin { get; init; }
    public required string Subject { get; init; }
    public required DataSyncInboxPayload Payload { get; set; }
    public string? RecordHash { get; set; }
    public DataSyncVersionVector? RecordVv { get; set; }
    public required string Token { get; set; }
    public DataSyncMergeFlags Flags { get; set; } = DataSyncMergeFlags.None;
    public DataSyncInboxClosure? Closure { get; set; }
    public DataSyncEditorRef? ClosedBy { get; set; }
    public DateTime CreatedAt { get; init; }
    public bool Open => Closure is null;

    public SimItem Clone() => (SimItem)MemberwiseClone();

    public override string ToString() => $"#{Id} {Type} {Kind}/{Key.Value[..6]} '{Subject}' link {LinkId}{(Open ? "" : " " + Closure)}";
}

/// <summary>One change a history entry records for one entity (§8.11 pre-images, version 2).</summary>
internal sealed record SimHistoryChange(string Kind, SyncKey Primary, string Action, object? Before, object? After,
    int? LinkId, IReadOnlyList<SyncKey> AliasesAdded);

internal sealed class SimHistoryEntry
{
    public required long Id { get; init; }
    public required DataSyncHistoryKind Kind { get; init; }
    public required DateTime At { get; init; }
    public List<SimHistoryChange> Changes { get; } = [];
    public bool Undone { get; set; }

    public SimHistoryEntry Clone()
    {
        var clone = new SimHistoryEntry { Id = Id, Kind = Kind, At = At, Undone = Undone };
        clone.Changes.AddRange(Changes);
        return clone;
    }
}

/// <summary>DataSyncLocalStates (§4.1): the actor, its counter, retired actors, Seq, the restore state, floors.</summary>
internal sealed class SimLocalState
{
    public string Salt { get; set; } = "";
    public int Generation { get; set; } = 1;
    public string ActorId { get; set; } = "";
    public long ActorCounter { get; set; }
    public Dictionary<string, long> RetiredActors { get; private set; } = new(StringComparer.Ordinal);
    public string DbInstanceId { get; set; } = "";
    public long LastSeq { get; set; }
    public DataSyncPauseReason? RestoreReason { get; set; }
    public int? RestoreLinkId { get; set; }
    public List<DataSyncRestoreEvidence> Evidence { get; private set; } = [];
    public Dictionary<string, long> TombstoneFloors { get; private set; } = new(StringComparer.Ordinal);

    /// <summary>Readers recorded as ahead (§7.5.1 step 1); settled once they read with every cursor ≤ LastSeq.</summary>
    public Dictionary<string, bool> ReadersAhead { get; private set; } = new(StringComparer.Ordinal);

    public SimLocalState Clone()
    {
        var clone = (SimLocalState)MemberwiseClone();
        clone.RetiredActors = new Dictionary<string, long>(RetiredActors, StringComparer.Ordinal);
        clone.Evidence = [.. Evidence];
        clone.TombstoneFloors = new Dictionary<string, long>(TombstoneFloors, StringComparer.Ordinal);
        clone.ReadersAhead = new Dictionary<string, bool>(ReadersAhead, StringComparer.Ordinal);
        return clone;
    }
}

/// <summary>
/// Everything one install keeps in its database: definitions and values (the service), side rows, links with
/// bases and pending records, the inbox, history and the local state. Restoring a database swaps this whole
/// object; <c>actor.json</c> lives outside it (<see cref="SimWatermark"/>).
/// </summary>
internal sealed class SimDb
{
    public List<SimRow> Rows { get; private set; } = [];

    /// <summary>Local order per kind with order (the service's integer <c>Order</c>).</summary>
    public Dictionary<string, List<string>> Order { get; private set; } = new(StringComparer.Ordinal);

    public Dictionary<string, SimLink> Links { get; private set; } = new(StringComparer.Ordinal);
    public List<SimItem> Items { get; private set; } = [];
    public Dictionary<(string Kind, string LocalKey), Dictionary<string, int>> Usage { get; private set; } = new();
    public Dictionary<(string Kind, string LocalKey), int> Values { get; private set; } = new();
    public List<SimHistoryEntry> History { get; private set; } = [];
    public SimLocalState Local { get; private set; } = new();
    public int NextLocalKey { get; set; }
    public long NextItemId { get; set; }
    public long NextHistoryId { get; set; }

    public SimDb Clone()
    {
        var clone = (SimDb)MemberwiseClone();
        clone.Rows = Rows.Select(r => r.Clone()).ToList();
        clone.Order = Order.ToDictionary(o => o.Key, o => o.Value.ToList(), StringComparer.Ordinal);
        clone.Links = Links.ToDictionary(l => l.Key, l => l.Value.Clone(), StringComparer.Ordinal);
        clone.Items = Items.Select(i => i.Clone()).ToList();
        clone.Usage = Usage.ToDictionary(u => u.Key, u => new Dictionary<string, int>(u.Value, StringComparer.Ordinal));
        clone.Values = new Dictionary<(string, string), int>(Values);
        clone.History = History.Select(h => h.Clone()).ToList();
        clone.Local = Local.Clone();
        return clone;
    }
}

/// <summary><c>actor.json</c> (§4.7): outside the database, so a database restored alone is caught by it.</summary>
internal sealed record SimWatermark(int Generation, string ActorId, long Counter, string DbInstanceId);

/// <summary>A desktop notification (§9.4); headless nodes create none.</summary>
internal sealed class SimNotification
{
    public required string Source { get; init; }
    public required string Case { get; init; }
    public required DateTime At { get; init; }
    public List<long> ItemIds { get; } = [];
    public bool Read { get; set; }

    /// <summary>A prompt asks a person to decide (new items, a pause, a restore), unlike a follow-override note.</summary>
    public bool IsPrompt => Case is "newItems" or "paused" or "restore";
}

/// <summary>The kinds the simulator runs, in apply order.</summary>
internal static class SimKinds
{
    public static IReadOnlyList<SimKind> All { get; } = [ExtensionGroupSimKind.Instance, TestItemSimKind.Instance];

    public static SimKind Of(string kind) => All.First(k => k.Kind == kind);

    public static IReadOnlyDictionary<string, IDataSyncKindCodec> Codecs { get; } =
        All.ToDictionary(k => k.Kind, k => k.Codec, StringComparer.Ordinal);

    public static IReadOnlyList<string> Ids { get; } = All.Select(k => k.Kind).ToList();

    public static DataSyncLimits Limits { get; } = DataSyncLimits.Default;
}

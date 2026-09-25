using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

// §2.7: the continuous merger's records.

public sealed record DataSyncLinkContext(int LinkId, string PeerNodeId, string PeerName,
    DataSyncLinkMode Mode,                                  // as the user set it
    DataSyncLinkMode EffectiveMode,                         // TwoWay when both devices follow each other (§8.1)
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> FirstContactKinds,                // kinds in their first pull on this link: B5 is skipped (§8.7)
    bool IsHeadless, DataSyncActorId SelfActor,
    IReadOnlyDictionary<string, long> OwnActorCounters,     // current actor → ActorCounter; retired actors → recorded counter (§5.6)
    string? PeerActorId,                                    // head.ActorId of this pull (§8.4 row A2)
    IReadOnlyDictionary<string, int> PeerComparisonFormVersions,   // head.Kinds[k].ComparisonFormVersion (§8.4 row A2)
    DataSyncMergeFlags LinkFlags);                          // once flags of the link (§8.7), consumed by this pull

public sealed record DataSyncAutoApplyPolicy
{
    public int MaxEntityDeletionsPerPull { get; init; } = 10;       // B2, new deletions only (§8.7)
    public double MaxEntityDeletionRatio { get; init; } = 0.2;      // B2, of the kind's based entities…
    public int MinEntitiesForRatio { get; init; } = 20;             // …only when it has at least this many
    public int MinBasesForKindEmptied { get; init; } = 3;           // B3
    public int MaxChildDeletionsPerEntity { get; init; } = 50;      // B4
    public double MaxChildDeletionRatio { get; init; } = 0.2;       // B4, only when the entity has ≥ MinChildrenForRatio children
    public int MinChildrenForRatio { get; init; } = 10;
    public int MaxUpdatedEntitiesPerPull { get; init; } = 50;       // B5
    public int MaxCreatedEntitiesPerPull { get; init; } = 50;       // B5
    public static DataSyncAutoApplyPolicy Default { get; } = new();
}

public sealed record DataSyncMergeInput(
    DataSyncLinkContext Link,
    DataSyncStagedPull? Incoming,                                                // null: re-merge pending records only
    IReadOnlyDictionary<string, DataSyncLocalKindState> Local,
    IReadOnlyDictionary<(string Kind, SyncKey Key), DataSyncPeerBase> Bases,   // this link's bases, by base key (§4.1)
    IReadOnlyList<(string Kind, SyncKey Key)> PendingToMerge,                   // pending records re-merged this time (§8.4)
    IReadOnlyDictionary<string, IDataSyncKindCodec> Codecs,
    IReadOnlyDictionary<(string Kind, string LocalKey), IReadOnlyDictionary<string, int>> ChildUsage,
    IReadOnlyDictionary<(string Kind, string LocalKey), int> ValueCounts,
    IReadOnlyList<DataSyncOpenInboxItem> OpenItems,                              // this link's open merger-derived items
    DataSyncAutoApplyPolicy Policy,
    DataSyncLimits Limits);

public sealed record DataSyncUsageQuery(string Kind, string LocalKey, IReadOnlyList<string> ChildIds, bool NeedValueCount);

public sealed record DataSyncMergeResult(
    DataSyncPauseReason? Pause, string? PauseDetail,        // a breaker tripped: nothing below is applied
    DataSyncAnomaly? Anomaly,                                // row A1/A2: the runner rolls back (§8.10.2)
    IReadOnlyList<ApplyBatch> Batches,                       // safe operations only
    IReadOnlyList<DataSyncRevisionDecision> Revisions,       // one per touched local entity or tombstone
    IReadOnlyList<DataSyncBaseUpdate> BaseUpdates,           // bases, exclusions, pending records set or cleared
    IReadOnlyList<DataSyncInboxDraft> Inbox,                 // the COMPLETE set of merger-derived items for evaluated entities,
                                                             // plus state-derived items this pull creates or refreshes
    IReadOnlyList<DataSyncOverlayChange> OverlayChanges,     // held children added/removed
    IReadOnlyList<DataSyncOrderAssignment> Order,            // per kind with order: synced local keys in shared order
    IReadOnlyDictionary<string, long> CursorAdvance,         // kind → snapshot MaxSeq, for every kind fully evaluated (§7.5.5)
    IReadOnlyList<DataSyncMergeNote> Notes,                  // history and notification facts (auto deletes, follow overrides…)
    IReadOnlyList<DataSyncClosureHint> ClosureHints,         // why merger-derived items of evaluated entities disappear (§9.3)
    IReadOnlyCollection<(string Kind, SyncKey Key)> Evaluated);

public sealed record DataSyncAnomaly(string Code /* "regression" | "duplicateActor" */, string ActorId, long SeenCounter,
    string Kind, SyncKey Key);

public static class DataSyncMerger
{
    /// <summary>
    /// Phase 1 (pure): what usage must be read before merging: ChildDeletionCandidates of every entity that may
    /// lose children, and value counts of entities the peer deleted.
    /// </summary>
    public static IReadOnlyList<DataSyncUsageQuery> CollectUsageQueries(DataSyncMergeInput input) =>
        throw new NotImplementedException();

    /// <summary>Phase 2 (pure, deterministic): §8.4–§8.8.</summary>
    public static DataSyncMergeResult Merge(DataSyncMergeInput input) => throw new NotImplementedException();
}

public sealed record DataSyncRevisionDecision(string Kind, EntityKeys Keys, string? LocalKey, DataSyncRevisionKind Revision,
    DataSyncVersionVector? RemoteVv, DataSyncVersionVector? TombstoneVv, bool ResultEqualsRemote, bool ResultEqualsLocal,
    string? OrderKey, JsonObject? Unknown, bool? ChildrenLocal, DataSyncEditorRef? AdoptEditor);

public sealed record DataSyncBaseUpdate(string Kind, SyncKey Key, DataSyncBaseState State, DataSyncExclusionReason? Exclusion,
    DataSyncWireRecord? Record, IReadOnlyDictionary<string, string>? ChildMap,
    DataSyncPendingRecord? Pending, bool ClearPending);

public sealed record DataSyncOverlayChange(string Kind, string LocalKey, IReadOnlyList<DataSyncHeldChild> Hold,
    IReadOnlyList<DataSyncHeldChild> Release);

public sealed record DataSyncOrderAssignment(string Kind, IReadOnlyList<(string LocalKey, string OrderKey)> Synced);

public sealed record DataSyncMergeNote(string Kind, string? LocalKey, string Name, string Code /* §9.4 */,
    IReadOnlyDictionary<string, string>? Args);

public sealed record DataSyncInboxDraft(string Kind /* "" for the link-level LargeChange item */,
    SyncKey Key /* SyncKey.LinkLevel for LargeChange */, string? LocalKey,
    DataSyncInboxItemType Type, DataSyncInboxItemOrigin Origin,
    string SubjectPath /* "" = whole entity; "largeChange" for link-level; else §8.5.1 path */, DataSyncInboxPayload Payload,
    string? RecordHash, DataSyncVersionVector? RecordVv, DataSyncVersionVector? LocalVv, DataSyncMergeFlags Flags,
    string Token);

public sealed record DataSyncOpenInboxItem(long Id, int? LinkId, string Kind, SyncKey Key, DataSyncInboxItemType Type,
    DataSyncInboxItemOrigin Origin, string SubjectPath, string Token, DataSyncVersionVector? RecordVv);

/// <summary>ResolvedElsewhere + By when the entity took a peer revision (row K5) edited by another device; Superseded otherwise.</summary>
public sealed record DataSyncClosureHint(string Kind, SyncKey Key, DataSyncInboxClosure Closure, DataSyncEditorRef? By);

/// <summary>What an inbox card shows (display values only). Part of HTTP responses: Newtonsoft-safe.</summary>
public sealed record DataSyncInboxPayload(
    string EntityName, string? Subtype, string? PeerName, DataSyncEditorRef? RemoteEditor, string? OriginName,
    IReadOnlyList<DataSyncFieldOutcome> Fields,             // the fields the item is about, with display values
    int? ValueCount, int? UsageCount,
    IReadOnlyList<DataSyncDisplayValue>? Children,          // mass deletion / in-use child, ≤ 500
    int ChildrenTotal,
    string? RemoteSubtype, string? LocalSubtype,
    IReadOnlyList<DataSyncInboxCandidate>? Candidates,      // link suggestion / identity conflict targets
    IReadOnlyList<DataSyncInboxRecordRef>? Records,         // row M: the peer records that bind to one entity
    IReadOnlyList<DataSyncLargeChangeEntry>? LargeChange);  // B5: names and change counts, ≤ 500

public sealed record DataSyncInboxCandidate(string LocalKey, string Name, string? Subtype,
    DataSyncNaturalMatch Match, bool Updatable);            // Updatable = same subtype

public sealed record DataSyncInboxRecordRef(string PrimaryKey, string Name, string? Subtype);

public sealed record DataSyncLargeChangeEntry(string Name, string Kind, bool Create, int Changes);

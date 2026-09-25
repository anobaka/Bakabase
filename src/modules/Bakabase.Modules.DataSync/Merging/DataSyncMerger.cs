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

/// <param name="OpenStateItems">
/// This link's open state-derived items (§9.3). Only breaker B8 reads them: they are counted with the merger-derived
/// ones against <see cref="DataSyncLimits.MaxOpenInboxItemsPerLink"/> (§8.7). Null or empty when there are none.
/// </param>
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
    DataSyncLimits Limits,
    IReadOnlyList<DataSyncOpenInboxItem>? OpenStateItems = null);               // this link's open state-derived items (B8)

public sealed record DataSyncUsageQuery(string Kind, string LocalKey, IReadOnlyList<string> ChildIds, bool NeedValueCount);

/// <param name="TombstonesToServe">
/// Row T2 (§8.4): tombstones a peer still publishes an older live record for — unserved ones (older than retention,
/// §4.6) and served ones the peer read before it knew the entity. Each is served again — <c>TombstoneServed =
/// true</c> and a Seq bump, no revision — so the peer receives this device's deletion at its next pull. Null or empty
/// when there are none.
/// </param>
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
    IReadOnlyCollection<(string Kind, SyncKey Key)> Evaluated,
    IReadOnlyList<(string Kind, SyncKey Key)>? TombstonesToServe = null);

/// <param name="Code"><see cref="DataSyncAnomalies.Regression"/> or <see cref="DataSyncAnomalies.DuplicateActor"/>.</param>
public sealed record DataSyncAnomaly(string Code /* "regression" | "duplicateActor" */, string ActorId, long SeenCounter,
    string Kind, SyncKey Key);

/// <summary>
/// The continuous merge, record by record (§8.4–§8.9). Pure and deterministic: it reads nothing but its input and
/// the codecs, and the same input always gives the same result (lists sorted, iteration in Seq order with ties by
/// primary key). The apply runner [C] calls it inside the apply transaction, after Refresh (§8.10.2).
/// </summary>
public static class DataSyncMerger
{
    /// <summary>
    /// Phase 1 (pure): what usage must be read before merging: the codec's <c>ChildDeletionCandidates</c> of every
    /// entity that may lose children (with the nodes below them, whose usage counts too), and value counts of
    /// entities the peer deleted or changed the type of. Grouped per local entity, sorted by kind and local key.
    /// </summary>
    public static IReadOnlyList<DataSyncUsageQuery> CollectUsageQueries(DataSyncMergeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new DataSyncMergeEngine(input, collectOnly: true).Collect();
    }

    /// <summary>Phase 2 (pure, deterministic): §8.4–§8.8.</summary>
    public static DataSyncMergeResult Merge(DataSyncMergeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new DataSyncMergeEngine(input, collectOnly: false).Merge();
    }
}

/// <summary>
/// How a merge names the operations it proposes (<see cref="ApplyOperation.ItemId"/>): <c>{kind}/k/{baseKey}</c>,
/// the base row the record is agreed or pending on — the local entity's primary key, or for a create the record's
/// primary (for a revive, the tombstone's). The runner maps a <c>ChangedDuringApply</c> item back to its base with
/// <see cref="TryParse"/>, and an entity created by the merge to its new local key through
/// <see cref="ApplyBatchOutcome.CreatedLocalKeysByItemId"/>.
/// </summary>
public static class DataSyncMergeItemIds
{
    public static string Of(string kind, SyncKey baseKey) => $"{kind}/k/{baseKey.Value}";

    public static bool TryParse(string itemId, out string kind, out SyncKey baseKey)
    {
        kind = "";
        baseKey = default;
        var marker = itemId?.LastIndexOf("/k/", StringComparison.Ordinal) ?? -1;
        if (marker <= 0) return false;
        var key = itemId![(marker + 3)..];
        if (!SyncKey.IsValid(key)) return false;
        kind = itemId[..marker];
        baseKey = new SyncKey(key);
        return true;
    }
}

/// <summary>
/// <see cref="DataSyncMergeNote.Code"/> values: history and notification facts (§9.4) a merge reports. Args keys are
/// listed per code.
/// </summary>
public static class DataSyncMergeNoteCodes
{
    /// <summary>Follow: fields that took the peer's value over a local change (§8.1). Args: <c>count</c>.</summary>
    public const string FollowOverride = "followOverride";

    /// <summary>A peer's deletion applied by itself (§8.6); settings that used the definition lose it (Q17).</summary>
    public const string AutoDeleted = "autoDeleted";

    /// <summary>Row K2: kept, changed here after the peer deleted it. Args: <c>peer</c>.</summary>
    public const string EditWinsKept = "editWinsKept";

    /// <summary>Row A2: equal vectors, different forms, relayed by another build: the base took the record.</summary>
    public const string NormalizationChanged = "normalizationChanged";

    /// <summary>Children the peer deleted, unused here, removed (§8.6): saved filters may lose that condition. Args: <c>count</c>.</summary>
    public const string ChildrenRemoved = "childrenRemoved";

    /// <summary>§8.9: unknown members both sides changed; the local value was kept. Args: <c>members</c> (comma-separated).</summary>
    public const string UnknownMembersKeptLocal = "unknownMembersKeptLocal";

    /// <summary>§8.8: the peer no longer offers it; nothing was changed here.</summary>
    public const string MissingAtPeer = "missingAtPeer";

    /// <summary>§3.6: "sync the definition only" turned off; the options of every device are combined.</summary>
    public const string ChildrenLocalTurnedOff = "childrenLocalTurnedOff";
}

/// <param name="SeenBoth">
/// A <c>MergedNoConflict</c> whose result equals the peer's form but not this device's: it took the peer's content
/// over a local difference that only this link's base weighed — a union without a base bringing back what this
/// device had removed, or a base older than what this device holds (restored from a backup). It is a state that has
/// seen both sides, never the peer's revision: <see cref="DataSyncRecordApply.Revise"/> then adds this device's
/// counter as <c>FollowMerged</c> does (§2.8). With a bare <c>Max</c>, a device that merged the same two versions
/// against another base (and so kept its own side) ended with the same vector and another content, which no later
/// merge could tell apart (found by the convergence simulator). A result equal to both sides stays a bare <c>Max</c>
/// (example D of §9.1).
/// </param>
public sealed record DataSyncRevisionDecision(string Kind, EntityKeys Keys, string? LocalKey, DataSyncRevisionKind Revision,
    DataSyncVersionVector? RemoteVv, DataSyncVersionVector? TombstoneVv, bool ResultEqualsRemote, bool ResultEqualsLocal,
    string? OrderKey, JsonObject? Unknown, bool? ChildrenLocal, DataSyncEditorRef? AdoptEditor, bool SeenBoth = false);

public sealed record DataSyncBaseUpdate(string Kind, SyncKey Key, DataSyncBaseState State, DataSyncExclusionReason? Exclusion,
    DataSyncWireRecord? Record, IReadOnlyDictionary<string, string>? ChildMap,
    DataSyncPendingRecord? Pending, bool ClearPending);

public sealed record DataSyncOverlayChange(string Kind, string LocalKey, IReadOnlyList<DataSyncHeldChild> Hold,
    IReadOnlyList<DataSyncHeldChild> Release);

/// <param name="Synced">
/// Synced entities in shared order with their order keys. An entity created by this merge appears under its
/// <see cref="CreateEntityOperation"/>'s item id (<see cref="DataSyncMergeItemIds"/>) until the runner maps it to its
/// new local key (<see cref="DataSyncRecordApply.ResolveOrder"/>).
/// </param>
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
/// <param name="Detail">
/// A machine word refining the card's text: for <c>DeletedHereEditedThere</c>, <c>restored</c> (the peer's version
/// dominates the tombstone: "restored on X") or <c>changedAfterDelete</c> (concurrent: "changed on X after you
/// deleted it"), <see cref="DataSyncInboxDrafts"/>. Null otherwise.
/// </param>
public sealed record DataSyncInboxPayload(
    string EntityName, string? Subtype, string? PeerName, DataSyncEditorRef? RemoteEditor, string? OriginName,
    IReadOnlyList<DataSyncFieldOutcome> Fields,             // the fields the item is about, with display values
    int? ValueCount, int? UsageCount,
    IReadOnlyList<DataSyncDisplayValue>? Children,          // mass deletion / in-use child, ≤ 500
    int ChildrenTotal,
    string? RemoteSubtype, string? LocalSubtype,
    IReadOnlyList<DataSyncInboxCandidate>? Candidates,      // link suggestion / identity conflict targets
    IReadOnlyList<DataSyncInboxRecordRef>? Records,         // row M: the peer records that bind to one entity
    IReadOnlyList<DataSyncLargeChangeEntry>? LargeChange,   // B5: names and change counts, ≤ 500
    string? Detail = null);

public sealed record DataSyncInboxCandidate(string LocalKey, string Name, string? Subtype,
    DataSyncNaturalMatch Match, bool Updatable);            // Updatable = same subtype

public sealed record DataSyncInboxRecordRef(string PrimaryKey, string Name, string? Subtype);

public sealed record DataSyncLargeChangeEntry(string Name, string Kind, bool Create, int Changes);

using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

// §2.4: local state as the planner and merger see it.

/// <param name="Unreadable">
/// This device's stored row does not parse (§3.3, <see cref="LocalEntity.Unreadable"/>): Content is ReadLocal of
/// <c>{name, type}</c> only. The merger never targets it, and items touching it are Held(LocalUnreadable).
/// </param>
public sealed record DataSyncLocalEntityState(
    string LocalKey, EntityKeys Keys, object Content /* ReadLocal */, string LocalHash, string SharedHash,
    DataSyncVersionVector Vv, DataSyncActorId? LastActor, DataSyncEditorRef? LastEditor, string? OrderKey,
    DataSyncEntitySyncState State, DataSyncOverlay Overlay, bool ChildrenLocal, bool CreatedBySync, bool PublishHeld,
    JsonObject? Unknown, int? ValueCount, long Seq, bool Unreadable = false);

public sealed record DataSyncTombstoneState(EntityKeys Keys, DataSyncVersionVector Vv, DataSyncEditorRef? LastEditor,
    DataSyncEntitySyncState StateAtDeletion, DataSyncTombstoneKind TombstoneKind, bool Served);

public sealed record DataSyncLocalKindState(string Kind, IReadOnlyList<DataSyncLocalEntityState> Entities,
    IReadOnlyList<DataSyncTombstoneState> Tombstones);

public sealed record DataSyncEditorRef(string NodeId, string Name, string ActorId);

/// <param name="Record">
/// The peer's wire record at the last agreement (<c>RecordJson</c>); Content is its content. It carries what is not
/// content: the base's OrderKey (§8.5.5, row A2's comparison form), Keys and EditedBy. Null when nothing was agreed.
/// </param>
/// <param name="ExclusionKeys">
/// Every record key the exclusion matches (<c>ExclusionKeysJson</c>, §5.2's exclusion index, row E); empty unless
/// State is Excluded.
/// </param>
public sealed record DataSyncPeerBase(string Kind, SyncKey Key, DataSyncBaseState State, DataSyncExclusionReason? Exclusion,
    JsonObject? Content /* peer content at the last agreement, peer child ids, unknown members kept */,
    DataSyncVersionVector? Vv, IReadOnlyDictionary<string, string> ChildMap,
    DataSyncPendingRecord? Pending, DataSyncWireRecord? Record, IReadOnlyList<string> ExclusionKeys);

/// <summary>A peer record this device received but did not agree to (§8.4). Stored once per link and entity.</summary>
public sealed record DataSyncPendingRecord(DataSyncWireRecord Record, string RecordHash, DataSyncPendingReason Reason,
    long EvaluatedAtLocalSeq, DataSyncMergeFlags Flags);

/// <summary>
/// Per-entity switches that change how a pending record is merged; stored with the pending record and copied to
/// items.
/// </summary>
public sealed record DataSyncMergeFlags(bool DeletionsAsItems = false, bool SkipDeletionBreaker = false,
    bool SkipLargeChange = false, DataSyncChildDeletionMode ChildDeletions = DataSyncChildDeletionMode.Normal)
{
    public static DataSyncMergeFlags None { get; } = new();
}

public static class DataSyncLocalStateExtensions
{
    /// <summary>
    /// The first-contact planner's input (v3.1's LocalKindSnapshot): only Synced entities are candidates;
    /// TombstonedKeys = every key of a tombstone ∪ aliases pointing at tombstoned rows (v3.1 §5.3).
    /// </summary>
    public static LocalKindSnapshot ToPlannerSnapshot(this DataSyncLocalKindState state) =>
        throw new NotImplementedException();
}

// §2.6: staged incoming data.

public sealed record DataSyncIncomingEntity(
    DataSyncWireRecord Record,
    object? Content,                     // typed, validated (null when held or a tombstone)
    JsonObject? Unknown,
    string? ValidatedHash,               // ContentHash(codec.Write(Content)); tokens use it (v3.1 §7.5)
    string DisplayName,                  // content.name, else "#<index>"
    Planning.DataSyncHeldReason? Held,
    IReadOnlyList<Planning.DataSyncPlanWarning> Warnings);

public sealed record DataSyncStagedKind(string Kind, int SchemaVersion, bool Supported,
    Planning.DataSyncHeldReason? KindHeld, IReadOnlyList<DataSyncIncomingEntity> Entities, long MaxSeq,
    bool FullReconciliation);

public sealed record DataSyncStagedPull(string PeerNodeId, string PeerName, DataSyncFeedManifest Manifest,
    IReadOnlyList<DataSyncStagedKind> Kinds, DateTime FetchedAtUtc);

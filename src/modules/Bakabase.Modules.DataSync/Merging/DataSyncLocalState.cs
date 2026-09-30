using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

// §2.4: local state as the merger sees it.

/// <param name="Unreadable">
/// This device's stored row does not parse (§3.3, <see cref="LocalEntity.Unreadable"/>): Content is ReadLocal of
/// <c>{name, type}</c> only. The merger never targets it, and items touching it are Held(LocalUnreadable).
/// </param>
/// <param name="OpenItemAnyLink">
/// The entity has an open inbox item of either origin on ANY link (§8.6). The merge input's <c>OpenItems</c> holds
/// only this link's merger-derived items, so the store [C] fills this from every link's open items.
/// </param>
/// <param name="PendingRecordAnyLink">
/// The entity has a pending record on ANY link (§8.6): on a base row of the entity, or a record bound to it that
/// waits under its own primary (rows I and M). The store [C] fills it from every link's bases.
/// </param>
public sealed record DataSyncLocalEntityState(
    string LocalKey, EntityKeys Keys, object Content /* ReadLocal */, string LocalHash, string SharedHash,
    DataSyncVersionVector Vv, DataSyncActorId? LastActor, DataSyncEditorRef? LastEditor, string? OrderKey,
    DataSyncEntitySyncState State, DataSyncOverlay Overlay, bool ChildrenLocal, bool CreatedBySync, bool PublishHeld,
    int? ValueCount, bool Unreadable = false, bool OpenItemAnyLink = false,
    bool PendingRecordAnyLink = false);

public sealed record DataSyncTombstoneState(EntityKeys Keys, DataSyncVersionVector Vv, DataSyncEditorRef? LastEditor,
    DataSyncEntitySyncState StateAtDeletion, DataSyncTombstoneKind TombstoneKind, bool Served);

/// <param name="Entities">Live rows of the kind in any state, in local order (the adapter's order).</param>
public sealed record DataSyncLocalKindState(string Kind, IReadOnlyList<DataSyncLocalEntityState> Entities,
    IReadOnlyList<DataSyncTombstoneState> Tombstones);

public sealed record DataSyncEditorRef(string NodeId, string Name, string ActorId);

/// <param name="Record">
/// The peer's wire record at the last agreement (<c>RecordJson</c>), the only copy of the base content
/// (<see cref="Content"/>). It also carries what is not content: the base's OrderKey (§8.5.5, row A2's comparison
/// form), Keys and EditedBy. Null when nothing was agreed.
/// </param>
/// <param name="ExclusionKeys">
/// Every record key the exclusion matches (<c>ExclusionKeysJson</c>, §5.2's exclusion index, row E); empty unless
/// State is Excluded.
/// </param>
public sealed record DataSyncPeerBase(string Kind, SyncKey Key, DataSyncBaseState State, DataSyncExclusionReason? Exclusion,
    DataSyncVersionVector? Vv, IReadOnlyDictionary<string, string> ChildMap,
    DataSyncPendingRecord? Pending, DataSyncWireRecord? Record, IReadOnlyList<string> ExclusionKeys)
{
    /// <summary>
    /// Peer content at the last agreement, with peer child ids. Read from <see cref="Record"/>, so
    /// the three-way merge and row A2's comparison form can never read two different bases.
    /// </summary>
    public JsonObject? Content => Record?.Content;
}

/// <summary>
/// A peer record this device received but did not agree to (§8.4). Stored once per link and entity, and merged again
/// at every apply of the link.
/// </summary>
public sealed record DataSyncPendingRecord(DataSyncWireRecord Record, string RecordHash, DataSyncPendingReason Reason,
    DataSyncMergeFlags Flags);

/// <summary>
/// Per-entity switches that change how a pending record is merged, which decisions set; stored with the pending record
/// and copied to items.
/// </summary>
/// <param name="ChildDeletions">The child-deletion mode a <c>MassChildDeletion</c> decision chose.</param>
/// <param name="DecidedPaths">
/// Conflicting paths a person decided (§9.2): the decision wrote its value here, so the local value stands and the
/// rest of the record merges.
/// </param>
public sealed record DataSyncMergeFlags(DataSyncChildDeletionMode ChildDeletions = DataSyncChildDeletionMode.Normal,
    IReadOnlyList<string>? DecidedPaths = null)
{
    public static DataSyncMergeFlags None { get; } = new();
}

// §2.6: staged incoming data.

public sealed record DataSyncIncomingEntity(
    DataSyncWireRecord Record,
    object? Content,                     // typed, validated (null when held or a tombstone)
    string DisplayName,                  // content.name, else "#<index>"
    Planning.DataSyncHeldReason? Held,
    IReadOnlyList<Planning.DataSyncPlanWarning> Warnings);

public sealed record DataSyncStagedKind(string Kind, int SchemaVersion, bool Supported,
    Planning.DataSyncHeldReason? KindHeld, IReadOnlyList<DataSyncIncomingEntity> Entities, long MaxSeq,
    bool FullReconciliation);

/// <param name="ReconcilesLink">
/// Every kind the fetch half merges for the link came from 0 (§8.8): the link's full reconciliation. One kind from 0
/// (superseded, or added to the link) is not; a kind waiting for this device's review is never merged, so it never
/// keeps the daily reconciliation from being recorded.
/// </param>
public sealed record DataSyncStagedPull(string PeerNodeId, string PeerName, DataSyncFeedManifest Manifest,
    IReadOnlyList<DataSyncStagedKind> Kinds, DateTime FetchedAtUtc, bool ReconcilesLink = false);

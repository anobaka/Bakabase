using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

// §2.4: local state as the planner and merger see it.

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
    JsonObject? Unknown, int? ValueCount, long Seq, bool Unreadable = false, bool OpenItemAnyLink = false,
    bool PendingRecordAnyLink = false);

/// <param name="Seq">
/// The tombstone row's feed sequence (§6.2). A pending record stored against the tombstone remembers it as
/// <see cref="DataSyncPendingRecord.EvaluatedAtLocalSeq"/>, so it is re-merged only when the row changes (§8.4
/// condition 2). 0 when unknown: the record is then re-merged whenever the row has any Seq.
/// </param>
public sealed record DataSyncTombstoneState(EntityKeys Keys, DataSyncVersionVector Vv, DataSyncEditorRef? LastEditor,
    DataSyncEntitySyncState StateAtDeletion, DataSyncTombstoneKind TombstoneKind, bool Served, long Seq = 0);

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
    /// Peer content at the last agreement: peer child ids, unknown members kept. Read from <see cref="Record"/>, so
    /// the three-way merge and row A2's comparison form can never read two different bases.
    /// </summary>
    public JsonObject? Content => Record?.Content;
}

/// <summary>A peer record this device received but did not agree to (§8.4). Stored once per link and entity.</summary>
/// <param name="AppliedBase">
/// What this device already applied of the peer's records while the base stays at the last agreement (row K6 with
/// conflicts); null when nothing was. It stays with the entity's row, whatever record waits there, until the base
/// advances. Stored beside the record (<c>PendingAppliedBaseJson</c>).
/// </param>
public sealed record DataSyncPendingRecord(DataSyncWireRecord Record, string RecordHash, DataSyncPendingReason Reason,
    long EvaluatedAtLocalSeq, DataSyncMergeFlags Flags, DataSyncAppliedBase? AppliedBase = null);

/// <summary>
/// §8.4 row K6 with conflicts: the safe part of <see cref="Record"/> was applied here while the base keeps the last
/// agreement (rows A2 and K compare with that). Merged three-way against the base, every path the record changed
/// reads as the peer's change again and overwrites whatever changed here since; later merges of the entity therefore
/// run against this record. <see cref="KeptPaths"/> are the conflicting paths, which did not apply: a later merge
/// still asks about each of them while this device and the peer differ there, with the base's value on its card (so
/// the item derived again keeps its token).
/// </summary>
public sealed record DataSyncAppliedBase(DataSyncWireRecord Record, IReadOnlyList<DataSyncKeptPath> KeptPaths);

/// <summary>A conflicting path of a <see cref="DataSyncAppliedBase"/> and the display of the base value there.</summary>
public sealed record DataSyncKeptPath(string Path, DataSyncDisplayValue? Base);

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
    /// TombstonedKeys = every key of a tombstone ∪ aliases pointing at tombstoned rows (v3.1 §5.3). A tombstone's
    /// <see cref="DataSyncTombstoneState.Keys"/> already hold its aliases. <c>Position</c> is the entity's place in
    /// <see cref="DataSyncLocalKindState.Entities"/> (local order), counted over every live row so it is the same
    /// whichever rows are candidates; <c>ContentHash</c> is the local hash (§3.1).
    /// </summary>
    public static LocalKindSnapshot ToPlannerSnapshot(this DataSyncLocalKindState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entities = new List<LocalIdentifiedEntity>();
        for (var i = 0; i < state.Entities.Count; i++)
        {
            var entity = state.Entities[i];
            if (entity.State != DataSyncEntitySyncState.Synced) continue;
            entities.Add(new LocalIdentifiedEntity(entity.LocalKey, entity.Keys, i, entity.Content, entity.LocalHash,
                entity.Unreadable));
        }

        var tombstoned = new HashSet<SyncKey>();
        foreach (var tombstone in state.Tombstones) tombstoned.UnionWith(tombstone.Keys.All);
        return new LocalKindSnapshot(state.Kind, entities, tombstoned);
    }
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

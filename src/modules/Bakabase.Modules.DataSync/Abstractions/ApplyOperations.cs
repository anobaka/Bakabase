using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Abstractions;

// v3.1 §2.3, extended (§2.3 here). A batch holds one kind's operations, executed in order by the kind's adapter.
// BuildBatches orders, per kind: creates (incoming order), updates and binds (incoming order), deletes. Subtype
// changes run in their own phase (§8.5.6). Order placement runs after all batches (§3.7).

public sealed record ApplyBatch(string Kind, IReadOnlyList<ApplyOperation> Operations);

public abstract record ApplyOperation(string ItemId);

/// <param name="Keys">EntityKeys.None → mint a fresh key.</param>
/// <param name="Content">
/// A peer's content as the codec's <c>PrepareCreate</c> left it, which the adapter hands to the service's ordinary
/// create — or, with <paramref name="FromPreImage"/>, a captured pre-image's content.
/// </param>
/// <param name="FromPreImage">
/// Undo re-creating a deleted entity from its captured pre-image (§8.11, <c>DataSyncUndoAction.Recreate</c>): the
/// adapter stores the content exactly as captured, where the service's ordinary create could normalize it (custom
/// properties: case-variant duplicates stored before IgnoreCase was switched on, F72).
/// </param>
public sealed record CreateEntityOperation(string ItemId, EntityKeys Keys, string OriginNodeId,
    int IncomingPosition, JsonObject Content, bool FromPreImage = false) : ApplyOperation(ItemId);

/// <param name="MergedContent">
/// The entity's new content, stored as it is: a merge result (<c>Merge3</c>, a review's <c>Merge</c>) is already
/// normalized as the owning service would normalize an edit.
/// </param>
public sealed record UpdateEntityOperation(string ItemId, string LocalKey, string ExpectedLocalHash,
    JsonObject MergedContent, EntityKeys AliasKeysToAdd, IReadOnlyList<string> AddedChildIds,
    IReadOnlyList<string> RemovedChildIds) : ApplyOperation(ItemId);

/// <summary>Records keys only; no content change.</summary>
public sealed record BindOnlyOperation(string ItemId, string LocalKey, EntityKeys AliasKeysToAdd)
    : ApplyOperation(ItemId);

public sealed record DeleteEntityOperation(string ItemId, string LocalKey, string ExpectedLocalHash)
    : ApplyOperation(ItemId);

/// <summary>
/// Phase one of Convert (§8.5.6). Always the only operation for its LocalKey in its batch; the merge with R is
/// computed afterwards from the re-read entity, never in the same batch.
/// </summary>
public sealed record ChangeSubtypeOperation(string ItemId, string LocalKey, string ExpectedLocalHash, string Subtype)
    : ApplyOperation(ItemId);

public sealed record ApplyBatchOutcome(
    IReadOnlyDictionary<string, string> CreatedLocalKeysByItemId,
    IReadOnlySet<string> ChangedDuringApplyItemIds);   // skipped because ExpectedLocalHash no longer held

/// <summary>
/// Local state for one kind as the first-contact planner sees it (v3.1 §2.3), built from
/// <c>DataSyncLocalKindState</c> by <c>DataSyncLocalStateExtensions.ToPlannerSnapshot()</c>. Only Synced entities
/// are candidates.
/// </summary>
public sealed record LocalKindSnapshot(
    string Kind,
    IReadOnlyList<LocalIdentifiedEntity> Entities,  // live entities only
    IReadOnlySet<SyncKey> TombstonedKeys);          // every key of a tombstone ∪ aliases pointing at tombstoned rows

/// <param name="Unreadable">
/// The stored row does not parse (§3.3): never a target; items touching it are Held(LocalUnreadable).
/// </param>
public sealed record LocalIdentifiedEntity(string LocalKey, EntityKeys Keys, int Position, object Content,
    string ContentHash, bool Unreadable = false);

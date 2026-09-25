using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>What an apply did to one entity, as undo reads it back (§8.11).</summary>
public static class DataSyncPreImageActions
{
    /// <summary>Sync created (or revived) it here: undo deletes it and leaves an unserved <c>UndoneCreate</c> tombstone.</summary>
    public const string Created = "created";

    /// <summary>Content changed: undo writes back the changed paths (a child-level diff) as a local revision.</summary>
    public const string Updated = "updated";

    /// <summary>Sync deleted it: undo re-creates it with the same child ids (<see cref="DataSyncUndoAction.Recreate"/>).</summary>
    public const string Deleted = "deleted";

    /// <summary>Keys of a peer's record were recorded on it: undo excludes that link's record, keeping the aliases.</summary>
    public const string Bound = "bound";

    /// <summary>Its subtype was changed (Convert): undo converts it back.</summary>
    public const string TypeChanged = "typeChanged";
}

/// <summary>One entity of a pre-image (version 2, §8.11): a diff for an update, whole content only where needed.</summary>
/// <param name="EntityId">The side row's id, which survives rekeys, revives and re-creates.</param>
/// <param name="LocalKey">The entity's local key when the apply wrote it.</param>
/// <param name="LinkId">The link whose record the change came from; null for a change made here (a resolution's content).</param>
/// <param name="Keys">The entity's keys after the apply: what an exclusion of the link's record lists.</param>
/// <param name="AfterLocalHash">The local hash the apply left: undo of a create refuses once it changed since.</param>
/// <param name="Changes">Updated: the child-level diff.</param>
/// <param name="Content">Deleted: the canonical local content before, re-created with the same child ids.</param>
/// <param name="Row">Deleted and type changed: the adapter's raw row before (<c>CapturePreImageAsync</c>).</param>
/// <param name="FromSubtype">Type changed: the subtype before.</param>
/// <param name="ToSubtype">Type changed: the subtype the apply gave it.</param>
/// <param name="AliasesAdded">Bound: the keys recorded on it.</param>
public sealed record DataSyncEntityPreImage(string Kind, int EntityId, string LocalKey, string Name, string Action,
    int? LinkId, IReadOnlyList<string> Keys, string? AfterLocalHash, DataSyncEntityChanges? Changes = null,
    JsonObject? Content = null, JsonObject? Row = null, string? FromSubtype = null, string? ToSubtype = null,
    IReadOnlyList<string>? AliasesAdded = null);

/// <summary>
/// <c>PreImageJson</c>, version 2 (§8.11): per entity what undo needs — child-level diffs for updates, the whole row
/// only for deletes and type changes — and the identity operations (key moves) of the apply.
/// </summary>
public sealed record DataSyncPreImageDocument(int Version, IReadOnlyList<DataSyncEntityPreImage> Entities,
    DataSyncIdentityPreImage? Identity)
{
    public const int CurrentVersion = 2;

    public static DataSyncPreImageDocument Empty { get; } = new(CurrentVersion, [], null);

    public string ToJson() => JsonSerializer.Serialize(this, DeepOptions);

    /// <summary>A stored pre-image; an empty column has none. Version 1 (v3.1) documents are not undoable here.</summary>
    public static DataSyncPreImageDocument Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 256 });
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The stored PreImageJson is corrupted: {e.Message}", e);
        }

        if (root is not JsonObject document || document["version"] is not JsonValue version ||
            version.GetValue<int>() != CurrentVersion) return Empty;
        try
        {
            return document.Deserialize<DataSyncPreImageDocument>(DeepOptions) ?? Empty;
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The stored PreImageJson is corrupted: {e.Message}", e);
        }
    }

    /// <summary>A 16-level multilevel property nests deeper than the default depth.</summary>
    internal static readonly JsonSerializerOptions DeepOptions = new(DataSyncJson.Options) { MaxDepth = 256 };
}

/// <summary>
/// What one run of an apply, review, resolution, undo or restore recorded for its history entry (§4.1): items,
/// change lists (for the lost-update guard, §6.5), the pre-image (for undo, §8.11) and the counts of its summary.
/// </summary>
internal sealed class DataSyncApplyRecorder
{
    private readonly Dictionary<(string Kind, string LocalKey), DataSyncEntityChanges> _changes = new();

    public List<DataSyncHistoryItem> Items { get; } = [];
    public List<DataSyncEntityPreImage> PreImages { get; } = [];
    public DataSyncIdentityPreImage Identity { get; } = new();

    /// <summary>Entities whose vector changed, by kind and primary key, for dominance closure (§9.3).</summary>
    public HashSet<(string Kind, SyncKey Key)> Touched { get; } = [];

    /// <summary>Local keys whose definition changed, by kind: the hub's <c>DataSyncApplied</c> (§8.10.6).</summary>
    public HashSet<(string Kind, string LocalKey)> ChangedDefinitions { get; } = [];

    public int Created, Updated, Linked, Unchanged, Skipped, ChangedSinceReview, ChangedDuringApply, Held, Deleted,
        TypeChanged, Reordered, Resolved;

    /// <summary>A restore's "Take the other devices' definitions" link (§9.5), stored with the entry.</summary>
    public int? TakeTheirsLinkId { get; set; }

    /// <summary>Something was applied: a history entry is written only then (§8.10.2).</summary>
    public bool Applied => Created + Updated + Linked + Deleted + TypeChanged + Reordered + Resolved > 0 ||
                           PreImages.Count > 0 || !Identity.IsEmpty;

    public void Item(string itemId, string kind, string name, DataSyncItemOutcome outcome, DataSyncItemAction action,
        string? localKey, DataSyncPlanItemType type) =>
        Items.Add(new DataSyncHistoryItem(itemId, kind, name, outcome, action, localKey, type));

    /// <summary>The entity's change list; an entity touched without a content change is listed with empty lists.</summary>
    public void Changes(DataSyncEntityChanges changes) => _changes[(changes.Kind, changes.LocalKey)] = changes;

    public DataSyncHistoryCounts Counts => new(Created, Updated, Linked, Unchanged, Skipped, ChangedSinceReview,
        ChangedDuringApply, Held, Deleted, TypeChanged, Reordered, Resolved);

    /// <summary>The history row (§4.1); <c>PreImageBytes</c> is the UTF-8 size of the pre-image.</summary>
    public DataSyncApplyLogDbModel ToLog(DataSyncHistoryKind kind, DataSyncLinkDbModel? link, string? taskId,
        DateTime appliedAtUtc, long transactionMs, int? undoOf = null)
    {
        var preImage = new DataSyncPreImageDocument(DataSyncPreImageDocument.CurrentVersion, PreImages,
            Identity.IsEmpty ? null : Identity).ToJson();
        return new DataSyncApplyLogDbModel
        {
            Kind = kind,
            LinkId = link?.Id,
            PeerNodeId = link?.PeerNodeId,
            PeerName = link?.PeerName,
            TaskId = taskId,
            AppliedAtUtc = appliedAtUtc,
            SummaryJson = DataSyncStoredJson.Write(Counts),
            ResultJson = new DataSyncApplyResultDocument(Items, _changes.Values.ToList(), transactionMs, TakeTheirsLinkId)
                .ToJson(),
            PreImageJson = preImage,
            PreImageBytes = Encoding.UTF8.GetByteCount(preImage),
            UndoOfLogId = undoOf,
        };
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// The <c>ResultJson</c> of an apply log (§4.1): per item its outcome, and per entity the apply wrote the change list
/// the lost-update guard reads (§6.5) and undo's child-level diffs are built from (§8.11).
/// </summary>
/// <param name="Items">What happened to each item, as the history shows it.</param>
/// <param name="Entities">
/// One change list per entity the apply wrote, taken from the entity as re-read after the write (§6.4). An entity the
/// apply touched without changing its content (a Publish decision, a bind) is listed with empty lists: it is still
/// the entity's most recent apply, so nothing older is compared against it.
/// </param>
/// <param name="TransactionMs">Diagnostics (v3.1 §8.2).</param>
public sealed record DataSyncApplyResultDocument(
    IReadOnlyList<DataSyncHistoryItem> Items,
    IReadOnlyList<DataSyncEntityChanges> Entities,
    long? TransactionMs = null)
{
    public string ToJson() => DataSyncStoredJson.Write(this);

    /// <summary>
    /// The entity change lists of a stored <c>ResultJson</c>. A document without them (or an empty column) has
    /// none; a corrupted one is a store bug and throws <see cref="InvalidDataException"/>.
    /// </summary>
    public static IReadOnlyList<DataSyncEntityChanges> ReadEntities(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return [];
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(resultJson, documentOptions: new JsonDocumentOptions {MaxDepth = 64});
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The stored ResultJson is corrupted: {e.Message}", e);
        }

        if (root is not JsonObject document || document["entities"] is not JsonArray entities) return [];
        try
        {
            return entities.Deserialize<List<DataSyncEntityChanges>>(DataSyncJson.Options) ?? [];
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The stored ResultJson has corrupted entity changes: {e.Message}", e);
        }
    }
}

/// <summary>One entity's changes as an apply wrote them (§6.5, §8.11), by path and by local child id.</summary>
/// <param name="LocalKey">The entity's local key when the apply wrote it.</param>
public sealed record DataSyncEntityChanges(string Kind, string LocalKey,
    IReadOnlyList<DataSyncScalarChange> Scalars, IReadOnlyList<DataSyncChildChange> Children)
{
    [JsonIgnore]
    public bool IsEmpty => Scalars.Count == 0 && Children.Count == 0;
}

/// <summary>
/// One scalar path of the local content (§8.5.1: <c>name</c>, <c>type</c>, <c>ignoreCase</c>, <c>childrenLocal</c>,
/// <c>settings.*</c>, <c>defaultValue</c>) with its value before and after the apply; null is absent. Dotted paths
/// name nested members; <c>childrenLocal</c> is the shared field kept on the side row (§3.6).
/// </summary>
public sealed record DataSyncScalarChange(string Path, JsonNode? Before, JsonNode? After);

/// <summary>
/// One child by its LOCAL id (§6.5: "each child by uuid"). <paramref name="Before"/> null: the apply added it;
/// <paramref name="After"/> null: it removed it; both: it renamed and/or moved it (a changed parent). The ids of the
/// two sides are equal.
/// </summary>
/// <param name="Path">The merge path the change came from (§8.5.1), shown on an item.</param>
/// <param name="BeforeContent">
/// The child's own local JSON before the apply, without its children: what undo writes back for a removed child.
/// A child that is a bare value (an extension) has it as <c>{"value": …}</c>.
/// </param>
/// <param name="AfterContent">The child's own local JSON after the apply: what "Put the synced change back" writes (§6.5).</param>
/// <param name="BeforeContainer">
/// The member that held the child before the apply (<c>choices</c>, <c>children</c>…), under its parent or at the
/// top level, and <paramref name="BeforeIndex"/> its place there: where undo puts a removed child back.
/// </param>
/// <param name="AfterContainer">Likewise after the apply, with <paramref name="AfterIndex"/>.</param>
public sealed record DataSyncChildChange(string Path, DataSyncChildInfo? Before, DataSyncChildInfo? After,
    JsonObject? BeforeContent = null, JsonObject? AfterContent = null, string? BeforeContainer = null,
    int? BeforeIndex = null, string? AfterContainer = null, int? AfterIndex = null)
{
    [JsonIgnore]
    public string ChildId => (After ?? Before ?? throw new InvalidOperationException("A child change names a child.")).Id;
}

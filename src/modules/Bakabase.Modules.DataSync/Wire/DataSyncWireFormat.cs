using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Ordering;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Wire;

/// <summary>
/// The JSON form of wire records and chunks (§7.5.4), and the kind content hash (§7.5.2). Records and chunks are
/// canonical JSON objects; a chunk is told apart by its <c>chunkOf</c> member.
/// <code>
/// {"chunks":0,"content":{…},"deleted":false,"editedBy":{"actorId":"…","name":"PC-1","nodeId":"…"},
///  "hash":"sha256:…","keys":["7f3c…","a1b2…"],"orderKey":"a0V","origin":"…","schemaVersion":1,"seq":131,
///  "vv":{"0f1e2d3c4b5a6978":12}}
/// {"chunkOf":"7f3c…","index":0,"items":[…],"path":"tags"}
/// </code>
/// A tombstone has <c>deleted: true</c> and no content or hash; a held record has <c>heldAtSource</c> (the reason
/// name) and no content or hash. Readers ignore record members they do not know, so the record may grow.
/// </summary>
public static class DataSyncWireFormat
{
    /// <summary>The largest sequence number or counter on the wire: 2^53, exact in every JSON reader.</summary>
    public const long MaxSeq = 1L << 53;

    /// <summary><c>recordHash</c> of a tombstone in the kind content hash.</summary>
    public const string TombstoneHash = "tombstone";

    /// <summary><c>recordHash</c> of a held record in the kind content hash.</summary>
    public const string HeldHash = "held";

    internal const string ChunkOfMember = "chunkOf";

    /// <summary>The canonical JSON object of a record.</summary>
    public static JsonObject ToJson(DataSyncWireRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var json = new JsonObject
        {
            ["chunks"] = record.Chunks,
            ["deleted"] = record.Deleted,
            ["keys"] = new JsonArray(record.Keys.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            ["origin"] = record.Origin,
            ["schemaVersion"] = record.SchemaVersion,
            ["seq"] = record.Seq,
            ["vv"] = JsonNode.Parse(record.Vv.ToCanonicalString()),
        };
        if (record.Content is not null) json["content"] = record.Content.DeepClone();
        if (record.Hash is not null) json["hash"] = record.Hash;
        if (record.HeldAtSource is { } held) json["heldAtSource"] = held.ToString();
        if (record.OrderKey is not null) json["orderKey"] = record.OrderKey;
        if (record.EditedBy is { } editor)
        {
            json["editedBy"] = new JsonObject
            {
                ["actorId"] = editor.ActorId,
                ["name"] = editor.Name,
                ["nodeId"] = editor.NodeId,
            };
        }

        return json;
    }

    /// <summary>The canonical JSON object of a chunk.</summary>
    public static JsonObject ToJson(DataSyncWireChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return new JsonObject
        {
            [ChunkOfMember] = chunk.Of,
            ["index"] = chunk.Index,
            ["items"] = chunk.Items.DeepClone(),
            ["path"] = chunk.Path,
        };
    }

    /// <summary>
    /// The kind content hash of a snapshot (§7.5.2 step 6): <c>ContentHash</c> of the canonical JSON array of
    /// <c>[primaryKey, seq, recordHash]</c> in page order, where <c>recordHash</c> is the record's hash, or
    /// <see cref="TombstoneHash"/> / <see cref="HeldHash"/>. Chunks take no part (the hash covers reassembled
    /// content).
    /// </summary>
    public static string KindContentHash(IEnumerable<DataSyncWireRecord> recordsInPageOrder)
    {
        ArgumentNullException.ThrowIfNull(recordsInPageOrder);
        var array = new JsonArray();
        foreach (var record in recordsInPageOrder)
        {
            var recordHash = record.Deleted ? TombstoneHash : record.HeldAtSource is not null ? HeldHash : record.Hash;
            array.Add(new JsonArray(JsonValue.Create(record.Keys[0]), JsonValue.Create(record.Seq),
                JsonValue.Create(recordHash)));
        }

        return ContentHash.Of(array);
    }

    /// <summary>
    /// Reads a record from peer JSON. Never throws; false with an error for anything a valid source never sends.
    /// Checks the envelope only; content is the codec's (through <see cref="DataSyncRecordAssembler"/>).
    /// </summary>
    public static bool TryReadRecord(JsonObject json, DataSyncLimits limits, out DataSyncWireRecord? record,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(limits);
        record = null;
        try
        {
            error = ReadRecord(json, limits, out record);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException
                                      or OverflowException or JsonException or KeyNotFoundException)
        {
            record = null;
            error = e.Message;
        }

        return error is null;
    }

    /// <summary>Reads a chunk from peer JSON. Never throws; false with an error when malformed.</summary>
    public static bool TryReadChunk(JsonObject json, out DataSyncWireChunk? chunk, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        chunk = null;
        try
        {
            if (!TryGetString(json, ChunkOfMember, out var of) || !SyncKey.IsValid(of))
                return Fail("chunkOf is not a sync key", out error);
            if (!TryGetInt64(json, "index", out var index) || index < 0 || index > int.MaxValue)
                return Fail("chunk index is invalid", out error);
            if (!TryGetString(json, "path", out var path) || !IsMemberName(path))
                return Fail("chunk path is invalid", out error);
            if (json["items"] is not JsonArray items) return Fail("chunk items are missing", out error);

            chunk = new DataSyncWireChunk(of, (int)index, path, items);
            error = null;
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException
                                      or OverflowException or JsonException or KeyNotFoundException)
        {
            chunk = null;
            error = e.Message;
            return false;
        }
    }

    /// <summary>A node id as federation writes them: 1..128 ASCII letters, digits, '-' or '_'.</summary>
    internal static bool IsNodeId(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>A top-level content member name: <c>^[a-z][A-Za-z0-9]{0,63}$</c>.</summary>
    internal static bool IsMemberName(string? value) =>
        value is { Length: > 0 and <= 64 } && value[0] is >= 'a' and <= 'z' && value.All(char.IsAsciiLetterOrDigit);

    /// <summary>A display string from a peer: no U+0000, no other control character, no unpaired surrogate.</summary>
    internal static bool IsDisplayText(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c)) return false;
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c)) return false;
        }

        return true;
    }

    private static string? ReadRecord(JsonObject json, DataSyncLimits limits, out DataSyncWireRecord? record)
    {
        record = null;
        if (json.ContainsKey(ChunkOfMember)) return "a chunk is not a record";

        if (json["keys"] is not JsonArray keyArray || keyArray.Count == 0 || keyArray.Count > limits.MaxKeysPerEntity)
            return "keys are missing or too many";
        var keys = new List<string>(keyArray.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in keyArray)
        {
            if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.String || !v.TryGetValue(out string? key) ||
                !SyncKey.IsValid(key)) return "a key is not a sync key";
            if (!seen.Add(key)) return $"key {key} appears twice in one record";
            keys.Add(key);
        }

        if (!TryGetString(json, "origin", out var origin) || !IsNodeId(origin)) return "origin is not a node id";
        if (!TryGetInt64(json, "seq", out var seq) || seq < 1 || seq > MaxSeq) return "seq is invalid";
        if (!DataSyncVersionVector.TryParse(json["vv"], limits, out var vv)) return "vv is invalid";
        if (!TryGetBool(json, "deleted", out var deleted)) return "deleted is missing";
        if (!TryGetInt64(json, "schemaVersion", out var schemaVersion) || schemaVersion < 1 ||
            schemaVersion > int.MaxValue) return "schemaVersion is invalid";
        if (!TryGetInt64(json, "chunks", out var chunks) || chunks < 0 || chunks > int.MaxValue)
            return "chunks is invalid";

        DataSyncEditorRef? editedBy = null;
        if (json.ContainsKey("editedBy"))
        {
            if (json["editedBy"] is not JsonObject editor) return "editedBy is not an object";
            if (!TryGetString(editor, "actorId", out var actorId) || !DataSyncActorId.IsValid(actorId))
                return "editedBy.actorId is invalid";
            if (!TryGetString(editor, "nodeId", out var editorNode) || !IsNodeId(editorNode))
                return "editedBy.nodeId is invalid";
            if (!TryGetString(editor, "name", out var editorName) || editorName.Length > limits.MaxEditorNameLength ||
                !IsDisplayText(editorName)) return "editedBy.name is invalid";
            editedBy = new DataSyncEditorRef(editorNode, editorName, actorId);
        }

        string? orderKey = null;
        if (json.ContainsKey("orderKey"))
        {
            if (!TryGetString(json, "orderKey", out var key) || key.Length > limits.MaxOrderKeyLength ||
                !FractionalIndex.IsValid(key)) return "orderKey is invalid";
            orderKey = key;
        }

        JsonObject? content = null;
        if (json.ContainsKey("content"))
        {
            if (json["content"] is not JsonObject c) return "content is not an object";
            content = c;
        }

        string? hash = null;
        if (json.ContainsKey("hash"))
        {
            // A malformed hash is a mismatch: the assembler holds the entity.
            if (!TryGetString(json, "hash", out var h)) return "hash is not a string";
            hash = h;
        }

        DataSyncHeldReason? heldAtSource = null;
        if (json.ContainsKey("heldAtSource"))
        {
            if (!TryGetString(json, "heldAtSource", out var reason)) return "heldAtSource is not a string";
            // A reason this build does not know (a newer source) still means the source withholds it.
            heldAtSource = Enum.TryParse<DataSyncHeldReason>(reason, ignoreCase: false, out var parsed) &&
                           Enum.IsDefined(parsed) && reason == parsed.ToString()
                ? parsed
                : DataSyncHeldReason.AtSource;
        }

        if (deleted)
        {
            if (content is not null || hash is not null || heldAtSource is not null || chunks != 0)
                return "a tombstone carries content";
        }
        else if (heldAtSource is not null)
        {
            if (content is not null || hash is not null || chunks != 0) return "a held record carries content";
        }
        else if (content is null || hash is null)
        {
            return "a live record has no content or hash";
        }

        // Detach content from the page, so the record owns it.
        record = new DataSyncWireRecord(keys, origin, seq, vv, editedBy, deleted, (int)schemaVersion, orderKey,
            content is null ? null : (JsonObject)content.DeepClone(), hash, heldAtSource, (int)chunks);
        return null;
    }

    internal static bool TryGetString(JsonObject json, string member, out string value)
    {
        value = null!;
        if (json[member] is not JsonValue v || v.GetValueKind() != JsonValueKind.String ||
            !v.TryGetValue(out string? s)) return false;
        value = s;
        return true;
    }

    internal static bool TryGetInt64(JsonObject json, string member, out long value)
    {
        value = 0;
        return json[member] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && JsonNumbers.TryGetInt64(v, out value);
    }

    internal static bool TryGetBool(JsonObject json, string member, out bool value)
    {
        value = false;
        if (json[member] is not JsonValue v) return false;
        switch (v.GetValueKind())
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                return true;
            default:
                return false;
        }
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace Bakabase.Modules.DataSync.Wire;

/// <summary>
/// Parses one page's bytes with JsonDocument (MaxDepth 64). Never throws on peer input (v3.1 L3 rule): every
/// problem is a page problem (the whole pull is discarded) or, later in <see cref="DataSyncRecordAssembler"/>, a
/// held record.
/// </summary>
/// <remarks>
/// A page is refused as <see cref="Corrupted"/> for invalid UTF-8, a byte order mark, JSON deeper than
/// <c>MaxJsonDepth</c>, a duplicate member, a number that is not an integer in <see cref="long"/> range, a
/// malformed envelope, record or chunk, or a record whose <c>seq</c> is not above the page's <c>sinceSeq</c> or
/// out of order; as <see cref="TooLarge"/> over <c>MaxPageBytes</c> or <c>MaxRecordsPerPage</c>; and as
/// <see cref="WrongSnapshot"/> when it names another snapshot or kind. Record members this build does not know are
/// ignored.
/// </remarks>
public static class DataSyncWireReader
{
    public const string Corrupted = "corrupted";
    public const string TooLarge = "tooLarge";
    public const string WrongSnapshot = "wrongSnapshot";

    public static DataSyncPageReadResult ReadPage(ReadOnlyMemory<byte> utf8, string expectedSnapshotId,
        string expectedKind, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(expectedSnapshotId);
        ArgumentNullException.ThrowIfNull(expectedKind);
        ArgumentNullException.ThrowIfNull(limits);
        try
        {
            return Read(utf8, expectedSnapshotId, expectedKind, limits);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException
                                      or FormatException or OverflowException or KeyNotFoundException
                                      or InvalidDataException or DecoderFallbackException)
        {
            // Last resort: whatever odd input makes a parser throw is a corrupted page, never an exception.
            return Problem(Corrupted);
        }
    }

    /// <summary>A kind id: <c>^[a-z][A-Za-z0-9]{1,63}$</c>.</summary>
    public static bool IsKindId(string? value) => value is { Length: >= 2 and <= 64 } && DataSyncWireFormat.IsMemberName(value);

    private static DataSyncPageReadResult Read(ReadOnlyMemory<byte> utf8, string expectedSnapshotId,
        string expectedKind, DataSyncLimits limits)
    {
        if (utf8.Length > limits.MaxPageBytes) return Problem(TooLarge);
        var span = utf8.Span;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) return Problem(Corrupted);
        if (!Utf8.IsValid(span)) return Problem(Corrupted);

        JsonObject root;
        using (var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
               {
                   MaxDepth = limits.MaxJsonDepth,
                   AllowTrailingCommas = false,
                   CommentHandling = JsonCommentHandling.Disallow,
               }))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Problem(Corrupted);
            if (!TryConvert(document.RootElement, out var node) || node is not JsonObject obj) return Problem(Corrupted);
            root = obj;
        }

        if (!DataSyncWireFormat.TryGetString(root, "snapshotId", out var snapshotId) ||
            !DataSyncWireFormat.TryGetString(root, "kind", out var kind) ||
            !DataSyncWireFormat.TryGetInt64(root, "sinceSeq", out var sinceSeq) ||
            sinceSeq is < 0 or > DataSyncWireFormat.MaxSeq ||
            !DataSyncWireFormat.TryGetBool(root, "complete", out var complete) ||
            root["records"] is not JsonArray items) return Problem(Corrupted);

        string? nextCursor = null;
        if (root.ContainsKey("nextCursor"))
        {
            if (!DataSyncWireFormat.TryGetString(root, "nextCursor", out var cursor) ||
                !DataSyncWireWriter.IsIdentifier(cursor)) return Problem(Corrupted);
            nextCursor = cursor;
        }

        if (complete == (nextCursor is not null)) return Problem(Corrupted);
        if (snapshotId != expectedSnapshotId || kind != expectedKind) return Problem(WrongSnapshot);
        if (items.Count > limits.MaxRecordsPerPage) return Problem(TooLarge);

        var records = new List<DataSyncWireRecord>();
        var chunks = new List<DataSyncWireChunk>();
        var objects = new List<JsonObject>(items.Count);
        var previousSeq = sinceSeq;
        foreach (var item in items)
        {
            if (item is not JsonObject obj) return Problem(Corrupted);
            objects.Add(obj);
            if (obj.ContainsKey(DataSyncWireFormat.ChunkOfMember))
            {
                if (!DataSyncWireFormat.TryReadChunk(obj, out var chunk, out _)) return Problem(Corrupted);
                chunks.Add(chunk!);
                continue;
            }

            if (!DataSyncWireFormat.TryReadRecord(obj, limits, out var record, out _)) return Problem(Corrupted);
            if (record!.Seq <= sinceSeq || record.Seq < previousSeq) return Problem(Corrupted);
            previousSeq = record.Seq;
            records.Add(record);
        }

        var page = new DataSyncFeedPage(snapshotId, kind, sinceSeq, objects, nextCursor, complete);
        return new DataSyncPageReadResult(page, records, chunks, null);
    }

    /// <summary>
    /// Copies a parsed element into a detached node tree, refusing duplicate members and numbers that are not
    /// integers in <see cref="long"/> range. Depth is already bounded by the document's MaxDepth.
    /// </summary>
    private static bool TryConvert(JsonElement element, out JsonNode? node)
    {
        node = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var obj = new JsonObject();
                foreach (var member in element.EnumerateObject())
                {
                    if (obj.ContainsKey(member.Name)) return false;
                    if (!TryConvert(member.Value, out var value)) return false;
                    obj.Add(member.Name, value);
                }

                node = obj;
                return true;
            }
            case JsonValueKind.Array:
            {
                var array = new JsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    if (!TryConvert(item, out var value)) return false;
                    array.Add(value);
                }

                node = array;
                return true;
            }
            case JsonValueKind.String:
                node = JsonValue.Create(element.GetString());
                return true;
            case JsonValueKind.Number:
            {
                // Integers only: no fraction, no exponent, in long range (so "1.0", "1e2" and 2^63 are refused).
                var raw = element.GetRawText();
                if (raw.AsSpan().IndexOfAny(".eE") >= 0 || !element.TryGetInt64(out var value)) return false;
                node = JsonValue.Create(value);
                return true;
            }
            case JsonValueKind.True:
                node = JsonValue.Create(true);
                return true;
            case JsonValueKind.False:
                node = JsonValue.Create(false);
                return true;
            case JsonValueKind.Null:
                node = null;
                return true;
            default:
                return false;
        }
    }

    private static DataSyncPageReadResult Problem(string problem) => new(null, [], [], problem);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Feed;

/// <summary>
/// Writes one kind of a snapshot as pages of raw canonical JSON (§7.5.3, §7.5.4): the pure engine's
/// <see cref="DataSyncWireWriter"/> (package A). A seam, so the feed's own tests can run before the writer lands and
/// against it afterwards.
/// </summary>
public interface IDataSyncFeedPageWriter
{
    /// <param name="records">Published records with <c>Seq &gt; sinceSeq</c>, in Seq order, unchunked.</param>
    IReadOnlyList<byte[]> WritePages(string snapshotId, string kind, long sinceSeq,
        IReadOnlyList<DataSyncWireRecord> records, DataSyncLimits limits);
}

/// <summary>The production writer: <see cref="DataSyncWireWriter.WritePages"/>.</summary>
public sealed class DataSyncWireFeedPageWriter : IDataSyncFeedPageWriter
{
    public IReadOnlyList<byte[]> WritePages(string snapshotId, string kind, long sinceSeq,
        IReadOnlyList<DataSyncWireRecord> records, DataSyncLimits limits) =>
        DataSyncWireWriter.WritePages(snapshotId, kind, sinceSeq, records, limits);
}

/// <summary>One record as its page carries it: what the kind content hash covers (§7.5.2 step 6).</summary>
/// <param name="RecordHash">The record's <c>hash</c>, or <c>"tombstone"</c> / <c>"held"</c>.</param>
public sealed record DataSyncFeedPageRecord(string PrimaryKey, long Seq, string RecordHash);

/// <summary>One kind of a snapshot as it was written: its pages, the cursor each page is asked for with, its records.</summary>
/// <param name="Cursors">The cursor that asks for each page: null for the first, then the previous page's <c>nextCursor</c>.</param>
public sealed record DataSyncFeedWrittenKind(IReadOnlyList<byte[]> Pages, IReadOnlyList<string?> Cursors,
    IReadOnlyList<DataSyncFeedPageRecord> Records, string ContentHash, long Bytes);

/// <summary>
/// Reads back the pages the writer produced, before they are served: every page must belong to this snapshot, kind
/// and since, the pages must chain through their cursors to one complete last page, and they must carry exactly the
/// records handed to the writer, in order. The kind content hash is computed from what the pages carry, so a record
/// the writer had to hold (content too large to travel) is hashed as held, exactly as a receiver recomputes it.
/// </summary>
/// <remarks>Streams over each page: record content and chunk items are skipped, never materialized.</remarks>
public static class DataSyncFeedPageScanner
{
    public const string TombstoneHash = "tombstone";
    public const string HeldHash = "held";

    /// <summary>Our own bytes: generous, since a deep multilevel property nests past federation's limit (F61).</summary>
    private const int MaxDepth = 256;

    /// <exception cref="InvalidOperationException">The writer produced pages that do not match its input.</exception>
    public static DataSyncFeedWrittenKind Scan(IReadOnlyList<byte[]> pages, string snapshotId, string kind,
        long sinceSeq, IReadOnlyList<DataSyncWireRecord> written)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(written);
        if (pages.Count == 0) throw Bug(kind, "no page was written");

        var cursors = new List<string?>(pages.Count) {null};
        var records = new List<DataSyncFeedPageRecord>(written.Count);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        for (var i = 0; i < pages.Count; i++)
        {
            var page = ScanPage(pages[i], records, kind);
            bytes += pages[i].Length;
            if (page.SnapshotId != snapshotId || page.Kind != kind || page.SinceSeq != sinceSeq)
                throw Bug(kind, $"page {i} belongs to another snapshot, kind or since");
            var last = i == pages.Count - 1;
            if (page.Complete != last) throw Bug(kind, $"page {i} is {(page.Complete ? "" : "not ")}complete");
            if (last)
            {
                if (page.NextCursor is not null) throw Bug(kind, "the last page names a next page");
            }
            else
            {
                if (string.IsNullOrEmpty(page.NextCursor) || !seenCursors.Add(page.NextCursor))
                    throw Bug(kind, $"page {i} names no new next page");
                cursors.Add(page.NextCursor);
            }
        }

        if (records.Count != written.Count ||
            records.Zip(written).Any(p => p.First.PrimaryKey != p.Second.Keys[0] || p.First.Seq != p.Second.Seq))
            throw Bug(kind, "the pages do not carry the records handed to the writer");

        return new DataSyncFeedWrittenKind(pages, cursors, records, KindContentHash(records), bytes);
    }

    /// <summary>
    /// §7.5.2 step 6: <c>ContentHash</c> of the canonical JSON array of <c>[primaryKey, seq, recordHash]</c> in page
    /// order. Chunks take no part: a record's hash covers its reassembled content.
    /// </summary>
    public static string KindContentHash(IEnumerable<DataSyncFeedPageRecord> recordsInPageOrder) =>
        ContentHash.Of(new JsonArray(recordsInPageOrder
            .Select(r => (JsonNode?) new JsonArray(JsonValue.Create(r.PrimaryKey), JsonValue.Create(r.Seq),
                JsonValue.Create(r.RecordHash)))
            .ToArray()));

    private sealed record Envelope(string? SnapshotId, string? Kind, long? SinceSeq, bool Complete, string? NextCursor);

    private static Envelope ScanPage(byte[] page, List<DataSyncFeedPageRecord> records, string kind)
    {
        var reader = new Utf8JsonReader(page, new JsonReaderOptions {MaxDepth = MaxDepth});
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw Bug(kind, "a page is not an object");
            string? snapshotId = null, pageKind = null, nextCursor = null;
            long? sinceSeq = null;
            var complete = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "snapshotId":
                        snapshotId = reader.GetString();
                        break;
                    case "kind":
                        pageKind = reader.GetString();
                        break;
                    case "sinceSeq":
                        sinceSeq = reader.GetInt64();
                        break;
                    case "complete":
                        complete = reader.GetBoolean();
                        break;
                    case "nextCursor":
                        nextCursor = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "records":
                        if (reader.TokenType != JsonTokenType.StartArray) throw Bug(kind, "records is not an array");
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType != JsonTokenType.StartObject) throw Bug(kind, "an item is not an object");
                            if (ScanItem(ref reader, kind) is { } record) records.Add(record);
                        }

                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new Envelope(snapshotId, pageKind, sinceSeq, complete, nextCursor);
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            throw Bug(kind, $"a page is not JSON the feed can read back: {e.Message}");
        }
    }

    /// <summary>One record or chunk, the reader on its StartObject; null for a chunk.</summary>
    private static DataSyncFeedPageRecord? ScanItem(ref Utf8JsonReader reader, string kind)
    {
        string? primaryKey = null, hash = null;
        long? seq = null;
        bool deleted = false, held = false, chunk = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "chunkOf":
                    chunk = true;
                    break;
                case "keys":
                    if (reader.TokenType != JsonTokenType.StartArray) throw Bug(kind, "keys is not an array");
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) primaryKey ??= reader.GetString();
                    break;
                case "seq":
                    seq = reader.GetInt64();
                    break;
                case "deleted":
                    deleted = reader.GetBoolean();
                    break;
                case "hash":
                    hash = reader.GetString();
                    break;
                case "heldAtSource":
                    held = true;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (chunk) return null;
        if (primaryKey is null || seq is null) throw Bug(kind, "a record has no key or seq");
        var recordHash = deleted ? TombstoneHash : held ? HeldHash : hash ?? throw Bug(kind, "a live record has no hash");
        return new DataSyncFeedPageRecord(primaryKey, seq.Value, recordHash);
    }

    private static InvalidOperationException Bug(string kind, string what) =>
        new($"The data sync feed wrote an unusable page of '{kind}': {what}.");
}

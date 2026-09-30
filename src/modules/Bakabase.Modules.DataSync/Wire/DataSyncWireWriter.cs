using System.Text;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Ordering;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Wire;

/// <summary>What <see cref="DataSyncWireWriter.WriteKind"/> made of one kind's records for one snapshot.</summary>
/// <param name="Pages">Each page's canonical JSON bytes, first page first.</param>
/// <param name="Records">
/// The records as written, in page order: unchanged, except that a record whose content cannot travel is written held
/// with no content — <c>TooLarge</c> when it does not fit a page, <c>Invalid</c> for a string no reader can decode.
/// </param>
/// <param name="ContentHash">
/// The kind content hash over <paramref name="Records"/> (§7.5.2 step 6): the manifest's
/// <c>DataSyncFeedKind.ContentHash</c>, which receivers recompute.
/// </param>
/// <param name="TotalBytes">The pages' total size, for the snapshot limits (§7.5.2).</param>
public sealed record DataSyncWrittenKind(IReadOnlyList<byte[]> Pages, IReadOnlyList<DataSyncWireRecord> Records,
    string ContentHash, long TotalBytes);

/// <summary>Source side: builds a frozen snapshot's pages from published records (§7.5.3, §7.5.4).</summary>
/// <remarks>
/// A page is canonical JSON:
/// <c>{"complete":false,"kind":"customProperty","nextCursor":"p2","records":[…],"sinceSeq":120,"snapshotId":"…"}</c>.
/// The first page has no cursor; page <c>n</c> (0-based) is asked for with <see cref="CursorOf"/>(n), and every page
/// but the last names the next one. A record travels whole or not at all: every page holds at most
/// <c>MaxPageBytes</c> and <c>MaxRecordsPerPage</c> records, and one that does not fit an empty page is held
/// (<c>TooLarge</c>). A kind with no records is one empty, complete page. The same input always gives the same
/// bytes.
/// </remarks>
public static class DataSyncWireWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Canonical JSON bytes per page (<see cref="WriteKind"/>'s pages).</summary>
    public static IReadOnlyList<byte[]> WritePages(string snapshotId, string kind, long sinceSeq,
        IReadOnlyList<DataSyncWireRecord> records, DataSyncLimits limits) =>
        WriteKind(snapshotId, kind, sinceSeq, records, limits).Pages;

    /// <summary>
    /// Writes one kind of a snapshot. <paramref name="records"/> are this device's published records with
    /// <c>Seq &gt; sinceSeq</c>, in ascending <c>Seq</c>, each live one with
    /// <c>Hash == ContentHash(Content)</c>, and with an envelope a reader accepts (keys, origin, vector, order key,
    /// editor). Anything else is a bug of the caller and throws <see cref="ArgumentException"/>, so a source never
    /// serves a page its readers refuse. The one exception is the editor's display name, which is only shown:
    /// it is shortened to <c>MaxEditorNameLength</c> and its control characters and unpaired surrogates are replaced.
    /// </summary>
    public static DataSyncWrittenKind WriteKind(string snapshotId, string kind, long sinceSeq,
        IReadOnlyList<DataSyncWireRecord> records, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(limits);
        if (!IsIdentifier(snapshotId)) throw new ArgumentException($"Invalid snapshot id '{snapshotId}'.", nameof(snapshotId));
        if (!DataSyncWireReader.IsKindId(kind)) throw new ArgumentException($"Invalid kind '{kind}'.", nameof(kind));
        if (sinceSeq is < 0 or > DataSyncWireFormat.MaxSeq)
            throw new ArgumentOutOfRangeException(nameof(sinceSeq), sinceSeq, "A since is 0..2^53.");

        // An item must fit an otherwise empty page whatever that page's cursor is.
        var itemCapacity = limits.MaxPageBytes - Utf8.GetByteCount(Envelope(snapshotId, kind, sinceSeq, int.MaxValue, "",
            last: false));
        if (itemCapacity <= 0) throw new ArgumentException("MaxPageBytes leaves no room for records.", nameof(limits));

        var written = new List<DataSyncWireRecord>(records.Count);
        var items = new List<string>(records.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var previousSeq = sinceSeq;
        foreach (var record in records)
        {
            Validate(record, sinceSeq, previousSeq, keys, limits);
            previousSeq = record.Seq;
            var (asWritten, item) = Serialize(SanitizeEditor(record, limits), itemCapacity);
            written.Add(asWritten);
            items.Add(item);
        }

        var pages = Paginate(snapshotId, kind, sinceSeq, items, limits);
        return new DataSyncWrittenKind(pages, written, DataSyncWireFormat.KindContentHash(written),
            pages.Sum(p => (long)p.Length));
    }

    /// <summary>The cursor that asks for page <paramref name="pageIndex"/> (0-based): null for the first, else <c>p{n}</c>.</summary>
    public static string? CursorOf(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        return pageIndex == 0 ? null : "p" + pageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The 0-based page a cursor asks for; null for a malformed cursor. A null cursor is the first page.</summary>
    public static int? PageIndexOf(string? cursor)
    {
        if (cursor is null) return 0;
        if (cursor.Length is < 2 or > 11 || cursor[0] != 'p' || cursor[1] == '0') return null;
        return int.TryParse(cursor.AsSpan(1), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var index) && index > 0
            ? index
            : null;
    }

    private static void Validate(DataSyncWireRecord record, long sinceSeq, long previousSeq, HashSet<string> keys,
        DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Keys is not { Count: > 0 } || record.Keys.Count > limits.MaxKeysPerEntity ||
            record.Keys.Any(k => !SyncKey.IsValid(k)))
            throw new ArgumentException($"A record has 1..{limits.MaxKeysPerEntity} valid keys.", nameof(record));
        foreach (var key in record.Keys)
        {
            if (!keys.Add(key)) throw new ArgumentException($"Key {key} appears twice in one kind.", nameof(record));
        }

        if (record.Seq <= sinceSeq || record.Seq < previousSeq || record.Seq > DataSyncWireFormat.MaxSeq)
            throw new ArgumentException($"Record {record.Keys[0]}: seq {record.Seq} is out of order.", nameof(record));
        if (!DataSyncWireFormat.IsNodeId(record.Origin))
            throw new ArgumentException($"Record {record.Keys[0]}: origin '{record.Origin}' is not a node id.", nameof(record));
        if (record.SchemaVersion < 1)
            throw new ArgumentException($"Record {record.Keys[0]}: schema version {record.SchemaVersion}.", nameof(record));
        if (record.Vv is null || record.Vv.Counters.Count > limits.MaxActorsPerVector)
            throw new ArgumentException($"Record {record.Keys[0]}: the vector is missing or too wide.", nameof(record));
        if (record.OrderKey is not null && (record.OrderKey.Length > limits.MaxOrderKeyLength ||
                                            !FractionalIndex.IsValid(record.OrderKey)))
            throw new ArgumentException($"Record {record.Keys[0]}: invalid order key '{record.OrderKey}'.", nameof(record));
        if (record.EditedBy is { } editor &&
            (!DataSyncActorId.IsValid(editor.ActorId) || !DataSyncWireFormat.IsNodeId(editor.NodeId) ||
             editor.Name is null))
            throw new ArgumentException($"Record {record.Keys[0]}: invalid editor.", nameof(record));

        var live = !record.Deleted && record.HeldAtSource is null;
        if (live != (record.Content is not null) || live != (record.Hash is not null))
            throw new ArgumentException($"Record {record.Keys[0]}: content and hash go with a live record only.",
                nameof(record));
        if (record.Deleted && record.HeldAtSource is not null)
            throw new ArgumentException($"Record {record.Keys[0]}: a tombstone is not held.", nameof(record));
    }

    /// <summary>The record with its editor's display name made readable (<see cref="WriteKind"/>).</summary>
    private static DataSyncWireRecord SanitizeEditor(DataSyncWireRecord record, DataSyncLimits limits)
    {
        if (record.EditedBy is not { } editor) return record;
        var name = editor.Name;
        if (name.Length <= limits.MaxEditorNameLength && DataSyncWireFormat.IsDisplayText(name)) return record;

        var sb = new StringBuilder(Math.Min(name.Length, limits.MaxEditorNameLength));
        for (var i = 0; i < name.Length && sb.Length < limits.MaxEditorNameLength; i++)
        {
            var c = name[i];
            if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
            {
                if (sb.Length + 2 > limits.MaxEditorNameLength) break;
                sb.Append(c).Append(name[++i]);
            }
            else
            {
                sb.Append(char.IsControl(c) || char.IsSurrogate(c) ? '\uFFFD' : c);
            }
        }

        return record with { EditedBy = editor with { Name = sb.ToString() } };
    }

    /// <summary>The record as written and its canonical JSON.</summary>
    private static (DataSyncWireRecord Written, string Item) Serialize(DataSyncWireRecord record, int itemCapacity)
    {
        if (record.Content is { } content)
        {
            if (ContentHash.Of(content) != record.Hash)
                throw new ArgumentException($"Record {record.Keys[0]}: hash does not match its content.", nameof(record));

            // Local content is never validated, and canonical JSON writes an unpaired surrogate as an escape a reader
            // parses but cannot decode: such content is withheld, so readers hold that one entity instead of refusing
            // the whole page at every pull.
            if (DataSyncWireFormat.HasUnpairedSurrogate(content))
                return Held(record, DataSyncHeldReason.Invalid, itemCapacity);
        }

        var item = CanonicalJson.Serialize(DataSyncWireFormat.ToJson(record));
        return Utf8.GetByteCount(item) <= itemCapacity ? (record, item) : Held(record, DataSyncHeldReason.TooLarge, itemCapacity);
    }

    private static (DataSyncWireRecord, string) Held(DataSyncWireRecord record, DataSyncHeldReason reason,
        int itemCapacity)
    {
        var held = record with { Content = null, Hash = null, HeldAtSource = reason };
        var item = CanonicalJson.Serialize(DataSyncWireFormat.ToJson(held));
        // Keys, a vector and an editor are bounded far below a page (64 keys, 256 actors).
        if (Utf8.GetByteCount(item) > itemCapacity)
            throw new ArgumentException($"Record {record.Keys[0]} does not fit a page even without content.",
                nameof(record));
        return (held, item);
    }

    private static List<byte[]> Paginate(string snapshotId, string kind, long sinceSeq, IReadOnlyList<string> items,
        DataSyncLimits limits)
    {
        var groups = new List<List<string>>();
        var current = new List<string>();
        var currentBytes = 0;
        foreach (var item in items)
        {
            var bytes = Utf8.GetByteCount(item);
            // Sized as if the page were not the last, which is the larger envelope.
            var overhead = Utf8.GetByteCount(Envelope(snapshotId, kind, sinceSeq, groups.Count + 1, "", last: false));
            if (current.Count > 0 && (current.Count >= limits.MaxRecordsPerPage ||
                                      overhead + currentBytes + 1 + bytes > limits.MaxPageBytes))
            {
                groups.Add(current);
                current = [];
                currentBytes = 0;
            }

            currentBytes += (current.Count > 0 ? 1 : 0) + bytes;
            current.Add(item);
        }

        groups.Add(current);

        var pages = new List<byte[]>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var last = i == groups.Count - 1;
            pages.Add(Utf8.GetBytes(Envelope(snapshotId, kind, sinceSeq, i + 1, string.Join(',', groups[i]), last)));
        }

        return pages;
    }

    /// <summary>A page's canonical JSON around already canonical items (members in canonical order).</summary>
    private static string Envelope(string snapshotId, string kind, long sinceSeq, int nextPage, string items, bool last)
    {
        var sb = new StringBuilder();
        sb.Append("{\"complete\":").Append(last ? "true" : "false");
        sb.Append(",\"kind\":").Append(CanonicalJson.Serialize(JsonValue.Create(kind)));
        if (!last) sb.Append(",\"nextCursor\":").Append(CanonicalJson.Serialize(JsonValue.Create(CursorOf(nextPage))));
        sb.Append(",\"records\":[").Append(items).Append(']');
        sb.Append(",\"sinceSeq\":").Append(sinceSeq.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"snapshotId\":").Append(CanonicalJson.Serialize(JsonValue.Create(snapshotId)));
        return sb.Append('}').ToString();
    }

    /// <summary>A snapshot id or cursor: 1..128 ASCII letters, digits, '-' or '_' (§7.5.3).</summary>
    internal static bool IsIdentifier(string? value) => DataSyncWireFormat.IsNodeId(value);
}

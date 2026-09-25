using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Wire;

/// <summary>
/// Receiver side of one kind of one snapshot (§7.5.4): collects its pages in order, reassembles chunked records,
/// then validates each entity through its codec (v3.1 §6.3 per-entity rules). Never throws on peer input.
/// </summary>
/// <remarks>
/// <para>
/// Two outcomes. A problem with the snapshot as a whole sets <see cref="Problem"/>, and the caller discards the
/// <b>whole pull</b> (the cursor does not move; it never plans from an incomplete snapshot): a page problem, a key
/// used twice in the kind, a missing last page, pages that disagree, a chunk of no chunked record, a record above
/// the manifest's <c>MaxSeq</c>, more live entities than the kind allows (<see cref="DataSyncWireReader.TooLarge"/>),
/// or a kind content hash or record count other than the manifest's.
/// </para>
/// <para>
/// A problem with one entity holds that entity: a missing chunk, a hash that does not match the reassembled
/// content, a newer schema, or content the codec holds. Tombstones and records held at the source take no codec.
/// </para>
/// </remarks>
public sealed class DataSyncRecordAssembler
{
    private readonly IDataSyncKindCodec _codec;
    private readonly DataSyncLimits _limits;
    private readonly List<DataSyncWireRecord> _records = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedDictionary<int, DataSyncWireChunk>> _chunks = new(StringComparer.Ordinal);
    private long? _sinceSeq;
    private long _lastSeq;
    private bool _complete;

    public DataSyncRecordAssembler(IDataSyncKindCodec codec, DataSyncLimits limits)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    /// <summary>
    /// Why the whole pull must be discarded (<see cref="DataSyncWireReader.Corrupted"/>,
    /// <see cref="DataSyncWireReader.TooLarge"/> or <see cref="DataSyncWireReader.WrongSnapshot"/>); null while the
    /// snapshot is sound. Check it after <see cref="Complete(long, bool)"/>.
    /// </summary>
    public string? Problem { get; private set; }

    /// <summary>Records (not chunks) received so far.</summary>
    public int RecordCount => _records.Count;

    /// <summary>The since the pages were served from; null before the first page.</summary>
    public long? SinceSeq => _sinceSeq;

    /// <summary>True once the page marked complete was added.</summary>
    public bool IsComplete => _complete;

    /// <summary>The kind content hash over the records received so far, in page order (§7.5.2 step 6).</summary>
    public string ContentHash => DataSyncWireFormat.KindContentHash(_records);

    /// <summary>Adds the next page, in cursor order. Does nothing once <see cref="Problem"/> is set.</summary>
    public void Add(DataSyncPageReadResult page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (Problem is not null) return;
        if (page.Problem is not null)
        {
            Problem = page.Problem;
            return;
        }

        if (page.Page is null || _complete)
        {
            Problem = DataSyncWireReader.Corrupted;
            return;
        }

        if (page.Page.Kind != _codec.Descriptor.Kind)
        {
            Problem = DataSyncWireReader.WrongSnapshot;
            return;
        }

        if (_sinceSeq is { } since && since != page.Page.SinceSeq)
        {
            Problem = DataSyncWireReader.Corrupted;
            return;
        }

        _sinceSeq = page.Page.SinceSeq;
        foreach (var record in page.Records)
        {
            if (record.Seq < _lastSeq || record.Keys.Any(k => !_keys.Add(k)))
            {
                Problem = DataSyncWireReader.Corrupted;
                return;
            }

            _lastSeq = record.Seq;
            _records.Add(record);
        }

        foreach (var chunk in page.Chunks)
        {
            if (!_chunks.TryGetValue(chunk.Of, out var byIndex)) _chunks[chunk.Of] = byIndex = new SortedDictionary<int, DataSyncWireChunk>();
            if (!byIndex.TryAdd(chunk.Index, chunk))
            {
                Problem = DataSyncWireReader.Corrupted;
                return;
            }
        }

        _complete = page.Page.Complete;
    }

    /// <summary>
    /// Completes the kind against its manifest entry: <see cref="Complete(long, bool)"/> with its <c>MaxSeq</c>,
    /// then the served since, the record count and the kind content hash must be the manifest's.
    /// </summary>
    public DataSyncStagedKind Complete(DataSyncFeedKind manifestKind, bool fullReconciliation)
    {
        ArgumentNullException.ThrowIfNull(manifestKind);
        if (Problem is null && manifestKind.Kind != _codec.Descriptor.Kind) Problem = DataSyncWireReader.WrongSnapshot;
        if (Problem is null && (_sinceSeq != manifestKind.SinceSeq || _records.Count != manifestKind.RecordCount ||
                                ContentHash != manifestKind.ContentHash)) Problem = DataSyncWireReader.Corrupted;
        return Complete(manifestKind.MaxSeq, fullReconciliation);
    }

    /// <summary>
    /// The staged kind. Entities with missing chunks, a hash mismatch or content the codec refuses are held; a
    /// problem with the snapshot as a whole sets <see cref="Problem"/> and returns no entities.
    /// </summary>
    public DataSyncStagedKind Complete(long maxSeq, bool fullReconciliation)
    {
        if (Problem is null && !_complete) Problem = DataSyncWireReader.Corrupted;
        if (Problem is null && _records.Any(r => r.Seq > maxSeq)) Problem = DataSyncWireReader.Corrupted;

        var chunked = _records.Where(r => r.Chunks > 0).Select(r => r.Keys[0]).ToHashSet(StringComparer.Ordinal);
        if (Problem is null && _chunks.Keys.Any(of => !chunked.Contains(of))) Problem = DataSyncWireReader.Corrupted;
        if (Problem is null && _records.Count(r => !r.Deleted) > MaxEntities(_codec.Descriptor.Kind))
            Problem = DataSyncWireReader.TooLarge;

        var descriptor = _codec.Descriptor;
        if (Problem is not null)
            return new DataSyncStagedKind(descriptor.Kind, descriptor.SchemaVersion, true, null, [], maxSeq,
                fullReconciliation);

        var entities = new List<DataSyncIncomingEntity>(_records.Count);
        for (var i = 0; i < _records.Count; i++) entities.Add(Stage(_records[i], i));
        return new DataSyncStagedKind(descriptor.Kind, descriptor.SchemaVersion, true, null, entities, maxSeq,
            fullReconciliation);
    }

    private DataSyncIncomingEntity Stage(DataSyncWireRecord record, int index)
    {
        var fallbackName = "#" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (record.Deleted) return new DataSyncIncomingEntity(record, null, null, null, fallbackName, null, []);
        if (record.HeldAtSource is not null)
            return new DataSyncIncomingEntity(record, null, null, null, fallbackName, DataSyncHeldReason.AtSource, []);

        var content = record.Content!;
        if (record.Chunks > 0)
        {
            var reassembled = Reassemble(record, content);
            if (reassembled is null) return Held(record, content, fallbackName, DataSyncHeldReason.Invalid, []);
            content = reassembled;
            record = record with { Content = content, Chunks = 0 };
        }

        if (record.Hash != Canonical.ContentHash.Of(content))
            return Held(record, content, fallbackName, DataSyncHeldReason.Invalid, []);

        var descriptor = _codec.Descriptor;
        var current = (JsonObject)content.DeepClone();
        if (record.SchemaVersion > descriptor.SchemaVersion)
            return Held(record, content, fallbackName, DataSyncHeldReason.NewerSchema, []);
        if (record.SchemaVersion < descriptor.SchemaVersion)
        {
            try
            {
                current = _codec.Upgrade(current, record.SchemaVersion);
            }
            catch (DataSyncHeldException e)
            {
                return Held(record, content, fallbackName, e.Reason, []);
            }
        }

        var read = _codec.Read(current, _limits);
        if (read.Held is { } held) return Held(record, content, fallbackName, held, read.Warnings);

        var typed = read.Content!;
        var name = _codec.NameOf(typed);
        return new DataSyncIncomingEntity(record, typed, read.Unknown, Canonical.ContentHash.Of(_codec.Write(typed)),
            string.IsNullOrEmpty(name) ? fallbackName : name, null, read.Warnings);
    }

    /// <summary>The content with its chunks put back, or null when a chunk is missing or they disagree.</summary>
    private JsonObject? Reassemble(DataSyncWireRecord record, JsonObject content)
    {
        if (record.Chunks > _limits.MaxChunksPerEntity) return null;
        if (!_chunks.TryGetValue(record.Keys[0], out var byIndex) || byIndex.Count != record.Chunks) return null;

        // Sorted by index, so exactly 0..n-1 when the first is 0 and the last is n-1.
        if (byIndex.Keys.First() != 0 || byIndex.Keys.Last() != record.Chunks - 1) return null;
        var path = byIndex.Values.First().Path;
        if (byIndex.Values.Any(c => c.Path != path) || content.ContainsKey(path)) return null;

        var items = new JsonArray();
        foreach (var chunk in byIndex.Values)
        {
            foreach (var item in chunk.Items) items.Add(item?.DeepClone());
        }

        var reassembled = (JsonObject)content.DeepClone();
        reassembled[path] = items;
        return reassembled;
    }

    private DataSyncIncomingEntity Held(DataSyncWireRecord record, JsonObject content, string fallbackName,
        DataSyncHeldReason reason, IReadOnlyList<DataSyncPlanWarning> warnings)
    {
        // Every kind's content has a top-level string name by convention; show it when it is readable.
        var name = DataSyncWireFormat.TryGetString(content, "name", out var n) && n.Length is > 0 &&
                   n.Length <= _limits.MaxNameLength && DataSyncWireFormat.IsDisplayText(n)
            ? n
            : fallbackName;
        return new DataSyncIncomingEntity(record, null, null, null, name, reason, warnings);
    }

    private int MaxEntities(string kind) => kind switch
    {
        DataSyncKindIds.CustomProperty => _limits.MaxCustomProperties,
        DataSyncKindIds.ExtensionGroup => _limits.MaxExtensionGroups,
        _ => Math.Max(_limits.MaxCustomProperties, _limits.MaxExtensionGroups),
    };
}

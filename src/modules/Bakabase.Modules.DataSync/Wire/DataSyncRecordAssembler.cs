using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;

namespace Bakabase.Modules.DataSync.Wire;

/// <summary>
/// Receiver side of one kind of one snapshot (§7.5.4): collects its pages in order, then validates each entity
/// through its codec (v3.1 §6.3 per-entity rules). Never throws on peer input.
/// </summary>
/// <remarks>
/// <para>
/// Two outcomes. A problem with the snapshot as a whole sets <see cref="Problem"/>, and the caller discards the
/// <b>whole pull</b> (the cursor does not move; it never plans from an incomplete snapshot): a page problem, a key
/// used twice in the kind, a missing last page, pages that disagree, a record above the manifest's <c>MaxSeq</c>, more live entities than the kind allows (<see cref="DataSyncWireReader.TooLarge"/>),
/// or a kind content hash or record count other than the manifest's.
/// </para>
/// <para>
/// A problem with one entity holds that entity: a hash that does not match the content, another schema, or content
/// the codec holds. Tombstones and records held at the source take no codec.
/// </para>
/// </remarks>
public sealed class DataSyncRecordAssembler
{
    private readonly IDataSyncKindCodec _codec;
    private readonly DataSyncLimits _limits;
    private readonly List<DataSyncWireRecord> _records = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
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

    /// <summary>Records received so far.</summary>
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
    /// The staged kind. Entities with a hash mismatch or content the codec refuses are held; a problem with the
    /// snapshot as a whole sets <see cref="Problem"/> and returns no entities.
    /// </summary>
    public DataSyncStagedKind Complete(long maxSeq, bool fullReconciliation)
    {
        if (Problem is null && !_complete) Problem = DataSyncWireReader.Corrupted;
        if (Problem is null && _records.Any(r => r.Seq > maxSeq)) Problem = DataSyncWireReader.Corrupted;

        if (Problem is null && _records.Count(r => !r.Deleted) > MaxEntities(_codec.Descriptor.Kind))
            Problem = DataSyncWireReader.TooLarge;

        var descriptor = _codec.Descriptor;
        if (Problem is not null)
            return new DataSyncStagedKind(descriptor.Kind, descriptor.SchemaVersion, true, null, [], maxSeq,
                fullReconciliation);

        // Validation is shared with the merger's re-staging of pending records (§8.4).
        var entities = _records.Select((r, i) => DataSyncRecordValidation.Stage(_codec, r, i, _limits)).ToList();
        return new DataSyncStagedKind(descriptor.Kind, descriptor.SchemaVersion, true, null, entities, maxSeq,
            fullReconciliation);
    }

    private int MaxEntities(string kind) => kind switch
    {
        DataSyncKindIds.CustomProperty => _limits.MaxCustomProperties,
        DataSyncKindIds.ExtensionGroup => _limits.MaxExtensionGroups,
        _ => Math.Max(_limits.MaxCustomProperties, _limits.MaxExtensionGroups),
    };
}

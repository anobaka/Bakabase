using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>One page added to a kind's assembly: whether the pull can go on, and where the next page is.</summary>
/// <param name="Ok">False: the whole pull must be discarded (the assembly's <c>Problem</c> says why).</param>
public sealed record DataSyncPageStep(bool Ok, string? NextCursor, bool Complete);

/// <summary>
/// Turns one kind's raw pages of one snapshot into a staged kind (§7.5.3, §7.5.4). The fetch cycle only moves bytes;
/// parsing and validating peer input is the wire reader's and the kind codec's, and never throws on peer input.
/// </summary>
public interface IDataSyncKindPageReader
{
    /// <summary>Whether this build can read the kind (a codec is registered). A kind it cannot read is never requested.</summary>
    bool Supports(string kind);

    IDataSyncKindPageAssembly Begin(string kind, string snapshotId, DataSyncFeedKind manifestKind,
        bool fullReconciliation);
}

public interface IDataSyncKindPageAssembly
{
    /// <summary>Why the whole pull must be discarded (<c>corrupted</c>, <c>tooLarge</c>, <c>wrongSnapshot</c>); null while sound.</summary>
    string? Problem { get; }

    DataSyncPageStep Add(ReadOnlyMemory<byte> page);

    /// <summary>
    /// The staged kind, checked against its manifest entry: every page read, the served since, the record count and
    /// the kind content hash. Null, with <see cref="Problem"/> set, when the pull must be discarded: it never plans
    /// from an incomplete snapshot (§7.5.4).
    /// </summary>
    DataSyncStagedKind? Complete();
}

/// <summary>
/// The default page reader, over <see cref="DataSyncWireReader"/> and <see cref="DataSyncRecordAssembler"/> with the
/// kind's codec from its registered adapter (<see cref="IDataSyncKind"/>).
/// </summary>
public sealed class DataSyncKindPageReader : IDataSyncKindPageReader
{
    public const string Corrupted = "corrupted";

    private readonly IReadOnlyDictionary<string, IDataSyncKindCodec> _codecs;
    private readonly DataSyncLimits _limits;

    public DataSyncKindPageReader(IEnumerable<IDataSyncKind> kinds, DataSyncLimits limits)
    {
        _codecs = kinds.Select(k => k.Codec)
            .GroupBy(c => c.Descriptor.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _limits = limits;
    }

    public bool Supports(string kind) => _codecs.ContainsKey(kind);

    public IDataSyncKindPageAssembly Begin(string kind, string snapshotId, DataSyncFeedKind manifestKind,
        bool fullReconciliation)
    {
        if (!_codecs.TryGetValue(kind, out var codec))
            throw new InvalidOperationException($"No data sync codec is registered for kind {kind}.");
        return new Assembly(codec, _limits, snapshotId, manifestKind, fullReconciliation);
    }

    /// <remarks>
    /// Pages are budgeted by count as well as by bytes (§12: everything peer-supplied is budgeted), so a source cannot
    /// keep a fetch going with pages that carry nothing. <see cref="DataSyncWireWriter"/> puts at least one item on
    /// every page but the last, so each of these discards the whole pull: a page that is not the last and carries
    /// neither a record nor a chunk, more records than the manifest counted, and more pages than those records could
    /// fill (each record and each of its chunks on a page of its own, plus the last page).
    /// </remarks>
    private sealed class Assembly(IDataSyncKindCodec codec, DataSyncLimits limits, string snapshotId,
        DataSyncFeedKind manifestKind, bool fullReconciliation) : IDataSyncKindPageAssembly
    {
        private readonly DataSyncRecordAssembler _assembler = new(codec, limits);
        private readonly List<DataSyncWireRecord> _records = [];
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private readonly long _maxPages = (long)Math.Max(0, manifestKind.RecordCount) *
            (1 + Math.Max(0, limits.MaxChunksPerEntity)) + 1;
        private long _pages;
        private bool _complete;

        public string? Problem { get; private set; }

        public DataSyncPageStep Add(ReadOnlyMemory<byte> page)
        {
            if (Problem is not null) return new DataSyncPageStep(false, null, false);
            if (_complete) return Fail(Corrupted);
            if (++_pages > _maxPages) return Fail(Corrupted);

            var read = DataSyncWireReader.ReadPage(page, snapshotId, manifestKind.Kind, limits);
            if (read.Problem is not null) return Fail(read.Problem);
            if (read.Page is null || read.Page.SinceSeq != manifestKind.SinceSeq) return Fail(Corrupted);
            if (!read.Page.Complete && read.Page.Records.Count == 0) return Fail(Corrupted);
            if (_records.Count + read.Records.Count > manifestKind.RecordCount) return Fail(Corrupted);

            foreach (var record in read.Records)
            {
                // Keys are unique within the kind, else the whole pull is corrupted (§7.5.4).
                if (record.Keys.Any(k => !_keys.Add(k))) return Fail(Corrupted);
                _records.Add(record);
            }

            _assembler.Add(read);
            _complete = read.Page.Complete;
            return new DataSyncPageStep(true, read.Page.NextCursor, _complete);
        }

        public DataSyncStagedKind? Complete()
        {
            if (Problem is not null) return null;
            if (!_complete || _records.Count != manifestKind.RecordCount ||
                KindContentHash(_records) != manifestKind.ContentHash)
            {
                Problem = Corrupted;
                return null;
            }

            return _assembler.Complete(manifestKind.MaxSeq, fullReconciliation);
        }

        private DataSyncPageStep Fail(string problem)
        {
            Problem = problem;
            return new DataSyncPageStep(false, null, false);
        }
    }

    /// <summary>
    /// The kind content hash of a snapshot (§7.5.2 step 6): <c>ContentHash</c> of the canonical JSON array of
    /// <c>[primaryKey, seq, recordHash]</c> in page order, where <c>recordHash</c> is the record's hash, or
    /// <c>"tombstone"</c> / <c>"held"</c>.
    /// </summary>
    public static string KindContentHash(IEnumerable<DataSyncWireRecord> recordsInPageOrder)
    {
        var array = new JsonArray();
        foreach (var record in recordsInPageOrder)
        {
            var recordHash = record.Deleted ? "tombstone" : record.HeldAtSource is not null ? "held" : record.Hash;
            array.Add(new JsonArray(JsonValue.Create(record.Keys[0]), JsonValue.Create(record.Seq),
                JsonValue.Create(recordHash)));
        }

        return ContentHash.Of(array);
    }
}

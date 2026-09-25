using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;

namespace Bakabase.Modules.DataSync.Wire;

/// <summary>
/// Parses one page's bytes with JsonDocument (MaxDepth 64). Never throws on peer input: every problem is a
/// page problem or a held record.
/// </summary>
public static class DataSyncWireReader
{
    public static DataSyncPageReadResult ReadPage(ReadOnlyMemory<byte> utf8, string expectedSnapshotId,
        string expectedKind, DataSyncLimits limits) => throw new NotImplementedException();
}

/// <summary>Reassembles chunked records, then validates each entity through its codec (v3.1 §6.3 per-entity rules).</summary>
public sealed class DataSyncRecordAssembler
{
    public DataSyncRecordAssembler(IDataSyncKindCodec codec, DataSyncLimits limits) =>
        throw new NotImplementedException();

    public void Add(DataSyncPageReadResult page) => throw new NotImplementedException();

    /// <summary>Holds entities with missing chunks.</summary>
    public DataSyncStagedKind Complete(long maxSeq, bool fullReconciliation) => throw new NotImplementedException();
}

/// <summary>Source side: builds a frozen snapshot's pages from published records (§7.5).</summary>
public static class DataSyncWireWriter
{
    /// <summary>Canonical JSON bytes per page, chunking large content.</summary>
    public static IReadOnlyList<byte[]> WritePages(string snapshotId, string kind, long sinceSeq,
        IReadOnlyList<DataSyncWireRecord> records, DataSyncLimits limits) => throw new NotImplementedException();
}

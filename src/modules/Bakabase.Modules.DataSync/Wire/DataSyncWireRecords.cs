using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Wire;

public static class DataSyncContract
{
    /// <summary>Bump only for a wire change older builds must not read.</summary>
    public const int Version = 1;

    /// <summary>Beta policy (Q10): peers below it are refused, no shims.</summary>
    public const int MinimumPeerVersion = 1;
}

/// <summary>One entity record. A tombstone has Deleted = true and no Content.</summary>
public sealed record DataSyncWireRecord(
    IReadOnlyList<string> Keys,                 // primary first, then aliases (v3.1 §5.3), 1..MaxKeysPerEntity
    string Origin,                              // node that first created it
    long Seq,                                   // the source's change sequence for this record
    DataSyncVersionVector Vv,
    DataSyncEditorRef? EditedBy,                // who produced this revision (actor, node, name as the source knows it)
    bool Deleted,
    int SchemaVersion,
    string? OrderKey,                           // kinds with order only
    JsonObject? Content,                        // canonical PUBLISHED content (§3.5), local order, source child ids;
                                                // "childrenLocal":true ⇒ no children and no defaultValue (§3.6)
    string? Hash,                               // ContentHash(Content) as sent
    DataSyncHeldReason? HeldAtSource,           // the source withholds this entity (too large, unreadable, publish held)
    int Chunks);                                // > 0: Content's children arrive in that many chunk records (§7.5.4)

public sealed record DataSyncWireChunk(string Of, int Index, string Path, JsonArray Items);

/// <summary>Counts only; sent only to readers holding a datasync grant (§7.5.1). Never names an entity.</summary>
public sealed record DataSyncSourceAttention(bool Headless, int OpenDecisions, int PausedLinks, bool RestorePending,
    int AwaitingReview);

public sealed record DataSyncFeedHead(string NodeId, string LibraryEpoch, string ActorId, int ContractVersion,
    int MinimumPeerContract, string AppVersion,
    long Seq,                                   // = LastSeq
    IReadOnlyList<DataSyncFeedKindHead> Kinds,
    DataSyncSourceAttention Attention,
    long? SeenCounter,                          // the highest counter of the reader's declared actor found in any of
                                                // this source's vectors (§5.6)
    DataSyncFeedCounterpart? Counterpart);      // as in the manifest, so a waiting approver needs no snapshot (§8.3)

public sealed record DataSyncFeedKindHead(string Kind, int SchemaVersion, long MaxSeq, bool CursorSuperseded,
    int ComparisonFormVersion);   // the source codec's (§3.4); a mismatch makes row A2 read drift, never a duplicate actor

public sealed record DataSyncFeedManifest(string SnapshotId, long ExpiresInMs, string NodeId, string LibraryEpoch,
    string ActorId, int ContractVersion, int MinimumPeerContract, string AppVersion,
    IReadOnlyList<DataSyncFeedKind> Kinds, DataSyncFeedCounterpart? Counterpart, DataSyncSourceAttention Attention);

public sealed record DataSyncFeedKind(string Kind, int SchemaVersion,
    long MaxSeq,                  // highest Seq of the kind at snapshot time (served or not)
    long TombstoneFloorSeq,       // THIS kind's floor: TombstoneFloorSeqsJson[kind], 0 when absent (§4.6)
    int LiveCount,                // TOTAL live published entities of the kind at snapshot time
    int TombstoneCount,           // TOTAL served tombstones of the kind at snapshot time
    string ContentHash,           // over this snapshot's records of the kind, §7.5.2
    long SinceSeq,                // the since actually served: the reader's, or 0 when superseded
    int RecordCount,              // records in this snapshot (Seq > SinceSeq)
    bool CursorSuperseded);       // true ⇔ the reader's since was below this kind's TombstoneFloorSeq or above LastSeq
                                  // (or the reader is an already-recorded reader ahead, §7.5.1); served from 0

/// <summary>The source's own link to the reading peer, told only to that peer (§8.3).</summary>
public sealed record DataSyncFeedCounterpart(string Mode /* "off"|"follow"|"twoWay" */, bool FirstContactCompleted,
    IReadOnlyList<string> Kinds);

/// <summary>One page, written and parsed as raw canonical JSON (§7.5.3), never through FederationJson (F61).</summary>
public sealed record DataSyncFeedPage(string SnapshotId, string Kind, long SinceSeq,
    IReadOnlyList<JsonObject> Records,   // records and chunks, in order
    string? NextCursor, bool Complete);

/// <param name="Problem">"corrupted" | "tooLarge" | "wrongSnapshot"; null when the page was read.</param>
public sealed record DataSyncPageReadResult(DataSyncFeedPage? Page, IReadOnlyList<DataSyncWireRecord> Records,
    IReadOnlyList<DataSyncWireChunk> Chunks, string? Problem);

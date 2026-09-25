namespace Bakabase.Modules.DataSync.Models.Db;

// §4.1: EF POCOs without attributes; BakabaseDbContext configures them (§4.2). Enums are stored as integers.

public record DataSyncEntityDbModel
{
    public int Id { get; set; }
    public string Kind { get; set; } = null!;
    public string LocalKey { get; set; } = null!;          // "12"
    public string SyncKey { get; set; } = null!;           // primary, 32 hex
    public string OriginNodeId { get; set; } = null!;
    public string? Fingerprint { get; set; }               // v3.1 §5.4
    public string LocalHash { get; set; } = null!;         // v3.1 ContentHash (was ContentHash)
    public string? RawHash { get; set; }                   // hash of the raw stored row, Refresh fast path (§6.1)
    public string SharedHash { get; set; } = null!;        // §3.4, of the validated published content
    public long Seq { get; set; }                          // feed sequence (§6.2)
    public string VvJson { get; set; } = "{}";             // DataSyncVersionVector canonical string
    public string? LastActorId { get; set; }
    public string? LastEditorNodeId { get; set; }
    public string? LastEditorName { get; set; }
    public string? OrderKey { get; set; }
    public DataSyncEntitySyncState State { get; set; } = DataSyncEntitySyncState.Synced;   // on a tombstone: the state at deletion
    public string? OverlayJson { get; set; }               // DataSyncOverlay; null = None
    public string? UnknownJson { get; set; }               // preserved unknown top-level members (§8.9)
    public bool ChildrenLocal { get; set; }                // SHARED content stored here (§3.6)
    public bool CreatedBySync { get; set; }
    public bool PublishHeld { get; set; }                  // lost-update guard (§6.5)
    public bool Unreadable { get; set; }                   // the stored row does not parse (§3.3): set by Refresh
                                                           // without a revision; served as HeldAtSource = LocalUnreadable
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }            // tombstone
    public DataSyncTombstoneKind? TombstoneKind { get; set; }
    public bool TombstoneServed { get; set; }              // false after retention (§4.6); the row is kept forever
}

public record DataSyncKeyAliasDbModel
{
    public int Id { get; set; }
    public string Kind { get; set; } = null!;
    public string AliasKey { get; set; } = null!;
    public string SyncKey { get; set; } = null!;           // the primary key (live or tombstoned row) it points to
    public DateTime CreatedAtUtc { get; set; }
}

public record DataSyncApplyLogDbModel
{
    public int Id { get; set; }
    public DataSyncHistoryKind Kind { get; set; }
    public int? LinkId { get; set; }
    public string? PeerNodeId { get; set; }
    public string? PeerName { get; set; }
    public string? TaskId { get; set; }
    public DateTime AppliedAtUtc { get; set; }
    public string SummaryJson { get; set; } = null!;       // counts by kind × outcome × action
    public string ResultJson { get; set; } = null!;        // per item: itemId, kind, name, localKey, outcome, action, detail,
                                                           // and the per-entity change list the lost-update guard reads (§6.5)
    public string PreImageJson { get; set; } = null!;      // version 2 (§8.11): child-level diffs, not whole rows
    public int PreImageBytes { get; set; }
    public int? UndoOfLogId { get; set; }
    public DateTime? UndoneAtUtc { get; set; }
    public string? UndoResultJson { get; set; }
}

public record DataSyncLinkDbModel
{
    public int Id { get; set; }
    public string PeerNodeId { get; set; } = null!;        // unique
    public string PeerName { get; set; } = null!;          // last known, display only
    public string? PeerAddress { get; set; }               // the address a request or code used, for map nodes of non-peers
    public DataSyncLinkMode Mode { get; set; }
    public DataSyncLinkMode LastMode { get; set; } = DataSyncLinkMode.TwoWay;   // what the receive arrow turns back on
    public DataSyncLinkState State { get; set; }
    public DataSyncPauseReason? PausedReason { get; set; }
    public string? PausedDetail { get; set; }
    public DataSyncLinkInitiator Initiator { get; set; }
    public string KindsJson { get; set; } = "[]";
    public string? PeerLibraryEpoch { get; set; }
    public string? PeerActorId { get; set; }
    public int? PeerContractVersion { get; set; }
    public string? PeerAppVersion { get; set; }
    public string CursorsJson { get; set; } = "{}";        // kind → source Seq fully evaluated and committed (§7.5.5)
    public string? FirstContactKindsJson { get; set; }     // kinds whose first contact completed
    public DateTime? FirstContactCompletedAtUtc { get; set; }
    public string? CounterpartJson { get; set; }           // last DataSyncFeedCounterpart seen
    public string? PeerAttentionJson { get; set; }         // last DataSyncSourceAttention seen (§7.5.1)
    public DateTime? AttentionNotifiedAtUtc { get; set; }  // at most one "decisions wait on X" notification a day (§9.4)
    public bool ReadBackDeclined { get; set; }             // the peer granted access but does not read us (§7.2.3)
    public string? PendingRequestId { get; set; }          // our outgoing datasync request, while AwaitingAccess
    public string? ReviewId { get; set; }                  // staged review, while AwaitingReview
    public DateTime? LastSyncedAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorDetail { get; set; }
    public DateTime? LastFullReconciliationAtUtc { get; set; }
    public string? OnceFlagsJson { get; set; }             // DataSyncMergeFlags consumed by the next apply of this link (§8.7)
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public record DataSyncPeerBaseDbModel
{
    public int Id { get; set; }
    public int LinkId { get; set; }
    public string Kind { get; set; } = null!;

    /// <summary>
    /// The local entity's primary key (re-pointed on retirement and rekey, §5.3), or — for Unbound and for Excluded
    /// records that never bound — the peer record's primary key (§8.4 rows E, I, M, N2). A record whose primary key
    /// already keys a base row of this link is stored as that row's pending record instead (§8.4 rows M and I).
    /// </summary>
    public string SyncKey { get; set; } = null!;

    public DataSyncBaseState State { get; set; }
    public DataSyncExclusionReason? ExclusionReason { get; set; }
    public string? ExclusionKeysJson { get; set; }         // every record key an exclusion matches (§8.4 row E)
    public string? RecordJson { get; set; }                // the peer's wire record at the last agreement (content included)
    public string? SharedHash { get; set; }                // comparison form of RecordJson, this build's codec
    public string? VvJson { get; set; }
    public string ChildMapJson { get; set; } = "{}";       // peer child id → local child id (many-to-one allowed)
    public string? PendingRecordJson { get; set; }         // the latest peer record not agreed to (§8.4), stored ONCE
    public string? PendingRecordHash { get; set; }         // its hash; items refer to it
    public long? PendingSeq { get; set; }
    public DataSyncPendingReason? PendingReason { get; set; }
    public long? PendingEvaluatedLocalSeq { get; set; }    // the entity's local Seq when the pending record was last merged
    public string? PendingFlagsJson { get; set; }          // DataSyncMergeFlags for re-merging it
    public string? PendingAppliedBaseJson { get; set; }    // DataSyncAppliedBase: what a conflicted merge applied (§8.4 K6)
    public DateTime UpdatedAtUtc { get; set; }
}

public record DataSyncInboxItemDbModel
{
    public long Id { get; set; }
    public int? LinkId { get; set; }                       // null for items that belong to no link (SuspectedLostUpdate)
    public string? PeerNodeId { get; set; }
    public string Kind { get; set; } = null!;              // "" for the link-level LargeChange item (the column is non-null)
    public string SyncKey { get; set; } = null!;           // SyncKey.LinkLevel for LargeChange
    public string? LocalKey { get; set; }
    public DataSyncInboxItemType Type { get; set; }
    public DataSyncInboxItemOrigin Origin { get; set; }
    public string SubjectPath { get; set; } = "";
    public string PayloadJson { get; set; } = null!;       // DataSyncInboxPayload (display values only)
    public string? RecordHash { get; set; }                // the pending record it was computed from (never a copy of it)
    public string? RecordVvJson { get; set; }              // that record's vector, for closure by dominance (§9.3)
    public string? LocalVvJson { get; set; }
    public string? FlagsJson { get; set; }                 // DataSyncMergeFlags the item was derived with (§9.2)
    public string Token { get; set; } = null!;             // §9.2
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? NotifiedAtUtc { get; set; }
    public int? NotificationId { get; set; }               // marked read when the item closes elsewhere (§9.4)
    public DateTime? ClosedAtUtc { get; set; }
    public DataSyncInboxClosure? Closure { get; set; }
    public DataSyncInboxAction? Action { get; set; }
    public string? ClosedByNodeId { get; set; }
    public string? ClosedByName { get; set; }
    public int? ApplyLogId { get; set; }
}

/// <summary>Exactly one row, Id = 1.</summary>
public record DataSyncLocalStateDbModel
{
    public int Id { get; set; }
    public string NodeId { get; set; } = null!;
    public string LibraryEpoch { get; set; } = null!;
    public int ActorGeneration { get; set; }               // ordinal only; never an input of the actor id
    public string ActorSalt { get; set; } = null!;         // 16 random hex, minted at every rotation (§5.6)
    public string ActorId { get; set; } = null!;
    public long ActorCounter { get; set; }                 // last counter issued by ActorId (§5.6)
    public string RetiredActorsJson { get; set; } = "{}";  // earlier actors of this device → recorded counter (§5.6)
    public string DbInstanceId { get; set; } = null!;      // 32 hex, minted with the row
    public long LastSeq { get; set; }                      // monotonic Seq high-water mark (§6.2)
    public string TombstoneFloorSeqsJson { get; set; } = "{}"; // kind → highest Seq of an unserved tombstone of that kind
    public string KindSchemaVersionsJson { get; set; } = "{}";
    public string ComparisonFormVersionsJson { get; set; } = "{}";   // §3.4
    public bool NewDefinitionsStayLocal { get; set; }      // §3.6
    public bool AllPaused { get; set; }
    public DataSyncPauseReason? RestoreReason { get; set; }
    public int? RestoreLinkId { get; set; }                // set when a restore is only suspected through one link (§5.6)
    public DateTime? RestoreDetectedAtUtc { get; set; }
    public string? RestoreDetail { get; set; }
    public string? RestoreEvidenceJson { get; set; }       // [{source: "watermark"|"reader"|"peer", nodeId?, name?, actorId?,
                                                           //   counter?, at, settled?}]; settled: a recorded reader has
                                                           //   since read with every cursor ≤ LastSeq (§5.6, §7.5.1)
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Peers that pull THIS device (source side).</summary>
public record DataSyncReaderDbModel
{
    public string NodeId { get; set; } = null!;            // key
    public string Name { get; set; } = null!;
    public DateTime FirstReadAtUtc { get; set; }
    public DateTime LastReadAtUtc { get; set; }            // written at most every 10 min (§7.5.6)
    public long LastSeqServed { get; set; }
    public string? Mode { get; set; }                      // "follow" | "twoWay" as the reader declared
    public string? State { get; set; }                     // the reader's own link state as it declared it (§7.5.6)
    public DateTime? NotifiedAtUtc { get; set; }           // "X started syncing with this device" (§9.4)
}

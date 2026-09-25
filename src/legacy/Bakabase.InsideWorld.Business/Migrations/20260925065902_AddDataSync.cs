using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataSyncApplyLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkId = table.Column<int>(type: "INTEGER", nullable: true),
                    PeerNodeId = table.Column<string>(type: "TEXT", nullable: true),
                    PeerName = table.Column<string>(type: "TEXT", nullable: true),
                    TaskId = table.Column<string>(type: "TEXT", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    PreImageJson = table.Column<string>(type: "TEXT", nullable: false),
                    PreImageBytes = table.Column<int>(type: "INTEGER", nullable: false),
                    UndoOfLogId = table.Column<int>(type: "INTEGER", nullable: true),
                    UndoneAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UndoResultJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSyncApplyLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSyncEntities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    LocalKey = table.Column<string>(type: "TEXT", nullable: false),
                    SyncKey = table.Column<string>(type: "TEXT", nullable: false),
                    OriginNodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    LocalHash = table.Column<string>(type: "TEXT", nullable: false),
                    RawHash = table.Column<string>(type: "TEXT", nullable: true),
                    SharedHash = table.Column<string>(type: "TEXT", nullable: false),
                    Seq = table.Column<long>(type: "INTEGER", nullable: false),
                    VvJson = table.Column<string>(type: "TEXT", nullable: false),
                    LastActorId = table.Column<string>(type: "TEXT", nullable: true),
                    LastEditorNodeId = table.Column<string>(type: "TEXT", nullable: true),
                    LastEditorName = table.Column<string>(type: "TEXT", nullable: true),
                    OrderKey = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    OverlayJson = table.Column<string>(type: "TEXT", nullable: true),
                    UnknownJson = table.Column<string>(type: "TEXT", nullable: true),
                    ChildrenLocal = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedBySync = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishHeld = table.Column<bool>(type: "INTEGER", nullable: false),
                    Unreadable = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TombstoneKind = table.Column<int>(type: "INTEGER", nullable: true),
                    TombstoneServed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSyncEntities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSyncInboxItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LinkId = table.Column<int>(type: "INTEGER", nullable: true),
                    PeerNodeId = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    SyncKey = table.Column<string>(type: "TEXT", nullable: false),
                    LocalKey = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectPath = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    RecordHash = table.Column<string>(type: "TEXT", nullable: true),
                    RecordVvJson = table.Column<string>(type: "TEXT", nullable: true),
                    LocalVvJson = table.Column<string>(type: "TEXT", nullable: true),
                    FlagsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NotifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NotificationId = table.Column<int>(type: "INTEGER", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Closure = table.Column<int>(type: "INTEGER", nullable: true),
                    Action = table.Column<int>(type: "INTEGER", nullable: true),
                    ClosedByNodeId = table.Column<string>(type: "TEXT", nullable: true),
                    ClosedByName = table.Column<string>(type: "TEXT", nullable: true),
                    ApplyLogId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSyncInboxItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSyncKeyAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    AliasKey = table.Column<string>(type: "TEXT", nullable: false),
                    SyncKey = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSyncKeyAliases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSyncLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PeerNodeId = table.Column<string>(type: "TEXT", nullable: false),
                    PeerName = table.Column<string>(type: "TEXT", nullable: false),
                    PeerAddress = table.Column<string>(type: "TEXT", nullable: true),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    LastMode = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    PausedReason = table.Column<int>(type: "INTEGER", nullable: true),
                    PausedDetail = table.Column<string>(type: "TEXT", nullable: true),
                    Initiator = table.Column<int>(type: "INTEGER", nullable: false),
                    KindsJson = table.Column<string>(type: "TEXT", nullable: false),
                    PeerLibraryEpoch = table.Column<string>(type: "TEXT", nullable: true),
                    PeerActorId = table.Column<string>(type: "TEXT", nullable: true),
                    PeerContractVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    PeerAppVersion = table.Column<string>(type: "TEXT", nullable: true),
                    CursorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    FirstContactKindsJson = table.Column<string>(type: "TEXT", nullable: true),
                    FirstContactCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CounterpartJson = table.Column<string>(type: "TEXT", nullable: true),
                    PeerAttentionJson = table.Column<string>(type: "TEXT", nullable: true),
                    AttentionNotifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReadBackDeclined = table.Column<bool>(type: "INTEGER", nullable: false),
                    PendingRequestId = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewId = table.Column<string>(type: "TEXT", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    LastErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorDetail = table.Column<string>(type: "TEXT", nullable: true),
                    LastFullReconciliationAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OnceFlagsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSyncLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSyncLocalStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    LibraryEpoch = table.Column<string>(type: "TEXT", nullable: false),
                    ActorGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorSalt = table.Column<string>(type: "TEXT", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", nullable: false),
                    ActorCounter = table.Column<long>(type: "INTEGER", nullable: false),
                    RetiredActorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    DbInstanceId = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeq = table.Column<long>(type: "INTEGER", nullable: false),
                    TombstoneFloorSeqsJson = table.Column<string>(type: "TEXT", nullable: false),
                    KindSchemaVersionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ComparisonFormVersionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    NewDefinitionsStayLocal = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllPaused = table.Column<bool>(type: "INTEGER", nullable: false),
                    RestoreReason = table.Column<int>(type: "INTEGER", nullable: true),
                    RestoreLinkId = table.Column<int>(type: "INTEGER", nullable: true),
                    RestoreDetectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestoreDetail = table.Column<string>(type: "TEXT", nullable: true),
                    RestoreEvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSyncLocalStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSyncPeerBases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LinkId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    SyncKey = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ExclusionReason = table.Column<int>(type: "INTEGER", nullable: true),
                    ExclusionKeysJson = table.Column<string>(type: "TEXT", nullable: true),
                    RecordJson = table.Column<string>(type: "TEXT", nullable: true),
                    SharedHash = table.Column<string>(type: "TEXT", nullable: true),
                    VvJson = table.Column<string>(type: "TEXT", nullable: true),
                    ChildMapJson = table.Column<string>(type: "TEXT", nullable: false),
                    PendingRecordJson = table.Column<string>(type: "TEXT", nullable: true),
                    PendingRecordHash = table.Column<string>(type: "TEXT", nullable: true),
                    PendingSeq = table.Column<long>(type: "INTEGER", nullable: true),
                    PendingReason = table.Column<int>(type: "INTEGER", nullable: true),
                    PendingEvaluatedLocalSeq = table.Column<long>(type: "INTEGER", nullable: true),
                    PendingFlagsJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSyncPeerBases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSyncReaders",
                columns: table => new
                {
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    FirstReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeqServed = table.Column<long>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: true),
                    NotifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSyncReaders", x => x.NodeId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncApplyLogs_AppliedAtUtc",
                table: "DataSyncApplyLogs",
                column: "AppliedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncApplyLogs_LinkId",
                table: "DataSyncApplyLogs",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncEntities_Kind_LocalKey",
                table: "DataSyncEntities",
                columns: new[] { "Kind", "LocalKey" },
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncEntities_Kind_Seq",
                table: "DataSyncEntities",
                columns: new[] { "Kind", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncEntities_Kind_SyncKey",
                table: "DataSyncEntities",
                columns: new[] { "Kind", "SyncKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncInboxItems_ClosedAtUtc",
                table: "DataSyncInboxItems",
                column: "ClosedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncInboxItems_Kind_SyncKey",
                table: "DataSyncInboxItems",
                columns: new[] { "Kind", "SyncKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncInboxItems_Kind_SyncKey_Type_SubjectPath",
                table: "DataSyncInboxItems",
                columns: new[] { "Kind", "SyncKey", "Type", "SubjectPath" },
                unique: true,
                filter: "\"ClosedAtUtc\" IS NULL AND \"LinkId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncInboxItems_LinkId_Kind_SyncKey_Type_SubjectPath",
                table: "DataSyncInboxItems",
                columns: new[] { "LinkId", "Kind", "SyncKey", "Type", "SubjectPath" },
                unique: true,
                filter: "\"ClosedAtUtc\" IS NULL AND \"LinkId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncKeyAliases_Kind_AliasKey",
                table: "DataSyncKeyAliases",
                columns: new[] { "Kind", "AliasKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncKeyAliases_Kind_SyncKey",
                table: "DataSyncKeyAliases",
                columns: new[] { "Kind", "SyncKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncLinks_PeerNodeId",
                table: "DataSyncLinks",
                column: "PeerNodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncPeerBases_LinkId_Kind_SyncKey",
                table: "DataSyncPeerBases",
                columns: new[] { "LinkId", "Kind", "SyncKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataSyncPeerBases_LinkId_PendingReason",
                table: "DataSyncPeerBases",
                columns: new[] { "LinkId", "PendingReason" },
                filter: "\"PendingReason\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataSyncApplyLogs");

            migrationBuilder.DropTable(
                name: "DataSyncEntities");

            migrationBuilder.DropTable(
                name: "DataSyncInboxItems");

            migrationBuilder.DropTable(
                name: "DataSyncKeyAliases");

            migrationBuilder.DropTable(
                name: "DataSyncLinks");

            migrationBuilder.DropTable(
                name: "DataSyncLocalStates");

            migrationBuilder.DropTable(
                name: "DataSyncPeerBases");

            migrationBuilder.DropTable(
                name: "DataSyncReaders");
        }
    }
}

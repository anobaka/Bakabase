using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddReusableFileArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtifactConsumers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false),
                    ConsumerKey = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerKind = table.Column<string>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactConsumers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArtifactLineage",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ParentArtifactId = table.Column<string>(type: "TEXT", nullable: false),
                    ChildArtifactId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactLineage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArtifactOperationBindings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerKind = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Slot = table.Column<string>(type: "TEXT", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactOperationBindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArtifactOperations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    IntentKey = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    InputFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    Restartable = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", nullable: true),
                    LeaseToken = table.Column<string>(type: "TEXT", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OutputArtifactId = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", nullable: false),
                    RootIsAppData = table.Column<bool>(type: "INTEGER", nullable: false),
                    QuarantineSourceRootPath = table.Column<string>(type: "TEXT", nullable: true),
                    QuarantineSourceRootIsAppData = table.Column<bool>(type: "INTEGER", nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Managed = table.Column<bool>(type: "INTEGER", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactConsumers_OperationId_ConsumerKey",
                table: "ArtifactConsumers",
                columns: new[] { "OperationId", "ConsumerKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactConsumers_OperationId_LeaseUntil",
                table: "ArtifactConsumers",
                columns: new[] { "OperationId", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactLineage_ChildArtifactId",
                table: "ArtifactLineage",
                column: "ChildArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactLineage_ParentArtifactId_ChildArtifactId",
                table: "ArtifactLineage",
                columns: new[] { "ParentArtifactId", "ChildArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactOperationBindings_OwnerKind_OwnerId_Slot",
                table: "ArtifactOperationBindings",
                columns: new[] { "OwnerKind", "OwnerId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactOperations_IntentKey_State",
                table: "ArtifactOperations",
                columns: new[] { "IntentKey", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_OperationId_State",
                table: "Artifacts",
                columns: new[] { "OperationId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtifactConsumers");

            migrationBuilder.DropTable(
                name: "ArtifactLineage");

            migrationBuilder.DropTable(
                name: "ArtifactOperationBindings");

            migrationBuilder.DropTable(
                name: "ArtifactOperations");

            migrationBuilder.DropTable(
                name: "Artifacts");
        }
    }
}

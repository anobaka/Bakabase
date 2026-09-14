using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadResultsAndWorkflowOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadResultOwners",
                columns: table => new
                {
                    DownloadTaskId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AcquisitionTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkflowRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadResultOwners", x => x.DownloadTaskId);
                });

            migrationBuilder.CreateTable(
                name: "DownloadResultProcessing",
                columns: table => new
                {
                    DownloadResultId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkflowRunId = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentsDirectory = table.Column<string>(type: "TEXT", nullable: true),
                    ContentsFilesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ContentsReadyAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    DispatchError = table.Column<string>(type: "TEXT", nullable: true),
                    FilterDidNotMatch = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadResultProcessing", x => x.DownloadResultId);
                });

            migrationBuilder.CreateTable(
                name: "DownloadResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    ThirdPartyId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    DownloadDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    FilesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WorkflowDefinitionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadResultOwners_AcquisitionTaskId",
                table: "DownloadResultOwners",
                column: "AcquisitionTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadResults_DeduplicationKey",
                table: "DownloadResults",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadResults_DownloadTaskId_SourceKey",
                table: "DownloadResults",
                columns: new[] { "DownloadTaskId", "SourceKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadResultOwners");

            migrationBuilder.DropTable(
                name: "DownloadResultProcessing");

            migrationBuilder.DropTable(
                name: "DownloadResults");
        }
    }
}

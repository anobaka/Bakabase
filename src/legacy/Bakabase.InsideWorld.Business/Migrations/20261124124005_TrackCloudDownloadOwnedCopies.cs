using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class TrackCloudDownloadOwnedCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudDownloadOwnedCopies",
                columns: table => new
                {
                    DownloadTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationKey = table.Column<string>(type: "TEXT", nullable: false),
                    SourceIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    CopyJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultId = table.Column<int>(type: "INTEGER", nullable: true),
                    ResultKey = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudDownloadOwnedCopies", x => x.DownloadTaskId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudDownloadOwnedCopies_OperationKey",
                table: "CloudDownloadOwnedCopies",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CloudDownloadOwnedCopies_State",
                table: "CloudDownloadOwnedCopies",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudDownloadOwnedCopies");
        }
    }
}

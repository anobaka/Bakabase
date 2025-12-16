using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddPathMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PathRules");

            migrationBuilder.CreateTable(
                name: "PathMarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false),
                    SyncStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathMarks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PathMarks_IsDeleted",
                table: "PathMarks",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PathMarks_Path",
                table: "PathMarks",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_PathMarks_Path_Type_Priority",
                table: "PathMarks",
                columns: new[] { "Path", "Type", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_PathMarks_SyncStatus",
                table: "PathMarks",
                column: "SyncStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PathMarks");

            migrationBuilder.CreateTable(
                name: "PathRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MarksJson = table.Column<string>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    UpdateDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PathRules_Path",
                table: "PathRules",
                column: "Path",
                unique: true);
        }
    }
}

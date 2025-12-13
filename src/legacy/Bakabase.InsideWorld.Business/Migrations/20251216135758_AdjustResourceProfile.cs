using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AdjustResourceProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PathRuleQueueItems");

            migrationBuilder.RenameColumn(
                name: "UpdateDt",
                table: "ResourceProfiles",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "CreateDt",
                table: "ResourceProfiles",
                newName: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "ResourceProfiles",
                newName: "UpdateDt");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "ResourceProfiles",
                newName: "CreateDt");

            migrationBuilder.CreateTable(
                name: "PathRuleQueueItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathRuleQueueItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PathRuleQueueItems_Status",
                table: "PathRuleQueueItems",
                column: "Status");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddComparisonResultPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComparisonResultPairs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Resource1Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Resource2Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalScore = table.Column<double>(type: "REAL", nullable: false),
                    RuleScoresJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonResultPairs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultPairs_GroupId",
                table: "ComparisonResultPairs",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultPairs_GroupId_Resource1Id_Resource2Id",
                table: "ComparisonResultPairs",
                columns: new[] { "GroupId", "Resource1Id", "Resource2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResultPairs_Resource1Id_Resource2Id",
                table: "ComparisonResultPairs",
                columns: new[] { "Resource1Id", "Resource2Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComparisonResultPairs");
        }
    }
}

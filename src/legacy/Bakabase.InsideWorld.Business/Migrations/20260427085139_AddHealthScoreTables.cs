using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddHealthScoreTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthScoreProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    MembershipFilterJson = table.Column<string>(type: "TEXT", nullable: true),
                    RulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthScoreProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceHealthScores",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<decimal>(type: "TEXT", nullable: false),
                    ProfileHash = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    EvaluatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceHealthScores", x => new { x.ResourceId, x.ProfileId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceHealthScores_ProfileId_ProfileHash",
                table: "ResourceHealthScores",
                columns: new[] { "ProfileId", "ProfileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceHealthScores_ResourceId",
                table: "ResourceHealthScores",
                column: "ResourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealthScoreProfiles");

            migrationBuilder.DropTable(
                name: "ResourceHealthScores");
        }
    }
}

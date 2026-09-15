using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceMatchSuggestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceMatchSuggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidateResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceMatchSuggestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceMatchSuggestions_ResourceId_CandidateResourceId",
                table: "ResourceMatchSuggestions",
                columns: new[] { "ResourceId", "CandidateResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceMatchSuggestions_Status",
                table: "ResourceMatchSuggestions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceMatchSuggestions");
        }
    }
}

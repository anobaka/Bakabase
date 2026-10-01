using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionMemoAndPostParserTaskTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PostParserTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CollectionMemoTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionMemoTargets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CollectionMemoRanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionMemoRanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionMemoRanges_CollectionMemoTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "CollectionMemoTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionMemoRanges_TargetId_StartAt",
                table: "CollectionMemoRanges",
                columns: new[] { "TargetId", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionMemoTargets_NormalizedName",
                table: "CollectionMemoTargets",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionMemoRanges");

            migrationBuilder.DropTable(
                name: "CollectionMemoTargets");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "PostParserTasks");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PostParserTasks");
        }
    }
}

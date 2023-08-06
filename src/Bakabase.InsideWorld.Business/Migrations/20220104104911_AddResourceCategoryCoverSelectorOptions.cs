using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class AddResourceCategoryCoverSelectorOptions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileChangeLogs");

            migrationBuilder.AddColumn<int>(
                name: "CoverSelectionOrder",
                table: "ResourceCategories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TryCompressedFilesOnCoverSelection",
                table: "ResourceCategories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverSelectionOrder",
                table: "ResourceCategories");

            migrationBuilder.DropColumn(
                name: "TryCompressedFilesOnCoverSelection",
                table: "ResourceCategories");

            migrationBuilder.CreateTable(
                name: "FileChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BatchId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ChangeDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    New = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Old = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileChangeLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileChangeLogs_BatchId",
                table: "FileChangeLogs",
                column: "BatchId");
        }
    }
}

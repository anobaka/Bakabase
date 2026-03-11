using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceSourceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Source",
                table: "ResourcesV2");

            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Source_SourceKey",
                table: "ResourcesV2");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ResourcesV2");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "ResourcesV2");

            migrationBuilder.CreateTable(
                name: "ResourceSourceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", nullable: false),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceSourceLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSourceLinks_ResourceId",
                table: "ResourceSourceLinks",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSourceLinks_ResourceId_Source_SourceKey",
                table: "ResourceSourceLinks",
                columns: new[] { "ResourceId", "Source", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSourceLinks_Source_SourceKey",
                table: "ResourceSourceLinks",
                columns: new[] { "Source", "SourceKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceSourceLinks");

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "ResourcesV2",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "ResourcesV2",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Source",
                table: "ResourcesV2",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Source_SourceKey",
                table: "ResourcesV2",
                columns: new[] { "Source", "SourceKey" });
        }
    }
}

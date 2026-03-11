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
                name: "IX_ResourceSourceLinks_Source_SourceKey",
                table: "ResourceSourceLinks",
                columns: new[] { "Source", "SourceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSourceLinks_ResourceId_Source_SourceKey",
                table: "ResourceSourceLinks",
                columns: new[] { "ResourceId", "Source", "SourceKey" },
                unique: true);

            // Migrate existing data: copy Source+SourceKey from ResourcesV2 to ResourceSourceLinks
            migrationBuilder.Sql(@"
                INSERT INTO ResourceSourceLinks (ResourceId, Source, SourceKey, CreateDt)
                SELECT Id, Source, SourceKey, CreateDt
                FROM ResourcesV2
                WHERE SourceKey IS NOT NULL AND SourceKey != ''
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceSourceLinks");
        }
    }
}

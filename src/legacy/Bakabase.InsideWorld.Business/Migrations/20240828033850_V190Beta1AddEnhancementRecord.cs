using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V190Beta1AddEnhancementRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Enhancements");

            migrationBuilder.CreateTable(
                name: "EnhancementRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnhancerId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnhancedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnhancementRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Path",
                table: "ResourcesV2",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnhancementRecords_EnhancerId",
                table: "EnhancementRecords",
                column: "EnhancerId");

            migrationBuilder.CreateIndex(
                name: "IX_EnhancementRecords_EnhancerId_ResourceId",
                table: "EnhancementRecords",
                columns: new[] { "EnhancerId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnhancementRecords_ResourceId",
                table: "EnhancementRecords",
                column: "ResourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnhancementRecords");

            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Path",
                table: "ResourcesV2");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Enhancements",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}

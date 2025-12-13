using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddPathMarkEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropertyMarkEffects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MarkId = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyPool = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyMarkEffects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceMarkEffects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MarkId = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceMarkEffects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyMarkEffects_MarkId",
                table: "PropertyMarkEffects",
                column: "MarkId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyMarkEffects_MarkId_PropertyPool_PropertyId_ResourceId",
                table: "PropertyMarkEffects",
                columns: new[] { "MarkId", "PropertyPool", "PropertyId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyMarkEffects_PropertyPool_PropertyId_ResourceId",
                table: "PropertyMarkEffects",
                columns: new[] { "PropertyPool", "PropertyId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceMarkEffects_MarkId",
                table: "ResourceMarkEffects",
                column: "MarkId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceMarkEffects_MarkId_Path",
                table: "ResourceMarkEffects",
                columns: new[] { "MarkId", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceMarkEffects_Path",
                table: "ResourceMarkEffects",
                column: "Path");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PropertyMarkEffects");

            migrationBuilder.DropTable(
                name: "ResourceMarkEffects");
        }
    }
}

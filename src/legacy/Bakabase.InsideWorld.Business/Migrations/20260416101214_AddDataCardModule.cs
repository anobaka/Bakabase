using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddDataCardModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataCardPropertyValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataCardPropertyValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataCardTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PropertyIds = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayTemplates = table.Column<string>(type: "TEXT", nullable: true),
                    MatchRules = table.Column<string>(type: "TEXT", nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataCardTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataCardPropertyValues_CardId",
                table: "DataCardPropertyValues",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_DataCardPropertyValues_CardId_PropertyId_Scope",
                table: "DataCardPropertyValues",
                columns: new[] { "CardId", "PropertyId", "Scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataCardPropertyValues_PropertyId",
                table: "DataCardPropertyValues",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_DataCardPropertyValues_PropertyId_Value",
                table: "DataCardPropertyValues",
                columns: new[] { "PropertyId", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_DataCards_Name",
                table: "DataCards",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_DataCards_TypeId",
                table: "DataCards",
                column: "TypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataCardPropertyValues");

            migrationBuilder.DropTable(
                name: "DataCards");

            migrationBuilder.DropTable(
                name: "DataCardTypes");
        }
    }
}

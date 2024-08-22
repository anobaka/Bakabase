using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class V190Beta1CustomProperty : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CategoryCustomPropertyMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryCustomPropertyMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomProperties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Options = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomProperties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomPropertyValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPropertyValues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryCustomPropertyMappings_CategoryId_PropertyId",
                table: "CategoryCustomPropertyMappings",
                columns: new[] { "CategoryId", "PropertyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomPropertyValues_PropertyId",
                table: "CustomPropertyValues",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPropertyValues_ResourceId",
                table: "CustomPropertyValues",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPropertyValues_ResourceId_PropertyId_Scope",
                table: "CustomPropertyValues",
                columns: new[] { "ResourceId", "PropertyId", "Scope" },
                unique: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceDisplayNameTemplate",
                table: "ResourceCategories",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResourceDisplayNameTemplate",
                table: "ResourceCategories");

            migrationBuilder.DropIndex(
                name: "IX_CustomPropertyValues_ResourceId_PropertyId_Scope",
                table: "CustomPropertyValues");

            migrationBuilder.DropIndex(
                name: "IX_CustomPropertyValues_ResourceId",
                table: "CustomPropertyValues");

            migrationBuilder.DropIndex(
                name: "IX_CustomPropertyValues_PropertyId",
                table: "CustomPropertyValues");

            migrationBuilder.DropIndex(
                name: "IX_CategoryCustomPropertyMappings_CategoryId_PropertyId",
                table: "CategoryCustomPropertyMappings");

            migrationBuilder.DropTable(
                name: "CustomPropertyValues");

            migrationBuilder.DropTable(
                name: "CustomProperties");

            migrationBuilder.DropTable(
                name: "CategoryCustomPropertyMappings");
        }
    }
}

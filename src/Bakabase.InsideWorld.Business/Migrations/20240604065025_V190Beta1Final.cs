using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class V190Beta1Final : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AliasesV2",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Text = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Preferred = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AliasesV2", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BuiltinPropertyValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Rating = table.Column<decimal>(type: "TEXT", nullable: true),
                    Introduction = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuiltinPropertyValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourcesV2",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    IsFile = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileCreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileModifyDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MediaLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    HasChildren = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourcesV2", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuiltinPropertyValues_ResourceId_Scope",
                table: "BuiltinPropertyValues",
                columns: new[] { "ResourceId", "Scope" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AliasesV2");

            migrationBuilder.DropTable(
                name: "BuiltinPropertyValues");

            migrationBuilder.DropTable(
                name: "ResourcesV2");
        }
    }
}

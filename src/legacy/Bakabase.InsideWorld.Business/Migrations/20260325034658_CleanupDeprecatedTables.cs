using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class CleanupDeprecatedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AliasGroups");

            migrationBuilder.DropTable(
                name: "CategoryComponents");

            migrationBuilder.DropTable(
                name: "CategoryEnhancerOptions");

            migrationBuilder.DropTable(
                name: "CustomPlayableFileSelectorOptionsList");

            migrationBuilder.DropTable(
                name: "CustomPlayerOptionsList");

            migrationBuilder.DropTable(
                name: "CustomResourceProperties");

            migrationBuilder.DropTable(
                name: "MediaLibraries");

            migrationBuilder.DropTable(
                name: "ResourceCategories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AliasGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AliasGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentKey = table.Column<string>(type: "TEXT", nullable: false),
                    ComponentType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryEnhancerOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnhancerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Options = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryEnhancerOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomPlayableFileSelectorOptionsList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExtensionsString = table.Column<string>(type: "TEXT", nullable: false),
                    MaxFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlayableFileSelectorOptionsList", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomPlayerOptionsList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommandTemplate = table.Column<string>(type: "TEXT", nullable: false),
                    Executable = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SubCustomPlayerOptionsListJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlayerOptionsList", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomResourceProperties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Index = table.Column<int>(type: "INTEGER", nullable: true),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ValueType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomResourceProperties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaLibraries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    PathConfigurationsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResourceCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    ComponentsJsonData = table.Column<string>(type: "TEXT", nullable: true),
                    CoverSelectionOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EnhancementOptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    GenerateNfo = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceDisplayNameTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    TryCompressedFilesOnCoverSelection = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraries_CategoryId",
                table: "MediaLibraries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraries_Name",
                table: "MediaLibraries",
                column: "Name");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V220AddPathRuleSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaLibraryResourceMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceRuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibraryResourceMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PathRuleQueueItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathRuleQueueItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PathRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    MarksJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SearchCriteriaJson = table.Column<string>(type: "TEXT", nullable: false),
                    NameTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    EnhancerSettingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    PlayableFileSettingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    PlayerSettingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreateDt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateDt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryResourceMappings_MediaLibraryId",
                table: "MediaLibraryResourceMappings",
                column: "MediaLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryResourceMappings_MediaLibraryId_ResourceId",
                table: "MediaLibraryResourceMappings",
                columns: new[] { "MediaLibraryId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryResourceMappings_ResourceId",
                table: "MediaLibraryResourceMappings",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PathRuleQueueItems_Status",
                table: "PathRuleQueueItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PathRules_Path",
                table: "PathRules",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceProfiles_Priority",
                table: "ResourceProfiles",
                column: "Priority");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaLibraryResourceMappings");

            migrationBuilder.DropTable(
                name: "PathRuleQueueItems");

            migrationBuilder.DropTable(
                name: "PathRules");

            migrationBuilder.DropTable(
                name: "ResourceProfiles");
        }
    }
}

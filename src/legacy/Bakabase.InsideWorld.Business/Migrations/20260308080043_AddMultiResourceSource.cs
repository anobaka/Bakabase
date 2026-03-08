using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiResourceSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Path",
                table: "ResourcesV2",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

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

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "ResourcesV2",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DLsiteWorks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Circle = table.Column<string>(type: "TEXT", nullable: true),
                    WorkType = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DrmKey = table.Column<string>(type: "TEXT", nullable: true),
                    IsPurchased = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDownloaded = table.Column<bool>(type: "INTEGER", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DLsiteWorks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExHentaiGalleries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GalleryId = table.Column<long>(type: "INTEGER", nullable: false),
                    GalleryToken = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    TitleJpn = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDownloaded = table.Column<bool>(type: "INTEGER", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExHentaiGalleries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SteamApps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    PlaytimeForever = table.Column<int>(type: "INTEGER", nullable: false),
                    RtimeLastPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    ImgIconUrl = table.Column<string>(type: "TEXT", nullable: true),
                    HasCommunityVisibleStats = table.Column<bool>(type: "INTEGER", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsInstalled = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstallPath = table.Column<string>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SteamApps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Source",
                table: "ResourcesV2",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Source_SourceKey",
                table: "ResourcesV2",
                columns: new[] { "Source", "SourceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourcesV2_Status",
                table: "ResourcesV2",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DLsiteWorks_ResourceId",
                table: "DLsiteWorks",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DLsiteWorks_WorkId",
                table: "DLsiteWorks",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExHentaiGalleries_GalleryId_GalleryToken",
                table: "ExHentaiGalleries",
                columns: new[] { "GalleryId", "GalleryToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExHentaiGalleries_ResourceId",
                table: "ExHentaiGalleries",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_SteamApps_AppId",
                table: "SteamApps",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SteamApps_ResourceId",
                table: "SteamApps",
                column: "ResourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DLsiteWorks");

            migrationBuilder.DropTable(
                name: "ExHentaiGalleries");

            migrationBuilder.DropTable(
                name: "SteamApps");

            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Source",
                table: "ResourcesV2");

            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Source_SourceKey",
                table: "ResourcesV2");

            migrationBuilder.DropIndex(
                name: "IX_ResourcesV2_Status",
                table: "ResourcesV2");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ResourcesV2");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "ResourcesV2");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ResourcesV2");

            migrationBuilder.AlterColumn<string>(
                name: "Path",
                table: "ResourcesV2",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}

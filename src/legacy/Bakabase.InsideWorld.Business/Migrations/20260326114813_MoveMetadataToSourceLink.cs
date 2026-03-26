using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class MoveMetadataToSourceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetadataFetchedAt",
                table: "SteamApps");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "SteamApps");

            migrationBuilder.DropColumn(
                name: "MetadataFetchedAt",
                table: "ExHentaiGalleries");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "ExHentaiGalleries");

            migrationBuilder.DropColumn(
                name: "MetadataFetchedAt",
                table: "DLsiteWorks");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "DLsiteWorks");

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataFetchedAt",
                table: "ResourceSourceLinks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "ResourceSourceLinks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetadataFetchedAt",
                table: "ResourceSourceLinks");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "ResourceSourceLinks");

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataFetchedAt",
                table: "SteamApps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "SteamApps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataFetchedAt",
                table: "ExHentaiGalleries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "ExHentaiGalleries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataFetchedAt",
                table: "DLsiteWorks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "DLsiteWorks",
                type: "TEXT",
                nullable: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bakabase.InsideWorld.Business.Migrations
{
    public partial class OptimizeSynchronization : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemProperties");

            migrationBuilder.DropColumn(
                name: "Cover",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "RawCover",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "StartFiles",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "SyncDt",
                table: "MediaLibraries");

            migrationBuilder.DropColumn(
                name: "SyncInterval",
                table: "MediaLibraries");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cover",
                table: "Resources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawCover",
                table: "Resources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartFiles",
                table: "Resources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncDt",
                table: "MediaLibraries",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SyncInterval",
                table: "MediaLibraries",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.CreateTable(
                name: "SystemProperties",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemProperties", x => x.Key);
                });
        }
    }
}

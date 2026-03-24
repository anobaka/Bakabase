using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamAppAndExHentaiGalleryAccountAndIsHidden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Account",
                table: "SteamApps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "SteamApps",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Account",
                table: "ExHentaiGalleries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "ExHentaiGalleries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Account",
                table: "SteamApps");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "SteamApps");

            migrationBuilder.DropColumn(
                name: "Account",
                table: "ExHentaiGalleries");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "ExHentaiGalleries");
        }
    }
}

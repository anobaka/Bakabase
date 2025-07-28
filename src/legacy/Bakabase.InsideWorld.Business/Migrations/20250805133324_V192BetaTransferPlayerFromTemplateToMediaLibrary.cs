using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V192BetaTransferPlayerFromTemplateToMediaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Players",
                table: "MediaLibraryTemplates");

            migrationBuilder.AddColumn<string>(
                name: "Players",
                table: "MediaLibrariesV2",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Players",
                table: "MediaLibrariesV2");

            migrationBuilder.AddColumn<string>(
                name: "Players",
                table: "MediaLibraryTemplates",
                type: "TEXT",
                nullable: true);
        }
    }
}

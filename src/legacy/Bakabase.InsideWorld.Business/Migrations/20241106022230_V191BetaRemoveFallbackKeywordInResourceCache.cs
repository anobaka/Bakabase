using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.InsideWorld.Business.Migrations
{
    /// <inheritdoc />
    public partial class V191BetaRemoveFallbackKeywordInResourceCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FallbackPlayableFilePaths",
                table: "ResourceCaches",
                newName: "PlayableFilePaths");

            migrationBuilder.RenameColumn(
                name: "FallbackCoverPaths",
                table: "ResourceCaches",
                newName: "CoverPaths");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlayableFilePaths",
                table: "ResourceCaches",
                newName: "FallbackPlayableFilePaths");

            migrationBuilder.RenameColumn(
                name: "CoverPaths",
                table: "ResourceCaches",
                newName: "FallbackCoverPaths");
        }
    }
}
